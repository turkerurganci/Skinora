using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
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

    /// <summary>
    /// T147 — a deposit is derived once the buyer has chosen the network, i.e.
    /// from acceptance on (06 §1.4). SELLER_CONFIRMED is included so a
    /// transaction whose inline allocation lost the sidecar round-trip and
    /// whose seller confirmed before the recovery job ran still gets its
    /// address — that is the state in which the buyer is shown it.
    /// </summary>
    internal static readonly TransactionStatus[] EligibleStates =
    [
        TransactionStatus.ACCEPTED,
        TransactionStatus.SELLER_CONFIRMED,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly ILogger<PaymentAddressAllocator> _logger;
    private readonly PaymentAddressIndexFloorOptions _floor;

    /// <param name="floor">
    /// T172a — the per-network index floor (deployment configuration, used
    /// after a database restore — DEPLOY_RUNBOOK §N.8 (b)); none = no floor.
    /// </param>
    public PaymentAddressAllocator(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        ILogger<PaymentAddressAllocator> logger,
        IOptions<PaymentAddressIndexFloorOptions>? floor = null)
    {
        _db = db;
        _services = services;
        _logger = logger;
        _floor = floor?.Value ?? new PaymentAddressIndexFloorOptions();
    }

    /// <summary>
    /// T172a — the index floor this allocator applies on
    /// <paramref name="network"/> (0 = none). The allocation below reads it
    /// through this method; the host test pins that the deployment value
    /// reaches it (T172a doğrulama F1).
    /// </summary>
    public int FloorFor(BlockchainNetwork network) => _floor.FloorFor(network);

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
                $"Transaction is in {transaction.Status} — payment address only allocated for ACCEPTED/SELLER_CONFIRMED.");
        }

        // 06 §1.4 (T147) — the deposit lives on the network the buyer chose at
        // acceptance and the HD index is counted per network (each network has
        // its own seed, 11 §F8 K7). No network, no address: there is no
        // fallback network to derive on.
        if (transaction.Network is not { } network)
        {
            return Failed(
                PaymentAddressAllocationStatus.TransactionIneligible, transactionId,
                "Transaction has no network yet — the deposit is derived on the network chosen at acceptance.");
        }

        // Idempotent re-entry: if a row already exists (any soft-delete state),
        // return it without re-deriving.
        var existing = await _db.Set<PaymentAddress>()
            .IgnoreQueryFilters()
            .Where(p => p.TransactionId == transactionId)
            .Select(p => new { p.Address, p.HdWalletIndex, p.Network })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            // Same-network rule (11 §F8 K3): a deposit on another network than
            // the transaction's would route the payment, its refunds and the
            // sweep through the wrong service. Unreachable by construction (a
            // pre-T147 deposit was derived on TRON and its listing accepts only
            // TRON), so it is reported, never repaired.
            if (existing.Network != network)
            {
                _logger.LogError(
                    "Transaction {TransactionId} is on {Network} but its deposit {Address} is on {DepositNetwork}; no address is returned.",
                    transactionId, network, existing.Address, existing.Network);
                return Failed(
                    PaymentAddressAllocationStatus.NetworkMismatch, transactionId,
                    $"Existing deposit is on {existing.Network}, transaction is on {network}.");
            }

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
            // never be reused (08 §3.2, 06 §3.7 invariant). Per network: the
            // uniqueness key is (Network, HdWalletIndex).
            var maxIndex = await _db.Set<PaymentAddress>()
                .IgnoreQueryFilters()
                .Where(p => p.Network == network)
                .MaxAsync(p => (int?)p.HdWalletIndex, cancellationToken);
            // T172a (⚠ PARA) — never below the network's configured floor, and
            // never below MAX + 1: the floor only raises the index, so it can
            // never hand out an index that was already used.
            var floor = FloorFor(network);
            var nextIndex = PaymentAddressIndexFloorOptions.NextIndex(maxIndex, floor);
            if (nextIndex > (maxIndex ?? -1) + 1)
            {
                _logger.LogWarning(
                    "Deposit index floor applied on {Network}: next index {Index} instead of {Natural} (PaymentAddresses:MinimumNextIndex — DEPLOY_RUNBOOK §N.8).",
                    network, nextIndex, (maxIndex ?? -1) + 1);
            }

            // T148 — derive on the network's own service (its own seed, K7).
            var deriveResult = await _services.Sidecar(network).DeriveAddressAsync(
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
                Network = network,
                Address = BlockchainAddress.Canonicalize(deriveResult.Address!),
                HdWalletIndex = nextIndex,
                ExpectedAmount = transaction.TotalAmount,
                ExpectedToken = transaction.StablecoinType,
                MonitoringStatus = MonitoringStatus.ACTIVE,
                // T158 (h) — the service's finalized head at derive time
                // (Ethereum; null on TRON): the block the deposit is watched from.
                MonitorFromBlock = deriveResult.WatchFromBlock,
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
