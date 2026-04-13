using AgentFlow.Abstractions;
using AgentFlow.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace AgentFlow.Core.Engine;

/// <summary>
/// Microsoft Agent Framework style brain over chat+orchestration APIs.
/// Uses Kernel chat services and structured orchestration decisions.
/// </summary>
public sealed class MafBrain : IAgentBrain
{
    private readonly ILogger<MafBrain> _logger;
    private readonly IChatCompletionService _chatCompletion;
    private readonly Kernel _kernel;
    private readonly bool _enabled;

    public MafBrain(Kernel kernel, IConfiguration configuration, ILogger<MafBrain> logger)
    {
        _kernel = kernel;
        _logger = logger;
        _chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        _enabled = configuration.GetValue<bool>("Brains:MAF:Enabled", false);
    }

    public async Task<ThinkResult> ThinkAsync(ThinkContext context, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return new ThinkResult
            {
                Decision = ThinkDecision.Checkpoint,
                Rationale = "MAF brain is disabled. Enable Brains:MAF:Enabled and configure MAF orchestration settings.",
                TokensUsed = 0
            };
        }

        using var span = AgentFlowTelemetry.BrainSource.StartActivity("MafThink", ActivityKind.Internal);
        span?.SetTag("agentflow.execution_id", context.ExecutionId);
        span?.SetTag("agentflow.iteration", context.Iteration);

        var history = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt(context));

        if (context.ThreadSnapshot is not null)
        {
            foreach (var turn in context.ThreadSnapshot.RecentTurns)
            {
                history.AddUserMessage(turn.UserMessage);
                if (!string.IsNullOrWhiteSpace(turn.AssistantResponse))
                    history.AddAssistantMessage(turn.AssistantResponse);
            }
        }

        history.AddUserMessage(BuildOrchestrationPrompt(context));

        var started = Stopwatch.GetTimestamp();
        var response = await _chatCompletion.GetChatMessageContentAsync(
            history,
            new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object> { ["temperature"] = 0.1 }
            },
            _kernel,
            ct);

        var latency = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        AgentFlowTelemetry.LlmLatency.Record(latency, new TagList { { "brain", "maf" } });
        span?.SetTag("agentflow.brain.latency_ms", latency);

        return ParseThinkResult(response.Content ?? "{}", response.Metadata);
    }

    public async Task<ObserveResult> ObserveAsync(ObserveContext context, CancellationToken ct = default)
    {
        var history = new ChatHistory();
        history.AddSystemMessage("You are a MAF observation module. Respond only as valid JSON.");
        history.AddUserMessage($$"""
            Tool: {{context.ToolName}}
            Success: {{context.ToolSucceeded}}
            Goal: {{context.UserGoal}}
            Output: {{context.ToolOutputJson}}

            Return JSON: {"summary":"...","goalAchieved":true|false}
            """);

        var response = await _chatCompletion.GetChatMessageContentAsync(history, kernel: _kernel, cancellationToken: ct);
        return ParseObserveResult(response.Content ?? "{}");
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
            """;
    }

    private static string BuildOrchestrationPrompt(ThinkContext context)
    {
        return $$"""
            ExecutionId: {{context.ExecutionId}}
            Iteration: {{context.Iteration}}
            User request: {{context.UserMessage}}
            Working memory: {{context.WorkingMemoryJson}}
            """;
    }

    private static ThinkResult ParseThinkResult(string json, IReadOnlyDictionary<string, object?>? metadata)
    {
        try
        {
            var clean = json.Trim().Trim('`');
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
                TokensUsed = TryReadTokens(metadata)
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
                TokensUsed = TryReadTokens(metadata)
            };
        }
    }

    private static ObserveResult ParseObserveResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
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

    private static int TryReadTokens(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null)
            return 0;

        if (metadata.TryGetValue("Usage", out var usage) && usage is not null)
        {
            var text = usage.ToString() ?? string.Empty;
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var tokens))
                return tokens;
        }

        return 0;
    }
}
