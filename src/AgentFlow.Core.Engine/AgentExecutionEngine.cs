using AgentFlow.Abstractions;
using AgentFlow.Observability;
using AgentFlow.Application.Memory;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Common;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Repositories;
using AgentFlow.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentFlow.Security;

namespace AgentFlow.Core.Engine;

/// <summary>
/// AgentExecutionEngine — The heart of AgentFlow.
/// Implements the IAgentExecutor contract using a Think-Act-Observe loop (ReAct).
/// </summary>
public sealed class AgentExecutionEngine : IAgentExecutor
{
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentExecutionRepository _executionRepo;
    private readonly IConversationThreadRepository _threadRepo; // ✅ NEW: Thread persistence
    private readonly IAgentBrainResolver _brainResolver;
    private readonly IToolExecutor _toolExecutor;
    private readonly IAgentMemoryService _memory;
    private readonly IPolicyEngine _policyEngine;
    private readonly IAgentEventTransport _eventTransport;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly IExecutionPlanner _planner;
    private readonly TokenBudgetService _tokenBudget;
    private readonly ILogger<AgentExecutionEngine> _logger;

    public AgentExecutionEngine(
        IAgentDefinitionRepository agentRepo,
        IAgentExecutionRepository executionRepo,
        IConversationThreadRepository threadRepo, // ✅ NEW
        IAgentBrainResolver brainResolver,
        IToolExecutor toolExecutor,
        IAgentMemoryService memory,
        IPolicyEngine policyEngine,
        IAgentEventTransport eventTransport,
        ICheckpointStore checkpointStore,
        IToolRegistry toolRegistry,
        IExecutionPlanner planner,
        TokenBudgetService tokenBudget,
        ILogger<AgentExecutionEngine> logger)
    {
        _agentRepo = agentRepo;
        _executionRepo = executionRepo;
        _threadRepo = threadRepo; // ✅ NEW
        _brainResolver = brainResolver;
        _toolExecutor = toolExecutor;
        _memory = memory;
        _policyEngine = policyEngine;
        _eventTransport = eventTransport;
        _checkpointStore = checkpointStore;
        _toolRegistry = toolRegistry;
        _planner = planner;
        _tokenBudget = tokenBudget;
        _logger = logger;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default)
    {
        // --- 0. Load Agent Definition ---
        var agentDef = await _agentRepo.GetByIdAsync(
            request.AgentKey, request.TenantId, ct);

        if (agentDef is null)
        {
            AgentFlowTelemetry.ExecutionsFailed.Add(1, new TagList { { "error", "NotFound" } });
            return new AgentExecutionResult
            {
                ExecutionId = "error",
                AgentKey = request.AgentKey,
                AgentVersion = "unknown",
                Status = ExecutionStatus.Failed,
                ErrorCode = "NotFound",
                ErrorMessage = $"AgentDefinition:{request.AgentKey}"
            };
        }

        // --- 1. Create Execution ---
        var execution = AgentExecution.Create(
            tenantId: request.TenantId,
            agentDefinitionId: agentDef.Id.ToString(),
            triggeredBy: request.UserId,
            input: new ExecutionInput { UserMessage = request.UserMessage },
            maxIterations: agentDef.LoopConfig.MaxIterations,
            correlationId: request.CorrelationId ?? Guid.NewGuid().ToString(),
            parentExecutionId: request.ParentExecutionId,
            priority: (AgentFlow.Abstractions.ExecutionPriority)(int)request.Priority);

        var insertResult = await _executionRepo.InsertAsync(execution, ct);
        if (!insertResult.IsSuccess)
        {
            return new AgentExecutionResult
            {
                ExecutionId = execution.Id,
                AgentKey = agentDef.Id.ToString(),
                AgentVersion = agentDef.Version.ToString(),
                Status = ExecutionStatus.Failed,
                ErrorMessage = insertResult.Error!.Message
            };
        }

        var brainResolution = await _brainResolver.ResolveAsync(
            request.TenantId,
            agentDef.Id.ToString(),
            new AgentBrainExecutionContext
            {
                UserId = request.UserId,
                ExecutionId = execution.Id,
                Metadata = request.Metadata
            },
            ct);
        var resolvedBrain = brainResolution.Brain;

        // --- 2. Start with timeout protection ---
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(agentDef.LoopConfig.MaxExecutionTime);
        var linkedCt = timeoutCts.Token;

        execution.Start();
        await _executionRepo.UpdateAsync(execution, ct);

        AgentFlowTelemetry.ExecutionsStarted.Add(1, 
            new TagList { { "agent_id", agentDef.Id.ToString() }, { "tenant_id", request.TenantId } });

        using var activity = ExecutionTracing.StartExecution(execution.Id, agentDef.Id.ToString(), request.TenantId);

        // --- 3. Audit: execution started ---
        await _memory.Audit.RecordAsync(new AuditEntry
        {
            ExecutionId = execution.Id,
            AgentId = agentDef.Id.ToString(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            EventType = AuditEventType.ExecutionStarted,
            CorrelationId = request.CorrelationId ?? string.Empty,
            EventJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                agentName = agentDef.Name,
                userMessage = request.UserMessage,
                maxIterations = agentDef.LoopConfig.MaxIterations
            })
        }, ct);

