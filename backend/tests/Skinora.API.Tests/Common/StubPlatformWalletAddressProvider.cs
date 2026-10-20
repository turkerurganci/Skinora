using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;

namespace Skinora.API.Tests.Common;

/// <summary>
/// Mutable stand-in for the configuration-backed
/// <see cref="IPlatformWalletAddressProvider"/>. The platform's own addresses
/// stopped being admin-editable SystemSetting rows in the 2026-09-16 custody
/// round, so fixtures assign them here instead of seeding rows.
/// </summary>
public sealed class StubPlatformWalletAddressProvider : IPlatformWalletAddressProvider
{
    public string? HotWalletAddress { get; set; }

    public string? ColdWalletAddress { get; set; }

    /// <summary>T157 — a network's own pair; a network without an entry keeps the interface default.</summary>
    public Dictionary<BlockchainNetwork, PlatformWalletAddresses> Pairs { get; } = new();

    public PlatformWalletAddresses For(BlockchainNetwork network) =>
        Pairs.TryGetValue(network, out var pair)
            ? pair
            : network == BlockchainNetwork.TRON
                ? new PlatformWalletAddresses(network, HotWalletAddress, ColdWalletAddress)
                : new PlatformWalletAddresses(network, null, null);
}
