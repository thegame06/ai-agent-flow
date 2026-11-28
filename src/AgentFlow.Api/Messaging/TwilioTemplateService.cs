using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Messaging;

/// <summary>
/// WhatsApp templates of the tenant's Twilio accounts (Twilio Content templates), kept in the same
/// catalog as Meta's so campaigns and follow-ups pick them the same way. Twilio holds the template
/// and asks WhatsApp to approve it; AgentFlow keeps the mapping of each variable to a field.
/// </summary>
public interface ITwilioTemplateService
{
    /// <param name="channelId">Only the Twilio account of this channel; null syncs every Twilio channel's account.</param>
    Task<TemplateSyncResult> SyncAsync(string tenantId, string? channelId, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> CreateAsync(string tenantId, ChannelDefinition channel, TemplateDraft draft, string user, CancellationToken ct = default);
    Task<TemplateOperationResult> DeleteAsync(string tenantId, WhatsAppTemplateDocument template, string user, CancellationToken ct = default);
}

public sealed class TwilioTemplateService : ITwilioTemplateService
{
    private readonly IWhatsAppTemplateStore _store;
    private readonly IChannelDefinitionRepository _channels;
    private readonly ITwilioCredentials _credentials;
    private readonly ITwilioContentApi _twilio;

    public TwilioTemplateService(IWhatsAppTemplateStore store, IChannelDefinitionRepository channels, ITwilioCredentials credentials, ITwilioContentApi twilio)
    {
        _store = store;
        _channels = channels;
        _credentials = credentials;
        _twilio = twilio;
    }

    public async Task<TemplateSyncResult> SyncAsync(string tenantId, string? channelId, string user, CancellationToken ct = default)
    {
        var channels = (await _channels.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct))
            .Where(TwilioWhatsApp.IsTwilio)
            .Where(c => channelId is null || c.Id == channelId)
            .ToList();
        if (channels.Count == 0)
            return new TemplateSyncResult { Errors = channelId is null ? [] : ["El canal no es de Twilio."] };

        // Templates belong to the Twilio account: several channels of one account sync it once.
        var accounts = new Dictionary<string, (TwilioAccount Account, string? ConnectionId)>();
        var errors = new List<string>();
        foreach (var channel in channels)
        {
            var connectionId = ConnectionOf(channel);
            var account = await _credentials.GetAsync(tenantId, connectionId, ct);
            if (account is null) errors.Add($"{channel.Name}: falta la conexion de Twilio (Account SID y Auth Token).");
            else accounts.TryAdd(account.AccountSid, (account, connectionId));
        }

