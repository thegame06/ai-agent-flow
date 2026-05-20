using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.ValueObjects;
using AgentFlow.Domain.Repositories;
using AgentFlow.Abstractions;
using AgentFlow.Abstractions.Workflow;
using AgentFlow.Api.Workflow;
using AgentFlow.Security;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

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
            workflowSteps: null,
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
            workflowSteps: null,
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

    // ═══════════════════════════════════════════════════════════════════════════
    // SYSTEM AGENTS SEED
    // Seeds the 3 platform-managed agents (Router, WorkflowBrain template,
    // Config Assistant). These are tenant-level but platform-owned and cannot
    // be deleted by users.
    // Guard: skips if any agent with AgentSystemRole.Router already exists.
    // ═══════════════════════════════════════════════════════════════════════════
    public static async Task SeedSystemAgentsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentDefinitionRepository>();
        var tenantId = "tenant-1";
        var systemUser = "platform@agentflow.dev";

        var existing = await agentRepo.GetAllAsync(tenantId, 0, 200);

        // ── MCP tool definitions (used in seed creation and data migration) ──
        var routerMcpTools = new (string name, string description)[]
        {
            ("af_list_workflows",      "List all available workflows and their trigger events"),
            ("af_trigger_workflow",    "Trigger a workflow by name with a payload"),
            ("af_get_session_context", "Get the current session context (user name, channel, window)"),
        };

        var configMcpTools = new (string name, string description)[]
        {
            ("af_list_agents",        "List all agents in the tenant"),
            ("af_get_agent",          "Get full detail of a specific agent"),
            ("af_list_workflows",     "List all workflows"),
            ("af_diagnose_workflow",  "Diagnose issues in a workflow"),
            ("af_diagnose_channel",   "Diagnose issues in a channel configuration"),
            ("af_scaffold_workflow",  "Generate a workflow scaffold from a description"),
            ("af_list_integrations",  "List available MCP servers and integrations"),
        };

        IReadOnlyList<WorkflowStep> BuildRouterSubflow() => new List<WorkflowStep>
        {
            new()
            {
                Id = "router-think-intent",
                Type = "think",
                Label = "Detect intent",
                Description = "Analyze incoming message and map it to the closest configured intent.",
                Config = new Dictionary<string, object> { ["mode"] = "intent_classification" }
            },
            new()
            {
                Id = "router-tool-list-workflows",
                Type = "tool_call",
                Label = "List workflows",
                Description = "Fetch available workflows and trigger contracts.",
                Config = new Dictionary<string, object> { ["tool"] = "af_list_workflows" }
            },
            new()
            {
                Id = "router-decide-route",
                Type = "decide",
                Label = "Decide routing",
                Description = "Select workflow/agent target or fallback clarification path.",
                Config = new Dictionary<string, object> { ["output"] = "routing_handoff" }
            }
        }.AsReadOnly();

        IReadOnlyList<WorkflowStep> BuildWorkflowBrainSubflow() => new List<WorkflowStep>
        {
            new()
            {
                Id = "brain-think-goal",
                Type = "think",
                Label = "Understand goal",
                Description = "Understand workflow goal and missing business data.",
                Config = new Dictionary<string, object> { ["mode"] = "data_collection" }
            },
            new()
            {
                Id = "brain-act-customer",
                Type = "act",
                Label = "Collect data",
                Description = "Ask customer for missing fields and validate values step-by-step.",
                Config = new Dictionary<string, object> { ["style"] = "guided_dialog" }
            },
            new()
            {
                Id = "brain-aggregate-output",
                Type = "aggregate",
                Label = "Build structured output",
                Description = "Return normalized structured output for downstream workflow nodes.",
                Config = new Dictionary<string, object> { ["output"] = "structured_json" }
            }
        }.AsReadOnly();

        IReadOnlyList<WorkflowStep> BuildConfigAssistantSubflow() => new List<WorkflowStep>
        {
            new()
            {
                Id = "config-assistant-discover",
                Type = "tool_call",
                Label = "Inspect tenant state",
                Description = "Inspect agents/workflows/channels before giving recommendations.",
                Config = new Dictionary<string, object> { ["tools"] = new[] { "af_list_agents", "af_list_workflows" } }
            },
            new()
            {
                Id = "config-assistant-diagnose",
                Type = "decide",
                Label = "Diagnose gaps",
                Description = "Diagnose missing pieces and prioritize actionable fixes.",
                Config = new Dictionary<string, object> { ["mode"] = "diagnostic" }
            },
            new()
            {
                Id = "config-assistant-guide",
                Type = "act",
                Label = "Guide user actions",
                Description = "Produce explicit step-by-step instructions and optional scaffold guidance.",
                Config = new Dictionary<string, object> { ["output"] = "guided_steps" }
            }
        }.AsReadOnly();

        IReadOnlyList<WorkflowStep> SubflowFor(AgentSystemRole role) => role switch
        {
            AgentSystemRole.Router => BuildRouterSubflow(),
            AgentSystemRole.WorkflowBrain => BuildWorkflowBrainSubflow(),
            AgentSystemRole.ConfigAssistant => BuildConfigAssistantSubflow(),
            _ => Array.Empty<WorkflowStep>()
        };

        // ── Helper: bind a list of MCP tools to an agent ──
        // ToolId convention: "mcp:{serverName}:{toolName}"
        void BindMcpTools(AgentDefinition agent, string serverName, IEnumerable<(string name, string description)> tools)
        {
            foreach (var (name, _) in tools)
            {
                // Skip if already bound
                if (agent.AuthorizedTools.Any(t => t.ToolName == name)) continue;
                agent.AddTool(new ToolBinding
                {
                    ToolId               = $"mcp:{serverName}:{name}",
                    ToolName             = name,
                    ToolVersion          = "1.0",
                    IsEnabled            = true,
                    MaxCallsPerExecution = 5,
                    GrantedPermissions   = new[] { "tool:execute:low" }.ToList().AsReadOnly()
                });
            }
        }

        // ── DATA MIGRATION: fix any existing system agents with IsSystemAgent=false ──
        // This handles cases where a previous seed ran before SetSystemRole set IsSystemAgent=true.
        var needsFix = existing
            .Where(a => a.SystemRole != AgentSystemRole.Custom && !a.IsSystemAgent)
            .ToList();
        foreach (var broken in needsFix)
        {
            broken.SetSystemRole(broken.SystemRole); // re-applies IsSystemAgent = true
            var fix = await agentRepo.UpdateAsync(broken);
            Console.WriteLine(fix.IsSuccess
                ? $"🔧 [Seed] Fixed '{broken.Name}' → IsSystemAgent=true"
                : $"❌ [Seed] Failed to fix '{broken.Name}': {fix.Error?.Message}");
        }

        // ── DATA MIGRATION: wire MCP tools to existing system agents that have no tools ──
        var needsToolWiring = existing
            .Where(a => a.SystemRole is AgentSystemRole.Router or AgentSystemRole.ConfigAssistant
                     && !a.AuthorizedTools.Any(t => t.ToolId.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var agent in needsToolWiring)
        {
            var toolsToAdd = agent.SystemRole == AgentSystemRole.Router ? routerMcpTools : configMcpTools;
            BindMcpTools(agent, "agentflow-mcp-server", toolsToAdd);
            var fix = await agentRepo.UpdateAsync(agent);
            Console.WriteLine(fix.IsSuccess
                ? $"🔧 [Seed] Wired MCP tools to '{agent.Name}'"
                : $"❌ [Seed] Failed to wire tools to '{agent.Name}': {fix.Error?.Message}");
        }

        // ── DATA MIGRATION: ensure sub-flow steps exist on system agents ──
        var subflowRoles = new[] { AgentSystemRole.Router, AgentSystemRole.WorkflowBrain, AgentSystemRole.ConfigAssistant };
        var needsSubflowWiring = existing
            .Where(a => subflowRoles.Contains(a.SystemRole) && (a.WorkflowSteps == null || a.WorkflowSteps.Count == 0))
            .ToList();
        foreach (var agent in needsSubflowWiring)
        {
            var subflow = SubflowFor(agent.SystemRole);
            if (subflow.Count == 0) continue;

            var update = agent.Update(
                name: agent.Name,
                description: agent.Description,
                brain: agent.Brain,
                loopConfig: agent.LoopConfig,
                memory: agent.Memory,
                session: agent.Session,
                workflowSteps: subflow,
                tools: agent.AuthorizedTools,
                tags: agent.Tags,
                updatedBy: systemUser,
                shadowAgentId: agent.ShadowAgentId,
                canaryAgentId: agent.CanaryAgentId,
                canaryWeight: agent.CanaryWeight);

            if (!update.IsSuccess)
            {
                Console.WriteLine($"❌ [Seed] Failed to attach sub-flow to '{agent.Name}': {update.Error?.Message}");
                continue;
            }

            var saved = await agentRepo.UpdateAsync(agent);
            Console.WriteLine(saved.IsSuccess
                ? $"🔧 [Seed] Attached sub-flow to '{agent.Name}' ({agent.SystemRole})"
                : $"❌ [Seed] Failed to persist sub-flow for '{agent.Name}': {saved.Error?.Message}");
        }

        // ── GUARD: only create agents that are missing ──
        var hasRouter = existing.Any(a => a.SystemRole == AgentSystemRole.Router);
        var hasBrain  = existing.Any(a => a.SystemRole == AgentSystemRole.WorkflowBrain);
        var hasConfig = existing.Any(a => a.SystemRole == AgentSystemRole.ConfigAssistant);

        if (hasRouter && hasBrain && hasConfig)
        {
            if (needsFix.Count > 0 || needsSubflowWiring.Count > 0)
                Console.WriteLine($"✅ [Seed] System agents present. {needsFix.Count} role fix(es), {needsSubflowWiring.Count} sub-flow fix(es).");
            else
                Console.WriteLine("✅ [Seed] System agents already seeded correctly. Skipping.");
            return;
        }

        async Task InsertSystemAgent(AgentDefinition agent, string label)
        {
            // Note: IsSystemAgent=true (set by SetSystemRole) allows Publish to
            // bypass the tools-required check. Tools are wired at execution time.
            var publishResult = agent.Publish(systemUser);
            if (!publishResult.IsSuccess)
                Console.WriteLine($"⚠️  [Seed] {label} Publish warning: {publishResult.Error?.Message}");

            var insertResult = await agentRepo.InsertAsync(agent);
            if (!insertResult.IsSuccess)
                Console.WriteLine($"❌ [Seed] {label} InsertAsync failed: {insertResult.Error?.Message}");
            else
                Console.WriteLine($"✅ [Seed] Inserted {label} [{agent.Id}] (IsSystemAgent={agent.IsSystemAgent}, status={agent.Status})");
        }

        // ─────────────────────────────────────────────────────────────────────
        // AGENT 1: Router
        // Receives ALL incoming channel messages. Detects customer intent and
        // decides which workflow to trigger using af_list_workflows +
        // af_trigger_workflow from the AgentFlow MCP Server.
        // ONE per tenant. IsSystemAgent = true.
        // ─────────────────────────────────────────────────────────────────────
        var routerBrain = new BrainConfiguration
        {
            ModelId = "gpt-4o-mini",
            Provider = "OpenAI",
            Temperature = 0.1f, // Low temperature: deterministic routing decisions
            MaxResponseTokens = 500,
            RequiresToolExecution = true,
            SystemPromptTemplate = @"You are the AgentFlow Router — the first point of contact for every customer message.

Your ONLY responsibilities are:
1. Detect the customer's intent from their message.
2. Match the intent to an available workflow using the af_list_workflows tool.
3. Trigger the correct workflow using the af_trigger_workflow tool.
4. If no workflow matches, respond with a polite message and list the available options.

Customer context you will always receive:
- sessionContext.displayName: Customer's name (use it to greet them)
- sessionContext.isWindowOpen: If false, you CANNOT send free text — a template was already sent
- sessionContext.channelType: The channel (WhatsApp, WebChat, etc.)

Rules:
- NEVER make up workflows that don't exist. Always call af_list_workflows first.
- NEVER answer questions outside your routing role — you are not a general assistant.
- If the customer's intent is unclear, ask ONE clarifying question.
- Always respond in the same language the customer used.
- Keep responses short (max 2 sentences before triggering a workflow).

Tool usage sequence:
1. af_list_workflows → know what is available
2. af_trigger_workflow → fire the matched workflow
3. af_get_session_context → enrich response with customer data if needed"
        };

        var routerLoop = new AgentLoopConfig
        {
            MaxIterations = 5,
            MaxExecutionTime = TimeSpan.FromSeconds(30),
            ToolCallTimeout = TimeSpan.FromSeconds(15),
            MaxRetries = 2,
            AllowParallelToolCalls = false,
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var routerMemory = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 1800,
            EnableLongTermMemory = false,
            EnableVectorMemory = false
        };

        var routerSession = new SessionConfig
        {
            EnableThreads = true,
            DefaultThreadTtl = TimeSpan.FromHours(24),
            MaxTurnsPerThread = 3, // Router should resolve quickly
            ContextWindowSize = 3,
            AutoCreateThread = true,
            EnableSummarization = false
        };

        // ── Helper: bind a list of MCP tools to an agent ──
        // (defined at top of method, see above)

        if (!hasRouter)
        {
            var routerResult = AgentDefinition.Create(
                tenantId, "AgentFlow Router",
                "Platform-managed Router agent. Receives all incoming channel messages, detects customer intent, and triggers the appropriate workflow via the AgentFlow MCP Server.",
                routerBrain, routerLoop, routerMemory, routerSession, BuildRouterSubflow(), systemUser);

            if (!routerResult.IsSuccess) { Console.WriteLine($"❌ [Seed] Router Create failed: {routerResult.Error!.Message}"); }
            else
            {
                var router = routerResult.Value!;
                router.SetTags(new[] { "system", "router", "platform-managed" }.ToList().AsReadOnly());
                router.SetSystemRole(AgentSystemRole.Router); // sets IsSystemAgent=true
                BindMcpTools(router, "agentflow-mcp-server", routerMcpTools);
                await InsertSystemAgent(router, "Router");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // AGENT 2: Workflow Brain (template)
        // The default "cerebro" for workflows. Users clone or reference this
        // agent in their ai.agent nodes. IsSystemAgent = false — users own and
        // customize their WorkflowBrain agents.
        // This seed provides the first ready-to-use brain so workflows can start
        // working immediately without manual agent creation.
        // ─────────────────────────────────────────────────────────────────────
        var brainBrain = new BrainConfiguration
        {
            ModelId = "gpt-4o",
            Provider = "OpenAI",
            Temperature = 0.4f,
            MaxResponseTokens = 1500,
            RequiresToolExecution = false,
            SystemPromptTemplate = @"You are a Workflow Brain — the conversational agent at the heart of a business workflow.

Your role is to:
1. Collect all information required by the workflow from the customer.
2. Validate that the information is complete and correct.
3. Return structured JSON data that downstream workflow nodes will consume.
4. Keep the customer informed of progress in a friendly, professional manner.

Customer context you will always receive:
- sessionContext.displayName: Use the customer's name in every message.
- sessionContext.channelType: Adapt your response style to the channel.
- sessionContext.isWindowOpen: If false, keep responses extremely brief.

Output format when data collection is complete:
Return a JSON block wrapped in ```json ... ``` with the collected fields.
Always include a ""status"" field: ""complete"" or ""incomplete"".
If ""incomplete"", include a ""missingFields"" array.

Communication rules:
- Ask for ONE piece of information at a time.
- Confirm collected data back to the customer before proceeding.
- If the customer provides incorrect data, explain what is wrong and ask again.
- Never expose system internals or JSON structure to the customer.
- Respond in the same language the customer uses."
        };

        var brainLoop = new AgentLoopConfig
        {
            MaxIterations = 20,
            MaxExecutionTime = TimeSpan.FromMinutes(5),
            ToolCallTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            AllowParallelToolCalls = false,
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var brainMemory = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 7200,
            EnableLongTermMemory = false,
            EnableVectorMemory = false
        };

        var brainSession = new SessionConfig
        {
            EnableThreads = true,
            DefaultThreadTtl = TimeSpan.FromHours(24),
            MaxTurnsPerThread = 50,
            ContextWindowSize = 10,
            AutoCreateThread = true,
            EnableSummarization = true
        };

        if (!hasBrain)
        {
            var brainResult = AgentDefinition.Create(
                tenantId, "Workflow Brain - Default",
                "Default WorkflowBrain agent for business logic execution. Assign this agent to ai.agent nodes in your workflows, or clone it to create specialized versions per workflow.",
                brainBrain, brainLoop, brainMemory, brainSession, BuildWorkflowBrainSubflow(), systemUser);

            if (!brainResult.IsSuccess) { Console.WriteLine($"❌ [Seed] WorkflowBrain Create failed: {brainResult.Error!.Message}"); }
            else
            {
                var brain = brainResult.Value!;
                brain.SetTags(new[] { "system", "workflow-brain", "default", "cloneable" }.ToList().AsReadOnly());
                brain.SetSystemRole(AgentSystemRole.WorkflowBrain); // sets IsSystemAgent=true
                await InsertSystemAgent(brain, "WorkflowBrain");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // AGENT 3: Config Assistant
        // Helps users build and configure workflows and agents via natural
        // language. Uses the AgentFlow MCP Server tools to inspect the tenant's
        // current configuration and detect what is missing.
        // IsSystemAgent = true.
        // ─────────────────────────────────────────────────────────────────────
        var configBrain = new BrainConfiguration
        {
            ModelId = "gpt-4o",
            Provider = "OpenAI",
            Temperature = 0.5f,
            MaxResponseTokens = 2000,
            RequiresToolExecution = true,
            SystemPromptTemplate = @"You are the AgentFlow Config Assistant — a platform configuration expert and workflow architect.

Your mission is to help users build, configure, and troubleshoot AgentFlow workflows and agents through natural language conversation.

You have access to these AgentFlow tools (via the AgentFlow MCP Server):
- af_list_agents: See all existing agents (system + user)
- af_get_agent: Inspect a specific agent's configuration
- af_list_workflows: See all workflows and their status
- af_diagnose_workflow: Find issues in a workflow (broken nodes, missing agents, draft status)
- af_diagnose_channel: Check if a channel has Router assigned, session window set, and is active
- af_list_integrations: List available MCP servers and external tools
- af_scaffold_workflow: Generate a workflow JSON scaffold from a natural language description

How to help users:
1. START by calling af_list_agents and af_list_workflows to understand the current state.
2. DIAGNOSE before prescribing: use af_diagnose_workflow and af_diagnose_channel to find issues.
3. GUIDE step-by-step: break complex configurations into small achievable tasks.
4. EXPLAIN WHY: when something is misconfigured, explain the business impact.
5. SCAFFOLD when asked: use af_scaffold_workflow to generate a starting point.

You CANNOT directly create or modify workflows — you guide the user to do it in the Workflow Designer.
You CAN generate scaffold JSON that the user can copy into the Designer.

Common issues to detect and explain:
- Workflows in Draft status (not triggerable)
- ai.agent nodes without an assigned WorkflowBrain agent
- Channels without a Router agent assigned
- Missing session window configuration
- No published workflows matching the Router's available events
- Integrations not connected to any agent's tool set

Always be concrete: tell the user EXACTLY what to click or configure."
        };

        var configLoop = new AgentLoopConfig
        {
            MaxIterations = 15,
            MaxExecutionTime = TimeSpan.FromMinutes(3),
            ToolCallTimeout = TimeSpan.FromSeconds(20),
            MaxRetries = 2,
            AllowParallelToolCalls = true, // Can call af_list_agents + af_list_workflows in parallel
            HitlConfig = new HumanInTheLoopConfig { Enabled = false }
        };

        var configMemory = new MemoryConfig
        {
            EnableWorkingMemory = true,
            WorkingMemoryTtlSeconds = 3600,
            EnableLongTermMemory = false,
            EnableVectorMemory = false
        };

        var configSession = new SessionConfig
        {
            EnableThreads = true,
            DefaultThreadTtl = TimeSpan.FromHours(2),
            MaxTurnsPerThread = 100,
            ContextWindowSize = 15,
            AutoCreateThread = true,
            EnableSummarization = true
        };

        if (!hasConfig)
        {
            var configResult = AgentDefinition.Create(
                tenantId, "AgentFlow Config Assistant",
                "Platform-managed Config Assistant. Guides users in building and configuring workflows, agents, and channels via natural language. Uses the AgentFlow MCP Server to inspect and diagnose the tenant configuration.",
                configBrain, configLoop, configMemory, configSession, BuildConfigAssistantSubflow(), systemUser);

            if (!configResult.IsSuccess) { Console.WriteLine($"❌ [Seed] ConfigAssistant Create failed: {configResult.Error!.Message}"); }
            else
            {
                var configAssistant = configResult.Value!;
                configAssistant.SetTags(new[] { "system", "config-assistant", "platform-managed" }.ToList().AsReadOnly());
                configAssistant.SetSystemRole(AgentSystemRole.ConfigAssistant); // sets IsSystemAgent=true
                BindMcpTools(configAssistant, "agentflow-mcp-server", configMcpTools);
                await InsertSystemAgent(configAssistant, "ConfigAssistant");
            }
        }

        await SeedTenantMcpDefaultsAsync(scope.ServiceProvider, tenantId, systemUser);
        await SeedSalesHappyPathAsync(scope.ServiceProvider, tenantId, systemUser);
        await SeedBusinessStarterPackAsync(scope.ServiceProvider, tenantId, systemUser);

        Console.WriteLine("✅ [Seed] System agents seed complete.");
    }

    private static async Task SeedTenantMcpDefaultsAsync(IServiceProvider services, string tenantId, string updatedBy)
    {
        var store = services.GetRequiredService<ITenantMcpSettingsStore>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var configuredServers = configuration
            .GetSection("Mcp:Servers")
            .GetChildren()
            .Select(x => x["Name"])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredServers.Length == 0)
            return;

        var current = await store.GetAsync(tenantId);
        var requiredServers = configuredServers
            .Where(name => string.Equals(name, "agentflow-mcp-server", StringComparison.OrdinalIgnoreCase));
        var mergedAllowedServers = current.AllowedServers
            .Concat(requiredServers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (current.Enabled &&
            mergedAllowedServers.SequenceEqual(current.AllowedServers, StringComparer.OrdinalIgnoreCase))
            return;

        await store.SaveAsync(current with
        {
            TenantId = tenantId,
            Enabled = true,
            Runtime = "MicrosoftAgentFramework",
            AllowedServers = mergedAllowedServers,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy
        });
    }

    private static async Task SeedSalesHappyPathAsync(IServiceProvider services, string tenantId, string ownerUser)
    {
        var agentRepo = services.GetRequiredService<IAgentDefinitionRepository>();
        var workflowStore = services.GetRequiredService<IWorkflowStudioStore>();

        var existingAgents = await agentRepo.GetAllAsync(tenantId, 0, 500);
        var salesAssistant = existingAgents.FirstOrDefault(a => string.Equals(a.Name, "Asistente de ventas", StringComparison.OrdinalIgnoreCase));

        if (salesAssistant is null)
        {
            var brain = new BrainConfiguration
            {
                ModelId = "gpt-4o-mini",
                Provider = "OpenAI",
                Temperature = 0.2f,
                MaxResponseTokens = 1200,
                RequiresToolExecution = true,
                SystemPromptTemplate = @"Eres el asistente de ventas de Annonai.

Objetivo:
- atender leads que escriben por un canal
- identificar que producto quieren
- buscar productos en inventario
- confirmar cantidades y datos del cliente
- crear la venta
- emitir factura
- si aplica, enviarla por WhatsApp

Reglas:
- responde en el idioma del cliente
- haz preguntas cortas y una por turno
- no inventes productos, precios ni stock; usa herramientas
- antes de vender, resuelve o crea el cliente en CRM
- antes de facturar, confirma el total con el cliente
- si falta un dato, dilo claramente
- cuando no haya match comercial, explica que no encontraste coincidencia y pide aclaracion

Herramientas a usar cuando aplique:
- af_commerce_resolve_party
- af_commerce_assert_active_session
- af_commerce_search_inventory
- af_commerce_calculate_sale
- af_commerce_create_sale
- af_commerce_create_invoice
- af_commerce_send_invoice_whatsapp
- af_commerce_send_conversation_message

Nunca expongas detalles tecnicos internos al cliente."
            };

            var loop = new AgentLoopConfig
            {
                MaxIterations = 12,
                MaxExecutionTime = TimeSpan.FromMinutes(3),
                ToolCallTimeout = TimeSpan.FromSeconds(25),
                MaxRetries = 2,
                AllowParallelToolCalls = false,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            };

            var memory = new MemoryConfig
            {
                EnableWorkingMemory = true,
                WorkingMemoryTtlSeconds = 3600,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            };

            var session = new SessionConfig
            {
                EnableThreads = true,
                DefaultThreadTtl = TimeSpan.FromHours(24),
                MaxTurnsPerThread = 60,
                ContextWindowSize = 12,
                AutoCreateThread = true,
                EnableSummarization = true
            };

            var create = AgentDefinition.Create(
                tenantId,
                "Asistente de ventas",
                "Asistente comercial preconfigurado para calificar leads, cotizar, crear ventas y emitir facturas.",
                brain,
                loop,
                memory,
                session,
                null,
                ownerUser);

            if (create.IsSuccess)
            {
                salesAssistant = create.Value!;
                salesAssistant.SetTags(new[] { "sales", "commerce", "seed", "happy-path" }.ToList().AsReadOnly());

                foreach (var toolName in new[]
                {
                    "af_commerce_resolve_party",
                    "af_commerce_assert_active_session",
                    "af_commerce_search_inventory",
                    "af_commerce_calculate_sale",
                    "af_commerce_create_sale",
                    "af_commerce_create_invoice",
                    "af_commerce_send_invoice_whatsapp",
                    "af_commerce_send_conversation_message"
                })
                {
                    salesAssistant.AddTool(new ToolBinding
                    {
                        ToolId = $"mcp:agentflow-mcp-server:{toolName}",
                        ToolName = toolName,
                        ToolVersion = "1.0",
                        IsEnabled = true,
                        MaxCallsPerExecution = 6,
                        GrantedPermissions = new[] { "tool:execute:low" }.ToList().AsReadOnly()
                    });
                }

                var publish = salesAssistant.Publish(ownerUser);
                if (publish.IsSuccess)
                    await agentRepo.InsertAsync(salesAssistant);
            }
        }

        if (salesAssistant is null)
            return;

        const string workflowId = "wf-sales-happy-path";
        var existingDefinition = await workflowStore.GetDefinitionAsync(tenantId, workflowId, CancellationToken.None);
        if (existingDefinition is null)
        {
            var definitionJson = $$$"""
            {
              "start": {
                "intents": [
                  {
                    "id": "buy-product",
                    "label": "comprar producto",
                    "description": "Cliente quiere comprar, cotizar o pedir precio de un producto.",
                    "examples": [
                      "quiero comprar",
                      "necesito una cotizacion",
                      "que precio tiene",
                      "quiero factura"
                    ],
                    "triggerSource": "message",
                    "confidenceThreshold": 0.7
                  }
                ]
              },
              "activities": [
                {
                  "id": "sales-agent",
                  "type": "ai.agent",
                  "config": {
                    "agentId": "{{{salesAssistant.Id}}}",
                    "agentName": "Asistente de ventas",
                    "input": "{{payload.content}}",
                    "context": "{{payload.channel}}"
                  }
                }
              ]
            }
            """;

            await workflowStore.UpsertTemplateAsync(new WorkflowTemplateContract
            {
                Id = "tpl-sales-happy-path",
                TenantId = tenantId,
                Name = "Base ventas por canal",
                Description = "Plantilla inicial para atender leads, cotizar y facturar desde una conversacion.",
                TriggerEventName = "connect.message.received",
                DefinitionJson = definitionJson,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = ownerUser
            }, CancellationToken.None);

            await workflowStore.UpsertDefinitionAsync(new WorkflowDefinitionContract
            {
                Id = workflowId,
                TenantId = tenantId,
                Name = "Ventas desde canal",
                TriggerEventName = "connect.message.received",
                Version = 1,
                Status = WorkflowDefinitionStatus.Published,
                DefinitionJson = definitionJson,
                Metadata = new Dictionary<string, string>
                {
                    ["seed"] = "true",
                    ["category"] = "sales",
                    ["designType"] = "workflow"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = ownerUser
            }, CancellationToken.None);
        }
    }

    private static async Task SeedBusinessStarterPackAsync(IServiceProvider services, string tenantId, string ownerUser)
    {
        var agentRepo = services.GetRequiredService<IAgentDefinitionRepository>();
        var workflowStore = services.GetRequiredService<IWorkflowStudioStore>();
        var channelRepo = services.GetRequiredService<IChannelDefinitionRepository>();
        var intentStore = services.GetRequiredService<IIntentRoutingStore>();

        var existingAgents = (await agentRepo.GetAllAsync(tenantId, 0, 500)).ToList();
        IReadOnlyList<WorkflowStep> BuildBusinessSubflow() => new List<WorkflowStep>
        {
            new()
            {
                Id = "intent-clarify",
                Type = "think",
                Label = "Entender necesidad",
                Description = "Detecta objetivo del cliente y datos faltantes.",
                Config = new Dictionary<string, object> { ["mode"] = "intent_and_gap_detection" }
            },
            new()
            {
                Id = "intent-collect",
                Type = "act",
                Label = "Recopilar datos",
                Description = "Solicita y valida un dato por turno.",
                Config = new Dictionary<string, object> { ["style"] = "guided_dialog" }
            },
            new()
            {
                Id = "intent-output",
                Type = "aggregate",
                Label = "Salida estructurada",
                Description = "Entrega salida estructurada para el workflow.",
                Config = new Dictionary<string, object> { ["output"] = "structured_json" }
            }
        }.AsReadOnly();

        async Task<AgentDefinition?> EnsureAgentAsync(
            string name,
            string description,
            string systemPrompt,
            string[] tags)
        {
            var existing = existingAgents.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;

            var brain = new BrainConfiguration
            {
                ModelId = "gpt-4o-mini",
                Provider = "OpenAI",
                Temperature = 0.25f,
                MaxResponseTokens = 1400,
                RequiresToolExecution = true,
                SystemPromptTemplate = systemPrompt
            };
            var loop = new AgentLoopConfig
            {
                MaxIterations = 12,
                MaxExecutionTime = TimeSpan.FromMinutes(3),
                ToolCallTimeout = TimeSpan.FromSeconds(25),
                MaxRetries = 2,
                AllowParallelToolCalls = false,
                HitlConfig = new HumanInTheLoopConfig { Enabled = false }
            };
            var memory = new MemoryConfig
            {
                EnableWorkingMemory = true,
                WorkingMemoryTtlSeconds = 3600,
                EnableLongTermMemory = false,
                EnableVectorMemory = false
            };
            var session = new SessionConfig
            {
                EnableThreads = true,
                DefaultThreadTtl = TimeSpan.FromHours(24),
                MaxTurnsPerThread = 50,
                ContextWindowSize = 10,
                AutoCreateThread = true,
                EnableSummarization = true
            };

            var create = AgentDefinition.Create(
                tenantId,
                name,
                description,
                brain,
                loop,
                memory,
                session,
                BuildBusinessSubflow(),
                ownerUser);
            if (!create.IsSuccess) return null;

            var agent = create.Value!;
            agent.SetTags(tags.ToList().AsReadOnly());
            var publish = agent.Publish(ownerUser);
            if (!publish.IsSuccess) return null;
            await agentRepo.InsertAsync(agent);
            existingAgents.Add(agent);
            return agent;
        }

        var salesAgent = await EnsureAgentAsync(
            "Asistente de ventas",
            "Califica prospectos, cotiza productos y cierra ventas.",
            "Eres un agente comercial. Calificas al cliente, propones opciones y cierras la venta con mensajes claros y cortos.",
            new[] { "seed", "ventas", "commerce" });
        var billingAgent = await EnsureAgentAsync(
            "Asistente de facturacion y cobro",
            "Emite facturas, explica montos y confirma pagos.",
            "Eres un agente de facturacion y cobro. Emite factura, confirma total y guia al cliente hasta completar el pago.",
            new[] { "seed", "facturacion", "cobro" });
        var inventoryAgent = await EnsureAgentAsync(
            "Asistente de inventario",
            "Consulta disponibilidad, reserva y alternativas de inventario.",
            "Eres un agente de inventario. Verifica stock, propone alternativas y confirma tiempos de entrega.",
            new[] { "seed", "inventario" });
        var supportAgent = await EnsureAgentAsync(
            "Asistente de soporte",
            "Atiende incidencias, consultas postventa y escalaciones.",
            "Eres un agente de soporte. Diagnostica el problema, solicita datos clave y resuelve o escala cuando corresponda.",
            new[] { "seed", "soporte" });
        var fallbackAgent = await EnsureAgentAsync(
            "Asistente de respaldo",
            "Atiende casos sin intencion clara y solicita aclaraciones seguras.",
            "Eres agente de respaldo. Si no hay claridad, pide una aclaracion breve y redirige al equipo correcto.",
            new[] { "seed", "fallback", "respaldo" });

        if (salesAgent is null || billingAgent is null || inventoryAgent is null || supportAgent is null || fallbackAgent is null)
            return;

        async Task EnsureWorkflowAsync(string id, string name, string intentLabel, string intentDescription, string[] examples, string agentId)
        {
            var existing = await workflowStore.GetDefinitionAsync(tenantId, id, CancellationToken.None);
            if (existing is not null) return;

            var definitionJsonTemplate = $$"""
            {
              "start": {
                "intents": [
                  {
                    "id": "{{id}}-intent",
                    "label": "{{intentLabel}}",
                    "description": "{{intentDescription}}",
                    "examples": [{{string.Join(", ", examples.Select(e => $"\"{e}\""))}}],
                    "triggerSource": "message",
                    "confidenceThreshold": 0.7
                  }
                ]
              },
              "activities": [
                {
                  "id": "primary-agent",
                  "type": "ai.agent",
                  "config": {
                    "agentId": "{{agentId}}",
                    "input": "__PAYLOAD_CONTENT__",
                    "context": "__PAYLOAD_CHANNEL__"
                  }
                }
              ]
            }
            """;
            var definitionJson = definitionJsonTemplate
                .Replace("__PAYLOAD_CONTENT__", "{{payload.content}}", StringComparison.Ordinal)
                .Replace("__PAYLOAD_CHANNEL__", "{{payload.channel}}", StringComparison.Ordinal);

            await workflowStore.UpsertTemplateAsync(new WorkflowTemplateContract
            {
                Id = $"tpl-{id}",
                TenantId = tenantId,
                Name = name,
                Description = $"Template inicial para {intentLabel}.",
                TriggerEventName = "connect.message.received",
                DefinitionJson = definitionJson,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = ownerUser
            }, CancellationToken.None);

            await workflowStore.UpsertDefinitionAsync(new WorkflowDefinitionContract
            {
                Id = id,
                TenantId = tenantId,
                Name = name,
                TriggerEventName = "connect.message.received",
                Version = 1,
                Status = WorkflowDefinitionStatus.Published,
                DefinitionJson = definitionJson,
                Metadata = new Dictionary<string, string>
                {
                    ["seed"] = "true",
                    ["category"] = "starter-pack"
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = ownerUser
            }, CancellationToken.None);
        }

        await EnsureWorkflowAsync("wf-starter-sales", "Starter ventas", "comprar producto", "Cliente quiere comprar o cotizar.", new[] { "quiero comprar", "necesito cotizacion", "precio del producto" }, salesAgent.Id);
        await EnsureWorkflowAsync("wf-starter-billing", "Starter facturacion y cobro", "pagar factura", "Cliente quiere pagar o consultar factura.", new[] { "quiero pagar", "enviame la factura", "estado de mi factura" }, billingAgent.Id);
        await EnsureWorkflowAsync("wf-starter-inventory", "Starter inventario", "consultar inventario", "Cliente quiere disponibilidad o stock.", new[] { "tienen stock", "hay disponible", "disponibilidad" }, inventoryAgent.Id);
        await EnsureWorkflowAsync("wf-starter-support", "Starter soporte", "soporte postventa", "Cliente reporta incidencia o necesita soporte.", new[] { "tengo un problema", "necesito soporte", "mi pedido fallo" }, supportAgent.Id);

        var channels = await channelRepo.GetAllAsync(tenantId, CancellationToken.None);
        var firstChannel = channels.FirstOrDefault();
        if (firstChannel is null) return;

        var sourceAgentId = !string.IsNullOrWhiteSpace(firstChannel.RouterAgentId)
            ? firstChannel.RouterAgentId!
            : fallbackAgent.Id;
        var fallbackAgentId = firstChannel.Config.GetValueOrDefault("DefaultAgentId") ?? fallbackAgent.Id;
        var channelKey = firstChannel.Type.ToString().ToLowerInvariant();

        var seedRules = new[]
        {
            new { Key = "comprar_producto", Desc = "Cliente quiere comprar o cotizar.", Target = salesAgent.Id, Workflow = "wf-starter-sales", Examples = new[] { "quiero comprar", "precio", "cotizacion" } },
            new { Key = "pagar_factura", Desc = "Cliente quiere pagar o revisar factura.", Target = billingAgent.Id, Workflow = "wf-starter-billing", Examples = new[] { "pagar factura", "enviar factura", "deuda" } },
            new { Key = "consultar_inventario", Desc = "Cliente consulta disponibilidad de productos.", Target = inventoryAgent.Id, Workflow = "wf-starter-inventory", Examples = new[] { "hay stock", "disponible", "inventario" } },
            new { Key = "soporte_postventa", Desc = "Cliente necesita ayuda o reporta incidencia.", Target = supportAgent.Id, Workflow = "wf-starter-support", Examples = new[] { "tengo un problema", "soporte", "reclamo" } },
            new { Key = "fallback_general", Desc = "Caso sin intencion clara.", Target = fallbackAgentId, Workflow = string.Empty, Examples = new[] { "hola", "ayuda", "no se" } },
        };

        foreach (var rule in seedRules)
        {
            await intentStore.UpsertRuleAsync(new IntentRoutingRule
            {
                Id = $"seed-{firstChannel.Id}-{rule.Key}",
                TenantId = tenantId,
                IntentKey = rule.Key,
                IntentDescription = rule.Desc,
                ExamplePhrases = rule.Examples,
                SourceAgentId = sourceAgentId,
                TargetAgentId = rule.Target,
                WorkflowDefinitionId = string.IsNullOrWhiteSpace(rule.Workflow) ? null : rule.Workflow,
                WorkflowName = string.IsNullOrWhiteSpace(rule.Workflow) ? null : rule.Workflow,
                Priority = rule.Key == "fallback_general" ? 10 : 100,
                Enabled = true,
                Channel = channelKey,
                ConditionsJson = JsonSerializer.Serialize(new { managedBy = "seed-starter-pack", channelId = firstChannel.Id }),
                HandoffPolicyJson = JsonSerializer.Serialize(new { source = "seed" }),
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
    }
}
