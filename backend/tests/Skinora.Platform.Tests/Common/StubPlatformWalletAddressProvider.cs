using Skinora.Platform.Application.Wallets;

namespace Skinora.Platform.Tests.Common;

/// <summary>
/// Mutable stand-in for the configuration-backed
/// <see cref="IPlatformWalletAddressProvider"/>. The platform's own wallet
/// addresses stopped being admin-editable SystemSetting rows in the 2026-09-16
/// custody round; the settings service reads them from here instead.
/// </summary>
public sealed class StubPlatformWalletAddressProvider : IPlatformWalletAddressProvider
{
    public string? HotWalletAddress { get; set; }

    public string? ColdWalletAddress { get; set; }
}
