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

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "human.assign",
                DisplayName = "Assign To Agent",
                Category = "Human",
                Description = "Routes the case/thread to a specific agent or queue.",
                InputSchema = new Dictionary<string, string>
                {
                    ["agentId"] = "string",
                    ["queue"] = "string",
                    ["priority"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["assignmentStatus"] = "assigned|queued" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "human.handoff",
                DisplayName = "Human Handoff",
                Category = "Human",
                Description = "Escalates the conversation to a human support team.",
                InputSchema = new Dictionary<string, string>
                {
                    ["team"] = "string",
                    ["reason"] = "string",
                    ["priority"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["handoffStatus"] = "escalated|queued" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "kyc.document_check",
                DisplayName = "KYC Document Check",
                Category = "KYC",
                Description = "Runs KYC document validation and returns caseId/decision.",
                InputSchema = new Dictionary<string, string>
                {
                    ["customerId"] = "string",
                    ["fullName"] = "string",
                    ["documentType"] = "string",
                    ["documentNumber"] = "string"
                },
                OutputSchema = new Dictionary<string, string>
                {
                    ["caseId"] = "string",
                    ["decisionStatus"] = "approved|needs_review|rejected"
                },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "kyc.review_case",
                DisplayName = "KYC Review Case",
                Category = "KYC",
                Description = "Performs human review decision for KYC case.",
                InputSchema = new Dictionary<string, string>
                {
                    ["caseId"] = "string",
                    ["approved"] = "bool",
                    ["notes"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["decisionStatus"] = "approved|rejected" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertActivityAsync(new WorkflowActivityCatalogContract
            {
                TypeName = "payments.create_intent",
                DisplayName = "Create Payment Intent",
                Category = "Payments",
                Description = "Creates payment intent and returns payment id.",
                InputSchema = new Dictionary<string, string>
                {
                    ["customerId"] = "string",
                    ["amount"] = "number",
                    ["currency"] = "string",
                    ["reference"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["paymentId"] = "string", ["status"] = "created|confirmed" },
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);
        }

        await SeedRuntimeIntegrationActivitiesAsync(store, cancellationToken);

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

            await store.UpsertEventAsync(new WorkflowEventCatalogContract
            {
                EventName = "kyc.document.submitted",
                DisplayName = "KYC Document Submitted",
                Entity = "KYC",
                Description = "Customer uploaded document and KYC flow should start.",
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);

            await store.UpsertEventAsync(new WorkflowEventCatalogContract
            {
                EventName = "payments.intent.created",
                DisplayName = "Payment Intent Created",
                Entity = "Payment",
                Description = "Payment intent created and awaits confirmation/follow-up.",
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = actor
            }, cancellationToken);
        }

        _logger.LogInformation("Workflow catalog seed completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedRuntimeIntegrationActivitiesAsync(IWorkflowStudioStore store, CancellationToken ct)
    {
        var actor = "system-seed";
        var now = DateTimeOffset.UtcNow;
        var activities = new[]
        {
            new WorkflowActivityCatalogContract
            {
                TypeName = "intent.branch",
                DisplayName = "Bifurcar por intención",
                Category = "Orquestación",
                Description = "Evalúa intenciones detectadas y redirige a nodos distintos por coincidencia.",
                InputSchema = new Dictionary<string, string>
                {
                    ["intent"] = "string (detected intent fallback)",
                    ["matchedIntentsCsv"] = "string (intent_a,intent_b,...)",
                    ["mode"] = "first|all",
                    ["case.<intentKey>"] = "string (id/name del nodo destino)"
                },
                OutputSchema = new Dictionary<string, string>
                {
                    ["next"] = "string",
                    ["nextIds"] = "array<string>",
                    ["matchedIntent"] = "string"
                },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "http.request",
                DisplayName = "Consultar API",
                Category = "Datos",
                Description = "Consulta o envia datos a una API usando una conexion REST reutilizable.",
                InputSchema = new Dictionary<string, string>
                {
                    ["url"] = "string (required)",
                    ["method"] = "GET|POST|PUT",
                    ["body"] = "json|string",
                    ["connectionId"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["statusCode"] = "number", ["body"] = "string" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "webhook.call",
                DisplayName = "Llamar webhook",
                Category = "Datos",
                Description = "Envia un evento a un webhook externo.",
                InputSchema = new Dictionary<string, string>
                {
                    ["url"] = "string (required)",
                    ["body"] = "json|string",
                    ["connectionId"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["statusCode"] = "number", ["body"] = "string" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "files.read",
                DisplayName = "Leer archivo",
                Category = "Archivos",
                Description = "Busca contenido almacenado por el workflow o sincronizado desde archivos.",
                InputSchema = new Dictionary<string, string> { ["path"] = "string", ["query"] = "string" },
                OutputSchema = new Dictionary<string, string> { ["count"] = "number", ["items"] = "array" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "drive.lookup",
                DisplayName = "Buscar en Drive",
                Category = "Archivos",
                Description = "Busca documentos sincronizados desde Drive o storage conectado.",
                InputSchema = new Dictionary<string, string> { ["folder"] = "string", ["query"] = "string" },
                OutputSchema = new Dictionary<string, string> { ["count"] = "number", ["items"] = "array" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "storage.write",
                DisplayName = "Guardar en storage",
                Category = "Archivos",
                Description = "Guarda informacion generada por el flujo para reutilizarla en otros pasos.",
                InputSchema = new Dictionary<string, string>
                {
                    ["bucket"] = "string",
                    ["path"] = "string (required)",
                    ["content"] = "string"
                },
                OutputSchema = new Dictionary<string, string> { ["status"] = "stored" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "mcp.tool_call",
                DisplayName = "Usar herramienta MCP",
                Category = "MCP",
                Description = "Ejecuta una herramienta MCP permitida para el tenant.",
                InputSchema = new Dictionary<string, string>
                {
                    ["server"] = "string (required)",
                    ["tool"] = "string (required)",
                    ["input"] = "json|string"
                },
                OutputSchema = new Dictionary<string, string> { ["outputJson"] = "json" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "voice.call",
                DisplayName = "Llamada de voz",
                Category = "Voz",
                Description = "Inicia una llamada de voz usando la conexion Twilio comun del tenant.",
                InputSchema = new Dictionary<string, string>
                {
                    ["connectionId"] = "string",
                    ["phoneNumber"] = "string (required)",
                    ["script"] = "string (required)"
                },
                OutputSchema = new Dictionary<string, string> { ["provider"] = "twilio", ["body"] = "json" },
                UpdatedAt = now,
                UpdatedBy = actor
            },
            new WorkflowActivityCatalogContract
            {
                TypeName = "callcenter.outbound_call",
                DisplayName = "Llamada call center",
                Category = "Call Center",
                Description = "Inicia una llamada saliente por campana o troncal usando la misma conexion Twilio.",
                InputSchema = new Dictionary<string, string>
                {
                    ["campaignId"] = "string",
                    ["connectionId"] = "string",
                    ["phoneNumber"] = "string (required)",
                    ["script"] = "string (required)"
                },
                OutputSchema = new Dictionary<string, string> { ["provider"] = "twilio", ["body"] = "json" },
                UpdatedAt = now,
                UpdatedBy = actor
            }
        };

        foreach (var activity in activities)
            await store.UpsertActivityAsync(activity, ct);
    }
}
