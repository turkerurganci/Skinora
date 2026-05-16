using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Hangfire recurring job that scans for <c>CREATED</c> / <c>ACCEPTED</c>
/// transactions without a <c>PaymentAddress</c> row and re-runs the
/// <see cref="IPaymentAddressAllocator"/>. Inline allocation in
/// <c>TransactionCreationService</c> is best-effort; this job recovers from
/// transient sidecar outages so a transaction can never be stuck waiting on
/// an address forever.
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
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
    ];

    private readonly AppDbContext _db;
    private readonly IPaymentAddressAllocator _allocator;
    private readonly ILogger<EnsurePaymentAddressJob> _logger;

    public EnsurePaymentAddressJob(
        AppDbContext db,
        IPaymentAddressAllocator allocator,
        ILogger<EnsurePaymentAddressJob> logger)
    {
        _db = db;
        _allocator = allocator;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _db.Set<Transaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted
                && EligibleStates.Contains(t.Status)
                && t.PaymentAddress == null)
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
