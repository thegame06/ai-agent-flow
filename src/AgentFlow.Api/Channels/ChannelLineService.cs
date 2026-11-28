using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api.Channels;

/// <summary>What a line is: its number, purpose, whether it is the default one and its contact hours.</summary>
public sealed record ChannelLineSettings
{
    /// <summary>Business number in E.164 (+525512345678); empty clears it (the connection's number is used).</summary>
    public string? PhoneNumber { get; init; }
    /// <summary>collections, sales, marketing or general.</summary>
    public string? Purpose { get; init; }
    public bool? DefaultLine { get; init; }
    /// <summary>"08:00-21:00"; empty means any time.</summary>
    public string? ContactHours { get; init; }
    public string? TimeZone { get; init; }
}

public sealed record ChannelLineView(
    string ChannelId,
    string Name,
    string Type,
    string? PhoneNumber,
    string? PhoneNumberId,
    string Purpose,
    bool DefaultLine,
    string? ContactHours,
    string TimeZone,
    string? AssistantId,
    bool Active);

/// <summary>
/// A tenant's lines (several numbers per channel type, each with its own purpose and assistant) and
/// the rule that keeps them apart: a number belongs to one line of a kind, in its tenant and on the
/// whole platform, so inbound traffic for it can only reach one tenant and one assistant.
/// </summary>
public sealed class ChannelLineService
{
    private readonly IChannelDefinitionRepository _channels;

    public ChannelLineService(IChannelDefinitionRepository channels) => _channels = channels;

