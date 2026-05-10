using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Controllers;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

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
        var agentExecutor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
        var threadRepo = scope.ServiceProvider.GetRequiredService<IConversationThreadRepository>();
        var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var connectionStore = scope.ServiceProvider.GetRequiredService<ITenantConnectionStore>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var httpClientFactory = scope.ServiceProvider.GetService<IHttpClientFactory>();
        var mcpGateway = scope.ServiceProvider.GetRequiredService<IMcpToolGateway>();

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
                        agentExecutor,
                        threadRepo,
                        database,
                        connectionStore,
                        dataProtectionProvider,
                        httpClientFactory,
                        mcpGateway,
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
        IAgentExecutor agentExecutor,
        IConversationThreadRepository threadRepo,
        IMongoDatabase database,
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory? httpClientFactory,
        IMcpToolGateway mcpGateway,
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

        if (string.Equals(activity.Type, "human.assign", StringComparison.OrdinalIgnoreCase))
        {
            var agentId = GetConfig(resolvedConfig, "agentId");
            var queue = GetConfig(resolvedConfig, "queue");
            var priority = GetConfig(resolvedConfig, "priority", "normal");
            var status = !string.IsNullOrWhiteSpace(agentId) ? "assigned" : "queued";
            await TryUpdateThreadInboxMetadataAsync(threadRepo, tenantId, execution, resolvedConfig, new Dictionary<string, string?>
            {
                ["assignedTo"] = agentId,
                ["queue"] = queue,
                ["priority"] = priority,
                ["status"] = "Active"
            }, ct);
            return JsonSerializer.Serialize(new
            {
                assignmentStatus = status,
                agentId,
                queue,
                priority
            });
        }

        if (string.Equals(activity.Type, "human.handoff", StringComparison.OrdinalIgnoreCase))
        {
            var team = GetConfig(resolvedConfig, "team", "support");
            var reason = GetConfig(resolvedConfig, "reason", "workflow_handoff");
            var priority = GetConfig(resolvedConfig, "priority", "normal");
            await TryUpdateThreadInboxMetadataAsync(threadRepo, tenantId, execution, resolvedConfig, new Dictionary<string, string?>
            {
                ["handoffTeam"] = team,
                ["handoffReason"] = reason,
                ["priority"] = priority,
                ["status"] = "Paused"
            }, ct);
            return JsonSerializer.Serialize(new
            {
                handoffStatus = "escalated",
                team,
                reason,
                priority
            });
        }

        if (string.Equals(activity.Type, "ai.agent", StringComparison.OrdinalIgnoreCase))
        {
            var input = GetConfig(resolvedConfig, "input", GetConfig(resolvedConfig, "prompt", string.Empty));
            if (string.IsNullOrWhiteSpace(input))
                throw new InvalidOperationException("Activity ai.agent requires config.input or config.prompt.");

            var maxLatencyMs = ParseInt(GetConfig(resolvedConfig, "maxLatencyMs"), 30000);
            var maxCostUsd = ParseDouble(GetConfig(resolvedConfig, "maxCostUsd"), -1);
            var dlpEnabled = ParseBool(GetConfig(resolvedConfig, "dlpEnabled"), true);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromMilliseconds(maxLatencyMs));

            var primaryModel = GetConfig(resolvedConfig, "model");
            var fallbackModel = GetConfig(resolvedConfig, "fallbackModel");
            var request = new AgentExecutionRequest
            {
                TenantId = tenantId,
                AgentKey = GetConfig(resolvedConfig, "agentId", "default-agent") ?? "default-agent",
                UserId = execution.RequestedBy,
                UserMessage = input!,
                ContextJson = JsonSerializer.Serialize(new
                {
                    workflowExecutionId = execution.Id,
                    workflowDefinitionId = execution.WorkflowDefinitionId,
                    model = primaryModel,
                    fallbackModel,
                    knowledge = GetConfig(resolvedConfig, "knowledge")
                }),
                CorrelationId = execution.CorrelationId,
                ThreadId = execution.Id,
                Priority = ExecutionPriority.Normal
            };

            var (result, usedFallback) = await ExecuteAgentWithFallbackAsync(
                agentExecutor,
                request,
                fallbackModel,
                primaryModel,
                linked.Token);
            var estimatedCostUsd = EstimateTokenCostUsd(result.TotalTokensUsed);
            if (maxCostUsd >= 0 && estimatedCostUsd > maxCostUsd)
            {
                throw new InvalidOperationException(
                    $"ai.agent estimated cost {estimatedCostUsd:F6} USD exceeds maxCostUsd {maxCostUsd:F6}.");
            }
            var response = result.FinalResponse ?? string.Empty;
            if (dlpEnabled)
                response = ApplyBasicDlp(response);

            return JsonSerializer.Serialize(new
            {
                executionId = result.ExecutionId,
                status = result.Status.ToString(),
                estimatedCostUsd,
                model = usedFallback ? fallbackModel : primaryModel,
                usedFallback,
                response
            });
        }

        if (string.Equals(activity.Type, "kyc.document_check", StringComparison.OrdinalIgnoreCase))
        {
            var cases = database.GetCollection<KycCaseDto>("kyc_cases");
            var caseId = Guid.NewGuid().ToString("N");
            var score = CalculateSimpleScore(GetConfig(resolvedConfig, "documentNumber"), GetConfig(resolvedConfig, "fullName"));
            var status = score >= 70 ? "approved" : "needs_review";

            var dto = new KycCaseDto
            {
                CaseId = caseId,
                TenantId = tenantId,
                CustomerId = GetConfig(resolvedConfig, "customerId"),
                FullName = GetConfig(resolvedConfig, "fullName"),
                DocumentType = GetConfig(resolvedConfig, "documentType"),
                DocumentNumber = GetConfig(resolvedConfig, "documentNumber"),
                DecisionStatus = status,
                RiskScore = score,
                ReviewRequired = status != "approved",
                Evidence = new List<string>(),
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = execution.RequestedBy
            };
            await cases.InsertOneAsync(dto, cancellationToken: ct);
            return JsonSerializer.Serialize(new { caseId = dto.CaseId, decisionStatus = dto.DecisionStatus, riskScore = dto.RiskScore });
        }

        if (string.Equals(activity.Type, "kyc.review_case", StringComparison.OrdinalIgnoreCase))
        {
            var cases = database.GetCollection<KycCaseDto>("kyc_cases");
            var caseId = GetConfig(resolvedConfig, "caseId");
            if (string.IsNullOrWhiteSpace(caseId))
                throw new InvalidOperationException("Activity kyc.review_case requires config.caseId.");

            var existing = await cases.Find(x => x.CaseId == caseId && x.TenantId == tenantId).FirstOrDefaultAsync(ct);
            if (existing is null)
                throw new InvalidOperationException($"KYC case {caseId} not found.");

            var approved = ParseBool(GetConfig(resolvedConfig, "approved"), true);
            existing.DecisionStatus = approved ? "approved" : "rejected";
            existing.ReviewRequired = false;
            existing.ReviewNotes = GetConfig(resolvedConfig, "notes");
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = execution.RequestedBy;
            await cases.ReplaceOneAsync(x => x.CaseId == caseId && x.TenantId == tenantId, existing, cancellationToken: ct);

            return JsonSerializer.Serialize(new { caseId = existing.CaseId, decisionStatus = existing.DecisionStatus });
        }

        if (string.Equals(activity.Type, "payments.create_intent", StringComparison.OrdinalIgnoreCase))
        {
            var payments = database.GetCollection<PaymentIntentDto>("payment_intents");
            var paymentId = Guid.NewGuid().ToString("N");
            var amount = ParseDecimal(GetConfig(resolvedConfig, "amount"), 0);
            if (amount <= 0)
                throw new InvalidOperationException("Activity payments.create_intent requires config.amount > 0.");

            var intent = new PaymentIntentDto
            {
                PaymentId = paymentId,
                TenantId = tenantId,
                CustomerId = GetConfig(resolvedConfig, "customerId"),
                Amount = amount,
                Currency = GetConfig(resolvedConfig, "currency", "USD") ?? "USD",
                Reference = GetConfig(resolvedConfig, "reference"),
                Status = "created",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await payments.InsertOneAsync(intent, cancellationToken: ct);
            return JsonSerializer.Serialize(new { paymentId = intent.PaymentId, status = intent.Status });
        }

        if (string.Equals(activity.Type, "http.request", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activity.Type, "webhook.call", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteHttpActivityAsync(
                tenantId,
                connectionStore,
                dataProtectionProvider,
                httpClientFactory,
                activity,
                execution,
                resolvedConfig,
                ct);
        }

        if (string.Equals(activity.Type, "mcp.tool_call", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteMcpActivityAsync(tenantId, mcpGateway, activity, execution, resolvedConfig, ct);
        }

        if (string.Equals(activity.Type, "storage.write", StringComparison.OrdinalIgnoreCase))
        {
            var storage = database.GetCollection<WorkflowStorageDocument>("workflow_storage");
            var bucket = GetConfig(resolvedConfig, "bucket", "default")!;
            var path = GetConfig(resolvedConfig, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Activity storage.write requires config.path.");

            var doc = new WorkflowStorageDocument
            {
                Id = $"{tenantId}:{bucket}:{path}",
                TenantId = tenantId,
                Bucket = bucket,
                Path = path!,
                Content = GetConfig(resolvedConfig, "content", string.Empty) ?? string.Empty,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = execution.RequestedBy
            };
            await storage.ReplaceOneAsync(x => x.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true }, ct);
            return JsonSerializer.Serialize(new { bucket, path, status = "stored" });
        }

        if (string.Equals(activity.Type, "files.read", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activity.Type, "drive.lookup", StringComparison.OrdinalIgnoreCase))
        {
            var storage = database.GetCollection<WorkflowStorageDocument>("workflow_storage");
            var path = GetConfig(resolvedConfig, "path", GetConfig(resolvedConfig, "folder", string.Empty));
            var query = GetConfig(resolvedConfig, "query", string.Empty);
            var bucket = GetConfig(resolvedConfig, "bucket", "default")!;
            var filter = Builders<WorkflowStorageDocument>.Filter.Eq(x => x.TenantId, tenantId);
            if (!string.IsNullOrWhiteSpace(path))
                filter &= Builders<WorkflowStorageDocument>.Filter.Regex(x => x.Path, new MongoDB.Bson.BsonRegularExpression(Regex.Escape(path), "i"));
            if (!string.IsNullOrWhiteSpace(query))
                filter &= Builders<WorkflowStorageDocument>.Filter.Regex(x => x.Content, new MongoDB.Bson.BsonRegularExpression(Regex.Escape(query), "i"));

            var docs = await storage.Find(filter).Limit(10).ToListAsync(ct);
            return JsonSerializer.Serialize(new
            {
                source = GetConfig(resolvedConfig, "source", activity.Type.StartsWith("drive.", StringComparison.OrdinalIgnoreCase) ? "drive" : "storage"),
                bucket,
                count = docs.Count,
                items = docs.Select(x => new { x.Path, preview = x.Content.Length > 240 ? x.Content[..240] : x.Content })
            });
        }

        if (string.Equals(activity.Type, "voice.call", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activity.Type, "callcenter.outbound_call", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteTwilioVoiceActivityAsync(
                tenantId,
                connectionStore,
                dataProtectionProvider,
                httpClientFactory,
                activity,
                execution,
                resolvedConfig,
                ct);
        }

        throw new InvalidOperationException($"Unknown activity type '{activity.Type}'.");
    }

    private static async Task<string?> ExecuteWithPolicyAsync(
        string tenantId,
        IConnectStore connectStore,
        IWorkflowSecurityPolicyService policy,
        IAgentExecutor agentExecutor,
        IConversationThreadRepository threadRepo,
        IMongoDatabase database,
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory? httpClientFactory,
        IMcpToolGateway mcpGateway,
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
                return await ExecuteActivityAsync(
                    tenantId,
                    connectStore,
                    policy,
                    agentExecutor,
                    threadRepo,
                    database,
                    connectionStore,
                    dataProtectionProvider,
                    httpClientFactory,
                    mcpGateway,
                    activity,
                    execution,
                    resolvedConfig,
                    linked.Token);
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

    private static async Task<string?> ExecuteHttpActivityAsync(
        string tenantId,
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory? httpClientFactory,
        WorkflowRuntimeActivity activity,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        CancellationToken ct)
    {
        var url = GetConfig(resolvedConfig, "url");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"Activity {activity.Type} requires config.url.");

        var connection = await ResolveConnectionAsync(tenantId, connectionStore, resolvedConfig, "rest", ct);
        if (connection is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            var baseUrl = GetConnectionConfig(connection.Connection, "baseUrl");
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException($"Connection {connection.Connection.Id} requires config.baseUrl.");
            url = $"{baseUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        }

        using var request = new HttpRequestMessage(
            new HttpMethod(GetConfig(resolvedConfig, "method", activity.Type == "webhook.call" ? "POST" : "GET")!),
            url);

        var body = GetConfig(resolvedConfig, "body", string.Empty);
        if (!string.IsNullOrWhiteSpace(body) && request.Method != HttpMethod.Get)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        if (connection is not null)
            ApplyConnectionAuth(request, connection, dataProtectionProvider);

        var http = httpClientFactory?.CreateClient("workflow-runtime") ?? new HttpClient();
        var started = DateTimeOffset.UtcNow;
        var response = await http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var durationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP activity failed with {(int)response.StatusCode}: {responseBody}");

        return JsonSerializer.Serialize(new
        {
            statusCode = (int)response.StatusCode,
            durationMs,
            body = responseBody,
            connectionId = connection?.Connection.Id
        });
    }

    private static async Task<string?> ExecuteMcpActivityAsync(
        string tenantId,
        IMcpToolGateway mcpGateway,
        WorkflowRuntimeActivity activity,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        CancellationToken ct)
    {
        var server = GetConfig(resolvedConfig, "server");
        var tool = GetConfig(resolvedConfig, "tool");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool))
            throw new InvalidOperationException("Activity mcp.tool_call requires config.server and config.tool.");

        var result = await mcpGateway.ExecuteAsync(
            server!,
            tool!,
            new ToolExecutionContext
            {
                TenantId = tenantId,
                UserId = execution.RequestedBy,
                ExecutionId = execution.Id,
                StepId = activity.Id ?? activity.Name ?? activity.Type,
                CorrelationId = execution.CorrelationId,
                InputJson = GetConfig(resolvedConfig, "input", "{}") ?? "{}",
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "workflow-runtime",
                    ["workflowDefinitionId"] = execution.WorkflowDefinitionId
                }
            },
            ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP activity failed: {result.ErrorCode} {result.ErrorMessage}");

        return result.OutputJson;
    }

    private static async Task<string?> ExecuteTwilioVoiceActivityAsync(
        string tenantId,
        ITenantConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory? httpClientFactory,
        WorkflowRuntimeActivity activity,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        CancellationToken ct)
    {
        var phoneNumber = GetConfig(resolvedConfig, "phoneNumber");
        var script = GetConfig(resolvedConfig, "script");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new InvalidOperationException($"Activity {activity.Type} requires config.phoneNumber.");
        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException($"Activity {activity.Type} requires config.script.");

        var connection = await ResolveConnectionAsync(tenantId, connectionStore, resolvedConfig, "twilio", ct)
            ?? throw new InvalidOperationException("Twilio connection not found. Configure one tenant connection with connectorId/provider 'twilio'.");
        var secret = ReadConnectionSecret(connection, dataProtectionProvider);
        var accountSid = GetConnectionConfig(connection.Connection, "accountSid", secret, "accountSid", "account");
        var authToken = GetSecretValue(secret, "authToken", "token", "secret");
        var fromPhone = GetConnectionConfig(connection.Connection, "fromPhoneNumber", secret, "fromPhoneNumber", "senderPhoneNumber", "from");
        var statusCallback = GetConnectionConfig(connection.Connection, "statusCallbackUrl", secret, "statusCallbackUrl", "statusCallbackURI");

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(fromPhone))
            throw new InvalidOperationException("Twilio connection requires accountSid, authToken secret and fromPhoneNumber.");

        var twiml = script!.Contains("<Say", StringComparison.OrdinalIgnoreCase)
            ? script
            : $"<Response><Say language='es-MX' loop='1' voice='Polly.Mia'>{System.Security.SecurityElement.Escape(CleanForAudio(script))}</Say></Response>";

        var form = new Dictionary<string, string>
        {
            ["To"] = phoneNumber!,
            ["From"] = fromPhone!,
            ["Twiml"] = twiml
        };
        if (!string.IsNullOrWhiteSpace(statusCallback))
        {
            form["StatusCallback"] = statusCallback!;
            form["StatusCallbackEvent"] = "initiated ringing answered completed busy no-answer canceled";
        }

        var http = httpClientFactory?.CreateClient("workflow-runtime") ?? new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Calls.json")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio voice call failed with {(int)response.StatusCode}: {body}");

        return JsonSerializer.Serialize(new
        {
            provider = "twilio",
            channel = activity.Type == "callcenter.outbound_call" ? "callcenter" : "voice",
            phoneNumber,
            connectionId = connection.Connection.Id,
            body
        });
    }

    private static async Task<ResolvedConnection?> ResolveConnectionAsync(
        string tenantId,
        ITenantConnectionStore connectionStore,
        Dictionary<string, string> resolvedConfig,
        string preferredConnector,
        CancellationToken ct)
    {
        var connectionId = GetConfig(resolvedConfig, "connectionId", GetConfig(resolvedConfig, "authProfileId", string.Empty));
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            var byId = await connectionStore.GetConnectionAsync(tenantId, connectionId!, ct);
            if (byId is not null)
                return new ResolvedConnection(byId, await connectionStore.GetSecretAsync(tenantId, byId.Id, ct));
        }

        var provider = GetConfig(resolvedConfig, "provider", preferredConnector);
        var connections = await connectionStore.GetConnectionsAsync(tenantId, ct);
        var match = connections.FirstOrDefault(x =>
            string.Equals(x.ConnectorId, preferredConnector, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ConnectorId, provider, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetConnectionConfig(x, "provider"), preferredConnector, StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains(preferredConnector, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? null
            : new ResolvedConnection(match, await connectionStore.GetSecretAsync(tenantId, match.Id, ct));
    }

    private static void ApplyConnectionAuth(HttpRequestMessage request, ResolvedConnection connection, IDataProtectionProvider dataProtectionProvider)
    {
        var secret = ReadConnectionSecret(connection, dataProtectionProvider);
        var authType = GetConnectionConfig(connection.Connection, "authType", secret, "authType");
        if (string.Equals(authType, "basic", StringComparison.OrdinalIgnoreCase))
        {
            var username = GetConnectionConfig(connection.Connection, "username", secret, "username", "user");
            var password = GetSecretValue(secret, "password", "token", "secret");
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));
            return;
        }

        var token = GetSecretValue(secret, "bearerToken", "apiKey", "token", "secret");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static Dictionary<string, string> ReadConnectionSecret(ResolvedConnection connection, IDataProtectionProvider dataProtectionProvider)
    {
        if (connection.Secret is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var protector = dataProtectionProvider.CreateProtector("tenant-connections-secrets-v1");
        var plain = protector.Unprotect(connection.Secret.CipherText);
        if (plain.TrimStart().StartsWith("{", StringComparison.Ordinal))
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["secret"] = plain };
    }

    private static string? GetConnectionConfig(TenantConnectionContract connection, string key, Dictionary<string, string>? secret = null, params string[] aliases)
    {
        if (connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (connection.Config.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return secret is null ? null : GetSecretValue(secret, key, aliases);
    }

    private static string? GetSecretValue(Dictionary<string, string> secret, string key, params string[] aliases)
    {
        if (secret.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var alias in aliases)
        {
            if (secret.TryGetValue(alias, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string CleanForAudio(string text)
    {
        var firstLine = text.Split('\n')[0];
        return Regex.Replace(firstLine, @"\d{3,}", match => string.Join(" ", match.Value.ToCharArray()));
    }

    private static string ApplyBasicDlp(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var output = input;
        output = System.Text.RegularExpressions.Regex.Replace(
            output,
            @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
            "[redacted-email]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        output = System.Text.RegularExpressions.Regex.Replace(
            output,
            @"\b(?:\+?\d{1,3}[\s-]?)?(?:\(?\d{2,4}\)?[\s-]?)?\d{3,4}[\s-]?\d{4}\b",
            "[redacted-phone]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        output = System.Text.RegularExpressions.Regex.Replace(
            output,
            @"\b(?:\d[ -]*?){13,19}\b",
            "[redacted-card]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        output = output.Replace("password", "[redacted-secret]", StringComparison.OrdinalIgnoreCase);
        output = output.Replace("secret", "[redacted-secret]", StringComparison.OrdinalIgnoreCase);
        output = output.Replace("token", "[redacted-secret]", StringComparison.OrdinalIgnoreCase);
        return output;
    }

    private static int CalculateSimpleScore(string? documentNumber, string? fullName)
    {
        var score = 50;
        if (!string.IsNullOrWhiteSpace(documentNumber) && documentNumber.Length >= 8) score += 20;
        if (!string.IsNullOrWhiteSpace(fullName) && fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2) score += 20;
        return Math.Clamp(score, 0, 100);
    }

    private static int ParseInt(string? raw, int fallback) => int.TryParse(raw, out var value) ? value : fallback;
    private static bool ParseBool(string? raw, bool fallback) => bool.TryParse(raw, out var value) ? value : fallback;
    private static decimal ParseDecimal(string? raw, decimal fallback) => decimal.TryParse(raw, out var value) ? value : fallback;
    private static double ParseDouble(string? raw, double fallback) => double.TryParse(raw, out var value) ? value : fallback;
    private static double EstimateTokenCostUsd(int totalTokens)
    {
        const double estimatedUsdPer1KTokens = 0.005d;
        return Math.Round((totalTokens / 1000d) * estimatedUsdPer1KTokens, 6);
    }

    private static async Task<(AgentExecutionResult Result, bool UsedFallback)> ExecuteAgentWithFallbackAsync(
        IAgentExecutor agentExecutor,
        AgentExecutionRequest primaryRequest,
        string? fallbackModel,
        string? primaryModel,
        CancellationToken ct)
    {
        try
        {
            var primary = await agentExecutor.ExecuteAsync(primaryRequest, ct);
            if (primary.Status == ExecutionStatus.Completed || string.IsNullOrWhiteSpace(fallbackModel))
                return (primary, false);
        }
        catch (OperationCanceledException) when (!string.IsNullOrWhiteSpace(fallbackModel))
        {
            // Fallback attempt below.
        }

        if (string.IsNullOrWhiteSpace(fallbackModel))
            throw new InvalidOperationException("ai.agent failed and no fallbackModel is configured.");

        var fallbackContext = JsonSerializer.Serialize(new
        {
            workflowExecutionId = primaryRequest.ThreadId,
            model = primaryModel,
            fallbackModel,
            useFallback = true
        });

        var fallbackRequest = primaryRequest with { ContextJson = fallbackContext };
        var fallback = await agentExecutor.ExecuteAsync(fallbackRequest, ct);
        return (fallback, true);
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
        [JsonPropertyName("equals")]
        public string? EqualsValue { get; init; }
        public string? NotEquals { get; init; }
    }

    private sealed record ResolvedConnection(TenantConnectionContract Connection, TenantConnectionSecretContract? Secret);

    private sealed class WorkflowStorageDocument
    {
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
    }

    private static bool ShouldExecute(Dictionary<string, JsonElement> payload, WorkflowCondition? condition)
    {
        if (condition is null || string.IsNullOrWhiteSpace(condition.Key))
            return true;

        payload.TryGetValue(condition.Key, out var value);
        var normalized = value.ValueKind == JsonValueKind.Undefined ? null : value.ToString();

        if (condition.EqualsValue is not null)
            return string.Equals(normalized, condition.EqualsValue, StringComparison.OrdinalIgnoreCase);

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

    private static async Task TryUpdateThreadInboxMetadataAsync(
        IConversationThreadRepository threadRepo,
        string tenantId,
        WorkflowExecutionContract execution,
        Dictionary<string, string> resolvedConfig,
        Dictionary<string, string?> metadata,
        CancellationToken ct)
    {
        var threadId = GetConfig(resolvedConfig, "threadId", execution.CorrelationId);
        if (string.IsNullOrWhiteSpace(threadId))
            return;

        var thread = await threadRepo.GetByIdAsync(threadId, tenantId, ct);
        if (thread is null)
            return;

        var result = thread.UpdateMetadata(metadata, execution.RequestedBy);
        if (!result.IsSuccess)
            return;

        await threadRepo.UpdateAsync(thread, ct);
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
