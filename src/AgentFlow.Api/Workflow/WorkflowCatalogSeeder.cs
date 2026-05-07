using AgentFlow.Abstractions.Workflow;

namespace AgentFlow.Api.Workflow;

public sealed class WorkflowCatalogSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowCatalogSeeder> _logger;

    public WorkflowCatalogSeeder(IServiceScopeFactory scopeFactory, ILogger<WorkflowCatalogSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStudioStore>();

        var activities = await store.GetActivitiesAsync(cancellationToken);
        if (activities.Count == 0)
        {
            var actor = "system-seed";
            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "connect.send_whatsapp_template",
                DisplayName = "Send WhatsApp Template",
                Category = "Connect",
                Description = "Enqueues a WhatsApp template message into Connect inbox.",
                InputSchema = new Dictionary<string, string>
                {
                    ["recipient"] = "string (required)",
                    ["templateId"] = "string",
                    ["campaignId"] = "string",
                    ["content"] = "string",
                    ["channel"] = "string (default: whatsapp)"
                },
                OutputSchema = new Dictionary<string, string> { ["inboxMessageId"] = "string" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "connect.update_inbox_status",
                DisplayName = "Update Inbox Status",
                Category = "Connect",
                Description = "Updates status of a Connect inbox message.",
                InputSchema = new Dictionary<string, string>
                {
                    ["messageId"] = "string (required)",
                    ["status"] = "Queued|Sent|Delivered|Read|Failed|Escalated",
                    ["lastError"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["status"] = "string" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "connect.enqueue_campaign_message",
                DisplayName = "Enqueue Campaign Message",
                Category = "Connect",
                Description = "Queues a campaign-related message in Connect inbox.",
                InputSchema = new Dictionary<string, string>
                {
                    ["recipient"] = "string (required)",
                    ["campaignId"] = "string",
                    ["templateId"] = "string",
                    ["content"] = "string",
                    ["channel"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["inboxMessageId"] = "string" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);
        }

        var events = await store.GetEventsAsync(cancellationToken);
        if (events.Count == 0)
        {
            var actor = "system-seed";
            await store.UpsertEventAsync(new WorkflowEventCatalogContract
            {
                EventName = "connect.message.received",
                DisplayName = "Message Received",
                Entity = "Conversation",
                Description = "Inbound message arrived from channel webhook.",
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertEventAsync(new WorkflowEventCatalogContract
            {
                EventName = "connect.campaign.scheduled",
                DisplayName = "Campaign Scheduled",
                Entity = "Campaign",
                Description = "A campaign was scheduled and is ready for dispatch.",
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);
        }

        _logger.LogInformation("Workflow catalog seed completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
