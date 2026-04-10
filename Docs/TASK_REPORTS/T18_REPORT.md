# T18 — User, UserLoginLog, RefreshToken entity'leri

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-10

---

## Yapılan İşler
- User entity (06 §3.1): 18 field, BaseEntity + ISoftDeletable + IAuditableEntity
- UserLoginLog entity (06 §3.2): long PK (IDENTITY), ISoftDeletable, immutable log (no UpdatedAt)
- RefreshToken entity (06 §3.3): BaseEntity + ISoftDeletable + IAuditableEntity, self-referencing FK (token rotation)
- EF Core IEntityTypeConfiguration: UserConfiguration, UserLoginLogConfiguration, RefreshTokenConfiguration
- Modül assembly kayıt mekanizması: UsersModuleDbRegistration, AuthModuleDbRegistration, AppDbContext.RegisterModuleAssembly()
- Auth → Users proje referansı (RefreshToken → User FK ilişkisi)

## Etkilenen Modüller / Dosyalar
- `Modules/Skinora.Users/Domain/Entities/User.cs` — yeni
- `Modules/Skinora.Users/Domain/Entities/UserLoginLog.cs` — yeni
- `Modules/Skinora.Users/Infrastructure/Persistence/UserConfiguration.cs` — yeni
- `Modules/Skinora.Users/Infrastructure/Persistence/UserLoginLogConfiguration.cs` — yeni
- `Modules/Skinora.Users/Infrastructure/Persistence/UsersModuleDbRegistration.cs` — yeni
- `Modules/Skinora.Auth/Domain/Entities/RefreshToken.cs` — yeni
- `Modules/Skinora.Auth/Infrastructure/Persistence/RefreshTokenConfiguration.cs` — yeni
- `Modules/Skinora.Auth/Infrastructure/Persistence/AuthModuleDbRegistration.cs` — yeni
- `Modules/Skinora.Auth/Skinora.Auth.csproj` — Users modül referansı eklendi
- `Skinora.Shared/Persistence/AppDbContext.cs` — modül assembly kayıt mekanizması eklendi
- `Skinora.API/Program.cs` — modül kayıtları eklendi

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | User entity: tüm field'lar 06 §3.1'e göre | ✓ | User.cs: SteamId, SteamDisplayName, SteamAvatarUrl, DefaultPayoutAddress, DefaultRefundAddress, Email, PreferredLanguage, TosAcceptedVersion, TosAcceptedAt, MobileAuthenticatorVerified, CompletedTransactionCount, SuccessfulTransactionRate, CooldownExpiresAt, IsDeactivated, DeactivatedAt, IsDeleted, DeletedAt + BaseEntity (Id, CreatedAt, UpdatedAt, RowVersion) |
| 2 | UserLoginLog entity: 06 §3.2'ye göre | ✓ | UserLoginLog.cs: Id (long), UserId, IpAddress, DeviceFingerprint, UserAgent, IsDeleted, DeletedAt, CreatedAt |
| 3 | RefreshToken entity: 06 §3.3'e göre (Token, ReplacedByTokenId self-ref) | ✓ | RefreshToken.cs: Id, UserId, Token, ExpiresAt, IsRevoked, RevokedAt, IsDeleted, DeletedAt, ReplacedByTokenId, DeviceInfo, IpAddress + BaseEntity (CreatedAt, UpdatedAt, RowVersion) |
| 4 | Unique constraint: User.SteamId | ✓ | UserConfiguration: `HasIndex(u => u.SteamId).IsUnique().HasDatabaseName("UQ_Users_SteamId")` |
| 5 | Unique constraint: RefreshToken.Token | ✓ | RefreshTokenConfiguration: `HasIndex(t => t.Token).IsUnique().HasDatabaseName("UQ_RefreshTokens_Token")` |
| 6 | FK: UserLoginLog→User | ✓ | UserLoginLogConfiguration: `HasOne(l => l.User).WithMany(u => u.LoginLogs).HasForeignKey(l => l.UserId)` |
| 7 | FK: RefreshToken→User | ✓ | RefreshTokenConfiguration: `HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)` |
| 8 | FK: RefreshToken→RefreshToken (self) | ✓ | RefreshTokenConfiguration: `HasOne(t => t.ReplacedByToken).WithMany().HasForeignKey(t => t.ReplacedByTokenId)` |
| 9 | Index: User.DefaultPayoutAddress, DefaultRefundAddress | ✓ | UserConfiguration: IX_Users_DefaultPayoutAddress, IX_Users_DefaultRefundAddress |
| 10 | Index: UserLoginLog (UserId, IpAddress, DeviceFingerprint) | ✓ | UserLoginLogConfiguration: IX_UserLoginLogs_UserId, IX_UserLoginLogs_IpAddress, IX_UserLoginLogs_DeviceFingerprint |
| 11 | Index: RefreshToken.UserId | ✓ | RefreshTokenConfiguration: IX_RefreshTokens_UserId |
| 12 | Soft delete: User, UserLoginLog, RefreshToken | ✓ | Tüm entity'ler ISoftDeletable implement ediyor, global query filter AppDbContext'te aktif |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Mevcut testler | ✓ 99/99 passed | `dotnet test --no-build` — 3.18 dk, regresyon yok |
| Build | ✓ 0 error, 0 warning | `dotnet build --no-restore` |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Doğrulama bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration oluşturulacak)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T18-user-entities`
- Commit: `0aa683e` — T18: User, UserLoginLog, RefreshToken entity'leri
- PR: Henüz açılmadı
- CI: Lokal build + test ✓ PASS

## Known Limitations / Follow-up
- Migration oluşturulmadı — T28 (Initial migration) bekliyor
- User entity'ye navigation property'ler (RefreshTokens, Transactions vb.) sonraki task'larda eklenecek (ilgili entity'ler tanımlandıkça)

## Notlar
- **UserLoginLog** `BaseEntity` kullanmıyor (long PK / IDENTITY vs BaseEntity'nin Guid PK'sı). `IAuditableEntity` da uygulanmıyor (log immutable — UpdatedAt yok).
- **Modül assembly kayıt mekanizması** bu task'ta oluşturuldu (`AppDbContext.RegisterModuleAssembly`). Sonraki modüller aynı pattern'i kullanacak.
- **Auth → Users referansı** eklendi (RefreshToken → User FK). Modüler mimari açısından kabul edilebilir — Auth modülünün User entity'sini bilmesi doğal.
- `UseIdentityColumn()` yerine `ValueGeneratedOnAdd()` kullanıldı — provider-agnostic, SQL Server IDENTITY'yi otomatik uygular.
