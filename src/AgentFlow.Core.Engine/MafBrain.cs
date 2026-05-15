using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using AgentFlow.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Microsoft Agent Framework implementation of IAgentBrain.
/// Model and credentials are resolved from Model Routing, not application settings.
/// </summary>
public sealed class MafBrain : IAgentBrain
{
    private readonly IModelRegistry _modelRegistry;
    private readonly IModelCredentialResolver _credentialResolver;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<MafBrain> _logger;

    public MafBrain(
        IModelRegistry modelRegistry,
        IModelCredentialResolver credentialResolver,
        IToolExecutor toolExecutor,
        ILogger<MafBrain> logger)
    {
        _modelRegistry = modelRegistry;
        _credentialResolver = credentialResolver;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    public async Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default)
    {
        using var span = AgentFlowTelemetry.BrainSource.StartActivity("MafThink", ActivityKind.Internal);
        span?.SetTag("agentflow.execution_id", context.ExecutionId);
        span?.SetTag("agentflow.iteration", context.Iteration);
        span?.SetTag("agentflow.model_id", context.ModelId);

        var agent = await CreateAgentAsync(
            context.TenantId,
            context.ModelId,
            BuildSystemPrompt(context),
            $"agentflow-{context.ExecutionId}",
            BuildMafTools(context),
            ct);

        var started = Stopwatch.GetTimestamp();
        var response = await agent.RunAsync(BuildOrchestrationPrompt(context), cancellationToken: ct);
        var latency = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        AgentFlowTelemetry.LlmLatency.Record(latency, new TagList { { "brain", "maf" } });
        span?.SetTag("agentflow.brain.latency_ms", latency);

        var responseText = response.ToString() ?? "{}";
        _logger.LogDebug("MAF think response for execution {ExecutionId}: {Response}", context.ExecutionId, responseText);

        return ParseThinkResult(responseText);
    }

    public async Task<ObserveResult> ObserveAsync(ObserveContext context, CancellationToken ct = default)
    {
        var agent = await CreateAgentAsync(
            context.TenantId,
            context.ModelId,
            "You are an AI agent observation module. Output only valid JSON.",
            "agentflow-observer",
            tools: null,
            ct);

        var response = await agent.RunAsync($$"""
            Tool: {{context.ToolName}}
            Success: {{context.ToolSucceeded}}
            Goal: {{context.UserGoal}}
            Output: {{context.ToolOutputJson}}

            Return JSON: {"summary":"...","goalAchieved":true|false}
            """, cancellationToken: ct);

        return ParseObserveResult(response.ToString() ?? "{}");
    }

    private async Task<AIAgent> CreateAgentAsync(
        string tenantId,
        string? modelId,
        string instructions,
        string name,
        IList<AITool>? tools,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("Agent modelId is not configured. Select a model from Model Routing before running the agent.");

        var provider = _modelRegistry.GetProvider(modelId)
            ?? throw new InvalidOperationException($"Model '{modelId}' is not registered in Model Routing.");

        if (!string.Equals(provider.ProviderId, "OpenAI", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Provider '{provider.ProviderId}' is not supported by MafBrain yet.");

        var credentials = await _credentialResolver.ResolveAsync(tenantId, modelId, ct);
        if (string.IsNullOrWhiteSpace(credentials?.ApiKey))
            throw new InvalidOperationException($"Model '{modelId}' has no linked API key profile for tenant '{tenantId}'.");

        var client = new OpenAIClient(credentials.ApiKey);
        return client.GetChatClient(modelId).AsAIAgent(
            instructions: instructions,
            name: name,
            tools: tools);
    }

    private IList<AITool>? BuildMafTools(ThinkContext context)
    {
        if (context.AvailableTools.Count == 0)
            return null;

        var tools = new List<AITool>(context.AvailableTools.Count);
        foreach (var tool in context.AvailableTools)
        {
            var description = string.IsNullOrWhiteSpace(tool.InputSchemaJson) || tool.InputSchemaJson == "{}"
                ? tool.Description
                : $"{tool.Description}. Input must be a JSON string matching this schema: {tool.InputSchemaJson}";

            Func<string, CancellationToken, Task<string>> invoke = (inputJson, cancellationToken)
                => ExecuteMafToolAsync(context, tool, inputJson, cancellationToken);

            tools.Add(AIFunctionFactory.Create(invoke, new AIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = description
            }));
        }

        return tools;
    }

    private async Task<string> ExecuteMafToolAsync(
        ThinkContext context,
        AvailableToolDescriptor tool,
        string inputJson,
        CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["agentflow.invoked_by"] = "MicrosoftAgentFramework"
        };

        var result = await _toolExecutor.ExecuteToolAsync(new ToolInvocationRequest
        {
            TenantId = context.TenantId,
            UserId = string.IsNullOrWhiteSpace(context.UserId) ? "maf-agent" : context.UserId,
            ExecutionId = context.ExecutionId,
            StepId = Guid.NewGuid().ToString("N"),
            ToolId = string.IsNullOrWhiteSpace(tool.ToolId) ? tool.Name : tool.ToolId,
            ToolName = tool.Name,
            InputJson = NormalizeToolInput(inputJson),
            CorrelationId = context.CorrelationId ?? context.ExecutionId,
            Metadata = metadata
        }, ct);

        return JsonSerializer.Serialize(new
        {
            isSuccess = result.IsSuccess,
            outputJson = result.OutputJson,
            errorCode = result.ErrorCode,
            errorMessage = result.ErrorMessage,
            durationMs = result.DurationMs
        });
    }

