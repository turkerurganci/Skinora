using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Default <see cref="IDeliveryDisputeAutoChecker"/>. Decides the 02 §10.1
/// second row + 03 §6.2 flow from the delivery evidence recorded on the
/// transaction (02 §9.2, 06 §2.24):
/// <list type="bullet">
///   <item>
///     Evidence sufficient for delivery → "Item envanterinize teslim edilmiş
///     durumda" (Sonuç B). The dispute closes immediately.
///   </item>
///   <item>
///     Misdelivery signature — the seller's asset is gone but nothing arrived
///     for the buyer → the dispute stays OPEN with a message that names that
///     situation, so the buyer escalates instead of waiting.
///   </item>
///   <item>
///     No evidence at all → the seller has not sent yet (Sonuç A). Stays OPEN.
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// v3.0 — the platform is not a party to the seller→buyer trade, so there is
/// no offer status to read and no <c>TradeOffers</c> table behind this any
/// more. The check is a pure read of already-recorded evidence.
/// </para>
/// <para>
/// It deliberately fails closed: anything short of proven delivery leaves the
/// dispute OPEN so a human decides, rather than an automated answer moving
/// money on a guess. The fuller behaviour — a forced, cache-bypassing
/// verification round and automatic admin escalation on the misdelivery
/// signature — belongs to <b>T130</b>, which rewrites this checker on top of
/// the delivery verification service (T125).
/// </para>
/// </remarks>
public sealed class DeliveryDisputeAutoChecker : IDeliveryDisputeAutoChecker
{
    private const string DeliveredMessage = DisputeAutoCheckMessages.DeliveryDelivered;
    private const string AssetGoneNotArrivedMessage = DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived;
    private const string NotSentMessage = DisputeAutoCheckMessages.DeliveryNotSent;

    // No dependencies: the decision is a pure function of the evidence already
    // on the transaction. T130 reintroduces the inventory port when it adds the
    // forced verification round.
    public Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return Task.FromResult(Check(transaction));
    }

    private static AutoCheckResult Check(Transaction transaction)
    {
        if (transaction.DeliveryEvidence.IsSufficientForDelivery())
        {
            return Resolved(DeliveredMessage);
        }

        // Item left the seller but never reached the buyer — a wrong item or a
        // third-party send. This must never resolve silently, and the message
        // must not imply the buyer has something to do on Steam: there is no
        // offer for them to accept (02 §10.1).
        if (transaction.DeliveryEvidence.IsMisdeliverySignature())
        {
            return Unresolved(AssetGoneNotArrivedMessage);
        }

        return Unresolved(NotSentMessage);
    }

    private static AutoCheckResult Resolved(string messageKey) =>
        new(Resolved: true,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false);

    private static AutoCheckResult Unresolved(string messageKey) =>
        new(Resolved: false,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: true);
}