    public async Task<IReadOnlyList<ChannelLineView>> ListAsync(string tenantId, CancellationToken ct)
        => (await _channels.GetAllAsync(tenantId, ct))
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Type).ThenByDescending(ChannelLines.IsDefault).ThenBy(x => x.Name)
            .Select(View)
            .ToList();

    public async Task<(ChannelLineView? Line, string? Error)> ApplyAsync(string tenantId, string channelId, ChannelLineSettings settings, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(channelId, tenantId, ct);
        if (channel is null || channel.TenantId != tenantId) return (null, "Canal no encontrado.");

        var config = new Dictionary<string, string>(channel.Config, StringComparer.OrdinalIgnoreCase);
        if (settings.PhoneNumber is not null)
        {
            var digits = ChannelLines.Digits(settings.PhoneNumber);
            if (digits.Length == 0)
            {
                config.Remove(ChannelLines.PhoneNumberKey);
                config.Remove(PhoneDigitsKey);
            }
            else if (digits.Length is < 8 or > 15)
                return (null, "El número debe ir con código de país, por ejemplo +525512345678.");
            else
            {
                config[ChannelLines.PhoneNumberKey] = "+" + digits;
                config[PhoneDigitsKey] = digits;
            }
        }
        if (settings.Purpose is not null)
        {
            var purpose = settings.Purpose.Trim();
            if (purpose.Length > 0 && ChannelLines.NormalizePurpose(purpose) == ChannelLines.Purposes.General &&
                !string.Equals(purpose, ChannelLines.Purposes.General, StringComparison.OrdinalIgnoreCase))
                return (null, "El propósito debe ser cobranza, ventas, marketing o general.");
            config[ChannelLines.PurposeKey] = ChannelLines.NormalizePurpose(purpose);
        }
        if (settings.ContactHours is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.ContactHours)) config.Remove(ChannelLines.ContactHoursKey);
            else if (!ChannelLines.TryParseHours(settings.ContactHours, out var from, out var to))
                return (null, "El horario debe ser como 08:00-21:00.");
            else config[ChannelLines.ContactHoursKey] = $"{from:HH\\:mm}-{to:HH\\:mm}";
        }
        if (settings.TimeZone is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.TimeZone)) config.Remove(ChannelLines.TimeZoneKey);
            else if (!IsTimeZone(settings.TimeZone.Trim())) return (null, $"No reconozco la zona horaria {settings.TimeZone}.");
            else config[ChannelLines.TimeZoneKey] = settings.TimeZone.Trim();
        }
        if (settings.DefaultLine is { } isDefault)
            config[ChannelLines.DefaultLineKey] = isDefault ? "true" : "false";

        var conflict = await NumberConflictAsync(_channels, tenantId, channel.Id, channel.Type,
            config.GetValueOrDefault(ChannelLines.PhoneNumberKey), config.GetValueOrDefault(ChannelLines.PhoneNumberIdKey), ct);
        if (conflict is not null) return (null, conflict);

        channel.UpdateConfig(config);
        var saved = await _channels.UpdateAsync(channel, ct);
        if (!saved.IsSuccess) return (null, saved.Error?.Message ?? "No se pudo guardar la línea.");

        // One default line per kind: choosing this one releases the others.
        if (settings.DefaultLine == true)
        {
            foreach (var other in await _channels.GetAllAsync(tenantId, ct))
            {
                if (other.Id == channel.Id || !SameKind(other.Type, channel.Type) || !ChannelLines.IsDefault(other)) continue;
                var otherConfig = new Dictionary<string, string>(other.Config, StringComparer.OrdinalIgnoreCase) { [ChannelLines.DefaultLineKey] = "false" };
                other.UpdateConfig(otherConfig);
                await _channels.UpdateAsync(other, ct);
            }
        }
        return (View(channel), null);
    }

    /// <summary>
    /// Why this number (or Meta number id) cannot be the line's, or null. Another line of the tenant
    /// is named; another tenant is not (its name is not ours to show).
    /// </summary>
    public static async Task<string?> NumberConflictAsync(IChannelDefinitionRepository channels, string tenantId, string? channelId,
        ChannelType type, string? number, string? phoneNumberId, CancellationToken ct)
    {
        var digits = ChannelLines.Digits(number);
        var numberId = string.IsNullOrWhiteSpace(phoneNumberId) ? null : phoneNumberId.Trim();
        if (digits.Length < 7 && numberId is null) return null;

        bool Same(ChannelDefinition other) =>
            other.Id != channelId && SameKind(other.Type, type) &&
            ((digits.Length >= 7 && ChannelLines.Digits(ChannelLines.NumberOf(other)) == digits) ||
             (numberId is not null && string.Equals(ChannelLines.NumberIdOf(other), numberId, StringComparison.Ordinal)));

        var mine = ((await channels.GetAllAsync(tenantId, ct)) ?? []).FirstOrDefault(x => x.TenantId == tenantId && Same(x));
        if (mine is not null) return $"Ese número ya es de la línea «{mine.Name}».";

        var platform = await FindAcrossTenantsAsync(channels, digits.Length >= 7 ? digits : null, numberId, ct);
        if (platform.Any(x => x.TenantId != tenantId && Same(x)))
            return "Ese número ya está registrado en otra cuenta de AgentFlow. Un número solo puede pertenecer a una cuenta.";
        return null;
    }

    /// <summary>
    /// The line (of any tenant) that owns this number or Meta number id: how a webhook shared by every
    /// tenant (one Meta app, one Twilio account with subaccounts) finds whose traffic it is. Null when
    /// no line, or more than one, claims it.
    /// </summary>
    public async Task<ChannelDefinition?> FindOwnerAsync(string? number, string? phoneNumberId, Func<ChannelType, bool> kind, CancellationToken ct)
    {
        var digits = ChannelLines.Digits(number);
        var numberId = string.IsNullOrWhiteSpace(phoneNumberId) ? null : phoneNumberId.Trim();
        if (digits.Length < 7 && numberId is null) return null;
        var owners = (await FindAcrossTenantsAsync(_channels, digits.Length >= 7 ? digits : null, numberId, ct))
            .Where(x => kind(x.Type) && x.Status == ChannelStatus.Active &&
                        ((digits.Length >= 7 && ChannelLines.Digits(ChannelLines.NumberOf(x)) == digits) ||
                         (numberId is not null && ChannelLines.NumberIdOf(x) == numberId)))
            .ToList();
        // Two tenants claiming one number must never guess: nobody gets the traffic.
        return owners.Select(x => x.TenantId).Distinct().Count() == 1 ? owners[0] : null;
    }

    public const string PhoneDigitsKey = "PhoneNumberDigits";

    /// <summary>The Meta WhatsApp Business account (WABA) id: template events come per account, not per number.</summary>
    public async Task<ChannelDefinition?> FindOwnerOfMetaAccountAsync(string? businessAccountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(businessAccountId)) return null;
        var owners = (await _channels.FindAcrossTenantsByConfigAsync("BusinessAccountId", businessAccountId.Trim(), ct))
            .Where(x => x.Type == ChannelType.WhatsApp && x.Status == ChannelStatus.Active)
            .ToList();
        return owners.Select(x => x.TenantId).Distinct().Count() == 1 ? owners[0] : null;
    }

    /// <summary>Lines of any tenant with this number (as saved by AgentFlow or typed with "+") or Meta number id.</summary>
    private static async Task<IReadOnlyList<ChannelDefinition>> FindAcrossTenantsAsync(IChannelDefinitionRepository channels,
        string? digits, string? numberId, CancellationToken ct)
    {
        var found = new Dictionary<string, ChannelDefinition>(StringComparer.Ordinal);
        if (digits is not null)
        {
            foreach (var x in await channels.FindAcrossTenantsByConfigAsync(PhoneDigitsKey, digits, ct) ?? []) found[x.Id] = x;
            foreach (var x in await channels.FindAcrossTenantsByConfigAsync(ChannelLines.PhoneNumberKey, "+" + digits, ct) ?? []) found[x.Id] = x;
        }
        if (numberId is not null)
            foreach (var x in await channels.FindAcrossTenantsByConfigAsync(ChannelLines.PhoneNumberIdKey, numberId, ct) ?? []) found[x.Id] = x;
        return found.Values.ToList();
    }

    public static ChannelLineView View(ChannelDefinition x) => new(
        x.Id, x.Name, x.Type.ToString(), ChannelLines.NumberOf(x), ChannelLines.NumberIdOf(x), ChannelLines.PurposeOf(x),
        ChannelLines.IsDefault(x), x.Config.GetValueOrDefault(ChannelLines.ContactHoursKey),
        x.Config.GetValueOrDefault(ChannelLines.TimeZoneKey) ?? ChannelLines.DefaultTimeZone,
        x.Config.GetValueOrDefault("DefaultAgentId"), x.Status == ChannelStatus.Active);

    private static bool SameKind(ChannelType a, ChannelType b) => a == b || (ChannelLines.IsVoice(a) && ChannelLines.IsVoice(b));

    private static bool IsTimeZone(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}
