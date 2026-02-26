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
        if (existingAgents.Any(a => a.Name.Contains("Demo v2"))) return; // Already seeded

        // ═══════════════════════════════════════════════════════════════════
        // DEMO AGENT 1: Conversational AI Assistant (No Tools - Pure Chat)
        // ═══════════════════════════════════════════════════════════════════
       
        var brain1 = new BrainConfiguration
        {
            ModelId = "gpt-4o-mini",
            Provider = "OpenAI",
            Temperature = 0.7f,
            MaxResponseTokens = 2000,
            SystemPromptTemplate = @"You are AgentFlow Assistant, a helpful and conversational AI assistant.

Your capabilities:
1. Answer questions about AgentFlow platform features and capabilities
2. Explain concepts in AI agents, orchestration, and workflow automation
3. Provide guidance on best practices for building reliable AI systems
4. Maintain context throughout the conversation and remember what we've discussed

Communication style:
- Be friendly, professional, and helpful
- Use clear, concise language
- Ask clarifying questions when needed
- Remember the conversation history to provide contextual responses

If users ask about something outside your knowledge, be honest about your limitations.",
            RequiresToolExecution = false // Pure conversational agent
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

        // 🎯 SESSION CONFIG - Enable conversation history
        var session1 = new SessionConfig
        {
            EnableThreads = true,
            DefaultThreadTtl = TimeSpan.FromHours(1), // 1 hour
            MaxTurnsPerThread = 50,
            ContextWindowSize = 10,
            AutoCreateThread = true,
            EnableSummarization = true
        };

        var tags1 = new[] { "demo", "conversational", "production-ready", "no-tools" }.ToList().AsReadOnly();

        var agent1Result = AgentDefinition.Create(
            tenantId: tenantId,
            name: "AgentFlow Assistant - Demo v2",
            description: "Conversational AI assistant with Thread support for maintaining conversation history. Provides helpful information about AgentFlow platform. No tools required - pure chat interface.",
            brain: brain1,
            loopConfig: loop1,
            memory: memory1,
            session: session1, // ✅ Session enabled
            ownerUserId: demoUser
        );

        if (!agent1Result.IsSuccess)
        {
            Console.WriteLine($"❌ Failed to create AgentFlow Assistant: {agent1Result.Error!.Message}");
            return;
        }

        var agent1 = agent1Result.Value!;
        agent1.SetTags(tags1);
        agent1.Publish(demoUser);

        await agentRepo.InsertAsync(agent1);

        // ═══════════════════════════════════════════════════════════════════
        // DEMO AGENT 2: Technical Q&A Expert
        // ═══════════════════════════════════════════════════════════════════
        
        var brain2 = new BrainConfiguration
        {
            ModelId = "gpt-4o-mini",
            Provider = "OpenAI",
            Temperature = 0.3f,
            MaxResponseTokens = 3000,
            SystemPromptTemplate = @"You are a Technical Expert specialized in software engineering, AI/ML, and system architecture.

Your expertise includes:
1. Software design patterns and best practices
2. AI/ML concepts, models, and deployment strategies
3. System architecture, scalability, and reliability
4. API design, microservices, and distributed systems
5. Cloud platforms and DevOps practices

Approach:
- Provide technical, accurate, and detailed explanations
- Include code examples when relevant
- Explain trade-offs and alternatives
- Reference industry standards and best practices
- Maintain conversation context to build on previous topics

Always be precise and thorough in your responses.",
            RequiresToolExecution = false
        };

        var loop2 = new AgentLoopConfig
        {
            MaxIterations = 8,
            MaxExecutionTime = TimeSpan.FromMinutes(3),
            ToolCallTimeout = TimeSpan.FromSeconds(60),
            MaxRetries = 3,
            AllowParallelToolCalls = false,
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var memory2 = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 7200, // 2 hours
            EnableLongTermMemory = false,
            EnableVectorMemory = false
        };

        var session2 = new SessionConfig
        {
            EnableThreads = true,
            DefaultThreadTtl = TimeSpan.FromHours(2), // 2 hours
            MaxTurnsPerThread = 100,
            ContextWindowSize = 15, // More context for technical discussions
            AutoCreateThread = true,
            EnableSummarization = true
        };

        var tags2 = new[] { "demo", "technical", "expert", "q-and-a" }.ToList().AsReadOnly();

        var agent2Result = AgentDefinition.Create(
            tenantId: tenantId,
            name: "Technical Expert - Demo v2",
            description: "Expert AI assistant with Thread support for technical Q&A, code review, and architecture discussions. Maintains deep conversation context across multiple turns for complex technical topics.",
            brain: brain2,
            loopConfig: loop2,
            memory: memory2,
            session: session2,
            ownerUserId: demoUser
        );

        if (!agent2Result.IsSuccess)
        {
            Console.WriteLine($"❌ Failed to create Technical Expert: {agent2Result.Error!.Message}");
            return;
        }

        var agent2 = agent2Result.Value!;
        agent2.SetTags(tags2);
        agent2.Publish(demoUser);

        await agentRepo.InsertAsync(agent2);

        Console.WriteLine("✅ Demo seed data created successfully:");
        Console.WriteLine("   - AgentFlow Assistant v2 (Conversational, Thread Support Enabled)");
        Console.WriteLine("   - Technical Expert v2 (Deep Technical Q&A, Thread Support Enabled)");
    }
}

