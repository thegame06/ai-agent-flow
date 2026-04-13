using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;

namespace AgentFlow.Tests.Integration.Orchestration;

public class BrainContractGoldenTests
{
    [Fact]
    public void ThinkContractGolden_SK_And_MAF_AreFunctionallyEquivalent()
    {
        var fixture = LoadFixture();
        foreach (var testCase in fixture.ThinkCases)
        {
            var sk = InvokeThinkParser(typeof(SemanticKernelBrain), testCase.Json);
            var maf = InvokeThinkParser(typeof(MafBrain), testCase.Json);

            var expectedDecision = Enum.Parse<ThinkDecision>(testCase.ExpectedDecision, ignoreCase: true);
            Assert.Equal(expectedDecision, sk.Decision);
            Assert.Equal(expectedDecision, maf.Decision);
            Assert.Equal(sk.Decision, maf.Decision);

            Assert.Equal(testCase.ExpectedToolName, sk.NextToolName);
            Assert.Equal(testCase.ExpectedToolName, maf.NextToolName);
            Assert.Equal(testCase.ExpectFinalAnswer, !string.IsNullOrWhiteSpace(sk.FinalAnswer));
            Assert.Equal(testCase.ExpectFinalAnswer, !string.IsNullOrWhiteSpace(maf.FinalAnswer));
        }
    }

    [Fact]
    public void ObserveContractGolden_SK_And_MAF_AreFunctionallyEquivalent()
    {
        var fixture = LoadFixture();
        foreach (var testCase in fixture.ObserveCases)
        {
            var sk = InvokeObserveParser(typeof(SemanticKernelBrain), testCase.Json);
            var maf = InvokeObserveParser(typeof(MafBrain), testCase.Json);

            Assert.Equal(testCase.ExpectedGoalAchieved, sk.GoalAchieved);
            Assert.Equal(testCase.ExpectedGoalAchieved, maf.GoalAchieved);
            Assert.Equal(sk.GoalAchieved, maf.GoalAchieved);

            Assert.Contains(testCase.ExpectSummaryContains, sk.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(testCase.ExpectSummaryContains, maf.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ThinkResult InvokeThinkParser(Type brainType, string json)
    {
        var method = brainType.GetMethod("ParseThinkResult", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ParseThinkResult not found for {brainType.Name}");

        var instance = method.IsStatic ? null : FormatterServices.GetUninitializedObject(brainType);
        var result = method.GetParameters().Length == 2
            ? method.Invoke(instance, [json, null])
            : method.Invoke(instance, [json]);

        return Assert.IsType<ThinkResult>(result);
    }

    private static ObserveResult InvokeObserveParser(Type brainType, string json)
    {
        var method = brainType.GetMethod("ParseObserveResult", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ParseObserveResult not found for {brainType.Name}");

        var instance = method.IsStatic ? null : FormatterServices.GetUninitializedObject(brainType);
        var result = method.Invoke(instance, [json]);
        return Assert.IsType<ObserveResult>(result);
    }

    private static BrainContractFixture LoadFixture()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tests", "AgentFlow.Tests.Integration", "Orchestration", "Fixtures", "brain-contract-golden.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BrainContractFixture>(json)
            ?? throw new InvalidOperationException("Could not deserialize brain contract fixture.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentFlow.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record BrainContractFixture
    {
        public required IReadOnlyList<ThinkCase> ThinkCases { get; init; }
        public required IReadOnlyList<ObserveCase> ObserveCases { get; init; }
    }

    private sealed record ThinkCase
    {
        public required string Name { get; init; }
        public required string Json { get; init; }
        public required string ExpectedDecision { get; init; }
        public string? ExpectedToolName { get; init; }
        public required bool ExpectFinalAnswer { get; init; }
    }

    private sealed record ObserveCase
    {
        public required string Name { get; init; }
        public required string Json { get; init; }
        public required bool ExpectedGoalAchieved { get; init; }
        public required string ExpectSummaryContains { get; init; }
    }
}