        var existing = await _store.ListAsync(tenantId, ct);
        int created = 0, updated = 0, statusChanged = 0, unchanged = 0, removed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var (accountSid, (account, connectionId)) in accounts)
        {
            IReadOnlyList<TwilioContent> remote;
            try
            {
                remote = await _twilio.ListAsync(account, ct);
            }
            catch (Exception ex) when (ex is TwilioApiException or HttpRequestException)
            {
                errors.Add($"Twilio: {ex.Message}");
                continue;
            }

            var mine = existing.Where(t => t.Provider == WhatsAppTemplateProviders.Twilio && t.BusinessAccountId == accountSid).ToList();
            var seen = new HashSet<string>();
            foreach (var r in remote.Where(r => !string.IsNullOrWhiteSpace(r.Sid) && !string.IsNullOrWhiteSpace(r.Body)))
            {
                var components = ComponentsOf(r.Body!, r.QuickReplies);
                var name = r.ApprovalName ?? r.FriendlyName;
                var hash = WhatsAppTemplateText.Hash(name, r.Language, r.Category, components);
                var local = mine.FirstOrDefault(t => t.ContentSid == r.Sid);
                if (local is null)
                {
                    local = new WhatsAppTemplateDocument
                    {
                        TenantId = tenantId,
                        Provider = WhatsAppTemplateProviders.Twilio,
                        ConnectionId = connectionId ?? string.Empty,
                        BusinessAccountId = accountSid,
                        ContentSid = r.Sid,
                        Origin = "twilio",
                        CreatedAt = now
                    };
                    Apply(local, r, name, components, hash);
                    foreach (var p in local.Parameters)
                        if (r.Examples.TryGetValue(p.Key, out var sample)) p.Example ??= sample;
                    local.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = r.Status, Reason = r.RejectionReason, At = now });
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
                        Apply(local, r, name, components, hash);
                        local.Version++;
                    }
                    else
                    {
                        local.Status = r.Status;
                        local.RejectedReason = r.RejectionReason;
                    }
                    if (statusDiff)
                        local.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = r.Status, Reason = r.RejectionReason, At = now });
                    if (local.StatusHistory.Count > 50) local.StatusHistory.RemoveRange(0, local.StatusHistory.Count - 50);
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

            // Gone from Twilio: kept for history (campaigns may point to it) but marked deleted.
            foreach (var gone in mine.Where(t => !seen.Contains(t.Id) && t.Status != WhatsAppTemplateStatuses.Deleted))
            {
                gone.Status = WhatsAppTemplateStatuses.Deleted;
                gone.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = WhatsAppTemplateStatuses.Deleted, Reason = "Ya no existe en Twilio", At = now });
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

    public async Task<TemplateOperationResult> CreateAsync(string tenantId, ChannelDefinition channel, TemplateDraft draft, string user, CancellationToken ct = default)
    {
        var error = WhatsAppTemplateService.Validate(draft);
        if (error is not null) return new(false, error);
        if (draft.Buttons?.Any(b => !b.Type.Equals("QUICK_REPLY", StringComparison.OrdinalIgnoreCase)) == true)
            return new(false, "Por ahora las plantillas de Twilio solo admiten botones de respuesta rapida.");

        var connectionId = ConnectionOf(channel);
        var account = await _credentials.GetAsync(tenantId, connectionId, ct);
        if (account is null) return new(false, "El canal de Twilio no tiene su conexion (Account SID y Auth Token). Configurala en Canales.");

        var existing = await _store.ListAsync(tenantId, ct);
        if (existing.Any(t => t.Provider == WhatsAppTemplateProviders.Twilio && t.BusinessAccountId == account.AccountSid &&
                              t.Name == draft.Name && t.Language == draft.Language && t.Status != WhatsAppTemplateStatuses.Deleted))
            return new(false, $"Ya existe la plantilla {draft.Name} en {draft.Language}.");

        // Twilio has one text: header and footer go into it. Its variables are numbered {{1}}..{{n}}.
        var text = string.Join("\n\n", new[] { draft.HeaderText is { Length: > 0 } h ? $"*{h.Trim()}*" : null, draft.Body.Trim(), draft.Footer?.Trim() }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        var replies = draft.Buttons?.Select(b => b.Text.Trim()).Where(t => t.Length > 0).ToList() ?? [];
        var (positional, parameters) = WhatsAppTemplateText.ToPositional([new WhatsAppTemplateComponent { Type = "BODY", Text = text }]);
        var body = positional[0].Text!;
        var components = ComponentsOf(body, replies);
        var examples = parameters.ToDictionary(p => p.Key, p => p.Example ?? p.Key);

        var template = new WhatsAppTemplateDocument
        {
            TenantId = tenantId,
            Provider = WhatsAppTemplateProviders.Twilio,
            ConnectionId = connectionId ?? string.Empty,
            BusinessAccountId = account.AccountSid,
            Name = draft.Name,
            Language = draft.Language,
            Category = draft.Category.ToUpperInvariant(),
            ParameterFormat = "POSITIONAL",
            Components = components,
            Parameters = WhatsAppTemplateText.MergeParameters(components, parameters),
            Origin = "agentflow",
            UpdatedBy = user
        };

        try
        {
            template.ContentSid = await _twilio.CreateAsync(account, draft.Name, draft.Language, body, replies, examples, ct);
        }
        catch (Exception ex) when (ex is TwilioApiException or HttpRequestException)
        {
            return new(false, $"Twilio no creo la plantilla: {ex.Message}");
        }

        // Created in Twilio; WhatsApp still has to approve it. A failed submission (for example a
        // sandbox number, which cannot have its own templates) leaves it created but unsubmitted.
        try
        {
            template.Status = await _twilio.SubmitForWhatsAppAsync(account, template.ContentSid, draft.Name, template.Category, ct);
        }
        catch (Exception ex) when (ex is TwilioApiException or HttpRequestException)
        {
            template.Status = WhatsAppTemplateStatuses.Unsubmitted;
            template.RejectedReason = $"No se pudo enviar a revision de WhatsApp: {ex.Message}";
        }

        template.ContentHash = WhatsAppTemplateText.Hash(template.Name, template.Language, template.Category, template.Components);
        template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = template.Status, Reason = template.RejectedReason, Source = "create" });
        await _store.SaveAsync(template, ct);
        await SnapshotAsync(template, "create", user, ct);
        return new(true, Template: template);
    }

    public async Task<TemplateOperationResult> DeleteAsync(string tenantId, WhatsAppTemplateDocument template, string user, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(template.ContentSid) && template.Status != WhatsAppTemplateStatuses.Deleted)
        {
            var account = await _credentials.GetAsync(tenantId, string.IsNullOrWhiteSpace(template.ConnectionId) ? null : template.ConnectionId, ct);
            if (account is null) return new(false, "No hay conexion de Twilio para borrar la plantilla.");
            try
            {
                await _twilio.DeleteAsync(account, template.ContentSid!, ct);
            }
            catch (TwilioApiException ex) when (ex.Status != 404)
            {
                return new(false, $"Twilio no borro la plantilla: {ex.Message}");
            }
        }
        template.Status = WhatsAppTemplateStatuses.Deleted;
        template.StatusHistory.Add(new WhatsAppTemplateStatusChange { Status = WhatsAppTemplateStatuses.Deleted, Reason = "Borrada", Source = "delete" });
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = user;
        await _store.SaveAsync(template, ct);
        return new(true, Template: template);
    }

    /// <summary>The Twilio connection of a channel (config ConnectionId), or null for the tenant's default one.</summary>
    public static string? ConnectionOf(ChannelDefinition channel)
        => channel.Config.GetValueOrDefault("ConnectionId") is { Length: > 0 } id ? id : null;

    public static List<WhatsAppTemplateComponent> ComponentsOf(string body, IReadOnlyList<string> quickReplies)
    {
        var components = new List<WhatsAppTemplateComponent> { new() { Type = "BODY", Text = body } };
        if (quickReplies.Count > 0)
            components.Add(new WhatsAppTemplateComponent
            {
                Type = "BUTTONS",
                Buttons = quickReplies.Select(t => new WhatsAppTemplateButton { Type = "QUICK_REPLY", Text = t }).ToList()
            });
        return components;
    }

    private static void Apply(WhatsAppTemplateDocument local, TwilioContent r, string name, List<WhatsAppTemplateComponent> components, string hash)
    {
        local.Name = name;
        local.Language = r.Language;
        local.Category = r.Category;
        local.Status = r.Status;
        local.RejectedReason = r.RejectionReason;
        local.ParameterFormat = "POSITIONAL";
        local.Components = components;
        local.Parameters = WhatsAppTemplateText.MergeParameters(components, local.Parameters);
        local.ContentHash = hash;
    }

    private Task SnapshotAsync(WhatsAppTemplateDocument template, string source, string user, CancellationToken ct)
        => _store.AddVersionAsync(new WhatsAppTemplateVersionDocument
        {
            TenantId = template.TenantId,
            TemplateId = template.Id,
            Version = template.Version,
            Category = template.Category,
            Status = template.Status,
            Components = template.Components,
            ContentHash = template.ContentHash,
            Source = source,
            CreatedBy = user
        }, ct);
}
