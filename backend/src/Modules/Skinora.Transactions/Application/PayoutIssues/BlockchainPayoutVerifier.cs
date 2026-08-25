using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// Production <see cref="IPayoutVerifier"/> — closes backlog
/// <c>StubPayoutVerifier</c>, the T60 K1 forward-deferral that shipped a
/// fail-closed stub while the Tron sidecar (T64–T69) was still outstanding.
/// The sidecar has since landed, so a seller who reports "my payout never
/// arrived" no longer has to wait for an admin when the chain can answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>No new external dependency.</b> Both sources of truth already exist and
/// are already maintained: the platform's own <see cref="BlockchainTransaction"/>
/// <c>SELLER_PAYOUT</c> row, and the sidecar status lookup
/// (<see cref="IBlockchainTransferClient.GetStatusAsync"/>) that
/// <c>OutgoingTransferConfirmationJob</c> polls every minute. This verifier
/// reads them; it broadcasts nothing and mutates nothing —
/// <see cref="PayoutIssueService"/> owns every state transition (07 §7.11).
/// </para>
/// <para>
/// <b>Only a confirmed chain record resolves automatically</b> (owner decision
/// 2026-08-25). Everything else still reaches an operator, so the fail-closed
/// direction the stub established is preserved where it matters:
/// </para>
/// <list type="bullet">
///   <item>Local row CONFIRMED with a hash → <c>Confirmed</c>. The row only
///   reaches CONFIRMED after the sidecar reported ≥20 blocks (05 §3.3), so it
///   is chain evidence, not a local guess — and re-asking the chain for a
///   settled record would add a network hop that can only turn a good answer
///   into <c>UnableToVerify</c>.</item>
///   <item>Local row FAILED, or no payout row at all on a COMPLETED
///   transaction → <c>AnomalyDetected</c>. A completed sale with no payout
///   record is a real inconsistency and is exactly the case a human should
///   see; the seller's complaint is then correct, not mistaken.</item>
///   <item>Row exists but was never broadcast (<c>TxHash</c> null) →
///   <c>StillPending</c>: the dispatcher has not picked it up yet, which the
///   retry path handles.</item>
///   <item>Row DETECTED/PENDING with a hash → ask the sidecar. Confirmed /
///   Failed / Pending map straight through; an unreachable sidecar is
///   <c>UnableToVerify</c> and escalates, as before.</item>
///   <item>Recorded hash disagrees with <paramref name="expectedPayoutTxHash"/>
///   → <c>AnomalyDetected</c>. The caller reads the hash off the same row
///   today, so a mismatch means something rewrote history between the two
///   reads and no automatic answer should be trusted.</item>
/// </list>
/// <para>
/// Messages are user-facing (they land in the 07 §7.11 response body and, on
/// the resolved path, in the seller's notification), so they are written in
/// Turkish like the rest of that surface and never leak internal state names.
/// </para>
/// </remarks>
public sealed class BlockchainPayoutVerifier : IPayoutVerifier
{
    private readonly AppDbContext _db;
    private readonly IBlockchainTransferClient _client;
    private readonly ILogger<BlockchainPayoutVerifier> _logger;

    public BlockchainPayoutVerifier(
        AppDbContext db,
        IBlockchainTransferClient client,
        ILogger<BlockchainPayoutVerifier> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    public async Task<PayoutVerificationResult> VerifyAsync(
        Guid transactionId,
        string? expectedPayoutTxHash,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a soft-deleted payout row is still chain history,
        // and hiding it here would turn a verifiable payout into "no record".
        var payout = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.TransactionId == transactionId
                        && b.Type == BlockchainTransactionType.SELLER_PAYOUT)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new { b.TxHash, b.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (payout is null)
        {
            _logger.LogWarning(
                "Payout verification found no SELLER_PAYOUT row for transaction {TransactionId}.",
                transactionId);
            return Anomaly(
                "İşleme ait ödeme kaydı bulunamadı — admin incelemesi başlatıldı.");
        }

        if (expectedPayoutTxHash is not null
            && payout.TxHash is not null
            && !string.Equals(expectedPayoutTxHash, payout.TxHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Payout hash mismatch for transaction {TransactionId}: expected {Expected}, recorded {Recorded}.",
                transactionId, expectedPayoutTxHash, payout.TxHash);
            return Anomaly(
                "Ödeme kaydı tutarsız görünüyor — admin incelemesi başlatıldı.");
        }

        switch (payout.Status)
        {
            case BlockchainTransactionStatus.CONFIRMED when payout.TxHash is not null:
                return new PayoutVerificationResult(
                    PayoutVerificationOutcome.Confirmed,
                    payout.TxHash,
                    "Ödeme blockchain üzerinde doğrulandı.");

            case BlockchainTransactionStatus.CONFIRMED:
                // Confirmed without a hash is not a state the pipeline can
                // produce; treating it as evidence would mean resolving on a
                // record nobody can look up.
                _logger.LogWarning(
                    "SELLER_PAYOUT row for transaction {TransactionId} is CONFIRMED with a null TxHash.",
                    transactionId);
                return Anomaly(
                    "Ödeme kaydı tutarsız görünüyor — admin incelemesi başlatıldı.");

            case BlockchainTransactionStatus.FAILED:
                return Anomaly(
                    "Ödeme transferi zincirde başarısız olmuş — admin incelemesi başlatıldı.");
        }

        if (payout.TxHash is null)
        {
            return new PayoutVerificationResult(
                PayoutVerificationOutcome.StillPending,
                null,
                "Ödeme henüz zincire gönderilmedi — takip ediliyor.");
        }

        var status = await _client.GetStatusAsync(payout.TxHash, cancellationToken);

        return status.Outcome switch
        {
            TransferStatusOutcome.Confirmed => new PayoutVerificationResult(
                PayoutVerificationOutcome.Confirmed,
                payout.TxHash,
                "Ödeme blockchain üzerinde doğrulandı."),

            TransferStatusOutcome.Failed => Anomaly(
                "Ödeme transferi zincirde başarısız olmuş — admin incelemesi başlatıldı."),

            TransferStatusOutcome.Pending => new PayoutVerificationResult(
                PayoutVerificationOutcome.StillPending,
                null,
                "Ödeme zincirde onay bekliyor — takip ediliyor."),

            _ => new PayoutVerificationResult(
                PayoutVerificationOutcome.UnableToVerify,
                null,
                "Blockchain doğrulaması şu an yapılamadı — admin incelemesi başlatıldı."),
        };
    }

    private static PayoutVerificationResult Anomaly(string message)
        => new(PayoutVerificationOutcome.AnomalyDetected, null, message);
}
