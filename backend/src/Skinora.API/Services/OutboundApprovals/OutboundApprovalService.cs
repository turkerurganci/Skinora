using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Application.Networks;

namespace Skinora.API.Services.OutboundApprovals;

/// <summary>
/// AD40 / AD41 — the admin approval of outbound payouts and refunds while the
/// launch gate <c>outbound.auto_send_enabled</c> is closed (T169 — 07 §9.35;
/// <see cref="OutboundApprovalGate"/>). ⚠ PARA.
/// </summary>
public interface IOutboundApprovalService
{
    /// <summary>AD40 — the gate's state and the rows waiting for an approval (oldest first).</summary>
    Task<OutboundApprovalQueueDto> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>AD41 — approve one row for sending.</summary>
    Task<ApprovalResult> ApproveAsync(
        Guid adminId, Guid blockchainTransactionId, string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>AD40 response.</summary>
/// <param name="AutoSendEnabled">The gate's state: true = rows are sent without approval.</param>
public sealed record OutboundApprovalQueueDto(
    bool AutoSendEnabled,
    IReadOnlyList<OutboundApprovalRowDto> Rows);

/// <summary>One outbound row waiting for an approval.</summary>
/// <param name="Token">The stablecoin the transaction expected (06 §3.8 <c>Token</c>).</param>
/// <param name="ChainToken">The stablecoin the row moves on chain (a wrong-token refund: the one that arrived); null when unresolvable.</param>
/// <param name="GasFee">The network fee already deducted from the amount (refunds) or charged to the seller (payout); null when none.</param>
public sealed record OutboundApprovalRowDto(
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainNetwork Network,
    BlockchainTransactionType Type,
    StablecoinType Token,
    StablecoinType? ChainToken,
    decimal Amount,
    decimal? GasFee,
    string ToAddress,
    int RetryCount,
    DateTime CreatedAt);

/// <summary>AD41 response: who approved what, when.</summary>
public sealed record OutboundApprovalDto(
    Guid BlockchainTransactionId,
    BlockchainNetwork Network,
    BlockchainTransactionType Type,
    decimal Amount,
    string ToAddress,
    DateTime ApprovedAt,
    Guid ApprovedByAdminId);

public enum ApprovalError
{
    RowNotFound,
    RowNotApprovable,
    RowNotOpen,
    AlreadyApproved,
}

public sealed record ApprovalResult(OutboundApprovalDto? Value, ApprovalError? Error, string? Message)
{
    public static ApprovalResult Ok(OutboundApprovalDto value) => new(value, null, null);
    public static ApprovalResult Fail(ApprovalError error, string message) => new(null, error, message);
}

/// <summary>
/// Default <see cref="IOutboundApprovalService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What approval does.</b> It stamps <c>ApprovedAt</c> /
/// <c>ApprovedByAdminId</c> on the row and writes an
/// <c>OUTBOUND_TRANSFER_APPROVED</c> audit row in the same save. Nothing is
/// sent here: the next dispatcher tick picks the row up and sends it exactly as
/// it would have without the gate (same id, same amount, same recipient, the
/// signer's own caps). The amount and the recipient were fixed when the row
/// was created and are not changed by anyone afterwards, so what the admin
/// sees is what leaves.
/// </para>
/// <para>
/// <b>Rules.</b> Only a payout or refund row (<see cref="OutboundApprovalGate.ApprovalTypes"/>
/// — never a sweep), only while it is <c>PENDING</c> with no hash, only once.
/// One admin is enough (yönetici kararı, T169: the platform has a single admin
/// today — 2026-10-21 karar sayfası madde 20); every approval is audited with
/// the row's network, type, amount and recipient. An approval given while the
/// gate is open is still recorded — it does nothing until the gate closes.
/// </para>
/// </remarks>
public sealed class OutboundApprovalService : IOutboundApprovalService
{
    private readonly AppDbContext _db;
    private readonly IStablecoinContractRegistry _contracts;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboundApprovalService> _logger;

    public OutboundApprovalService(
        AppDbContext db,
        IStablecoinContractRegistry contracts,
        TimeProvider clock,
        ILogger<OutboundApprovalService> logger)
    {
        _db = db;
        _contracts = contracts;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OutboundApprovalQueueDto> ListPendingAsync(CancellationToken cancellationToken)
    {
        var autoSend = await OutboundApprovalGate.IsAutoSendEnabledAsync(_db, cancellationToken);

        var rows = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(b => b.Status == BlockchainTransactionStatus.PENDING
                && b.TxHash == null
                && b.ApprovedAt == null
                && OutboundApprovalGate.ApprovalTypes.Contains(b.Type))
            .OrderBy(b => b.CreatedAt)
            .Take(200)
            .Select(b => new
            {
                b.Id,
                b.TransactionId,
                b.Network,
                b.Type,
                b.Token,
                b.ActualTokenAddress,
                b.Amount,
                b.GasFee,
                b.ToAddress,
                b.RetryCount,
                b.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new OutboundApprovalQueueDto(
            autoSend,
            rows.Select(r => new OutboundApprovalRowDto(
                    r.Id, r.TransactionId, r.Network, r.Type, r.Token,
                    OutboundTransferExpectation.ChainToken(r.Network, r.Type, r.Token, r.ActualTokenAddress, _contracts),
                    r.Amount, r.GasFee, r.ToAddress, r.RetryCount, r.CreatedAt))
                .ToList());
    }

    public async Task<ApprovalResult> ApproveAsync(
        Guid adminId, Guid blockchainTransactionId, string? ipAddress, CancellationToken cancellationToken)
    {
        var row = await _db.Set<BlockchainTransaction>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == blockchainTransactionId, cancellationToken);
        if (row is null)
            return ApprovalResult.Fail(ApprovalError.RowNotFound, "No such blockchain transaction row.");
        if (!OutboundApprovalGate.RequiresApproval(row.Type))
            return ApprovalResult.Fail(ApprovalError.RowNotApprovable,
                $"A {row.Type} row is not a payout or refund; it never waits for an approval.");
        if (row.ApprovedAt is not null)
            return ApprovalResult.Fail(ApprovalError.AlreadyApproved,
                "The row is already approved.");
        if (row.Status != BlockchainTransactionStatus.PENDING || row.TxHash is not null)
            return ApprovalResult.Fail(ApprovalError.RowNotOpen,
                $"The row is {row.Status}{(row.TxHash is null ? string.Empty : " with a hash")} — only a PENDING row without a hash can be approved.");

        var now = _clock.GetUtcNow().UtcDateTime;
        row.ApprovedAt = now;
        row.ApprovedByAdminId = adminId;

        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = null,
            ActorId = adminId,
            ActorType = ActorType.ADMIN,
            Action = AuditAction.OUTBOUND_TRANSFER_APPROVED,
            EntityType = nameof(BlockchainTransaction),
            EntityId = row.Id.ToString("D"),
            OldValue = null,
            NewValue = JsonSerializer.Serialize(new
            {
                blockchainTransactionId = row.Id,
                transactionId = row.TransactionId,
                network = row.Network.ToString(),
                type = row.Type.ToString(),
                token = row.Token.ToString(),
                amount = row.Amount.ToString("0.######", CultureInfo.InvariantCulture),
                gasFee = row.GasFee?.ToString("0.######", CultureInfo.InvariantCulture),
                toAddress = row.ToAddress,
                retryCount = row.RetryCount,
            }),
            IpAddress = ipAddress,
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "OUTBOUND TRANSFER APPROVED — row {Row} ({Type}, {Network}) {Amount} → {To} by admin {Admin}; the dispatcher sends it on its next tick.",
            row.Id, row.Type, row.Network, row.Amount, row.ToAddress, adminId);

        return ApprovalResult.Ok(new OutboundApprovalDto(
            row.Id, row.Network, row.Type, row.Amount, row.ToAddress, now, adminId));
    }
}
