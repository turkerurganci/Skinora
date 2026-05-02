namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Orchestrates the <c>POST /transactions</c> happy path (T45 — 07 §7.2,
/// 03 §2.2 "İşlem Başlatma"). Owns the request validation, Steam inventory
/// snapshot, fraud pre-check, FLAGGED-vs-CREATED status decision, outbox
/// publish (<c>TransactionCreatedEvent</c>) and SaveChanges as a single
/// unit of work.
/// </summary>
public interface ITransactionCreationService
{
    Task<CreateTransactionOutcome> CreateAsync(
        Guid sellerId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken);
}
