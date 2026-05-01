using System.Runtime.CompilerServices;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests;

// EF Core'un IModelSource implementasyonu AppDbContext tipini anahtar alarak
// modeli process-statik olarak cache'ler. IntegrationTestBase türevi test
// sınıfları (RefreshTokenServiceTests, TosAcceptanceServiceTests, vb.)
// Program.cs çalıştırmadan doğrudan AppDbContext kurar; eğer bu sınıflardan
// biri AdminAuthorityResolverTests'in static ctor'ı çalışmadan önce bir
// DbContext açarsa, Admin modülü kayıtlı olmadığı için modeli AdminUserRole/
// AdminRole/AdminRolePermission entity'leri olmadan cache'ler. Sonraki
// AdminAuthorityResolverTests "Cannot create a DbSet for 'AdminUserRole'..."
// hatasıyla kırılır.
//
// CI run 25221461254 (T40 task branch, e54564f docs-only commit) bu race ile
// 7 Auth integration test fail verdi; aynı kod 25220660821 ve post-merge main
// 25221463512'de şanslı sıralama ile geçti. Skinora.API.Tests'in eşdeğer
// initializer'ı bu yarışı oradaki paralel koşumda kapatıyordu — Auth.Tests
// assembly'sine de aynı çözüm gerekli.
//
// ModuleInitializer assembly yüklendiğinde — yani herhangi bir test sınıfı
// instantiate edilmeden önce — çalışır. Skinora.Auth.Tests bu dört modüle
// proje referansı üzerinden erişebilir (Auth → Shared/Users/Platform/Admin);
// diğer modülleri (Notifications/Transactions/Disputes/Fraud/Payments/Steam)
// hiçbir test sınıfı şu an kullanmıyor, eklenirse buraya da eklenmeli.
internal static class TestAssemblyModuleInitializer
{
    [ModuleInitializer]
    internal static void RegisterAllModules()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
        AdminModuleDbRegistration.RegisterAdminModule();
    }
}
