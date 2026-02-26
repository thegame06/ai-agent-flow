using AgentFlow.ToolSDK;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AgentFlow.Extensions.Tools;

/// <summary>
/// Deterministic Credit Bureau API Plugin for Loan Officer Demo.
/// Processs calling a credit bureau to retrieve credit scores.
/// 
/// In production, this would call Experian, Equifax, or TransUnion APIs.
/// </summary>
public sealed class BureauAPIPlugin : IToolPlugin
{
    private readonly ILogger<BureauAPIPlugin> _logger;

    public BureauAPIPlugin(ILogger<BureauAPIPlugin> logger)
    {
        _logger = logger;
    }

    public ToolMetadata Metadata => new()
    {
        Id = "bureau-api",
        Name = "Credit Bureau API",
        Version = "1.0.0",
        Author = "AgentFlow Demo Team",
        Description = "Retrieve credit score and history from credit bureau (deterministic implementation)",
        Tags = new[] { "finance", "credit", "banking", "demo" },
        RiskLevel = ToolRiskLevel.Medium, // Accesses sensitive financial data
        License = "MIT"
    };

    public ToolSchema GetSchema()
    {
        return new ToolSchema
        {
            Parameters = new Dictionary<string, ParameterSchema>
            {
                ["fullName"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Full name of the person to lookup (e.g., 'John Doe')"
                },
                ["ssn"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Social Security Number (last 4 digits only for demo, e.g., '1234')",
                    Pattern = "^\\d{4}$"
                },
                ["purpose"] = new ParameterSchema
                {
                    Type = "string",
                    Description = "Purpose of credit check (e.g., 'loan application', 'credit card')",
                    DefaultValue = "loan application"
                }
            },
            Required = new[] { "fullName", "ssn" },
            Example = new
            {
                fullName = "John Doe",
                ssn = "5678",
                purpose = "loan application"
            }
        };
    }

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "BureauAPI called for tenant {TenantId}, execution {ExecutionId}",
            context.TenantId, context.ExecutionId);

        try
        {
            // Extract parameters
            if (!context.Parameters.TryGetValue("fullName", out var fullNameObj) || fullNameObj is not string fullName)
            {
                return ToolResult.FromError(
                    "Parameter 'fullName' is required and must be a string",
                    "BUREAU_MISSING_NAME");
            }

            if (!context.Parameters.TryGetValue("ssn", out var ssnObj) || ssnObj is not string ssn)
            {
                return ToolResult.FromError(
                    "Parameter 'ssn' is required and must be a string",
                    "BUREAU_MISSING_SSN");
            }

            var purpose = context.Parameters.TryGetValue("purpose", out var purposeObj) && purposeObj is string p
                ? p
                : "loan application";

            // Process API latency
            await Task.Delay(Random.Shared.Next(100, 300), ct);

            // Deterministic credit score based on name hash (deterministic for demo)
            int creditScore = DeterministicCreditScore(fullName);
            string creditHistory = creditScore switch
            {
                >= 750 => "excellent",
                >= 700 => "good",
                >= 650 => "fair",
                >= 600 => "poor",
                _ => "very poor"
            };

            int delinquencies = creditScore >= 700 ? 0 : Random.Shared.Next(1, 4);
            int totalAccounts = Random.Shared.Next(5, 15);
            double utilizationRate = creditScore >= 700 ? 0.25 : 0.65;

            var result = new
            {
                success = true,
                creditScore,
                creditHistory,
                details = new
                {
                    fullName,
                    ssnLast4 = ssn,
                    reportDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                    bureauName = "MockBureau Inc.",
                    purpose,
                    delinquencies,
                    totalAccounts,
                    utilizationRate,
                    remarks = creditScore >= 700 
                        ? "Clean credit history. Low risk."
                        : "Some payment irregularities. Moderate risk."
                }
            };

            _logger.LogInformation(
                "Credit check completed for {FullName}. Score: {Score}, History: {History}",
                fullName, creditScore, creditHistory);

            return ToolResult.FromSuccess(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BureauAPI execution failed");
            return ToolResult.FromError(
                $"Credit bureau API error: {ex.Message}",
                "BUREAU_API_ERROR");
        }
    }

    public PluginCapabilities Capabilities => new()
    {
        SupportsAsync = true,
        SupportsStreaming = false,
        IsCacheable = true, // Cache results for 24h in production
        RequiresNetwork = true,
        IsReadOnly = true // Doesn't modify external state
    };

    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement { PolicyGroupId = "pii-access", Reason = "Tool accesses personally identifiable information" },
        new PolicyRequirement { PolicyGroupId = "financial-data-access", Reason = "Tool accesses sensitive financial data" },
        new PolicyRequirement { PolicyGroupId = "credit-check-authorized", Reason = "User must be authorized to perform credit checks" }
    };

    /// <summary>
    /// Generate deterministic credit score based on name (for demo consistency).
    /// In production, this would call a real bureau API.
    /// </summary>
    private static int DeterministicCreditScore(string fullName)
    {
        // Use FNV-1a hash for deterministic score
        uint hash = 2166136261;
        foreach (char c in fullName.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }

        // Map to credit score range (550-850)
        return 550 + (int)(hash % 301);
    }
}
