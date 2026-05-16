using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the T72 amount validation pipeline when a deposit address
/// receives a supported stablecoin that is different from the expected token
/// for the transaction (02 §4.4 "Yanlış token (desteklenen TRC-20)",
/// 08 §3.4 wrong-token table).
/// </summary>
/// <remarks>
/// Refund flow: a <c>WRONG_TOKEN_REFUND</c> BlockchainTransaction row is
/// queued at <c>Status=PENDING</c> with <c>Token</c> set to the
/// <em>expected</em> stablecoin (06 §3.8 token semantiği note) and
/// <c>ActualTokenAddress</c> carrying the wrong contract. T73 sidecar
/// consumer dispatches the actual TRC-20 transfer. Sub-threshold cases emit
/// <see cref="RefundBlockedAdminAlertEvent"/> instead.
///
/// <para>
/// T71 emits <c>WRONG_TOKEN_INCOMING</c> at Status=DETECTED but does not run
/// finality (TronGrid filter returns <c>only_confirmed=true</c> so the
/// inbound side is treated as final-on-detection in the T72 MVP). Multi-event
/// finality probing for wrong-token incomings is forward-deferred — see
/// K-future in <c>Docs/TASK_REPORTS/T72_REPORT.md</c>.
/// </para>
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction the wrong token applies to.</param>
/// <param name="BuyerId">Buyer user id (notification recipient).</param>
/// <param name="RefundTransactionId">
/// Identifier of the <c>WRONG_TOKEN_REFUND</c> BlockchainTransaction row
/// queued for T73 dispatch.
/// </param>
/// <param name="ExpectedStablecoin">Token the transaction was billed for.</param>
/// <param name="ActualStablecoin">Token the buyer actually sent.</param>
/// <param name="ActualContractAddress">Contract address of the wrong token.</param>
/// <param name="ReceivedAmount">Confirmed on-chain amount in the wrong token's base units.</param>
/// <param name="SourceAddress">Refund destination — parsed from the inbound transfer.</param>
/// <param name="TxHash">Inbound transaction hash for cross-reference.</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record WrongTokenRefundRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid BuyerId,
    Guid RefundTransactionId,
    StablecoinType ExpectedStablecoin,
    StablecoinType ActualStablecoin,
    string ActualContractAddress,
    decimal ReceivedAmount,
    string SourceAddress,
    string TxHash,
    DateTime OccurredAt) : IDomainEvent;
