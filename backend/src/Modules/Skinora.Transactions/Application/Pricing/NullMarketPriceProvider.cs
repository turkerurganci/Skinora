using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Pricing;

/// <summary>
/// Default <see cref="IMarketPriceProvider"/> registered until T81 wires the
/// Steam Market Price API. Returns <c>null</c> for every lookup, which the
/// fraud pre-check interprets as "no market signal" — the transaction
/// proceeds as <c>CREATED</c> instead of <c>FLAGGED</c>. The doc-supported
/// behavior is equivalent: 02 §14.4 only flags when an actual deviation
/// exceeds the configured threshold.
/// </summary>
public sealed class NullMarketPriceProvider : IMarketPriceProvider
{
    public Task<decimal?> TryGetMarketPriceAsync(
        string itemClassId,
        string? itemInstanceId,
        StablecoinType denomination,
        CancellationToken cancellationToken)
        => Task.FromResult<decimal?>(null);
}
