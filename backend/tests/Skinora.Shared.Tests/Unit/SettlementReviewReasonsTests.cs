using System.Reflection;
using Skinora.Shared.Events;

namespace Skinora.Shared.Tests.Unit;

/// <summary>
/// T129 second fix round — the rank table that keeps a recorded settlement
/// escalation reason from being lowered (validator finding B4).
/// </summary>
/// <remarks>
/// The payout refusal in <c>SettlementVerificationJob.ClearForPayoutAsync</c> is
/// keyed on the VALUE of <c>Transaction.SettlementEscalationReason</c>, so the
/// value's monotonicity is what actually holds the money: once the ordering here
/// is wrong, the rule silently unwrites itself one re-check later. The
/// job's own behaviour is asserted in <c>SettlementVerificationJobTests</c>; this
/// file pins the ordering it depends on.
/// </remarks>
public class SettlementReviewReasonsTests
{
    private const string Unreadable = SettlementReviewReasons.Unreadable;
    private const string NoReference = SettlementReviewReasons.NoDeliveryReference;
    private const string Ambiguous = SettlementReviewReasons.AmbiguousDeparture;
    private const string Reversal = SettlementReviewReasons.ReversalGated;

    // Nothing observed (0) < item left the buyer (1) < it came back to the
    // seller (2). The two zero-rank codes are equal-strength on purpose: they
    // differ in admin PROCEDURE (DEPLOY_RUNBOOK §I.3 vs §I.5), not in what the
    // platform saw, and neither may overwrite the other once recorded.
    [Theory]
    [InlineData(Unreadable, 0)]
    [InlineData(NoReference, 0)]
    [InlineData(Ambiguous, 1)]
    [InlineData(Reversal, 2)]
    public void Strength_RanksTheReasonCodes(string reason, int expected)
    {
        Assert.Equal(expected, SettlementReviewReasons.Strength(reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SETTLEMENT_SOMETHING_ELSE")]
    public void Strength_RanksAnUnknownOrAbsentReasonBelowEveryKnownOne(string? reason)
    {
        var unknown = SettlementReviewReasons.Strength(reason);

        Assert.True(unknown < SettlementReviewReasons.Strength(Unreadable));
        Assert.True(unknown < SettlementReviewReasons.Strength(NoReference));
        Assert.True(unknown < SettlementReviewReasons.Strength(Ambiguous));
        Assert.True(unknown < SettlementReviewReasons.Strength(Reversal));
    }

    /// <summary>
    /// A code added without a rank would score -1, which makes it silently
    /// unwritable over any existing reason — a new finding lost, in the
    /// direction that releases money. Reflection so the guard cannot be
    /// forgotten alongside the constant.
    /// </summary>
    [Fact]
    public void EveryDeclaredReasonCode_IsRanked()
    {
        var codes = typeof(SettlementReviewReasons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.Equal(4, codes.Count);

        var unranked = codes
            .Where(c => SettlementReviewReasons.Strength(c.Value) < 0)
            .Select(c => c.Name)
            .ToList();

        Assert.Empty(unranked);
    }

    /// <summary>
    /// The two halves of the rule have to agree: <c>ClearForPayout</c> refuses on
    /// <see cref="SettlementReviewReasons.ObservedDeparture"/> while
    /// <c>Escalate</c> protects on <see cref="SettlementReviewReasons.Strength"/>,
    /// so a code that observed a departure but ranked with the non-observing ones
    /// could still be overwritten by one of them and lose its refusal.
    /// </summary>
    [Theory]
    [InlineData(Unreadable)]
    [InlineData(NoReference)]
    [InlineData(Ambiguous)]
    [InlineData(Reversal)]
    public void ObservedDeparture_AndStrength_DrawTheSameLine(string reason)
    {
        Assert.Equal(
            SettlementReviewReasons.ObservedDeparture(reason),
            SettlementReviewReasons.Strength(reason) >= 1);
    }
}
