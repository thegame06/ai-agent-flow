using System.Text;
using AgentFlow.Api.Security;

namespace AgentFlow.Tests.Unit.Security;

public sealed class WebhookSignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");

    [Fact]
    public void Signature_FromTheAppSecret_IsAccepted()
    {
        var header = MetaWebhookSignature.Sign(Body, "app-secret");
        Assert.True(MetaWebhookSignature.IsValid(Body, header, ["other-tenant-secret", "app-secret"]));
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        var header = MetaWebhookSignature.Sign(Body, "app-secret");
        var tampered = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[{"fake":1}]}""");
        Assert.False(MetaWebhookSignature.IsValid(tampered, header, ["app-secret"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=abcdef")]
    [InlineData("sha256=not-hex")]
    public void MissingOrMalformedHeader_IsRejected(string? header)
        => Assert.False(MetaWebhookSignature.IsValid(Body, header, ["app-secret"]));

    [Fact]
    public void WrongSecret_IsRejected()
        => Assert.False(MetaWebhookSignature.IsValid(Body, MetaWebhookSignature.Sign(Body, "someone-else"), ["app-secret"]));

    [Fact]
    public void Verification_WithTheConfiguredToken_EchoesTheChallenge()
        => Assert.Equal("1158201444", MetaWebhookVerification.Check("subscribe", "verify-me", "1158201444", "verify-me"));

    [Theory]
    [InlineData("subscribe", "wrong", "verify-me")]
    [InlineData("unsubscribe", "verify-me", "verify-me")]
    [InlineData(null, "verify-me", "verify-me")]
    [InlineData("subscribe", null, "verify-me")]
    [InlineData("subscribe", "", "")]
    public void Verification_WithoutTheRightModeOrToken_IsRefused(string? mode, string? token, string? expected)
        => Assert.Null(MetaWebhookVerification.Check(mode, token, "123", expected));
}
