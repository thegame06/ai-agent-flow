using System.Reflection;
using AgentFlow.Abstractions;
using AgentFlow.Core.Engine;

namespace AgentFlow.Tests.Unit.Engine;

public sealed class MafBrainParseTests
{
    private static ThinkResult Parse(string response) =>
        (ThinkResult)typeof(MafBrain).GetMethod("ParseThinkResult", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [response])!;

    [Fact]
    public void PlainTextReply_IsTheFinalAnswer()
    {
        var result = Parse("Hola Ana, tu saldo vencido es 1,350.00 NIO. ¿Cuándo podrías pagar?");

        Assert.Equal(ThinkDecision.ProvideFinalAnswer, result.Decision);
        Assert.StartsWith("Hola Ana", result.FinalAnswer);
    }

    [Fact]
    public void JsonContract_IsStillParsed()
    {
        var result = Parse("""{"decision":"ProvideFinalAnswer","finalAnswer":"Listo","rationale":"ok"}""");
        Assert.Equal(ThinkDecision.ProvideFinalAnswer, result.Decision);
        Assert.Equal("Listo", result.FinalAnswer);
    }

    [Fact]
    public void BrokenJson_StillGoesToACheckpoint()
        => Assert.Equal(ThinkDecision.Checkpoint, Parse("""{"decision":"ProvideFinalAnswer", "finalAnswer": """).Decision);
}
