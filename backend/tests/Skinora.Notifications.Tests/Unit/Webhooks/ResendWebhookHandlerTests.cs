using Skinora.Notifications.Application.Webhooks;

namespace Skinora.Notifications.Tests.Unit.Webhooks;

public sealed class ResendWebhookHandlerTests
{
    [Theory]
    [InlineData("email.bounced", ResendWebhookEventType.Bounced)]
    [InlineData("email.delivery_delayed", ResendWebhookEventType.DeliveryDelayed)]
    [InlineData("email.complained", ResendWebhookEventType.Complained)]
    [InlineData("email.failed", ResendWebhookEventType.Failed)]
    [InlineData("email.suppressed", ResendWebhookEventType.Suppressed)]
    [InlineData("EMAIL.BOUNCED", ResendWebhookEventType.Bounced)] // case-insensitive
    [InlineData("contact.created", ResendWebhookEventType.Unknown)] // future Resend event
    [InlineData("bounced", ResendWebhookEventType.Bounced)] // tolerates missing prefix
    [InlineData(null, ResendWebhookEventType.Unknown)]
    [InlineData("", ResendWebhookEventType.Unknown)]
    public void ParseEventType_KnownAndUnknown(string? raw, ResendWebhookEventType expected)
    {
        Assert.Equal(expected, ResendWebhookHandler.ParseEventType(raw));
    }

    [Theory]
    [InlineData("user@example.com", "u***@example.com")]
    [InlineData("a@x", "***@x")]
    [InlineData("ab@x", "a***@x")]
    [InlineData(null, "***")]
    [InlineData("", "***")]
    public void MaskEmail_PreservesDomainAndFirstChar(string? input, string expected)
    {
        Assert.Equal(expected, ResendWebhookHandler.MaskEmail(input));
    }
}
