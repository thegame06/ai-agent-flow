using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentFlow.Api.Connect;

namespace AgentFlow.Api.Messaging;

public sealed record TemplateSyncResult
{
    public int Accounts { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public int StatusChanged { get; init; }
    public int Unchanged { get; init; }
    public int Removed { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>A template as a person writes it (header, body with {{field}} placeholders, footer, buttons).</summary>
public sealed record TemplateDraft
{
    public string? ConnectionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Language { get; init; } = "es";
    public string Category { get; init; } = "UTILITY";
    public string? HeaderText { get; init; }
    public string Body { get; init; } = string.Empty;
    public string? Footer { get; init; }
    public List<WhatsAppTemplateButton>? Buttons { get; init; }
}

public sealed record TemplateOperationResult(bool Ok, string? Error = null, WhatsAppTemplateDocument? Template = null);

public interface IWhatsAppTemplateService
{
    Task<TemplateSyncResult> SyncAsync(string tenantId, string? connectionId, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> CreateAsync(string tenantId, TemplateDraft draft, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> EditAsync(string tenantId, string templateId, TemplateDraft draft, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> DeleteAsync(string tenantId, string templateId, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> UpdateParametersAsync(string tenantId, string templateId, IReadOnlyList<WhatsAppTemplateParameter> parameters, string user, CancellationToken ct = default);
    /// <summary>Meta's message_template_status_update / quality / category webhooks.</summary>
    Task<bool> ApplyWebhookAsync(string tenantId, string metaTemplateId, string? status, string? reason, string? quality, string? category, CancellationToken ct = default);
}

public sealed partial class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly IWhatsAppTemplateStore _store;
    private readonly IMetaWhatsAppAccounts _accounts;
    private readonly IMetaTemplateApi _meta;

    public WhatsAppTemplateService(IWhatsAppTemplateStore store, IMetaWhatsAppAccounts accounts, IMetaTemplateApi meta)
    {
        _store = store;
        _accounts = accounts;
        _meta = meta;
    }

    [GeneratedRegex("^[a-z0-9_]{1,512}$")]
    private static partial Regex ValidName();

    public async Task<TemplateSyncResult> SyncAsync(string tenantId, string? connectionId, string user, CancellationToken ct = default)
    {
        var accounts = (await _accounts.GetAllAsync(tenantId, ct))
            .Where(a => connectionId is null || a.ConnectionId == connectionId)
            .ToList();
        if (accounts.Count == 0)
            return new TemplateSyncResult { Errors = ["No hay una conexion de Meta (WhatsApp Business) configurada para este tenant."] };

        var existing = await _store.ListAsync(tenantId, ct);
        int created = 0, updated = 0, statusChanged = 0, unchanged = 0, removed = 0;
        var errors = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var account in accounts)
        {
            IReadOnlyList<MetaTemplate> remote;
            try
            {
                remote = await _meta.ListAsync(account, ct);
            }
            catch (MetaApiException ex)
            {
                errors.Add($"{account.Name}: {ex.MetaMessage ?? ex.Message}");
                continue;
            }

            var mine = existing.Where(t => t.ConnectionId == account.ConnectionId).ToList();
            var seen = new HashSet<string>();
            foreach (var r in remote)
            {
                var components = WhatsAppTemplateText.FromMetaComponents(r.Components);
                var hash = WhatsAppTemplateText.Hash(r.Name, r.Language, r.Category, components);
                var local = mine.FirstOrDefault(t => t.MetaTemplateId == r.Id)
                            ?? mine.FirstOrDefault(t => t.Name == r.Name && t.Language == r.Language);

                if (local is null)
                {
                    local = new WhatsAppTemplateDocument
                    {
                        TenantId = tenantId,
                        ConnectionId = account.ConnectionId,
                        Origin = "meta",
                        CreatedAt = now
                    };
                    Apply(local, r, components, hash);
                    // Meta's own sample values become the defaults of the mapping.
                    var examples = WhatsAppTemplateText.ExamplesFromMeta(r.Components);
                    foreach (var p in local.Parameters)
                        if (examples.TryGetValue((p.Component, p.Key), out var sample)) p.Example ??= sample;
                    local.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = r.Status, Reason = r.RejectedReason, At = now });
                    local.LastSyncedAt = now;
                    local.UpdatedBy = user;
                    await _store.SaveAsync(local, ct);
                    await SnapshotAsync(local, "sync", user, ct);
                    created++;
                }
                else
                {
                    var contentChanged = local.ContentHash != hash;
                    var statusDiff = local.Status != r.Status;
                    if (contentChanged)
                    {
                        Apply(local, r, components, hash);
                        local.Version++;
                    }
                    else
                    {
                        local.MetaTemplateId = r.Id;
                        local.Status = r.Status;
                        local.QualityScore = r.QualityScore;
                        local.RejectedReason = r.RejectedReason;
                    }
                    if (statusDiff)
                        local.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = r.Status, Reason = r.RejectedReason, At = now });
                    TrimHistory(local);
                    local.LastSyncedAt = now;
                    local.UpdatedAt = now;
                    await _store.SaveAsync(local, ct);
                    if (contentChanged)
                    {
                        await SnapshotAsync(local, "sync", user, ct);
                        updated++;
                    }
                    else if (statusDiff) statusChanged++;
                    else unchanged++;
                }
                seen.Add(local.Id);
            }

            // Gone from Meta: kept for history (campaigns may point to it) but marked deleted.
            foreach (var gone in mine.Where(t => !seen.Contains(t.Id) && t.Status != WhatsAppTemplateStatuses.Deleted && t.MetaTemplateId is not null))
            {
                gone.Status = WhatsAppTemplateStatuses.Deleted;
                gone.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = WhatsAppTemplateStatuses.Deleted, Reason = "Ya no existe en Meta", At = now });
                gone.LastSyncedAt = now;
                gone.UpdatedAt = now;
                await _store.SaveAsync(gone, ct);
                removed++;
            }
        }

