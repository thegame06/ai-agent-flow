# 🎯 Hybrid Scoring Engine - Usage Examples

> **Fecha**: 2026-05-18  
> **Componente**: AgentFlow.Intents - Intent Classification System  
> **Estado**: ✅ PRODUCTION READY

---

## 📋 Quick Start

### 1. Register Services

```csharp
using AgentFlow.Intents;

// In your Startup.cs or Program.cs
public void ConfigureServices(IServiceCollection services)
{
    // Register all intent routing services
    services.AddIntentRouting();
    
    // Register your embedding generator implementation
    services.AddSingleton<IEmbeddingGenerator, YourEmbeddingImplementation>();
    
    // Register vector memory (if not already registered)
    services.AddSingleton<IVectorMemory, QdrantVectorMemory>();
    
    // Register intent routing store (if not already registered)
    services.AddSingleton<IIntentRoutingStore, MongoIntentRoutingStore>();
}
```

---

## 🚀 Basic Usage

### Example 1: Simple Classification

```csharp
using AgentFlow.Intents.Classification;
using AgentFlow.Intents.Classification.Models;

public class MyService
{
    private readonly IIntentScoringEngine _scoringEngine;
    private readonly ILogger<MyService> _logger;

    public MyService(
        IIntentScoringEngine scoringEngine,
        ILogger<MyService> logger)
    {
        _scoringEngine = scoringEngine;
        _logger = logger;
    }

    public async Task<string> RouteMessageAsync(
        string message,
        string tenantId,
        string channel,
        CancellationToken ct = default)
    {
        // Classify the intent
        var result = await _scoringEngine.ClassifyAsync(
            message: message,
            tenantId: tenantId,
            channel: channel,
            ct: ct);

        // Log the decision
        _logger.LogInformation(
            "Intent classification completed: {IntentKey} with confidence {Confidence}",
            result.BestMatch?.IntentKey ?? "NO_MATCH",
            result.Confidence);

        // Make routing decision based on confidence
        if (result.Confidence >= ConfidenceLevel.Medium)
        {
            // Auto-route with confidence
            var targetAgent = result.BestMatch!.Rule.TargetAgentId;
            _logger.LogInformation("Auto-routing to agent: {AgentId}", targetAgent);
            return targetAgent;
        }
        else
        {
            // Low confidence or no match - route to human
            _logger.LogWarning(
                "Low confidence ({Confidence}) - routing to human agent. Explanation: {Explanation}",
                result.Confidence,
                result.ExplanationJson);
            return "human-agent-fallback";
        }
    }
}
```

### Example 2: With Full Decision Logging

```csharp
public async Task ProcessIncomingMessageAsync(
    string message,
    string tenantId,
    string channel)
{
    var result = await _scoringEngine.ClassifyAsync(message, tenantId, channel);

    // Log full decision details for audit
    _logger.LogInformation(
        @"Intent Classification Result:
        Message: {Message}
        Best Match: {IntentKey}
        Confidence: {Confidence}
        Score: {Score:P}
        Requires Review: {RequiresReview}
        
        All Candidates:
        {Candidates}
        
        Full Explanation:
        {Explanation}",
        result.Message,
        result.BestMatch?.IntentKey ?? "NO_MATCH",
        result.Confidence,
        result.BestScore,
        result.RequiresHumanReview,
        string.Join("\n        ", result.AllCandidates.Select(c => 
            $"- {c.IntentKey}: {c.SimilarityScore:F3}")),
        result.ExplanationJson);

    // Process based on confidence level
    switch (result.Confidence)
    {
        case ConfidenceLevel.High:
            await AutoRouteWithConfidenceAsync(result);
            break;
            
        case ConfidenceLevel.Medium:
            await AutoRouteWithMonitoringAsync(result);
            break;
            
        case ConfidenceLevel.Low:
            await RouteToHumanReviewAsync(result);
            break;
            
        case ConfidenceLevel.NoMatch:
            await HandleNoMatchAsync(result);
            break;
    }
}

private async Task AutoRouteWithConfidenceAsync(IntentClassificationResult result)
{
    var rule = result.BestMatch!.Rule;
    
    _logger.LogInformation(
        "✅ High confidence routing: {IntentKey} → Agent {AgentId}",
        rule.IntentKey,
        rule.TargetAgentId);

    if (rule.WorkflowDefinitionId != null)
    {
        // Trigger workflow
        await TriggerWorkflowAsync(rule.WorkflowDefinitionId, result.Message);
    }
    else
    {
        // Hand off to target agent
        await HandoffToAgentAsync(rule.TargetAgentId, result.Message);
    }
}

private async Task AutoRouteWithMonitoringAsync(IntentClassificationResult result)
{
    var rule = result.BestMatch!.Rule;
    
    _logger.LogWarning(
        "⚠️ Medium confidence routing: {IntentKey} → Agent {AgentId} (Score: {Score:F3})",
        rule.IntentKey,
        rule.TargetAgentId,
        result.BestScore);

    // Log to monitoring system for quality tracking
    await LogClassificationQualityAsync(result);

    // Proceed with routing
    await AutoRouteWithConfidenceAsync(result);
}

private async Task RouteToHumanReviewAsync(IntentClassificationResult result)
{
    _logger.LogWarning(
        "🔍 Low confidence - human review required: {IntentKey} (Score: {Score:F3})",
        result.BestMatch?.IntentKey ?? "NO_MATCH",
        result.BestScore);

    // Create review ticket
    await CreateHumanReviewTicketAsync(new ReviewTicket
    {
        Message = result.Message,
        SuggestedIntent = result.BestMatch?.IntentKey,
        ConfidenceScore = result.BestScore,
        Explanation = result.ExplanationJson,
        CreatedAt = DateTimeOffset.UtcNow
    });

    // Send to human agent
    await RouteToHumanAgentAsync(result.Message);
}

private async Task HandleNoMatchAsync(IntentClassificationResult result)
{
    _logger.LogError(
        "❌ No intent match found for message: '{Message}' (Score: {Score:F3})",
        result.Message,
        result.BestScore);

    // Log for intent catalog improvement
    await LogUnmatchedMessageAsync(result);

    // Route to default fallback handler
    await RouteToFallbackHandlerAsync(result.Message);
}
```

