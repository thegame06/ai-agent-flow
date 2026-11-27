using System.Text.Json;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Microsoft.AspNetCore.DataProtection;

namespace AgentFlow.Api.Connect;

/// <summary>The Meta (WhatsApp Business) account behind one WhatsApp channel, without its secrets.</summary>
public sealed record WhatsAppChannelMetaView
{
    public string ChannelId { get; init; } = string.Empty;
    public string ChannelName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ConnectionId { get; init; }
    public string? BusinessAccountId { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? AppId { get; init; }
    public bool HasToken { get; init; }
    public bool HasAppSecret { get; init; }
    /// <summary>An old channel that still keeps its token in plain config: saving moves it to the encrypted secret.</summary>
    public bool TokenInPlainConfig { get; init; }
}

public sealed record WhatsAppChannelMetaRequest
{
    public string? BusinessAccountId { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? AppId { get; init; }
    /// <summary>Only when it changes; empty keeps the stored one.</summary>
    public string? AccessToken { get; init; }
    /// <summary>Only when it changes; empty keeps the stored one.</summary>
    public string? AppSecret { get; init; }
}

public interface IWhatsAppChannelMetaService
{
    Task<IReadOnlyList<WhatsAppChannelMetaView>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<WhatsAppChannelMetaView?> GetAsync(string tenantId, string channelId, CancellationToken ct = default);
    Task<(WhatsAppChannelMetaView? View, string? Error)> SaveAsync(string tenantId, string channelId, WhatsAppChannelMetaRequest request, string user, CancellationToken ct = default);
}

/// <summary>
/// A WhatsApp channel on the Business API points to a tenant connection that holds its Meta account:
/// ids in the connection config, access token and app secret in the encrypted secret. The channel
/// itself never keeps the token. A company can have several channels (collections, sales,
/// marketing), from the same Meta account or from different ones.
/// </summary>
public sealed class WhatsAppChannelMetaService : IWhatsAppChannelMetaService
{
    private const string Protector = "tenant-connections-secrets-v1";
    private static readonly string[] PlainSecretKeys = ["ApiToken", "AccessToken", "apiToken", "accessToken"];

    private readonly IChannelDefinitionRepository _channels;
    private readonly ITenantConnectionStore _connections;
    private readonly IDataProtectionProvider _dataProtection;

    public WhatsAppChannelMetaService(IChannelDefinitionRepository channels, ITenantConnectionStore connections, IDataProtectionProvider dataProtection)
    {
        _channels = channels;
        _connections = connections;
        _dataProtection = dataProtection;
    }

    public async Task<IReadOnlyList<WhatsAppChannelMetaView>> ListAsync(string tenantId, CancellationToken ct = default)
    {
        var views = new List<WhatsAppChannelMetaView>();
        foreach (var channel in await _channels.GetByTypeAsync(ChannelType.WhatsApp, tenantId, ct))
            if (IsBusiness(channel))
                views.Add(await ViewAsync(tenantId, channel, ct));
        return views;
    }

    public async Task<WhatsAppChannelMetaView?> GetAsync(string tenantId, string channelId, CancellationToken ct = default)
    {
        var channel = await _channels.GetByIdAsync(channelId, tenantId, ct);
        return channel is null || channel.Type != ChannelType.WhatsApp ? null : await ViewAsync(tenantId, channel, ct);
    }

