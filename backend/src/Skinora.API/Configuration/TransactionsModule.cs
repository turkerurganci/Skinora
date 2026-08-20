using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skinora.Admin.Application.Users;
using Skinora.API.BackgroundJobs;
using Skinora.API.Services;
using Skinora.API.Services.HotWallet;
using Skinora.Platform.Application.Settings;
using Skinora.Shared.BackgroundJobs;
using Skinora.Fraud.Application.Account;
using Skinora.Fraud.Application.Pricing;
using Skinora.Transactions.Application.Admin;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PaymentMonitoring;
using Skinora.Transactions.Application.PayoutIssues;
using Skinora.API.Services.Reconciliation;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Application.Pricing;
using Skinora.Transactions.Application.Reconciliation;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Application.Wallets;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Transactions module — T45 lifecycle
/// services (eligibility, params, creation), T67/T81 forward-deferred
/// stub ports (Steam inventory, market price), and the cross-module
/// glue that wires <c>IAccountFlagChecker</c> implemented in
/// <c>Skinora.Fraud</c> against the port declared inside
/// <c>Skinora.Transactions</c>.
/// </summary>
public static class TransactionsModule
{
    public static IServiceCollection AddTransactionsModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        // T47 — timeout scheduling tunables (poll interval, scanner batch,
        // recovery threshold). Operational config, not SystemSettings.
        services.Configure<TimeoutSchedulingOptions>(
            configuration.GetSection(TimeoutSchedulingOptions.SectionName));

        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        // T45 — lifecycle services (07 §7.1–§7.4).
        services.AddScoped<ITransactionLimitsProvider, TransactionLimitsProvider>();
        services.AddScoped<ITransactionParamsService, TransactionParamsService>();
        services.AddScoped<ITransactionEligibilityService, TransactionEligibilityService>();
        services.AddScoped<IFraudPreCheckService, FraudPreCheckService>();
        services.AddScoped<ITransactionCreationService, TransactionCreationService>();
        services.AddSingleton<IInvitationCodeGenerator, InvitationCodeGenerator>();

        // T46 — detail + accept (07 §7.5–§7.6).
        services.AddScoped<ITransactionDetailService, TransactionDetailService>();
        services.AddScoped<ITransactionAcceptanceService, TransactionAcceptanceService>();

        // T123 — seller readiness confirmation (07 §7.6a, 03 §2.3). The gate
        // that opens the payment window and takes the 02 §9.2 delivery baseline.
        services.AddScoped<ITransactionReadinessService, TransactionReadinessService>();

        // T125 — the 02 §9.2 delivery evidence engine. Side-effect free by
        // contract (reads two inventories + the launch-gate setting, returns a
        // verdict), so it is safe to call repeatedly. Its consumers land later:
        // T126 confirm-receipt, T127 the scanner's pre-timeout verification
        // round, T130 the dispute auto-checker.
        services.AddScoped<IDeliveryVerificationService, DeliveryVerificationService>();

        // T126 — buyer receipt confirmation (07 §7.6b, 03 §3.5). The first
        // production caller of TransactionTrigger.DeliverItem, and so far the
        // only way out of PAYMENT_RECEIVED that is not a cancellation.
        services.AddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();

        // T127 — the 05 §4.4 verification round the delivery timeout must run
        // before it is allowed to cancel. Its misdelivery escalation port
        // (IDeliveryMisdeliveryEscalator) is implemented in the Disputes module,
        // which is where the Dispute type lives — see DisputesModule.
        services.AddScoped<IDeliveryTimeoutRound, DeliveryTimeoutRound>();

        // T130 — the dispute-open sibling of the timeout round. Registered here
        // rather than in the Disputes module for the same reason: the arm that
        // fires DeliverItem is Transactions-side work, and the Disputes checker
        // only maps its verdict onto what the buyer is told.
        services.AddScoped<IDeliveryDisputeRound, DeliveryDisputeRound>();

