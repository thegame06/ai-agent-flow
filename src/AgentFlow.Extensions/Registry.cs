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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _tenantStates = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tenantAllowlists = new();
    private readonly ConcurrentDictionary<string, string> _quarantine = new();
    private readonly ILogger<ExtensionRegistry> _logger;

    public ExtensionRegistry(
        IEnumerable<IToolPlugin> tools,
        IEnumerable<IPolicyEvaluator> policies,
        ILogger<ExtensionRegistry> logger)
    {
        _logger = logger;
        foreach (var tool in tools) RegisterTool(tool);
        foreach (var policy in policies) RegisterPolicy(policy);
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

    public Task<Result<ExtensionCatalogEntry>> RegisterPackageAsync(ExtensionPackageRegistrationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ExtensionId) || string.IsNullOrWhiteSpace(request.Version))
        {
            return Task.FromResult(Result<ExtensionCatalogEntry>.Failure(Error.Validation("extension", "ExtensionId/version are required.")));
        }

        var signatureValid = ValidatePackageSignature(request);
        if (!signatureValid)
        {
            return Task.FromResult(Result<ExtensionCatalogEntry>.Failure(Error.Validation("signature", "Invalid extension package signature.")));
        }

        var quarantined = _quarantine.TryGetValue(request.ExtensionId, out var quarantineReason);

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
        return Task.FromResult(Result<ExtensionCatalogEntry>.Success(entry));
    }

    public Task<IReadOnlyList<ExtensionCatalogEntry>> BrowseCatalogAsync(string? search = null, CancellationToken ct = default)
    {
        IEnumerable<ExtensionCatalogEntry> query = _catalog.Values;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.ExtensionId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Metadata.Vendor.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<ExtensionCatalogEntry>>(query.OrderBy(x => x.Name).ToList());
    }

    public Task<IReadOnlyList<string>> GetSourcesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_catalog.Values.Select(x => x.Source).Distinct().OrderBy(x => x).ToList());
    }

    public Task<Result> SetTenantInstallStateAsync(string tenantId, string extensionId, bool installed, bool enabled, CancellationToken ct = default)
    {
        if (!_catalog.ContainsKey(extensionId))
        {
            return Task.FromResult(Result.Failure(Error.NotFound($"extension:{extensionId}")));
        }

        if (installed)
        {
            var allowlist = _tenantAllowlists.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, byte>());
            if (allowlist.Count > 0 && !allowlist.ContainsKey(extensionId))
            {
                return Task.FromResult(Result.Failure(Error.Forbidden($"extension '{extensionId}' is not allowlisted for tenant '{tenantId}'")));
            }

            var state = _tenantStates.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, bool>());
            state[extensionId] = enabled;
            return Task.FromResult(Result.Success());
        }

        if (_tenantStates.TryGetValue(tenantId, out var states))
        {
            states.TryRemove(extensionId, out _);
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetTenantEnabledStateAsync(string tenantId, string extensionId, bool enabled, CancellationToken ct = default)
    {
        if (_quarantine.ContainsKey(extensionId))
        {
            return Task.FromResult(Result.Failure(Error.Forbidden($"extension '{extensionId}' is quarantined")));
        }

        var state = _tenantStates.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, bool>());
        if (!state.ContainsKey(extensionId))
        {
            return Task.FromResult(Result.Failure(Error.NotFound($"tenant-extension:{tenantId}:{extensionId}")));
        }

        state[extensionId] = enabled;
        return Task.FromResult(Result.Success());
    }

    public Task<IReadOnlyDictionary<string, bool>> GetTenantExtensionStatesAsync(string tenantId, CancellationToken ct = default)
    {
        if (!_tenantStates.TryGetValue(tenantId, out var state))
        {
            return Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
        }

        return Task.FromResult<IReadOnlyDictionary<string, bool>>(state.ToDictionary(x => x.Key, x => x.Value));
    }

    public Task<Result> SetTenantAllowlistAsync(string tenantId, IReadOnlyList<string> extensionIds, CancellationToken ct = default)
    {
        var allowlist = new ConcurrentDictionary<string, byte>(extensionIds.Distinct().ToDictionary(x => x, _ => (byte)1));
        _tenantAllowlists[tenantId] = allowlist;
        return Task.FromResult(Result.Success());
    }

    public Task<IReadOnlySet<string>> GetTenantAllowlistAsync(string tenantId, CancellationToken ct = default)
    {
        if (!_tenantAllowlists.TryGetValue(tenantId, out var allowlist))
        {
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        }

        return Task.FromResult<IReadOnlySet<string>>(allowlist.Keys.ToHashSet());
    }

    public Task<Result> QuarantineExtensionAsync(string extensionId, string reason, CancellationToken ct = default)
    {
        _quarantine[extensionId] = reason;

        foreach (var tenant in _tenantStates.Values)
        {
            if (tenant.ContainsKey(extensionId)) tenant[extensionId] = false;
        }

        if (_catalog.TryGetValue(extensionId, out var existing))
        {
            _catalog[extensionId] = existing with
            {
                Metadata = existing.Metadata with { IsQuarantined = true, QuarantineReason = reason }
            };
        }

        return Task.FromResult(Result.Success());
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
