using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.AdminWallets;

/// <summary>
/// AD38 — <c>GET /admin/wallets</c> (T157, 07 §9.33): the platform's wallets,
/// balances and transfers per network. Every network gets its own block; a
/// network's balance never stands in for another's (11 §F8 K3).
/// </summary>
public interface IAdminWalletOverviewService
{
    Task<WalletOverviewDto> GetAsync(CancellationToken cancellationToken);
}

public sealed record WalletOverviewDto(IReadOnlyList<NetworkWalletsDto> Networks);

/// <param name="ServiceConfigured">False = no blockchain service for the network: nothing can be sent or read.</param>
/// <param name="Degraded">The health probe sees the service in an outage.</param>
/// <param name="NativeAsset">TRX / ETH — the asset that pays the network fee.</param>
/// <param name="BalancesStatus"><c>OK</c>, <c>UNAVAILABLE</c> (service did not answer) or <c>NOT_CONFIGURED</c>.</param>
/// <param name="BalancesBlock">Block (finalized / solidified) the balances were read at.</param>
public sealed record NetworkWalletsDto(
    BlockchainNetwork Network,
    bool ServiceConfigured,
    bool Degraded,
    string NativeAsset,
    string BalancesStatus,
    long? BalancesBlock,
    IReadOnlyList<PlatformWalletDto> Wallets,
    OutboundQueueDto Outbound,
    IReadOnlyList<ColdTransferDto> RecentColdTransfers,
    IReadOnlyList<OutboundRowDto> RecentOutbound);

/// <param name="Role"><c>HOT</c>, <c>COLD</c> or <c>FUEL</c>.</param>
/// <param name="Address">Display form (Ethereum: EIP-55); null = not configured / not provisioned.</param>
/// <param name="AddressMismatch">The backend's configured hot address differs from the one the service signs with.</param>
/// <param name="NativeFloor">The wallet's native-asset alarm floor (decimal string), when it has one.</param>
public sealed record PlatformWalletDto(
    string Role,
    string? Address,
    string? ConfiguredAddress,
    bool AddressMismatch,
    IReadOnlyList<WalletBalanceDto>? Balances,
    string? NativeFloor,
    bool? BelowNativeFloor);

public sealed record WalletBalanceDto(string Symbol, string Amount);

/// <param name="PendingCount">Outbound rows waiting to be sent (PENDING).</param>
/// <param name="OldestPendingAt">Creation time of the oldest of them.</param>
/// <param name="InFlightCount">Sent, waiting for finality (DETECTED).</param>
public sealed record OutboundQueueDto(int PendingCount, DateTime? OldestPendingAt, int InFlightCount);

public sealed record ColdTransferDto(
    long Id,
    StablecoinType Token,
    decimal Amount,
    string FromAddress,
    string ToAddress,
    string TxHash,
    string InitiatedBy,
    DateTime CreatedAt);

public sealed record OutboundRowDto(
    Guid Id,
    Guid TransactionId,
    BlockchainTransactionType Type,
    BlockchainTransactionStatus Status,
    StablecoinType Token,
    decimal Amount,
    string ToAddress,
    string? TxHash,
    int RetryCount,
    DateTime CreatedAt);

public sealed class AdminWalletOverviewService : IAdminWalletOverviewService
{
    private const int StablecoinDecimals = 6;
    private const int RecentCount = 10;
    private const int RecentOutboundCount = 20;

    private static readonly string[] Stablecoins = ["USDT", "USDC"];