        // T129 — settlement window (02 §4.5.1). The provider reads the three
        // settlement settings; the verification service answers the end-of-
        // window question ("is the item still with the buyer, and if not, did it
        // go back to the seller?") and is side-effect free like its T125
        // sibling. The job that acts on the verdict is registered with the other
        // recurring jobs below.
        services.AddScoped<Skinora.Transactions.Application.Settlement.ISettlementSettingsProvider,
            Skinora.Transactions.Application.Settlement.SettlementSettingsProvider>();
        services.AddScoped<Skinora.Transactions.Application.Settlement.ISettlementVerificationService,
            Skinora.Transactions.Application.Settlement.SettlementVerificationService>();

        // T83a — user transaction list (07 §7.1). F4 retro recovery: T45
        // doc-ref claimed §7.1–§7.4 but the list endpoint was never
        // implemented; T88 dashboard surfaced the gap.
        services.AddScoped<ITransactionListService, TransactionListService>();

        // T51 — user-initiated cancel (07 §7.7, 02 §7).
        services.AddScoped<ITransactionCancellationService, TransactionCancellationService>();

        // WP15 — shared post-terminal reputation projector. Wraps the T43
        // aggregator + cooldown evaluator so every terminal-transition caller
        // (COMPLETED, CANCELLED_TIMEOUT, Steam-driven cancel, user-cancel)
        // refreshes the denormalized reputation/cooldown fields identically
        // (06 §8.2). The wrapped services live in UsersModule.
        services.AddScoped<Skinora.Transactions.Application.Reputation.ITransactionReputationRefresher,
            Skinora.Transactions.Application.Reputation.TransactionReputationRefresher>();

        // T67 — Steam inventory reader + cache invalidator ports. Stubs are
        // registered with TryAddScoped so SteamModule.AddSteamModule can
        // swap them for the sidecar-backed implementations via Replace().
        // Tests that build TransactionCreationService directly (without DI)
        // pass a NullSteamInventoryCacheInvalidator to keep the flow closed.
        services.TryAddScoped<ISteamInventoryReader, StubSteamInventoryReader>();
        services.TryAddScoped<ISteamInventoryCacheInvalidator, NullSteamInventoryCacheInvalidator>();

        // WP12 (T90 K3) — Steam trade-offer URL resolver port. Null default
        // (returns no URL) registered via TryAddScoped; SteamModule.Replace()

        // WP4a — wire the fraud pre-check price seam to the T81 Steam Market
        // stack. PriceServiceMarketPriceProvider (Skinora.Fraud) bridges the
        // Transactions IMarketPriceProvider port to Fraud's IPriceService
        // (cache-first, rate-limited). Explicit AddScoped (not TryAdd) so it
        // deterministically wins — a TryAdd could silently leave the rule inert
        // (NullMarketPriceProvider). Same cross-module placement rationale as
        // IAccountFlagChecker below. NOTE: the rule still only fires live when
        // SteamMarket:Provider=steam-market AND price_deviation_threshold is
        // configured (seeded default 1.0 = 100%).
        services.AddScoped<IMarketPriceProvider, PriceServiceMarketPriceProvider>();

        // Cross-module: IAccountFlagChecker is declared in Skinora.Transactions
        // but implemented in Skinora.Fraud (Fraud already references
        // Transactions; the reverse direction would be a project cycle).
        services.AddScoped<IAccountFlagChecker, AccountFlagChecker>();

