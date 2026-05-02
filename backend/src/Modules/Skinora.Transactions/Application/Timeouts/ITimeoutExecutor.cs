namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Hangfire job target for the per-transaction payment timeout (09 §13.3).
/// </summary>
/// <remarks>
/// The expression captured by <see cref="ITimeoutSchedulingService.SchedulePaymentTimeoutAsync"/>
/// resolves <c>ITimeoutExecutor</c> from DI when the delay elapses and invokes
/// <see cref="ExecutePaymentTimeoutAsync"/>. The handler enforces the 09 §13.3
/// state-validation no-op pattern before firing the
/// <c>TransactionTrigger.Timeout</c> trigger, so an orphan or stale job cannot
/// cancel a transaction that has already advanced.
/// </remarks>
public interface ITimeoutExecutor
{
    Task ExecutePaymentTimeoutAsync(Guid transactionId);
}

/// <summary>
/// Hangfire job target for the per-transaction warning. The default
/// implementation (T47) only marks the warning as sent; the real notification
/// fan-out is forward-deferred to T48.
/// </summary>
public interface IWarningDispatcher
{
    Task DispatchWarningAsync(Guid transactionId);
}
