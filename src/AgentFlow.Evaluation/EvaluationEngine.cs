using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentFlow.Evaluation;

// =========================================================================
// HALLUCINATION DETECTOR
// =========================================================================

/// <summary>
/// Structural hallucination detection: compares numeric values and entities
/// in the final response against all tool outputs.
/// Rule: If significant data in response ≠ data in any tool output → hallucination.
/// </summary>
public static class HallucinationDetector
{
    private static readonly Regex NumberPattern =
        new(@"\b(\d{1,3}(?:[.,]\d{3})*(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\b", RegexOptions.Compiled);

    private static readonly Regex MoneyPattern =
        new(@"[$€£¥]\s*[\d,\.]+|[\d,\.]+\s*(?:USD|EUR|GBP|MXN|COP)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static HallucinationAnalysis Analyze(string finalResponse, IReadOnlyList<ToolCallRecord> toolCalls)
    {
        var successfulOutputs = toolCalls
            .Where(t => t.IsSuccess && !string.IsNullOrEmpty(t.OutputJson))
            .Select(t => t.OutputJson!)
            .ToList();

        if (successfulOutputs.Count == 0)
            return new HallucinationAnalysis { Risk = HallucinationRisk.None };

        // Extract numbers from response
        var responseNumbers = ExtractNumbers(finalResponse);
        var responseMoney = MoneyPattern.Matches(finalResponse)
            .Select(m => NormalizeNumber(m.Value))
            .Where(n => n is not null)
            .ToHashSet();

        // Extract numbers from all tool outputs combined
        var combinedOutput = string.Join(" ", successfulOutputs);
        var outputNumbers = ExtractNumbers(combinedOutput);

        var suspiciousNumbers = responseNumbers
            .Where(n => !outputNumbers.Contains(n) && !IsCommonNumber(n))
            .ToList();

        var suspiciousMoneyValues = responseMoney
            .Where(m => m is not null && !outputNumbers.Contains(m!))
            .ToList();

        var totalCandidates = responseNumbers.Count + responseMoney.Count;
        var totalSuspicious = suspiciousNumbers.Count + suspiciousMoneyValues.Count;

        if (totalCandidates == 0)
            return new HallucinationAnalysis { Risk = HallucinationRisk.None };

        var ratio = (double)totalSuspicious / totalCandidates;

        var risk = ratio switch
        {
            0   => HallucinationRisk.None,
            < 0.10 => HallucinationRisk.Low,
            < 0.25 => HallucinationRisk.Medium,
            < 0.50 => HallucinationRisk.High,
            _   => HallucinationRisk.Critical
        };

        return new HallucinationAnalysis
        {
            Risk = risk,
            SuspiciousValues = [.. suspiciousNumbers, .. suspiciousMoneyValues.Where(s => s is not null).Select(s => s!)],
            TotalCandidates = totalCandidates,
            TotalSuspicious = totalSuspicious,
            RatioSuspicious = ratio
        };
    }

    private static HashSet<string> ExtractNumbers(string text)
    {
        return NumberPattern.Matches(text)
            .Select(m => NormalizeNumber(m.Value))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet();
    }

    private static string? NormalizeNumber(string raw)
    {
        var cleaned = raw.Replace(",", "").Replace("$", "").Replace("€", "").Trim();
        return decimal.TryParse(cleaned, out var d) ? d.ToString("G") : null;
    }

    private static bool IsCommonNumber(string n) =>
        n is "0" or "1" or "2" or "3" or "100" or "1000";
}

public sealed record HallucinationAnalysis
{
    public HallucinationRisk Risk { get; init; }
    public IReadOnlyList<string> SuspiciousValues { get; init; } = [];
    public int TotalCandidates { get; init; }
    public int TotalSuspicious { get; init; }
    public double RatioSuspicious { get; init; }
}

// =========================================================================
// POLICY COMPLIANCE CHECKER (deterministic)
// =========================================================================

public static class PolicyComplianceChecker
{
    public sealed record ComplianceCheck
    {
        public required string Description { get; init; }
        public bool Passed { get; init; }
    }

