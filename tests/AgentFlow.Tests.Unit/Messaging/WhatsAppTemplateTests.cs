using System.Text.Json;
using System.Text.Json.Nodes;
using AgentFlow.Api.Connect;
using AgentFlow.Api.Messaging;
using AgentFlow.Infrastructure.Channels.WhatsApp;

namespace AgentFlow.Tests.Unit.Messaging;

public sealed class WhatsAppTemplateTests
{
    private const string Tenant = "tenant-1";

    // A template as Meta's list endpoint returns it.
    private const string MetaReminder = """
    {
      "id": "1001", "name": "recordatorio_pago", "language": "es", "status": "APPROVED", "category": "UTILITY",
      "parameter_format": "POSITIONAL", "quality_score": { "score": "GREEN" },
      "components": [
        { "type": "HEADER", "format": "TEXT", "text": "Hola {{1}}", "example": { "header_text": ["Ana"] } },
        { "type": "BODY", "text": "Tu saldo vencido es {{1}} {{2}}. Responde a este mensaje para ponerte al corriente.",
          "example": { "body_text": [["NIO", "1,350.00"]] } },
        { "type": "FOOTER", "text": "Financiera Demo" },
        { "type": "BUTTONS", "buttons": [
            { "type": "QUICK_REPLY", "text": "Ya pague" },
            { "type": "URL", "text": "Ver saldo", "url": "https://pagos.demo/{{1}}", "example": ["https://pagos.demo/PR-1"] } ] }
      ]
    }
    """;

    [Fact]
    public void MetaTemplate_IsReadWithItsSlotsAndSampleValues()
    {
        using var doc = JsonDocument.Parse(MetaReminder);
        var components = WhatsAppTemplateText.FromMetaComponents(doc.RootElement.GetProperty("components"));

        Assert.Equal(["HEADER", "BODY", "FOOTER", "BUTTONS"], components.Select(c => c.Type));
        Assert.Equal(
            [("header", "1"), ("body", "1"), ("body", "2"), ("button:1", "1")],
            WhatsAppTemplateText.Slots(components));

        var examples = WhatsAppTemplateText.ExamplesFromMeta(doc.RootElement.GetProperty("components"));
        Assert.Equal("Ana", examples[("header", "1")]);
        Assert.Equal("1,350.00", examples[("body", "2")]);
    }

