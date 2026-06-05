using AgentFlow.Abstractions;
using AgentFlow.Api.AuthProfiles;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.Controllers.DTOs;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Extensions;
using AgentFlow.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentFlow.Tests.Unit.Api;

public sealed class AgentsControllerTests
{
    [Fact]
    public async Task GetAgents_RuntimeKindAliasMultimodal_FiltersResults()
    {
        var repo = new Mock<IAgentDefinitionRepository>();
        repo.Setup(x => x.GetAllAsync("tenant-1", 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                BuildAgent("tenant-1", "texto", AgentRuntimeKind.Text),
                BuildAgent("tenant-1", "voz", AgentRuntimeKind.Voice),
                BuildAgent("tenant-1", "multi", AgentRuntimeKind.MultimodalRealtime)
            });

        var controller = CreateController(repo.Object);

        var result = await controller.GetAgents("tenant-1", runtimeKind: "multimodal", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<AgentListItemDto>>(ok.Value).ToList();
        var item = Assert.Single(items);
        Assert.Equal("multi", item.Name);
        Assert.Equal(nameof(AgentRuntimeKind.MultimodalRealtime), item.RuntimeKind);
    }

    [Fact]
    public async Task GetAgents_InvalidRuntimeKind_ReturnsBadRequest()
    {
        var repo = new Mock<IAgentDefinitionRepository>();
        var controller = CreateController(repo.Object);

        var result = await controller.GetAgents("tenant-1", runtimeKind: "fax", ct: CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode ?? 400);
        repo.Verify(x => x.GetAllAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStats_ReturnsCatalogMetricsAndRuntimeBuckets()
    {
        var publishedText = BuildAgent("tenant-1", "texto publicado", AgentRuntimeKind.Text, publish: true, tools: 1);
        var voiceSystem = BuildAgent("tenant-1", "router voz", AgentRuntimeKind.Voice, tools: 1, systemRole: AgentSystemRole.Router);
        var multimodal = BuildAgent("tenant-1", "video", AgentRuntimeKind.MultimodalRealtime);

        var repo = new Mock<IAgentDefinitionRepository>();
        repo.Setup(x => x.GetAllAsync("tenant-1", 0, 5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { publishedText, voiceSystem, multimodal });

        var controller = CreateController(repo.Object);

        var result = await controller.GetStats("tenant-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var stats = Assert.IsType<AgentCatalogStatsDto>(ok.Value);
        Assert.Equal(3, stats.Total);
        Assert.Equal(1, stats.Published);
        Assert.Equal(1, stats.System);
        Assert.Equal(2, stats.WithTools);
        Assert.Equal(1, stats.RuntimeKinds.Single(x => x.Key == nameof(AgentRuntimeKind.Text)).Count);
        Assert.Equal(1, stats.RuntimeKinds.Single(x => x.Key == nameof(AgentRuntimeKind.Voice)).Count);
        Assert.Equal(1, stats.RuntimeKinds.Single(x => x.Key == nameof(AgentRuntimeKind.MultimodalRealtime)).Count);
    }

    private static AgentsController CreateController(IAgentDefinitionRepository repo)
    {
        return new AgentsController(
            repo,
            Mock.Of<IRuntimeModelProfileStore>(),
            Mock.Of<ITenantContextAccessor>(),
            Mock.Of<IExtensionRegistry>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentsController>.Instance);
    }

    private static AgentDefinition BuildAgent(
        string tenantId,
        string name,
        AgentRuntimeKind runtimeKind,
        bool publish = false,
        int tools = 0,
        AgentSystemRole systemRole = AgentSystemRole.Custom)
    {
        var create = AgentDefinition.Create(
            tenantId,
            name,
            $"{name} desc",
            new BrainConfiguration
            {
                ModelId = "gpt-4o-mini",
                Provider = "OpenAI",
                SystemPromptTemplate = "Asistente de prueba",
                Temperature = 0.2f,
                MaxResponseTokens = 512
            },
            new AgentLoopConfig
            {
                MaxIterations = 3,
                ToolCallTimeout = TimeSpan.FromSeconds(5),
                MaxRetries = 1
            },
            new MemoryConfig(),
            new SessionConfig
            {
                RuntimeKind = runtimeKind,
                MaxTurnsPerThread = 10,
                ContextWindowSize = 5
            },
            Array.Empty<WorkflowStep>(),
            "user-1");

        var agent = create.Value!;
        for (var i = 0; i < tools; i++)
        {
            agent.AddTool(new ToolBinding
            {
                ToolId = $"tool-{i}",
                ToolName = $"Tool {i}",
                ToolVersion = "1.0.0",
                GrantedPermissions = Array.Empty<string>()
            });
        }

        if (systemRole != AgentSystemRole.Custom)
            agent.SetSystemRole(systemRole);

        if (publish)
            agent.Publish("user-1");

        return agent;
    }
}
