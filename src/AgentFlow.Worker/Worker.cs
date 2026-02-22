using AgentFlow.Abstractions;
using AgentFlow.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Worker;

// =========================================================================
// AGENT EVENT WORKER — Processes agent trigger events from any source
// =========================================================================

/// <summary>
/// Background worker that consumes events from IAgentEventSource and
/// dispatches them to the appropriate IAgentExecutor.
///
/// Deployment: AgentFlow.Worker runs as a separate process (Worker Service).
/// Multiple instances can run in parallel (scale-out).
/// Each execution acquires a distributed lock before starting.
/// </summary>
public sealed class AgentEventWorker : BackgroundService
{
    private readonly IAgentEventSource _eventSource;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentEventWorker> _logger;

    public AgentEventWorker(
        IAgentEventSource eventSource,
        IServiceProvider services,
        ILogger<AgentEventWorker> logger)
    {
        _eventSource = eventSource;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AgentEventWorker started. Source: {SourceType}", _eventSource.SourceType);

        await foreach (var @event in _eventSource.StreamAsync(stoppingToken))
        {
            // Each event is processed in a scoped context (one scope per execution)
            _ = ProcessEventAsync(@event, stoppingToken);
        }

        _logger.LogInformation("AgentEventWorker stopped.");
    }

    private async Task ProcessEventAsync(AgentEvent @event, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
        var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLockService>();

        var lockKey = $"agent-event:{@event.TenantId}:{@event.AgentKey}:{@event.EventId}";

        await using var @lock = await lockService.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(5), ct);

        if (@lock is null)
        {
            _logger.LogWarning(
                "Could not acquire lock for event '{EventId}'. Duplicate? Skipping.",
                @event.EventId);
            return;
        }

        _logger.LogInformation(
            "Processing event '{EventType}' for agent '{AgentKey}' (tenant={TenantId}, correlation={CorrelationId})",
            @event.EventType, @event.AgentKey, @event.TenantId, @event.CorrelationId);

        try
        {
            var request = new AgentExecutionRequest
            {
                TenantId = @event.TenantId,
                AgentKey = @event.AgentKey,
                UserId = @event.Headers.GetValueOrDefault("userId", "system"),
                UserMessage = @event.Payload,
                SessionId = @event.SessionId,
                CorrelationId = @event.CorrelationId
            };

            var result = await executor.ExecuteAsync(request, ct);

            _logger.LogInformation(
                "Event '{EventId}' processed: execution={ExecutionId}, status={Status}, steps={Steps}",
                @event.EventId, result.ExecutionId, result.Status, result.TotalSteps);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Event processing cancelled for '{EventId}'", @event.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled error processing event '{EventId}' for agent '{AgentKey}'",
                @event.EventId, @event.AgentKey);
            // Do NOT rethrow — worker keeps running for other events
        }
    }
}