        // T47 — timeout scheduling primitives + Hangfire job targets.
        services.AddScoped<ITimeoutSchedulingService, TimeoutSchedulingService>();
        services.AddScoped<ITimeoutExecutor, TimeoutExecutor>();
        services.AddScoped<IDeadlineScannerJob, DeadlineScannerJob>();
        // T48 — real warning dispatcher publishes TimeoutWarningEvent to the
        // outbox; the Notifications consumer fans it out to the buyer through
        // the in-app + external channel pipeline (T37).
        services.AddScoped<IWarningDispatcher, WarningDispatcher>();
        // T49 — phase-aware side-effect publisher. Both TimeoutExecutor and
        // DeadlineScannerJob delegate the post-trigger fan-out (notification +
        // refund + late-payment-monitor events) here so the mapping lives in
        // one place.
        services.AddScoped<ITimeoutSideEffectPublisher, TimeoutSideEffectPublisher>();
        // T50 — timeout freeze/resume engine. Single-tx overloads are consumed
        // by the T59 emergency-hold orchestrator; bulk overloads are consumed
        // by the future maintenance / Steam-outage / blockchain-degradation
        // admin paths.
        services.AddScoped<ITimeoutFreezeService, TimeoutFreezeService>();

        // T53 — gas fee management. SystemSetting reader + decision wrapper +
        // admin-alert side-effect tier. Callers (T57+ refund orchestrator,
        // T73 blockchain sidecar consumer) compose the trio: read live ratios,
        // compute refund vs block, raise alert atomically with the business
        // state change.
        services.AddScoped<IGasFeeSettingsProvider, GasFeeSettingsProvider>();
        services.AddScoped<IRefundDecisionService, RefundDecisionService>();
        services.AddScoped<IRefundBlockedAlertService, RefundBlockedAlertService>();

        // T59 — admin transaction lifecycle orchestrator. Composes the T44
        // state machine + T50 freeze service for AD19 / AD19b / AD19c
        // (07 §9.20–§9.22, 02 §7, 03 §8.8).
        services.AddScoped<IAdminTransactionService, AdminTransactionService>();

        // T63 — admin transaction read service backing AD6 / AD7 / AD16b
        // (07 §9.6 / §9.7 / §9.17). Implementation lives in Skinora.API
        // because AD7 detail composes data from Skinora.Notifications,
        // Skinora.Disputes and Skinora.Fraud (modules Skinora.Transactions
        // cannot reference without a project cycle).
        services.AddScoped<IAdminTransactionQueryService, AdminTransactionQueryService>();

        // T105 — AD16 user-detail cross-module activity aggregation (07 §9.16,
        // 04 §8.9). Same composition-root rationale as the read service above:
        // fans out into Transactions / Fraud / Disputes, which Skinora.Admin
        // cannot reference without a project cycle.
        services.AddScoped<IAdminUserActivityProvider, AdminUserActivityProvider>();

        // T60 — seller payout issue (07 §7.11, 02 §10.3, 06 §3.8a, 03 §2.4a
        // Senaryo A). Stub IPayoutVerifier is forward-deferred until the Tron
        // sidecar lands (T64–T69 devir); the admin resolver lives in
        // Skinora.API/Services because Skinora.Transactions cannot reference
        // Skinora.Admin (where AdminUserRole is declared).
        services.AddScoped<IPayoutIssueService, PayoutIssueService>();
        services.TryAddScoped<IPayoutVerifier, StubPayoutVerifier>();
        services.AddScoped<IPayoutEscalationAdminResolver, PayoutEscalationAdminResolver>();

        // T70 — blockchain sidecar HD wallet wiring (08 §3.2, 05 §3.3).
        services.Configure<BlockchainSidecarOptions>(
            configuration.GetSection(BlockchainSidecarOptions.SectionName));