    private static readonly BlockchainTransactionType[] OutboundTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
        BlockchainTransactionType.SWEEP,
    ];

    private readonly AppDbContext _db;
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkHealth _health;
    private readonly IPlatformWalletAddressProvider _addresses;
    private readonly IBlockchainNetworkSettingsProvider _settings;
    private readonly ILogger<AdminWalletOverviewService> _logger;

    public AdminWalletOverviewService(
        AppDbContext db,
        IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health,
        IPlatformWalletAddressProvider addresses,
        IBlockchainNetworkSettingsProvider settings,
        ILogger<AdminWalletOverviewService> logger)
    {
        _db = db;
        _services = services;
        _health = health;
        _addresses = addresses;
        _settings = settings;
        _logger = logger;
    }

    public async Task<WalletOverviewDto> GetAsync(CancellationToken cancellationToken)
    {
        var networks = new List<NetworkWalletsDto>();

        // One network at a time for the database (one context), but the two
        // services are asked in parallel by reading the remote parts first.
        var remote = Enum.GetValues<BlockchainNetwork>()
            .ToDictionary(n => n, n => ReadRemoteAsync(n, cancellationToken));
        await Task.WhenAll(remote.Values);

        foreach (var network in Enum.GetValues<BlockchainNetwork>())
        {
            networks.Add(await BuildAsync(network, await remote[network], cancellationToken));
        }

        return new WalletOverviewDto(networks);
    }

    // ----------------------------------------------------------------- remote

    private sealed record Remote(
        BlockchainSidecarPlatformWalletsResult? ServiceWallets,
        BlockchainSidecarBalancesResult? Balances,
        IReadOnlyList<string> Queried);

    private async Task<Remote> ReadRemoteAsync(BlockchainNetwork network, CancellationToken cancellationToken)
    {
        if (!_services.IsConfigured(network)) return new Remote(null, null, []);

        var sidecar = _services.Sidecar(network);
        var pair = _addresses.For(network);

        BlockchainSidecarPlatformWalletsResult? serviceWallets = null;
        try
        {
            serviceWallets = await sidecar.GetPlatformWalletsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Platform wallet lookup on {Network} failed.", network);
        }

        var queried = new List<string>();
        foreach (var address in new[]
        {
            pair.HotWalletAddress,
            pair.ColdWalletAddress,
            serviceWallets?.Status == BlockchainSidecarStatus.Success ? serviceWallets.FuelWalletAddress : null,
        })
        {
            if (address is not null && !queried.Contains(address, BlockchainAddress.Comparer))
                queried.Add(address);
        }

        BlockchainSidecarBalancesResult? balances = null;
        if (queried.Count > 0)
        {
            try
            {
                balances = await sidecar.GetWalletBalancesAsync(queried, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Balance read on {Network} failed.", network);
            }
        }

        return new Remote(serviceWallets, balances, queried);
    }

    // ------------------------------------------------------------------ build

    private async Task<NetworkWalletsDto> BuildAsync(
        BlockchainNetwork network, Remote remote, CancellationToken cancellationToken)
    {
        var policy = BlockchainNetworkPolicy.For(network);
        var configured = _services.IsConfigured(network);
        var settings = await _settings.GetAsync(network, cancellationToken);
        var pair = _addresses.For(network);

        var balancesStatus = !configured
            ? "NOT_CONFIGURED"
            : remote.Balances is { Status: BlockchainSidecarStatus.Success, Balances: not null }
                ? "OK"
                : remote.Queried.Count == 0 ? "NOT_CONFIGURED" : "UNAVAILABLE";

        var wallets = new List<PlatformWalletDto>
        {
            Wallet("HOT", pair.HotWalletAddress, pair.HotWalletAddress, policy, remote,
                settings.HotWalletNativeBalanceMinimum),
            Wallet("COLD", pair.ColdWalletAddress, pair.ColdWalletAddress, policy, remote, null),
        };

        // The service's own view of its hot wallet: a difference from the
        // backend's configuration means someone is looking at the wrong address.
        if (remote.ServiceWallets is { Status: BlockchainSidecarStatus.Success } sw)
        {
            if (sw.HotWalletAddress is not null && pair.HotWalletAddress is not null
                && !BlockchainAddress.AreEqual(sw.HotWalletAddress, pair.HotWalletAddress))
            {
                wallets[0] = wallets[0] with { AddressMismatch = true };
            }

            if (policy.Network == BlockchainNetwork.ETHEREUM)
            {
                wallets.Add(Wallet("FUEL", sw.FuelWalletAddress, null, policy, remote,
                    settings.FuelWalletNativeBalanceMinimum));
            }
        }
        else if (policy.Network == BlockchainNetwork.ETHEREUM && configured)
        {
            // The fuel wallet's address comes from the service only; when the
            // service did not answer, say so instead of omitting the wallet.
            wallets.Add(new PlatformWalletDto("FUEL", null, null, false, null,
                settings.FuelWalletNativeBalanceMinimum?.ToString("0.##################", CultureInfo.InvariantCulture),
                null));
        }

        var pending = await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters()
            .Where(b => b.Network == network && OutboundTypes.Contains(b.Type)
                && b.Status == BlockchainTransactionStatus.PENDING)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min(b => b.CreatedAt) })
            .FirstOrDefaultAsync(cancellationToken);
        var inFlight = await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters()
            .CountAsync(b => b.Network == network && OutboundTypes.Contains(b.Type)
                && b.Status == BlockchainTransactionStatus.DETECTED, cancellationToken);

        var cold = await (
            from c in _db.Set<ColdWalletTransfer>().AsNoTracking()
            where c.Network == network
            join u in _db.Set<User>().AsNoTracking().IgnoreQueryFilters()
                on c.InitiatedByAdminId equals u.Id into admins
            from admin in admins.DefaultIfEmpty()
            orderby c.CreatedAt descending
            select new ColdTransferDto(
                c.Id, c.Token, c.Amount, c.FromAddress, c.ToAddress, c.TxHash,
                admin == null ? string.Empty : admin.SteamDisplayName, c.CreatedAt))
            .Take(RecentCount)
            .ToListAsync(cancellationToken);

        var recent = await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters()
            .Where(b => b.Network == network && OutboundTypes.Contains(b.Type))
            .OrderByDescending(b => b.CreatedAt)
            .Take(RecentOutboundCount)
            .Select(b => new OutboundRowDto(
                b.Id, b.TransactionId, b.Type, b.Status, b.Token, b.Amount, b.ToAddress,
                b.TxHash, b.RetryCount, b.CreatedAt))
            .ToListAsync(cancellationToken);

        return new NetworkWalletsDto(
            network,
            configured,
            configured && _health.IsDegraded(network),
            policy.NativeAssetSymbol,
            balancesStatus,
            remote.Balances?.BlockNumber,
            wallets,
            new OutboundQueueDto(pending?.Count ?? 0, pending?.Oldest, inFlight),
            cold,
            recent);
    }

    private static PlatformWalletDto Wallet(
        string role,
        string? address,
        string? configured,
        BlockchainNetworkPolicy policy,
        Remote remote,
        decimal? nativeFloor)
    {
        IReadOnlyList<WalletBalanceDto>? balances = null;
        bool? below = null;

        if (address is not null && remote.Balances is { Status: BlockchainSidecarStatus.Success, Balances: not null } snapshot)
        {
            var row = snapshot.Balances.FirstOrDefault(b => BlockchainAddress.AreEqual(b.Address, address));
            if (row is not null)
            {
                var list = new List<WalletBalanceDto>();
                var native = Scale(row.Tokens, policy.NativeAssetSymbol, policy.NativeAssetDecimals);
                if (native is not null)
                {
                    list.Add(new WalletBalanceDto(
                        policy.NativeAssetSymbol, Format(native.Value, policy.NativeAssetDecimals)));
                    if (nativeFloor is { } floor) below = native.Value < floor;
                }
                foreach (var symbol in Stablecoins)
                {
                    var amount = Scale(row.Tokens, symbol, StablecoinDecimals);
                    if (amount is not null)
                        list.Add(new WalletBalanceDto(symbol, Format(amount.Value, StablecoinDecimals)));
                }
                balances = list;
            }
        }

        return new PlatformWalletDto(
            role,
            BlockchainAddress.ToDisplay(address),
            BlockchainAddress.ToDisplay(configured),
            AddressMismatch: false,
            balances,
            nativeFloor?.ToString("0.##################", CultureInfo.InvariantCulture),
            below);
    }

    /// <summary>Raw integer base units (a decimal string) to a decimal amount; null when missing or not an integer.</summary>
    private static decimal? Scale(IReadOnlyDictionary<string, string> tokens, string symbol, int decimals)
    {
        if (!tokens.TryGetValue(symbol, out var raw) || string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var units)) return null;
        var divisor = 1m;
        for (var i = 0; i < decimals; i++) divisor *= 10m;
        return units / divisor;
    }

    private static string Format(decimal value, int decimals) =>
        value.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
}
