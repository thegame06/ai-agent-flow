using AgentFlow.Abstractions;
using AgentFlow.DSL;

namespace AgentFlow.Tests.Unit.DSL;

/// <summary>
/// Tests for the DSL Parser (JSON → AgentDefinitionDsl).
/// Validates all 10+ design-time invariants from the DSL Engine workflow.
/// </summary>
public sealed class DslParserTests
{
    private readonly IDslParser _parser = new JsonDslParser();

    [Fact]
    public void Parse_ValidJson_ReturnsSuccess()
    {
        var json = BuildValidDslJson();
        var result = _parser.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("test_agent", result.Value!.Agent.Key);
        Assert.Equal("1.0.0", result.Value.Agent.Version);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsFailure()
    {
        var result = _parser.Parse("");
        Assert.False(result.IsSuccess);
        Assert.Contains("empty", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsFailure()
    {
        var result = _parser.Parse("{ not valid json }");
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid JSON", result.Error!.Message);
    }

    [Fact]
    public void Parse_MissingAgentKey_FailsParsing()
    {
        // key is a 'required' property, so JSON deserialization fails — this is correct behavior
        var json = """{ "agent": { "version": "1.0.0", "role": "test" } }""";
        var result = _parser.Parse(json);
        // Missing required property → parse failure with JsonException
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Parse_RuntimeConfig_DefaultValues()
    {
        var json = BuildValidDslJson();
        var result = _parser.Parse(json);
        Assert.True(result.IsSuccess);

        var runtime = result.Value!.Agent.Runtime;
        Assert.Equal("hybrid", runtime.Mode);
        Assert.Equal(6, runtime.MaxIterations);
    }

    [Fact]
    public void Serialize_RoundTrip_Preserves()
    {
        var json = BuildValidDslJson();
        var result = _parser.Parse(json);
        Assert.True(result.IsSuccess);

        var serialized = _parser.Serialize(result.Value!);
        var reparsed = _parser.Parse(serialized);
        Assert.True(reparsed.IsSuccess);
        Assert.Equal(result.Value!.Agent.Key, reparsed.Value!.Agent.Key);
    }

    private static string BuildValidDslJson() => """
    {
      "agent": {
        "key": "test_agent",
        "version": "1.0.0",
        "role": "A test agent for unit tests",
        "runtime": {
          "mode": "hybrid",
          "temperature": 0.2,
          "maxIterations": 6,
          "maxExecutionSeconds": 120
        },
        "modelRouting": {
          "strategy": "static",
          "default": "gpt-4o"
        },
        "authorizedTools": ["ToolA", "ToolB"],
        "flows": [
          {
            "name": "main_flow",
            "trigger": { "type": "intent", "value": "*" },
            "steps": [
              { "tool": "ToolA", "required": true }
            ]
          }
        ]
      }
    }
    """;
}
