using AgentFlow.Abstractions;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.ModelRouting;
using AgentFlow.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/v1/model-routing")]
[Authorize]
public sealed class ModelRoutingController : ControllerBase
{
    private readonly IModelRegistry _registry;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IAuthProfilesStore _authProfiles;
    private readonly IModelCatalogStore _catalog;

    public ModelRoutingController(
        IModelRegistry registry,
        ITenantContextAccessor tenantContext,
        IAuthProfilesStore authProfiles,
        IModelCatalogStore catalog)
    {
        _registry = registry;
        _tenantContext = tenantContext;
        _authProfiles = authProfiles;
        _catalog = catalog;
    }

    [HttpGet("models")]
    public IActionResult GetAvailableModels()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var models = _registry.GetProviders()
            .Select(p => new
            {
                p.ModelId,
                p.ProviderId,
                p.Metadata.DisplayName,
                p.Metadata.CostPer1KTokens,
                p.Metadata.MaxContextTokens,
                p.Metadata.Tier,
                ProviderProfileId = _authProfiles.GetModelProfileId(context.TenantId, p.ModelId),
                Status = "Active" // Default for registered models
            });

        return Ok(models);
    }

    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var providers = _registry.GetProviders()
            .GroupBy(p => p.ProviderId)
            .Select(g => new
            {
                ProviderId = g.Key,
                ModelCount = g.Count(),
                Models = g.Select(m => m.ModelId).OrderBy(x => x).ToList()
            })
            .OrderBy(p => p.ProviderId);

        return Ok(providers);
    }

    [HttpGet("providers/{providerId}/models")]
    public IActionResult GetProviderModels(string providerId)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var models = _registry.GetProviders()
            .Where(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            .Select(p => new
            {
                p.ModelId,
                p.ProviderId,
                p.Metadata.DisplayName,
                p.Metadata.CostPer1KTokens,
                p.Metadata.MaxContextTokens,
                p.Metadata.Tier,
                ProviderProfileId = _authProfiles.GetModelProfileId(context.TenantId, p.ModelId),
                Status = "Active"
            })
            .ToList();

        return Ok(models);
    }

    [HttpPost("models")]
    public IActionResult RegisterModel([FromBody] RegisterModelRequest request)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        if (string.IsNullOrWhiteSpace(request.ModelId))
            return BadRequest(new { message = "modelId is required." });
        if (string.IsNullOrWhiteSpace(request.ProviderId))
            return BadRequest(new { message = "providerId is required." });
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { message = "displayName is required." });
        if (request.MaxContextTokens <= 0)
            return BadRequest(new { message = "maxContextTokens must be > 0." });
        if (request.CostPer1KTokens < 0)
            return BadRequest(new { message = "costPer1KTokens must be >= 0." });
        if (!string.Equals(request.ProviderId, "OpenAI", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only OpenAI models are supported by the current runtime. Add other providers after implementing their runtime adapter." });

        var providerProfileId = request.ProviderProfileId;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            providerProfileId = string.IsNullOrWhiteSpace(providerProfileId)
                ? $"{request.ProviderId}:{request.ModelId}"
                : providerProfileId;

            _authProfiles.Upsert(context.TenantId, new UpsertProviderAuthProfileRequest
            {
                Provider = request.ProviderId,
                ProfileId = providerProfileId,
                AuthType = "api_key",
                Secret = request.ApiKey,
                Metadata = new Dictionary<string, string>
                {
                    ["modelId"] = request.ModelId
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(providerProfileId))
        {
            var profile = _authProfiles.Get(context.TenantId, providerProfileId);
            if (profile is null)
                return BadRequest(new { message = $"providerProfileId '{providerProfileId}' not found for tenant." });

            if (!string.Equals(profile.Provider, request.ProviderId, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "providerProfileId provider does not match model providerId." });
        }

        var provider = CreateConfiguredProvider(
            request.ModelId,
            request.ProviderId,
            context.TenantId,
            new ModelMetadata
            {
                DisplayName = request.DisplayName,
                CostPer1KTokens = request.CostPer1KTokens,
                MaxContextTokens = request.MaxContextTokens,
                Tier = request.Tier
            });

        _registry.Register(provider);

        if (!string.IsNullOrWhiteSpace(providerProfileId))
        {
            _authProfiles.LinkModelProfile(context.TenantId, request.ModelId, providerProfileId);
        }

        var existing = _catalog.Get(context.TenantId, request.ModelId);
        _catalog.Upsert(new ModelCatalogEntry
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            TenantId = context.TenantId,
            ModelId = request.ModelId,
            ProviderId = request.ProviderId,
            DisplayName = request.DisplayName,
            CostPer1KTokens = request.CostPer1KTokens,
            MaxContextTokens = request.MaxContextTokens,
            Tier = request.Tier,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return CreatedAtAction(nameof(GetAvailableModels), new
        {
            provider.ModelId,
            provider.ProviderId
        }, new
        {
            provider.ModelId,
            provider.ProviderId,
            provider.Metadata.DisplayName,
            provider.Metadata.CostPer1KTokens,
            provider.Metadata.MaxContextTokens,
            provider.Metadata.Tier,
            ProviderProfileId = providerProfileId,
            Status = "Active"
        });
    }

    [HttpPost("models/{modelId}/set-primary")]
    public IActionResult SetPrimaryModel(string modelId)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var target = _registry.GetProvider(modelId);
        if (target is null) return NotFound(new { message = $"Model '{modelId}' not found." });

        var sameProviderModels = _registry.GetProviders()
            .Where(p => string.Equals(p.ProviderId, target.ProviderId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var model in sameProviderModels)
        {
            var tier = model.ModelId == modelId ? "Primary" : "Secondary";
            _registry.Register(CreateConfiguredProvider(
                model.ModelId,
                model.ProviderId,
                context.TenantId,
                new ModelMetadata
                {
                    DisplayName = model.Metadata.DisplayName,
                    CostPer1KTokens = model.Metadata.CostPer1KTokens,
                    MaxContextTokens = model.Metadata.MaxContextTokens,
                    Tier = tier
                }));

            var existing = _catalog.Get(context.TenantId, model.ModelId);
            _catalog.Upsert(new ModelCatalogEntry
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                TenantId = context.TenantId,
                ModelId = model.ModelId,
                ProviderId = model.ProviderId,
                DisplayName = model.Metadata.DisplayName,
                CostPer1KTokens = model.Metadata.CostPer1KTokens,
                MaxContextTokens = model.Metadata.MaxContextTokens,
                Tier = tier,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        return Ok(new
        {
            message = $"Model '{modelId}' is now Primary for provider '{target.ProviderId}'."
        });
    }

    [HttpPost("models/{modelId}/test")]
    public async Task<IActionResult> TestModel(string modelId, CancellationToken ct)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var provider = _registry.GetProvider(modelId);
        if (provider is null) return NotFound(new { message = $"Model '{modelId}' not found." });

        try
        {
            var healthy = await provider.IsHealthyAsync(ct);
            return Ok(new
            {
                provider.ModelId,
                provider.ProviderId,
                Healthy = healthy,
                CheckedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                provider.ModelId,
                provider.ProviderId,
                Healthy = false,
                CheckedAt = DateTimeOffset.UtcNow,
                Error = ex.Message
            });
        }
    }

    [HttpPost("models/{modelId}/bind-profile")]
    public IActionResult BindModelProfile(string modelId, [FromBody] BindModelProfileRequest request)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var model = _registry.GetProvider(modelId);
        if (model is null) return NotFound(new { message = $"Model '{modelId}' not found." });

        var profile = _authProfiles.Get(context.TenantId, request.ProviderProfileId);
        if (profile is null) return NotFound(new { message = $"Profile '{request.ProviderProfileId}' not found." });

        if (!string.Equals(profile.Provider, model.ProviderId, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Profile provider does not match model provider." });

        _authProfiles.LinkModelProfile(context.TenantId, modelId, request.ProviderProfileId);
        var existing = _catalog.Get(context.TenantId, modelId);
        if (existing is not null)
            _catalog.Upsert(existing with { UpdatedAt = DateTimeOffset.UtcNow });

        return Ok(new
        {
            modelId,
            model.ProviderId,
            ProviderProfileId = request.ProviderProfileId,
            message = "Model successfully linked to auth profile."
        });
    }

    [HttpDelete("models/{modelId}")]
    public IActionResult DisableModel(string modelId)
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var removed = _registry.Remove(modelId);
        if (!removed) return NotFound(new { message = $"Model '{modelId}' not found." });
        _catalog.Delete(context.TenantId, modelId);

        return Ok(new { message = $"Model '{modelId}' removed from routing registry." });
    }

    [HttpGet("models/healthy")]
    public async Task<IActionResult> GetHealthyModels()
    {
        var context = _tenantContext.Current!;
        if (!context.IsPlatformAdmin) return Forbid();

        var models = await _registry.GetHealthyModelIdsAsync();
        return Ok(models);
    }

    private ConfiguredModelProvider CreateConfiguredProvider(
        string modelId,
        string providerId,
        string tenantId,
        ModelMetadata metadata)
    {
        return new ConfiguredModelProvider(
            modelId,
            providerId,
            metadata,
            _ => Task.FromResult(HasProviderConfiguration(tenantId, modelId, providerId)));
    }

    private bool HasProviderConfiguration(string tenantId, string modelId, string providerId)
    {
        if (!string.Equals(providerId, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var linkedProfileId = _authProfiles.GetModelProfileId(tenantId, modelId);
        if (!string.IsNullOrWhiteSpace(linkedProfileId))
        {
            var profile = _authProfiles.Get(tenantId, linkedProfileId);
            return profile is not null &&
                (profile.ExpiresAt is null || profile.ExpiresAt > DateTimeOffset.UtcNow) &&
                string.Equals(profile.Provider, providerId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

public sealed record RegisterModelRequest
{
    public string ModelId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderId { get; init; } = "OpenAI";
    public string Tier { get; init; } = "Primary";
    public double CostPer1KTokens { get; init; }
    public int MaxContextTokens { get; init; } = 128000;
    public string? ProviderProfileId { get; init; }
    public string? ApiKey { get; init; } // Reserved for future persisted provider credentials
}

public sealed record BindModelProfileRequest
{
    public required string ProviderProfileId { get; init; }
}
