using AgentFlow.Abstractions;
using AgentFlow.ModelRouting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentFlow.Tests.Unit.ModelRouting;

public class ModelRoleOrchestratorTests
{
    [Fact]
    public async Task ResolveAsync_PrimaryUnhealthy_UsesFallback()
    {
        var registry = new InMemoryModelRegistry();
        registry.Register(new DeterministicModelProvider(
            "primary-model",
            healthCheck: _ => Task.FromResult(false)));
        registry.Register(new DeterministicModelProvider(
            "fallback-model",
            healthCheck: _ => Task.FromResult(true)));

        var orchestrator = new ModelRoleOrchestrator(
            registry,
            NullLogger<ModelRoleOrchestrator>.Instance,
            Options.Create(new ModelRoleOrchestrationOptions()));
        var result = await orchestrator.ResolveAsync(new ModelRoleRoutingRequest
        {
            TenantId = "tenant-a",
            Role = ModelRole.Reasoning,
            CandidateModelIds = ["primary-model", "fallback-model"]
        });

        Assert.Equal("fallback-model", result.ModelId);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task ResolveAsync_AllUnhealthy_Throws()
    {
        var registry = new InMemoryModelRegistry();
        registry.Register(new DeterministicModelProvider(
            "primary-model",
            healthCheck: _ => Task.FromResult(false)));

        var orchestrator = new ModelRoleOrchestrator(
            registry,
            NullLogger<ModelRoleOrchestrator>.Instance,
            Options.Create(new ModelRoleOrchestrationOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ResolveAsync(new ModelRoleRoutingRequest
        {
            TenantId = "tenant-a",
            Role = ModelRole.TextToSpeech,
            CandidateModelIds = ["primary-model"]
        }));
    }
}
