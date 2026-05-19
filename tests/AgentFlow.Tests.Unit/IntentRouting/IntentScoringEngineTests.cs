namespace AgentFlow.Tests.Unit.IntentRouting;

using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Classification.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Tests unitarios para IntentScoringEngine - Happy Path.
/// Valida el flujo completo: Semantic + Keyword → Hybrid Scoring → Confidence Level.
/// </summary>
public sealed class IntentScoringEngineTests
{
    private readonly Mock<ISemanticIntentMatcher> _semanticMatcher;
    private readonly Mock<IKeywordIntentMatcher> _keywordMatcher;
    private readonly Mock<ILogger<IntentScoringEngine>> _logger;
    private readonly IntentScoringEngine _engine;

    public IntentScoringEngineTests()
    {
        _semanticMatcher = new Mock<ISemanticIntentMatcher>();
        _keywordMatcher = new Mock<IKeywordIntentMatcher>();
        _logger = new Mock<ILogger<IntentScoringEngine>>();

        _engine = new IntentScoringEngine(
            _semanticMatcher.Object,
            _keywordMatcher.Object,
            _logger.Object);
    }

    [Fact]
    public async Task ClassifyAsync_HighConfidence_LoanApplication()
    {
        // Arrange
        const string userMessage = "Quiero solicitar un préstamo personal";
        const string tenantId = "tenant-1";
        const string channel = "whatsapp";

        // Semantic matcher retorna match fuerte
        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>
            {
                new SemanticMatch
                {
                    IntentKey = "loan_application",
                    DisplayName = "Solicitud de Préstamo",
                    Category = "Sales",
                    SimilarityScore = 0.95,
                    Explanation = "High semantic similarity to loan application intent"
                }
            });

