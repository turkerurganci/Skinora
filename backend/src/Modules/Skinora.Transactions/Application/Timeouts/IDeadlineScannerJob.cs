namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Self-rescheduling Hangfire job that fires <c>Timeout</c> on every
/// transaction whose phase deadline has elapsed (05 §4.4 "Aşama ayrımı":
/// AcceptDeadline / SellerConfirmDeadline / DeliveryDeadline are
/// scanner-driven; PaymentDeadline is per-tx Hangfire delayed job + scanned as
/// a belt-and-suspenders fallback per 05 §4.4 + 09 §13.3 atomicity note).
/// Sub-minute polling uses the self-rescheduling delayed-job pattern (09 §13.4)
/// because Hangfire 5-field cron has 1-minute granularity at best.
/// <para>
/// T124 exception: the delivery phase is observed but not cancelled. 05 §4.4
/// requires a delivery verification round before the delivery timeout may
/// cancel, and that round lands in T127.
/// </para>
/// </summary>
public interface IDeadlineScannerJob
{
    Task ScanAndRescheduleAsync();
}
