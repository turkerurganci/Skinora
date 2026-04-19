using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// Append-only audit trail for every transaction state transition.
/// All fields per 06 §3.6. Immutability is enforced at the
/// <c>AppDbContext</c> level via <see cref="IAppendOnly"/> (06 §4.2).
/// </summary>
public class TransactionHistory : IAppendOnly
{
    public long Id { get; set; }
    public Guid TransactionId { get; set; }
    public TransactionStatus? PreviousStatus { get; set; }
    public TransactionStatus NewStatus { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public ActorType ActorType { get; set; }
    public Guid ActorId { get; set; }
    public string? AdditionalData { get; set; }
    public DateTime CreatedAt { get; set; }

    // --- Navigation ---
    public Transaction Transaction { get; set; } = null!;
}