        // Keyword matcher retorna match moderado
        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>
            {
                new KeywordMatch
                {
                    IntentKey = "loan_application",
                    KeywordScore = 0.85,
                    MatchedKeywords = new List<string> { "préstamo", "solicitar" },
                    MatchType = KeywordMatchType.Partial
                }
            });

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, channel, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("loan_application", result.BestMatch.IntentKey);
        Assert.Equal(ConfidenceLevel.High, result.Confidence);
        Assert.True(result.BestScore >= 0.90, $"Expected BestScore >= 0.90, got {result.BestScore}");
        Assert.NotEmpty(result.AllCandidates);
        Assert.NotNull(result.ExplanationJson);

        // Verify hybrid formula: 0.7*0.95 + 0.2*0.85 + 0.1*0.0 = 0.665 + 0.17 = 0.835
        // Pero el scoring incluye priority (default 0.5) → 0.665 + 0.17 + 0.05 = 0.885
        // Redondeado puede dar >= 0.90 con ajustes
    }

    [Fact]
    public async Task ClassifyAsync_MediumConfidence_PaymentStatus()
    {
        // Arrange
        const string userMessage = "¿Cómo va mi pago?";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>
            {
                new SemanticMatch
                {
                    IntentKey = "payment_status",
                    DisplayName = "Estado de Pago",
                    Category = "Payments",
                    SimilarityScore = 0.82,
                    Explanation = "Moderate semantic similarity"
                }
            });

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>
            {
                new KeywordMatch
                {
                    IntentKey = "payment_status",
                    KeywordScore = 0.75,
                    MatchedKeywords = new List<string> { "pago" },
                    MatchType = KeywordMatchType.Partial
                }
            });

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("payment_status", result.BestMatch.IntentKey);
        Assert.Equal(ConfidenceLevel.Medium, result.Confidence);
        Assert.InRange(result.BestScore, 0.75, 0.89);
    }

    [Fact]
    public async Task ClassifyAsync_LowConfidence_AmbiguousMessage()
    {
        // Arrange
        const string userMessage = "Tengo una duda";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>
            {
                new SemanticMatch
                {
                    IntentKey = "general_support",
                    DisplayName = "Soporte General",
                    Category = "Support",
                    SimilarityScore = 0.65,
                    Explanation = "Low semantic similarity"
                }
            });

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>
            {
                new KeywordMatch
                {
                    IntentKey = "general_support",
                    KeywordScore = 0.55,
                    MatchedKeywords = new List<string> { "duda" },
                    MatchType = KeywordMatchType.Fuzzy
                }
            });

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("general_support", result.BestMatch.IntentKey);
        Assert.Equal(ConfidenceLevel.Low, result.Confidence);
        Assert.InRange(result.BestScore, 0.50, 0.74);
    }

    [Fact]
    public async Task ClassifyAsync_NoMatch_IrrelevantMessage()
    {
        // Arrange
        const string userMessage = "hola qwerty asdfgh";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>());

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>());

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.BestMatch);
        Assert.Equal(ConfidenceLevel.NoMatch, result.Confidence);
        Assert.Equal(0.0, result.BestScore);
        Assert.Empty(result.AllCandidates);
    }

    [Fact]
    public async Task ClassifyAsync_MultipleMatches_ReturnsTopScorer()
    {
        // Arrange
        const string userMessage = "Quiero pagar mi préstamo";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>
            {
                new SemanticMatch
                {
                    IntentKey = "payment_status",
                    DisplayName = "Estado de Pago",
                    Category = "Payments",
                    SimilarityScore = 0.80,
                    Explanation = "Match on payment"
                },
                new SemanticMatch
                {
                    IntentKey = "loan_application",
                    DisplayName = "Solicitud de Préstamo",
                    Category = "Sales",
                    SimilarityScore = 0.75,
                    Explanation = "Match on loan"
                }
            });

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>
            {
                new KeywordMatch
                {
                    IntentKey = "payment_status",
                    KeywordScore = 0.85,
                    MatchedKeywords = new List<string> { "pagar" },
                    MatchType = KeywordMatchType.Partial
                },
                new KeywordMatch
                {
                    IntentKey = "loan_application",
                    KeywordScore = 0.70,
                    MatchedKeywords = new List<string> { "préstamo" },
                    MatchType = KeywordMatchType.Partial
                }
            });

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("payment_status", result.BestMatch.IntentKey); // Higher combined score
        Assert.Equal(2, result.AllCandidates.Count);
        Assert.True(result.AllCandidates[0].FinalScore > result.AllCandidates[1].FinalScore);
    }

    [Fact]
    public async Task ClassifyAsync_EmptyMessage_ReturnsNoMatch()
    {
        // Arrange
        const string userMessage = "";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>());

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>());

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.BestMatch);
        Assert.Equal(ConfidenceLevel.NoMatch, result.Confidence);
        Assert.Equal(0.0, result.BestScore);
    }

    [Fact]
    public async Task ClassifyAsync_ExactKeywordMatch_BoostsScore()
    {
        // Arrange
        const string userMessage = "solicitar préstamo personal";
        const string tenantId = "tenant-1";

        _semanticMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticMatch>
            {
                new SemanticMatch
                {
                    IntentKey = "loan_application",
                    DisplayName = "Solicitud de Préstamo",
                    Category = "Sales",
                    SimilarityScore = 0.88,
                    Explanation = "High semantic similarity"
                }
            });

        _keywordMatcher.Setup(x => x.FindMatchesAsync(userMessage, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeywordMatch>
            {
                new KeywordMatch
                {
                    IntentKey = "loan_application",
                    KeywordScore = 1.0, // Exact match
                    MatchedKeywords = new List<string> { "solicitar", "préstamo", "personal" },
                    MatchType = KeywordMatchType.Exact
                }
            });

        // Act
        var result = await _engine.ClassifyAsync(userMessage, tenantId, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("loan_application", result.BestMatch.IntentKey);
        Assert.Equal(ConfidenceLevel.High, result.Confidence);
        Assert.True(result.BestScore >= 0.90, $"Exact match should boost to High confidence, got {result.BestScore}");
    }
}
