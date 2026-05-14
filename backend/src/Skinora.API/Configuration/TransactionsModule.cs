using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.API.Services;
using Skinora.Fraud.Application.Account;
using Skinora.Transactions.Application.Admin;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PayoutIssues;
using Skinora.Transactions.Application.Pricing;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;

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

        // T51 — user-initiated cancel (07 §7.7, 02 §7).
        services.AddScoped<ITransactionCancellationService, TransactionCancellationService>();

        // T67 — Steam inventory reader + cache invalidator ports. Stubs are
        // registered with TryAddScoped so SteamModule.AddSteamModule can
        // swap them for the sidecar-backed implementations via Replace().
        // Tests that build TransactionCreationService directly (without DI)
        // pass a NullSteamInventoryCacheInvalidator to keep the flow closed.
        services.TryAddScoped<ISteamInventoryReader, StubSteamInventoryReader>();
        services.TryAddScoped<ISteamInventoryCacheInvalidator, NullSteamInventoryCacheInvalidator>();

        // T81 forward-deferred — market price stub. Defaults to "no market
        // signal" so the fraud pre-check never fires until T81 ships.
        services.TryAddScoped<IMarketPriceProvider, NullMarketPriceProvider>();

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

        // T60 — seller payout issue (07 §7.11, 02 §10.3, 06 §3.8a, 03 §2.4a
        // Senaryo A). Stub IPayoutVerifier is forward-deferred until the Tron
        // sidecar lands (T64–T69 devir); the admin resolver lives in
        // Skinora.API/Services because Skinora.Transactions cannot reference
        // Skinora.Admin (where AdminUserRole is declared).
        services.AddScoped<IPayoutIssueService, PayoutIssueService>();
        services.TryAddScoped<IPayoutVerifier, StubPayoutVerifier>();
        services.AddScoped<IPayoutEscalationAdminResolver, PayoutEscalationAdminResolver>();

        return services;
    }
}
