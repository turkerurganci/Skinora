using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// T160b — builds a <see cref="ChargedGasFeeResolver"/> with its collaborators
/// defaulted for tests that only care about the estimate and the settings: a
/// fresh measurement cache, no metrics, and NO refund retry unless a policy is
/// passed (a stub that fails would otherwise make a test wait the real 1 s + 3 s).
/// </summary>
public static class ChargedGasFeeResolverFactory
{
    public static ChargedGasFeeResolver Create(
        IBlockchainServiceRegistry services,
        IBlockchainNetworkSettingsProvider settings,
        GasFeeEstimateRetryPolicy? refundRetry = null,
        TimeProvider? clock = null,
        MeasuredGasFeeCache? measured = null,
        IGasFeeFallbackObserver? observer = null,
        ILogger<ChargedGasFeeResolver>? logger = null)
    {
        var time = clock ?? TimeProvider.System;
        return new ChargedGasFeeResolver(
            services,
            settings,
            measured ?? new MeasuredGasFeeCache(time),
            observer ?? NullGasFeeFallbackObserver.Instance,
            refundRetry ?? GasFeeEstimateRetryPolicy.SingleAttempt,
            time,
            logger ?? NullLogger<ChargedGasFeeResolver>.Instance);
    }
}
