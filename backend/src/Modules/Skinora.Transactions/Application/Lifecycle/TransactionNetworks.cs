using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Wire rules for the network fields of the transaction endpoints (07 §1.3 —
/// T147): <c>acceptedNetworks</c> on <c>POST /transactions</c> and
/// <c>network</c> on <c>POST /transactions/:id/accept</c>.
/// </summary>
public static class TransactionNetworks
{
    /// <summary>
    /// The network a request that names none is read as (öneriyle kaydedildi —
    /// ⚠, T147). Until the network-aware UI (T156) sends the field, every
    /// existing client creates and accepts TRON transactions exactly as before
    /// — the same default T146 gave the wallet endpoints.
    /// </summary>
    public const BlockchainNetwork Default = BlockchainNetwork.TRON;

    private static readonly IReadOnlyList<string> Names = Enum.GetNames<BlockchainNetwork>();

    /// <summary>
    /// Parses one wire value. Case-sensitive on purpose — the value is an enum
    /// name (<c>"TRON"</c> / <c>"ETHEREUM"</c>), and accepting <c>"tron"</c>
    /// would let two spellings of the same network into the records.
    /// </summary>
    public static bool TryParse(string? value, out BlockchainNetwork network)
    {
        network = default;
        if (string.IsNullOrEmpty(value) || !Names.Contains(value, StringComparer.Ordinal))
            return false;

        network = Enum.Parse<BlockchainNetwork>(value);
        return true;
    }

    /// <summary>
    /// Parses <c>acceptedNetworks</c>: omitted → <see cref="Default"/>; empty,
    /// duplicate or unknown entries are a validation error. The result is in
    /// enum order (TRON first) so every per-network check reports the same
    /// network first whatever order the client sent.
    /// </summary>
    public static bool TryParseAccepted(
        IReadOnlyList<string>? values,
        out IReadOnlyList<BlockchainNetwork> networks,
        out string? error)
    {
        networks = [];
        error = null;

        if (values is null)
        {
            networks = [Default];
            return true;
        }

        if (values.Count == 0)
        {
            error = "acceptedNetworks must name at least one network.";
            return false;
        }

        var parsed = new List<BlockchainNetwork>(values.Count);
        foreach (var value in values)
        {
            if (!TryParse(value, out var network))
            {
                error = $"acceptedNetworks contains an unknown network; allowed: {string.Join(", ", Names)}.";
                return false;
            }

            if (parsed.Contains(network))
            {
                error = $"acceptedNetworks lists {network} more than once.";
                return false;
            }

            parsed.Add(network);
        }

        parsed.Sort();
        networks = parsed;
        return true;
    }

    /// <summary>
    /// 02 §12.3 payout-address cooldown, per network (T146): the address on
    /// one network is unusable for new listings while it is younger than
    /// <c>wallet.payout_address_cooldown_hours</c>. A row without a change
    /// time (migrated, pre-change-tracking address) carries no cooldown.
    /// </summary>
    public static bool IsPayoutCooldownActive(
        DateTime? payoutAddressChangedAt, int? cooldownHours, DateTime nowUtc)
    {
        if (!cooldownHours.HasValue || !payoutAddressChangedAt.HasValue) return false;
        return nowUtc - payoutAddressChangedAt.Value < TimeSpan.FromHours(cooldownHours.Value);
    }
}
