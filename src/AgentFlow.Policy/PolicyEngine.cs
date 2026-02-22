using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AgentFlow.Policy;

// =========================================================================
// POLICY DEFINITIONS (stored as documents, loaded at runtime)
// =========================================================================

// =========================================================================
// POLICY STORE
// =========================================================================

public interface IPolicyStore
{
    Task<PolicySetDefinition?> GetPolicySetAsync(string policySetId, string tenantId, CancellationToken ct = default);
}

// =========================================================================
// POLICY EVALUATORS — one per type
// =========================================================================

/// <summary>Checks regex pattern against LLM response, user input, or final response.</summary>
public sealed class RegexPolicyEvaluator : IPolicyEvaluator
{
    public string ExtensionId => "core.policy.regex";
    public string Version => "1.0.0";
    public string PolicyType => "regex-check";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    public Task<(bool Violated, string? Evidence)> EvaluateAsync(
        PolicyDefinition policy,
        PolicyEvaluationContext context,
        CancellationToken ct = default)
    {
        if (!policy.Config.TryGetValue("pattern", out var pattern))
            return Task.FromResult((false, (string?)null));

        var targetField = policy.Config.GetValueOrDefault("applyTo", "auto");

        var textToCheck = targetField switch
        {
            "userMessage"   => context.UserMessage,
            "llmResponse"   => context.LlmResponse,
            "finalResponse" => context.FinalResponse,
            "toolInput"     => context.ToolInputJson,
            "toolOutput"    => context.ToolOutputJson,
            _               => context.Checkpoint switch
            {
                PolicyCheckpoint.PreAgent     => context.UserMessage,
                PolicyCheckpoint.PostLLM      => context.LlmResponse,
                PolicyCheckpoint.PreResponse  => context.FinalResponse,
                PolicyCheckpoint.PreTool      => context.ToolInputJson,
                PolicyCheckpoint.PostTool     => context.ToolOutputJson,
                _                             => null
            }
        };

        if (string.IsNullOrEmpty(textToCheck))
            return Task.FromResult((false, (string?)null));

        var match = Regex.Match(textToCheck, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var evidence = $"Pattern '{pattern}' matched at position {match.Index}: '{match.Value}'";
            return Task.FromResult((true, (string?)evidence));
        }

        return Task.FromResult((false, (string?)null));
    }
}

/// <summary>
/// Detects prompt injection attempts in user input or LLM rationale.
/// </summary>
public sealed class PromptInjectionEvaluator : IPolicyEvaluator
{
    public string ExtensionId => "core.policy.injection";
    public string Version => "1.0.0";
    public string PolicyType => "prompt-injection";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    private static readonly (string Pattern, string Description)[] InjectionPatterns =
    [
        (@"ignore.{0,20}(previous|above|prior|all).{0,20}(instruction|prompt|rule)", "Instruction override"),
        (@"you are now\s+\w", "Identity override"),
        (@"your new (system|role|instruction)", "System prompt replacement"),
        (@"forget.{0,20}(instruction|rule|context)", "Context erasure"),
        (@"disregard.{0,20}(instruction|rule)", "Rule bypass"),
        (@"<\|.*?\|>", "Special token injection"),
        (@"(?:^|\s)system:\s", "System role injection"),
        (@"jailbreak|DAN mode|developer mode", "Jailbreak attempt")
    ];

    public Task<(bool Violated, string? Evidence)> EvaluateAsync(
        PolicyDefinition policy,
        PolicyEvaluationContext context,
        CancellationToken ct = default)
    {
        var textToCheck = context.UserMessage ?? context.LlmResponse ?? string.Empty;

        foreach (var (pattern, description) in InjectionPatterns)
        {
            var match = Regex.Match(textToCheck, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
                return Task.FromResult((true, (string?)$"[{description}] Pattern matched: '{match.Value}'"));
        }

        return Task.FromResult((false, (string?)null));
    }
}

public sealed class RateLimitPolicyEvaluator : IPolicyEvaluator
{
    public string ExtensionId => "core.policy.ratelimit";
    public string Version => "1.0.0";
    public string PolicyType => "rate-limit";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    public Task<(bool Violated, string? Evidence)> EvaluateAsync(
        PolicyDefinition policy,
        PolicyEvaluationContext context,
        CancellationToken ct = default)
    {
        return Task.FromResult((false, (string?)null));
    }
}

// =========================================================================
// COMPOSITE POLICY ENGINE
// =========================================================================

/// <summary>
/// Transversal policy engine. Evaluates ALL policies in a PolicySet at the given checkpoint.
/// Principles:
/// - Shadow policies NEVER block (only record)
/// - First Blocking violation stops further evaluation
/// - PolicyStoreCache should be scoped per-request (loaded once, evaluated N times)
/// </summary>
public sealed class CompositePolicyEngine : IPolicyEngine
{
    private readonly IPolicyStore _store;
    private readonly IReadOnlyDictionary<string, IPolicyEvaluator> _evaluators;
    private readonly ILogger<CompositePolicyEngine> _logger;

