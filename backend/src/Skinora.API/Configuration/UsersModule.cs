using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Auth.Application.Session;
using Skinora.Notifications.Application.Account;
using Skinora.Notifications.Application.Settings;
using Skinora.Platform.Infrastructure.Reputation;
using Skinora.Shared.Discord;
using Skinora.Shared.Email;
using Skinora.Transactions.Application.Account;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Application.Wallet;
using Skinora.Users.Application.Account;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Application.Settings;
using Skinora.Users.Application.Wallet;
using StackExchange.Redis;
// T79 — TelegramSettings consolidated into Skinora.Shared.Telegram so the
// settings + bot transport + connection service share a single config
// source. Bound here as well as in Program.cs so legacy callers see the
// same instance.
// T80 — DiscordSettings + IDiscordOAuthClient moved to
// Skinora.Shared.Discord; binding lives in Program.cs alongside the
// rate limiter + typed HttpClient registrations.

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

        // T34 — wallet address management (07 §5.3, §5.4). T82 swaps the
        // NoMatchWalletSanctionsCheck stub for DbWalletSanctionsCheck which
        // queries SanctionedAddress via ISanctionedAddressLookup (06 §3.25).
        services.AddSingleton<ITrc20AddressValidator, Trc20AddressValidator>();
        services.AddScoped<IWalletSanctionsCheck, DbWalletSanctionsCheck>();
        services.AddScoped<IActiveTransactionCounter, ActiveTransactionCounter>();
        services.AddScoped<IWalletAddressService, WalletAddressService>();

        // T35 — account settings (07 §5.6–§5.16a). Cross-module glue:
        // INotificationPreferenceStore implementation lives in
        // Skinora.Notifications because that module owns UserNotificationPreference.
        // TelegramSettings (T79) + DiscordSettings (T80) are bound at the
        // API composition root (Program.cs) since the bot-transport
        // consumers live in Skinora.Shared.{Telegram,Discord}.

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
        // implementation when Resend:Provider == "resend". IEmailSender lives
        // in Users so the interface is available without pulling Resend into
        // this module; the concrete Resend sender lives in the same namespace
        // and is selected here at composition time.
        var emailProvider = configuration[$"{ResendSettings.SectionName}:{nameof(ResendSettings.Provider)}"]
            ?? ResendSettings.ProviderLogging;

        if (string.Equals(emailProvider, ResendSettings.ProviderResend, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddScoped<IEmailSender, ResendVerificationEmailSender>();
        }
        else
        {
            services.TryAddScoped<IEmailSender, LoggingEmailSender>();
        }

        // Discord OAuth HTTP client — T80 swap. When
        // Discord:Provider == "discord", Program.cs registered the
        // typed HttpClient for DiscordOAuthClient already; this branch
        // just registers the stub fallback so logging-mode environments
        // keep working without contacting Discord.
        var discordProvider = configuration[$"{DiscordSettings.SectionName}:{nameof(DiscordSettings.Provider)}"]
            ?? DiscordSettings.ProviderLogging;

        if (!string.Equals(discordProvider, DiscordSettings.ProviderDiscord, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddScoped<IDiscordOAuthClient, StubDiscordOAuthClient>();
        }

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
