using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Tests.Unit.StateMachine;

/// <summary>
/// Drift guard for <see cref="TransactionStatusSets"/>.
///
/// <para>
/// The set is not asserted against a second hand-written list — that is exactly
/// the failure mode it exists to end. It is asserted against
/// <see cref="TransactionStateMachine"/> itself: a status is terminal precisely
/// when the machine permits no trigger out of it. Adding a terminal status to
/// the machine without adding it to the set (or the reverse) fails here.
/// </para>
///
/// <para>
/// Why this guard was written: <see cref="TransactionStatus.REFUNDED"/> became
/// terminal with WP5's buyer-favour dispute resolution, and the change reached
/// some copies of the terminal list and missed others. It was found and
/// half-fixed three separate times over four phases — each time by a human
/// reading one call site, never by a test — while three predicates in the fraud
/// module were never spotted at all. Every stale copy carried a comment
/// asserting parity it did not have.
/// </para>
/// </summary>
public sealed class TransactionStatusSetsTests
{
    [Fact]
    public void Terminal_MatchesTheStatusesTheStateMachinePermitsNoTriggerFrom()
    {
        var derivedTerminal = Enum.GetValues<TransactionStatus>()
            .Where(status => !new TransactionStateMachine(Sample(status))
                .PermittedTriggers.Any())
            .OrderBy(s => s)
            .ToArray();

        var declaredTerminal = TransactionStatusSets.Terminal
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(derivedTerminal, declaredTerminal);
    }

    [Fact]
    public void Terminal_ContainsRefunded()
    {
        // Pinned separately from the derived assertion above: REFUNDED is the
        // value whose omission caused the drift, so its presence gets a failure
        // message of its own rather than being folded into a set comparison.
        Assert.Contains(TransactionStatus.REFUNDED, TransactionStatusSets.Terminal);
    }

    [Fact]
    public void Terminal_KeepsFlaggedActive()
    {
        // 07 §9.21 — a FLAGGED transaction is still awaiting an admin decision,
        // so every "active" surface must keep counting it and emergency hold
        // must remain applicable to it.
        Assert.DoesNotContain(TransactionStatus.FLAGGED, TransactionStatusSets.Terminal);
    }

    [Fact]
    public void Cancelled_IsTerminalMinusCompleted()
    {
        var expected = TransactionStatusSets.Terminal
            .Where(s => s != TransactionStatus.COMPLETED)
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(expected, TransactionStatusSets.Cancelled.OrderBy(s => s).ToArray());
    }

    /// <summary>
    /// A transaction carrying enough state for the machine to evaluate its
    /// guards without throwing. The exact field values do not matter to this
    /// test — every non-terminal status has at least one <i>unguarded</i>
    /// transition, so a status reads as terminal only when the machine declares
    /// no transition at all.
    /// </summary>
    private static Transaction Sample(TransactionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        SellerId = Guid.NewGuid(),
        BuyerId = Guid.NewGuid(),
        BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
        TargetBuyerSteamId = "76561198000000099",
        BuyerRefundAddress = "TX9876543210",
        BuyerTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc",
        ItemAssetId = "100001",
        ItemClassId = "200002",
        ItemName = "Test Skin",
        StablecoinType = StablecoinType.USDT,
        Price = 100m,
        CommissionRate = 0.02m,
        CommissionAmount = 2m,
        TotalAmount = 102m,
        SellerPayoutAddress = "TX1234567890",
        PaymentTimeoutMinutes = 30,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}
