using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;

namespace Skinora.Platform.Infrastructure.Configuration;

/// <summary>
/// Reads the platform's pinned wallet addresses from deployment configuration
/// — the same variables each network's signing service pins itself to, so one
/// value in <c>.env</c> feeds both sides and a drift between them fails loudly
/// at the signer rather than silently redirecting funds (05 §3.3, owner
/// decision 2026-09-16).
/// <list type="bullet">
///   <item><description>TRON: <c>HOT_WALLET_ADDRESS</c>, <c>COLD_WALLET_ADDRESS</c>
///   (names predate the network dimension and are kept).</description></item>
///   <item><description>Ethereum (T148): <c>ETHEREUM_HOT_WALLET_ADDRESS</c>,
///   <c>ETHEREUM_COLD_WALLET_ADDRESS</c>.</description></item>
/// </list>
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
/// The TRON check is deliberately structural (base58 alphabet, T-prefix,
/// 34 chars). The signer performs full base58check validation including the
/// checksum; duplicating that here would mean a second base58 implementation
/// in a codebase whose Tron arithmetic lives in the sidecar. The Ethereum check
/// is complete: <c>0x</c> + 40 hex, and a mixed-case value must carry a valid
/// EIP-55 checksum (an all-lowercase value is accepted — it has no checksum to
/// break). The stored form is the canonical lowercase one (06 §1.4).
/// </para>
/// </remarks>
public sealed class EnvPlatformWalletAddressProvider : IPlatformWalletAddressProvider
{
    public const string HotWalletConfigurationKey = "HOT_WALLET_ADDRESS";
    public const string ColdWalletConfigurationKey = "COLD_WALLET_ADDRESS";
    public const string EthereumHotWalletConfigurationKey = "ETHEREUM_HOT_WALLET_ADDRESS";
    public const string EthereumColdWalletConfigurationKey = "ETHEREUM_COLD_WALLET_ADDRESS";

    /// <summary>Documented "not set" sentinel inherited from the old SystemSetting rows.</summary>
    private const string UnconfiguredSentinel = "NONE";

    private static readonly Regex TronAddressPattern =
        new("^T[1-9A-HJ-NP-Za-km-z]{33}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PlatformWalletAddresses _tron;
    private readonly PlatformWalletAddresses _ethereum;

    public EnvPlatformWalletAddressProvider(
        IConfiguration configuration,
        ILogger<EnvPlatformWalletAddressProvider> logger)
    {
        _tron = new PlatformWalletAddresses(
            BlockchainNetwork.TRON,
            ReadTron(configuration, HotWalletConfigurationKey),
            ReadTron(configuration, ColdWalletConfigurationKey));
        _ethereum = new PlatformWalletAddresses(
            BlockchainNetwork.ETHEREUM,
            ReadEvm(configuration, EthereumHotWalletConfigurationKey),
            ReadEvm(configuration, EthereumColdWalletConfigurationKey));

        logger.LogInformation(
            "Platform wallet addresses resolved from configuration — TRON hot: {TronHot}, cold: {TronCold}; "
            + "ETHEREUM hot: {EthHot}, cold: {EthCold}.",
            _tron.HotWalletAddress ?? "(unset)",
            _tron.ColdWalletAddress ?? "(unset)",
            _ethereum.HotWalletAddress ?? "(unset)",
            _ethereum.ColdWalletAddress ?? "(unset)");
    }

    public string? HotWalletAddress => _tron.HotWalletAddress;

    public string? ColdWalletAddress => _tron.ColdWalletAddress;

    public PlatformWalletAddresses For(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => _tron,
        BlockchainNetwork.ETHEREUM => _ethereum,
        _ => new PlatformWalletAddresses(network, null, null),
    };

    private static string? ReadTron(IConfiguration configuration, string key)
    {
        var raw = ReadRaw(configuration, key);
        if (raw is null) return null;

        if (!TronAddressPattern.IsMatch(raw))
        {
            throw new InvalidOperationException(
                $"Startup fail-fast: {key} is not a Tron address ('{raw}'). " +
                "Set it to the platform wallet address used by the blockchain sidecar, " +
                $"or leave it empty / '{UnconfiguredSentinel}' while that wallet does not exist yet.");
        }

        return raw;
    }

    private static string? ReadEvm(IConfiguration configuration, string key)
    {
        var raw = ReadRaw(configuration, key);
        if (raw is null) return null;

        if (EvmAddress.Check(raw) == EvmAddressCheck.Invalid)
        {
            throw new InvalidOperationException(
                $"Startup fail-fast: {key} is not an Ethereum address ('{raw}'). " +
                "Expected 0x followed by 40 hex digits; a mixed-case value must carry a valid " +
                "EIP-55 checksum. Set it to the platform wallet address used by the Ethereum " +
                $"service, or leave it empty / '{UnconfiguredSentinel}' while that wallet does not exist yet.");
        }

        return BlockchainAddress.Canonicalize(raw);
    }

    private static string? ReadRaw(IConfiguration configuration, string key)
    {
        var raw = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (string.Equals(raw, UnconfiguredSentinel, StringComparison.Ordinal)) return null;
        return raw;
    }
}
