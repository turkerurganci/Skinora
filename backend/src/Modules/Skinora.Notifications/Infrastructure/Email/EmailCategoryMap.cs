using Skinora.Shared.Email;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Infrastructure.Email;

/// <summary>
/// Maps each <see cref="NotificationType"/> to its
/// <see cref="EmailCategory"/> — drives the HTML wrapper choice and the
/// Resend <c>category</c> tag (08 §4.2). The map is deliberately
/// exhaustive: any new <see cref="NotificationType"/> that is not added
/// here throws at composition time so we never silently mis-categorise
/// a production email.
/// </summary>
public static class EmailCategoryMap
{
    private static readonly IReadOnlyDictionary<NotificationType, EmailCategory> Map =
        new Dictionary<NotificationType, EmailCategory>
        {
            // --- Transaction lifecycle (08 §4.2 — "İşlem bildirimleri") ---
            [NotificationType.TRANSACTION_INVITE] = EmailCategory.Transaction,
            [NotificationType.BUYER_ACCEPTED] = EmailCategory.Transaction,
            [NotificationType.PAYMENT_WINDOW_OPEN] = EmailCategory.Transaction,
            [NotificationType.PAYMENT_RECEIVED] = EmailCategory.Transaction,
            [NotificationType.DELIVERY_EXPECTED] = EmailCategory.Transaction,
            [NotificationType.TRANSACTION_COMPLETED] = EmailCategory.Transaction,
            [NotificationType.SELLER_PAYMENT_SENT] = EmailCategory.Transaction,
            [NotificationType.TRANSACTION_CANCELLED] = EmailCategory.Transaction,
            [NotificationType.PAYMENT_INCORRECT] = EmailCategory.Transaction,
            [NotificationType.LATE_PAYMENT_REFUNDED] = EmailCategory.Transaction,
            [NotificationType.PAYMENT_REFUNDED] = EmailCategory.Transaction,
            [NotificationType.INSUFFICIENT_PAYMENT] = EmailCategory.Transaction,
            [NotificationType.OVERPAYMENT_REFUNDED] = EmailCategory.Transaction,
            [NotificationType.WRONG_TOKEN_REFUND] = EmailCategory.Transaction,
            // Backlog F7Gate-EventsWithoutConsumer — the seller's reported
            // payout problem was closed. A money-movement outcome on their own
            // sale, so it rides the transaction wrapper next to
            // SELLER_PAYMENT_SENT rather than the security one.
            [NotificationType.PAYOUT_ISSUE_RESOLVED] = EmailCategory.Transaction,

            // --- Timeout warnings (08 §4.2 — "Timeout uyarıları") ---
            [NotificationType.TIMEOUT_WARNING] = EmailCategory.Timeout,

            // --- Security (08 §4.2 — "Güvenlik") ---
            // Fraud-flag and emergency-hold notifications materially change
            // the user's ability to trade, so they belong on the security
            // wrapper alongside the explicit security events that T79+ will
            // dispatch (wallet address changed, login from new device …).
            [NotificationType.TRANSACTION_FLAGGED] = EmailCategory.Security,
            [NotificationType.DISPUTE_RESULT] = EmailCategory.Security,
            [NotificationType.FLAG_RESOLVED] = EmailCategory.Security,
            [NotificationType.EMERGENCY_HOLD_APPLIED] = EmailCategory.Security,
            [NotificationType.EMERGENCY_HOLD_RELEASED] = EmailCategory.Security,
            // T105a — account suspension lifecycle (security/account event).
            [NotificationType.ACCOUNT_SUSPENDED] = EmailCategory.Security,
            [NotificationType.ACCOUNT_UNSUSPENDED] = EmailCategory.Security,

            // --- Admin operational alerts (08 §4.2 — "Hesap", admin tools) ---
            // Admin-targeted notifications use the account wrapper since
            // they're routed to staff mailboxes, not customer-facing trade
            // updates.
            [NotificationType.ADMIN_FLAG_ALERT] = EmailCategory.Account,
            [NotificationType.ADMIN_ESCALATION] = EmailCategory.Account,
            [NotificationType.ADMIN_PAYMENT_FAILURE] = EmailCategory.Account,
            // WP16 — platform health outage alert is an admin operational tool.
            [NotificationType.ADMIN_PLATFORM_OUTAGE] = EmailCategory.Account,
        };

    public static EmailCategory Resolve(NotificationType type)
    {
        if (Map.TryGetValue(type, out var category))
        {
            return category;
        }

        throw new InvalidOperationException(
            $"EmailCategoryMap is missing an entry for NotificationType.{type}. " +
            "Add the type to the map so production email gets the correct wrapper.");
    }

    /// <summary>
    /// Returns the Resend <c>category</c> tag value (used in the
    /// <c>tags</c> field of <c>POST /emails</c>). Lower-cased so it
    /// matches dashboard filters by default.
    /// </summary>
    public static string ResolveTag(NotificationType type)
        => Resolve(type).ToString().ToLowerInvariant();
}
