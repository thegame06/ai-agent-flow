using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Channels;
using AgentFlow.Api.Collections;
using AgentFlow.Domain.Aggregates;

namespace AgentFlow.Tests.Unit.Collections;

/// <summary>
/// What the live Twilio and QR tests showed: "no se" read as a refusal, a later "hola" repeating the
/// assistant's last action, and a greeting with the placeholder name "Cliente".
/// </summary>
public sealed class AssistantConversationQualityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 18, 0, 0, TimeSpan.FromHours(-6));

    [Theory]
    [InlineData("No se")]
    [InlineData("No sé")]
    [InlineData("Algún día. Me pueden prestar más plata?")]
    [InlineData("todavía no sé")]
    [InlineData("cuando pueda")]
    [InlineData("el próximo año")]
    public void AnAnswerWithoutCommitment_IsNotARefusal(string reply)
        => Assert.Equal(CampaignOutcomes.Replied, CampaignReplyClassifier.Classify(reply, Now).Outcome);

    [Theory]
    [InlineData("Tal vez el próximo año, estoy desempleado")]
    [InlineData("me despidieron y no tengo plata")]
    public void SayingTheyCannotPay_CountsEvenWithDoubt(string reply)
        => Assert.Equal(CampaignOutcomes.CannotPay, CampaignReplyClassifier.Classify(reply, Now).Outcome);

    [Theory]
    [InlineData("No")]
    [InlineData("no quiero pagar")]
    [InlineData("no me interesa")]
    public void ARealRefusal_StaysNotInterested(string reply)
        => Assert.Equal(CampaignOutcomes.NotInterested, CampaignReplyClassifier.Classify(reply, Now).Outcome);

    [Fact]
    public void NoSeSiPuedo_StaysUnclear()
        => Assert.Null(CampaignReplyClassifier.Classify("no se si pueda pagar el viernes", Now).Outcome);

    [Theory]
    [InlineData("Cliente Prueba WhatsApp", null)]
    [InlineData("Unknown", null)]
    [InlineData("Carlos Méndez Soto", "Carlos")]
    [InlineData("ana lópez", "ana")]
    [InlineData("50581143874", null)]
    [InlineData(null, null)]
    public void TheGreeting_UsesARealFirstNameOrNone(string? fullName, string? expected)
        => Assert.Equal(expected, CollectionsAssistantService.FirstNameOf(fullName));

    [Fact]
    public void WritingAgainAfterManyHours_StartsAFreshConversationForTheAssistant()
    {
        var session = Session(hoursIdle: 13);

        Assert.True(InboundMessageIngest.RestartIfIdle(session, TimeSpan.FromHours(12), DateTimeOffset.UtcNow));
        Assert.Null(session.ThreadId);
        Assert.True(session.Metadata.ContainsKey("conversation.restarted_at"));
    }

    [Fact]
    public void AConversationInProgress_KeepsItsThread()
    {
        var session = Session(hoursIdle: 2);
        Assert.False(InboundMessageIngest.RestartIfIdle(session, TimeSpan.FromHours(12), DateTimeOffset.UtcNow));
        Assert.Equal("thread-1", session.ThreadId);
    }

    [Fact]
    public void AConversationAPersonOwns_IsNeverRestarted()
    {
        var session = Session(hoursIdle: 30);
        ConversationHandoff.Request(session, "no puede pagar", "assistant", DateTimeOffset.UtcNow);
        Assert.False(InboundMessageIngest.RestartIfIdle(session, TimeSpan.FromHours(12), DateTimeOffset.UtcNow));
        Assert.Equal("thread-1", session.ThreadId);
    }

    private static ChannelSession Session(int hoursIdle)
    {
        var session = ChannelSession.Create("tenant-1", "channel-1", ChannelType.WhatsApp, "+50581143874");
        session.LinkThread("thread-1");
        session.RecordIncomingMessage("hola");
        typeof(ChannelSession).GetProperty(nameof(ChannelSession.LastActivityAt))!.SetValue(session, DateTimeOffset.UtcNow.AddHours(-hoursIdle));
        return session;
    }
}
