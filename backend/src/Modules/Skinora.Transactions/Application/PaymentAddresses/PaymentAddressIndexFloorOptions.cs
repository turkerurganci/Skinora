using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// T172a — per-network floor for the next deposit HD index (DEPLOY_RUNBOOK
/// §N.8 (b), DEFERRED_BACKLOG <c>PostRestoreChainReconciliationTooling</c>).
/// ⚠ PARA.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> <see cref="PaymentAddressAllocator"/> takes the next index as
/// <c>MAX(HdWalletIndex) + 1</c> of the network. A database restored from a
/// backup does not know the indices handed out after the backup, so without a
/// floor it hands the same deposit addresses to new transactions; a late
/// payment of the lost transaction would then be credited to the new one.
/// </para>
/// <para>
/// <b>Rule.</b> next index = <c>max(MAX(HdWalletIndex) + 1, floor)</c>. The
/// floor only ever raises the index: a floor at or below the current maximum
/// changes nothing, so a stale or mistyped (too small) floor can never cause an
/// index to be reused. Skipped indices are simply never derived.
/// </para>
/// <para>
/// <b>Where it lives.</b> Deployment configuration, not a <c>SystemSetting</c>
/// row: a database restore must not be able to rewind it, and it must be in
/// place before the backend's first minute (<c>EnsurePaymentAddressJob</c>
/// allocates for accepted transactions on start). Environment:
/// <c>PaymentAddresses__MinimumNextIndex__TRON</c>,
/// <c>PaymentAddresses__MinimumNextIndex__ETHEREUM</c>. A network that is not
/// listed has no floor.
/// </para>
/// </remarks>
public sealed class PaymentAddressIndexFloorOptions
{
    public const string SectionName = "PaymentAddresses";

    /// <summary>The largest non-hardened BIP-32 child index (2^31 − 1).</summary>
    public const int MaxDerivableIndex = int.MaxValue;

    /// <summary>Network name (<c>TRON</c>, <c>ETHEREUM</c>) → the smallest index the next deposit may get.</summary>
    public Dictionary<string, int> MinimumNextIndex { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The floor of <paramref name="network"/>; 0 (no floor) when unset.</summary>
    public int FloorFor(BlockchainNetwork network)
    {
        foreach (var (key, value) in MinimumNextIndex)
        {
            if (string.Equals(key, network.ToString(), StringComparison.OrdinalIgnoreCase)) return value;
        }
        return 0;
    }

    /// <summary>
    /// Configuration errors (fail fast at start-up): an unknown network name or
    /// a negative floor. Empty when the configuration is usable.
    /// </summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        foreach (var (key, value) in MinimumNextIndex)
        {
            if (!Enum.TryParse<BlockchainNetwork>(key, ignoreCase: true, out var parsed)
                || !string.Equals(parsed.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{SectionName}:MinimumNextIndex:{key} — unknown network (expected TRON or ETHEREUM).");
            }
            if (value < 0)
            {
                errors.Add($"{SectionName}:MinimumNextIndex:{key} = {value} — must be 0 or greater.");
            }
        }
        return errors;
    }

    /// <summary>
    /// The index the allocator uses: <c>max(currentMax + 1, floor)</c>
    /// (<paramref name="currentMax"/> null = the network has no deposit yet).
    /// </summary>
    public static int NextIndex(int? currentMax, int floor) =>
        Math.Max((currentMax ?? -1) + 1, Math.Max(0, floor));
}
