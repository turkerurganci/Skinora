using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Application.MultiAccount;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Users.Application.MultiAccount;

namespace Skinora.Fraud;

/// <summary>
/// DI wiring for the Skinora.Fraud module — fraud flag lifecycle (T54).
/// <c>IAccountFlagChecker</c> is registered by <c>TransactionsModule</c>
/// because that's where the port is declared (Skinora.Fraud already
/// references Skinora.Transactions; the reverse would be a cycle).
/// </summary>
public static class FraudModule
{
    public static IServiceCollection AddFraudModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // T54 — fraud flag write + read services.
        services.AddScoped<IFraudFlagService, FraudFlagService>();
        services.AddScoped<IFraudFlagAdminQueryService, FraudFlagAdminQueryService>();

        // T54 cross-module — adapter that routes the Transactions
        // pre-create writer port through the Fraud-owned audit / outbox
        // pipeline (mirrors the IAccountFlagChecker pattern).
        services.AddScoped<ITransactionFraudFlagWriter, TransactionFraudFlagWriter>();

        // T56 — multi-account detector. Port lives in Skinora.Users so the
        // wallet update path can call it without referencing Fraud.
        services.AddScoped<IMultiAccountDetector, MultiAccountDetector>();

        return services;
    }
}
