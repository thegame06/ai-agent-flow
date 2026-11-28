using AgentFlow.Api.Connect;
using AgentFlow.Api.Controllers;
using AgentFlow.Api.Messaging;
using AgentFlow.Api.Voice;
using AgentFlow.Domain.Aggregates;
using Microsoft.AspNetCore.Http;

namespace AgentFlow.Tests.Unit.Messaging;

public sealed class TwilioWhatsAppTests
{
    private const string Tenant = "tenant-1";

    [Theory]
    [InlineData("whatsapp:+50581143874", "+50581143874")]
    [InlineData("WhatsApp: +14155238886", "+14155238886")]
    [InlineData("+50581143874", "+50581143874")]
    [InlineData("whatsapp:", null)]
    [InlineData(null, null)]
    public void Phone_DropsTheWhatsAppPrefix(string? address, string? expected)
        => Assert.Equal(expected, TwilioWhatsApp.Phone(address));

    [Fact]
    public void IncomingMessage_GoesToTheTwilioChannelOfThatNumber()
    {
        var qr = Channel("QR", new() { ["AuthMode"] = "qr", ["Provider"] = "twilio" });
        var meta = Channel("Meta", new() { ["AuthMode"] = "business", ["PhoneNumberId"] = "111" });
        var sandbox = Channel("Sandbox", new() { ["AuthMode"] = "twilio", ["Provider"] = "twilio" });
        var ventas = Channel("Ventas", new() { ["AuthMode"] = "twilio", ["FromPhoneNumber"] = "+1 (555) 000-2222" });
        var channels = new[] { qr, meta, sandbox, ventas };

        Assert.Same(ventas, TwilioWhatsApp.ChannelFor(channels, "whatsapp:+15550002222"));
        // Another number (the sandbox's) reaches the Twilio channel with no number set.
        Assert.Same(sandbox, TwilioWhatsApp.ChannelFor(channels, "whatsapp:+14155238886"));
    }

    [Fact]
    public void IncomingMessage_NeverReachesAQrOrMetaChannel()
    {
        // The QR channel keeps a legacy Provider=twilio in its config; that does not make it a Twilio channel.
        var qr = Channel("QR", new() { ["AuthMode"] = "qr", ["Provider"] = "twilio" });
        var meta = Channel("Meta", new() { ["AuthMode"] = "business" });
        Assert.Null(TwilioWhatsApp.ChannelFor([qr, meta], "whatsapp:+14155238886"));
    }

    [Fact]
    public void MetaWebhook_NeverRoutesToATwilioChannel()
    {
        var sandbox = Channel("Sandbox", new() { ["AuthMode"] = "twilio", ["Provider"] = "twilio" });
        Assert.Null(WhatsAppWebhookController.ChannelForNumber([sandbox], "999"));
        Assert.Null(WhatsAppWebhookController.ChannelForNumber([sandbox], null));
        Assert.False(WhatsAppChannelMetaService.IsBusiness(sandbox));
    }

    [Fact]
    public void Signature_IsCheckedAgainstThePublicUrlBehindATunnel()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5000);
        context.Request.Path = "/api/v1/tenants/tenant-1/webhooks/whatsapp/twilio";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var urls = TwilioWebhookSignatureValidator.CandidateUrls(context.Request, "https://demo.trycloudflare.com/").ToList();

        Assert.Equal("https://demo.trycloudflare.com/api/v1/tenants/tenant-1/webhooks/whatsapp/twilio", urls[0]);
        Assert.Contains("https://localhost:5000/api/v1/tenants/tenant-1/webhooks/whatsapp/twilio", urls);
        Assert.Equal("http://localhost:5000/api/v1/tenants/tenant-1/webhooks/whatsapp/twilio", urls[^1]);
    }

    [Fact]
    public void Signature_MatchesTwilioDocumentedExample()
    {
        // Example from Twilio's webhook security docs.
        var form = new Dictionary<string, string>
        {
            ["CallSid"] = "CA1234567890ABCDE",
            ["Caller"] = "+12349013030",
            ["Digits"] = "1234",
            ["From"] = "+12349013030",
            ["To"] = "+18005551212",
        };
        var signature = TwilioWebhookSignatureValidator.BuildTwilioSignature(
            "https://mycompany.com/myapp.php?foo=1&bar=2", form, "12345");
        Assert.Equal("0/KCTR6DLpKmkAf8muzZqo1nDgQ=", signature);
    }

    private static ChannelDefinition Channel(string name, Dictionary<string, string> config)
    {
        var channel = ChannelDefinition.Create(Tenant, name, ChannelType.WhatsApp, config);
        channel.Activate();
        return channel;
    }
}