        // --- 4. Main Loop ---
        string? executedThreadId = null;
        try
        {
            var loopResult = await RunLoopAsync(execution, agentDef, request, resolvedBrain, brainResolution.Provider, linkedCt);

            if (!loopResult.IsSuccess)
            {
                execution.Fail(loopResult.Error!.Code, loopResult.Error.Message);
                await _executionRepo.UpdateAsync(execution, ct);
            }
            else
            {
                executedThreadId = loopResult.Value;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            execution.Fail("Engine.Timeout", "Execution exceeded maximum time.");
            await _executionRepo.UpdateAsync(execution, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in execution {ExecutionId}", execution.Id);
            execution.Fail("Engine.UnhandledException", ex.Message);
            await _executionRepo.UpdateAsync(execution, ct);
        }
        finally
        {
            timeoutCts.Dispose();
            await _memory.Working.ClearAsync(execution.Id, CancellationToken.None);

            // --- 5. Publish Completion Event ---
            try
            {
                await _eventTransport.PublishAsync(new AgentEvent
                {
                    EventType = "execution.completed",
                    TenantId = request.TenantId,
                    AgentKey = agentDef.Id.ToString(),
                    Payload = JsonSerializer.Serialize(new
                    {
                        executionId = execution.Id,
                        status = execution.Status.ToString(),
                        durationMs = execution.GetDuration()?.TotalMilliseconds,
                        steps = execution.Steps.Count
                    }),
                    CorrelationId = request.CorrelationId,
                    SessionId = request.SessionId
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish execution.completed event for {ExecutionId}", execution.Id);
            }
        }

        var durationMs = execution.GetDuration()?.TotalMilliseconds ?? 0;
        var totalTokens = execution.Steps.Sum(s => s.TokensUsed ?? 0);
        var executionSegment = request.Metadata.TryGetValue("segment", out var segment) && !string.IsNullOrWhiteSpace(segment)
            ? segment
            : "default";
        var variant = request.Metadata.TryGetValue("isShadow", out var isShadow) && isShadow.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "challenger"
            : "champion";
        var brain = ResolveBrainTag(brainResolution.Provider);

        AgentFlowTelemetry.ExecutionDuration.Record(durationMs, new TagList
        {
            { "agent_id", agentDef.Id.ToString() },
            { "tenant_id", request.TenantId },
            { "status", execution.Status.ToString().ToLowerInvariant() }
        });

        AgentFlowTelemetry.ExecutionLatencyBySegment.Record(durationMs, new TagList
        {
            { "segment", executionSegment },
            { "variant", variant },
            { "brain", brain }
        });

        AgentFlowTelemetry.ExecutionOutcomes.Add(1, new TagList
        {
            { "status", execution.Status.ToString().ToLowerInvariant() },
            { "segment", executionSegment },
            { "variant", variant },
            { "brain", brain }
        });

        AgentFlowTelemetry.TokensUsed.Add(totalTokens, new TagList
        {
            { "agent_id", agentDef.Id.ToString() },
            { "tenant_id", request.TenantId },
            { "brain", brain }
        });

        var estimatedCostUsd = EstimateTokenCostUsd(totalTokens);
        AgentFlowTelemetry.TokenCostPerExecution.Record(estimatedCostUsd, new TagList
        {
            { "agent_id", agentDef.Id.ToString() },
            { "tenant_id", request.TenantId },
            { "segment", executionSegment },
            { "variant", variant },
            { "brain", brain }
        });
        AgentFlowTelemetry.TokenCostPer1K.Record((totalTokens / 1000d) > 0 ? estimatedCostUsd / (totalTokens / 1000d) : 0d, new TagList
        {
            { "agent_id", agentDef.Id.ToString() },
            { "tenant_id", request.TenantId },
            { "brain", brain }
        });

        if (execution.Status == ExecutionStatus.Completed)
        {
            AgentFlowTelemetry.ExecutionsCompleted.Add(1, new TagList
            {
                { "agent_id", agentDef.Id.ToString() },
                { "tenant_id", request.TenantId }
            });
        }
        else if (execution.Status == ExecutionStatus.Failed)
        {
            AgentFlowTelemetry.ExecutionsFailed.Add(1, new TagList
            {
                { "agent_id", agentDef.Id.ToString() },
                { "tenant_id", request.TenantId },
                { "error", execution.ErrorCode ?? "Unknown" }
            });
        }

        return MapToResult(execution, agentDef, executedThreadId);
    }

    public async Task<AgentExecutionResult> ResumeAsync(
        string executionId,
        string tenantId,
        CheckpointDecision decision,
        CancellationToken ct = default)
    {
        var execution = await _executionRepo.GetByIdAsync(executionId, tenantId, ct);
        if (execution is null) throw new SecurityException("Execution not found.");

        var agentDef = await _agentRepo.GetByIdAsync(execution.AgentDefinitionId, tenantId, ct);
        if (agentDef is null) throw new SecurityException("Agent definition not found.");

        var checkpoint = await _checkpointStore.GetAsync(executionId, tenantId, ct);
        if (checkpoint is null) throw new InvalidOperationException("No pending checkpoint found for this execution.");

        if (!decision.Approved)
        {
            execution.Fail("Human.Rejected", decision.Feedback ?? "Rejected by human.");
            await _executionRepo.UpdateAsync(execution, ct);
            await _checkpointStore.DeleteAsync(executionId, tenantId, ct);
            return MapToResult(execution, agentDef);
        }

        execution.ResumeFromReview(decision.ApprovedBy ?? "human");

        // If the human provided modified input, we use it for the next step
        // In ReAct, the next step is usually the tool call.
        // If the checkpoint happened AFTER Think but BEFORE Act, we have the tool info.
        
        await _checkpointStore.DeleteAsync(executionId, tenantId, ct);

        // Resume the loop. Note: currentMessage and goalAchieved need context.
        // Simplified: we trigger RunLoopAsync again.
        // We might need to adjust the currentMessage if Feedback was provided.
        var resumeRequest = new AgentExecutionRequest
        {
            TenantId = tenantId,
            AgentKey = agentDef.Id.ToString(),
            UserId = decision.ApprovedBy ?? "human",
            UserMessage = execution.Input.UserMessage // Continue with original goal
        };

        var brainResolution = await _brainResolver.ResolveAsync(
            tenantId,
            agentDef.Id.ToString(),
            new AgentBrainExecutionContext
            {
                UserId = decision.ApprovedBy,
                ExecutionId = execution.Id,
                Metadata = new Dictionary<string, string>()
            },
            ct);

        var resumeStatus = await RunLoopAsync(
            execution,
            agentDef,
            resumeRequest,
            brainResolution.Brain,
            brainResolution.Provider,
            ct);
        
        if (!resumeStatus.IsSuccess)
        {
            execution.Fail(resumeStatus.Error!.Code, resumeStatus.Error.Message);
            await _executionRepo.UpdateAsync(execution, ct);
        }

        return MapToResult(execution, agentDef);
    }

    private AgentExecutionResult MapToResult(AgentExecution execution, AgentDefinition agentDef, string? threadId = null)
    {
        return new AgentExecutionResult
        {
            ExecutionId = execution.Id,
            AgentKey = agentDef.Id.ToString(),
            AgentVersion = agentDef.Version.ToString(),
            Status = (ExecutionStatus)(int)execution.Status,
            FinalResponse = execution.Output?.FinalResponse,
            TotalSteps = execution.Steps.Count,
            TotalTokensUsed = execution.Steps.Sum(s => s.TokensUsed ?? 0),
            DurationMs = (long)(execution.GetDuration()?.TotalMilliseconds ?? 0),
            ErrorCode = execution.ErrorCode,
            ErrorMessage = execution.ErrorMessage,
            ThreadId = threadId // ✅ Include thread ID for multi-turn conversations
        };
    }

    private async Task<Result<string?>> RunLoopAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        IAgentBrain resolvedBrain,
        BrainProvider resolvedBrainProvider,
        CancellationToken ct)
    {
        bool goalAchieved = false;
        string currentMessage = request.UserMessage;
        string? latestPayload = request.UserMessage;
        ExecutionPlan? activePlan = null;
        var completedPlanSteps = 0;

        // ✅ NEW: Load or create conversation thread if session enabled
        ConversationThread? currentThread = null;
        Abstractions.ChatHistorySnapshot? threadSnapshot = null;

        if (agentDef.Session.EnableThreads)
        {
            currentThread = await LoadOrCreateThreadAsync(agentDef, request, execution.Id, ct);
            if (currentThread is not null)
            {
                var domainSnapshot = currentThread.GetChatHistory(agentDef.Session.ContextWindowSize);
                
                // Map Domain ChatHistorySnapshot → Abstractions ChatHistorySnapshot
                threadSnapshot = new Abstractions.ChatHistorySnapshot
                {
                    ThreadId = domainSnapshot.ThreadId,
                    RecentTurns = domainSnapshot.RecentTurns.Select(t => new Abstractions.ConversationTurn
                    {
                        UserMessage = t.UserMessage,
                        AssistantResponse = t.AssistantResponse,
                        Timestamp = t.Timestamp
                    }).ToList(),
                    TotalTurns = domainSnapshot.TotalTurns,
                    OlderContextSummary = domainSnapshot.OlderContextSummary
                };
                
                _logger.LogDebug("Loaded thread {ThreadId} with {TurnCount} turns for execution {ExecutionId}",
                    currentThread.Id, threadSnapshot.TotalTurns, execution.Id);
            }
        }

        // RESPECT RUNTIME MODE: Deterministic agents only get 1 iteration (or explicit steps)
        var maxIterations = agentDef.LoopConfig.RuntimeMode == AgentFlow.Abstractions.RuntimeMode.Deterministic 
            ? 1 
            : agentDef.LoopConfig.MaxIterations;
        var maxAllowedSteps = Math.Max(4, maxIterations * 4);

        if (RequiresPlanningPhase(agentDef))
        {
            var availableToolsForPlan = agentDef.AuthorizedTools
                .Where(t => t.IsEnabled)
                .Select(t => new AvailableToolDescriptor
                {
                    Name = t.ToolName,
                    Description = t.ToolName,
                    InputSchemaJson = "{}"
                })
                .ToList();

            activePlan = await _planner.CreatePlan(new PlannerCreateContext
            {
                TenantId = request.TenantId,
                ExecutionId = execution.Id,
                Goal = request.UserMessage,
                SystemPrompt = agentDef.Brain.SystemPromptTemplate,
                PlannerType = agentDef.LoopConfig.PlannerType,
                MaxSteps = maxIterations,
                TokenBudget = request.TokenBudget,
                AvailableTools = availableToolsForPlan
            }, ct);

            await AppendPlanStepAsync(execution, request.TenantId, 0, "initial-plan", activePlan, ct);
        }

        if (agentDef.LoopConfig.PlannerType == PlannerType.Sequential && agentDef.WorkflowSteps.Count > 0)
        {
            var sequentialResult = await RunSequentialWorkflowAsync(
                execution, agentDef, request, resolvedBrain, currentMessage, latestPayload, maxIterations, ct);

            if (!sequentialResult.IsSuccess)
                return Result<string?>.Failure(sequentialResult.Error!);

            goalAchieved = true;
            latestPayload = sequentialResult.Value;
        }

        while (!goalAchieved && execution.CurrentIteration < maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            if (execution.Steps.Count >= maxAllowedSteps)
                return Result<string?>.Failure(Error.EngineError($"Maximum step guardrail reached ({maxAllowedSteps})."));

            var budgetValidation = _tokenBudget.Validate(request.TokenBudget, execution.Steps.Sum(s => s.TokensUsed ?? 0), 500);
            if (!budgetValidation.IsValid)
                return Result<string?>.Failure(Error.EngineError(budgetValidation.ErrorMessage ?? "Token budget exhausted."));

            if (activePlan is not null)
            {
                var planDecision = _planner.NextStep(new PlannerNextStepContext
                {
                    Plan = activePlan,
                    CompletedSteps = completedPlanSteps,
                    RemainingTokenBudget = budgetValidation.RemainingTokens,
                    MaxSteps = maxIterations
                });

                if (planDecision.ShouldStop)
                {
                    return Result<string?>.Failure(Error.EngineError(planDecision.StopReason ?? "Planning stop criteria reached."));
                }

                if (planDecision.Step is not null)
                {
                    currentMessage = $"Goal: {request.UserMessage}\nCurrent plan step: {planDecision.Step.Description}";
                }
            }

            var memorySummary = await _memory.BuildContextSummaryAsync(
                agentDef.Id,
                execution.Id,
                request.TenantId,
                currentMessage,
                agentDef.Memory.VectorSearchTopK,
                agentDef.Memory.VectorMinRelevanceScore,
                ct);

            var availableTools = agentDef.AuthorizedTools
                .Where(t => t.IsEnabled)
                .Select(t => new AvailableToolDescriptor
                {
                    Name = t.ToolName,
                    Description = t.ToolName,
                    InputSchemaJson = "{}"
                })
                .ToList();

            // === THINK ===
            var thinkSw = Stopwatch.StartNew();
            using var thinkActivity = ExecutionTracing.StartThinkStep(execution.Id, execution.CurrentIteration);

            var thinkCtx = new ThinkContext
            {
                TenantId = request.TenantId,
                ExecutionId = execution.Id,
                SystemPrompt = agentDef.Brain.SystemPromptTemplate,
                UserMessage = currentMessage,
                Iteration = execution.CurrentIteration,
                History = execution.Steps.Cast<object>().ToList(),
                WorkingMemoryJson = memorySummary ?? "{}",
                AvailableTools = availableTools,
                ThreadSnapshot = threadSnapshot // ✅ NEW: Pass thread history to LLM
            };

            var thinkResult = await resolvedBrain.ThinkAsync(thinkCtx, ct);
            thinkSw.Stop();
            var rationale = thinkResult.Rationale ?? string.Empty;
            ExecutionTracing.RecordThinkDecision(thinkActivity, thinkResult.Decision.ToString(), rationale);
            AgentFlowTelemetry.LlmLatency.Record(thinkSw.ElapsedMilliseconds, 
                new TagList { { "agent_id", agentDef.Id.ToString() }, { "step", "think" }, { "brain", ResolveBrainTag(resolvedBrainProvider) } });

            var thinkStep = new AgentStep
            {
                StepType = StepType.Think,
                Iteration = execution.CurrentIteration,
                StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-thinkSw.ElapsedMilliseconds),
                CompletedAt = DateTimeOffset.UtcNow,
                DurationMs = thinkSw.ElapsedMilliseconds,
                LlmResponse = rationale,
                TokensUsed = thinkResult.TokensUsed,
                ThinkingRationale = rationale,
                IsSuccess = true
            };

            // === POST-LLM POLICY CHECK ===
            var postLlmPolicy = await EvaluatePoliciesAsync(PolicyCheckpoint.PostLLM, execution, agentDef, request, 
                llmResponse: thinkResult.Rationale, ct: ct);
            if (!postLlmPolicy.IsSuccess) return Result<string?>.Failure(postLlmPolicy.Error!);

            execution.AppendStep(thinkStep);
            await _executionRepo.AppendStepAsync(execution.Id, request.TenantId, thinkStep, ct);

            // === DECISION ROUTING ===
            switch (thinkResult.Decision)
            {
                case ThinkDecision.ProvideFinalAnswer:
                    var output = new ExecutionOutput
                    {
                        FinalResponse = thinkResult.FinalAnswer ?? string.Empty,
                        TotalTokensUsed = execution.Steps.Sum(s => s.TokensUsed ?? 0),
                        TotalToolCalls = execution.Steps.Count(s => s.StepType == StepType.Act),
                        TotalIterations = execution.CurrentIteration
                    };

                    execution.Complete(output);

                    // === PRE-RESPONSE POLICY CHECK ===
                    var preResponsePolicy = await EvaluatePoliciesAsync(PolicyCheckpoint.PreResponse, execution, agentDef, request, 
                        finalResponse: output.FinalResponse, ct: ct);
if (!preResponsePolicy.IsSuccess) return Result<string?>.Failure(preResponsePolicy.Error!);

                    await _executionRepo.UpdateAsync(execution, CancellationToken.None);
                    goalAchieved = true;
                    break;

                case ThinkDecision.UseTool:
                    var actResult = await ActAsync(
                        execution, agentDef, request,
                        thinkResult.NextToolName!,
                        thinkResult.NextToolInputJson!,
                        ct);

                    if (!actResult.IsSuccess)
                    {
                        if (activePlan is not null)
                        {
                            activePlan = await _planner.RevisePlan(new PlannerReviseContext
                            {
                                BaseContext = new PlannerCreateContext
                                {
                                    TenantId = request.TenantId,
                                    ExecutionId = execution.Id,
                                    Goal = request.UserMessage,
                                    SystemPrompt = agentDef.Brain.SystemPromptTemplate,
                                    PlannerType = agentDef.LoopConfig.PlannerType,
                                    MaxSteps = maxIterations,
                                    TokenBudget = request.TokenBudget,
                                    AvailableTools = availableTools
                                },
                                CurrentPlan = activePlan,
                                FailureReason = actResult.Error!.Message,
                                CompletedSteps = completedPlanSteps
                            }, ct);

                            await AppendPlanStepAsync(execution, request.TenantId, execution.CurrentIteration, "replan-tool-failure", activePlan, ct);
                            completedPlanSteps = 0;
                            continue;
                        }

                        return Result<string?>.Failure(actResult.Error!);
                    }

                    // === POST-TOOL POLICY CHECK ===
                    var postToolPolicy = await EvaluatePoliciesAsync(PolicyCheckpoint.PostTool, execution, agentDef, request, 
                        toolName: thinkResult.NextToolName, toolOutput: actResult.Value?.OutputJson, ct: ct);
                    if (!postToolPolicy.IsSuccess) return Result<string?>.Failure(postToolPolicy.Error!);

                    var observeResult = await ObserveAsync(
                        execution, agentDef, request, resolvedBrain,
                        thinkResult.NextToolName!,
                        actResult.Value!,
                        ct);

                    if (!observeResult.IsSuccess) return Result<string?>.Failure(observeResult.Error!);

                    if (observeResult.Value!.GoalAchieved)
                    {
                        var finalOutput = new ExecutionOutput
                        {
                            FinalResponse = observeResult.Value.Summary,
                            TotalTokensUsed = execution.Steps.Sum(s => s.TokensUsed ?? 0),
                            TotalToolCalls = execution.Steps.Count(s => s.StepType == StepType.Act),
                            TotalIterations = execution.CurrentIteration
                        };
                        execution.Complete(finalOutput);
                        await _executionRepo.UpdateAsync(execution, CancellationToken.None);
                        goalAchieved = true;
                    }
                    else
                    {
                        completedPlanSteps++;
                        currentMessage = $"Previous observation: {observeResult.Value.Summary}. Continue with: {request.UserMessage}";
                    }
                    break;

                case ThinkDecision.Checkpoint:
                    execution.PauseForReview(thinkResult.Rationale ?? "LLM requested checkpoint.");
                    await _executionRepo.UpdateAsync(execution, ct);
                    
                    await _checkpointStore.SaveAsync(new AgentCheckpoint
                    {
                        ExecutionId = execution.Id,
                        TenantId = request.TenantId,
                        AgentKey = agentDef.Id.ToString(),
                        CheckpointId = Guid.NewGuid().ToString(),
                        Reason = thinkResult.Rationale ?? "LLM requested manual verification.",
                        ToolName = thinkResult.NextToolName,
                        ToolInputJson = thinkResult.NextToolInputJson,
                        LlmRationale = thinkResult.Rationale
                    }, ct);

                    goalAchieved = true; // Break loop, but status is HumanReviewPending
                    break;

                default:
                    return Result<string?>.Failure(Error.EngineError($"Unknown think decision: {thinkResult.Decision}"));
            }
        }

        if (!goalAchieved)
        {
            return Result<string?>.Failure(Error.EngineError(
                $"Agent did not achieve goal within {agentDef.LoopConfig.MaxIterations} iterations."));
        }

        // ✅ NEW: Save conversation turn to thread if enabled
        if (currentThread is not null && execution.Status == Abstractions.ExecutionStatus.Completed)
        {
            var totalTokens = execution.Steps.Sum(s => s.TokensUsed ?? 0);
            var response = execution.Output?.FinalResponse;
            
            await SaveThreadTurnAsync(
                currentThread, 
                execution.Id, 
                totalTokens, 
                request.UserMessage, 
                response, 
                ct);
        }

        return Result<string?>.Success(currentThread?.Id);
    }

    private static bool RequiresPlanningPhase(AgentDefinition agentDef)
        => agentDef.LoopConfig.PlannerType is PlannerType.TreeOfThought
            || (agentDef.LoopConfig.PlannerType == PlannerType.Sequential && agentDef.WorkflowSteps.Count == 0);

    private async Task AppendPlanStepAsync(
        AgentExecution execution,
        string tenantId,
        int iteration,
        string reason,
        ExecutionPlan plan,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var step = new AgentStep
        {
            StepType = StepType.Plan,
            Iteration = iteration,
            StartedAt = now,
            CompletedAt = now,
            DurationMs = 0,
            InputJson = JsonSerializer.Serialize(new { reason, planRevision = plan.Revision }),
            OutputJson = JsonSerializer.Serialize(plan),
            LlmResponse = JsonSerializer.Serialize(plan),
            ThinkingRationale = reason,
            IsSuccess = true
        };

        execution.AppendStep(step);
        await _executionRepo.AppendStepAsync(execution.Id, tenantId, step, ct);
    }

    private async Task<Result<string?>> RunSequentialWorkflowAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        IAgentBrain brain,
        string currentMessage,
        string? latestPayload,
        int maxIterations,
        CancellationToken ct)
    {
        var stepsById = agentDef.WorkflowSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .ToDictionary(s => s.Id, s => s);

        if (stepsById.Count == 0)
            return Result<string?>.Failure(Error.Validation("WorkflowSteps", "Sequential planner requires at least one workflow step."));

        var currentStep = agentDef.WorkflowSteps[0];
        var visited = 0;

        while (visited < maxIterations && currentStep is not null)
        {
            ct.ThrowIfCancellationRequested();
            visited++;

            var stepType = NormalizeStepType(currentStep.Type);
            switch (stepType)
            {
                case "think":
                case "plan":
                    {
                        var prompt = GetConfigString(currentStep.Config, "prompt")
                            ?? GetConfigString(currentStep.Config, "instruction")
                            ?? currentMessage;

                        var thinkResult = await brain.ThinkAsync(new ThinkContext
                        {
                            TenantId = request.TenantId,
                            ExecutionId = execution.Id,
                            SystemPrompt = agentDef.Brain.SystemPromptTemplate,
                            UserMessage = prompt,
                            Iteration = execution.CurrentIteration,
                            History = execution.Steps.Cast<object>().ToList(),
                            WorkingMemoryJson = latestPayload ?? "{}",
                            AvailableTools = agentDef.AuthorizedTools.Where(t => t.IsEnabled)
                                .Select(t => new AvailableToolDescriptor { Name = t.ToolName, Description = t.ToolName })
                                .ToList()
                        }, ct);

                        latestPayload = thinkResult.FinalAnswer ?? thinkResult.Rationale ?? prompt;
                        currentMessage = latestPayload;

                        await AppendWorkflowAuditStepAsync(
                            execution, request.TenantId, stepType == "plan" ? StepType.Plan : StepType.Think,
                            visited - 1, currentStep, prompt, latestPayload, thinkResult.TokensUsed, null, ct);
                        break;
                    }

                case "act":
                case "tool_call":
                    {
                        var toolNames = GetConfigStringList(currentStep.Config, "toolNames");
                        var singleTool = GetConfigString(currentStep.Config, "toolName");
                        if (toolNames.Count == 0 && !string.IsNullOrWhiteSpace(singleTool))
                            toolNames.Add(singleTool!);

                        if (toolNames.Count == 0)
                            return Result<string?>.Failure(Error.Validation("WorkflowStep", $"Step '{currentStep.Label}' requires toolName or toolNames."));

                        var toolInput = BuildToolInputJson(currentStep.Config, latestPayload ?? currentMessage);

                        if (toolNames.Count > 1 && agentDef.LoopConfig.AllowParallelToolCalls)
                        {
                            var tasks = toolNames.Select(name => ActAsync(execution, agentDef, request, name, toolInput, ct)).ToList();
                            var results = await Task.WhenAll(tasks);
                            var failed = results.FirstOrDefault(r => !r.IsSuccess);
                            if (failed is not null && !failed.IsSuccess)
                                return Result<string?>.Failure(failed.Error!);

                            var aggregatePayload = JsonSerializer.Serialize(results.Select((r, index) => new
                            {
                                toolName = toolNames[index],
                                output = r.Value!.OutputJson,
                                success = r.Value.IsSuccess
                            }).ToList());

                            latestPayload = aggregatePayload;
                            await AppendWorkflowAuditStepAsync(
                                execution, request.TenantId, StepType.Aggregate, visited - 1, currentStep,
                                toolInput, aggregatePayload, null,
                                $"Parallel aggregation of {toolNames.Count} tool calls", ct);

                            foreach (var postTool in results.Select((result, index) => new { result, toolName = toolNames[index] }))
                            {
                                var postToolPolicy = await EvaluatePoliciesAsync(
                                    PolicyCheckpoint.PostTool,
                                    execution,
                                    agentDef,
                                    request,
                                    toolName: postTool.toolName,
                                    toolOutput: postTool.result.Value?.OutputJson,
                                    ct: ct);

                                if (!postToolPolicy.IsSuccess)
                                    return Result<string?>.Failure(postToolPolicy.Error!);
                            }
                        }
                        else
                        {
                            var act = await ActAsync(execution, agentDef, request, toolNames[0], toolInput, ct);
                            if (!act.IsSuccess)
                                return Result<string?>.Failure(act.Error!);

                            latestPayload = act.Value!.OutputJson;

                            var postToolPolicy = await EvaluatePoliciesAsync(
                                PolicyCheckpoint.PostTool,
                                execution,
                                agentDef,
                                request,
                                toolName: toolNames[0],
                                toolOutput: act.Value.OutputJson,
                                ct: ct);

                            if (!postToolPolicy.IsSuccess)
                                return Result<string?>.Failure(postToolPolicy.Error!);
                        }
                        break;
                    }

                case "observe":
                case "aggregate":
                    {
                        var observe = await brain.ObserveAsync(new ObserveContext
                        {
                            TenantId = request.TenantId,
                            ToolName = currentStep.Label,
                            ToolOutputJson = latestPayload ?? "{}",
                            ToolSucceeded = true,
                            UserGoal = request.UserMessage,
                            History = execution.Steps.Cast<object>().ToList()
                        }, ct);

                        latestPayload = observe.Summary;
                        currentMessage = observe.Summary;

                        await AppendWorkflowAuditStepAsync(
                            execution, request.TenantId,
                            stepType == "aggregate" ? StepType.Aggregate : StepType.Observe,
                            visited - 1, currentStep, latestPayload, observe.Summary, observe.TokensUsed, null, ct);
                        break;
                    }

                case "decide":
                    {
                        var decision = EvaluateDecision(currentStep, latestPayload);
                        var decisionJson = JsonSerializer.Serialize(decision);
                        await AppendWorkflowAuditStepAsync(
                            execution, request.TenantId, StepType.Decision, visited - 1, currentStep,
                            latestPayload, decisionJson, null, decision.reason, ct);

                        var nextId = decision.passed
                            ? currentStep.Connections.FirstOrDefault()
                            : currentStep.Connections.Skip(1).FirstOrDefault();

                        if (string.IsNullOrWhiteSpace(nextId))
                        {
                            return await CompleteSequentialExecutionAsync(execution, agentDef, request, latestPayload, visited, ct);
                        }

                        currentStep = stepsById.GetValueOrDefault(nextId);
                        continue;
                    }

                case "human_review":
                    execution.PauseForReview(currentStep.Description);
                    await _executionRepo.UpdateAsync(execution, ct);
                    return Result<string?>.Failure(Error.Unauthorized("Sequential workflow paused for human review."));

                default:
                    return Result<string?>.Failure(Error.Validation("WorkflowStep", $"Unsupported workflow step type '{currentStep.Type}'."));
            }

            var defaultNext = currentStep.Connections.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(defaultNext))
            {
                return await CompleteSequentialExecutionAsync(execution, agentDef, request, latestPayload, visited, ct);
            }

            currentStep = stepsById.GetValueOrDefault(defaultNext);
        }