---

## 🔬 Advanced Examples

### Example 3: Batch Classification

```csharp
public async Task<IReadOnlyList<IntentClassificationResult>> ClassifyBatchAsync(
    IReadOnlyList<string> messages,
    string tenantId,
    string channel,
    CancellationToken ct = default)
{
    // Process all messages in parallel
    var tasks = messages.Select(message => 
        _scoringEngine.ClassifyAsync(message, tenantId, channel, ct));

    var results = await Task.WhenAll(tasks);

    // Generate batch summary
    var summary = new
    {
        TotalMessages = messages.Count,
        HighConfidence = results.Count(r => r.Confidence == ConfidenceLevel.High),
        MediumConfidence = results.Count(r => r.Confidence == ConfidenceLevel.Medium),
        LowConfidence = results.Count(r => r.Confidence == ConfidenceLevel.Low),
        NoMatch = results.Count(r => r.Confidence == ConfidenceLevel.NoMatch),
        RequiresReview = results.Count(r => r.RequiresHumanReview),
        AverageScore = results.Average(r => r.BestScore)
    };

    _logger.LogInformation("Batch classification summary: {@Summary}", summary);

    return results;
}
```

### Example 4: Custom Confidence Handling

```csharp
public async Task<RoutingDecision> RouteWithCustomPolicyAsync(
    string message,
    string tenantId,
    string channel)
{
    var result = await _scoringEngine.ClassifyAsync(message, tenantId, channel);

    // Custom business logic based on confidence and score
    var decision = new RoutingDecision
    {
        IntentKey = result.BestMatch?.IntentKey,
        TargetAgent = result.BestMatch?.Rule.TargetAgentId,
        AutoRoute = false,
        Reason = "Pending evaluation"
    };

    // Custom thresholds for high-risk intents
    if (IsHighRiskIntent(result.BestMatch?.IntentKey))
    {
        // Require higher confidence for sensitive operations
        if (result.BestScore >= 0.95f)
        {
            decision.AutoRoute = true;
            decision.Reason = "High confidence on high-risk intent";
        }
        else
        {
            decision.AutoRoute = false;
            decision.Reason = "High-risk intent requires score ≥ 0.95";
        }
    }
    else
    {
        // Use standard confidence levels for normal intents
        decision.AutoRoute = result.Confidence >= ConfidenceLevel.Medium;
        decision.Reason = $"Standard routing with {result.Confidence} confidence";
    }

    return decision;
}

private bool IsHighRiskIntent(string? intentKey)
{
    var highRiskIntents = new[] 
    { 
        "wire_transfer", 
        "account_closure", 
        "loan_approval",
        "password_reset" 
    };
    
    return intentKey != null && highRiskIntents.Contains(intentKey);
}
```

### Example 5: A/B Testing Different Scoring Weights

```csharp
public class CustomScoringEngine
{
    private readonly ISemanticIntentMatcher _semanticMatcher;
    private readonly IKeywordIntentMatcher _keywordMatcher;

    // Configurable weights for A/B testing
    private float _semanticWeight;
    private float _keywordWeight;
    private float _priorityWeight;

    public void SetWeights(float semantic, float keyword, float priority)
    {
        if (Math.Abs(semantic + keyword + priority - 1.0f) > 0.001f)
        {
            throw new ArgumentException("Weights must sum to 1.0");
        }

        _semanticWeight = semantic;
        _keywordWeight = keyword;
        _priorityWeight = priority;
    }

    public async Task<IntentClassificationResult> ClassifyWithCustomWeightsAsync(
        string message,
        string tenantId,
        string channel)
    {
        // Get candidates from both matchers
        var semanticTask = _semanticMatcher.FindCandidatesAsync(message, tenantId, channel);
        var keywordTask = _keywordMatcher.FindCandidatesAsync(message, tenantId, channel);

        await Task.WhenAll(semanticTask, keywordTask);

        var semanticCandidates = await semanticTask;
        var keywordCandidates = await keywordTask;

        // Apply custom weights
        var combinedScores = CombineWithCustomWeights(
            semanticCandidates,
            keywordCandidates,
            _semanticWeight,
            _keywordWeight,
            _priorityWeight);

        // ... rest of classification logic
    }
}
```

