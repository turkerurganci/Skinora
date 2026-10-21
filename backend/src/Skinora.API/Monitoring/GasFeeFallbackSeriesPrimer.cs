using Microsoft.Extensions.Hosting;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.API.Monitoring;

/// <summary>
/// T160b düzeltme turu (B2) — builds the <see cref="IGasFeeFallbackObserver"/>
/// singleton when the application starts, so every
/// <c>skinora_backend_gas_fee_fallback_charged_total</c> series is published
/// at 0 from the first scrape on.
/// </summary>
/// <remarks>
/// <see cref="PrometheusGasFeeFallbackObserver"/> creates its series in its
/// constructor, and the only other consumer is <c>ChargedGasFeeResolver</c>
/// (scoped, built on the first refund or payout). A refund priced right after
/// startup — the outbox chain is primed by a hosted service — could then
/// create the series and increment it before Prometheus ever saw 0, and
/// <c>increase()</c> would miss that first fallback. Taking the observer as a
/// constructor dependency is enough: the host builds every hosted service
/// before it starts any of them. Registered in <c>TransactionsModule</c>,
/// ahead of <c>OutboxStartupHook</c>.
/// </remarks>
public sealed class GasFeeFallbackSeriesPrimer : IHostedService
{
    public GasFeeFallbackSeriesPrimer(IGasFeeFallbackObserver observer)
    {
        Observer = observer;
    }

    /// <summary>The observer built at startup (kept for the wiring test).</summary>
    public IGasFeeFallbackObserver Observer { get; }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
