using System.Text.Json;
using AgentFlow.Abstractions;
using AgentFlow.Api.Campaigns;
using AgentFlow.Api.Messaging;
using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;
using AgentFlow.Domain.Repositories;
using AgentFlow.Infrastructure.Channels.WhatsApp;
using AgentFlow.Infrastructure.Providers;
using Moq;

namespace AgentFlow.Tests.Unit.Messaging;

/// <summary>
/// Twilio Content templates live in the same catalog as Meta's: synced from Twilio, created there
/// from AgentFlow, and sent by Content SID with each variable by its number.
/// </summary>
public sealed class TwilioTemplateTests
{
    private const string Tenant = "tenant-1";
    private static readonly TwilioAccount Account = new("AC123", "token");

    [Fact]
    public void ContentAndApprovals_Item_IsReadWithItsWhatsAppApproval()
    {
        using var doc = JsonDocument.Parse("""
        {
          "sid": "HX1", "friendly_name": "recordatorio_pago", "language": "es",
          "variables": { "1": "Carlos", "2": "450" },
          "types": {
            "twilio/text": { "body": "Hola {{1}}, tu saldo es {{2}}." },
            "twilio/quick-reply": { "body": "Hola {{1}}, tu saldo es {{2}}.", "actions": [ { "title": "Ya pague", "id": "a" }, { "title": "Hablar con asesor", "id": "b" } ] }
          },
          "approval_requests": { "name": "recordatorio_pago", "category": "utility", "status": "received", "rejection_reason": "" }
        }
        """);

        var content = TwilioContentApi.Parse(doc.RootElement);

        Assert.Equal("Hola {{1}}, tu saldo es {{2}}.", content.Body);
        Assert.Equal(["Ya pague", "Hablar con asesor"], content.QuickReplies);
        Assert.Equal("450", content.Examples["2"]);
        Assert.Equal("UTILITY", content.Category);
        Assert.Equal(WhatsAppTemplateStatuses.Pending, content.Status);
        Assert.Null(content.RejectionReason);
    }

    [Theory]
    [InlineData("approved", WhatsAppTemplateStatuses.Approved)]
    [InlineData("rejected", WhatsAppTemplateStatuses.Rejected)]
    [InlineData("paused", WhatsAppTemplateStatuses.Paused)]
    [InlineData("unsubmitted", WhatsAppTemplateStatuses.Unsubmitted)]
    [InlineData(null, WhatsAppTemplateStatuses.Unsubmitted)]
    public void TwilioStatuses_MapToTheCatalog(string? twilio, string expected)
        => Assert.Equal(expected, TwilioContentApi.StatusOf(twilio));

    [Fact]
    public async Task Sync_AddsTheAccountsTemplates_ThenFollowsStatus_AndMarksDeleted()
    {
        var store = new MemoryStore();
        var api = new Mock<ITwilioContentApi>();
        api.SetupSequence(a => a.ListAsync(Account, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Content("HX1", "recordatorio_pago", "Hola {{1}}", "pending"), Content("HX2", "aviso", "Aviso {{1}}", "approved")])
            .ReturnsAsync([Content("HX1", "recordatorio_pago", "Hola {{1}}", "approved")]);
        var service = Service(store, api, TwilioChannel());

        var first = await service.SyncAsync(Tenant, null, "u1");
        var second = await service.SyncAsync(Tenant, null, "u1");

        Assert.Equal(2, first.Created);
        Assert.Equal(1, second.StatusChanged);
        Assert.Equal(1, second.Removed);
        var all = await store.ListAsync(Tenant);
        var reminder = all.Single(t => t.ContentSid == "HX1");
        Assert.Equal(WhatsAppTemplateProviders.Twilio, reminder.Provider);
        Assert.Equal("AC123", reminder.BusinessAccountId);
        Assert.Equal("twilio-main", reminder.ConnectionId);
        Assert.Equal(WhatsAppTemplateStatuses.Approved, reminder.Status);
        Assert.Equal("Carlos", reminder.Parameters.Single().Example);
        Assert.Equal(WhatsAppTemplateStatuses.Deleted, all.Single(t => t.ContentSid == "HX2").Status);
    }

