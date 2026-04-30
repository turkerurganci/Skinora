using Skinora.Shared.Enums;

namespace Skinora.Notifications.Application.Channels;

/// <summary>
/// Channel-aware redaction for <c>TargetExternalId</c> values (email
/// addresses / Telegram chat IDs / Discord user IDs). Used both for the
/// 06 §6.2 at-rest anonymization on account deletion and for operational
/// log statements that should never carry full PII.
/// </summary>
/// <remarks>
/// Format: <c>***@***.com</c> for email, <c>tg:***{last4}</c> for Telegram,
/// <c>dsc:***{last4}</c> for Discord. Inputs shorter than four characters
/// degrade to <c>tg:***</c> / <c>dsc:***</c> so we never leak a near-full
/// short identifier.
/// </remarks>
public static class TargetExternalIdMasker
{
    public static string Mask(NotificationChannel channel, string targetExternalId)
        => channel switch
        {
            NotificationChannel.EMAIL => "***@***.com",
            NotificationChannel.TELEGRAM => MaskWithTail(targetExternalId, "tg"),
            NotificationChannel.DISCORD => MaskWithTail(targetExternalId, "dsc"),
            _ => "***",
        };

    private static string MaskWithTail(string value, string prefix)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 4)
            return $"{prefix}:***";
        return $"{prefix}:***{value[^4..]}";
    }
}