    [Fact]
    public void Build_FillsEachSlotFromTheContact_AndGivesMetaPayloadAndText()
    {
        var template = Reminder();
        var message = WhatsAppTemplateText.Build(template, new Dictionary<string, string>
        {
            ["primer_nombre"] = "Ana",
            ["moneda"] = "NIO",
            ["total_vencido"] = "1,350.00",
            ["prestamos"] = "PRUEBA-WA-L1"
        });

        Assert.Empty(message.Missing);
        Assert.StartsWith("*Hola Ana*", message.Text);
        Assert.Contains("Tu saldo vencido es NIO 1,350.00.", message.Text);
        Assert.Contains("[Ver saldo]", message.Text);

        var payload = JsonNode.Parse(message.ToSendJson())!;
        Assert.Equal("recordatorio_pago", payload["name"]!.GetValue<string>());
        Assert.Equal("es", payload["language"]!["code"]!.GetValue<string>());
        var components = payload["components"]!.AsArray();
        Assert.Equal("Ana", components[0]!["parameters"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(["NIO", "1,350.00"], components[1]!["parameters"]!.AsArray().Select(p => p!["text"]!.GetValue<string>()));
        Assert.Null(components[1]!["parameters"]![0]!["parameter_name"]); // positional
        Assert.Equal("url", components[2]!["sub_type"]!.GetValue<string>());
        Assert.Equal("1", components[2]!["index"]!.GetValue<string>());
        Assert.Equal("PRUEBA-WA-L1", components[2]!["parameters"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Build_ReportsWhatTheContactIsMissing_SoNothingGoesOutEmpty()
    {
        var message = WhatsAppTemplateText.Build(Reminder(), new Dictionary<string, string> { ["primer_nombre"] = "Ana" });
        Assert.Equal(["moneda", "total_vencido", "prestamos"], message.Missing);
    }

    [Fact]
    public void NamedTemplates_SendTheParameterName()
    {
        var template = new WhatsAppTemplateDocument
        {
            Name = "aviso",
            ParameterFormat = "NAMED",
            Components = [new() { Type = "BODY", Text = "Hola {{primer_nombre}}, debes {{total_vencido}}." }]
        };
        var message = WhatsAppTemplateText.Build(template, new Dictionary<string, string> { ["primer_nombre"] = "Ana", ["total_vencido"] = "500" });

        var param = JsonNode.Parse(message.ToSendJson())!["components"]![0]!["parameters"]![0]!;
        Assert.Equal("primer_nombre", param["parameter_name"]!.GetValue<string>());
        Assert.Equal("Hola Ana, debes 500.", message.Text);
    }

    [Fact]
    public void Editor_RenumbersFieldsInOrderOfAppearance_PerComponent()
    {
        var (components, parameters) = WhatsAppTemplateText.ToPositional(
        [
            new() { Type = "HEADER", Format = "TEXT", Text = "Hola {{primer_nombre}}" },
            new() { Type = "BODY", Text = "{{primer_nombre}}, debes {{moneda}} {{total_vencido}}. Gracias {{primer_nombre}}. Asesor: {{asesor}}" },
        ]);

        Assert.Equal("Hola {{1}}", components[0].Text);
        Assert.Equal("{{1}}, debes {{2}} {{3}}. Gracias {{1}}. Asesor: {{4}}", components[1].Text);
        Assert.Equal(["primer_nombre", "primer_nombre", "moneda", "total_vencido", null],
            parameters.Select(p => p.Field));
        var free = parameters.Last();
        Assert.Equal(("body", "4", "static", "asesor"), (free.Component, free.Key, free.Source, free.Example));
    }

    [Fact]
    public void MetaReview_GetsAnExampleForEveryPlaceholder()
    {
        var template = Reminder();
        var components = WhatsAppTemplateText.ToMetaComponents(template.Components, template.Parameters, "POSITIONAL");

        Assert.Equal("Ana", components[0]!["example"]!["header_text"]![0]!.GetValue<string>());
        Assert.Equal(["NIO", "1,350.00"], components[1]!["example"]!["body_text"]![0]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal("https://pagos.demo/PR-1001", components[3]!["buttons"]![1]!["example"]![0]!.GetValue<string>());
        Assert.Null(components[2]!["example"]); // footer has no variables
    }

    [Fact]
    public void Hash_ChangesWithContent_NotWithStatus()
    {
        var a = Reminder();
        var hash = WhatsAppTemplateText.Hash(a.Name, a.Language, a.Category, a.Components);
        a.Status = WhatsAppTemplateStatuses.Paused;
        Assert.Equal(hash, WhatsAppTemplateText.Hash(a.Name, a.Language, a.Category, a.Components));
        a.Components[1].Text += " Gracias.";
        Assert.NotEqual(hash, WhatsAppTemplateText.Hash(a.Name, a.Language, a.Category, a.Components));
    }

    [Fact]
    public void OutgoingMessage_CarriesTheTemplateForTheChannel()
    {
        var message = WhatsAppTemplateText.Build(Reminder(), new Dictionary<string, string>
        {
            ["primer_nombre"] = "Ana", ["moneda"] = "NIO", ["total_vencido"] = "1", ["prestamos"] = "L1"
        });
        var payload = WhatsAppTemplatePayload.From(new Dictionary<string, string> { [WhatsAppTemplatePayload.MetadataKey] = message.ToSendJson() });

        Assert.NotNull(payload);
        Assert.Equal(("recordatorio_pago", "es"), (payload!.Name, payload.Language));
        Assert.Equal(3, JsonDocument.Parse(payload.ComponentsJson!).RootElement.GetArrayLength());
        Assert.Null(WhatsAppTemplatePayload.From(new Dictionary<string, string>()));
    }

    // ------------------------------------------------------------------
    // Sync, create, edit, webhook

    [Fact]
    public async Task Sync_CreatesThenKeepsMapping_AndVersionsOnlyContentChanges()
    {
        var (service, store, meta) = Create();
        meta.Templates.Add(Parse(MetaReminder));

        var first = await service.SyncAsync(Tenant, null, "u");
        Assert.Equal(1, first.Created);
        var template = Assert.Single(await store.ListAsync(Tenant));
        Assert.Equal((1, "APPROVED", "GREEN"), (template.Version, template.Status, template.QualityScore));
        Assert.Equal("Ana", template.Parameters.Single(p => p.Component == "header").Example); // Meta's sample

        // A person maps the placeholders.
        await service.UpdateParametersAsync(Tenant, template.Id,
        [
            new() { Component = "header", Key = "1", Source = "field", Field = "primer_nombre" },
            new() { Component = "body", Key = "1", Source = "field", Field = "moneda" },
        ], "u");

        // Same content: nothing new. Status change only: no new version.
        Assert.Equal(1, (await service.SyncAsync(Tenant, null, "u")).Unchanged);
        meta.Templates[0] = meta.Templates[0] with { Status = "PAUSED" };
        Assert.Equal(1, (await service.SyncAsync(Tenant, null, "u")).StatusChanged);
        template = (await store.GetAsync(Tenant, template.Id))!;
        Assert.Equal((1, "PAUSED"), (template.Version, template.Status));

        // Edited in Meta: version 2, mapping kept.
        meta.Templates[0] = Parse(MetaReminder.Replace("Tu saldo vencido", "Tu saldo pendiente"));
        Assert.Equal(1, (await service.SyncAsync(Tenant, null, "u")).Updated);
        template = (await store.GetAsync(Tenant, template.Id))!;
        Assert.Equal(2, template.Version);
        Assert.Equal("primer_nombre", template.Parameters.Single(p => p.Component == "header").Field);
        Assert.Equal(2, (await store.GetVersionsAsync(Tenant, template.Id)).Count);

        // Deleted in Meta: kept here, marked deleted.
        meta.Templates.Clear();
        Assert.Equal(1, (await service.SyncAsync(Tenant, null, "u")).Removed);
        Assert.Equal(WhatsAppTemplateStatuses.Deleted, (await store.GetAsync(Tenant, template.Id))!.Status);
    }

    [Fact]
    public async Task Sync_WithoutAMetaConnection_SaysSo()
    {
        var (service, _, _) = Create(withAccount: false);
        var result = await service.SyncAsync(Tenant, null, "u");
        Assert.Contains("conexion de Meta", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task Create_SendsMetaThePositionalTemplateWithExamples()
    {
        var (service, store, meta) = Create();
        var result = await service.CreateAsync(Tenant, new TemplateDraft
        {
            Name = "aviso_saldo",
            Category = "UTILITY",
            HeaderText = "Hola {{primer_nombre}}",
            Body = "Tu saldo vencido es {{moneda}} {{total_vencido}}. Responde para ayudarte.",
            Footer = "Financiera Demo",
            Buttons = [new() { Type = "QUICK_REPLY", Text = "Quiero pagar" }]
        }, "u");

        Assert.True(result.Ok, result.Error);
        var sent = Assert.Single(meta.Created);
        Assert.Equal("POSITIONAL", sent["parameter_format"]!.GetValue<string>());
        Assert.Equal("Tu saldo vencido es {{1}} {{2}}. Responde para ayudarte.", sent["components"]![1]!["text"]!.GetValue<string>());
        Assert.Equal(["NIO", "1,350.00"], sent["components"]![1]!["example"]!["body_text"]![0]!.AsArray().Select(v => v!.GetValue<string>()));

        var saved = Assert.Single(await store.ListAsync(Tenant));
        Assert.Equal(("PENDING", "agentflow", "meta-new-1"), (saved.Status, saved.Origin, saved.MetaTemplateId));
        Assert.Equal(["primer_nombre", "moneda", "total_vencido"], saved.Parameters.Select(p => p.Field));
    }

    [Theory]
    [InlineData("Aviso Saldo", "Tu saldo es {{total_vencido}}.", "minusculas")]
    [InlineData("aviso", "{{primer_nombre}}, paga hoy", "empiece o termine")]
    [InlineData("aviso", "", "obligatorio")]
    public void Create_ChecksMetasRulesFirst(string name, string body, string expected)
        => Assert.Contains(expected, WhatsAppTemplateService.Validate(new TemplateDraft { Name = name, Body = body }));

    [Fact]
    public async Task Edit_SendsNewContent_AndGoesBackToReview()
    {
        var (service, store, meta) = Create();
        meta.Templates.Add(Parse(MetaReminder));
        await service.SyncAsync(Tenant, null, "u");
        var template = Assert.Single(await store.ListAsync(Tenant));

        var result = await service.EditAsync(Tenant, template.Id, new TemplateDraft
        {
            Category = "UTILITY",
            HeaderText = "Hola {{primer_nombre}}",
            Body = "Te recordamos tu saldo de {{moneda}} {{total_vencido}}. Responde aqui."
        }, "u");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("1001", Assert.Single(meta.Edited).Id);
        template = (await store.GetAsync(Tenant, template.Id))!;
        Assert.Equal((2, "PENDING"), (template.Version, template.Status));
    }

    [Fact]
    public async Task Webhook_UpdatesStatusAndReason()
    {
        var (service, store, meta) = Create();
        meta.Templates.Add(Parse(MetaReminder.Replace("\"APPROVED\"", "\"PENDING\"")));
        await service.SyncAsync(Tenant, null, "u");

        Assert.True(await service.ApplyWebhookAsync(Tenant, "1001", "REJECTED", "INVALID_FORMAT", null, null));
        var template = Assert.Single(await store.ListAsync(Tenant));
        Assert.Equal(("REJECTED", "INVALID_FORMAT"), (template.Status, template.RejectedReason));
        Assert.False(await service.ApplyWebhookAsync(Tenant, "9999", "APPROVED", null, null, null));
    }

    // ------------------------------------------------------------------

    private static WhatsAppTemplateDocument Reminder()
    {
        using var doc = JsonDocument.Parse(MetaReminder);
        var components = WhatsAppTemplateText.FromMetaComponents(doc.RootElement.GetProperty("components"));
        return new WhatsAppTemplateDocument
        {
            Name = "recordatorio_pago",
            Language = "es",
            Category = "UTILITY",
            Status = WhatsAppTemplateStatuses.Approved,
            Components = components,
            Parameters =
            [
                new() { Component = "header", Key = "1", Source = "field", Field = "primer_nombre", Example = "Ana" },
                new() { Component = "body", Key = "1", Source = "field", Field = "moneda", Example = "NIO" },
                new() { Component = "body", Key = "2", Source = "field", Field = "total_vencido", Example = "1,350.00" },
                new() { Component = "button:1", Key = "1", Source = "field", Field = "prestamos", Example = "PR-1001" },
            ]
        };
    }

    private static MetaTemplate Parse(string json)
    {
        var t = JsonDocument.Parse(json).RootElement;
        return new MetaTemplate(
            t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!, t.GetProperty("language").GetString()!,
            t.GetProperty("status").GetString()!, t.GetProperty("category").GetString()!, t.GetProperty("parameter_format").GetString(),
            t.GetProperty("quality_score").GetProperty("score").GetString(), null, t.GetProperty("components").Clone());
    }

    private static (WhatsAppTemplateService, InMemoryTemplateStore, FakeMeta) Create(bool withAccount = true)
    {
        var store = new InMemoryTemplateStore();
        var meta = new FakeMeta();
        var accounts = new FakeAccounts(withAccount
            ? [new MetaWhatsAppAccount { ConnectionId = "c1", Name = "Meta", BusinessAccountId = "waba-1", AccessToken = "t" }]
            : []);
        return (new WhatsAppTemplateService(store, accounts, meta), store, meta);
    }

    private sealed class FakeMeta : IMetaTemplateApi
    {
        public List<MetaTemplate> Templates { get; } = [];
        public List<JsonObject> Created { get; } = [];
        public List<(string Id, JsonObject Body)> Edited { get; } = [];

        public Task<IReadOnlyList<MetaTemplate>> ListAsync(MetaWhatsAppAccount account, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetaTemplate>>(Templates.ToList());
        public Task<(string Id, string Status, string? Category)> CreateAsync(MetaWhatsAppAccount account, JsonObject request, CancellationToken ct = default)
        {
            Created.Add(request);
            return Task.FromResult(($"meta-new-{Created.Count}", "PENDING", (string?)null));
        }
        public Task EditAsync(MetaWhatsAppAccount account, string metaTemplateId, JsonObject request, CancellationToken ct = default)
        {
            Edited.Add((metaTemplateId, request));
            return Task.CompletedTask;
        }
        public Task DeleteAsync(MetaWhatsAppAccount account, string name, string? metaTemplateId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAccounts(IReadOnlyList<MetaWhatsAppAccount> accounts) : IMetaWhatsAppAccounts
    {
        public Task<IReadOnlyList<MetaWhatsAppAccount>> GetAllAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(accounts);
        public Task<MetaWhatsAppAccount?> GetAsync(string tenantId, string connectionId, CancellationToken ct = default)
            => Task.FromResult(accounts.FirstOrDefault(a => a.ConnectionId == connectionId));
    }

    private sealed class InMemoryTemplateStore : IWhatsAppTemplateStore
    {
        private readonly Dictionary<string, WhatsAppTemplateDocument> _templates = [];
        private readonly List<WhatsAppTemplateVersionDocument> _versions = [];

        // Round-trip through JSON so the service never shares an instance with the "database".
        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

        public Task<IReadOnlyList<WhatsAppTemplateDocument>> ListAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppTemplateDocument>>(_templates.Values.Where(t => t.TenantId == tenantId).Select(Copy).ToList());
        public Task<WhatsAppTemplateDocument?> GetAsync(string tenantId, string id, CancellationToken ct = default)
            => Task.FromResult(_templates.TryGetValue(id, out var t) && t.TenantId == tenantId ? Copy(t) : null);
        public Task<WhatsAppTemplateDocument?> FindByMetaIdAsync(string tenantId, string metaTemplateId, CancellationToken ct = default)
            => Task.FromResult(_templates.Values.Where(t => t.TenantId == tenantId && t.MetaTemplateId == metaTemplateId).Select(Copy).FirstOrDefault());
        public Task SaveAsync(WhatsAppTemplateDocument template, CancellationToken ct = default) { _templates[template.Id] = Copy(template); return Task.CompletedTask; }
        public Task DeleteAsync(string tenantId, string id, CancellationToken ct = default) { _templates.Remove(id); return Task.CompletedTask; }
        public Task AddVersionAsync(WhatsAppTemplateVersionDocument version, CancellationToken ct = default) { _versions.Add(version); return Task.CompletedTask; }
        public Task<IReadOnlyList<WhatsAppTemplateVersionDocument>> GetVersionsAsync(string tenantId, string templateId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhatsAppTemplateVersionDocument>>(_versions.Where(v => v.TemplateId == templateId).OrderByDescending(v => v.Version).ToList());
        public Task<IReadOnlyList<string>> GetTenantIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_templates.Values.Select(t => t.TenantId).Distinct().ToList());
    }
}
