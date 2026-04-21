using System.Runtime.CompilerServices;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests;

// EF Core'un `IModelSource` implementasyonu AppDbContext tipini anahtar alarak
// model'i statik olarak cache'ler. IntegrationTestBase türevi test sınıfları
// (ör. OutboxTests) Program.cs'yi çalıştırmadan doğrudan AppDbContext kurar;
// eğer bu sınıf, Skinora.API çalıştıran WebApplicationFactory tabanlı bir test
// sınıfından (AuthSteamEndpointTests, AuthenticationTests) önce bir DbContext
// açarsa, modül kayıtları henüz yapılmamış olduğu için modeli eksik entity
// kümesiyle cache'ler. Sonraki WebApplicationFactory test'leri cache'lenmiş
// modeli devralır ve "Cannot create a DbSet for 'RefreshToken'..." gibi hatalar
// atar. Sıralama flaky olduğu için koşullara göre rastgele kırılma olur.
//
// ModuleInitializer assembly yüklendiğinde — yani herhangi bir test sınıfı
// instantiate edilmeden önce — çalışır, böylece tüm modül assembly'leri
// AppDbContext ilk model build'inden önce _moduleAssemblies listesine girer.
internal static class TestAssemblyModuleInitializer
{
    [ModuleInitializer]
    internal static void RegisterAllModules()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
        AdminModuleDbRegistration.RegisterAdminModule();
        PaymentsModuleDbRegistration.RegisterPaymentsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }
}
