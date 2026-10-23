using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.WalletClosures;

/// <summary>
/// The two-person tool that closes an outbound row with the hash of a
/// transaction that is already on chain (T157 — 07 §9.32; DEPLOY_RUNBOOK §D
/// adım 7.4; T172a — DEPLOY_RUNBOOK §N.8). ⚠ PARA.
/// </summary>
/// <remarks>
/// <para>
/// <b>T172a — TRON and the restore case.</b> After a database restore an
/// outbound row that already went out on chain is <c>PENDING</c> again (TRON
/// would sign it a second time — its service keeps no journal), or was closed
/// by hand as <c>FAILED</c> with the <c>POST_RESTORE_DUPLICATE</c> note, which
/// leaves the books short (reconciliation and the refund-fee ledger count only
/// <c>CONFIRMED</c> rows). The tool now takes either network and either row: a
/// <c>PENDING</c> row without a hash, or a <c>FAILED</c> row without a hash
/// that carries the note. Approval binds the chain's hash to <i>the same row</i>
/// (<c>→ DETECTED</c>) and the confirmation job finishes it from the chain
/// (<c>CONFIRMED</c>, block, confirmations, realized fee; a payout announces
/// itself so the transaction completes). Every check below applies to both.
/// The TRON service answers the verify read too (<c>POST /api/transfer/verify</c>,
/// solidified transaction, TRC-20 <c>Transfer</c> log). The sender of a row the
/// dispatcher never sent is resolved the way the dispatcher would (the deposit
/// of the transaction's payment, or the hot wallet for a payout / an Ethereum
/// refund after the sweep) and written to the row on approval, so the
/// reconciliation and the refund-fee ledger count it where it left from.
/// </para>
/// <para>
/// <b>Why.</b> After the Ethereum service loses its journal it stops signing
/// (<c>JOURNAL_BEHIND_CHAIN</c>): a row it no longer remembers would be signed
/// again. The operator finds the nonces in between on the chain and matches
/// each to a Skinora row; this service records that match so the dispatcher
/// never asks the service about the row again and the confirmation job
/// finishes it from the chain.
/// </para>
/// <para>
/// <b>Rules, each pinned by a test and a mutation.</b> Only the Ethereum
/// network; only a row that is outbound, <c>PENDING</c> and has no hash; the
/// hash is <c>0x</c> + 64 hex and is bound to at most one row; the proposer is
/// never the approver; every step is audited; the tool sends nothing — approval
/// writes <c>TxHash</c> and moves the row <c>PENDING → DETECTED</c>.
/// Approval also asks the Ethereum service about the hash and refuses unless
/// the chain shows it <i>finalized and successful</i> (fail closed: an
/// unanswered question is a refusal).
/// </para>
/// <para>
/// <b>Recipient and amount are checked by the machine.</b> The service's status
/// answer for a hash its journal does not know says only "mined, status 1", so
/// after it the tool asks the Ethereum service to <i>verify</i> the hash against
/// the row (<c>POST /api/transfer/verify</c>): a successful transaction with a
/// Transfer log of the token's contract to the row's recipient for exactly the
/// row's amount (from the hot wallet for a payout, from the deposit for a
/// refund or sweep). Anything but an exact match — no matching transfer, a
/// revert, an unknown hash, an unreachable or unsupporting service — refuses the
/// approval. The two admins still compare the explorer page with the row; the
/// machine check is the second line, not the only one.
/// </para>
/// <para>
/// <b>T157 düzeltme turu (⚠ PARA).</b> What the row moves on chain comes from
/// <see cref="OutboundTransferExpectation"/> — the rule the dispatcher signs
/// with and the confirmation job's status read expects (T158 (g)): a
/// wrong-token refund moves the token that arrived (<c>ActualTokenAddress</c>),
/// a refund after the sweep leaves the hot wallet (<c>FromAddress</c>). A row
/// whose token or sender cannot be resolved is refused (fail closed) — the
/// confirmation job could never confirm it either. The status read itself
/// carries that expectation, so the Ethereum service checks the hash against
/// the row there too. And a row is refused while another open
/// (<c>PENDING</c>/<c>DETECTED</c>) outbound row on the network has the same
/// recipient, token and amount (<c>AMBIGUOUS_ROW_MATCH</c>, yönetici kararı):
/// the hash cannot say which of the two it paid, and binding it to the
/// unsigned one would leave the other to be blocked on the unique hash index
/// at its own dispatch — the seller one payment short.
/// </para>
/// </remarks>
public sealed partial class OutboundRowClosureService : IOutboundRowClosureService
{
    public const int MinReasonLength = 20;
    public const int MaxReasonLength = 2000;
    public const int MaxNoteLength = 2000;

