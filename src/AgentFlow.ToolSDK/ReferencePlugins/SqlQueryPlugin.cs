using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AgentFlow.ToolSDK.ReferencePlugins;

/// <summary>
/// Reference implementation: SQL Server query plugin.
/// Demonstrates how to create a database connector for AgentFlow.
/// 
/// PRODUCTION NOTE: This is a simplified example. For production use:
/// - Implement connection pooling
/// - Add query timeout configuration
/// - Implement parameterized queries only (prevent SQL injection)
/// - Add retry logic with exponential backoff
/// - Integrate with tenant-specific connection string vault
/// </summary>
public sealed class SqlQueryPlugin : IToolPlugin
{
    private readonly string _connectionString;

    public SqlQueryPlugin(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public ToolMetadata Metadata => new()
    {
        Id = "sql-query",
        Name = "SQL Query Executor",
        Version = "1.0.0",
        Author = "AgentFlow Team",
        Description = "Execute read-only SQL queries against SQL Server databases. Supports parameterized queries for safety.",
        Tags = new[] { "database", "sql-server", "query" },
        License = "MIT",
        RiskLevel = ToolRiskLevel.Medium // Read-only but accesses sensitive data
    };

    public ToolSchema GetSchema() => new()
    {
        Parameters = new Dictionary<string, ParameterSchema>
        {
            ["query"] = new()
            {
                Type = "string",
                Description = "SQL query to execute. Must be a SELECT statement (read-only). Use @param syntax for parameters.",
                Pattern = @"^\s*SELECT\s+.*" // Basic validation for SELECT only
            },
            ["parameters"] = new()
            {
                Type = "object",
                Description = "Optional: Parameters for the query (key-value pairs). Prevents SQL injection.",
                Properties = new Dictionary<string, ParameterSchema>()
            },
            ["maxRows"] = new()
            {
                Type = "number",
                Description = "Maximum number of rows to return (default: 100, max: 1000)",
                DefaultValue = 100,
                Minimum = 1,
                Maximum = 1000
            }
        },
        Required = new[] { "query" },
        Example = new
        {
            query = "SELECT TOP 10 * FROM Customers WHERE Country = @country",
            parameters = new { country = "USA" },
            maxRows = 10
        }
    };

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        try
        {
            var query = context.Parameters["query"].ToString()!;
            var maxRows = context.Parameters.TryGetValue("maxRows", out var maxRowsObj)
                ? Convert.ToInt32(maxRowsObj)
                : 100;

            // Security: Enforce read-only queries
            if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResult.FromError(
                    "Only SELECT queries are allowed for security reasons.",
                    "QUERY_NOT_ALLOWED",
                    "escalate");
            }

            // Limit rows for safety
            if (maxRows > 1000) maxRows = 1000;

            var results = new List<Dictionary<string, object?>>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(query, connection)
            {
                CommandTimeout = 30 // 30 seconds timeout
            };

            // Add parameters if provided
            if (context.Parameters.TryGetValue("parameters", out var paramsObj) 
                && paramsObj is JsonElement paramsJson)
            {
                foreach (var prop in paramsJson.EnumerateObject())
                {
                    command.Parameters.AddWithValue($"@{prop.Name}", prop.Value.ToString());
                }
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            
            var rowCount = 0;
            while (await reader.ReadAsync(ct) && rowCount < maxRows)
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
                rowCount++;
            }

            return ToolResult.FromSuccess(new
            {
                rowCount = results.Count,
                rows = results,
                truncated = rowCount >= maxRows
            }, new Dictionary<string, string>
            {
                ["ExecutionTimeMs"] = "..." // Could measure actual time
            });
        }
        catch (SqlException ex)
        {
            return ToolResult.FromError(
                $"SQL error: {ex.Message}",
                $"SQL_{ex.Number}",
                ex.Number == -2 ? "retry" : "escalate"); // -2 = timeout
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

        if (!context.Parameters.TryGetValue("query", out var queryObj) || string.IsNullOrWhiteSpace(queryObj.ToString()))
        {
            errors.Add("Query parameter is required and cannot be empty.");
        }
        else
        {
            var query = queryObj.ToString()!.TrimStart();
            if (!query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Only SELECT queries are allowed. INSERT, UPDATE, DELETE are blocked for safety.");
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
        IsCacheable = true, // Same query = same results (if data doesn't change frequently)
        RequiresNetwork = true,
        IsReadOnly = true,
        EstimatedExecutionMs = 500
    };

    public IReadOnlyList<PolicyRequirement> RequiredPolicies => new[]
    {
        new PolicyRequirement
        {
            PolicyGroupId = "database-access",
            IsMandatory = true,
            Reason = "Accesses tenant database with potentially sensitive data"
        }
    };
}
