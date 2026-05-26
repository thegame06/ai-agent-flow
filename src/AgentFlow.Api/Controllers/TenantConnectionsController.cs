using AgentFlow.Abstractions;
using AgentFlow.Api.Connect;
using AgentFlow.Application.Memory;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId}/connections")]
[Authorize]
public sealed class TenantConnectionsController : ControllerBase
{
    private readonly ITenantConnectionStore _store;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IDataProtector _protector;
    private readonly IAuditMemory _audit;
    private readonly IProviderRegistry _providerRegistry;

    public TenantConnectionsController(
        ITenantConnectionStore store,
        ITenantContextAccessor tenantContext,
        IDataProtectionProvider dataProtectionProvider,
        IAuditMemory audit,
        IProviderRegistry providerRegistry)
    {
        _store = store;
        _tenantContext = tenantContext;
        _protector = dataProtectionProvider.CreateProtector("tenant-connections-secrets-v1");
        _audit = audit;
        _providerRegistry = providerRegistry;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var items = await _store.GetConnectionsAsync(tenantId, ct);
        return Ok(items.Select(x => new ConnectionResponse(
            x.Id,
            x.Name,
            x.Type,
            x.ConnectorId,
            x.Config,
            x.AllowedAgentIds,
            x.AllowedNodeIds,
            x.AllowedConnectorIds,
            x.SecretVersion,
            x.SecretRotatedAt,
            x.SecretExpiresAt,
            x.UpdatedAt,
            x.UpdatedBy)));
    }

    [HttpPut("{connectionId}")]
    public async Task<IActionResult> Upsert([FromRoute] string tenantId, [FromRoute] string connectionId, [FromBody] UpsertConnectionRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectManage)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;
        var existing = await _store.GetConnectionAsync(tenantId, connectionId, ct);

        var saved = await _store.UpsertConnectionAsync(new TenantConnectionContract
        {
            Id = connectionId,
            TenantId = tenantId,
            Name = request.Name,
            Type = request.Type,
            ConnectorId = request.ConnectorId,
            Config = request.Config,
            AllowedAgentIds = request.AllowedAgentIds,
            AllowedNodeIds = request.AllowedNodeIds,
            AllowedConnectorIds = request.AllowedConnectorIds,
            SecretVersion = existing?.SecretVersion ?? 1,
            SecretRotatedAt = existing?.SecretRotatedAt,
            SecretExpiresAt = existing?.SecretExpiresAt,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        await RecordAuditAsync(tenantId, connectionId, "connection.upsert", new { request.Name, request.Type }, ct);
        return Ok(saved);
    }

    [HttpPost("{connectionId}/secret")]
    public async Task<IActionResult> RotateSecret([FromRoute] string tenantId, [FromRoute] string connectionId, [FromBody] RotateConnectionSecretRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectSecretRotate)) return Forbid();

        var connection = await _store.GetConnectionAsync(tenantId, connectionId, ct);
        if (connection is null) return NotFound(new { message = "Connection not found." });

        var nextVersion = connection.SecretVersion + 1;
        var now = DateTimeOffset.UtcNow;
        var actor = _tenantContext.Current!.UserId;

        await _store.UpsertSecretAsync(new TenantConnectionSecretContract
        {
            ConnectionId = connectionId,
            TenantId = tenantId,
            CipherText = _protector.Protect(request.Secret),
            Version = nextVersion,
            RotatedAt = now,
            ExpiresAt = request.ExpiresAt,
            RotatedBy = actor
        }, ct);

        var updated = await _store.UpsertConnectionAsync(connection with
        {
            SecretVersion = nextVersion,
            SecretRotatedAt = now,
            SecretExpiresAt = request.ExpiresAt,
            UpdatedAt = now,
            UpdatedBy = actor
        }, ct);

