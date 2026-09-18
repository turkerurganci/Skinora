using Skinora.Platform.Application.Wallets;

namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Null object for hosts that have no wallet configuration (unit tests, the
/// non-API convenience constructor). Reports both addresses as unset, which is
/// the same state a fresh deployment starts in.
/// </summary>
internal sealed class UnsetPlatformWalletAddresses : IPlatformWalletAddressProvider
{
    public static readonly UnsetPlatformWalletAddresses Instance = new();

    private UnsetPlatformWalletAddresses()
    {
    }

    public string? HotWalletAddress => null;

    public string? ColdWalletAddress => null;
}