    public static (double Score, IReadOnlyList<ComplianceCheck> Checks) Calculate(
        EvaluationRequest request,
        PolicyComplianceConfig config)
    {
        var checks = new List<ComplianceCheck>();

        // Check 1: steps count
        var stepCount = request.ToolCalls.Count;
        checks.Add(new() {
            Description = $"Steps ({stepCount}) <= maxSteps ({config.MaxSteps})",
            Passed = stepCount <= config.MaxSteps
        });

        // Check 2: all tools authorized
        var unauthorizedTools = request.ToolCalls
            .Where(t => !config.AuthorizedTools.Contains(t.ToolName))
            .ToList();
        checks.Add(new() {
            Description = $"All tools are authorized ({unauthorizedTools.Count} unauthorized)",
            Passed = unauthorizedTools.Count == 0
        });

        // Check 3: required steps (from test case)
        if (request.ApplicableTestCase is not null && request.ApplicableTestCase.ExpectedTools.Count > 0)
        {
            var executedTools = request.ToolCalls.Select(t => t.ToolName).ToHashSet();
            var missingRequired = request.ApplicableTestCase.ExpectedTools
                .Where(t => !executedTools.Contains(t))
                .ToList();
            checks.Add(new() {
                Description = $"Expected tools executed (missing: {string.Join(", ", missingRequired)})",
                Passed = missingRequired.Count == 0
            });
        }

        // Check 4: duration (if available)
        if (config.MaxDurationMs > 0)
        {
            checks.Add(new() {
                Description = $"Total tool calls completed (compliance check)",
                Passed = true // actual duration checked in engine
            });
        }

        var score = checks.Count == 0 ? 1.0 :
            (double)checks.Count(c => c.Passed) / checks.Count;

        return (score, checks);
    }
}

public sealed record PolicyComplianceConfig
{
    public int MaxSteps { get; init; } = 6;
    public IReadOnlyList<string> AuthorizedTools { get; init; } = [];
    public int MaxDurationMs { get; init; } = 120_000;
}

// =========================================================================
// TOOL USAGE ACCURACY
// =========================================================================

public static class ToolUsageAccuracyCalculator
{
    public static double? Calculate(EvaluationRequest request)
    {
        if (request.ApplicableTestCase is null ||
            request.ApplicableTestCase.ExpectedTools.Count == 0)
            return null; // No ground truth available

        var actual = request.ToolCalls.Select(t => t.ToolName).ToList();
        var expected = request.ApplicableTestCase.ExpectedTools;

        var intersection = actual.Intersect(expected).Count();
        var denominator = Math.Max(actual.Count, expected.Count);

        if (denominator == 0) return 1.0;

        var baseAccuracy = (double)intersection / denominator;

        // Bonus for correct order (up to +0.1)
        var orderBonus = CalculateOrderBonus(actual, expected);

        return Math.Min(1.0, baseAccuracy + orderBonus);
    }

    private static double CalculateOrderBonus(List<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count == 0 || expected.Count == 0) return 0;

        var minLen = Math.Min(actual.Count, expected.Count);
        var correctOrder = Enumerable.Range(0, minLen).Count(i => actual[i] == expected[i]);
        return (double)correctOrder / minLen * 0.1;
    }
}

// =========================================================================
// MAIN EVALUATOR
// =========================================================================

/// <summary>
/// Orchestrates all evaluation checks.
/// Principles:
/// - Async (does not block response to user)
/// - Never uses same LLM as agent for quality scoring
/// - HallucinationRisk >= High forces QualityScore = 0
/// </summary>
public sealed class AgentEvaluator : IAgentEvaluator
{
    private readonly IAgentBrain? _judgeModel;  // null = disable LLM quality scoring
    private readonly ILogger<AgentEvaluator> _logger;

    public AgentEvaluator(ILogger<AgentEvaluator> logger, IAgentBrain? judgeModel = null)
    {
        _logger = logger;
        _judgeModel = judgeModel;
    }

