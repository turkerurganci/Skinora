using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Default <see cref="IPaymentAddressAllocator"/>. Loops on the
/// <c>HdWalletIndex</c>/<c>Address</c> UNIQUE constraints (08 §3.2) so that
/// concurrent allocators that race on the same <c>MAX(index)+1</c> value
/// converge — each retry re-reads the maximum and re-derives.
/// </summary>
public sealed class PaymentAddressAllocator : IPaymentAddressAllocator
{
    /// <summary>
    /// Cap on retry attempts. UNIQUE collisions are rare in steady state
    /// (only when two creations race on the same <c>MAX+1</c>), so 5 is
    /// generous; the cap exists to prevent a runaway loop if the sidecar
    /// starts returning a deterministic duplicate address.
    /// </summary>
    public const int MaxRetryAttempts = 5;

    private static readonly TransactionStatus[] EligibleStates =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly ILogger<PaymentAddressAllocator> _logger;

    public PaymentAddressAllocator(
        AppDbContext db,
        IBlockchainSidecarClient sidecar,
        ILogger<PaymentAddressAllocator> logger)
    {
        _db = db;
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task<PaymentAddressAllocationResult> AllocateAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);

        if (transaction is null)
        {
            return Failed(
                PaymentAddressAllocationStatus.TransactionNotFound, transactionId,
                "Transaction was not found or is soft-deleted.");
        }

        if (!EligibleStates.Contains(transaction.Status))
        {
            return Failed(
                PaymentAddressAllocationStatus.TransactionIneligible, transactionId,
                $"Transaction is in {transaction.Status} — payment address only allocated for CREATED/ACCEPTED.");
        }

        // Idempotent re-entry: if a row already exists (any soft-delete state),
        // return it without re-deriving.
        var existing = await _db.Set<PaymentAddress>()
            .IgnoreQueryFilters()
            .Where(p => p.TransactionId == transactionId)
            .Select(p => new { p.Address, p.HdWalletIndex })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new PaymentAddressAllocationResult(
                PaymentAddressAllocationStatus.AlreadyExisted,
                transactionId,
                existing.Address,
                existing.HdWalletIndex,
                ErrorMessage: null);
        }

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            // Read MAX across soft-deleted rows too — derivation indices must
            // never be reused (08 §3.2, 06 §3.7 invariant).
            var maxIndex = await _db.Set<PaymentAddress>()
                .IgnoreQueryFilters()
                .MaxAsync(p => (int?)p.HdWalletIndex, cancellationToken);
            var nextIndex = (maxIndex ?? -1) + 1;

            var deriveResult = await _sidecar.DeriveAddressAsync(
                nextIndex, transactionId, cancellationToken);

            switch (deriveResult.Status)
            {
                case BlockchainSidecarStatus.NotConfigured:
                    return Failed(
                        PaymentAddressAllocationStatus.SidecarNotConfigured, transactionId,
                        "Blockchain sidecar reports HD wallet not configured.");
                case BlockchainSidecarStatus.Unavailable:
                case BlockchainSidecarStatus.InvalidRequest:
                    return Failed(
                        PaymentAddressAllocationStatus.SidecarUnavailable, transactionId,
                        $"Blockchain sidecar returned {deriveResult.Status}.");
                case BlockchainSidecarStatus.Success:
                    break;
                default:
                    return Failed(
                        PaymentAddressAllocationStatus.SidecarUnavailable, transactionId,
                        $"Blockchain sidecar returned unexpected status {deriveResult.Status}.");
            }

            var paymentAddress = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                Address = deriveResult.Address!,
                HdWalletIndex = nextIndex,
                ExpectedAmount = transaction.TotalAmount,
                ExpectedToken = transaction.StablecoinType,
                MonitoringStatus = MonitoringStatus.ACTIVE,
            };
            _db.Set<PaymentAddress>().Add(paymentAddress);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Allocated payment address {Address} for transaction {TransactionId} at index {Index}",
                    paymentAddress.Address, transactionId, nextIndex);

                return new PaymentAddressAllocationResult(
                    PaymentAddressAllocationStatus.Created,
                    transactionId,
                    paymentAddress.Address,
                    paymentAddress.HdWalletIndex,
                    ErrorMessage: null);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _db.Entry(paymentAddress).State = EntityState.Detached;

                _logger.LogWarning(ex,
                    "UNIQUE collision on attempt {Attempt} for transaction {TransactionId} (index {Index}) — retrying",
                    attempt, transactionId, nextIndex);
            }
        }

        return Failed(
            PaymentAddressAllocationStatus.ExhaustedRetries, transactionId,
            $"Exhausted {MaxRetryAttempts} retry attempts on UNIQUE constraints.");
    }

    private static PaymentAddressAllocationResult Failed(
        PaymentAddressAllocationStatus status, Guid transactionId, string message) =>
        new(status, transactionId, Address: null, HdWalletIndex: null, ErrorMessage: message);

    // T128 — the unique-violation predicate moved to
    // Infrastructure.Persistence.DbConstraintViolations so the 02 §2.3
    // one-open-transaction-per-item gate reads the same rule from the same
    // place. Behaviour unchanged.
    private static bool IsUniqueViolation(DbUpdateException ex)
        => DbConstraintViolations.IsUnique(ex);
}