        await RecordAuditAsync(tenantId, connectionId, "connection.secret.rotate", new { nextVersion, request.ExpiresAt }, ct);
        return Ok(new { updated.Id, updated.SecretVersion, updated.SecretRotatedAt, updated.SecretExpiresAt });
    }

    [HttpGet("{connectionId}/health")]
    public async Task<IActionResult> Health([FromRoute] string tenantId, [FromRoute] string connectionId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var connection = await _store.GetConnectionAsync(tenantId, connectionId, ct);
        if (connection is null) return NotFound(new { message = "Connection not found." });

        var secret = await _store.GetSecretAsync(tenantId, connectionId, ct);
        var checks = BuildChecks(connection, secret);

        var status = checks.All(x => x.Status == ConnectionHealthStatus.Healthy)
            ? ConnectionHealthStatus.Healthy
            : checks.Any(x => x.Status == ConnectionHealthStatus.Unhealthy)
                ? ConnectionHealthStatus.Unhealthy
                : ConnectionHealthStatus.Degraded;

        return Ok(new
        {
            connectionId,
            status,
            checkedAt = DateTimeOffset.UtcNow,
            checks
        });
    }

    [HttpGet("resources")]
    public async Task<IActionResult> GetResources([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var resources = new List<object>();
        foreach (var connection in await _store.GetConnectionsAsync(tenantId, ct))
        {
            var secret = await _store.GetSecretAsync(tenantId, connection.Id, ct);
            var checks = BuildChecks(connection, secret);
            var ready = checks.All(check => check.Status == ConnectionHealthStatus.Healthy);
            resources.Add(new
            {
                resourceType = "workflow-connection",
                resourceKey = $"connection:{connection.Id}",
                connection.Id,
                connection.Name,
                connection.Type,
                connection.ConnectorId,
                ready,
                capabilities = CapabilitiesForConnection(connection),
                suggestedNodes = SuggestedNodesForConnection(connection),
                requiredConfigKeys = RequiredConfigKeys(connection),
                secretRequired = SecretRequired(connection),
                policy = new { connection.AllowedAgentIds, connection.AllowedNodeIds, connection.AllowedConnectorIds },
                checks
            });
        }

        return Ok(resources);
    }

    [HttpGet("capability-matrix")]
    public async Task<IActionResult> GetCapabilityMatrix([FromRoute] string tenantId, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var connections = await _store.GetConnectionsAsync(tenantId, ct);
        var providers = _providerRegistry.GetAll()
            .Select(provider => new
            {
                provider = provider.ProviderId,
                capabilities = provider.Capabilities
                    .Select(cap => new
                    {
                        cap.Name,
                        cap.Channel,
                        cap.Description,
                        cap.SupportsStreaming,
                        boundConnections = connections
                            .Where(connection => MatchesProvider(provider.ProviderId, connection))
                            .Select(connection => new
                            {
                                connection.Id,
                                connection.Name,
                                connection.ConnectorId,
                                hasCapabilityHint = CapabilitiesForConnection(connection)
                                    .Contains(cap.Name, StringComparer.OrdinalIgnoreCase)
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .OrderBy(x => x.provider)
            .ToList();

        return Ok(new
        {
            tenantId,
            generatedAt = DateTimeOffset.UtcNow,
            providers
        });
    }

    [HttpPost("resources/{connectionId}/resolve")]
    public async Task<IActionResult> ResolveResource([FromRoute] string tenantId, [FromRoute] string connectionId, [FromBody] ResolveConnectionRequest request, CancellationToken ct)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectUseResource)) return Forbid();

        var connection = await _store.GetConnectionAsync(tenantId, connectionId, ct);
        if (connection is null) return NotFound(new { message = "Connection not found." });

        if (!IsAllowed(connection.AllowedAgentIds, request.AgentId) ||
            !IsAllowed(connection.AllowedNodeIds, request.NodeId) ||
            !IsAllowed(connection.AllowedConnectorIds, request.ConnectorId))
        {
            await RecordUsageAsync(tenantId, connectionId, request, false, "access_denied", "Connection policy denied request.", ct);
            return Forbid();
        }

        var secret = await _store.GetSecretAsync(tenantId, connectionId, ct);
        if (secret is null)
        {
            await RecordUsageAsync(tenantId, connectionId, request, false, "secret_missing", "Connection has no secret configured.", ct);
            return BadRequest(new { message = "Connection secret missing." });
        }

        if (secret.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            await RecordUsageAsync(tenantId, connectionId, request, false, "secret_expired", "Connection secret expired.", ct);
            return BadRequest(new { message = "Connection secret expired. Rotate it before usage." });
        }

        await RecordUsageAsync(tenantId, connectionId, request, true, null, null, ct);

        return Ok(new
        {
            resourceType = "workflow-connection",
            resourceKey = $"connection:{connection.Id}",
            connection.Id,
            connection.Name,
            connection.Type,
            connection.ConnectorId,
            connection.Config,
            secretVersion = secret.Version,
            secret = _protector.Unprotect(secret.CipherText)
        });
    }

    [HttpGet("{connectionId}/audit")]
    public async Task<IActionResult> GetUsageAudit([FromRoute] string tenantId, [FromRoute] string connectionId, [FromQuery] int limit = 200, CancellationToken ct = default)
    {
        if (!CanAccess(tenantId, AgentFlowPermissions.ConnectRead)) return Forbid();

        var rows = await _store.GetUsageAuditAsync(tenantId, connectionId, limit, ct);

        return Ok(new
        {
            connectionId,
            total = rows.Count,
            successCount = rows.Count(x => x.Succeeded),
            failureCount = rows.Count(x => !x.Succeeded),
            rows
        });
    }

    private static IReadOnlyList<ConnectionHealthCheck> BuildChecks(TenantConnectionContract connection, TenantConnectionSecretContract? secret)
    {
        var checks = new List<ConnectionHealthCheck>();

        checks.Add(new ConnectionHealthCheck(
            "config.present",
            connection.Config.Count > 0 ? ConnectionHealthStatus.Healthy : ConnectionHealthStatus.Unhealthy,
            connection.Config.Count > 0 ? "Configuration is present." : "Configuration is empty."));

        var secretRequired = SecretRequired(connection);
        checks.Add(new ConnectionHealthCheck(
            "secret.exists",
            !secretRequired || secret is not null ? ConnectionHealthStatus.Healthy : ConnectionHealthStatus.Unhealthy,
            !secretRequired
                ? "Secret is optional for this connection."
                : secret is null
                    ? "No secret configured."
                    : $"Secret version {secret.Version} found."));

        if (secret is not null)
        {
            var expiryStatus = secret.ExpiresAt switch
            {
                null => ConnectionHealthStatus.Healthy,
                var exp when exp <= DateTimeOffset.UtcNow => ConnectionHealthStatus.Unhealthy,
                var exp when exp <= DateTimeOffset.UtcNow.AddDays(3) => ConnectionHealthStatus.Degraded,
                _ => ConnectionHealthStatus.Healthy
            };

            checks.Add(new ConnectionHealthCheck(
                "secret.expiry",
                expiryStatus,
                secret.ExpiresAt is null
                    ? "Secret has no expiration date."
                    : $"Secret expires at {secret.ExpiresAt:O}."));
        }

        foreach (var requiredConfigKey in RequiredConfigKeys(connection))
        {
            checks.Add(new ConnectionHealthCheck(
                $"config.{requiredConfigKey}",
                HasConfigValue(connection, requiredConfigKey) ? ConnectionHealthStatus.Healthy : ConnectionHealthStatus.Unhealthy,
                $"Required key for {connection.ConnectorId}: {requiredConfigKey}."));
        }

        return checks;
    }

    private static bool HasConfigValue(TenantConnectionContract connection, string key)
        => connection.Config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool SecretRequired(TenantConnectionContract connection)
    {
        if (connection.Type == TenantConnectionType.Storage &&
            (!connection.Config.TryGetValue("provider", out var provider) ||
             string.Equals(provider, "internal", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return connection.Type is not TenantConnectionType.Storage;
    }

    private static IReadOnlyList<string> RequiredConfigKeys(TenantConnectionContract connection)
    {
        if (string.Equals(connection.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "provider", "accountSid", "fromPhoneNumber" };
        }

        if (string.Equals(connection.ConnectorId, "rest-api", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "baseUrl", "authType" };
        }

        if (string.Equals(connection.ConnectorId, "mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "server", "runtime" };
        }

        if (string.Equals(connection.ConnectorId, "storage", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "provider", "bucket" };
        }

        return connection.Type switch
        {
            TenantConnectionType.Sql => new[] { "connectionString" },
            TenantConnectionType.NoSql => new[] { "database" },
            TenantConnectionType.Rest => new[] { "baseUrl" },
            TenantConnectionType.Sheets => new[] { "spreadsheetId" },
            TenantConnectionType.Messaging => new[] { "provider" },
            TenantConnectionType.Storage => new[] { "bucket" },
            TenantConnectionType.Mcp => new[] { "server" },
            _ => new[] { "endpoint" }
        };
    }

    private static IReadOnlyList<string> CapabilitiesForConnection(TenantConnectionContract connection)
    {
        if (string.Equals(connection.ConnectorId, "twilio", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                CommunicationCapabilities.CallOutbound,
                CommunicationCapabilities.CallControl,
                CommunicationCapabilities.TextSend,
                "status.callbacks"
            };
        }

        if (string.Equals(connection.ConnectorId, "storage", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "files.read", "drive.lookup", "storage.write" };
        }

        if (string.Equals(connection.ConnectorId, "mcp", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "mcp.tool_call", "tool discovery" };
        }

        if (string.Equals(connection.ConnectorId, "rest-api", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "http.request", "webhook.call" };
        }

        if (string.Equals(connection.ConnectorId, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "reasoning",
                "embeddings",
                CommunicationCapabilities.AudioTranscribe,
                CommunicationCapabilities.AudioSynthesize
            };
        }

        return connection.Type switch
        {
            TenantConnectionType.Messaging => new[] { "messages", "callbacks" },
            TenantConnectionType.Storage => new[] { "files.read", "storage.write" },
            TenantConnectionType.Mcp => new[] { "mcp.tool_call" },
            TenantConnectionType.Rest => new[] { "http.request" },
            TenantConnectionType.Sheets => new[] { "sheets.read", "sheets.write" },
            _ => new[] { "connection.use" }
        };
    }

    private static IReadOnlyList<string> SuggestedNodesForConnection(TenantConnectionContract connection)
        => CapabilitiesForConnection(connection)
            .Select(capability => capability switch
            {
                CommunicationCapabilities.CallOutbound => "voice.call",
                CommunicationCapabilities.CallControl => "callcenter.outbound_call",
                CommunicationCapabilities.TextSend => "connect.enqueue_campaign_message",
                "files.read" => "files.read",
                "drive.lookup" => "drive.lookup",
                "storage.write" => "storage.write",
                "mcp.tool_call" => "mcp.tool_call",
                "http.request" => "http.request",
                "webhook.call" => "webhook.call",
                _ => capability
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task RecordUsageAsync(
        string tenantId,
        string connectionId,
        ResolveConnectionRequest request,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct)
    {
        await _store.RecordUsageAsync(new ConnectionUsageAuditContract
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            ConnectionId = connectionId,
            AgentId = request.AgentId,
            NodeId = request.NodeId,
            ConnectorId = request.ConnectorId,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            OccurredAt = DateTimeOffset.UtcNow
        }, ct);

        await RecordAuditAsync(tenantId, connectionId, "connection.resource.resolve", new
        {
            request.AgentId,
            request.NodeId,
            request.ConnectorId,
            succeeded,
            errorCode,
            errorMessage
        }, ct);
    }

    private static bool IsAllowed(IReadOnlyList<string> allowedValues, string requested)
        => allowedValues.Count == 0 || allowedValues.Contains(requested, StringComparer.OrdinalIgnoreCase);

    private static bool MatchesProvider(string providerId, TenantConnectionContract connection)
    {
        if (string.Equals(connection.ConnectorId, providerId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (connection.Config.TryGetValue("provider", out var configured) &&
            string.Equals(configured, providerId, StringComparison.OrdinalIgnoreCase))
            return true;

        return providerId switch
        {
            "meta" => string.Equals(connection.ConnectorId, "whatsapp-business", StringComparison.OrdinalIgnoreCase),
            "openai" => string.Equals(connection.ConnectorId, "openai", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(connection.ConnectorId, "rest-api", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool CanAccess(string tenantId, string permission)
    {
        var context = _tenantContext.Current!;
        return (context.TenantId == tenantId || context.IsPlatformAdmin) &&
               (context.HasPermission(permission) || context.IsPlatformAdmin);
    }

    private async Task RecordAuditAsync(string tenantId, string connectionId, string action, object payload, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        await _audit.RecordAsync(new AuditEntry
        {
            TenantId = tenantId,
            UserId = context.UserId,
            AgentId = "connection-module",
            ExecutionId = connectionId,
            EventType = AuditEventType.ConnectOperation,
            CorrelationId = HttpContext.TraceIdentifier,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new { action, payload })
        }, ct);
    }
}

public sealed record UpsertConnectionRequest(
    string Name,
    TenantConnectionType Type,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyList<string> AllowedAgentIds,
    IReadOnlyList<string> AllowedNodeIds,
    IReadOnlyList<string> AllowedConnectorIds);

public sealed record RotateConnectionSecretRequest(string Secret, DateTimeOffset? ExpiresAt);

public sealed record ResolveConnectionRequest(string AgentId, string NodeId, string ConnectorId);

public sealed record ConnectionResponse(
    string Id,
    string Name,
    TenantConnectionType Type,
    string ConnectorId,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyList<string> AllowedAgentIds,
    IReadOnlyList<string> AllowedNodeIds,
    IReadOnlyList<string> AllowedConnectorIds,
    int SecretVersion,
    DateTimeOffset? SecretRotatedAt,
    DateTimeOffset? SecretExpiresAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

public sealed record ConnectionHealthCheck(
    string Check,
    ConnectionHealthStatus Status,
    string Detail);
