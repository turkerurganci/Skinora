using System.Runtime.CompilerServices;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests;

/// <summary>
/// Assembly-load hook that registers every module assembly the unit tests in
/// this DLL touch, before any test class static constructor or
/// <c>AppDbContext.OnModelCreating</c> can run. Without this, test-class
/// ordering decides whether Platform's IEntityTypeConfiguration is on the
/// model the first time EF caches it — locally the order happens to favour
/// us, but the CI runner's parallel test scheduling exposes the race and
/// SystemSetting (and other Platform entities) disappear from the model.
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }
}
