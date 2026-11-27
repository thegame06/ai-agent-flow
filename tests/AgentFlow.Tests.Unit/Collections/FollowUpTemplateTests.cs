using AgentFlow.Abstractions.Connect;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Messaging;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using Moq;

namespace AgentFlow.Tests.Unit.Collections;

/// <summary>
/// Follow-ups over WhatsApp's official API: free text only within 24 h of the customer's last
/// message; after that, the step's approved template (or nothing, with the reason recorded).
/// </summary>
public sealed class FollowUpTemplateTests
{
    private const string Tenant = "tenant-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OutsideThe24hWindow_TheStepGoesAsItsTemplate()
    {
        var (service, store, outbound) = Create(official: true, templateId: "tpl-1");

        await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        var metadata = SentMetadata(outbound);
        Assert.Contains(WhatsAppTemplatePayload.MetadataKey, metadata.Keys);
        Assert.Equal("tpl-1", metadata["wa.template_id"]);
        var record = Assert.Single(store.Contact.FollowUps!);
        Assert.Equal(("sent", "seguimiento_saldo"), (record.Status, record.Template));
        Assert.Contains("Ana", record.Message); // the customer's data, filled in
    }

    [Fact]
    public async Task OutsideTheWindow_WithoutATemplate_NothingIsSent_AndTheReasonIsKept()
    {
        var (service, store, outbound) = Create(official: true, templateId: null);

        var summary = await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        Assert.Equal(1, summary.Failed);
        outbound.VerifyNoOtherCalls();
        Assert.Contains("24 h", Assert.Single(store.Contact.FollowUps!).Error);
    }

    [Fact]
    public async Task InsideTheWindow_TheStepIsSentAsText()
    {
        // The customer wrote 3 hours ago: WhatsApp still allows free text.
        var (service, _, outbound) = Create(official: true, templateId: "tpl-1", lastReplyHoursAgo: 3, trigger: "outcome:callback_requested");

        await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        Assert.DoesNotContain(WhatsAppTemplatePayload.MetadataKey, SentMetadata(outbound).Keys);
    }

    [Fact]
    public async Task OverQr_TextIsFine_EvenAfter24h()
    {
        var (service, _, outbound) = Create(official: false, templateId: null);

        await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        Assert.DoesNotContain(WhatsAppTemplatePayload.MetadataKey, SentMetadata(outbound).Keys);
    }

    [Theory]
    [InlineData("PENDING", "waba-1", "no esta aprobada")]
    [InlineData("APPROVED", "waba-otra", "otra cuenta de Meta")]
    public async Task ATemplateThatCannotBeUsed_IsNotSent(string status, string waba, string reason)
    {
        var (service, store, outbound) = Create(official: true, templateId: "tpl-1", templateStatus: status, templateWaba: waba);

        await service.RunForCampaignAsync(Tenant, "camp-1", Now);

        outbound.VerifyNoOtherCalls();
        Assert.Contains(reason, Assert.Single(store.Contact.FollowUps!).Error);
    }

    // ------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> SentMetadata(Mock<IOutboundMessageSender> outbound)
    {
        var call = Assert.Single(outbound.Invocations.Where(i => i.Method.Name == nameof(IOutboundMessageSender.SendAsync)));
        return (IReadOnlyDictionary<string, string>)call.Arguments[7]!;
    }

    private sealed class Store
    {
        public CampaignContactExecutionContract Contact { get; set; } = null!;
    }

    private static (CampaignFollowUpService, Store, Mock<IOutboundMessageSender>) Create(
        bool official,
        string? templateId,
        double? lastReplyHoursAgo = null,
        string trigger = "no_response",
        string templateStatus = "APPROVED",
        string templateWaba = "waba-1")
    {
        var policy = new CampaignFollowUpPolicy
        {
            Rules = [new() { Id = "paso", Trigger = trigger, AfterHours = 1, Message = "Hola {{primer_nombre}}, debes {{total_vencido}}", WhatsAppTemplateId = templateId }]
        };
        var campaign = new CampaignContract
        {
            Id = "camp-1", TenantId = Tenant, Channel = "whatsapp", Timezone = "America/Mexico_City", Enabled = true,
            FollowupPolicyJson = System.Text.Json.JsonSerializer.Serialize(policy, CampaignFollowUpPolicy.Json)
        };
        var state = new Store
        {
            Contact = new CampaignContactExecutionContract
            {
                Id = "c1", TenantId = Tenant, CampaignId = "camp-1", RunId = "r1", Channel = "whatsapp",
                Recipient = "+525511111111", RecipientKey = RecipientKeys.From("+525511111111"),
                Status = CampaignContactExecutionStatus.Sent,
                Variables = new Dictionary<string, string> { ["primer_nombre"] = "Ana", ["total_vencido"] = "1,200.00" },
                CreatedAt = Now.AddHours(-50),
                Outcome = trigger.StartsWith("outcome:") ? trigger[8..] : null,
                OutcomeAt = Now.AddHours(-5),
                RepliedAt = lastReplyHoursAgo is null ? null : Now.AddHours(-lastReplyHoursAgo.Value),
                LastReplyAt = lastReplyHoursAgo is null ? null : Now.AddHours(-lastReplyHoursAgo.Value)
            }
        };

        var campaigns = new Mock<ICampaignStore>();
        campaigns.Setup(s => s.GetCampaignAsync(Tenant, "camp-1", It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        campaigns.Setup(s => s.GetOptedOutRecipientKeysAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new HashSet<string>());
        campaigns.Setup(s => s.GetContactExecutionsByCampaignAsync(Tenant, "camp-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [state.Contact]);
        campaigns.Setup(s => s.UpdateContactExecutionAsync(It.IsAny<CampaignContactExecutionContract>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CampaignContactExecutionContract c, CancellationToken _) => state.Contact = c);

        var outbound = new Mock<IOutboundMessageSender>();
        outbound.Setup(o => o.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboundSendResult(true, "ch", "msg", "inbox", null));

        var channel = ChannelDefinition.Create(Tenant, "WhatsApp Cobranza", ChannelType.WhatsApp,
            official
                ? new Dictionary<string, string> { ["AuthMode"] = "business", ["BusinessAccountId"] = "waba-1" }
                : new Dictionary<string, string> { ["AuthMode"] = "qr" });
        channel.Activate();
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(c => c.GetByTypeAsync(ChannelType.WhatsApp, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);

        var templates = new Mock<IWhatsAppTemplateStore>();
        templates.Setup(t => t.GetAsync(Tenant, "tpl-1", It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateDocument
        {
            Id = "tpl-1",
            TenantId = Tenant,
            Name = "seguimiento_saldo",
            Status = templateStatus,
            BusinessAccountId = templateWaba,
            Components = [new() { Type = "BODY", Text = "Hola {{1}}, tu saldo vencido es {{2}}. Responde para ayudarte." }],
            Parameters =
            [
                new() { Component = "body", Key = "1", Source = "field", Field = "primer_nombre" },
                new() { Component = "body", Key = "2", Source = "field", Field = "total_vencido" },
            ]
        });

        return (new CampaignFollowUpService(campaigns.Object, outbound.Object, null, templates.Object, channels.Object), state, outbound);
    }
}
