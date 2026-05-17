using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PostCancel;

/// <summary>
/// Startup hook that re-registers every active post-cancel monitor with the
/// blockchain sidecar (T75 — 08 §3.4). Source of truth is
/// <c>PaymentAddress.MonitoringStatus IN (POST_CANCEL_24H, POST_CANCEL_7D,
/// POST_CANCEL_30D)</c>; sidecar memory is replayed from there.
/// </summary>
/// <remarks>
/// <para>
/// Runs once on host start. The hook tolerates sidecar unavailability —
/// each address failure logs at Warning and continues; the catch-up sweep
/// runs again on the next backend restart. Periodic reconciliation
/// (T96 K-future) sits on top of this hook for steady-state drift cases.
/// </para>
/// <para>
/// <c>cancelledAt</c> is reconstructed from
/// <c>MonitoringExpiresAt - StateWindowDuration</c> so the sidecar's
/// window math matches the original cancel anchor even when the recovery
/// runs days after the cancel. <c>initialState</c> is the persisted
/// status — the sidecar honours it verbatim instead of recomputing.
/// </para>
/// </remarks>
public sealed class PostCancelMonitorRecoveryHook : IHostedService
{
    private static readonly TimeSpan Window24h = TimeSpan.FromHours(24);
    private static readonly TimeSpan Window7d = TimeSpan.FromDays(7);
    private static readonly TimeSpan Window30d = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostCancelMonitorRecoveryHook> _logger;

    public PostCancelMonitorRecoveryHook(
        IServiceScopeFactory scopeFactory,
        ILogger<PostCancelMonitorRecoveryHook> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecoverAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Same posture as RefreshTokenCleanupJobRegistrar: never block
            // host start. A failure here means late-payment monitoring is
            // dormant until the next restart or a periodic reconciler.
            _logger.LogError(ex,
                "PostCancelMonitorRecoveryHook failed to replay sidecar registrations.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sidecar = scope.ServiceProvider.GetRequiredService<IBlockchainSidecarClient>();

        var addresses = await db.Set<PaymentAddress>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                && (p.MonitoringStatus == MonitoringStatus.POST_CANCEL_24H
                    || p.MonitoringStatus == MonitoringStatus.POST_CANCEL_7D
                    || p.MonitoringStatus == MonitoringStatus.POST_CANCEL_30D))
            .ToListAsync(cancellationToken);

        if (addresses.Count == 0)
        {
            _logger.LogInformation(
                "PostCancelMonitorRecoveryHook: nothing to recover (no active post-cancel monitors).");
            return;
        }

        int recovered = 0, skipped = 0, failed = 0;
        foreach (var address in addresses)
        {
            if (address.MonitoringExpiresAt is null)
            {
                _logger.LogWarning(
                    "PostCancelMonitorRecoveryHook: PaymentAddress {Id} has POST_CANCEL_* state without MonitoringExpiresAt — skipping.",
                    address.Id);
                skipped++;
                continue;
            }

            var cancelledAt = address.MonitoringExpiresAt.Value - WindowFor(address.MonitoringStatus);
            var contract = KnownStablecoinContracts.ResolveContractAddress(address.ExpectedToken);
            var status = await sidecar.StartPostCancelMonitoringAsync(
                new PostCancelMonitorStartRequest(
                    Address: address.Address,
                    PaymentAddressId: address.Id,
                    TransactionId: address.TransactionId,
                    ExpectedContract: contract,
                    ExpectedSymbol: address.ExpectedToken.ToString(),
                    CancelledAt: cancelledAt,
                    InitialState: address.MonitoringStatus.ToString(),
                    InitialStateExpiresAt: address.MonitoringExpiresAt),
                cancellationToken);

            switch (status)
            {
                case BlockchainSidecarStatus.Success:
                    recovered++;
                    break;
                case BlockchainSidecarStatus.InvalidRequest:
                    _logger.LogWarning(
                        "PostCancelMonitorRecoveryHook: sidecar rejected PaymentAddress {Id} as InvalidRequest — skipping.",
                        address.Id);
                    failed++;
                    break;
                default:
                    _logger.LogWarning(
                        "PostCancelMonitorRecoveryHook: sidecar unavailable for PaymentAddress {Id} (status={Status}) — will retry on next restart.",
                        address.Id, status);
                    failed++;
                    break;
            }
        }

        _logger.LogInformation(
            "PostCancelMonitorRecoveryHook complete: total={Total} recovered={Recovered} skipped={Skipped} failed={Failed}",
            addresses.Count, recovered, skipped, failed);
    }

    private static TimeSpan WindowFor(MonitoringStatus status) => status switch
    {
        MonitoringStatus.POST_CANCEL_24H => Window24h,
        MonitoringStatus.POST_CANCEL_7D => Window7d,
        MonitoringStatus.POST_CANCEL_30D => Window30d,
        _ => Window24h,
    };
}
