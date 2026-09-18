using Skinora.Platform.Application.Wallets;

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
}
