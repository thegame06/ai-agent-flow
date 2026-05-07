using System.Text.Json;
using System.Threading.Channels;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Connect;

namespace AgentFlow.Api.Workflow;

public interface IWorkflowExecutionQueue
{
    ValueTask EnqueueAsync(WorkflowQueueItem item, CancellationToken ct = default);
    ValueTask<WorkflowQueueItem> DequeueAsync(CancellationToken ct);
}

public sealed record WorkflowQueueItem(string TenantId, string ExecutionId);

public sealed class InMemoryWorkflowExecutionQueue : IWorkflowExecutionQueue
{
    private readonly Channel<WorkflowQueueItem> _channel = Channel.CreateUnbounded<WorkflowQueueItem>();

    public ValueTask EnqueueAsync(WorkflowQueueItem item, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(item, ct);

    public ValueTask<WorkflowQueueItem> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);
}

public sealed class WorkflowRuntimeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowExecutionQueue _queue;
    private readonly ILogger<WorkflowRuntimeWorker> _logger;

    public WorkflowRuntimeWorker(
        IServiceScopeFactory scopeFactory,
        IWorkflowExecutionQueue queue,
        ILogger<WorkflowRuntimeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await _queue.DequeueAsync(stoppingToken);
                await ProcessItemAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow runtime worker loop failed.");
            }
        }
    }

    private async Task ProcessItemAsync(WorkflowQueueItem item, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStudioStore>();
        var connectStore = scope.ServiceProvider.GetRequiredService<IConnectStore>();
        var audit = scope.ServiceProvider.GetRequiredService<IWorkflowAuditService>();
        var policy = scope.ServiceProvider.GetRequiredService<IWorkflowSecurityPolicyService>();

        var execution = (await store.GetExecutionsAsync(item.TenantId, 500, ct))
            .FirstOrDefault(x => x.Id == item.ExecutionId);
        if (execution is null) return;

        await store.UpdateExecutionStatusAsync(item.TenantId, item.ExecutionId, WorkflowExecutionStatus.Running, null, ct);
        await audit.RecordExecutionActionAsync(
            item.TenantId,
            "workflow-runtime",
            "workflow.execution.running",
            item.ExecutionId,
            execution.WorkflowDefinitionId,
            new { queue = "in-memory" },
            execution.CorrelationId,
            ct);

        try
        {
            var definition = await store.GetDefinitionAsync(item.TenantId, execution.WorkflowDefinitionId, ct);
            if (definition is null)
                throw new InvalidOperationException($"Workflow definition {execution.WorkflowDefinitionId} not found.");

            var runtime = JsonSerializer.Deserialize<WorkflowRuntimeDefinition>(definition.DefinitionJson) ?? new WorkflowRuntimeDefinition();
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(execution.PayloadJson) ?? new();
            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var persistedContext = JsonSerializer.Deserialize<Dictionary<string, string>>(execution.ContextJson) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in persistedContext)
                context[kv.Key] = kv.Value;
            foreach (var kv in payload)
                context[$"payload.{kv.Key}"] = kv.Value.ToString() ?? string.Empty;

            var byId = runtime.Activities.Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToDictionary(x => x.Id!, x => x, StringComparer.OrdinalIgnoreCase);
            var ordered = runtime.Activities;
            var current = ordered.FirstOrDefault();
            var guard = 0;

            while (current is not null && guard < 1000)
            {
                guard++;
                if (!ShouldExecute(payload, current.When))
                {
                    current = ResolveNext(current.Next, ordered, byId, current);
                    continue;
                }

                var resolvedConfig = ResolveConfig(current.Config, context);
                var stepId = Guid.NewGuid().ToString("N");
                await store.CreateStepLogAsync(new WorkflowExecutionStepLogContract
                {
                    Id = stepId,
                    TenantId = item.TenantId,
                    ExecutionId = execution.Id,
                    ActivityType = current.Type,
                    ActivityName = current.Name ?? current.Id ?? current.Type,
                    Status = WorkflowExecutionStatus.Running,
                    InputJson = JsonSerializer.Serialize(resolvedConfig),
                    StartedAt = DateTimeOffset.UtcNow
                }, ct);

                try
                {
                    var output = await ExecuteWithPolicyAsync(
                        item.TenantId,
                        connectStore,
                        policy,
                        current,
                        execution,
                        resolvedConfig,
                        ct);
                    await store.CompleteStepLogAsync(item.TenantId, stepId, WorkflowExecutionStatus.Completed, output, null, ct);
                    CaptureOutputs(context, current, output);
                    await store.UpdateExecutionContextAsync(item.TenantId, execution.Id, JsonSerializer.Serialize(context), ct);
                    current = ResolveNext(current.OnSuccess ?? current.Next, ordered, byId, current);
                }
                catch (Exception ex)
                {
                    await store.CompleteStepLogAsync(item.TenantId, stepId, WorkflowExecutionStatus.Failed, null, ex.Message, ct);
                    if (!string.IsNullOrWhiteSpace(current.OnFailure))
                    {
                        current = ResolveNext(current.OnFailure, ordered, byId, current);
                        continue;
                    }
                    throw;
                }
            }

            await store.UpdateExecutionStatusAsync(item.TenantId, item.ExecutionId, WorkflowExecutionStatus.Completed, null, ct);
            await audit.RecordExecutionActionAsync(
                item.TenantId,
                "workflow-runtime",
                "workflow.execution.completed",
                item.ExecutionId,
                execution.WorkflowDefinitionId,
                new { status = WorkflowExecutionStatus.Completed.ToString() },
                execution.CorrelationId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed. ExecutionId={ExecutionId}", item.ExecutionId);
            await store.UpdateExecutionStatusAsync(item.TenantId, item.ExecutionId, WorkflowExecutionStatus.Failed, ex.Message, ct);
            await audit.RecordExecutionActionAsync(
                item.TenantId,
                "workflow-runtime",
                "workflow.execution.failed",
                item.ExecutionId,
                execution.WorkflowDefinitionId,
                new { error = ex.Message },
                execution.CorrelationId,
                ct);
        }
    }

    private static async Task<string?> ExecuteActivityAsync(
        string tenantId,
        IConnectStore connectStore,
        IWorkflowSecurityPolicyService policy,
        WorkflowRuntimeActivity activity,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        CancellationToken ct)
    {
        if (!policy.IsAllowedActivityType(activity.Type))
            throw new InvalidOperationException($"Activity type '{activity.Type}' is blocked by security policy.");

        if (string.Equals(activity.Type, "connect.send_whatsapp_template", StringComparison.OrdinalIgnoreCase))
        {
            var channel = GetConfig(resolvedConfig, "channel", "whatsapp");
            var recipient = GetConfig(resolvedConfig, "recipient");
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("Activity connect.send_whatsapp_template requires config.recipient.");

            var templateId = GetConfig(resolvedConfig, "templateId");
            var content = GetConfig(resolvedConfig, "content", $"Triggered by workflow {execution.WorkflowDefinitionId}");
            var campaignId = GetConfig(resolvedConfig, "campaignId");

            var created = await connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Channel = channel,
                Recipient = recipient,
                Content = content,
                CampaignId = campaignId,
                TemplateId = templateId,
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = execution.RequestedBy
            }, ct);

            return JsonSerializer.Serialize(new { inboxMessageId = created.Id, created.Status, created.Channel });
        }

        if (string.Equals(activity.Type, "connect.update_inbox_status", StringComparison.OrdinalIgnoreCase))
        {
            var messageId = GetConfig(resolvedConfig, "messageId");
            if (string.IsNullOrWhiteSpace(messageId))
                throw new InvalidOperationException("Activity connect.update_inbox_status requires config.messageId.");

            var statusRaw = GetConfig(resolvedConfig, "status", "Sent");
            if (!Enum.TryParse<ConnectOperationalStatus>(statusRaw, true, out var status))
                throw new InvalidOperationException($"Invalid status '{statusRaw}' for connect.update_inbox_status.");

            var updated = await connectStore.UpdateMessageStatusAsync(
                tenantId,
                messageId,
                status,
                execution.RequestedBy,
                GetConfig(resolvedConfig, "lastError", null),
                ct);

            if (updated is null)
                throw new InvalidOperationException($"Inbox message {messageId} not found.");

            return JsonSerializer.Serialize(new { updated.Id, updated.Status });
        }

        if (string.Equals(activity.Type, "connect.enqueue_campaign_message", StringComparison.OrdinalIgnoreCase))
        {
            var recipient = GetConfig(resolvedConfig, "recipient");
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("Activity connect.enqueue_campaign_message requires config.recipient.");

            var created = await connectStore.CreateInboxMessageAsync(new ConnectInboxMessageContract
            {
                Id = Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                Channel = GetConfig(resolvedConfig, "channel", "whatsapp"),
                Recipient = recipient,
                Content = GetConfig(resolvedConfig, "content", "Campaign workflow message"),
                CampaignId = GetConfig(resolvedConfig, "campaignId"),
                TemplateId = GetConfig(resolvedConfig, "templateId"),
                Status = ConnectOperationalStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = execution.RequestedBy
            }, ct);

            return JsonSerializer.Serialize(new { inboxMessageId = created.Id });
        }

        throw new InvalidOperationException($"Unknown activity type '{activity.Type}'.");
    }

    private static async Task<string?> ExecuteWithPolicyAsync(
        string tenantId,
        IConnectStore connectStore,
        IWorkflowSecurityPolicyService policy,
        WorkflowRuntimeActivity activity,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        CancellationToken ct)
    {
        var attempts = Math.Max(1, activity.RetryCount + 1);
        var delayMs = Math.Max(0, activity.RetryDelayMs);
        var timeoutMs = activity.TimeoutMs <= 0 ? 30000 : activity.TimeoutMs;
        Exception? last = null;

        for (var i = 1; i <= attempts; i++)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            try
            {
                return await ExecuteActivityAsync(tenantId, connectStore, policy, activity, execution, resolvedConfig, linked.Token);
            }
            catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
            {
                last = new TimeoutException($"Activity timed out after {timeoutMs}ms.", oce);
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (i < attempts && delayMs > 0)
                await Task.Delay(delayMs, ct);
        }

        throw last ?? new InvalidOperationException("Activity execution failed.");
    }

    private static string? GetConfig(Dictionary<string, string> config, string key, string? defaultValue = "")
    {
        if (!config.TryGetValue(key, out var value))
            return defaultValue;

        return value ?? defaultValue;
    }

    private sealed record WorkflowRuntimeDefinition
    {
        public List<WorkflowRuntimeActivity> Activities { get; init; } = [];
    }

    private sealed record WorkflowRuntimeActivity
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string Type { get; init; } = string.Empty;
        public Dictionary<string, JsonElement> Config { get; init; } = new();
        public WorkflowCondition? When { get; init; }
        public string? Next { get; init; }
        public string? OnSuccess { get; init; }
        public string? OnFailure { get; init; }
        public int TimeoutMs { get; init; } = 30000;
        public int RetryCount { get; init; } = 0;
        public int RetryDelayMs { get; init; } = 0;
    }

    private sealed record WorkflowCondition
    {
        public string? Key { get; init; }
        public string? Equals { get; init; }
        public string? NotEquals { get; init; }
    }

    private static bool ShouldExecute(Dictionary<string, JsonElement> payload, WorkflowCondition? condition)
    {
        if (condition is null || string.IsNullOrWhiteSpace(condition.Key))
            return true;

        payload.TryGetValue(condition.Key, out var value);
        var normalized = value.ValueKind == JsonValueKind.Undefined ? null : value.ToString();

        if (condition.Equals is not null)
            return string.Equals(normalized, condition.Equals, StringComparison.OrdinalIgnoreCase);

        if (condition.NotEquals is not null)
            return !string.Equals(normalized, condition.NotEquals, StringComparison.OrdinalIgnoreCase);

        return true;
    }

    private static Dictionary<string, string> ResolveConfig(Dictionary<string, JsonElement> raw, Dictionary<string, string> context)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
            resolved[key] = ResolveTokens(text, context);
        }
        return resolved;
    }

    private static string ResolveTokens(string value, Dictionary<string, string> context)
    {
        var result = value;
        foreach (var kv in context)
        {
            result = result.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static void CaptureOutputs(Dictionary<string, string> context, WorkflowRuntimeActivity activity, string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return;
        using var doc = JsonDocument.Parse(outputJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        var prefix = $"steps.{(activity.Id ?? activity.Name ?? activity.Type)}";
        foreach (var prop in doc.RootElement.EnumerateObject())
            context[$"{prefix}.{prop.Name}"] = prop.Value.ToString();
    }

    private static WorkflowRuntimeActivity? ResolveNext(
        string? nextId,
        List<WorkflowRuntimeActivity> ordered,
        Dictionary<string, WorkflowRuntimeActivity> byId,
        WorkflowRuntimeActivity current)
    {
        if (!string.IsNullOrWhiteSpace(nextId))
        {
            if (byId.TryGetValue(nextId, out var nextById)) return nextById;
            var byName = ordered.FirstOrDefault(x => string.Equals(x.Name, nextId, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        var index = ordered.IndexOf(current);
        return index >= 0 && index + 1 < ordered.Count ? ordered[index + 1] : null;
    }
}
