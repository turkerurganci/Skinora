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
/// listed, or listed with an empty value, has no floor.
/// </para>
/// <para>
/// <b>Strict parsing (T172a doğrulama F1, ⚠ PARA).</b> The values are bound as
/// text and parsed here, not by the configuration binder: the binder silently
/// drops a dictionary entry it cannot convert to <c>int</c>, so <c>1.000</c>,
/// <c>1,000</c>, <c>1e4</c> or <c>99999999999</c> used to start the backend
/// with no floor at all. A value must be ASCII digits only (no sign, no
/// separator, no spaces) and fit an <c>int</c>; anything else is an
/// <see cref="Errors"/> entry and the backend does not start
/// (<c>ValidateOnStart</c>, TransactionsModule). The effective floor of each
/// network is logged once at start-up
/// (<see cref="PaymentAddressIndexFloorStartupLog"/>).
/// </para>
/// </remarks>
public sealed class PaymentAddressIndexFloorOptions
{
    public const string SectionName = "PaymentAddresses";

    /// <summary>
    /// Network name (<c>TRON</c>, <c>ETHEREUM</c>) → the smallest index the
    /// next deposit may get, as written in the configuration (digits only).
    /// </summary>
    public Dictionary<string, string?> MinimumNextIndex { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The floor of <paramref name="network"/>; 0 (no floor) when unset or
    /// empty. Throws on a value <see cref="Errors"/> refuses — unreachable in a
    /// started host (validated at start-up), and never a silent 0.
    /// </summary>
    public int FloorFor(BlockchainNetwork network)
    {
        foreach (var (key, raw) in MinimumNextIndex)
        {
            if (!string.Equals(key, network.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            if (IsUnset(raw)) return 0;
            return TryParseFloor(raw, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"{SectionName}:MinimumNextIndex:{key} = '{raw}' is not a whole number (digits only, e.g. 1000).");
        }
        return 0;
    }

    /// <summary>
    /// Configuration errors (fail fast at start-up): an unknown network name or
    /// a value that is not ASCII digits fitting an <c>int</c>. Empty when the
    /// configuration is usable.
    /// </summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        foreach (var (key, raw) in MinimumNextIndex)
        {
            if (!Enum.TryParse<BlockchainNetwork>(key, ignoreCase: true, out var parsed)
                || !string.Equals(parsed.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{SectionName}:MinimumNextIndex:{key} — unknown network (expected TRON or ETHEREUM).");
            }
            if (!IsUnset(raw) && !TryParseFloor(raw, out _))
            {
                errors.Add(
                    $"{SectionName}:MinimumNextIndex:{key} = '{raw}' — must be a whole number written with digits only "
                    + $"(no sign, no thousands separator, no exponent; at most {int.MaxValue}), e.g. 1000 not 1.000.");
            }
        }
        return errors;
    }

    /// <summary>
    /// Strict floor parser: ASCII digits only, within <c>int</c>. No sign,
    /// separator, whitespace, exponent or culture.
    /// </summary>
    public static bool TryParseFloor(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        foreach (var c in raw)
        {
            if (c is < '0' or > '9') return false;
        }
        return int.TryParse(raw, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool IsUnset(string? raw) => string.IsNullOrWhiteSpace(raw);

    /// <summary>
    /// The index the allocator uses: <c>max(currentMax + 1, floor)</c>
    /// (<paramref name="currentMax"/> null = the network has no deposit yet).
    /// Throws when <paramref name="currentMax"/> is already the last
    /// non-hardened index (2^31 − 1): there is no next index, and wrapping
    /// around would hand out index 0 again (T172a doğrulama S4-5).
    /// </summary>
    public static int NextIndex(int? currentMax, int floor)
    {
        var next = Math.Max((long)(currentMax ?? -1) + 1, Math.Max(0, floor));
        return next <= int.MaxValue
            ? (int)next
            : throw new InvalidOperationException(
                "The network's deposit index space is exhausted (MAX(HdWalletIndex) = 2^31 − 1); no index is reused.");
    }
}