        return Result<string?>.Failure(Error.EngineError($"Sequential workflow did not complete within {maxIterations} steps."));
    }

    private async Task AppendWorkflowAuditStepAsync(
        AgentExecution execution,
        string tenantId,
        StepType type,
        int iteration,
        WorkflowStep workflowStep,
        string? input,
        string? output,
        int? tokensUsed,
        string? rationale,
        CancellationToken ct)
    {
        var step = new AgentStep
        {
            StepType = type,
            Iteration = iteration,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = 0,
            InputJson = input,
            OutputJson = output,
            TokensUsed = tokensUsed,
            ThinkingRationale = rationale,
            LlmResponse = output,
            IsSuccess = true,
            ToolName = workflowStep.Label
        };

        execution.AppendStep(step);
        await _executionRepo.AppendStepAsync(execution.Id, tenantId, step, ct);
    }

    private async Task<Result<string?>> CompleteSequentialExecutionAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        string? latestPayload,
        int visited,
        CancellationToken ct)
    {
        var finalResponse = latestPayload ?? string.Empty;
        var preResponsePolicy = await EvaluatePoliciesAsync(
            PolicyCheckpoint.PreResponse,
            execution,
            agentDef,
            request,
            finalResponse: finalResponse,
            ct: ct);

        if (!preResponsePolicy.IsSuccess)
            return Result<string?>.Failure(preResponsePolicy.Error!);

        execution.Complete(new ExecutionOutput
        {
            FinalResponse = finalResponse,
            TotalTokensUsed = execution.Steps.Sum(s => s.TokensUsed ?? 0),
            TotalToolCalls = execution.Steps.Count(s => s.StepType == StepType.Act),
            TotalIterations = visited
        });
        await _executionRepo.UpdateAsync(execution, CancellationToken.None);
        return Result<string?>.Success(finalResponse);
    }

    private static string NormalizeStepType(string? type) => (type ?? "think").Trim().ToLowerInvariant();

    private static string? GetConfigString(IReadOnlyDictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return null;
        return value switch
        {
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            JsonNode node => node.ToJsonString(),
            _ => value.ToString()
        };
    }

    private static List<string> GetConfigStringList(IReadOnlyDictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return [];
        return value switch
        {
            JsonElement el when el.ValueKind == JsonValueKind.Array => el.EnumerateArray().Select(x => x.ToString()).ToList(),
            IEnumerable<object> values => values.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToList(),
            string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            _ => []
        };
    }

    private static string BuildToolInputJson(IReadOnlyDictionary<string, object> config, string payload)
    {
        var inputTemplate = GetConfigString(config, "inputTemplate");
        if (string.IsNullOrWhiteSpace(inputTemplate))
            return payload;

        return inputTemplate.Replace("{{input}}", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static (bool passed, string reason) EvaluateDecision(WorkflowStep step, string? payload)
    {
        payload ??= string.Empty;
        var mode = GetConfigString(step.Config, "mode") ?? "contains";
        var expected = GetConfigString(step.Config, "matchValue") ?? "true";

        return mode.ToLowerInvariant() switch
        {
            "non_empty" => (!string.IsNullOrWhiteSpace(payload), "Decision gate evaluated non_empty"),
            "equals" => (string.Equals(payload.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase), $"Decision gate compared equals '{expected}'"),
            _ => (payload.Contains(expected, StringComparison.OrdinalIgnoreCase), $"Decision gate checked contains '{expected}'")
        };
    }

    private async Task<Result<ToolExecutionResult>> ActAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        string toolName,
        string toolInputJson,
        CancellationToken ct)
    {
        // === GURU TIP: HUMAN-IN-THE-LOOP FOR HIGH RISK MCP TOOLS ===
        // If the tool is HIGH risk, we must pause for human verification before Activating it.
        var tool = _toolRegistry.Resolve(toolName);
        if (tool != null && tool.RiskLevel >= ToolRiskLevel.High)
        {
            _logger.LogInformation("HITL: Tool {ToolName} has risk {RiskLevel}. Pausing execution {ExecutionId}.", 
                toolName, tool.RiskLevel, execution.Id);

            execution.PauseForReview($"Human verification required for security-sensitive tool: {toolName}");
            await _executionRepo.UpdateAsync(execution, ct);
            
            await _checkpointStore.SaveAsync(new AgentCheckpoint
            {
                ExecutionId = execution.Id,
                TenantId = request.TenantId,
                AgentKey = agentDef.Id.ToString(),
                CheckpointId = Guid.NewGuid().ToString(),
                Reason = $"Security Review: {toolName} requires authorization (Risk: {tool.RiskLevel})",
                ToolName = toolName,
                ToolInputJson = toolInputJson,
                LlmRationale = "Guru Enforcement: High risk tools require manual sign-off."
            }, ct);

            return Result<ToolExecutionResult>.Failure(Error.Unauthorized("Execution paused for security verification."));
        }

        // === PRE-TOOL POLICY CHECK ===
        var preToolPolicy = await EvaluatePoliciesAsync(PolicyCheckpoint.PreTool, execution, agentDef, request, 
            toolName: toolName, toolInput: toolInputJson, ct: ct);
        if (!preToolPolicy.IsSuccess) return Result<ToolExecutionResult>.Failure(preToolPolicy.Error!);

        using var toolActivity = ExecutionTracing.StartToolExecution(toolName, execution.Id);
        var actSw = Stopwatch.StartNew();
        AgentFlowTelemetry.ToolInvocations.Add(1, new TagList { { "tool_name", toolName } });

        var stepId = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        var binding = agentDef.AuthorizedTools.FirstOrDefault(t => t.ToolName == toolName);

        if (binding is null)
            return Result<ToolExecutionResult>.Failure(
                Error.Forbidden($"Tool '{toolName}' is not authorized for this agent."));

        var toolCallsForTool = execution.Steps
            .Count(s => s.StepType == StepType.Act && s.ToolName == toolName);

        if (toolCallsForTool >= binding.MaxCallsPerExecution)
            return Result<ToolExecutionResult>.Failure(
                Error.EngineError($"Tool '{toolName}' has reached its per-execution call limit."));

        using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        toolCts.CancelAfter(agentDef.LoopConfig.ToolCallTimeout);

        ToolExecutionResult toolResult;
        try
        {
            toolResult = await _toolExecutor.ExecuteToolAsync(new ToolInvocationRequest
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ExecutionId = execution.Id,
                StepId = stepId,
                ToolId = binding.ToolId,
                ToolName = toolName,
                InputJson = toolInputJson,
                CorrelationId = request.CorrelationId ?? execution.Id
            }, toolCts.Token);
        }
        catch (OperationCanceledException)
        {
            toolResult = new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = "Tool timeout",
                DurationMs = actSw.ElapsedMilliseconds
            };
        }

        actSw.Stop();
        AgentFlowTelemetry.ToolDuration.Record(actSw.ElapsedMilliseconds, new TagList { { "tool_name", toolName } });

        if (!toolResult.IsSuccess)
        {
            AgentFlowTelemetry.ToolFailures.Add(1, new TagList { { "tool_name", toolName } });
            if (binding.ToolId.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase) ||
                toolName.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase))
            {
                AgentFlowTelemetry.McpToolFailures.Add(1, new TagList
                {
                    { "tool_name", toolName },
                    { "tool_id", binding.ToolId }
                });
            }
            toolActivity?.SetStatus(ActivityStatusCode.Error, toolResult.ErrorMessage);
        }

        var actStep = new AgentStep
        {
            StepType = StepType.Act,
            Iteration = execution.CurrentIteration,
            StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-actSw.ElapsedMilliseconds),
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = actSw.ElapsedMilliseconds,
            ToolId = binding.ToolId,
            ToolName = toolName,
            InputJson = toolInputJson,
            OutputJson = toolResult.OutputJson,
            IsSuccess = toolResult.IsSuccess,
            ErrorMessage = toolResult.ErrorMessage
        };

        execution.AppendStep(actStep);
        await _executionRepo.AppendStepAsync(execution.Id, request.TenantId, actStep, ct);

        return Result<ToolExecutionResult>.Success(toolResult);
    }

    private static string ResolveBrainTag(BrainProvider provider) => provider switch
    {
        BrainProvider.MicrosoftAgentFramework => "maf",
        BrainProvider.SemanticKernel => "sk",
        _ => provider.ToString().ToLowerInvariant()
    };

    private static double EstimateTokenCostUsd(int totalTokens)
    {
        const double usdPer1kTokens = 0.003;
        return (totalTokens / 1000d) * usdPer1kTokens;
    }

    private async Task<Result<ObserveResult>> ObserveAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        IAgentBrain brain,
        string toolName,
        ToolExecutionResult toolResult,
        CancellationToken ct)
    {
        var observeSw = Stopwatch.StartNew();
        using var observeActivity = AgentFlowTelemetry.BrainSource.StartActivity("Observe")
            ?.SetTag("agentflow.execution_id", execution.Id)
            ?.SetTag("agentflow.tool_name", toolName);

        var observeResult = await brain.ObserveAsync(new ObserveContext
        {
            TenantId = request.TenantId,
            ToolName = toolName,
            ToolOutputJson = toolResult.OutputJson ?? "{}",
            ToolSucceeded = toolResult.IsSuccess,
            UserGoal = request.UserMessage,
            History = execution.Steps.Cast<object>().ToList()
        }, ct);

        observeSw.Stop();

        var observeStep = new AgentStep
        {
            StepType = StepType.Observe,
            Iteration = execution.CurrentIteration,
            StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-observeSw.ElapsedMilliseconds),
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = observeSw.ElapsedMilliseconds,
            LlmResponse = observeResult.Summary,
            IsSuccess = true
        };

        execution.AppendStep(observeStep);
        await _executionRepo.AppendStepAsync(execution.Id, request.TenantId, observeStep, ct);

        return Result<ObserveResult>.Success(observeResult);
    }

    public async Task<Result> CancelAsync(
        string executionId,
        string tenantId,
        string cancelledBy,
        CancellationToken ct = default)
    {
        var execution = await _executionRepo.GetByIdAsync(executionId, tenantId, ct);
        if (execution is null)
            return Result.Failure(Error.NotFound($"Execution:{executionId}"));

        var cancelResult = execution.Cancel(cancelledBy);
        if (!cancelResult.IsSuccess) return cancelResult;

        await _executionRepo.UpdateAsync(execution, ct);
        return Result.Success();
    }

    private async Task<Result<PolicyResult>> EvaluatePoliciesAsync(
        PolicyCheckpoint checkpoint,
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        string? toolName = null,
        string? toolInput = null,
        string? toolOutput = null,
        string? llmResponse = null,
        string? finalResponse = null,
        CancellationToken ct = default)
    {
        var context = new PolicyEvaluationContext
        {
            TenantId = request.TenantId,
            AgentKey = agentDef.Id.ToString(),
            AgentVersion = agentDef.Version.ToString(),
            PolicySetId = agentDef.LoopConfig.PlannerType.ToString(), // Temporary mapping
            ExecutionId = execution.Id,
            UserId = request.UserId,
            Checkpoint = checkpoint,
            UserMessage = request.UserMessage,
            ToolName = toolName,
            ToolInputJson = toolInput,
            ToolOutputJson = toolOutput,
            LlmResponse = llmResponse,
            FinalResponse = finalResponse
        };

        var result = await _policyEngine.EvaluateAsync(checkpoint, context, ct);

        if (result.Decision == PolicyDecision.Escalate)
        {
            var violation = result.Violations.FirstOrDefault();
            execution.PauseForReview(violation?.Description ?? "Human review requested by policy.");
            await _executionRepo.UpdateAsync(execution, ct);

            await _checkpointStore.SaveAsync(new AgentCheckpoint
            {
                ExecutionId = execution.Id,
                TenantId = request.TenantId,
                AgentKey = agentDef.Id.ToString(),
                CheckpointId = Guid.NewGuid().ToString(),
                Reason = violation?.Description ?? "Policy Escalation",
                ToolName = toolName,
                ToolInputJson = toolInput,
                LlmRationale = llmResponse ?? result.Decision.ToString()
            }, ct);

            return Result<PolicyResult>.Failure(Error.Unauthorized("Execution paused for human review."));
        }

        if (result.Decision == PolicyDecision.Block)
        {
            var violation = result.Violations.FirstOrDefault();
            return Result<PolicyResult>.Failure(Error.Unauthorized(
                $"Policy Violation: {violation?.Description ?? "Unknown policy breach"}. Code: {violation?.Code}"));
        }

        return Result<PolicyResult>.Success(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // THREAD MANAGEMENT HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load existing thread or create new one if AutoCreateThread is enabled.
    /// </summary>
    private async Task<ConversationThread?> LoadOrCreateThreadAsync(
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        string executionId,
        CancellationToken ct)
    {
        ConversationThread? thread = null;

        // Try to load existing thread if ThreadId provided
        if (!string.IsNullOrEmpty(request.ThreadId))
        {
            thread = await _threadRepo.GetByIdAsync(request.ThreadId, request.TenantId, ct);
            if (thread is not null)
            {
                _logger.LogInformation("Loaded existing thread {ThreadId} for execution {ExecutionId}",
                    request.ThreadId, executionId);
                return thread;
            }

            _logger.LogWarning("ThreadId {ThreadId} not found, will create new thread if AutoCreateThread=true",
                request.ThreadId);
        }

        // Auto-create/reuse thread if enabled
        if (agentDef.Session.AutoCreateThread)
        {
            var threadKey = GenerateThreadKey(agentDef, request);

            // Reuse deterministic session thread when available
            var existingByKey = await _threadRepo.GetByKeyAsync(threadKey, request.TenantId, ct);
            if (existingByKey is not null)
            {
                _logger.LogInformation("Reusing existing thread {ThreadId} (key={ThreadKey}) for execution {ExecutionId}",
                    existingByKey.Id, threadKey, executionId);
                return existingByKey;
            }

            thread = ConversationThread.Create(
                tenantId: request.TenantId,
                threadKey: threadKey,
                agentDefinitionId: agentDef.Id,
                userId: request.UserId,
                expiresIn: agentDef.Session.DefaultThreadTtl,
                maxTurns: agentDef.Session.MaxTurnsPerThread,
                metadata: new Dictionary<string, string>
                {
                    ["agentName"] = agentDef.Name,
                    ["agentVersion"] = agentDef.Version.ToString(),
                    ["createdByExecution"] = executionId,
                    ["sessionId"] = request.SessionId ?? string.Empty
                });

            var insertResult = await _threadRepo.InsertAsync(thread, ct);
            if (insertResult.IsSuccess)
            {
                _logger.LogInformation("Auto-created new thread {ThreadId} for execution {ExecutionId}",
                    thread.Id, executionId);
                return thread;
            }

            // Race-safe fallback: if key already exists, load and continue
            var recovered = await _threadRepo.GetByKeyAsync(threadKey, request.TenantId, ct);
            if (recovered is not null)
            {
                _logger.LogInformation("Recovered concurrent thread {ThreadId} (key={ThreadKey}) after insert conflict",
                    recovered.Id, threadKey);
                return recovered;
            }

            _logger.LogError("Failed to create thread: {Error}", insertResult.Error?.Message);
            return null;
        }

        return null;
    }

    /// <summary>
    /// Save conversation turn to thread after successful execution.
    /// </summary>
    private async Task SaveThreadTurnAsync(
        ConversationThread thread,
        string executionId,
        int tokensUsed,
        string userMessage,
        string? assistantResponse,
        CancellationToken ct)
    {
        var appendResult = thread.AppendExecution(executionId, tokensUsed, userMessage, assistantResponse);

        if (!appendResult.IsSuccess)
        {
            _logger.LogWarning("Failed to append execution to thread {ThreadId}: {Error}",
                thread.Id, appendResult.Error?.Message);
            return;
        }

        var updateResult = await _threadRepo.UpdateAsync(thread, ct);
        if (!updateResult.IsSuccess)
        {
            _logger.LogError("Failed to persist thread {ThreadId} update: {Error}",
                thread.Id, updateResult.Error?.Message);
        }
        else
        {
            _logger.LogDebug("Saved turn to thread {ThreadId}, total turns: {TurnCount}",
                thread.Id, thread.TurnCount);
        }
    }

    /// <summary>
    /// Generate thread key based on agent's ThreadKeyPattern.
    /// Supports variables: {agentName}, {userId}, {date}, {guid}, {sessionId}
    /// NOTE: if SessionId is provided, key becomes deterministic for session continuity.
    /// </summary>
    private string GenerateThreadKey(AgentDefinition agentDef, AgentExecutionRequest request)
    {
        var pattern = agentDef.Session.ThreadKeyPattern;
        var now = DateTimeOffset.UtcNow;
        var sessionId = request.SessionId ?? string.Empty;
        var guidPart = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")[..8]
            : sessionId;

        var threadKey = pattern
            .Replace("{agentName}", agentDef.Name.Replace(" ", "-").ToLowerInvariant())
            .Replace("{userId}", request.UserId)
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{sessionId}", sessionId)
            .Replace("{guid}", guidPart);

        return threadKey;
    }
}
