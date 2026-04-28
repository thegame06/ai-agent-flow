using AgentFlow.Abstractions;
using AgentFlow.Evaluation;
using AgentFlow.Observability;
using AgentFlow.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace AgentFlow.Evaluation;

/// <summary>
/// Background worker that listens for 'execution.completed' events 
/// and automatically triggers the Evaluation Engine.
/// 
/// Design:
/// - Reactive evaluation (don't block the agent loop)
/// - Persistence of results in the Evaluation Store
/// - Integration with human review triggers
/// </summary>
public sealed class EvaluationBackgroundWorker : BackgroundService
{
    private readonly IAgentEventTransport _eventTransport;
    private readonly IServiceProvider _services;
    private readonly ILogger<EvaluationBackgroundWorker> _logger;

    public EvaluationBackgroundWorker(
        IAgentEventTransport eventTransport,
        IServiceProvider services,
        ILogger<EvaluationBackgroundWorker> logger)
    {
        _eventTransport = eventTransport;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EvaluationBackgroundWorker started. Subscribing to execution.completed events.");

        // Subscribe to ALL execution.completed events across the platform
        await using var subscription = await _eventTransport.SubscribeAsync("*", async @event =>
        {
            if (@event.EventType != "execution.completed") return;

            try
            {
                await ProcessEvaluationAsync(@event, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing automatic evaluation for event {EventId}", @event.EventId);
            }
        }, stoppingToken);

        // Keep the worker alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }

        _logger.LogInformation("EvaluationBackgroundWorker stopping.");
    }

    private async Task ProcessEvaluationAsync(AgentEvent @event, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var executionRepo = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAgentEvaluator>();
        var resultStore = scope.ServiceProvider.GetRequiredService<IEvaluationResultStore>();

        var payload = JsonDocument.Parse(@event.Payload).RootElement;
        var executionId = payload.GetProperty("executionId").GetString();

        if (string.IsNullOrEmpty(executionId)) return;

        // 1. Fetch full execution details (to get steps, intent, etc.)
        var execution = await executionRepo.GetByIdAsync(executionId, @event.TenantId, ct);
        if (execution is null) return;

        _logger.LogInformation("Automatically evaluating execution {ExecutionId} for agent {AgentKey}", 
            executionId, @event.AgentKey);

        // 2. Prepare Evaluation Request
        var request = new EvaluationRequest
        {
            ExecutionId = executionId,
            TenantId = @event.TenantId,
            AgentKey = @event.AgentKey,
            AgentVersion = "1.0.0", // TODO: Get from AgentDefinition
            UserMessage = execution.Input.UserMessage,
            FinalResponse = execution.Output?.FinalResponse ?? "",
            ToolCalls = execution.Steps
                .Where(s => s.StepType == AgentFlow.Domain.Enums.StepType.Act)
                .Select(s => new ToolCallRecord
                {
                    ToolName = s.ToolName ?? "Unknown",
                    InputJson = s.InputJson ?? "{}",
                    OutputJson = s.OutputJson,
                    IsSuccess = s.IsSuccess,
                    OrderIndex = s.Iteration
                }).ToList(),
            Config = new EvaluationConfig
            {
                EnableHallucinationDetection = true,
                EnableQualityScoring = false, // Disable LLM judge by default for auto-evals unless optimized
                Mode = EvaluationMode.Observing
            }
        };

        // 3. Run Evaluation
        var result = await evaluator.EvaluateAsync(request, ct);

        // 4. Persist Result
        await resultStore.SaveAsync(result, ct);

        var executionVariant = execution.ParentExecutionId is null ? "champion" : "challenger";
        var brain = execution.Input.Variables.TryGetValue("brain", out var brainTag) && !string.IsNullOrWhiteSpace(brainTag)
            ? brainTag.ToLowerInvariant()
            : "unknown";
        AgentFlowTelemetry.EvaluationComparisons.Add(1, new TagList
        {
            { "tenant_id", @event.TenantId },
            { "agent_key", @event.AgentKey },
            { "comparison", executionVariant == "challenger" ? "champion_challenger" : "single" },
            { "variant", executionVariant },
            { "brain", brain }
        });
        AgentFlowTelemetry.EvaluationScoreDelta.Record(Math.Abs(result.QualityScore - result.PolicyComplianceScore), new TagList
        {
            { "tenant_id", @event.TenantId },
            { "agent_key", @event.AgentKey },
            { "comparison", executionVariant == "challenger" ? "champion_challenger" : "single" },
            { "brain", brain }
        });

        // 5. Trigger Shadow Execution if configured
        var agentDef = await agentRepo.GetByIdAsync(@event.AgentKey, @event.TenantId, ct);
        if (agentDef != null && !string.IsNullOrEmpty(agentDef.ShadowAgentId))
        {
            _logger.LogInformation("Triggering shadow execution of agent {ShadowAgentId} for original execution {ExecutionId}", 
                agentDef.ShadowAgentId, executionId);

            var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
            _ = executor.ExecuteAsync(new AgentExecutionRequest
            {
                TenantId = @event.TenantId,
                AgentKey = agentDef.ShadowAgentId,
                UserId = "shadow-engine",
                UserMessage = execution.Input.UserMessage,
                CorrelationId = @event.CorrelationId,
                SessionId = @event.SessionId,
                ParentExecutionId = executionId, // Link them
                Metadata = new Dictionary<string, string> { { "isShadow", "true" } }
            }, ct);
        }

        _logger.LogInformation("Auto-evaluation complete for {ExecutionId}: Quality={Quality:F2}, Hallucination={Risk}", 
            executionId, result.QualityScore, result.HallucinationRisk);
    }
}