    [Fact]
    public async Task Create_NumbersTheFields_AndKeepsItUnsubmitted_WhenWhatsAppReviewIsRefused()
    {
        var store = new MemoryStore();
        var api = new Mock<ITwilioContentApi>();
        string? sentBody = null;
        IReadOnlyDictionary<string, string>? sentExamples = null;
        api.Setup(a => a.CreateAsync(Account, "recordatorio_pago", "es", It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback((TwilioAccount _, string _, string _, string body, IReadOnlyList<string> _, IReadOnlyDictionary<string, string> examples, CancellationToken _) => (sentBody, sentExamples) = (body, examples))
            .ReturnsAsync("HX9");
        api.Setup(a => a.SubmitForWhatsAppAsync(Account, "HX9", "recordatorio_pago", "UTILITY", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TwilioApiException("Sandbox numbers cannot have templates", 400));
        var channel = TwilioChannel();

        var result = await Service(store, api, channel).CreateAsync(Tenant, channel, new TemplateDraft
        {
            ChannelId = channel.Id,
            Name = "recordatorio_pago",
            Body = "Hola {{primer_nombre}}, tu saldo vencido es {{total_vencido}}."
        }, "u1");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Hola {{1}}, tu saldo vencido es {{2}}.", sentBody);
        Assert.Equal(2, sentExamples!.Count);
        var saved = Assert.Single(await store.ListAsync(Tenant));
        Assert.Equal("HX9", saved.ContentSid);
        Assert.Equal(WhatsAppTemplateStatuses.Unsubmitted, saved.Status);
        Assert.Contains("Sandbox", saved.RejectedReason);
        Assert.Equal(["primer_nombre", "total_vencido"], saved.Parameters.Select(p => p.Field));
    }

    [Fact]
    public void ATwilioTemplate_IsSentByContentSid_WithEachVariableByItsNumber()
    {
        var template = new WhatsAppTemplateDocument
        {
            TenantId = Tenant,
            Provider = WhatsAppTemplateProviders.Twilio,
            ContentSid = "HX1",
            Name = "recordatorio_pago",
            Status = WhatsAppTemplateStatuses.Approved,
            Components = [new WhatsAppTemplateComponent { Type = "BODY", Text = "Tu saldo es {{2}}, {{1}}." }],
            Parameters =
            [
                new WhatsAppTemplateParameter { Component = "body", Key = "1", Source = "field", Field = "primer_nombre" },
                new WhatsAppTemplateParameter { Component = "body", Key = "2", Source = "field", Field = "total_vencido" }
            ]
        };

        var built = WhatsAppTemplateText.Build(template, new Dictionary<string, string> { ["primer_nombre"] = "Carlos", ["total_vencido"] = "450" });
        var payload = WhatsAppTemplatePayload.From(new Dictionary<string, string> { [WhatsAppTemplatePayload.MetadataKey] = built.ToSendJson() })!;
        var form = TwilioWhatsAppProviderAdapter.BuildForm(new ProviderMessageSendRequest
        {
            Recipient = "+50581143874",
            Content = built.Text,
            TemplateName = payload.Name,
            TemplateComponentsJson = payload.ComponentsJson
        }, "+14155238886");

        Assert.Equal("Tu saldo es 450, Carlos.", built.Text);
        Assert.Equal("HX1", form["ContentSid"]);
        Assert.False(form.ContainsKey("Body"));
        var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(form["ContentVariables"])!;
        Assert.Equal("Carlos", variables["1"]);
        Assert.Equal("450", variables["2"]);
    }

    [Fact]
    public void ATemplate_OnlyGoesOutThroughAChannelOfItsProvider()
    {
        var twilioTemplate = new WhatsAppTemplateDocument { Name = "t", Provider = WhatsAppTemplateProviders.Twilio, ContentSid = "HX1" };
        var metaTemplate = new WhatsAppTemplateDocument { Name = "m", Provider = WhatsAppTemplateProviders.Meta, BusinessAccountId = "waba-1" };
        var twilio = TwilioChannel();
        var meta = ChannelDefinition.Create(Tenant, "Meta", ChannelType.WhatsApp, new Dictionary<string, string> { ["AuthMode"] = "business", ["BusinessAccountId"] = "waba-1" });

        Assert.Null(CampaignChannels.TemplateProblem(twilioTemplate, twilio));
        Assert.Null(CampaignChannels.TemplateProblem(metaTemplate, meta));
        Assert.Contains("Twilio", CampaignChannels.TemplateProblem(twilioTemplate, meta));
        Assert.Contains("Meta", CampaignChannels.TemplateProblem(metaTemplate, twilio));
    }

    private static TwilioContent Content(string sid, string name, string body, string status)
        => new(sid, name, "es", body, [], new Dictionary<string, string> { ["1"] = "Carlos" }, name, "UTILITY", TwilioContentApi.StatusOf(status), null);

    private static ChannelDefinition TwilioChannel()
        => ChannelDefinition.Create(Tenant, "WhatsApp por Twilio", ChannelType.WhatsApp,
            new Dictionary<string, string> { ["AuthMode"] = "twilio", ["Provider"] = "twilio", ["ConnectionId"] = "twilio-main" });

    private static TwilioTemplateService Service(MemoryStore store, Mock<ITwilioContentApi> api, ChannelDefinition channel)
    {
        var channels = new Mock<IChannelDefinitionRepository>();
        channels.Setup(c => c.GetByTypeAsync(ChannelType.WhatsApp, Tenant, It.IsAny<CancellationToken>())).ReturnsAsync([channel]);
        var credentials = new Mock<ITwilioCredentials>();
        credentials.Setup(c => c.GetAsync(Tenant, "twilio-main", It.IsAny<CancellationToken>())).ReturnsAsync(Account);
        return new TwilioTemplateService(store, channels.Object, credentials.Object, api.Object);
    }

    private sealed class MemoryStore : IWhatsAppTemplateStore
    {
        private readonly Dictionary<string, WhatsAppTemplateDocument> _templates = [];
        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

        public Task<IReadOnlyList<WhatsAppTemplateDocument>> ListAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppTemplateDocument>>(_templates.Values.Where(t => t.TenantId == tenantId).Select(Copy).ToList());
        public Task<WhatsAppTemplateDocument?> GetAsync(string tenantId, string id, CancellationToken ct = default)
            => Task.FromResult(_templates.TryGetValue(id, out var t) ? Copy(t) : null);
        public Task<WhatsAppTemplateDocument?> FindByMetaIdAsync(string tenantId, string metaTemplateId, CancellationToken ct = default)
            => Task.FromResult<WhatsAppTemplateDocument?>(null);
        public Task SaveAsync(WhatsAppTemplateDocument template, CancellationToken ct = default) { _templates[template.Id] = Copy(template); return Task.CompletedTask; }
        public Task DeleteAsync(string tenantId, string id, CancellationToken ct = default) { _templates.Remove(id); return Task.CompletedTask; }
        public Task AddVersionAsync(WhatsAppTemplateVersionDocument version, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<WhatsAppTemplateVersionDocument>> GetVersionsAsync(string tenantId, string templateId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppTemplateVersionDocument>>([]);
        public Task<IReadOnlyList<string>> GetTenantIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([Tenant]);
    }
}
