using System.Text.Json;
using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Workflow;
using AgentFlow.Application.Channels;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using Moq;

namespace AgentFlow.Tests.Unit.Workflow;

/// <summary>Regression tests for campaign/workflow delivery (messages used to never leave the platform).</summary>
public sealed class CampaignDeliveryTests
{
    private const string Tenant = "tenant-1";

    // ── workflow definition validation ──────────────────────────────────────────

    [Theory]
    [InlineData("{\"activities\":[{\"type\":\"channel.send\"}]}")]  // designer format
    [InlineData("{\"Activities\":[{\"Type\":\"channel.send\"}]}")]  // PascalCase
    public void Policy_AcceptsAnyCasing_AndAllowsChannelSend(string definitionJson)
    {
        var policy = new WorkflowSecurityPolicyService();

        var ex = Record.Exception(() => policy.ValidateDefinitionOrThrow(definitionJson));

        Assert.Null(ex);
        Assert.True(policy.IsAllowedActivityType("channel.send"));
    }

    [Fact]
    public void Policy_StillRejectsUnknownActivities()
    {
        var policy = new WorkflowSecurityPolicyService();

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateDefinitionOrThrow("{\"activities\":[{\"type\":\"shell.exec\"}]}"));
    }

    // ── template tokens ─────────────────────────────────────────────────────────

    [Fact]
    public void Template_ResolvesNestedPayloadFields_CaseInsensitive()
    {
        // Campaign payloads are serialized with PascalCase property names.
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["contact"] = new { Id = "c1", Recipient = "+5215550001", Party = new { DisplayName = "Ana" } },
            ["segment"] = "s1"
        });
        var context = WorkflowTemplateContext.FromPayloadJson(payloadJson);

        Assert.Equal("+5215550001", WorkflowTemplateContext.ResolveTokens("{{payload.contact.recipient}}", context));
        Assert.Equal("Hola Ana", WorkflowTemplateContext.ResolveTokens("Hola {{payload.contact.party.displayName}}", context));
        Assert.Equal("s1", WorkflowTemplateContext.ResolveTokens("{{payload.segment}}", context));
        Assert.Contains("\"Recipient\"", context["payload.contact"]); // the whole object is still available
    }

    // ── outbound sender ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Sender_ResolvesChannelByType_SendsThroughGateway_AndRecordsSent()
    {
        var channel = ActiveChannel(ChannelType.WhatsApp);
        var (sender, gateway, store, records) = CreateSender(channel, SendResult.Ok("wamid-1"));

        var result = await sender.SendAsync(Tenant, "whatsapp", "+5215550001", "Recordatorio de pago", "user-1", campaignId: "camp-1");

        Assert.True(result.Sent);
        Assert.Equal(channel.Id, result.ChannelId);
        gateway.Verify(g => g.SendMessageAsync(channel.Id, It.Is<ChannelMessage>(m =>
            m.Content == "Recordatorio de pago" && m.Metadata["campaign_id"] == "camp-1"), It.IsAny<CancellationToken>()), Times.Once);
        var record = Assert.Single(records);
        Assert.Equal(ConnectOperationalStatus.Sent, record.Status);
        Assert.Null(record.LastError);
    }

    [Fact]
    public async Task Sender_UnresolvedTemplateRecipient_FailsWithoutSending()
    {
        var (sender, gateway, _, records) = CreateSender(ActiveChannel(ChannelType.Api), SendResult.Ok("x"));

        var result = await sender.SendAsync(Tenant, "api", "{{payload.contact.recipient}}", "hola", "user-1");

        Assert.False(result.Sent);
        Assert.StartsWith("invalid_recipient", result.Error);
        gateway.Verify(g => g.SendMessageAsync(It.IsAny<string>(), It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(ConnectOperationalStatus.Failed, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Sender_NoActiveChannel_Fails()
    {
        var inactive = ChannelDefinition.Create(Tenant, "wa", ChannelType.WhatsApp); // never activated
        var (sender, _, _, records) = CreateSender(inactive, SendResult.Ok("x"));

        var result = await sender.SendAsync(Tenant, "whatsapp", "+5215550001", "hola", "user-1");

        Assert.False(result.Sent);
        Assert.StartsWith("no_active_channel", result.Error);
        Assert.Equal(ConnectOperationalStatus.Failed, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Sender_TransportRejects_RecordsFailedWithProviderError()
    {
        var (sender, _, _, records) = CreateSender(ActiveChannel(ChannelType.WhatsApp), SendResult.Fail("131047: re-engagement window expired"));

        var result = await sender.SendAsync(Tenant, "whatsapp", "+5215550001", "hola", "user-1");

        Assert.False(result.Sent);
        var record = Assert.Single(records);
        Assert.Equal(ConnectOperationalStatus.Failed, record.Status);
        Assert.Contains("131047", record.LastError);
    }

    private static ChannelDefinition ActiveChannel(ChannelType type)
    {
        var channel = ChannelDefinition.Create(Tenant, $"{type}-main", type);
        channel.Activate();
        return channel;
    }

    private static (OutboundMessageSender Sender, Mock<IChannelGateway> Gateway, Mock<IConnectStore> Store, List<ConnectInboxMessageContract> Records)
        CreateSender(ChannelDefinition channel, SendResult sendResult)
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(r => r.GetByIdAsync(It.IsAny<string>(), Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, CancellationToken _) => id == channel.Id ? channel : null);
        channels.Setup(r => r.GetByTypeAsync(channel.Type, Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { channel });

        var gateway = new Mock<IChannelGateway>();
        gateway.Setup(g => g.SendMessageAsync(It.IsAny<string>(), It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendResult);

        var records = new List<ConnectInboxMessageContract>();
        var store = new Mock<IConnectStore>();
        store.Setup(s => s.CreateInboxMessageAsync(It.IsAny<ConnectInboxMessageContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectInboxMessageContract m, CancellationToken _) => { records.Add(m); return m; });

        return (new OutboundMessageSender(channels.Object, gateway.Object, store.Object), gateway, store, records);
    }
}