    private static string BuildSystemPrompt(ThinkContext context)
    {
        var tools = context.AvailableTools.Any()
            ? string.Join("\n", context.AvailableTools.Select(t => $"- {t.Name}: {t.Description}"))
            : "- none";

        return $$"""
            {{context.SystemPrompt}}

            You are running in Microsoft Agent Framework orchestration mode.
            Decide between using a tool, giving a final answer, asking for more context, or checkpointing.
            Output MUST be strict JSON with fields:
            {
              "decision": "UseTool|ProvideFinalAnswer|RequestMoreContext|Checkpoint",
              "rationale": "string",
              "nextToolName": "string|null",
              "nextToolInputJson": "string|null",
              "finalAnswer": "string|null"
            }

            Available tools:
            {{tools}}

            If you need current external data, call the matching tool through Microsoft Agent Framework.
            Tool input must be a JSON string. After a tool call, still return the strict JSON decision contract.
            """;
    }

    private static string NormalizeToolInput(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
            return "{}";

        var trimmed = inputJson.Trim();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)
            ? trimmed
            : JsonSerializer.Serialize(trimmed);
    }

    private static string BuildOrchestrationPrompt(ThinkContext context)
    {
        var recentThread = context.ThreadSnapshot?.RecentTurns.Count > 0
            ? string.Join("\n", context.ThreadSnapshot.RecentTurns.Select(t =>
                $"User: {t.UserMessage}\nAssistant: {t.AssistantResponse ?? string.Empty}"))
            : "No prior conversation turns.";

        return $$"""
            ExecutionId: {{context.ExecutionId}}
            Iteration: {{context.Iteration}}
            User request: {{context.UserMessage}}
            Working memory: {{context.WorkingMemoryJson}}
            Recent conversation:
            {{recentThread}}
            """;
    }

    private static ThinkResult ParseThinkResult(string json)
    {
        try
        {
            var clean = CleanJson(json);
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;

            var decisionRaw = root.TryGetProperty("decision", out var d) ? d.GetString() : null;
            var decision = Enum.TryParse<ThinkDecision>(decisionRaw, ignoreCase: true, out var parsed)
                ? parsed
                : ThinkDecision.Checkpoint;

            var parsedResult = new ThinkResult
            {
                Decision = decision,
                Rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() : null,
                NextToolName = root.TryGetProperty("nextToolName", out var tn) && tn.ValueKind != JsonValueKind.Null ? tn.GetString() : null,
                NextToolInputJson = root.TryGetProperty("nextToolInputJson", out var ti) && ti.ValueKind != JsonValueKind.Null ? ti.GetString() : null,
                FinalAnswer = root.TryGetProperty("finalAnswer", out var fa) && fa.ValueKind != JsonValueKind.Null ? fa.GetString() : null,
                TokensUsed = 0
            };

            return BrainContractValidator.NormalizeThinkResult(parsedResult, "MAF");
        }
        catch (JsonException ex)
        {
            return new ThinkResult
            {
                Decision = ThinkDecision.Checkpoint,
                Rationale = BrainContractValidator.SerializeContractErrors(
                    "MAF",
                    "ThinkResult",
                    [$"Malformed JSON: {ex.Message}"]),
                TokensUsed = 0
            };
        }
    }

    private static ObserveResult ParseObserveResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(CleanJson(json));
            var root = doc.RootElement;
            var parsedResult = new ObserveResult
            {
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                GoalAchieved = root.TryGetProperty("goalAchieved", out var g) && g.GetBoolean()
            };

            return BrainContractValidator.NormalizeObserveResult(parsedResult, "MAF");
        }
        catch
        {
            return BrainContractValidator.NormalizeObserveResult(
                new ObserveResult { Summary = json, GoalAchieved = false },
                "MAF");
        }
    }

    private static string CleanJson(string text)
    {
        var clean = text.Trim();
        if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && clean.EndsWith("```", StringComparison.Ordinal))
            return clean[7..^3].Trim();

        if (clean.StartsWith("```", StringComparison.Ordinal) && clean.EndsWith("```", StringComparison.Ordinal))
            return clean[3..^3].Trim();

        return clean;
    }
}
