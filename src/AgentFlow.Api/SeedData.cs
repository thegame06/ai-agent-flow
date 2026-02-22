using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Domain.Repositories;

namespace AgentFlow.Api;

public static class SeedData
{
    public static async Task SeedDemoDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var tenantId = "tenant-1"; // Demo tenant
        var demoUser = "demo-user@agentflow.dev";

        // Check if we already have demo data
        var existingAgents = await agentRepo.GetAllAsync(tenantId, 0, 10);
        if (existingAgents.Any(a => a.Name.Contains("Demo"))) return; // Already seeded

        // ═══════════════════════════════════════════════════════════════════
        // DEMO AGENT 1: Customer Support Agent
        // ═══════════════════════════════════════════════════════════════════
       
        var brain1 = new BrainConfiguration
        {
            ModelId = "gpt-4o-mini",
            Provider = "OpenAI",
            Temperature = 0.7f,
            MaxResponseTokens = 2000,
            SystemPromptTemplate = @"You are a helpful customer support agent for AgentFlow platform.

Your role:
1. Answer questions about the AgentFlow platform features
2. Troubleshoot common technical issues
3. Guide users through configuration and setup
4. Escalate complex issues to human support when needed

Always be professional, friendly, and helpful. If you're unsure, it's better to escalate than to provide incorrect information.",
            RequiresToolExecution = true
        };

        var loop1 = new AgentLoopConfig
        {
            MaxIterations = 10,
            MaxExecutionTime = TimeSpan.FromMinutes(2),
            ToolCallTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            AllowParallelToolCalls = false,
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var memory1 = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 3600,
            EnableLongTermMemory = false,
            EnableVectorMemory = false
        };

        var tools1 = new List<ToolBinding>
        {
            new()
            {
                ToolId = "search-knowledge-base",
                ToolName = "Search Knowledge Base",
                ToolVersion = "1.0.0",
                IsEnabled = true,
                MaxCallsPerExecution = 5
            },
            new()
            {
                ToolId = "create-support-ticket",
                ToolName = "Create Support Ticket",
                ToolVersion = "1.0.0",
                IsEnabled = true,
                MaxCallsPerExecution = 2
            }
        }.AsReadOnly();

        var tags1 = new[] { "demo", "customer-support", "production-ready" }.ToList().AsReadOnly();

        var agent1Result = AgentDefinition.Create(
            tenantId: tenantId,
            name: "Customer Support Agent - Demo",
            description: "AI-powered customer support agent that handles inquiries, troubleshoots issues, and escalates to humans when needed.",
            brain: brain1,
            loopConfig: loop1,
            memory: memory1,
            session: null,
            ownerUserId: demoUser
        );

        if (!agent1Result.IsSuccess)
        {
            Console.WriteLine($"❌ Failed to create Customer Support Agent: {agent1Result.Error!.Message}");
            return;
        }

        var agent1 = agent1Result.Value;
        agent1.ReplaceTools(tools1);
        agent1.SetTags(tags1);
        agent1.Publish(demoUser);

        await agentRepo.InsertAsync(agent1);

        // ═══════════════════════════════════════════════════════════════════
        // DEMO AGENT 2: Code Review Agent
        // ═══════════════════════════════════════════════════════════════════
        
        var brain2 = new BrainConfiguration
        {
            ModelId = "gpt-4o",
            Provider = "OpenAI",
            Temperature = 0.3f,
            MaxResponseTokens = 4000,
            SystemPromptTemplate = @"You are an expert code reviewer with deep knowledge of software engineering best practices.

Your responsibilities:
1. Analyze code for bugs, security vulnerabilities, and performance issues
2. Check compliance with coding standards and conventions
3. Suggest improvements for code quality and maintainability
4. Identify potential edge cases and error handling gaps

Provide constructive, specific feedback. Always explain WHY something should be changed.",
            RequiresToolExecution = true
        };

        var loop2 = new AgentLoopConfig
        {
            MaxIterations = 5,
            MaxExecutionTime = TimeSpan.FromMinutes(3),
            ToolCallTimeout = TimeSpan.FromSeconds(60),
            MaxRetries = 3,
            AllowParallelToolCalls = false,
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var memory2 = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 1800,
            EnableLongTermMemory = true,
            EnableVectorMemory = true,
            VectorCollectionName = "code-patterns-db",
            VectorSearchTopK = 5,
            VectorMinRelevanceScore = 0.75f
        };

        var tools2 = new List<ToolBinding>
        {
            new()
            {
                ToolId = "static-code-analysis",
                ToolName = "Static Code Analyzer",
                ToolVersion = "1.0.0",
                IsEnabled = true,
                MaxCallsPerExecution = 10
            },
            new()
            {
                ToolId = "github-api",
                ToolName = "GitHub API",
                ToolVersion = "1.0.0",
                IsEnabled = true,
                MaxCallsPerExecution = 5
            }
        }.AsReadOnly();

        var tags2 = new[] { "demo", "code-review", "devops" }.ToList().AsReadOnly();

        var agent2Result = AgentDefinition.Create(
            tenantId: tenantId,
            name: "Code Review Agent - Demo",
            description: "Automated code review agent that analyzes pull requests, suggests improvements, and enforces coding standards.",
            brain: brain2,
            loopConfig: loop2,
            memory: memory2,
            session: null,
            ownerUserId: demoUser
        );

        if (!agent2Result.IsSuccess)
        {
            Console.WriteLine($"❌ Failed to create Code Review Agent: {agent2Result.Error!.Message}");
            return;
        }

        var agent2 = agent2Result.Value;
        agent2.ReplaceTools(tools2);
        agent2.SetTags(tags2);
        agent2.Publish(demoUser);

        await agentRepo.InsertAsync(agent2);

        Console.WriteLine("✅ Demo seed data created successfully:");
        Console.WriteLine("   - Customer Support Agent");
        Console.WriteLine("   - Code Review Agent");
    }
}
