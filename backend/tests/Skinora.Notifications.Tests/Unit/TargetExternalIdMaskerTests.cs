using Skinora.Notifications.Application.Channels;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="TargetExternalIdMasker"/> — the shared
/// redaction helper used by the account anonymizer (06 §6.2) and by the
/// stub channel handlers' operational log statements.
/// </summary>
public class TargetExternalIdMaskerTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("user@example.com")]
    [InlineData("a@b")]
    [InlineData("")]
    public void Mask_Email_AlwaysFullyRedacted(string input)
    {
        Assert.Equal("***@***.com", TargetExternalIdMasker.Mask(NotificationChannel.EMAIL, input));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mask_Telegram_PreservesLastFourDigits()
    {
        Assert.Equal("tg:***6789", TargetExternalIdMasker.Mask(NotificationChannel.TELEGRAM, "123456789"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mask_Discord_PreservesLastFourDigits()
    {
        Assert.Equal("dsc:***4321", TargetExternalIdMasker.Mask(NotificationChannel.DISCORD, "987654321"));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(NotificationChannel.TELEGRAM, "12", "tg:***")]
    [InlineData(NotificationChannel.TELEGRAM, "", "tg:***")]
    [InlineData(NotificationChannel.DISCORD, "ab", "dsc:***")]
    public void Mask_ShortInput_FallsBackToPrefixOnly(
        NotificationChannel channel, string input, string expected)
    {
        Assert.Equal(expected, TargetExternalIdMasker.Mask(channel, input));
    }
}
