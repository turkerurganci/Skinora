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
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // --- Navigation properties ---
    public Transaction Transaction { get; set; } = null!;
    public PaymentAddress? PaymentAddress { get; set; }
}