        return new TemplateSyncResult
        {
            Accounts = accounts.Count,
            Created = created,
            Updated = updated,
            StatusChanged = statusChanged,
            Unchanged = unchanged,
            Removed = removed,
            Errors = errors
        };
    }

    public async Task<TemplateOperationResult> CreateAsync(string tenantId, TemplateDraft draft, string user, CancellationToken ct = default)
    {
        var error = Validate(draft);
        if (error is not null) return new(false, error);

        var account = await ResolveAccountAsync(tenantId, draft.ConnectionId, ct);
        if (account is null) return new(false, "No hay una conexion de Meta (WhatsApp Business) configurada.");

        var existing = await _store.ListAsync(tenantId, ct);
        if (existing.Any(t => t.ConnectionId == account.ConnectionId && t.Name == draft.Name && t.Language == draft.Language && t.Status != WhatsAppTemplateStatuses.Deleted))
            return new(false, $"Ya existe la plantilla {draft.Name} en {draft.Language}.");

        var (components, parameters) = WhatsAppTemplateText.ToPositional(ToComponents(draft));
        var template = new WhatsAppTemplateDocument
        {
            TenantId = tenantId,
            ConnectionId = account.ConnectionId,
            BusinessAccountId = account.BusinessAccountId,
            Name = draft.Name,
            Language = draft.Language,
            Category = draft.Category.ToUpperInvariant(),
            ParameterFormat = "POSITIONAL",
            Components = components,
            Origin = "agentflow",
            UpdatedBy = user
        };
        template.Parameters = WhatsAppTemplateText.MergeParameters(components, parameters);

        try
        {
            var (id, status, category) = await _meta.CreateAsync(account, new JsonObject
            {
                ["name"] = template.Name,
                ["language"] = template.Language,
                ["category"] = template.Category,
                ["parameter_format"] = template.ParameterFormat,
                ["components"] = WhatsAppTemplateText.ToMetaComponents(template.Components, template.Parameters, template.ParameterFormat)
            }, ct);
            template.MetaTemplateId = id;
            template.Status = status;
            if (category is not null) template.Category = category; // Meta may recategorize it
        }
        catch (MetaApiException ex)
        {
            return new(false, ex.MetaMessage ?? ex.Message);
        }

        template.ContentHash = WhatsAppTemplateText.Hash(template.Name, template.Language, template.Category, template.Components);
        template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = template.Status, Source = "create" });
        await _store.SaveAsync(template, ct);
        await SnapshotAsync(template, "create", user, ct);
        return new(true, Template: template);
    }

    public async Task<TemplateOperationResult> EditAsync(string tenantId, string templateId, TemplateDraft draft, string user, CancellationToken ct = default)
    {
        var template = await _store.GetAsync(tenantId, templateId, ct);
        if (template is null) return new(false, "Plantilla no encontrada.");
        if (template.MetaTemplateId is null) return new(false, "La plantilla no existe en Meta.");
        if (template.Status is not (WhatsAppTemplateStatuses.Approved or WhatsAppTemplateStatuses.Rejected or WhatsAppTemplateStatuses.Paused))
            return new(false, "Meta solo permite editar plantillas aprobadas, rechazadas o pausadas.");
        var error = Validate(draft with { Name = template.Name, Language = template.Language });
        if (error is not null) return new(false, error);

        var account = await ResolveAccountAsync(tenantId, template.ConnectionId, ct);
        if (account is null) return new(false, "La conexion de Meta de esta plantilla ya no existe.");

        var (components, parameters) = WhatsAppTemplateText.ToPositional(ToComponents(draft));
        // A slot that still holds the same field keeps what the person set (example, static value);
        // everything else comes from the new text.
        var kept = template.Parameters.Where(p => parameters.Any(n => n.Component == p.Component && n.Key == p.Key && n.Field == p.Field));
        var merged = WhatsAppTemplateText.MergeParameters(components, kept.Concat(parameters));
        var request = new JsonObject
        {
            ["components"] = WhatsAppTemplateText.ToMetaComponents(components, merged, "POSITIONAL")
        };
        // Meta only lets the category change while the template is rejected.
        if (template.Status == WhatsAppTemplateStatuses.Rejected && !draft.Category.Equals(template.Category, StringComparison.OrdinalIgnoreCase))
            request["category"] = draft.Category.ToUpperInvariant();

        try
        {
            await _meta.EditAsync(account, template.MetaTemplateId, request, ct);
        }
        catch (MetaApiException ex)
        {
            return new(false, ex.MetaMessage ?? ex.Message);
        }

        template.Components = components;
        template.Parameters = merged;
        template.ParameterFormat = "POSITIONAL";
        if (request.ContainsKey("category")) template.Category = draft.Category.ToUpperInvariant();
        template.Version++;
        template.ContentHash = WhatsAppTemplateText.Hash(template.Name, template.Language, template.Category, template.Components);
        template.Status = WhatsAppTemplateStatuses.Pending; // Meta reviews the edit again
        template.RejectedReason = null;
        template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = template.Status, Reason = "Editada", Source = "edit" });
        TrimHistory(template);
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = user;
        await _store.SaveAsync(template, ct);
        await SnapshotAsync(template, "edit", user, ct);
        return new(true, Template: template);
    }

    public async Task<TemplateOperationResult> DeleteAsync(string tenantId, string templateId, string user, CancellationToken ct = default)
    {
        var template = await _store.GetAsync(tenantId, templateId, ct);
        if (template is null) return new(false, "Plantilla no encontrada.");

        if (template.MetaTemplateId is not null && template.Status != WhatsAppTemplateStatuses.Deleted)
        {
            var account = await ResolveAccountAsync(tenantId, template.ConnectionId, ct);
            if (account is null) return new(false, "La conexion de Meta de esta plantilla ya no existe.");
            try
            {
                await _meta.DeleteAsync(account, template.Name, template.MetaTemplateId, ct);
            }
            catch (MetaApiException ex)
            {
                return new(false, ex.MetaMessage ?? ex.Message);
            }
        }

        template.Status = WhatsAppTemplateStatuses.Deleted;
        template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = WhatsAppTemplateStatuses.Deleted, Reason = "Eliminada desde AgentFlow", Source = "delete" });
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = user;
        await _store.SaveAsync(template, ct);
        return new(true, Template: template);
    }

    public async Task<TemplateOperationResult> UpdateParametersAsync(string tenantId, string templateId, IReadOnlyList<WhatsAppTemplateParameter> parameters, string user, CancellationToken ct = default)
    {
        var template = await _store.GetAsync(tenantId, templateId, ct);
        if (template is null) return new(false, "Plantilla no encontrada.");

        var slots = WhatsAppTemplateText.Slots(template.Components).ToHashSet();
        foreach (var p in parameters)
        {
            if (!slots.Contains((p.Component, p.Key))) return new(false, $"La plantilla no tiene el placeholder {p.Component} {{{{{p.Key}}}}}.");
            if (p.Source == "field" && !WhatsAppTemplateText.Fields.Any(f => f.Key.Equals(p.Field, StringComparison.OrdinalIgnoreCase)))
                return new(false, $"Campo desconocido: {p.Field}.");
            if (p.Source is not ("field" or "static")) return new(false, "La fuente debe ser field o static.");
        }

        template.Parameters = WhatsAppTemplateText.MergeParameters(template.Components, parameters.Concat(template.Parameters).GroupBy(p => (p.Component, p.Key)).Select(g => g.First()));
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = user;
        await _store.SaveAsync(template, ct);
        return new(true, Template: template);
    }

    public async Task<bool> ApplyWebhookAsync(string tenantId, string metaTemplateId, string? status, string? reason, string? quality, string? category, CancellationToken ct = default)
    {
        var template = await _store.FindByMetaIdAsync(tenantId, metaTemplateId, ct);
        if (template is null) return false;

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals(template.Status, StringComparison.OrdinalIgnoreCase))
        {
            template.Status = status.ToUpperInvariant();
            template.RejectedReason = template.Status == WhatsAppTemplateStatuses.Rejected ? reason : null;
            template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = template.Status, Reason = reason, Source = "webhook", At = now });
            TrimHistory(template);
        }
        if (!string.IsNullOrWhiteSpace(quality)) template.QualityScore = quality.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(category)) template.Category = category.ToUpperInvariant();
        template.UpdatedAt = now;
        await _store.SaveAsync(template, ct);
        return true;
    }

    // ------------------------------------------------------------------

    private static void Apply(WhatsAppTemplateDocument local, MetaTemplate r, List<WhatsAppTemplateComponent> components, string hash)
    {
        local.MetaTemplateId = r.Id;
        local.Name = r.Name;
        local.Language = r.Language;
        local.Category = r.Category;
        local.Status = r.Status;
        local.QualityScore = r.QualityScore;
        local.RejectedReason = r.RejectedReason;
        local.ParameterFormat = r.ParameterFormat ?? "POSITIONAL";
        local.Components = components;
        local.Parameters = WhatsAppTemplateText.MergeParameters(components, local.Parameters);
        local.ContentHash = hash;
        local.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task SnapshotAsync(WhatsAppTemplateDocument t, string source, string user, CancellationToken ct)
        => await _store.AddVersionAsync(new WhatsAppTemplateVersionDocument
        {
            TenantId = t.TenantId,
            TemplateId = t.Id,
            Version = t.Version,
            Category = t.Category,
            Status = t.Status,
            Components = t.Components,
            ContentHash = t.ContentHash,
            Source = source,
            CreatedBy = user
        }, ct);

    private static void TrimHistory(WhatsAppTemplateDocument t)
    {
        if (t.StatusHistory.Count > 30) t.StatusHistory = t.StatusHistory.TakeLast(30).ToList();
    }

    private async Task<MetaWhatsAppAccount?> ResolveAccountAsync(string tenantId, string? connectionId, CancellationToken ct)
        => string.IsNullOrWhiteSpace(connectionId)
            ? (await _accounts.GetAllAsync(tenantId, ct)).FirstOrDefault()
            : await _accounts.GetAsync(tenantId, connectionId, ct);

    private static List<WhatsAppTemplateComponent> ToComponents(TemplateDraft draft)
    {
        var components = new List<WhatsAppTemplateComponent>();
        if (!string.IsNullOrWhiteSpace(draft.HeaderText))
            components.Add(new WhatsAppTemplateComponent { Type = "HEADER", Format = "TEXT", Text = draft.HeaderText.Trim() });
        components.Add(new WhatsAppTemplateComponent { Type = "BODY", Text = draft.Body.Trim() });
        if (!string.IsNullOrWhiteSpace(draft.Footer))
            components.Add(new WhatsAppTemplateComponent { Type = "FOOTER", Text = draft.Footer.Trim() });
        var buttons = draft.Buttons?.Where(b => !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (buttons is { Count: > 0 })
            components.Add(new WhatsAppTemplateComponent { Type = "BUTTONS", Buttons = buttons });
        return components;
    }

    /// <summary>Meta's rules, checked before calling it so the person gets a clear message.</summary>
    public static string? Validate(TemplateDraft draft)
    {
        if (!ValidName().IsMatch(draft.Name ?? string.Empty))
            return "El nombre solo puede tener minusculas, numeros y guion bajo (por ejemplo recordatorio_pago).";
        if (string.IsNullOrWhiteSpace(draft.Body)) return "El cuerpo del mensaje es obligatorio.";
        if (draft.Body.Length > 1024) return "El cuerpo puede tener hasta 1024 caracteres.";
        if (draft.HeaderText?.Length > 60) return "El encabezado puede tener hasta 60 caracteres.";
        if (draft.Footer?.Length > 60) return "El pie puede tener hasta 60 caracteres.";
        if (draft.Category.ToUpperInvariant() is not ("UTILITY" or "MARKETING" or "AUTHENTICATION"))
            return "La categoria debe ser UTILITY, MARKETING o AUTHENTICATION.";
        if (WhatsAppTemplateText.PlaceholdersIn(draft.HeaderText).Count > 1) return "El encabezado admite una sola variable.";
        var body = draft.Body.Trim();
        if (body.StartsWith("{{") || body.EndsWith("}}"))
            return "Meta no acepta que el cuerpo empiece o termine con una variable.";
        if (draft.Buttons is { Count: > 10 }) return "Maximo 10 botones.";
        return null;
    }
}
