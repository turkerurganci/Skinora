using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentMonitoring;

namespace Skinora.Transactions.Tests.Unit.PaymentMonitoring;

/// <summary>
/// T139 — the decision table behind <see cref="EnsurePaymentMonitorJob"/>.
/// Pure input/output, so every branch is asserted without a database or a
/// sidecar; the integration suite then proves the job acts on these verdicts.
/// </summary>
public class EnsurePaymentMonitorJobClassificationTests
{
    [Theory]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    public void Open_Window_Arms(TransactionStatus status)
        => Assert.Equal(
            PaymentMonitorAction.Arm,
            EnsurePaymentMonitorJob.Classify(status, depositSwept: false));

    [Theory]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.REFUNDED)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT)]
    [InlineData(TransactionStatus.CANCELLED_SELLER)]
    [InlineData(TransactionStatus.CANCELLED_BUYER)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN)]
    public void Terminal_Status_Disarms(TransactionStatus status)
        => Assert.Equal(
            PaymentMonitorAction.Disarm,
            EnsurePaymentMonitorJob.Classify(status, depositSwept: false));

    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    [InlineData(TransactionStatus.FLAGGED)]
    public void Unopened_Window_Is_Left_Alone(TransactionStatus status)
        => Assert.Equal(
            PaymentMonitorAction.Idle,
            EnsurePaymentMonitorJob.Classify(status, depositSwept: false));

    [Theory]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    public void Swept_Deposit_Disarms_Even_While_The_Transaction_Is_Live(
        TransactionStatus status)
        => Assert.Equal(
            PaymentMonitorAction.Disarm,
            EnsurePaymentMonitorJob.Classify(status, depositSwept: true));

    // Defensive: a SWEEP row cannot exist this early (SweepQueueJob gates on
    // ITEM_DELIVERED), but the classifier must not stamp STOPPED on a row whose
    // window has not opened — a FLAGGED transaction can still be approved and
    // resume.
    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    [InlineData(TransactionStatus.FLAGGED)]
    public void Sweep_Does_Not_Force_A_Disarm_Before_The_Window_Opened(
        TransactionStatus status)
        => Assert.Equal(
            PaymentMonitorAction.Idle,
            EnsurePaymentMonitorJob.Classify(status, depositSwept: true));

    /// <summary>
    /// The reason this guard exists: the three sets are the job's entire view
    /// of the state machine, and a status added later would silently fall
    /// through <see cref="EnsurePaymentMonitorJob.ActionableStates"/> — the
    /// monitor would never be armed for it (buyer pays into an unwatched
    /// address) or never disarmed (the row stays ACTIVE forever, exactly the
    /// T139 defect). Classification is not optional for any status.
    /// </summary>
    [Fact]
    public void Every_TransactionStatus_Is_Classified_Exactly_Once()
    {
        foreach (var status in Enum.GetValues<TransactionStatus>())
        {
            var matches = 0;
            if (EnsurePaymentMonitorJob.ArmedStates.Contains(status)) matches++;
            if (EnsurePaymentMonitorJob.WindowClosedStates.Contains(status)) matches++;
            if (EnsurePaymentMonitorJob.WindowNotOpenStates.Contains(status)) matches++;

            Assert.True(
                matches == 1,
                $"TransactionStatus.{status} appears in {matches} of the three "
                + "EnsurePaymentMonitorJob sets; it must appear in exactly one.");
        }
    }

    /// <summary>
    /// <c>ActionableStates</c> is what the SQL filter uses. If it ever drifted
    /// from the union of armed + closed, the job would fetch rows it cannot act
    /// on (wasting the batch) or miss rows it must act on.
    /// </summary>
    [Fact]
    public void ActionableStates_Is_Exactly_Armed_Plus_Closed()
    {
        var expected = EnsurePaymentMonitorJob.ArmedStates
            .Concat(EnsurePaymentMonitorJob.WindowClosedStates)
            .OrderBy(s => s)
            .ToArray();
        var actual = EnsurePaymentMonitorJob.ActionableStates.OrderBy(s => s).ToArray();

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(
            EnsurePaymentMonitorJob.ActionableStates,
            s => EnsurePaymentMonitorJob.WindowNotOpenStates.Contains(s));
    }
}