    [GeneratedRegex("^0x[0-9a-fA-F]{64}$")]
    private static partial Regex EthereumTxHashPattern();

    /// <summary>T172a — a TRON transaction id: 64 hex digits, no prefix (TronScan's form).</summary>
    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex TronTxHashPattern();

    /// <summary>
    /// T172a — the note a row closed by hand after a restore carries
    /// (DEPLOY_RUNBOOK §N.8 adım 5). Only a <c>FAILED</c> row that starts with it
    /// can be brought back with a chain hash: any other failed row is a send
    /// that did not happen.
    /// </summary>
    public const string PostRestoreDuplicateMarker = "POST_RESTORE_DUPLICATE";

    private const int ErrorMessageMaxLength = 500;

    private static readonly BlockchainTransactionType[] OutboundTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        BlockchainTransactionType.SWEEP,
        BlockchainTransactionType.REFUND_FEE_SWEEP,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly IStablecoinContractRegistry _contracts;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboundRowClosureService> _logger;

    public OutboundRowClosureService(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IPlatformWalletAddressProvider walletAddresses,
        IStablecoinContractRegistry contracts,
        TimeProvider clock,
        ILogger<OutboundRowClosureService> logger)
    {
        _db = db;
        _services = services;
        _walletAddresses = walletAddresses;
        _contracts = contracts;
        _clock = clock;
        _logger = logger;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<ClosureCandidateDto>> ListCandidatesAsync(
        BlockchainNetwork? network, CancellationToken cancellationToken)
    {
        var query = OpenRows(_db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters());
        if (network.HasValue) query = query.Where(b => b.Network == network.Value);
        var rows = await query
            .OrderBy(b => b.CreatedAt)
            .Take(200)
            .Select(b => new
            {
                Dto = new ClosureCandidateDto(
                    b.Id, b.TransactionId, b.Network, b.Status, b.Type, b.Token, null, b.Amount,
                    b.FromAddress, b.ToAddress, b.RetryCount, b.CreatedAt,
                    _db.Set<OutboundRowClosure>().Any(c => c.BlockchainTransactionId == b.Id
                        && c.Status != OutboundRowClosureStatus.REJECTED)),
                b.ActualTokenAddress,
            })
            .ToListAsync(cancellationToken);
        // The token the row moves on chain (a wrong-token refund: the one that arrived).
        return rows
            .Select(r => r.Dto with
            {
                ChainToken = OutboundTransferExpectation.ChainToken(
                    r.Dto.Network, r.Dto.Type, r.Dto.Token, r.ActualTokenAddress, _contracts),
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ClosureDto>> ListAsync(
        OutboundRowClosureStatus? status, BlockchainNetwork? network, CancellationToken cancellationToken)
    {
        var query = _db.Set<OutboundRowClosure>().AsNoTracking();
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        if (network.HasValue) query = query.Where(c => c.Network == network.Value);

        return await ProjectAsync(query, cancellationToken);
    }

    /// <summary>Closures with their rows, newest first, the chain token resolved.</summary>
    private async Task<List<ClosureDto>> ProjectAsync(
        IQueryable<OutboundRowClosure> query, CancellationToken cancellationToken)
    {
        var rows = await (
            from c in query
            join b in _db.Set<BlockchainTransaction>().IgnoreQueryFilters().AsNoTracking()
                on c.BlockchainTransactionId equals b.Id
            join p in _db.Set<User>().IgnoreQueryFilters().AsNoTracking() on c.ProposedByAdminId equals p.Id
            join d in _db.Set<User>().IgnoreQueryFilters().AsNoTracking() on c.DecidedByAdminId equals d.Id into ds
            from d in ds.DefaultIfEmpty()
            orderby c.CreatedAt descending
            select new
            {
                Dto = new ClosureDto(
                    c.Id, c.Status, c.Network, c.BlockchainTransactionId, b.TransactionId, b.Type, b.Token, null,
                    b.Amount, b.FromAddress, b.ToAddress, b.Status, c.TxHash, c.Reason,
                    c.ProposedByAdminId, p.SteamDisplayName, c.CreatedAt,
                    c.DecidedByAdminId, d == null ? null : d.SteamDisplayName, c.DecidedAt, c.DecisionNote),
                b.Network,
                b.ActualTokenAddress,
            })
            .Take(200)
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => r.Dto with
            {
                ChainToken = OutboundTransferExpectation.ChainToken(
                    r.Network, r.Dto.RowType, r.Dto.Token, r.ActualTokenAddress, _contracts),
            })
            .ToList();
    }

    // -------------------------------------------------------------- propose

    public async Task<ClosureResult> ProposeAsync(
        Guid adminId, Guid blockchainTransactionId, string? txHash, string? reason,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var cleanReason = reason?.Trim() ?? string.Empty;
        if (cleanReason.Length < MinReasonLength || cleanReason.Length > MaxReasonLength)
        {
            return ClosureResult.Fail(ClosureError.Validation,
                $"reason is required ({MinReasonLength}-{MaxReasonLength} characters): say which nonce, which account and what was matched.");
        }
        var row = await LoadRowAsync(blockchainTransactionId, cancellationToken);
        if (row is null)
        {
            return ClosureResult.Fail(ClosureError.RowNotFound, "Outbound row not found.");
        }
        if (!TryCanonicalHash(row.Network, txHash, out var hash))
        {
            return ClosureResult.Fail(ClosureError.InvalidHash, HashShapeMessage(row.Network));
        }
        if (!OutboundTypes.Contains(row.Type))
        {
            return ClosureResult.Fail(ClosureError.RowNotOutbound,
                "Only an outbound row (payout, refund or sweep) can be closed with this tool.");
        }
        if (!IsOpen(row))
        {
            return ClosureResult.Fail(ClosureError.RowNotOpen,
                $"The row is no longer open: only a PENDING row without a hash, or a FAILED row without a hash noted {PostRestoreDuplicateMarker} after a restore, can be closed.");
        }
        var expected = await ExpectationAsync(row, cancellationToken);
        if (expected is null)
        {
            return ClosureResult.Fail(ClosureError.RowTransferUnresolved, UnresolvedMessage(row));
        }
        if (await HasLookAlikeOpenRowAsync(row, cancellationToken))
        {
            return ClosureResult.Fail(ClosureError.AmbiguousRowMatch, AmbiguousMessage);
        }
        if (await HashIsBoundAsync(row.Network, hash, cancellationToken))
        {
            return ClosureResult.Fail(ClosureError.HashAlreadyBound,
                "This hash is already bound to a row or to another closure.");
        }
        if (await _db.Set<OutboundRowClosure>().AnyAsync(
                c => c.BlockchainTransactionId == row.Id
                    && c.Status != OutboundRowClosureStatus.REJECTED, cancellationToken))
        {
            return ClosureResult.Fail(ClosureError.OpenClosureExists,
                "This row already has a live closure.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var closure = new OutboundRowClosure
        {
            Id = Guid.NewGuid(),
            BlockchainTransactionId = row.Id,
            Network = row.Network,
            TxHash = hash,
            Reason = cleanReason,
            Status = OutboundRowClosureStatus.PROPOSED,
            ProposedByAdminId = adminId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Set<OutboundRowClosure>().Add(closure);
        AddAudit(adminId, closure.Id, AuditAction.OUTBOUND_ROW_CLOSURE_PROPOSED, now, ipAddress,
            RowEnvelope(row, hash, cleanReason,
                chainToken: OutboundTransferExpectation.ChainToken(row, _contracts),
                fromAddress: expected.FromAddress));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex,
                "Closure proposal for row {Row} lost a uniqueness race on hash {Hash}.", row.Id, hash);
            return ClosureResult.Fail(ClosureError.Conflict,
                "Another proposal for this row or hash was recorded at the same moment.");
        }

        _logger.LogWarning(
            "OUTBOUND ROW CLOSURE PROPOSED — closure {Closure} row {Row} hash {Hash} by admin {Admin}.",
            closure.Id, row.Id, hash, adminId);
        return ClosureResult.Ok(await ReadAsync(closure.Id, cancellationToken));
    }

    // -------------------------------------------------------------- approve

    public async Task<ClosureResult> ApproveAsync(
        Guid adminId, Guid closureId, string? confirmTxHash,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var closure = await _db.Set<OutboundRowClosure>()
            .FirstOrDefaultAsync(c => c.Id == closureId, cancellationToken);
        if (closure is null)
        {
            return ClosureResult.Fail(ClosureError.ClosureNotFound, "Closure not found.");
        }
        if (closure.Status != OutboundRowClosureStatus.PROPOSED)
        {
            return ClosureResult.Fail(ClosureError.ClosureNotOpen,
                "The closure is already decided.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // Two-person rule. Checked before anything else about the closure, and
        // logged: an attempt to approve one's own proposal is something a
        // reviewer wants to see.
        if (closure.ProposedByAdminId == adminId)
        {
            await RefuseAsync(adminId, closure, "SELF_APPROVAL", closure.TxHash, now, ipAddress,
                cancellationToken);
            return ClosureResult.Fail(ClosureError.SelfApproval,
                "The admin who proposed a closure cannot approve it; a different admin must.");
        }

        // The approver types the hash again: proof the hash on their screen
        // is the one they looked up, not just the one the proposer sent.
        if (!TryCanonicalHash(closure.Network, confirmTxHash, out var confirmed)
            || !string.Equals(confirmed, closure.TxHash, StringComparison.Ordinal))
        {
            await RefuseAsync(adminId, closure, "HASH_CONFIRMATION_MISMATCH", closure.TxHash, now,
                ipAddress, cancellationToken);
            return ClosureResult.Fail(ClosureError.HashMismatch,
                "txHash does not match the proposed hash. Type the hash you verified on the explorer.");
        }

        var row = await LoadRowAsync(closure.BlockchainTransactionId, cancellationToken);
        if (row is null
            || row.Network != closure.Network
            || !IsOpen(row))
        {
            return ClosureResult.Fail(ClosureError.RowNotOpen,
                "The row is no longer open (it was sent, closed or failed since the proposal). Reject the proposal.");
        }
        if (await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters().AnyAsync(
                b => b.Network == closure.Network && b.TxHash == closure.TxHash,
                cancellationToken))
        {
            return ClosureResult.Fail(ClosureError.HashAlreadyBound,
                "This hash is already bound to a row.");
        }

        // What the row moves on chain — the confirmation job will expect exactly
        // this (T158 (g)), so a row it cannot describe is not closed.
        var expected = await ExpectationAsync(row, cancellationToken);
        var chainToken = OutboundTransferExpectation.ChainToken(row, _contracts);
        if (expected is null || chainToken is null)
        {
            await RefuseAsync(adminId, closure, "ROW_TRANSFER_UNRESOLVED", closure.TxHash, now,
                ipAddress, cancellationToken);
            return ClosureResult.Fail(ClosureError.RowTransferUnresolved, UnresolvedMessage(row));
        }
        // Checked again: a look-alike row may have been queued since the proposal.
        if (await HasLookAlikeOpenRowAsync(row, cancellationToken))
        {
            await RefuseAsync(adminId, closure, "AMBIGUOUS_ROW_MATCH", closure.TxHash, now,
                ipAddress, cancellationToken);
            return ClosureResult.Fail(ClosureError.AmbiguousRowMatch, AmbiguousMessage);
        }

        // The chain must show the hash as finalized and successful. Fail
        // closed: an unanswered question is a refusal, never a pass. The read
        // carries the row's expectation (T158 (g)): for a hash the journal does
        // not hold the service checks the Transfer log against it.
        var status = await _services.Transfers(row.Network)
            .GetStatusAsync(closure.TxHash, expected, cancellationToken);
        switch (status.Outcome)
        {
            case TransferStatusOutcome.Confirmed:
                break;
            case TransferStatusOutcome.Pending:
                return ClosureResult.Fail(ClosureError.HashNotFinal,
                    "The chain does not show this transaction as finalized yet. Try again later.");
            case TransferStatusOutcome.Failed:
                await RefuseAsync(adminId, closure, "HASH_NOT_SUCCESSFUL", closure.TxHash, now,
                    ipAddress, cancellationToken);
                return ClosureResult.Fail(ClosureError.HashNotSuccessful,
                    $"The transaction did not succeed on chain ({status.ContractRet ?? "no result"}); it cannot close a row as sent.");
            case TransferStatusOutcome.Rejected:
                await RefuseAsync(adminId, closure, "HASH_NOT_FOUND", closure.TxHash, now,
                    ipAddress, cancellationToken);
                return ClosureResult.Fail(ClosureError.HashNotFound,
                    $"The {row.Network} service does not know this hash ({status.ErrorMessage}).");
            case TransferStatusOutcome.Unverified:
                // T158 (g) UNKNOWN: mined, but its Transfer log is not the row's.
                await RefuseAsync(adminId, closure, "HASH_DOES_NOT_MATCH_ROW", closure.TxHash, now,
                    ipAddress, cancellationToken);
                return ClosureResult.Fail(ClosureError.HashDoesNotMatchRow, DoesNotMatchMessage);
            default:
                return ClosureResult.Fail(ClosureError.ChainUnavailable,
                    $"The {row.Network} service could not answer; nothing was changed. Try again when it is reachable.");
        }

        // The status above only says "mined, status 1" for a hash the journal does
        // not know. Check that THIS transaction moved THIS row's amount to THIS
        // row's recipient — and only an exact match goes on.
        var verify = await _services.Transfers(row.Network).VerifyTransferAsync(
            new TransferVerifyRequest(
                closure.TxHash,
                chainToken.Value,
                expected.FromAddress,
                row.ToAddress,
                row.Amount),
            cancellationToken);
        switch (verify.Outcome)
        {
            case TransferVerifyOutcome.Match:
                break;
            case TransferVerifyOutcome.NoMatchingTransfer:
            case TransferVerifyOutcome.Reverted:
                await RefuseAsync(adminId, closure, "HASH_DOES_NOT_MATCH_ROW", closure.TxHash, now,
                    ipAddress, cancellationToken);
                return ClosureResult.Fail(ClosureError.HashDoesNotMatchRow, DoesNotMatchMessage);
            case TransferVerifyOutcome.NotFound:
                await RefuseAsync(adminId, closure, "HASH_NOT_FOUND", closure.TxHash, now,
                    ipAddress, cancellationToken);
                return ClosureResult.Fail(ClosureError.HashNotFound,
                    $"The {row.Network} service's node does not know this hash.");
            default:
                // Unavailable and NotSupported: not verified, so not approved.
                return ClosureResult.Fail(ClosureError.ChainUnavailable,
                    $"The {row.Network} service could not verify the transaction against the row; nothing was changed. Try again when it is reachable.");
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Compare-and-set on the row: the dispatcher or an earlier closure may
        // have moved it since it was read. Zero rows updated = it moved. The
        // row must still be in the state it was read in (PENDING, or the
        // post-restore FAILED row with its note) and still carry no hash.
        // T172a — the sender the hash was verified from is written when the
        // row had none (a row the dispatcher never sent), so the books count
        // it where it left from.
        var readStatus = row.Status;
        var note = readStatus == BlockchainTransactionStatus.FAILED
            ? $"Reopened with the on-chain hash by closure {closure.Id} (T172a, post-restore). Was: {row.ErrorMessage}"
            : $"Closed with the on-chain hash by closure {closure.Id} (T157).";
        if (note.Length > ErrorMessageMaxLength) note = note[..ErrorMessageMaxLength];
        var fromAddress = string.IsNullOrWhiteSpace(row.FromAddress)
            ? BlockchainAddress.Canonicalize(expected.FromAddress)
            : row.FromAddress;
        var updated = await _db.Set<BlockchainTransaction>()
            .IgnoreQueryFilters()
            .Where(b => b.Id == row.Id
                && b.Network == closure.Network
                && b.Status == readStatus
                && b.TxHash == null
                && (b.Status == BlockchainTransactionStatus.PENDING
                    || (b.Status == BlockchainTransactionStatus.FAILED
                        && b.ErrorMessage != null
                        && b.ErrorMessage.StartsWith(PostRestoreDuplicateMarker))))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.TxHash, closure.TxHash)
                .SetProperty(b => b.Status, BlockchainTransactionStatus.DETECTED)
                .SetProperty(b => b.ConfirmationCount, 0)
                .SetProperty(b => b.NextAttemptAt, (DateTime?)null)
                .SetProperty(b => b.FromAddress, fromAddress)
                .SetProperty(b => b.ErrorMessage, note),
                cancellationToken);
        if (updated != 1)
        {
            await dbTx.RollbackAsync(cancellationToken);
            return ClosureResult.Fail(ClosureError.RowNotOpen,
                "The row changed while the approval ran; nothing was changed.");
        }

        closure.Status = OutboundRowClosureStatus.APPROVED;
        closure.DecidedByAdminId = adminId;
        closure.DecidedAt = now;
        closure.UpdatedAt = now;
        AddAudit(adminId, closure.Id, AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED, now, ipAddress,
            RowEnvelope(row, closure.TxHash, closure.Reason,
                chainToken: chainToken,
                fromAddress: expected.FromAddress,
                rowStatusBefore: readStatus.ToString(),
                proposedByAdminId: closure.ProposedByAdminId,
                chainContractRet: status.ContractRet,
                chainBlock: status.BlockNumber));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Closure {Closure} approval could not be saved.", closure.Id);
            await dbTx.RollbackAsync(cancellationToken);
            return ClosureResult.Fail(ClosureError.Conflict,
                "The closure was changed at the same moment; nothing was changed.");
        }

        _logger.LogWarning(
            "OUTBOUND ROW CLOSURE APPROVED — closure {Closure} {Network} row {Row} ({Before} → DETECTED) now carries {Hash}; proposed by {Proposer}, approved by {Approver}. No money was sent.",
            closure.Id, closure.Network, row.Id, readStatus, closure.TxHash, closure.ProposedByAdminId, adminId);
        return ClosureResult.Ok(await ReadAsync(closure.Id, cancellationToken));
    }

    // --------------------------------------------------------------- reject

    public async Task<ClosureResult> RejectAsync(
        Guid adminId, Guid closureId, string? note,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var closure = await _db.Set<OutboundRowClosure>()
            .FirstOrDefaultAsync(c => c.Id == closureId, cancellationToken);
        if (closure is null)
        {
            return ClosureResult.Fail(ClosureError.ClosureNotFound, "Closure not found.");
        }
        if (closure.Status != OutboundRowClosureStatus.PROPOSED)
        {
            return ClosureResult.Fail(ClosureError.ClosureNotOpen,
                "The closure is already decided.");
        }

        var cleanNote = note?.Trim();
        if (string.IsNullOrEmpty(cleanNote) || cleanNote.Length > MaxNoteLength)
        {
            return ClosureResult.Fail(ClosureError.Validation,
                $"note is required (1-{MaxNoteLength} characters).");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        closure.Status = OutboundRowClosureStatus.REJECTED;
        closure.DecidedByAdminId = adminId;
        closure.DecidedAt = now;
        closure.DecisionNote = cleanNote;
        closure.UpdatedAt = now;
        AddAudit(adminId, closure.Id, AuditAction.OUTBOUND_ROW_CLOSURE_REJECTED, now, ipAddress,
            JsonSerializer.Serialize(new
            {
                txHash = closure.TxHash,
                blockchainTransactionId = closure.BlockchainTransactionId,
                proposedByAdminId = closure.ProposedByAdminId,
                note = cleanNote,
            }));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClosureResult.Fail(ClosureError.Conflict,
                "The closure was changed at the same moment; nothing was changed.");
        }

        return ClosureResult.Ok(await ReadAsync(closure.Id, cancellationToken));
    }

    // -------------------------------------------------------------- helpers

    private const string DoesNotMatchMessage =
        "This transaction does not move the row's amount of the row's token to the row's recipient from the row's sender. It cannot close this row.";

    private const string AmbiguousMessage =
        "Another open outbound row on this network has the same recipient, token and amount; a chain hash cannot say which one it paid. Follow the manual path (DEPLOY_RUNBOOK §D 7.4 / §N.8).";

    private static string HashShapeMessage(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON
            ? "txHash must be the TRON transaction id: 64 hex digits, no 0x prefix."
            : "txHash must be 0x followed by 64 hex digits.";

    /// <summary>
    /// The rows the tool may close (T172a): outbound, no hash, and either
    /// PENDING or FAILED with the post-restore note.
    /// </summary>
    private static IQueryable<BlockchainTransaction> OpenRows(IQueryable<BlockchainTransaction> rows) =>
        rows.Where(b => b.TxHash == null
            && OutboundTypes.Contains(b.Type)
            && (b.Status == BlockchainTransactionStatus.PENDING
                || (b.Status == BlockchainTransactionStatus.FAILED
                    && b.ErrorMessage != null
                    && b.ErrorMessage.StartsWith(PostRestoreDuplicateMarker))));

    private static bool IsOpen(BlockchainTransaction row) =>
        row.TxHash is null
        && (row.Status == BlockchainTransactionStatus.PENDING
            || (row.Status == BlockchainTransactionStatus.FAILED
                && row.ErrorMessage is not null
                && row.ErrorMessage.StartsWith(PostRestoreDuplicateMarker, StringComparison.Ordinal)));

    /// <summary>
    /// What the row moves on chain, with the sender resolved the way the
    /// dispatcher would resolve it when the row does not record one (T172a — a
    /// row restored or re-created before it was ever sent): the payout's hot
    /// wallet; a refund's deposit (the transaction's payment address), or the
    /// hot wallet for an Ethereum buyer refund after a confirmed sweep; a
    /// refund whose sweep is still in flight is unresolved (fail closed).
    /// </summary>
    private async Task<TransferStatusExpectation?> ExpectationAsync(
        BlockchainTransaction row, CancellationToken cancellationToken)
    {
        var direct = OutboundTransferExpectation.For(row, _contracts, _walletAddresses);
        if (direct is not null || !string.IsNullOrWhiteSpace(row.FromAddress)) return direct;

        var token = OutboundTransferExpectation.ChainToken(row, _contracts);
        if (token is null || string.IsNullOrWhiteSpace(row.ToAddress)) return null;
        var contract = _contracts.ResolveContractAddress(row.Network, token.Value);
        if (string.IsNullOrWhiteSpace(contract)) return null;

        var sender = await ResolveSenderAsync(row, cancellationToken);
        return sender is null
            ? null
            : new TransferStatusExpectation(contract, sender, row.ToAddress, row.Amount);
    }

    private async Task<string?> ResolveSenderAsync(BlockchainTransaction row, CancellationToken cancellationToken)
    {
        if (row.Type == BlockchainTransactionType.SELLER_PAYOUT)
        {
            var hot = _walletAddresses.For(row.Network).HotWalletAddress;
            return string.IsNullOrWhiteSpace(hot) ? null : hot;
        }

        var source = RefundSource.Deposit;
        if (row.Type is not (BlockchainTransactionType.SWEEP or BlockchainTransactionType.REFUND_FEE_SWEEP))
        {
            source = await RefundSourceResolver.ResolveAsync(
                _db, row.Network, row.Type, row.TransactionId, cancellationToken);
        }
        if (source == RefundSource.WaitForSweep) return null;
        if (source == RefundSource.HotWallet)
        {
            var hot = _walletAddresses.For(row.Network).HotWalletAddress;
            return string.IsNullOrWhiteSpace(hot) ? null : hot;
        }

        // The deposit the transaction's payment arrived on — the dispatcher's rule.
        var paymentAddressId = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.TransactionId == row.TransactionId
                && (b.Type == BlockchainTransactionType.BUYER_PAYMENT
                    || b.Type == BlockchainTransactionType.WRONG_TOKEN_INCOMING)
                && b.PaymentAddressId != null)
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.PaymentAddressId)
            .FirstOrDefaultAsync(cancellationToken);
        if (paymentAddressId is null) return null;
        return await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Id == paymentAddressId.Value && p.Network == row.Network)
            .Select(p => p.Address)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string UnresolvedMessage(BlockchainTransaction row) =>
        row.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND
            ? $"The token this wrong-token refund moves (contract '{row.ActualTokenAddress}') is not a configured stablecoin; the row cannot be verified, so it cannot be closed."
            : "The row does not say which account the tokens left (no sender, and not a payout); it cannot be verified, so it cannot be closed.";

    /// <summary>
    /// Yönetici kararı (⚠ PARA): another open (PENDING/DETECTED) outbound row on
    /// the network with the same recipient, the same on-chain token and the
    /// same amount. A row whose token cannot be resolved counts as the same
    /// token (fail closed).
    /// </summary>
    private async Task<bool> HasLookAlikeOpenRowAsync(
        BlockchainTransaction row, CancellationToken cancellationToken)
    {
        var token = OutboundTransferExpectation.ChainToken(row, _contracts);
        var others = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Id != row.Id
                && b.Network == row.Network
                && (b.Status == BlockchainTransactionStatus.PENDING
                    || b.Status == BlockchainTransactionStatus.DETECTED)
                && OutboundTypes.Contains(b.Type)
                && b.Amount == row.Amount)
            .Select(b => new { b.Type, b.Token, b.ActualTokenAddress, b.ToAddress })
            .ToListAsync(cancellationToken);
        return others.Any(o =>
            string.Equals(o.ToAddress, row.ToAddress, StringComparison.OrdinalIgnoreCase)
            && (OutboundTransferExpectation.ChainToken(
                    row.Network, o.Type, o.Token, o.ActualTokenAddress, _contracts) ?? token) == token);
    }

    /// <summary>
    /// The network's hash shape, in its one stored form: Ethereum <c>0x</c> +
    /// 64 hex, TRON 64 hex without a prefix — both lowercase. T172a: TRON ids
    /// come back from the service in lowercase; an id typed in CAPITALS is
    /// stored the same way, so it hits the same unique index.
    /// </summary>
    private static bool TryCanonicalHash(BlockchainNetwork network, string? raw, out string hash)
    {
        hash = string.Empty;
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        var pattern = network == BlockchainNetwork.TRON ? TronTxHashPattern() : EthereumTxHashPattern();
        if (!pattern.IsMatch(trimmed)) return false;
        hash = trimmed.ToLowerInvariant();
        return true;
    }

    private Task<BlockchainTransaction?> LoadRowAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    /// <summary>The hash is bound to a row of the network already, or to a live closure.</summary>
    private async Task<bool> HashIsBoundAsync(
        BlockchainNetwork network, string hash, CancellationToken cancellationToken)
    {
        if (await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters().AnyAsync(
                b => b.Network == network && b.TxHash == hash, cancellationToken))
        {
            return true;
        }
        return await _db.Set<OutboundRowClosure>().AsNoTracking().AnyAsync(
            c => c.Network == network && c.TxHash == hash
                && c.Status != OutboundRowClosureStatus.REJECTED, cancellationToken);
    }

    private async Task RefuseAsync(
        Guid adminId, OutboundRowClosure closure, string code, string txHash,
        DateTime now, string? ipAddress, CancellationToken cancellationToken)
    {
        AddAudit(adminId, closure.Id, AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED, now, ipAddress,
            JsonSerializer.Serialize(new { code, txHash, proposedByAdminId = closure.ProposedByAdminId }));
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "OUTBOUND ROW CLOSURE REFUSED — closure {Closure} admin {Admin} code {Code}.",
            closure.Id, adminId, code);
    }

    private void AddAudit(
        Guid adminId, Guid closureId, AuditAction action, DateTime now, string? ipAddress,
        string newValue)
    {
        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = null,
            ActorId = adminId,
            ActorType = ActorType.ADMIN,
            Action = action,
            EntityType = nameof(OutboundRowClosure),
            EntityId = closureId.ToString("D"),
            OldValue = null,
            NewValue = newValue,
            IpAddress = ipAddress,
            CreatedAt = now,
        });
    }

    private static string RowEnvelope(
        BlockchainTransaction row, string txHash, string reason,
        StablecoinType? chainToken = null, string? fromAddress = null,
        string? rowStatusBefore = null,
        Guid? proposedByAdminId = null, string? chainContractRet = null, long? chainBlock = null) =>
        JsonSerializer.Serialize(new
        {
            blockchainTransactionId = row.Id,
            transactionId = row.TransactionId,
            network = row.Network.ToString(),
            rowStatus = rowStatusBefore ?? row.Status.ToString(),
            rowError = row.ErrorMessage,
            type = row.Type.ToString(),
            toAddress = row.ToAddress,
            amount = row.Amount.ToString("0.######", CultureInfo.InvariantCulture),
            token = row.Token.ToString(),
            chainToken = chainToken?.ToString(),
            fromAddress,
            txHash,
            reason,
            proposedByAdminId,
            chainContractRet,
            chainBlock,
        });

    private async Task<ClosureDto> ReadAsync(Guid closureId, CancellationToken cancellationToken) =>
        (await ProjectAsync(
            _db.Set<OutboundRowClosure>().AsNoTracking().Where(c => c.Id == closureId),
            cancellationToken)).Single();
}