---

## 📊 Monitoring & Observability

### Example 6: Telemetry Integration

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

public class TelemetryAwareIntentRouter
{
    private readonly IIntentScoringEngine _scoringEngine;
    private readonly Meter _meter;
    private readonly Counter<int> _classificationsCounter;
    private readonly Histogram<double> _scoreHistogram;

    public TelemetryAwareIntentRouter(IIntentScoringEngine scoringEngine)
    {
        _scoringEngine = scoringEngine;
        _meter = new Meter("AgentFlow.Intents");
        
        _classificationsCounter = _meter.CreateCounter<int>(
            "intent.classifications.total",
            description: "Total number of intent classifications");
            
        _scoreHistogram = _meter.CreateHistogram<double>(
            "intent.classification.score",
            description: "Distribution of classification scores");
    }

    public async Task<IntentClassificationResult> ClassifyWithTelemetryAsync(
        string message,
        string tenantId,
        string channel)
    {
        using var activity = Activity.Current?.Source.StartActivity("ClassifyIntent");
        activity?.SetTag("tenant.id", tenantId);
        activity?.SetTag("channel", channel);

        var result = await _scoringEngine.ClassifyAsync(message, tenantId, channel);

        // Record metrics
        _classificationsCounter.Add(1, new KeyValuePair<string, object?>("confidence", result.Confidence.ToString()));
        _scoreHistogram.Record(result.BestScore);

        // Add telemetry to activity
        activity?.SetTag("intent.key", result.BestMatch?.IntentKey ?? "NO_MATCH");
        activity?.SetTag("confidence", result.Confidence.ToString());
        activity?.SetTag("score", result.BestScore);
        activity?.SetTag("requires_review", result.RequiresHumanReview);

        return result;
    }
}
```

---

## 🧪 Testing Examples

### Example 7: Unit Test

```csharp
using Moq;
using Xunit;

public class IntentScoringEngineTests
{
    [Fact]
    public async Task ClassifyAsync_HighConfidence_ReturnsAutoRoute()
    {
        // Arrange
        var semanticMatcherMock = new Mock<ISemanticIntentMatcher>();
        var keywordMatcherMock = new Mock<IKeywordIntentMatcher>();
        var loggerMock = new Mock<ILogger<IntentScoringEngine>>();

        var rule = new IntentRoutingRule
        {
            Id = "rule-1",
            TenantId = "tenant-1",
            IntentKey = "loan_application",
            Priority = 1000,
            // ... other required properties
        };

        var semanticMatch = new IntentMatch
        {
            IntentKey = "loan_application",
            SimilarityScore = 0.95f,
            MatchedVia = "semantic",
            Rule = rule
        };

        semanticMatcherMock
            .Setup(m => m.FindCandidatesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { semanticMatch });

        keywordMatcherMock
            .Setup(m => m.FindCandidatesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IntentMatch>());

        var engine = new IntentScoringEngine(
            semanticMatcherMock.Object,
            keywordMatcherMock.Object,
            loggerMock.Object);

        // Act
        var result = await engine.ClassifyAsync(
            "I want to apply for a loan",
            "tenant-1",
            "whatsapp");

        // Assert
        Assert.NotNull(result.BestMatch);
        Assert.Equal("loan_application", result.BestMatch.IntentKey);
        Assert.Equal(ConfidenceLevel.High, result.Confidence);
        Assert.False(result.RequiresHumanReview);
        Assert.True(result.BestScore >= 0.90f);
    }
}
```

---

## 📖 Best Practices

### ✅ DO

1. **Always check `RequiresHumanReview` flag** before auto-routing
2. **Log `ExplanationJson` for audit compliance** in regulated environments
3. **Monitor confidence distribution** to identify intent catalog gaps
4. **Use batch classification** for high-throughput scenarios
5. **Implement custom confidence policies** for high-risk intents

### ❌ DON'T

1. **Don't ignore Low confidence results** - they indicate gaps in your intent catalog
2. **Don't hardcode confidence thresholds** - make them configurable per tenant
3. **Don't skip telemetry** - classification metrics are critical for quality monitoring
4. **Don't auto-route NoMatch results** - always have a fallback handler
5. **Don't discard `AllCandidates`** - they're useful for debugging and improvement

---

## 🔗 Related Documentation

- [INTENT-ROUTING-ARCHITECTURE.md](../../docs/INTENT-ROUTING-ARCHITECTURE.md) - Full architecture overview
- [INTENT-ROUTING-IMPLEMENTATION-PLAN.md](../../docs/INTENT-ROUTING-IMPLEMENTATION-PLAN.md) - Implementation roadmap
- [README.md](./README.md) - Module documentation
- [IMPLEMENTATION-SUMMARY.md](./IMPLEMENTATION-SUMMARY.md) - Technical implementation details
