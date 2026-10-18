using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Hangfire recurring job that scans for accepted transactions (<c>ACCEPTED</c>
/// / <c>SELLER_CONFIRMED</c>, network chosen) without a <c>PaymentAddress</c>
/// row and re-runs the <see cref="IPaymentAddressAllocator"/>. Inline
/// allocation in <c>TransactionAcceptanceService</c> is best-effort (T147 —
/// it moved there from creation with the network choice); this job recovers
/// from transient sidecar outages so a transaction can never be stuck waiting
/// on an address forever.
/// </summary>
public sealed class EnsurePaymentAddressJob
{
    public const string RecurringJobId = "ensure-payment-address";

    /// <summary>
    /// Cron expression: every minute. The sidecar derive call is cheap
    /// (in-process crypto), so a per-minute scan does not stress the sidecar
    /// even at peak transaction volume.
    /// </summary>
    public const string Cron = "* * * * *";

    /// <summary>
    /// Maximum transactions processed per run. Keeps the job bounded so a
    /// large backlog cannot block other recurring jobs on a shared worker.
    /// </summary>
    public const int BatchSize = 50;

    private static readonly TransactionStatus[] EligibleStates =
        PaymentAddressAllocator.EligibleStates;

    private readonly AppDbContext _db;
    private readonly IPaymentAddressAllocator _allocator;
    private readonly IBlockchainServiceRegistry _services;
    private readonly ILogger<EnsurePaymentAddressJob> _logger;

    public EnsurePaymentAddressJob(
        AppDbContext db,
        IPaymentAddressAllocator allocator,
        IBlockchainServiceRegistry services,
        ILogger<EnsurePaymentAddressJob> logger)
    {
        _db = db;
        _allocator = allocator;
        _services = services;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // T148 düzeltme turu 1 (F1) — only transactions on a network whose
        // service is configured are candidates. Derivation on an unconfigured
        // network fails closed every time and leaves the transaction without an
        // address, so selecting it would keep it at the head of the
        // oldest-first batch forever: BatchSize of them would starve every
        // other network's allocations. T147 — only an accepted transaction
        // with a chosen network is a candidate: the allocator never invents a
        // default network (11 §F8 T147 kararı xiii), and every accepted row
        // carries one (CK_Transactions_Network_Accepted).
        var configured = _services.ConfiguredNetworks.ToList();
        if (configured.Count == 0) return;

        var pending = await _db.Set<Transaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted
                && EligibleStates.Contains(t.Status)
                && t.PaymentAddress == null
                && t.Network != null
                && configured.Contains(t.Network.Value))
            .OrderBy(t => t.CreatedAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return;

        _logger.LogInformation(
            "EnsurePaymentAddressJob found {Count} transactions awaiting address allocation",
            pending.Count);

        var succeeded = 0;
        var failed = 0;
        foreach (var transactionId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _allocator.AllocateAsync(transactionId, cancellationToken);
            switch (result.Status)
            {
                case PaymentAddressAllocationStatus.Created:
                case PaymentAddressAllocationStatus.AlreadyExisted:
                    succeeded++;
                    break;
                default:
                    failed++;
                    _logger.LogWarning(
                        "EnsurePaymentAddressJob failed to allocate for transaction {TransactionId}: {Status} — {Message}",
                        transactionId, result.Status, result.ErrorMessage);
                    break;
            }
        }

        _logger.LogInformation(
            "EnsurePaymentAddressJob processed {Succeeded} successes, {Failed} failures",
            succeeded, failed);
    }

    // Hangfire serializes Expression<Action<T>> so the entry-point exposes a
    // synchronous wrapper. The job body itself runs async on the worker.
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
