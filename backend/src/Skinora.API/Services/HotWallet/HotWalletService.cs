using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.API.Services.Reconciliation;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Wallets;

namespace Skinora.API.Services.HotWallet;

/// <summary>
/// Admin-driven hot wallet operations orchestrator (T77 — 05 §3.3). Lives in
/// <c>Skinora.API/Services</c> rather than <c>Skinora.Transactions</c>
/// because the side effects (ColdWalletTransfer ledger row in
/// <c>Skinora.Payments</c>, AuditLog row in <c>Skinora.Platform</c>) sit in
/// modules <c>Skinora.Transactions</c> does not reference — same cross-module
/// composition rule used by <see cref="ReconciliationService"/>.
/// </summary>
public sealed class HotWalletService : IHotWalletService
{
    private const int AmountScale = 6;
    private static readonly decimal AmountScaleQuantum = 0.000001m;

    private readonly AppDbContext _db;
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly TimeProvider _clock;
    private readonly ILogger<HotWalletService> _logger;

    public HotWalletService(
        AppDbContext db,
        IBlockchainSidecarClient sidecar,
        IPlatformWalletAddressProvider walletAddresses,
        TimeProvider clock,
        ILogger<HotWalletService> logger)
    {
        _db = db;
        _sidecar = sidecar;
        _walletAddresses = walletAddresses;
        _clock = clock;
        _logger = logger;
    }

    public async Task<HotWalletColdTransferOutcome> InitiateColdTransferAsync(
        decimal amount,
        StablecoinType token,
        Guid initiatingAdminId,
        CancellationToken cancellationToken)
    {
        // Reject zero/negative and out-of-scale amounts at the boundary.
        // The sidecar will also reject these (INVALID_TRANSFER_AMOUNT), but
        // surfacing here keeps the audit-log invariant clean (no row is
        // written when input is malformed).
        if (amount <= 0m)
        {
            return new HotWalletColdTransferOutcome.InvalidAmount(
                "Amount must be a positive decimal.");
        }
        if (amount != decimal.Round(amount, AmountScale, MidpointRounding.ToZero))
        {
            return new HotWalletColdTransferOutcome.InvalidAmount(
                $"Amount must have at most {AmountScale} fractional digits.");
        }
        if (amount % AmountScaleQuantum != 0m)
        {
            return new HotWalletColdTransferOutcome.InvalidAmount(
                $"Amount must be a multiple of {AmountScaleQuantum.ToString(CultureInfo.InvariantCulture)}.");
        }

        var addresses = ReadWalletAddresses();
        if (addresses.HotWallet is null)
        {
            return new HotWalletColdTransferOutcome.HotWalletNotConfigured();
        }
        if (addresses.ColdWallet is null)
        {
            return new HotWalletColdTransferOutcome.ColdWalletNotConfigured();
        }

        // Correlation handle the sidecar logs; not persisted backend-side.
        // The ColdWalletTransfer.Id (long IDENTITY) is the durable handle
        // and is only known after the SaveChanges below.
        var correlationId = Guid.NewGuid();

        var sidecarResult = await _sidecar.SendHotToColdTransferAsync(
            new HotToColdTransferRequest(
                ColdTransferId: correlationId,
                ToColdAddress: addresses.ColdWallet,
                Amount: amount.ToString("0.######", CultureInfo.InvariantCulture),
                Token: token.ToString()),
            cancellationToken);

        if (sidecarResult.Status != BlockchainSidecarStatus.Success
            || string.IsNullOrWhiteSpace(sidecarResult.TxHash))
        {
            _logger.LogWarning(
                "Hot→cold transfer rejected by sidecar (status={Status}, token={Token}, amount={Amount}, admin={Admin}).",
                sidecarResult.Status, token, amount, initiatingAdminId);
            return new HotWalletColdTransferOutcome.SidecarUnavailable(
                sidecarResult.Status.ToString());
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transfer = new ColdWalletTransfer
        {
            Amount = amount,
            Token = token,
            FromAddress = addresses.HotWallet,
            ToAddress = addresses.ColdWallet,
            TxHash = sidecarResult.TxHash,
            InitiatedByAdminId = initiatingAdminId,
            CreatedAt = nowUtc,
        };
        _db.Set<ColdWalletTransfer>().Add(transfer);

        // AuditLog written inside the same SaveChanges so the row is
        // atomically visible to reconciliation alongside the ledger entry.
        var payload = JsonSerializer.Serialize(new
        {
            token = token.ToString(),
            amount = amount.ToString("0.######", CultureInfo.InvariantCulture),
            fromAddress = addresses.HotWallet,
            toAddress = addresses.ColdWallet,
            txHash = sidecarResult.TxHash,
        });
        _db.Set<AuditLog>().Add(new AuditLog
        {
            UserId = null,
            ActorId = initiatingAdminId,
            ActorType = ActorType.ADMIN,
            Action = AuditAction.COLD_WALLET_TRANSFER_INITIATED,
            EntityType = nameof(ColdWalletTransfer),
            EntityId = sidecarResult.TxHash,
            OldValue = null,
            NewValue = payload,
            CreatedAt = nowUtc,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Hot→cold transfer accepted: id={Id} token={Token} amount={Amount} tx={TxHash} admin={Admin}.",
            transfer.Id, token, amount, sidecarResult.TxHash, initiatingAdminId);

        return new HotWalletColdTransferOutcome.Success(
            ColdTransferId: transfer.Id,
            TxHash: sidecarResult.TxHash,
            Amount: amount,
            Token: token,
            FromAddress: addresses.HotWallet,
            ToAddress: addresses.ColdWallet);
    }

    /// <summary>
    /// Both addresses are deployment configuration shared with the signer, not
    /// admin-editable settings (05 §3.3, owner decision 2026-09-16): the
    /// sidecar refuses to sign a consolidation to anything but its own
    /// <c>COLD_WALLET_ADDRESS</c>, so a drifted value fails there rather than
    /// sending platform funds to an address someone typed into the panel.
    /// </summary>
    private (string? HotWallet, string? ColdWallet) ReadWalletAddresses() =>
        (_walletAddresses.HotWalletAddress, _walletAddresses.ColdWalletAddress);

}
