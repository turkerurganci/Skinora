using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Pricing;

/// <summary>
/// Fail-open <see cref="IMarketPriceProvider"/> that returns <c>null</c> for
/// every lookup, which the fraud pre-check interprets as "no market signal" —
/// the transaction proceeds as <c>CREATED</c> instead of <c>FLAGGED</c>
/// (02 §14.4 only flags when an actual deviation exceeds the threshold).
/// </summary>
/// <remarks>
/// WP4a wired the production binding to <c>PriceServiceMarketPriceProvider</c>
/// (Skinora.Fraud), which bridges to the T81 Steam Market price stack. This
/// null provider remains as the explicit no-signal fallback and the default in
/// unit tests that do not exercise the price-deviation rule.
/// </remarks>
public sealed class NullMarketPriceProvider : IMarketPriceProvider
{
    public Task<decimal?> TryGetMarketPriceAsync(
        string marketHashName,
        StablecoinType denomination,
        CancellationToken cancellationToken)
        => Task.FromResult<decimal?>(null);
}
