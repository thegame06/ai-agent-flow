using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace AgentFlow.Extensions;

public sealed record ExtensionCatalogMetadata
{
    public required string Vendor { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Low;
    public string Compatibility { get; init; } = "any";
    public bool SignatureValid { get; init; }
    public string? SignatureAlgorithm { get; init; }
    public bool IsQuarantined { get; init; }
    public string? QuarantineReason { get; init; }
}

public sealed record ExtensionCatalogEntry
{
    public required string ExtensionId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Source { get; init; } = "local";
    public DateTimeOffset PublishedAt { get; init; } = DateTimeOffset.UtcNow;
    public ExtensionCatalogMetadata Metadata { get; init; } = new()
    {
        Vendor = "unknown",
        SignatureValid = false
    };
}

public sealed record ExtensionPackageRegistrationRequest
{
    public required string ExtensionId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Vendor { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Medium;
    public string Compatibility { get; init; } = "agentflow-core>=1.0.0";
    public string Source { get; init; } = "remote";
    public string? SignatureAlgorithm { get; init; }
    public string? Signature { get; init; }
    public string ManifestJson { get; init; } = "{}";
    public string PayloadHash { get; init; } = string.Empty;
}

public interface IExtensionRegistry
{
    void RegisterTool(IToolPlugin tool);
    void RegisterPolicy(IPolicyEvaluator evaluator);

    IReadOnlyList<IToolPlugin> GetTools();
    IToolPlugin? GetTool(string name);

    IReadOnlyList<IPolicyEvaluator> GetPolicies();

    IReadOnlyList<IAgentFlowExtension> GetAllExtensions();

