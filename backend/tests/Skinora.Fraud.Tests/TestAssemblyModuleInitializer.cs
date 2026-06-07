using System.Runtime.CompilerServices;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests;

// EF Core'un IModelSource implementasyonu AppDbContext tipini anahtar alarak
// modeli process-statik olarak cache'ler. IntegrationTestBase türevi test
// sınıfları Program.cs çalıştırmadan doğrudan AppDbContext kurar; modül
// kayıtları ise her test sınıfının kendi static ctor'ında yapılıyor ve bu
// sınıflar FARKLI alt kümeler kaydediyordu:
//   FraudFlagServiceTests / MultiAccountDetectorTests → Users+Transactions+Fraud+Platform
//   AccountFlagCheckerTests / FraudFlagAdminQueryServiceTests / FraudFlagEntityTests
//                                                       → Users+Transactions+Fraud (Platform YOK)
//   PriceServiceTests                                   → Fraud (yalnız)
// xUnit sınıfları paralel koştuğundan, Platform kaydetmeyen bir sınıf ilk
// AppDbContext'i açarsa model AuditLog/SystemSetting (Skinora.Platform)
// entity'leri olmadan cache'lenir; sonrasında o entity'lere ihtiyaç duyan
// sınıflar "System.InvalidOperationException : Cannot create a DbSet for
// 'AuditLog'/'SystemSetting' because this type is not included in the model
// for the context." ile kırılır — sıralamaya bağlı flaky.
//
// CI run 27090686509 (T101 K11 PR #155, docs-only commit 4376e58) bu race ile
// 11+ Fraud integration testi fail verdi; aynı kod bir önceki commit'te
// (8792392) ve `gh run rerun --failed` sonrası şanslı sıralama ile geçti.
// Skinora.API.Tests + Skinora.Auth.Tests'in eşdeğer initializer'ları bu yarışı
// kendi assembly'lerinde çoktan kapatmıştı — Skinora.Fraud.Tests bu fix'ten
// yoksundu.
//
// ModuleInitializer assembly yüklendiğinde — herhangi bir test sınıfı
// instantiate edilmeden ve herhangi bir model build tetiklenmeden önce —
// çalışır. Böylece model her zaman tam modül kümesiyle kurulur. Skinora.Fraud
// .Tests bu dört modüle proje referansı üzerinden erişebilir (csproj:
// Fraud/Platform/Transactions/Users). Başka bir modül entity'si kullanılırsa
// buraya da eklenmeli. Per-class static ctor'lar idempotent (RegisterModule
// Assembly lock+contains korumalı) olduğundan dokunulmadan bırakıldı.
internal static class TestAssemblyModuleInitializer
{
    [ModuleInitializer]
    internal static void RegisterAllModules()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }
}