        services.AddHttpClient<HttpBlockchainSidecarClient>(
            HttpBlockchainSidecarClient.HttpClientName,
            (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<BlockchainSidecarOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                }
                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds <= 0 ? 10 : options.TimeoutSeconds);
            });
        services.AddScoped<IBlockchainSidecarClient>(sp =>
            sp.GetRequiredService<HttpBlockchainSidecarClient>());

        services.AddScoped<IPaymentAddressAllocator, PaymentAddressAllocator>();
        services.AddScoped<EnsurePaymentAddressJob>();
        services.AddHostedService<EnsurePaymentAddressJobRegistrar>();

        // T139 — active payment monitor lifecycle (08 §3.4). The per-minute
        // reconciler arms every open payment window, re-arms after a backend or
        // sidecar restart, and disarms (stamping MonitoringStatus = STOPPED)
        // once the deposit has been swept or the transaction went terminal.
        services.AddScoped<EnsurePaymentMonitorJob>();
        services.AddHostedService<EnsurePaymentMonitorJobRegistrar>();

        // The inline fast path, wired through the outbox by the
        // SELLER_CONFIRMED transition. Explicit registration for the same
        // reason as its three siblings below: the Transactions assembly is NOT
        // in the OutboxModule MediatR scan list, so a missing line here would
        // silently drop the event — IPublisher.Publish with zero handlers
        // returns normally and the outbox row is stamped PROCESSED. The T139
        // validation round found exactly that defect here (finding B1); the
        // reconciler above masked it by re-arming within a minute.
        services.AddScoped<PaymentMonitorStartDispatcher>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.PaymentMonitorStartRequestedEvent>>(sp =>
            sp.GetRequiredService<PaymentMonitorStartDispatcher>());

        // T71 — inbound blockchain webhook handler (08 §3.4). The signature
        // envelope is verified by WebhookSignatureMiddleware (extended to
        // cover /api/v1/webhooks/blockchain in T71); the handler persists
        // BlockchainTransaction rows (06 §3.8). T72 wires the amount
        // validation pipeline (state-machine advance + refund intent rows +
        // outbox events) into the handler's PaymentConfirmed and
        // WrongTokenIncoming paths.
        services.AddScoped<IAmountValidationService, AmountValidationService>();
        services.AddScoped<IBlockchainWebhookHandler, BlockchainWebhookHandler>();

        // T73 — outbound transfer dispatcher (08 §3.1, §3.3, 05 §3.3). Two
        // recurring Hangfire jobs (per-minute) plus the sidecar HTTP port
        // and SystemSetting-backed retry policy. The dispatcher picks up
        // PENDING BlockchainTransaction rows (refund/payout/sweep) and asks
        // the sidecar to broadcast them; the confirmation job flips
        // DETECTED → CONFIRMED / FAILED based on solidity-node finality.
        services.AddHttpClient<HttpBlockchainTransferClient>(
            HttpBlockchainTransferClient.HttpClientName,
            (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<BlockchainSidecarOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                }
                // Transfer broadcast may serialize behind the sidecar's
                // RateLimitedQueue (TronGrid RPS budget) — give the call a
                // longer budget than the cheap derive endpoint.
                var seconds = options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds * 3;
                client.Timeout = TimeSpan.FromSeconds(seconds);
            });
        services.AddScoped<IBlockchainTransferClient>(sp =>
            sp.GetRequiredService<HttpBlockchainTransferClient>());

        services.AddScoped<ITransferRetryPolicy, SystemSettingsTransferRetryPolicy>();
        services.AddScoped<OutgoingTransferDispatchJob>();
        services.AddScoped<OutgoingTransferConfirmationJob>();
        services.AddScoped<SellerPayoutQueueJob>();
        // WP3 — deposit → hot wallet sweep producer. Queues the PENDING SWEEP
        // row once a transaction reaches ITEM_DELIVERED (deferred past the
        // buyer-refund window, owner decision); the dispatch + confirmation
        // jobs above settle it and reconciliation credits the hot wallet inflow.
        services.AddScoped<SweepQueueJob>();
        // T129 — the end-of-window settlement check. Produces neither transfer
        // row itself: it stamps the clearance both producers above now require,
        // or refunds the buyer when the trade turns out to have been reversed.
        services.AddScoped<Skinora.Transactions.Application.Settlement.SettlementVerificationJob>();
        services.AddHostedService<OutgoingTransferJobsRegistrar>();

        // WP1 — seller payout completion. The confirmation job emits
        // PayoutCompletedEvent; this consumer fires Complete → COMPLETED.
        // Explicit registration: the Transactions assembly is NOT in the
        // OutboxModule MediatR scan list (only API host / Notifications /
        // Realtime are), so a missing line here would silently drop the event.
        services.AddScoped<PayoutCompletedConsumer>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.PayoutCompletedEvent>>(sp =>
            sp.GetRequiredService<PayoutCompletedConsumer>());

        // WP2 — buyer payment refund. The three terminal-cancel paths
        // (delivery timeout, admin-cancel AD19, emergency-hold-release-cancel
        // AD19c) publish PaymentRefundToBuyerRequestedEvent; this consumer
        // queues the PENDING BUYER_REFUND row the dispatcher broadcasts.
        // Explicit registration: the Transactions assembly is NOT in the
        // OutboxModule MediatR scan list, so a missing line here would silently
        // drop the event (the live admin-cancel refund defect WP2 closes).
        services.AddScoped<PaymentRefundToBuyerConsumer>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.PaymentRefundToBuyerRequestedEvent>>(sp =>
            sp.GetRequiredService<PaymentRefundToBuyerConsumer>());

        // T75 — post-cancel monitoring (02 §4.4, 08 §3.4). Starter is the
        // shared entry point used by every cancel handler (T49 timeout,
        // T51 user-cancel, T59 admin-cancel + emergency-hold release CANCEL,
        // T47 DeadlineScannerJob). The MediatR notification handler picks
        // up the outbox event and calls the sidecar; the recovery hook
        // replays persisted POST_CANCEL_* state on host start.
        services.AddScoped<IPostCancelMonitorStarter, PostCancelMonitorStarter>();
        services.AddScoped<PostCancelMonitorStartDispatcher>();
        services.AddScoped<MediatR.INotificationHandler<
            Skinora.Shared.Events.PostCancelMonitorStartRequestedEvent>>(sp =>
            sp.GetRequiredService<PostCancelMonitorStartDispatcher>());
        services.AddHostedService<PostCancelMonitorRecoveryHook>();

        // T76 — daily on-chain vs ledger reconciliation (05 §3.3). The
        // sidecar batch-snapshot endpoint, the Hangfire recurring job, and
        // the registrar that reads the cron from SystemSettings at startup.
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddScoped<ReconciliationJob>();
        // WP14 — the registrar is also an ICronJobReconfigurer so an admin
        // change to reconciliation.schedule_cron re-registers the recurring job
        // at runtime instead of waiting for the next host restart. Register the
        // concrete once and forward it to both IHostedService and the
        // reconfigurer so all three resolve the same singleton instance.
        services.AddSingleton<ReconciliationJobRegistrar>();
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliationJobRegistrar>());
        services.AddSingleton<ICronJobReconfigurer>(sp => sp.GetRequiredService<ReconciliationJobRegistrar>());

        // T77 — admin-initiated hot→cold consolidation + periodic hot
        // wallet balance monitor (05 §3.3). HotWalletService writes the
        // ColdWalletTransfer ledger row alongside an audit entry; the
        // monitor job broadcasts threshold breaches over SignalR.
        services.AddScoped<IHotWalletService, HotWalletService>();
        services.AddScoped<IHotWalletMonitorService, HotWalletMonitorService>();
        services.AddScoped<HotWalletMonitorJob>();
        // WP14 — same reconfigurer wiring as ReconciliationJobRegistrar above
        // (hot_wallet.monitor_cron).
        services.AddSingleton<HotWalletMonitorJobRegistrar>();
        services.AddHostedService(sp => sp.GetRequiredService<HotWalletMonitorJobRegistrar>());
        services.AddSingleton<ICronJobReconfigurer>(sp => sp.GetRequiredService<HotWalletMonitorJobRegistrar>());

        // WP14 — replace the Platform module's no-op settings-change propagator
        // with the real one that re-registers the cron jobs above when their
        // schedule setting changes. Replace is order-independent w.r.t.
        // AddPlatformModule so exactly one registration survives.
        services.Replace(ServiceDescriptor.Singleton<ISettingChangePropagator, CronSettingChangePropagator>());

        return services;
    }
}
