using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// T169 — which outbound row types the payout/refund launch gate holds
/// (⚠ PARA). Money leaving the platform to a user waits for an admin approval;
/// moves between the platform's own addresses never do.
/// </summary>
public class OutboundApprovalGateTests
{
    [Theory]
    [InlineData(BlockchainTransactionType.SELLER_PAYOUT)]
    [InlineData(BlockchainTransactionType.BUYER_REFUND)]
    [InlineData(BlockchainTransactionType.EXCESS_REFUND)]
    [InlineData(BlockchainTransactionType.WRONG_TOKEN_REFUND)]
    [InlineData(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND)]
    [InlineData(BlockchainTransactionType.LATE_PAYMENT_REFUND)]
    public void Money_To_A_User_Requires_Approval(BlockchainTransactionType type) =>
        Assert.True(OutboundApprovalGate.RequiresApproval(type));

    [Theory]
    [InlineData(BlockchainTransactionType.SWEEP)]
    [InlineData(BlockchainTransactionType.REFUND_FEE_SWEEP)]
    [InlineData(BlockchainTransactionType.BUYER_PAYMENT)]
    [InlineData(BlockchainTransactionType.WRONG_TOKEN_INCOMING)]
    [InlineData(BlockchainTransactionType.SPAM_TOKEN_INCOMING)]
    public void Platform_Internal_And_Inbound_Rows_Never_Wait(BlockchainTransactionType type) =>
        Assert.False(OutboundApprovalGate.RequiresApproval(type));

    [Fact]
    public void Every_Enum_Value_Is_Classified()
    {
        // A new outbound type must be placed on purpose: add it to one of the
        // two theories above (and to ApprovalTypes if it pays a user).
        var classified = new[]
        {
            BlockchainTransactionType.SELLER_PAYOUT, BlockchainTransactionType.BUYER_REFUND,
            BlockchainTransactionType.EXCESS_REFUND, BlockchainTransactionType.WRONG_TOKEN_REFUND,
            BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, BlockchainTransactionType.LATE_PAYMENT_REFUND,
            BlockchainTransactionType.SWEEP, BlockchainTransactionType.REFUND_FEE_SWEEP,
            BlockchainTransactionType.BUYER_PAYMENT, BlockchainTransactionType.WRONG_TOKEN_INCOMING,
            BlockchainTransactionType.SPAM_TOKEN_INCOMING,
        };
        Assert.Equal(
            Enum.GetValues<BlockchainTransactionType>().OrderBy(t => t).ToArray(),
            classified.OrderBy(t => t).ToArray());
    }
}
