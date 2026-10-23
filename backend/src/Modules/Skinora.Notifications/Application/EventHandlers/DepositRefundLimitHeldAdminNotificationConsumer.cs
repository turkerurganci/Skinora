using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// T169a — translates a <see cref="DepositRefundLimitHeldEvent"/> (08 §3.3c)
/// into an <see cref="NotificationType.ADMIN_PAYMENT_FAILURE"/> in-app
/// notification for every admin, the channel the other "a payment is not
/// leaving" alerts use (SELLER_PAYOUT_DEFERRED). The refund waits PENDING; the
/// admin either waits for the 24-hour window or releases it from the wallets
/// screen (AD42).
/// </summary>
/// <remarks>
/// T165 — built with the plain (non money-alarm) constructor on purpose: the
/// operator's Telegram chat and e-mail already get the dedicated
/// <c>deposit-refund-daily-limit-held</c> money alarm (<c>category: money</c>) from
/// <c>skinora_backend_deposit_refund_limit_held_rows</c>, which also says "resolved"
/// when the refund leaves. Passing the money-alarm observer here would announce the
/// same hold a second time as <c>admin-money-alarm-raised</c>.
/// </remarks>
public sealed class DepositRefundLimitHeldAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<DepositRefundLimitHeldEvent>
{
    /// <summary>Error code the admin sees in the ADMIN_PAYMENT_FAILURE body.</summary>
    public const string ErrorCode = "DEPOSIT_REFUND_DAILY_LIMIT";

    public DepositRefundLimitHeldAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<DepositRefundLimitHeldAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.deposit-refund-limit-held-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(DepositRefundLimitHeldEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_PAYMENT_FAILURE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                ["ErrorCode"] = $"{ErrorCode}:{domainEvent.Network}:{domainEvent.Type}",
            },
            TransactionId: domainEvent.TransactionId);
}
