namespace Skinora.Transactions.Domain.StateMachine;

/// <summary>
/// Caller-supplied context for user-initiated cancellation triggers
/// (SellerCancel, BuyerCancel, AdminCancel, SellerDecline).
/// </summary>
public sealed record CancellationContext(string CancelReason);
