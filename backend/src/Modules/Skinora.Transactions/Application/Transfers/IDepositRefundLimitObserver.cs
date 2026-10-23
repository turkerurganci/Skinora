using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T169a — per-network visibility of the deposit refund daily limit
/// (<see cref="DepositRefundDailyLimit"/>). The dispatcher reports every run
/// for every network; the API publishes it as Prometheus gauges and the
/// <c>deposit-refund-daily-limit-held</c> rule alarms while a refund waits.
/// </summary>
public interface IDepositRefundLimitObserver
{
    /// <param name="network">The network the numbers belong to.</param>
    /// <param name="heldRows">Refunds the limit is holding right now.</param>
    /// <param name="usedUsdt">What deposit refunds sent in the last 24 hours.</param>
    /// <param name="limitUsdt">The network's limit (<c>blockchain.*.deposit_refund_daily_limit_usdt</c>).</param>
    void Report(BlockchainNetwork network, int heldRows, decimal usedUsdt, decimal limitUsdt);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullDepositRefundLimitObserver : IDepositRefundLimitObserver
{
    public static readonly NullDepositRefundLimitObserver Instance = new();

    public void Report(BlockchainNetwork network, int heldRows, decimal usedUsdt, decimal limitUsdt)
    {
    }
}