    Task<Result<ExtensionCatalogEntry>> RegisterPackageAsync(ExtensionPackageRegistrationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ExtensionCatalogEntry>> BrowseCatalogAsync(string? search = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSourcesAsync(CancellationToken ct = default);
    Task<Result> SetTenantInstallStateAsync(string tenantId, string extensionId, bool installed, bool enabled, CancellationToken ct = default);
    Task<Result> SetTenantEnabledStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, bool>> GetTenantExtensionStatesAsync(string tenantId, CancellationToken ct = default);
    Task<Result> SetTenantAllowlistAsync(string tenantId, IReadOnlyList<string> extensionIds, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetTenantAllowlistAsync(string tenantId, CancellationToken ct = default);
    Task<Result> QuarantineExtensionAsync(string extensionId, string reason, CancellationToken ct = default);
}

public sealed class ExtensionRegistry : IExtensionRegistry
{
    private readonly ConcurrentDictionary<string, IToolPlugin> _tools = new();
    private readonly ConcurrentDictionary<string, IPolicyEvaluator> _policies = new();
    private readonly ConcurrentDictionary<string, ExtensionCatalogEntry> _catalog = new();
    // Tenant install/enable state, allowlists and quarantine live in the store so they
    // survive restarts and are shared across instances (see ITenantExtensionStateStore).
    private readonly ITenantExtensionStateStore _stateStore;
    private readonly ILogger<ExtensionRegistry> _logger;

    public ExtensionRegistry(
        IEnumerable<IToolPlugin> tools,
        IEnumerable<IPolicyEvaluator> policies,
        ITenantExtensionStateStore stateStore,
        ILogger<ExtensionRegistry> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
        foreach (var tool in tools) RegisterTool(tool);
        foreach (var policy in policies) RegisterPolicy(policy);
        SeedCommerceModules();
    }

    public void RegisterTool(IToolPlugin tool)
    {
        if (_tools.TryAdd(tool.Name, tool))
        {
            _catalog[tool.ExtensionId] = new ExtensionCatalogEntry
            {
                ExtensionId = tool.ExtensionId,
                Name = tool.Name,
                Version = tool.Version,
                Description = tool.Description,
                Source = "local",
                Metadata = new ExtensionCatalogMetadata
                {
                    Vendor = "builtin",
                    Permissions = tool.RequiredPermissions,
                    RiskLevel = tool.RiskLevel,
                    Compatibility = "agentflow-core>=1.0.0",
                    SignatureValid = true,
                    SignatureAlgorithm = "builtin"
                }
            };

            _logger.LogInformation("Extension Registered: Tool '{ToolName}' v{Version}", tool.Name, tool.Version);
        }
    }

    public void RegisterPolicy(IPolicyEvaluator evaluator)
    {
        _policies.TryAdd(evaluator.PolicyType, evaluator);
    }

    public IReadOnlyList<IToolPlugin> GetTools() => _tools.Values.ToList();
    public IToolPlugin? GetTool(string name) => _tools.GetValueOrDefault(name);
    public IReadOnlyList<IPolicyEvaluator> GetPolicies() => _policies.Values.ToList();

    public IReadOnlyList<IAgentFlowExtension> GetAllExtensions()
    {
        var all = new List<IAgentFlowExtension>();
        all.AddRange(_tools.Values);
        return all;
    }

    public async Task<Result<ExtensionCatalogEntry>> RegisterPackageAsync(ExtensionPackageRegistrationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExtensionId) || string.IsNullOrWhiteSpace(request.Version))
        {
            return Result<ExtensionCatalogEntry>.Failure(Error.Validation("extension", "ExtensionId/version are required."));
        }

        var signatureValid = ValidatePackageSignature(request);
        if (!signatureValid)
        {
            return Result<ExtensionCatalogEntry>.Failure(Error.Validation("signature", "Invalid extension package signature."));
        }

        var quarantine = await _stateStore.GetQuarantinedAsync(ct);
        var quarantined = quarantine.TryGetValue(request.ExtensionId, out var quarantineReason);

        var entry = new ExtensionCatalogEntry
        {
            ExtensionId = request.ExtensionId,
            Name = request.Name,
            Version = request.Version,
            Description = request.Description,
            Source = request.Source,
            Metadata = new ExtensionCatalogMetadata
            {
                Vendor = request.Vendor,
                Permissions = request.Permissions,
                RiskLevel = request.RiskLevel,
                Compatibility = request.Compatibility,
                SignatureValid = signatureValid,
                SignatureAlgorithm = request.SignatureAlgorithm,
                IsQuarantined = quarantined,
                QuarantineReason = quarantineReason
            }
        };

        _catalog[request.ExtensionId] = entry;
        return Result<ExtensionCatalogEntry>.Success(entry);
    }

    public async Task<IReadOnlyList<ExtensionCatalogEntry>> BrowseCatalogAsync(string? search = null, CancellationToken ct = default)
    {
        // The catalog is rebuilt at startup; overlay the persisted quarantine so it survives restarts.
        var quarantine = await _stateStore.GetQuarantinedAsync(ct);
        IEnumerable<ExtensionCatalogEntry> query = _catalog.Values.Select(entry =>
            quarantine.TryGetValue(entry.ExtensionId, out var reason)
                ? entry with { Metadata = entry.Metadata with { IsQuarantined = true, QuarantineReason = reason } }
                : entry);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.ExtensionId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Metadata.Vendor.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderBy(x => x.Name).ToList();
    }

    public Task<IReadOnlyList<string>> GetSourcesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_catalog.Values.Select(x => x.Source).Distinct().OrderBy(x => x).ToList());
    }

    public async Task<Result> SetTenantInstallStateAsync(string tenantId, string extensionId, bool installed, bool enabled, CancellationToken ct = default)
    {
        if (!_catalog.ContainsKey(extensionId))
        {
            return Result.Failure(Error.NotFound($"extension:{extensionId}"));
        }

        if (installed)
        {
            var allowlist = await _stateStore.GetAllowlistAsync(tenantId, ct);
            if (allowlist.Count > 0 && !allowlist.Contains(extensionId))
            {
                return Result.Failure(Error.Forbidden($"extension '{extensionId}' is not allowlisted for tenant '{tenantId}'"));
            }

            await _stateStore.SetStateAsync(tenantId, extensionId, enabled, ct);
            return Result.Success();
        }

        await _stateStore.RemoveStateAsync(tenantId, extensionId, ct);
        return Result.Success();
    }

