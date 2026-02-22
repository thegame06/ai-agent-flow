using AgentFlow.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AgentFlow.TestRunner;

// =========================================================================
// AGENT TEST RUNNER
// =========================================================================

/// <summary>
/// Runs agent test suites as part of the DSL lifecycle (Draft → TestPassed).
/// Used in:
/// - Pre-publish validation (blocking)
/// - CI/CD pipeline (via CLI or API)
/// - Regression testing post-calibration
///
/// Principle: A Published agent must pass its full TestSuite.
/// </summary>
public sealed class AgentTestRunner : IAgentTestRunner
{
    private readonly IAgentExecutor _executor;
    private readonly IAgentEvaluator _evaluator;
    private readonly ILogger<AgentTestRunner> _logger;

    public AgentTestRunner(
        IAgentExecutor executor,
        IAgentEvaluator evaluator,
        ILogger<AgentTestRunner> logger)
    {
        _executor = executor;
        _evaluator = evaluator;
        _logger = logger;
    }

    public async Task<TestSuiteResult> RunSuiteAsync(
        TestSuiteRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running test suite for agent '{AgentKey}@{Version}' (tenant={TenantId})",
            request.AgentKey, request.AgentVersion, request.TenantId);

        var suiteSw = Stopwatch.StartNew();

        // TODO Phase 3: load TestSuiteDsl from AgentDefinition via repository
        // For now: inject via the request
        var testCases = new List<TestCase>(); // Will be loaded from agent DSL
        var results = new List<TestCaseResult>();

        int passed = 0, failed = 0, warned = 0;

        foreach (var testCase in testCases)
        {
            if (ct.IsCancellationRequested) break;

            // Filter by tags if requested
            if (request.Tags is { Length: > 0 } &&
                !request.Tags.Any(t => testCase.Tags.Contains(t)))
            {
                results.Add(new TestCaseResult
                {
                    CaseName = testCase.Name,
                    Verdict = TestCaseVerdict.Skipped,
                    DurationMs = 0
                });
                continue;
            }

            var caseResult = await RunCaseAsync(new TestCaseRequest
            {
                TenantId = request.TenantId,
                AgentKey = request.AgentKey,
                AgentVersion = request.AgentVersion,
                TestCase = testCase
            }, ct);

            results.Add(caseResult);

            switch (caseResult.Verdict)
            {
                case TestCaseVerdict.Pass: passed++; break;
                case TestCaseVerdict.Fail: failed++; break;
                case TestCaseVerdict.Warning: warned++; break;
            }
        }

        suiteSw.Stop();

        var verdict = failed > 0 ? TestSuiteVerdict.Fail
            : warned > 0 ? TestSuiteVerdict.Warning
            : TestSuiteVerdict.Pass;

        _logger.LogInformation(
            "Test suite complete: {Passed} passed, {Failed} failed, {Warned} warned in {Duration}ms. Verdict: {Verdict}",
            passed, failed, warned, suiteSw.ElapsedMilliseconds, verdict);

        return new TestSuiteResult
        {
            AgentKey = request.AgentKey,
            AgentVersion = request.AgentVersion,
            TotalCases = results.Count(r => r.Verdict != TestCaseVerdict.Skipped),
            Passed = passed,
            Failed = failed,
            Warned = warned,
            TotalDurationMs = suiteSw.ElapsedMilliseconds,
            Verdict = verdict,
            Results = results
        };
    }

    public async Task<TestCaseResult> RunCaseAsync(
        TestCaseRequest request,
        CancellationToken ct = default)
    {
        var tc = request.TestCase;
        _logger.LogDebug("Running test case '{CaseName}'", tc.Name);

        var caseSw = Stopwatch.StartNew();
        var failures = new List<string>();
        double? qualityScore = null;
        string? executionId = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(tc.MaxDurationMs);

            var execResult = await _executor.ExecuteAsync(new AgentExecutionRequest
            {
                TenantId = request.TenantId,
                AgentKey = request.AgentKey,
                AgentVersion = request.AgentVersion,
                UserId = "test-runner",
                UserMessage = tc.Input,
                CorrelationId = $"test:{tc.Name}"
            }, cts.Token);

            caseSw.Stop();
            executionId = execResult.ExecutionId;

            // ── Validate: execution status
            if (execResult.Status != ExecutionStatus.Completed)
                failures.Add($"Execution did not complete. Status: {execResult.Status}. Error: {execResult.ErrorMessage}");

            var response = execResult.FinalResponse ?? string.Empty;

            // ── Validate: expected output contains
            foreach (var expected in tc.ExpectedOutputContains)
                if (!response.Contains(expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"Response missing expected content: '{expected}'");

            // ── Validate: output must NOT contain
            foreach (var notExpected in tc.ExpectedOutputNotContains)
                if (response.Contains(notExpected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"Response contains forbidden content: '{notExpected}'");

            // ── Validate: duration
            if (caseSw.ElapsedMilliseconds > tc.MaxDurationMs)
                failures.Add($"Execution took {caseSw.ElapsedMilliseconds}ms, max allowed: {tc.MaxDurationMs}ms");

            var verdict = failures.Count == 0 ? TestCaseVerdict.Pass : TestCaseVerdict.Fail;

            return new TestCaseResult
            {
                CaseName = tc.Name,
                Verdict = verdict,
                DurationMs = caseSw.ElapsedMilliseconds,
                QualityScore = qualityScore,
                ExecutionId = executionId,
                FailureReasons = failures
            };
        }
        catch (OperationCanceledException)
        {
            caseSw.Stop();
            return new TestCaseResult
            {
                CaseName = tc.Name,
                Verdict = TestCaseVerdict.Fail,
                DurationMs = caseSw.ElapsedMilliseconds,
                ExecutionId = executionId,
                FailureReasons = [$"Test case timed out after {tc.MaxDurationMs}ms"]
            };
        }
        catch (Exception ex)
        {
            caseSw.Stop();
            _logger.LogError(ex, "Test case '{CaseName}' threw unexpected exception", tc.Name);
            return new TestCaseResult
            {
                CaseName = tc.Name,
                Verdict = TestCaseVerdict.Fail,
                DurationMs = caseSw.ElapsedMilliseconds,
                ExecutionId = executionId,
                FailureReasons = [$"Unexpected exception: {ex.GetType().Name}: {ex.Message}"]
            };
        }
    }
}