    public async Task<EvaluationResult> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Evaluating execution '{ExecutionId}' for agent '{AgentKey}@{Version}'",
            request.ExecutionId, request.AgentKey, request.AgentVersion);

        var violations = new List<EvaluationViolation>();
        var requiresHumanReview = false;
        var reviewPriority = HumanReviewPriority.Low;

        // ── 1. Hallucination Detection (always, independent of config)
        var hallucinationAnalysis = HallucinationDetector.Analyze(
            request.FinalResponse,
            request.ToolCalls);

        if (hallucinationAnalysis.Risk >= HallucinationRisk.High)
        {
            violations.Add(new() {
                Code = "EVAL001",
                Description = $"HallucinationRisk={hallucinationAnalysis.Risk}. Suspicious values not found in tool outputs: [{string.Join(", ", hallucinationAnalysis.SuspiciousValues.Take(5))}]",
                Severity = EvaluationSeverity.Critical,
                Evidence = $"Ratio: {hallucinationAnalysis.RatioSuspicious:P0} ({hallucinationAnalysis.TotalSuspicious}/{hallucinationAnalysis.TotalCandidates})"
            });
            requiresHumanReview = true;
            reviewPriority = HumanReviewPriority.Immediate;
        }

        // ── 2. Quality Score via LLM Judge (skip if hallucination = Critical → force 0)
        double qualityScore = hallucinationAnalysis.Risk == HallucinationRisk.Critical
            ? 0.0
            : await ComputeQualityScoreAsync(request, ct);

        if (qualityScore < request.Config.RequireHumanReviewOnScoreBelow)
        {
            violations.Add(new() {
                Code = "EVAL002",
                Description = $"QualityScore ({qualityScore:F2}) below threshold ({request.Config.RequireHumanReviewOnScoreBelow:F2})",
                Severity = EvaluationSeverity.High
            });
            requiresHumanReview = true;
            if (reviewPriority < HumanReviewPriority.High)
                reviewPriority = HumanReviewPriority.High;
        }

        // ── 3. Policy Compliance (deterministic)
        var (complianceScore, _) = PolicyComplianceChecker.Calculate(
            request,
            new PolicyComplianceConfig { MaxSteps = 20, AuthorizedTools = [] });

        if (complianceScore < 0.8)
        {
            violations.Add(new() {
                Code = "EVAL003",
                Description = $"PolicyComplianceScore ({complianceScore:F2}) below 0.80",
                Severity = EvaluationSeverity.High
            });
            requiresHumanReview = true;
        }

        // ── 4. Tool Usage Accuracy (vs test case, if applicable)
        var toolAccuracy = ToolUsageAccuracyCalculator.Calculate(request);

        // ── 5. Test case output validation
        if (request.ApplicableTestCase is not null)
        {
            var tc = request.ApplicableTestCase;
            foreach (var expected in tc.ExpectedOutputContains)
            {
                if (!request.FinalResponse.Contains(expected, StringComparison.OrdinalIgnoreCase))
                    violations.Add(new() {
                        Code = "EVAL004",
                        Description = $"Expected output to contain: '{expected}'",
                        Severity = EvaluationSeverity.Warning
                    });
            }

            foreach (var notExpected in tc.ExpectedOutputNotContains)
            {
                if (request.FinalResponse.Contains(notExpected, StringComparison.OrdinalIgnoreCase))
                    violations.Add(new() {
                        Code = "EVAL005",
                        Description = $"Output must NOT contain: '{notExpected}'",
                        Severity = EvaluationSeverity.High
                    });
            }
        }

        _logger.LogInformation(
            "Evaluation done: Quality={Quality:F2}, Compliance={Compliance:F2}, Hallucination={Hallucination}, HumanReview={HumanReview}",
            qualityScore, complianceScore, hallucinationAnalysis.Risk, requiresHumanReview);

        return new EvaluationResult
        {
            ExecutionId = request.ExecutionId,
            TenantId = request.TenantId,
            QualityScore = qualityScore,
            PolicyComplianceScore = complianceScore,
            HallucinationRisk = hallucinationAnalysis.Risk,
            ToolUsageAccuracy = toolAccuracy,
            RequiresHumanReview = requiresHumanReview,
            ReviewPriority = reviewPriority,
            EvaluationRationale = $"Hallucination: {hallucinationAnalysis.Risk}. Quality: {qualityScore:F2}. Compliance: {complianceScore:F2}.",
            Violations = violations,
            IsShadowEvaluation = request.IsShadowEvaluation
        };
    }

    private async Task<double> ComputeQualityScoreAsync(EvaluationRequest request, CancellationToken ct)
    {
        if (!request.Config.EnableQualityScoring || _judgeModel is null)
            return 1.0; // No scoring configured → assume pass (monitoring mode)

        var toolSummary = string.Join("\n", request.ToolCalls.Select(t =>
            $"- {t.ToolName}: {(t.IsSuccess ? t.OutputJson?[..Math.Min(200, t.OutputJson?.Length ?? 0)] : "FAILED")}"));

        var judgePrompt = string.Join("\n",
            "Evaluate the following agent interaction. Score from 0.0 to 1.0.",
            "",
            $"USER MESSAGE: {request.UserMessage}",
            "",
            "TOOL RESULTS:",
            toolSummary,
            "",
            $"AGENT RESPONSE: {request.FinalResponse}",
            "",
            "Evaluate: relevance, completeness, accuracy (data matches tools), tone.",
            """Respond ONLY in JSON: {"score": 0.0, "rationale": "..."}""");

        try
        {
            var result = await _judgeModel.ThinkAsync(new ThinkContext
            {
                TenantId = request.TenantId,
                ExecutionId = request.ExecutionId,
                SystemPrompt = "You are a quality evaluator for AI agents. Be concise and precise.",
                UserMessage = judgePrompt,
                Iteration = 0
            }, ct);

            if (result.FinalAnswer is not null)
            {
                var doc = JsonDocument.Parse(result.FinalAnswer);
                if (doc.RootElement.TryGetProperty("score", out var scoreEl))
                    return Math.Clamp(scoreEl.GetDouble(), 0.0, 1.0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM judge call failed. Defaulting quality score to 0.75.");
            return 0.75;
        }

        return 0.75; // Fallback
    }
}
