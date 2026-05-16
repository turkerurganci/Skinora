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
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public StablecoinType Token { get; set; }
    public string? ActualTokenAddress { get; set; }
    public decimal? GasFee { get; set; }
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
