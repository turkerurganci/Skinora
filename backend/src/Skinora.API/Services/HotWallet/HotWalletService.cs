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
using Skinora.Transactions.Application.Networks;
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
/// <remarks>
/// T148 — a consolidation runs on one network: its service signs, its hot/cold
/// pair is the source and the pinned destination, and the ledger row carries
/// it. T157 — AD39 names the network (07 §1.3 "Admin", §9.34); Ethereum moves USDT
/// only (11 §F8 K2) and its service needs the platform's fee cap
/// (<c>blockchain.ethereum.fee_cap_usd_per_transfer</c>, 08 §3.7.7), read here
/// at the moment of the request. When the service waits (fee above the cap, no
/// ETH price, no gas) nothing is signed and nothing is written — the admin
/// tries again later.
/// </remarks>
public sealed class HotWalletService : IHotWalletService
{

    private const int AmountScale = 6;
    private static readonly decimal AmountScaleQuantum = 0.000001m;

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IPlatformWalletAddressProvider _walletAddresses;
    private readonly IBlockchainNetworkSettingsProvider _networkSettings;
    private readonly TimeProvider _clock;
    private readonly ILogger<HotWalletService> _logger;

    public HotWalletService(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IPlatformWalletAddressProvider walletAddresses,
        IBlockchainNetworkSettingsProvider networkSettings,
        TimeProvider clock,
        ILogger<HotWalletService> logger)
    {
        _db = db;
        _services = services;
        _walletAddresses = walletAddresses;
        _networkSettings = networkSettings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<HotWalletColdTransferOutcome> InitiateColdTransferAsync(
        BlockchainNetwork network,
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

        // Ethereum moves USDT only (11 §F8 K2); TRON keeps both stablecoins.
        if (network == BlockchainNetwork.ETHEREUM && token != StablecoinType.USDT)
        {
            return new HotWalletColdTransferOutcome.TokenNotSupported(network, token);
        }

        // Fail closed per network (05 §3.3a): no service, nothing is sent.
        if (!_services.IsConfigured(network))
        {
            return new HotWalletColdTransferOutcome.NetworkNotConfigured(network);
        }

        var addresses = ReadWalletAddresses(network);
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

        // Ethereum's service refuses a cold-wallet transfer without the fee
        // cap (08 §3.7.7). No cap configured = nothing signed, same as the
        // service's own "no cap, no send".
        string? feeCapUsd = null;
        if (network == BlockchainNetwork.ETHEREUM)
        {
            var settings = await _networkSettings.GetAsync(network, cancellationToken);
            if (settings.FeeCapUsdPerTransfer is not { } cap || cap <= 0m)
            {
                return new HotWalletColdTransferOutcome.Deferred("FEE_CAP_NOT_CONFIGURED");
            }
            feeCapUsd = cap.ToString("0.######", CultureInfo.InvariantCulture);
        }

        var sidecarResult = await _services.Sidecar(network).SendHotToColdTransferAsync(
            new HotToColdTransferRequest(
                ColdTransferId: correlationId,
                ToColdAddress: addresses.ColdWallet,
                Amount: amount.ToString("0.######", CultureInfo.InvariantCulture),
                Token: token.ToString(),
                FeeCapUsd: feeCapUsd),
            cancellationToken);

        if (sidecarResult.Status == BlockchainSidecarStatus.Deferred)
        {
            _logger.LogWarning(
                "Hot→cold transfer on {Network} not signed for now ({Reason}); token={Token}, amount={Amount}, admin={Admin}.",
                network, sidecarResult.Reason, token, amount, initiatingAdminId);
            return new HotWalletColdTransferOutcome.Deferred(sidecarResult.Reason ?? "DEFERRED");
        }

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
            // 06 §1.4 (T145) / T148 — the ledger row carries the network the
            // consolidation ran on.
            Network = network,
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
            network = network.ToString(),
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
            "Hot→cold transfer accepted on {Network}: id={Id} token={Token} amount={Amount} tx={TxHash} admin={Admin}.",
            network, transfer.Id, token, amount, sidecarResult.TxHash, initiatingAdminId);

        return new HotWalletColdTransferOutcome.Success(
            ColdTransferId: transfer.Id,
            TxHash: sidecarResult.TxHash,
            Amount: amount,
            Token: token,
            FromAddress: addresses.HotWallet,
            ToAddress: addresses.ColdWallet,
            Network: network);
    }

    /// <summary>
    /// Both addresses are deployment configuration shared with the signer, not
    /// admin-editable settings (05 §3.3, owner decision 2026-09-16): the
    /// sidecar refuses to sign a consolidation to anything but its own
    /// <c>COLD_WALLET_ADDRESS</c>, so a drifted value fails there rather than
    /// sending platform funds to an address someone typed into the panel.
    /// </summary>
    private (string? HotWallet, string? ColdWallet) ReadWalletAddresses(BlockchainNetwork network)
    {
        var pair = _walletAddresses.For(network);
        return (pair.HotWalletAddress, pair.ColdWalletAddress);
    }

}
