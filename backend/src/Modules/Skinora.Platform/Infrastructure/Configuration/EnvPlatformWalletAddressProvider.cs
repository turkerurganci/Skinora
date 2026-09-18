using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Wallets;

namespace Skinora.Platform.Infrastructure.Configuration;

/// <summary>
/// Reads the platform's pinned wallet addresses from deployment configuration
/// (<c>HOT_WALLET_ADDRESS</c>, <c>COLD_WALLET_ADDRESS</c>) — the same two env
/// vars the blockchain sidecar signs against, so one value in <c>.env</c> feeds
/// both services and a drift between them fails loudly at the signer rather
/// than silently redirecting funds (05 §3.3, owner decision 2026-09-16).
/// </summary>
/// <remarks>
/// <para>
/// Malformed input is a startup failure, not a runtime surprise: the
/// constructor throws and <c>SettingsBootstrapHook</c> resolves this provider
/// while it proves configuration, so the host stops before serving traffic.
/// Unset stays legal — a fresh environment has no wallet yet, and each caller
/// already degrades safely (see <see cref="IPlatformWalletAddressProvider"/>).
/// </para>
/// <para>
/// The format check is deliberately structural (base58 alphabet, T-prefix,
/// 34 chars). The signer performs full base58check validation including the
/// checksum; duplicating that here would mean a second base58 implementation
/// in a codebase whose Tron arithmetic lives in the sidecar.
/// </para>
/// </remarks>
public sealed class EnvPlatformWalletAddressProvider : IPlatformWalletAddressProvider
{
    public const string HotWalletConfigurationKey = "HOT_WALLET_ADDRESS";
    public const string ColdWalletConfigurationKey = "COLD_WALLET_ADDRESS";

    /// <summary>Documented "not set" sentinel inherited from the old SystemSetting rows.</summary>
    private const string UnconfiguredSentinel = "NONE";

    private static readonly Regex TronAddressPattern =
        new("^T[1-9A-HJ-NP-Za-km-z]{33}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public EnvPlatformWalletAddressProvider(
        IConfiguration configuration,
        ILogger<EnvPlatformWalletAddressProvider> logger)
    {
        HotWalletAddress = Read(configuration, HotWalletConfigurationKey);
        ColdWalletAddress = Read(configuration, ColdWalletConfigurationKey);

        logger.LogInformation(
            "Platform wallet addresses resolved from configuration — hot: {Hot}, cold: {Cold}.",
            HotWalletAddress ?? "(unset)",
            ColdWalletAddress ?? "(unset)");
    }

    public string? HotWalletAddress { get; }

    public string? ColdWalletAddress { get; }

    private static string? Read(IConfiguration configuration, string key)
    {
        var raw = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (string.Equals(raw, UnconfiguredSentinel, StringComparison.Ordinal)) return null;

        if (!TronAddressPattern.IsMatch(raw))
        {
            throw new InvalidOperationException(
                $"Startup fail-fast: {key} is not a Tron address ('{raw}'). " +
                "Set it to the platform wallet address used by the blockchain sidecar, " +
                $"or leave it empty / '{UnconfiguredSentinel}' while that wallet does not exist yet.");
        }

        return raw;
    }
}
