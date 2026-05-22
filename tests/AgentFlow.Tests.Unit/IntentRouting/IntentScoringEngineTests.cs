namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Classification.Models;
using AgentFlow.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public sealed class IntentScoringEngineTests
{
    private readonly Mock<ISemanticIntentMatcher> _semanticMatcher = new();
    private readonly Mock<IKeywordIntentMatcher> _keywordMatcher = new();
    private readonly Mock<ILogger<IntentScoringEngine>> _logger = new();

    [Fact]
    public async Task ClassifyAsync_WithMatches_ReturnsBestMatch()
    {
        var rule = BuildRule("loan_application", 900);

        _semanticMatcher.Setup(x => x.FindCandidatesAsync("quiero prestamo", "tenant-1", "whatsapp", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new IntentMatch { IntentKey = "loan_application", SimilarityScore = 0.95f, MatchedVia = "semantic", Rule = rule }
            });

        _keywordMatcher.Setup(x => x.FindCandidatesAsync("quiero prestamo", "tenant-1", "whatsapp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new IntentMatch { IntentKey = "loan_application", SimilarityScore = 0.80f, MatchedVia = "keyword", Rule = rule }
            });

        var engine = new IntentScoringEngine(_semanticMatcher.Object, _keywordMatcher.Object, _logger.Object);
        var result = await engine.ClassifyAsync("quiero prestamo", "tenant-1", "whatsapp", CancellationToken.None);

        Assert.NotNull(result.BestMatch);
        Assert.Equal("loan_application", result.BestMatch.IntentKey);
        Assert.True(result.BestScore > 0.8f);
        Assert.NotEqual(ConfidenceLevel.NoMatch, result.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_NoMatches_ReturnsNoMatch()
    {
        _semanticMatcher.Setup(x => x.FindCandidatesAsync("hola", "tenant-1", null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentMatch>());

        _keywordMatcher.Setup(x => x.FindCandidatesAsync("hola", "tenant-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentMatch>());

        var engine = new IntentScoringEngine(_semanticMatcher.Object, _keywordMatcher.Object, _logger.Object);
        var result = await engine.ClassifyAsync("hola", "tenant-1", null, CancellationToken.None);

        Assert.Null(result.BestMatch);
        Assert.Equal(ConfidenceLevel.NoMatch, result.Confidence);
        Assert.Equal(0f, result.BestScore);
    }

    private static IntentRoutingRule BuildRule(string key, int priority)
    {
        var now = DateTimeOffset.UtcNow;
        return new IntentRoutingRule
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = "tenant-1",
            IntentKey = key,
            SourceAgentId = "router-agent",
            TargetAgentId = "workflow-agent",
            WorkflowDefinitionId = "wf-1",
            WorkflowName = "wf-1",
            Priority = priority,
            Enabled = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
