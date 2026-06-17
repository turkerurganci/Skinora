using System.Runtime.CompilerServices;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests;

// EF Core caches the AppDbContext model statically keyed by the context type.
// Steam integration test classes register their modules in per-class static
// ctors, which run lazily the first time each class is touched. If a class that
// does not register PlatformModule (e.g. TradeOfferSteamBotEntityTests —
// Users + Steam only) builds the model before a Platform-dependent class
// (BotRestrictionRecoveryConsumerTests / SteamWebhookHandlerTests), the cached
// model omits the AuditLog entity and the recovery tests fail with
// "Cannot create a DbSet for 'AuditLog' because this type is not included in the
// model for the context". The ordering is non-deterministic, so the break is
// flaky across runs.
//
// Registering every module this assembly uses at assembly load — before any
// test class is instantiated — makes the first model build complete regardless
// of class execution order. Mirrors
// Skinora.API.Tests.TestAssemblyModuleInitializer.
internal static class TestAssemblyModuleInitializer
{
    [ModuleInitializer]
    internal static void RegisterAllModules()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }
}
