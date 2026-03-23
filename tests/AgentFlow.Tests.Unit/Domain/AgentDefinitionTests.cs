using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.ValueObjects;
using Xunit;

namespace AgentFlow.Tests.Unit.Domain;

public sealed class AgentDefinitionTests
{
    [Fact]
    public void Clone_WithValidParameters_CreatesNewDraft()
    {
        // Arrange
        var workflow = new[]
        {
            new WorkflowStep
            {
                Id = "step-1",
                Type = "think",
                Label = "Prompt Chain",
                Config = new Dictionary<string, object> { ["prompt"] = "Rewrite user input" },
                Connections = ["step-2"]
            }
        }.ToList().AsReadOnly();

        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: workflow,
            ownerUserId: "user-1");

        Assert.True(originalResult.IsSuccess);
        var original = originalResult.Value!;

        // Add a tool to the original
        original.AddTool(new ToolBinding
        {
            ToolId = "tool-1",
            ToolName = "SearchTool",
            ToolVersion = "1.0.0",
            GrantedPermissions = new[] { "read" }.ToList().AsReadOnly()
        });

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: "Cloned Agent",
            newDescription: "Cloned from original",
            clonedBy: "user-2");

        // Assert
        Assert.True(cloneResult.IsSuccess);
        var cloned = cloneResult.Value!;

        Assert.NotEqual(original.Id, cloned.Id); // Different ID
        Assert.Equal("Cloned Agent", cloned.Name);
        Assert.Equal("Cloned from original", cloned.Description);
        Assert.Equal(AgentStatus.Draft, cloned.Status); // Always Draft
        Assert.Equal("user-2", cloned.OwnerUserId);
        Assert.Equal("user-2", cloned.CreatedBy);

        // Configuration should be copied
        Assert.Equal(original.Brain.ModelId, cloned.Brain.ModelId);
        Assert.Equal(original.Brain.Temperature, cloned.Brain.Temperature);
        Assert.Equal(original.LoopConfig.MaxIterations, cloned.LoopConfig.MaxIterations);
        Assert.Equal(original.Memory.EnableWorkingMemory, cloned.Memory.EnableWorkingMemory);

        // Tools should be copied
        Assert.Single(cloned.AuthorizedTools);
        Assert.Equal("tool-1", cloned.AuthorizedTools[0].ToolId);
        Assert.Single(cloned.WorkflowSteps);
        Assert.Equal("Prompt Chain", cloned.WorkflowSteps[0].Label);

        // Experimentation settings should NOT be copied
        Assert.Null(cloned.ShadowAgentId);
        Assert.Null(cloned.CanaryAgentId);
        Assert.Equal(0.0, cloned.CanaryWeight);
    }

    [Fact]
    public void Clone_WithEmptyName_ReturnsFail()
    {
        // Arrange
        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var original = originalResult.Value!;

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: "",
            newDescription: null,
            clonedBy: "user-2");

        // Assert
        Assert.False(cloneResult.IsSuccess);
        Assert.Contains("required", cloneResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clone_WithLongName_ReturnsFail()
    {
        // Arrange
        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var original = originalResult.Value!;

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: new string('A', 101), // 101 characters
            newDescription: null,
            clonedBy: "user-2");

        // Assert
        Assert.False(cloneResult.IsSuccess);
        Assert.Contains("100", cloneResult.Error!.Message);
    }

    [Fact]
    public void Clone_WithNullDescription_UsesDefaultMessage()
    {
        // Arrange
        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var original = originalResult.Value!;

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: "Cloned Agent",
            newDescription: null,
            clonedBy: "user-2");

        // Assert
        Assert.True(cloneResult.IsSuccess);
        var cloned = cloneResult.Value!;
        Assert.Equal("Cloned from Original Agent", cloned.Description);
    }

    [Fact]
    public void Clone_WithPublishedAgent_CreatesNewDraft()
    {
        // Arrange
        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var original = originalResult.Value!;

        // Add tool and publish
        original.AddTool(new ToolBinding
        {
            ToolId = "tool-1",
            ToolName = "SearchTool",
            ToolVersion = "1.0.0",
            GrantedPermissions = new[] { "read" }.ToList().AsReadOnly()
        });
        original.Publish("user-1");

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: "Cloned Agent",
            newDescription: "Cloned from published",
            clonedBy: "user-2");

        // Assert
        Assert.True(cloneResult.IsSuccess);
        var cloned = cloneResult.Value!;
        Assert.Equal(AgentStatus.Draft, cloned.Status); // Even if source is Published, clone is Draft
        Assert.Equal(AgentStatus.Published, original.Status); // Original unchanged
    }

    [Fact]
    public void Clone_CopiesTagsButNotExperimentation()
    {
        // Arrange
        var originalResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Original Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant.",
                Temperature = 0.7f,
                MaxResponseTokens = 4096
            },
            loopConfig: new AgentLoopConfig
            {
                MaxIterations = 25,
                ToolCallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 3,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            },
            memory: new MemoryConfig
            {
                EnableWorkingMemory = true,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            },
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var original = originalResult.Value!;
        original.SetTags(new[] { "production", "customer-support" }.ToList().AsReadOnly());

        // Simulate experimentation configuration
        original.Update(
            name: original.Name,
            description: original.Description,
            brain: original.Brain,
            loopConfig: original.LoopConfig,
            memory: original.Memory,
            session: null,
            workflowSteps: original.WorkflowSteps,
            tools: original.AuthorizedTools,
            tags: original.Tags,
            updatedBy: "user-1",
            shadowAgentId: "agent-shadow",
            canaryAgentId: "agent-canary",
            canaryWeight: 0.10);

        // Act
        var cloneResult = AgentDefinition.Clone(
            source: original,
            newName: "Cloned Agent",
            newDescription: null,
            clonedBy: "user-2");

        // Assert
        Assert.True(cloneResult.IsSuccess);
        var cloned = cloneResult.Value!;

        // Tags should be copied
        Assert.Equal(2, cloned.Tags.Count);
        Assert.Contains("production", cloned.Tags);
        Assert.Contains("customer-support", cloned.Tags);

        // Experimentation settings should NOT be copied
        Assert.Null(cloned.ShadowAgentId);
        Assert.Null(cloned.CanaryAgentId);
        Assert.Equal(0.0, cloned.CanaryWeight);

        // Original should still have experimentation settings
        Assert.Equal("agent-shadow", original.ShadowAgentId);
        Assert.Equal("agent-canary", original.CanaryAgentId);
        Assert.Equal(0.10, original.CanaryWeight);
    }

    [Fact]
    public void Update_WithWorkflowSteps_PersistsSequentialPlannerShape()
    {
        var createResult = AgentDefinition.Create(
            tenantId: "tenant-1",
            name: "Workflow Agent",
            description: "Original description",
            brain: new BrainConfiguration
            {
                ModelId = "gpt-4o",
                Provider = "OpenAI",
                SystemPromptTemplate = "You are a helpful assistant."
            },
            loopConfig: new AgentLoopConfig { MaxIterations = 10 },
            memory: new MemoryConfig(),
            session: null,
            workflowSteps: null,
            ownerUserId: "user-1");

        var agent = createResult.Value!;

        var updateResult = agent.Update(
            name: "Workflow Agent",
            description: "Updated",
            brain: agent.Brain,
            loopConfig: agent.LoopConfig with { PlannerType = AgentFlow.Abstractions.PlannerType.Sequential, AllowParallelToolCalls = true },
            memory: agent.Memory,
            session: null,
            workflowSteps: new[]
            {
                new WorkflowStep
                {
                    Id = "step-think",
                    Type = "think",
                    Label = "Chain Prompt",
                    Config = new Dictionary<string, object> { ["prompt"] = "Summarize input" },
                    Connections = ["step-decide"]
                },
                new WorkflowStep
                {
                    Id = "step-decide",
                    Type = "decide",
                    Label = "Gate",
                    Config = new Dictionary<string, object> { ["mode"] = "contains", ["matchValue"] = "approve" }
                }
            }.ToList().AsReadOnly(),
            tools: [],
            tags: ["sprint-1"],
            updatedBy: "user-2");

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(AgentFlow.Abstractions.PlannerType.Sequential, agent.LoopConfig.PlannerType);
        Assert.True(agent.LoopConfig.AllowParallelToolCalls);
        Assert.Equal(2, agent.WorkflowSteps.Count);
        Assert.Equal("decide", agent.WorkflowSteps[1].Type);
    }
}
