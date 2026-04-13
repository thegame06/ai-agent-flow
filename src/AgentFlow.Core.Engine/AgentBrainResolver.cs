using AgentFlow.Abstractions;
using AgentFlow.Evaluation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Core.Engine;

public sealed class AgentBrainResolver : IAgentBrainResolver
{
    public const string AgentOverrideMetadataKey = "brainProviderAgentOverride";
    public const string TenantOverrideMetadataKey = "brainProviderTenantOverride";
    public const string ProviderFlagKey = "brain-provider-maf";

    private readonly IFeatureFlagService _featureFlags;
    private readonly Func<BrainProvider, IAgentBrain> _brainFactory;
    private readonly BrainProvider _defaultProvider;
    private readonly ILogger<AgentBrainResolver> _logger;

    public AgentBrainResolver(
        IFeatureFlagService featureFlags,
        Func<BrainProvider, IAgentBrain> brainFactory,
        IConfiguration configuration,
        ILogger<AgentBrainResolver> logger)
    {
        _featureFlags = featureFlags;
        _brainFactory = brainFactory;
        _logger = logger;

        var defaultProviderStr = configuration["AgentBrain:DefaultProvider"] ?? "SemanticKernel";
        if (!Enum.TryParse<BrainProvider>(defaultProviderStr, true, out var defaultProvider))
        {
            _logger.LogWarning("Invalid AgentBrain:DefaultProvider '{Provider}'. Falling back to SemanticKernel.", defaultProviderStr);
            defaultProvider = BrainProvider.SemanticKernel;
        }

        _defaultProvider = defaultProvider;
    }

    public async Task<BrainResolutionResult> ResolveAsync(
        string tenantId,
        string agentId,
        AgentBrainExecutionContext context,
        CancellationToken ct = default)
    {
        if (TryResolveOverride(context.Metadata, AgentOverrideMetadataKey, out var agentOverride))
            return Build(agentOverride, "agent_override");

        if (TryResolveOverride(context.Metadata, TenantOverrideMetadataKey, out var tenantOverride))
            return Build(tenantOverride, "tenant_override");

        var flagContext = new FeatureFlagContext
        {
            AgentId = agentId,
            UserId = context.UserId,
            ExecutionId = context.ExecutionId,
            Metadata = context.Metadata
        };

        var mafEnabled = await _featureFlags.IsEnabledAsync(tenantId, ProviderFlagKey, flagContext, ct);
        if (mafEnabled)
            return Build(BrainProvider.MicrosoftAgentFramework, "feature_flag");

        return Build(_defaultProvider, "default");
    }

    private BrainResolutionResult Build(BrainProvider provider, string source)
        => new()
        {
            Provider = provider,
            Brain = _brainFactory(provider),
            ResolutionSource = source
        };

    private bool TryResolveOverride(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out BrainProvider provider)
    {
        provider = default;

        if (!metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
            return false;

        if (Enum.TryParse<BrainProvider>(rawValue, true, out provider))
            return true;

        _logger.LogWarning(
            "Invalid brain provider override '{Provider}' in metadata key '{MetadataKey}'. Ignoring override.",
            rawValue,
            key);

        return false;
    }
}
