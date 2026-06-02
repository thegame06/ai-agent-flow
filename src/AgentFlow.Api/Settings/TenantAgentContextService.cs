using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using AgentFlow.Abstractions;

namespace AgentFlow.Api.Settings;

public interface ITenantAgentContextService : ITenantAgentContextComposer
{
    Task<TenantAgentContextSettingsDto> GetAsync(string tenantId, string userId, CancellationToken ct = default);
    Task<TenantAgentContextSettingsDto> SaveAsync(string tenantId, TenantAgentContextSettingsDto settings, string userId, CancellationToken ct = default);
}

public sealed class TenantAgentContextService : ITenantAgentContextService
{
    private readonly IMongoCollection<TenantAgentContextSettingsDocument> _collection;

    public TenantAgentContextService(IMongoDatabase database)
    {
        _collection = database.GetCollection<TenantAgentContextSettingsDocument>("tenant_agent_context_settings");
    }

    public async Task<TenantAgentContextSettingsDto> GetAsync(string tenantId, string userId, CancellationToken ct = default)
    {
        var doc = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct)
            ?? TenantAgentContextSettingsDocument.Default(tenantId, userId);
        return ToDto(doc);
    }

    public async Task<TenantAgentContextSettingsDto> SaveAsync(string tenantId, TenantAgentContextSettingsDto settings, string userId, CancellationToken ct = default)
    {
        var current = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var doc = new TenantAgentContextSettingsDocument
        {
            Id = tenantId,
            TenantId = tenantId,
            GlobalMarkdown = Normalize(settings.GlobalMarkdown),
            RouterMarkdown = Normalize(settings.RouterMarkdown),
            WorkflowMarkdown = Normalize(settings.WorkflowMarkdown),
            ConfigAssistantMarkdown = Normalize(settings.ConfigAssistantMarkdown),
            CustomMarkdown = Normalize(settings.CustomMarkdown),
            WhatsAppMarkdown = Normalize(settings.WhatsAppMarkdown),
            VoiceMarkdown = Normalize(settings.VoiceMarkdown),
            CallCenterMarkdown = Normalize(settings.CallCenterMarkdown),
            WebChatMarkdown = Normalize(settings.WebChatMarkdown),
            ApiMarkdown = Normalize(settings.ApiMarkdown),
            UpdatedAt = now,
            UpdatedBy = userId,
            CreatedAt = current?.CreatedAt ?? now
        };

        await _collection.ReplaceOneAsync(x => x.TenantId == tenantId, doc, new ReplaceOptions { IsUpsert = true }, ct);
        return ToDto(doc);
    }

    public async Task<string> ComposeSystemPromptAsync(
        string tenantId,
        string baseSystemPrompt,
        string agentId,
        string systemRole,
        string? channelType,
        CancellationToken ct = default)
    {
        var settings = await GetAsync(tenantId, "system", ct);
        var fragments = new List<string>();
        var normalizedRole = (systemRole ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRole != "router")
            AddIfPresent(fragments, settings.GlobalMarkdown, "Contexto global del tenant");

        switch (normalizedRole)
        {
            case "router":
                AddIfPresent(fragments, settings.RouterMarkdown, "Contexto para Router");
                break;
            case "workflowbrain":
                AddIfPresent(fragments, settings.WorkflowMarkdown, "Contexto para Workflow");
                break;
            case "configassistant":
                AddIfPresent(fragments, settings.ConfigAssistantMarkdown, "Contexto para Config Assistant");
                break;
            default:
                AddIfPresent(fragments, settings.CustomMarkdown, "Contexto para agentes custom");
                break;
        }

        var normalizedChannel = (channelType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRole != "router")
        {
            switch (normalizedChannel)
            {
                case "whatsapp":
                    AddIfPresent(fragments, settings.WhatsAppMarkdown, "Contexto de canal WhatsApp");
                    break;
                case "voice":
                    AddIfPresent(fragments, settings.VoiceMarkdown, "Contexto de canal voz");
                    break;
                case "callcenter":
                    AddIfPresent(fragments, settings.CallCenterMarkdown, "Contexto de canal call center");
                    break;
                case "webchat":
                    AddIfPresent(fragments, settings.WebChatMarkdown, "Contexto de canal webchat");
                    break;
                case "api":
                    AddIfPresent(fragments, settings.ApiMarkdown, "Contexto de canal API");
                    break;
            }
        }

        if (fragments.Count == 0)
            return baseSystemPrompt;

        return $"{baseSystemPrompt}\n\n## Tenant Runtime Context\nAgentId: {agentId}\nRole: {systemRole}\nChannel: {channelType ?? "unknown"}\n\n{string.Join("\n\n", fragments)}";
    }

    private static void AddIfPresent(List<string> target, string? markdown, string title)
    {
        if (!string.IsNullOrWhiteSpace(markdown))
            target.Add($"### {title}\n{markdown.Trim()}");
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\r\n", "\n").Trim();

    private static TenantAgentContextSettingsDto ToDto(TenantAgentContextSettingsDocument doc) => new()
    {
        GlobalMarkdown = doc.GlobalMarkdown,
        RouterMarkdown = doc.RouterMarkdown,
        WorkflowMarkdown = doc.WorkflowMarkdown,
        ConfigAssistantMarkdown = doc.ConfigAssistantMarkdown,
        CustomMarkdown = doc.CustomMarkdown,
        WhatsAppMarkdown = doc.WhatsAppMarkdown,
        VoiceMarkdown = doc.VoiceMarkdown,
        CallCenterMarkdown = doc.CallCenterMarkdown,
        WebChatMarkdown = doc.WebChatMarkdown,
        ApiMarkdown = doc.ApiMarkdown,
        UpdatedAt = doc.UpdatedAt,
        UpdatedBy = doc.UpdatedBy
    };

    private sealed class TenantAgentContextSettingsDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string Id { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string GlobalMarkdown { get; set; } = string.Empty;
        public string RouterMarkdown { get; set; } = string.Empty;
        public string WorkflowMarkdown { get; set; } = string.Empty;
        public string ConfigAssistantMarkdown { get; set; } = string.Empty;
        public string CustomMarkdown { get; set; } = string.Empty;
        public string WhatsAppMarkdown { get; set; } = string.Empty;
        public string VoiceMarkdown { get; set; } = string.Empty;
        public string CallCenterMarkdown { get; set; } = string.Empty;
        public string WebChatMarkdown { get; set; } = string.Empty;
        public string ApiMarkdown { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;

        public static TenantAgentContextSettingsDocument Default(string tenantId, string userId) => new()
        {
            Id = tenantId,
            TenantId = tenantId,
            GlobalMarkdown = """
Responde en espanol claro y profesional.
No prometas acciones no configuradas en el sistema.
Si una accion requiere un workflow, integracion o handoff que no existe, dilo con claridad y ofrece una alternativa segura.
Registra resultados estructurados cuando el flujo lo permita.
""",
            RouterMarkdown = """
Clasifica con prudencia.
Si la intencion no es clara, usa fallback en vez de inventar una ruta.
No conviertas un error tecnico en una accion de negocio.
""",
            WorkflowMarkdown = """
Ejecuta solo acciones soportadas por el tenant.
Si falta informacion critica para continuar, pide aclaracion concreta.
Mantiene trazabilidad de decisiones, outcomes y handoffs.
""",
            ConfigAssistantMarkdown = """
Propone configuraciones editables y explica supuestos.
Prioriza reusar capacidades existentes antes de sugerir nuevas integraciones.
No ocultes dependencias tecnicas relevantes.
""",
            CustomMarkdown = """
Respeta las politicas del tenant y evita inventar datos.
Si el contexto del negocio no alcanza, responde con claridad y solicita lo minimo necesario.
""",
            WhatsAppMarkdown = """
Responde con mensajes breves y accionables.
Evita bloques largos salvo que el usuario pida detalle.
Considera restricciones de ventana y plantillas cuando apliquen.
""",
            VoiceMarkdown = """
Habla con frases cortas y faciles de seguir.
Confirma objetivo y datos importantes antes de continuar.
Si hay encuesta o playbook, captura respuestas estructuradas.
""",
            CallCenterMarkdown = """
Prioriza claridad operativa, confirmacion de identidad y siguiente accion.
Si el caso requiere humano, deja la llamada trazada con resultado y razon.
""",
            WebChatMarkdown = """
Mantiene tono rapido y util.
Usa formato corto y evita redundancia.
""",
            ApiMarkdown = """
Responde pensando en integraciones y automatizacion.
Prefiere salidas consistentes y faciles de procesar.
""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = userId
        };
    }
}

public sealed record TenantAgentContextSettingsDto
{
    public string GlobalMarkdown { get; init; } = string.Empty;
    public string RouterMarkdown { get; init; } = string.Empty;
    public string WorkflowMarkdown { get; init; } = string.Empty;
    public string ConfigAssistantMarkdown { get; init; } = string.Empty;
    public string CustomMarkdown { get; init; } = string.Empty;
    public string WhatsAppMarkdown { get; init; } = string.Empty;
    public string VoiceMarkdown { get; init; } = string.Empty;
    public string CallCenterMarkdown { get; init; } = string.Empty;
    public string WebChatMarkdown { get; init; } = string.Empty;
    public string ApiMarkdown { get; init; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
