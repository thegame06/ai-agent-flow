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
    private readonly IAgentBrain _brain;
    private readonly IToolExecutor _toolExecutor;
    private readonly IAgentMemoryService _memory;
    private readonly IPolicyEngine _policyEngine;
    private readonly IAgentEventTransport _eventTransport;
    private readonly ICheckpointStore _checkpointStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<AgentExecutionEngine> _logger;

    public AgentExecutionEngine(
        IAgentDefinitionRepository agentRepo,
        IAgentExecutionRepository executionRepo,
        IConversationThreadRepository threadRepo, // ✅ NEW
        IAgentBrain brain,
        IToolExecutor toolExecutor,
        IAgentMemoryService memory,
        IPolicyEngine policyEngine,
        IAgentEventTransport eventTransport,
        ICheckpointStore checkpointStore,
        IToolRegistry toolRegistry,
        ILogger<AgentExecutionEngine> logger)
    {
        _agentRepo = agentRepo;
        _executionRepo = executionRepo;
        _threadRepo = threadRepo; // ✅ NEW
        _brain = brain;
        _toolExecutor = toolExecutor;
        _memory = memory;
        _policyEngine = policyEngine;
        _eventTransport = eventTransport;
        _checkpointStore = checkpointStore;
        _toolRegistry = toolRegistry;
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
            CorrelationId = request.CorrelationId,
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
            var loopResult = await RunLoopAsync(execution, agentDef, request, linkedCt);

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

        var resumeStatus = await RunLoopAsync(execution, agentDef, resumeRequest, ct);
        
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
        CancellationToken ct)
    {
        bool goalAchieved = false;
        string currentMessage = request.UserMessage;

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

        while (!goalAchieved && execution.CurrentIteration < maxIterations)
        {
            ct.ThrowIfCancellationRequested();

            var memorySummary = await _memory.BuildContextSummaryAsync(
                agentDef.Id, execution.Id, request.TenantId, currentMessage, ct);

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

            var thinkResult = await _brain.ThinkAsync(thinkCtx, ct);
            thinkSw.Stop();
            var rationale = thinkResult.Rationale ?? string.Empty;
            ExecutionTracing.RecordThinkDecision(thinkActivity, thinkResult.Decision.ToString(), rationale);
            AgentFlowTelemetry.LlmLatency.Record(thinkSw.ElapsedMilliseconds, 
                new TagList { { "agent_id", agentDef.Id.ToString() }, { "step", "think" } });

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

                    if (!actResult.IsSuccess) return Result<string?>.Failure(actResult.Error!);

                    // === POST-TOOL POLICY CHECK ===
                    var postToolPolicy = await EvaluatePoliciesAsync(PolicyCheckpoint.PostTool, execution, agentDef, request, 
                        toolName: thinkResult.NextToolName, toolOutput: actResult.Value?.OutputJson, ct: ct);
                    if (!postToolPolicy.IsSuccess) return Result<string?>.Failure(postToolPolicy.Error!);

                    var observeResult = await ObserveAsync(
                        execution, agentDef, request,
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

    private async Task<Result<ObserveResult>> ObserveAsync(
        AgentExecution execution,
        AgentDefinition agentDef,
        AgentExecutionRequest request,
        string toolName,
        ToolExecutionResult toolResult,
        CancellationToken ct)
    {
        var observeSw = Stopwatch.StartNew();
        using var observeActivity = AgentFlowTelemetry.BrainSource.StartActivity("Observe")
            ?.SetTag("agentflow.execution_id", execution.Id)
            ?.SetTag("agentflow.tool_name", toolName);

        var observeResult = await _brain.ObserveAsync(new ObserveContext
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

        // Auto-create new thread if enabled
        if (agentDef.Session.AutoCreateThread)
        {
            var threadKey = GenerateThreadKey(agentDef, request);
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
                    ["createdByExecution"] = executionId
                });

            var insertResult = await _threadRepo.InsertAsync(thread, ct);
            if (insertResult.IsSuccess)
            {
                _logger.LogInformation("Auto-created new thread {ThreadId} for execution {ExecutionId}",
                    thread.Id, executionId);
                return thread;
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
    /// Supports variables: {agentName}, {userId}, {date}, {guid}
    /// </summary>
    private string GenerateThreadKey(AgentDefinition agentDef, AgentExecutionRequest request)
    {
        var pattern = agentDef.Session.ThreadKeyPattern;
        var threadKey = pattern
            .Replace("{agentName}", agentDef.Name.Replace(" ", "-").ToLowerInvariant())
            .Replace("{userId}", request.UserId)
            .Replace("{date}", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"))
            .Replace("{guid}", Guid.NewGuid().ToString("N")[..8]);

        return threadKey;
    }
}