    public CompositePolicyEngine(
        IPolicyStore store,
        IEnumerable<IPolicyEvaluator> evaluators,
        ILogger<CompositePolicyEngine> logger)
    {
        _store = store;
        _evaluators = evaluators.ToDictionary(e => e.PolicyType, e => e);
        _logger = logger;
    }

    public async Task<PolicyResult> EvaluateAsync(
        PolicyCheckpoint checkpoint,
        PolicyEvaluationContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.PolicySetId))
            return PolicyResult.Allow();  // No PolicySet configured → passthrough

        var policySet = await _store.GetPolicySetAsync(context.PolicySetId, context.TenantId, ct);
        if (policySet is null)
        {
            _logger.LogWarning(
                "PolicySet '{PolicySetId}' not found for tenant '{TenantId}'. Allowing by default.",
                context.PolicySetId, context.TenantId);
            return PolicyResult.Allow();
        }

        var applicablePolicies = policySet.Policies
            .Where(p => p.IsEnabled && p.AppliesAt == checkpoint)
            .Where(p => !p.TargetSegments.Any() || p.TargetSegments.Intersect(context.UserSegments).Any())
            .ToList();

        if (applicablePolicies.Count == 0)
            return PolicyResult.Allow();

        var violations = new List<PolicyViolation>();

        foreach (var policy in applicablePolicies)
        {
            if (!_evaluators.TryGetValue(policy.PolicyType, out var evaluator))
            {
                _logger.LogWarning(
                    "No evaluator registered for policy type '{PolicyType}'. Skipping policy '{PolicyId}'.",
                    policy.PolicyType, policy.PolicyId);
                continue;
            }

            try
            {
                var (violated, evidence) = await evaluator.EvaluateAsync(policy, context, ct);

                if (!violated) continue;

                _logger.LogWarning(
                    "Policy '{PolicyId}' [{Action}] violated at {Checkpoint} for execution '{ExecutionId}'. Evidence: {Evidence}",
                    policy.PolicyId, policy.Action, checkpoint, context.ExecutionId, evidence ?? "none");

                if (policy.Action == PolicyAction.Shadow)
                {
                    // Shadow: record but don't block
                    violations.Add(new PolicyViolation
                    {
                        Code = policy.PolicyId,
                        Description = policy.Description,
                        Severity = policy.Severity,
                        PolicyId = policy.PolicyId
                    });
                    continue;
                }

                var decision = policy.Action switch
                {
                    PolicyAction.Block    => PolicyDecision.Block,
                    PolicyAction.Escalate => PolicyDecision.Escalate,
                    PolicyAction.Warn     => PolicyDecision.Warn,
                    _                     => PolicyDecision.Allow
                };

                var violation = new PolicyViolation
                {
                    Code    = policy.PolicyId,
                    Description = $"{policy.Description}{(evidence is not null ? $" Evidence: {evidence}" : "")}",
                    Severity    = policy.Severity,
                    PolicyId    = policy.PolicyId
                };

                if (decision == PolicyDecision.Block)
                {
                    // Block immediately — don't evaluate remaining policies
                    return new PolicyResult
                    {
                        Decision   = PolicyDecision.Block,
                        Violations = [violation]
                    };
                }

                violations.Add(violation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating policy '{PolicyId}'", policy.PolicyId);
                // Policy evaluation failure → don't block (fail open), but log
            }
        }

        var overallDecision = violations.Any(v => v.Severity >= PolicySeverity.High)
            ? PolicyDecision.Warn
            : PolicyDecision.Allow;

        return new PolicyResult { Decision = overallDecision, Violations = violations };
    }
}

// =========================================================================
// IN-MEMORY POLICY STORE (for tests and bootstrap)
// =========================================================================

public sealed class InMemoryPolicyStore : IPolicyStore
{
    private readonly Dictionary<string, PolicySetDefinition> _store = new();

    public void Register(PolicySetDefinition policySet) =>
        _store[$"{policySet.TenantId}:{policySet.PolicySetId}"] = policySet;

    public Task<PolicySetDefinition?> GetPolicySetAsync(
        string policySetId, string tenantId, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault($"{tenantId}:{policySetId}"));
}