    public async Task<(WhatsAppChannelMetaView? View, string? Error)> SaveAsync(string tenantId, string channelId, WhatsAppChannelMetaRequest request, string user, CancellationToken ct = default)
    {
        var channel = await _channels.GetByIdAsync(channelId, tenantId, ct);
        if (channel is null || channel.Type != ChannelType.WhatsApp) return (null, "Canal de WhatsApp no encontrado.");
        if (string.IsNullOrWhiteSpace(request.BusinessAccountId)) return (null, "Falta el id de la cuenta de WhatsApp Business (WABA).");
        if (string.IsNullOrWhiteSpace(request.PhoneNumberId)) return (null, "Falta el id del numero de telefono.");

        var connectionId = MetaConnectionOf(channel) ?? $"meta-{channel.Id}";
        var existing = await _connections.GetConnectionAsync(tenantId, connectionId, ct);
        var now = DateTimeOffset.UtcNow;

        // Secret: keep what is stored and replace only what was typed. An old channel's plain token moves here.
        var secret = existing is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : await ReadSecretAsync(tenantId, connectionId, ct);
        var plainToken = PlainSecretKeys.Select(k => channel.Config.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (!string.IsNullOrWhiteSpace(request.AccessToken)) secret["apiToken"] = request.AccessToken.Trim();
        else if (!secret.ContainsKey("apiToken") && plainToken is not null) secret["apiToken"] = plainToken;
        if (!string.IsNullOrWhiteSpace(request.AppSecret)) secret["appSecret"] = request.AppSecret.Trim();
        if (!secret.TryGetValue("apiToken", out var token) || string.IsNullOrWhiteSpace(token))
            return (null, "Falta el token de acceso de Meta.");

        var config = new Dictionary<string, string>
        {
            ["provider"] = "meta",
            ["businessAccountId"] = request.BusinessAccountId.Trim(),
            ["phoneNumberId"] = request.PhoneNumberId.Trim()
        };
        if (!string.IsNullOrWhiteSpace(request.AppId)) config["appId"] = request.AppId.Trim();

        await _connections.UpsertConnectionAsync(new TenantConnectionContract
        {
            Id = connectionId,
            TenantId = tenantId,
            Name = $"WhatsApp {channel.Name}",
            Type = TenantConnectionType.Messaging,
            ConnectorId = "whatsapp-business",
            Config = config,
            AllowedAgentIds = existing?.AllowedAgentIds ?? [],
            AllowedNodeIds = existing?.AllowedNodeIds ?? [],
            AllowedConnectorIds = existing?.AllowedConnectorIds ?? [],
            SecretVersion = (existing?.SecretVersion ?? 0) + 1,
            SecretRotatedAt = now,
            SecretExpiresAt = existing?.SecretExpiresAt,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = user
        }, ct);
        await _connections.UpsertSecretAsync(new TenantConnectionSecretContract
        {
            ConnectionId = connectionId,
            TenantId = tenantId,
            CipherText = _dataProtection.CreateProtector(Protector).Protect(JsonSerializer.Serialize(secret)),
            Version = (existing?.SecretVersion ?? 0) + 1,
            RotatedAt = now,
            RotatedBy = user
        }, ct);

        // The channel sends through the Meta provider with this connection; no token in its config.
        var channelConfig = new Dictionary<string, string>(channel.Config);
        foreach (var key in PlainSecretKeys) channelConfig.Remove(key);
        channelConfig["AuthMode"] = "business";
        channelConfig["Provider"] = "meta";
        channelConfig["ConnectionId"] = connectionId;
        channelConfig["PhoneNumberId"] = config["phoneNumberId"];
        channelConfig["BusinessAccountId"] = config["businessAccountId"];
        channel.UpdateConfig(channelConfig);
        await _channels.UpdateAsync(channel, ct);

        return (await ViewAsync(tenantId, channel, ct), null);
    }

    public static bool IsBusiness(ChannelDefinition channel)
        => !string.Equals(channel.Config.GetValueOrDefault("AuthMode", "qr"), "qr", StringComparison.OrdinalIgnoreCase);

    private static string? MetaConnectionOf(ChannelDefinition channel)
        => channel.Config.GetValueOrDefault("ConnectionId") is { Length: > 0 } id &&
           (channel.Config.GetValueOrDefault("Provider") is null or "meta" || id.StartsWith("meta-", StringComparison.Ordinal))
            ? id
            : null;

    private async Task<WhatsAppChannelMetaView> ViewAsync(string tenantId, ChannelDefinition channel, CancellationToken ct)
    {
        var connectionId = MetaConnectionOf(channel);
        var connection = connectionId is null ? null : await _connections.GetConnectionAsync(tenantId, connectionId, ct);
        var secret = connection is null ? null : await ReadSecretAsync(tenantId, connection.Id, ct);
        return new WhatsAppChannelMetaView
        {
            ChannelId = channel.Id,
            ChannelName = channel.Name,
            Status = channel.Status.ToString(),
            ConnectionId = connection?.Id,
            BusinessAccountId = connection?.Config.GetValueOrDefault("businessAccountId") ?? channel.Config.GetValueOrDefault("BusinessAccountId"),
            PhoneNumberId = connection?.Config.GetValueOrDefault("phoneNumberId") ?? channel.Config.GetValueOrDefault("PhoneNumberId"),
            AppId = connection?.Config.GetValueOrDefault("appId"),
            HasToken = secret?.ContainsKey("apiToken") == true,
            HasAppSecret = secret?.ContainsKey("appSecret") == true,
            TokenInPlainConfig = PlainSecretKeys.Any(k => !string.IsNullOrWhiteSpace(channel.Config.GetValueOrDefault(k)))
        };
    }

    private async Task<Dictionary<string, string>> ReadSecretAsync(string tenantId, string connectionId, CancellationToken ct)
    {
        var stored = await _connections.GetSecretAsync(tenantId, connectionId, ct);
        if (stored is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plain = _dataProtection.CreateProtector(Protector).Unprotect(stored.CipherText);
        var values = plain.TrimStart().StartsWith('{')
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? []
            : new Dictionary<string, string> { ["apiToken"] = plain };
        return new Dictionary<string, string>(values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)), StringComparer.OrdinalIgnoreCase);
    }
}
