using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Auth.Application.Session;
using Skinora.Notifications.Application.Account;
using Skinora.Notifications.Application.Settings;
using Skinora.Platform.Infrastructure.Reputation;
using Skinora.Transactions.Application.Account;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Application.Wallet;
using Skinora.Users.Application.Account;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Application.Settings;
using Skinora.Users.Application.Wallet;
using StackExchange.Redis;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Users module — profile read services
/// (T33 — 07 §5.1, §5.2, §5.5), wallet address management
/// (T34 — 07 §5.3, §5.4), and account settings
/// (T35 — 07 §5.6–§5.16a). Cross-module services whose implementations
/// live in sibling modules (wallet counter, notification preference store)
/// are also registered here since the API composition root is the only
/// place with references to every module.
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        // T43 — reputation scoring + cancel cooldown (02 §13, §14.2, 06 §3.1).
        // Cross-module glue: ports live in Skinora.Users, the SystemSetting
        // readers live in Skinora.Platform (owns SystemSetting), the
        // Transaction-aware aggregator + cooldown evaluator live in
        // Skinora.Transactions (owns Transaction + TransactionHistory).
        services.AddScoped<IReputationThresholdsProvider, ReputationThresholdsProvider>();
        services.AddScoped<ICancelCooldownThresholdsProvider, CancelCooldownThresholdsProvider>();
        services.AddScoped<IReputationScoreCalculator, ReputationScoreCalculator>();
        services.AddScoped<IReputationAggregator, ReputationAggregator>();
        services.AddScoped<IUserCancelCooldownEvaluator, CancelCooldownEvaluator>();

        services.AddScoped<IUserProfileService, UserProfileService>();

        // T34 — wallet address management (07 §5.3, §5.4)
        services.AddSingleton<ITrc20AddressValidator, Trc20AddressValidator>();
        services.AddSingleton<IWalletSanctionsCheck, NoMatchWalletSanctionsCheck>();
        services.AddScoped<IActiveTransactionCounter, ActiveTransactionCounter>();
        services.AddScoped<IWalletAddressService, WalletAddressService>();

        // T35 — account settings (07 §5.6–§5.16a). Cross-module glue:
        // INotificationPreferenceStore implementation lives in
        // Skinora.Notifications because that module owns UserNotificationPreference.
        services.Configure<TelegramSettings>(configuration.GetSection(TelegramSettings.SectionName));
        services.Configure<DiscordSettings>(configuration.GetSection(DiscordSettings.SectionName));

        services.AddScoped<INotificationPreferenceStore, NotificationPreferenceStore>();
        services.AddScoped<IAccountSettingsService, AccountSettingsService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();

        // Redis-backed short-lived stores (mirror RedisReAuthTokenStore — T31)
        services.AddSingleton<IEmailVerificationCodeStore>(sp =>
            new RedisEmailVerificationCodeStore(
                sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix: "skinora"));
        services.AddSingleton<ITelegramVerificationStore>(sp =>
            new RedisTelegramVerificationStore(
                sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix: "skinora"));
        services.AddSingleton<IDiscordOAuthStateStore>(sp =>
            new RedisDiscordOAuthStateStore(
                sp.GetRequiredService<IConnectionMultiplexer>(), keyPrefix: "skinora"));

        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<ITelegramConnectionService, TelegramConnectionService>();
        services.AddScoped<IDiscordConnectionService, DiscordConnectionService>();

        // Email sender — T78 swaps LoggingEmailSender for the Resend-backed
        // implementation. IEmailSender lives in Users so the interface is
        // available without pulling Resend into this module.
        services.TryAddScoped<IEmailSender, LoggingEmailSender>();

        // Discord OAuth HTTP client — T80 swaps the stub for a real
        // Discord token-exchange call.
        services.TryAddScoped<IDiscordOAuthClient, StubDiscordOAuthClient>();

        // Steam trade-hold check — T64–T69 swap the stub for a sidecar
        // call (GetTradeHoldDurations). See 08 §2.2.
        services.TryAddScoped<ITradeHoldChecker, StubTradeHoldChecker>();

        services.AddSingleton<ITradeUrlParser, TradeUrlParser>();
        services.AddScoped<ISteamTradeUrlService, SteamTradeUrlService>();

        // T36 — account deactivate + delete (07 §5.17, 02 §19, 06 §6.2).
        // Cross-module glue mirrors the T34/T35 pattern: the port interface
        // lives in Skinora.Users and each sibling module registers its impl
        // here at the composition root.
        services.AddScoped<IUserActiveTransactionChecker, UserActiveTransactionChecker>();
        services.AddScoped<INotificationAccountAnonymizer, NotificationAccountAnonymizer>();
        services.AddScoped<IAuthAccountAnonymizer, AuthAccountAnonymizer>();
        services.AddScoped<IAccountLifecycleService, AccountLifecycleService>();

        return services;
    }
}
