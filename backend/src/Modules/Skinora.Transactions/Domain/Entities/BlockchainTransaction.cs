using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// Record of all blockchain transfers — incoming payments, refunds, and seller payouts.
/// All fields per 06 §3.8.
/// </summary>
public class BlockchainTransaction
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? PaymentAddressId { get; set; }
    public BlockchainTransactionType Type { get; set; }
    public string? TxHash { get; set; }

    /// <summary>
    /// On-chain log index of the TRC-20 Transfer event within <see cref="TxHash"/>
    /// (08 §3.4 — WP10 event-index dedup). For inbound monitored rows
    /// (BUYER_PAYMENT / WRONG_TOKEN_INCOMING / SPAM_TOKEN_INCOMING) this is the
    /// sidecar-resolved event index — together with <see cref="TxHash"/> it forms
    /// the per-event uniqueness key, so a single transaction carrying several
    /// transfers to the deposit address is credited per event. The common
    /// single-transfer payment reports 0. Outbound rows (refunds / payouts /
    /// sweep) leave it NULL — they have no inbound event and each carries a
    /// distinct broadcast TxHash.
    /// </summary>
    public int? EventIndex { get; set; }

    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public StablecoinType Token { get; set; }
    public string? ActualTokenAddress { get; set; }
    public decimal? GasFee { get; set; }

    /// <summary>
    /// TRX actually burned by this transfer, in SUN, read from the chain
    /// receipt at confirmation time. NULL until confirmed, and legitimately
    /// <c>0</c> when the transfer cost the sender nothing.
    /// </summary>
    /// <remarks>
    /// Recorded for measurement, never for charging: <see cref="GasFee"/> was
    /// fixed when the row was queued and is not revisited — collecting the
    /// difference afterwards would need a second transfer costing more than
    /// the difference itself. What this column buys is the ability to ask how
    /// close the pre-send estimate actually lands, which until now was assumed
    /// rather than known. Read together with <see cref="OriginEnergyUsage"/>:
    /// a zero fee alongside a non-zero owner-paid energy means the CONTRACT
    /// absorbed the cost, not that the transfer was free to arrange.
    /// </remarks>
    public long? RealizedFeeSun { get; set; }

    /// <summary>Total Energy the call consumed, however it was paid for.</summary>
    public long? EnergyUsageTotal { get; set; }

    /// <summary>Energy the contract owner absorbed on the sender's behalf.</summary>
    public long? OriginEnergyUsage { get; set; }

    public BlockchainTransactionStatus Status { get; set; }
    public long? BlockNumber { get; set; }
    public int ConfirmationCount { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// Earliest UTC instant at which the outgoing-transfer dispatcher will
    /// pick this row up again (T73). NULL means "eligible immediately" — set
    /// to <c>now + retryInterval[RetryCount]</c> after a transient failure.
    /// Inbound rows (BUYER_PAYMENT / WRONG_TOKEN_INCOMING / SPAM_TOKEN_INCOMING)
    /// leave this NULL; the dispatcher's WHERE clause filters them out by
    /// <c>Type</c> regardless.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // --- Navigation properties ---
    public Transaction Transaction { get; set; } = null!;
    public PaymentAddress? PaymentAddress { get; set; }
}
