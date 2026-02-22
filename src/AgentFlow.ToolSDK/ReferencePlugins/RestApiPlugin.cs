using System.Net.Http.Json;
using System.Text.Json;

namespace AgentFlow.ToolSDK.ReferencePlugins;

/// <summary>
/// Reference implementation: Generic REST API plugin.
/// Demonstrates how to create an HTTP connector for AgentFlow.
/// 
/// PRODUCTION NOTE: This is a simplified example. For production use:
/// - Implement authentication strategies (OAuth, API Key, JWT)
/// - Add retry logic with exponential backoff
/// - Implement circuit breaker pattern
/// - Add request/response logging with PII masking
/// - Support for webhooks and async callbacks
/// - Rate limiting awareness
/// </summary>
public sealed class RestApiPlugin : IToolPlugin
{
    private readonly HttpClient _httpClient;

    public RestApiPlugin(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public ToolMetadata Metadata => new()
    {
        Id = "rest-api-call",
        Name = "REST API Call",
        Version = "1.0.0",
        Author = "AgentFlow Team",
        Description = "Make HTTP requests to external REST APIs (GET, POST, PUT, DELETE). Supports JSON payloads and headers.",
        Tags = new[] { "http", "rest", "api", "integration" },
        License = "MIT",
        RiskLevel = ToolRiskLevel.Medium // Can read/write external data
    };

    public ToolSchema GetSchema() => new()
    {
        Parameters = new Dictionary<string, ParameterSchema>
        {
            ["url"] = new()
            {
                Type = "string",
                Description = "Full URL to call (must be HTTPS for production)",
                Pattern = @"^https?://.*"
            },
            ["method"] = new()
            {
                Type = "string",
                Description = "HTTP method (GET, POST, PUT, DELETE)",
                EnumValues = new[] { "GET", "POST", "PUT", "DELETE" },
                DefaultValue = "GET"
            },
            ["headers"] = new()
            {
                Type = "object",
                Description = "Optional: HTTP headers (key-value pairs)",
                Properties = new Dictionary<string, ParameterSchema>()
            },
            ["body"] = new()
            {
                Type = "object",
                Description = "Optional: Request body (for POST/PUT). Will be sent as JSON."
            },
            ["timeout"] = new()
            {
                Type = "number",
                Description = "Optional: Request timeout in seconds (default: 30)",
                DefaultValue = 30,
                Minimum = 1,
                Maximum = 300
            }
        },
        Required = new[] { "url", "method" },
        Example = new
        {
            url = "https://api.example.com/v1/customers",
            method = "POST",
            headers = new { Authorization = "Bearer {{api_key}}" },
            body = new { name = "John Doe", email = "john@example.com" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        try
        {
            var url = context.Parameters["url"].ToString()!;
            var method = context.Parameters.TryGetValue("method", out var methodObj)
                ? methodObj.ToString()!.ToUpperInvariant()
                : "GET";

            var timeout = context.Parameters.TryGetValue("timeout", out var timeoutObj)
                ? Convert.ToInt32(timeoutObj)
                : 30;

            // Security: Production should enforce HTTPS
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) 
                && !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.FromError(
                    "Only HTTPS URLs are allowed in production. Use localhost for testing.",
                    "INSECURE_URL",
                    "escalate");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Add headers if provided
            if (context.Parameters.TryGetValue("headers", out var headersObj) 
                && headersObj is JsonElement headersJson)
            {
                foreach (var prop in headersJson.EnumerateObject())
                {
                    request.Headers.TryAddWithoutValidation(prop.Name, prop.Value.ToString());
                }
            }

            // Add body if provided (for POST/PUT)
            if ((method == "POST" || method == "PUT") 
                && context.Parameters.TryGetValue("body", out var bodyObj))
            {
                request.Content = JsonContent.Create(bodyObj);
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            
            // Try to parse as JSON, fallback to string
            object? parsedBody = null;
            try
            {
                parsedBody = JsonSerializer.Deserialize<object>(responseBody);
            }
            catch
            {
                parsedBody = responseBody;
            }

            return ToolResult.FromSuccess(new
            {
                statusCode = (int)response.StatusCode,
                statusText = response.ReasonPhrase,
                headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
                body = parsedBody,
                isSuccess = response.IsSuccessStatusCode
            }, new Dictionary<string, string>
            {
                ["ExecutionTimeMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                ["StatusCode"] = ((int)response.StatusCode).ToString()
            });
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.FromError(
                $"HTTP request failed: {ex.Message}",
                "HTTP_REQUEST_FAILED",
                "retry");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.FromError(
                "Request timed out",
                "TIMEOUT",
                "retry");
        }
        catch (Exception ex)
        {
            return ToolResult.FromError(
                $"Unexpected error: {ex.Message}",
                "UNEXPECTED_ERROR",
                "escalate");
        }
    }

    public Task<ToolValidationResult> ValidateAsync(ToolContext context, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (!context.Parameters.TryGetValue("url", out var urlObj) || string.IsNullOrWhiteSpace(urlObj.ToString()))
        {
            errors.Add("URL parameter is required.");
        }
        else if (!Uri.TryCreate(urlObj.ToString(), UriKind.Absolute, out _))
        {
            errors.Add("URL must be a valid absolute URI.");
        }

        if (context.Parameters.TryGetValue("method", out var methodObj))
        {
            var method = methodObj.ToString()!.ToUpperInvariant();
            if (!new[] { "GET", "POST", "PUT", "DELETE" }.Contains(method))
            {
                errors.Add("Method must be one of: GET, POST, PUT, DELETE.");
            }
        }

        return Task.FromResult(errors.Count == 0
            ? ToolValidationResult.Success()
            : ToolValidationResult.Failure(errors.ToArray()));
    }

    public PluginCapabilities Capabilities => new()
    {
        SupportsAsync = false,
        SupportsStreaming = false,
        IsCacheable = false, // External APIs may return different results
        RequiresNetwork = true,
        IsReadOnly = false, // Can make POST/PUT/DELETE requests
        EstimatedExecutionMs = 2000
    };

    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement
        {
            PolicyGroupId = "external-api-call",
            IsMandatory = true,
            Reason = "Makes calls to external APIs which may expose tenant data"
        }
    };
}