    public async Task<Result> SetTenantEnabledStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default)
    {
        var quarantine = await _stateStore.GetQuarantinedAsync(ct);
        if (quarantine.ContainsKey(extensionId))
        {
            return Result.Failure(Error.Forbidden($"extension '{extensionId}' is quarantined"));
        }

        var states = await _stateStore.GetStatesAsync(tenantId, ct);
        if (!states.ContainsKey(extensionId))
        {
            return Result.Failure(Error.NotFound($"tenant-extension:{tenantId}:{extensionId}"));
        }

        await _stateStore.SetStateAsync(tenantId, extensionId, enabled, ct);
        return Result.Success();
    }

    public Task<IReadOnlyDictionary<string, bool>> GetTenantExtensionStatesAsync(string tenantId, CancellationToken ct = default)
        => _stateStore.GetStatesAsync(tenantId, ct);

    public async Task<Result> SetTenantAllowlistAsync(string tenantId, IReadOnlyList<string> extensionIds, CancellationToken ct = default)
    {
        await _stateStore.SetAllowlistAsync(tenantId, extensionIds.Distinct().ToList(), ct);
        return Result.Success();
    }

    public Task<IReadOnlySet<string>> GetTenantAllowlistAsync(string tenantId, CancellationToken ct = default)
        => _stateStore.GetAllowlistAsync(tenantId, ct);

    public async Task<Result> QuarantineExtensionAsync(string extensionId, string reason, CancellationToken ct = default)
    {
        await _stateStore.QuarantineAsync(extensionId, reason, ct);

        if (_catalog.TryGetValue(extensionId, out var existing))
        {
            _catalog[extensionId] = existing with
            {
                Metadata = existing.Metadata with { IsQuarantined = true, QuarantineReason = reason }
            };
        }

        return Result.Success();
    }

    private static bool ValidatePackageSignature(ExtensionPackageRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Signature) || string.IsNullOrWhiteSpace(request.SignatureAlgorithm))
        {
            return false;
        }

        if (!request.SignatureAlgorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = $"{request.ExtensionId}|{request.Version}|{request.ManifestJson}|{request.PayloadHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        var digest = Convert.ToHexString(hash);

        return string.Equals(digest, request.Signature, StringComparison.OrdinalIgnoreCase);
    }

    private void SeedCommerceModules()
    {
        var modules = new[]
        {
            ("communication-inbox", "Communication Inbox", "Inbox comercial integrado en chat"),
            ("inventory", "Inventory", "Inventario y catalogo de productos"),
            ("sales-pos", "Sales POS", "Punto de venta y ordenes comerciales"),
            ("billing", "Billing", "Facturacion y estados de cobro")
        };

        foreach (var (id, name, description) in modules)
        {
            _catalog.TryAdd(id, new ExtensionCatalogEntry
            {
                ExtensionId = id,
                Name = name,
                Version = "1.0.0",
                Description = description,
                Source = "builtin-module",
                Metadata = new ExtensionCatalogMetadata
                {
                    Vendor = "agentflow",
                    SignatureValid = true,
                    SignatureAlgorithm = "builtin",
                    Compatibility = "agentflow-core>=1.0.0"
                }
            });
        }
    }
}

public interface IToolInvoker
{
    Task<ToolResult> InvokeAsync(
        string toolName,
        string inputJson,
        ToolExecutionContext? customContext = null,
        CancellationToken ct = default);
}

public sealed class ToolInvoker : IToolInvoker
{
    private readonly IExtensionRegistry _registry;
    private readonly ILogger<ToolInvoker> _logger;

    public ToolInvoker(IExtensionRegistry registry, ILogger<ToolInvoker> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        string inputJson,
        ToolExecutionContext? customContext = null,
        CancellationToken ct = default)
    {
        var tool = _registry.GetTool(toolName);
        if (tool == null)
        {
            return ToolResult.Failure("EXT001", $"Tool '{toolName}' not found in registry.");
        }

        var context = customContext ?? new ToolExecutionContext
        {
            TenantId = "system-debug",
            UserId = "developer",
            ExecutionId = $"dbg-{Guid.NewGuid():N}",
            StepId = "0",
            CorrelationId = "debug",
            InputJson = inputJson
        };

        _logger.LogDebug("Invoking Tool '{ToolName}' standalone", toolName);

        try
        {
            return await tool.ExecuteAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool '{ToolName}' failed during standalone invocation", toolName);
            return ToolResult.Failure("EXT_ERR", ex.Message);
        }
    }
}
