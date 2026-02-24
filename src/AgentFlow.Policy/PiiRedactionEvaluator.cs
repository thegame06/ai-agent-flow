using AgentFlow.Abstractions;
using System.Text.RegularExpressions;

namespace AgentFlow.Policy;

/// <summary>
/// Enterprise-grade PII (Personally Identifiable Information) Redaction.
/// Detects credit cards, emails, and sensitive patterns before they leave the boundary.
/// Supports: Blocking, Escalating (HITL), or Shadow (Logging).
/// </summary>
public sealed class PiiRedactionEvaluator : IPolicyEvaluator
{
    public string ExtensionId => "core.policy.pii_redaction";
    public string Version => "1.0.0";
    public string PolicyType => "pii-redaction";

    private static readonly Dictionary<string, (string Pattern, string Label)> PiiPatterns = new()
    {
        ["credit_card"] = (@"\b(?:\d[ -]*?){13,16}\b", "Credit Card"),
        ["email"] = (@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", "Email Address"),
        ["phone"] = (@"\b(?:\+?(\d{1,3}))?[-. (]*(\d{3})[-. )]*(\d{3})[-. ]*(\d{4})\b", "Phone Number"),
        ["ssn"] = (@"\b\d{3}-\d{2}-\d{4}\b", "Social Security Number")
    };

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ExtensionHealthStatus> CheckHealthAsync(CancellationToken ct = default) => 
        Task.FromResult(ExtensionHealthStatus.Healthy());

    public Task<(bool Violated, string? Evidence)> EvaluateAsync(
        PolicyDefinition policy,
        PolicyEvaluationContext context,
        CancellationToken ct = default)
    {
        // 1. Identify what to check
        var textToCheck = policy.AppliesAt switch
        {
            PolicyCheckpoint.PostLLM => context.LlmResponse,
            PolicyCheckpoint.PreResponse => context.FinalResponse,
            PolicyCheckpoint.PreTool => context.ToolInputJson,
            PolicyCheckpoint.PostTool => context.ToolOutputJson,
            _ => context.UserMessage
        };

        if (string.IsNullOrEmpty(textToCheck))
            return Task.FromResult((false, (string?)null));

        // 2. Load enabled detectors from config (or check all by default)
        var detectors = policy.Config.TryGetValue("detectors", out var d) 
            ? d.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList()
            : PiiPatterns.Keys.ToList();

        foreach (var detectorId in detectors)
        {
            if (PiiPatterns.TryGetValue(detectorId, out var pii))
            {
                var match = Regex.Match(textToCheck, pii.Pattern);
                if (match.Success)
                {
                    // Guru Tip: In a real "Redaction" policy, we might actually MASK the data 
                    // in the context for downstream steps. But as an Evaluator, we just report violation.
                    var evidence = $"Sensitive data detected ({pii.Label}): {Mask(match.Value)}";
                    return Task.FromResult((true, (string?)evidence));
                }
            }
        }

        return Task.FromResult((false, (string?)null));
    }

    private static string Mask(string value) 
    {
        if (value.Length <= 4) return "****";
        return $"{value[..2]}...{value[^2..]}";
    }
}
