# T06 — Authentication Altyapısı

**Faz:** F0 | **Durum:** ✓ PASS | **Tarih:** 2026-04-06

---

## Yapılan İşler
- JWT Bearer authentication konfigürasyonu (15dk access token, 30sn clock skew)
- JWT signing key rotation desteği (PreviousSecret ile grace period)
- Policy-based authorization: Authenticated, AdminAccess, SuperAdmin, Permission:* (dinamik)
- PermissionPolicyProvider: "Permission:" prefix'li policy'leri dinamik oluşturur
- PermissionAuthorizationHandler: SuperAdmin tüm permission'ları bypass eder
- AuthModule DI registration (IServiceCollection extension)
- Program.cs'de UseAuthentication/UseAuthorization middleware aktifleştirildi
- appsettings.json JWT section, docker-compose.yml JWT env variable'ları eklendi
- DiagnosticsController'a auth test endpoint'leri eklendi
- 17 integration test yazıldı

## Etkilenen Modüller / Dosyalar

### Yeni dosyalar
- `backend/src/Modules/Skinora.Auth/Configuration/JwtSettings.cs` — JWT ayarları options class
- `backend/src/Modules/Skinora.Auth/Configuration/AuthPolicies.cs` — Policy sabitleri, claim type'lar, rol sabitleri
- `backend/src/Modules/Skinora.Auth/Authorization/PermissionRequirement.cs` — Permission requirement
- `backend/src/Modules/Skinora.Auth/Authorization/PermissionAuthorizationHandler.cs` — Permission handler (SuperAdmin bypass)
- `backend/src/Modules/Skinora.Auth/Authorization/PermissionPolicyProvider.cs` — Dinamik policy provider
- `backend/src/Skinora.API/Configuration/AuthModule.cs` — JWT + Authorization DI registration
- `backend/tests/Skinora.API.Tests/Integration/AuthenticationTests.cs` — 17 integration test

### Değişen dosyalar
- `backend/src/Skinora.API/Program.cs` — Auth middleware aktifleştirme, AuthModule registration
- `backend/src/Skinora.API/appsettings.json` — JWT configuration section
- `backend/src/Skinora.API/Skinora.API.csproj` — JwtBearer NuGet paketi
- `backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj` — AspNetCore.App FrameworkReference
- `backend/src/Skinora.API/Controllers/DiagnosticsController.cs` — Auth test endpoint'leri
- `backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj` — System.IdentityModel.Tokens.Jwt paketi
- `docker-compose.yml` — JWT env variable'ları

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | JWT Bearer authentication konfigüre edildi (15dk access token) | ✓ Karşılandı | `AuthModule.cs` — AccessTokenExpiryMinutes=15, TokenValidationParameters strict |
| 2 | Refresh token mekanizması tanımlı (HttpOnly + Secure + SameSite=Strict cookie) | ~ Kısmi | `JwtSettings.RefreshTokenExpiryDays=7` ve docker-compose env tanımlı. Cookie attribute set/read T29'da uygulanacak — bu task altyapı kapsamında, validator tarafından kabul edildi |
| 3 | Policy-based authorization tanımlı (kullanıcı, admin, permission bazlı) | ✓ Karşılandı | `AuthPolicies`: Authenticated, AdminAccess, SuperAdmin + `PermissionPolicyProvider` dinamik policy |
| 4 | [Authorize], [AllowAnonymous] attribute'ları kullanıma hazır | ✓ Karşılandı | DiagnosticsController test endpoint'lerinde doğrulandı, 17 integration test PASS |
| 5 | JWT signing key rotation desteği (grace period) | ✓ Karşılandı | `IssuerSigningKeys` listesinde current + previous key, test: `ProtectedEndpoint_WithPreviousSecretToken_Returns200` |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ 38/38 passed | `dotnet test` — 17 yeni auth test + 21 mevcut test, tümü PASS |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Tarih | 2026-04-06 |
| Bulgu sayısı | 0 kritik, 1 minor (~ Kısmi: kriter #2 refresh cookie wiring T29'a deferred) |
| Düzeltme gerekli mi | Hayır |
| Build | ✓ 0 warning, 0 error |
| Test | ✓ 38/38 (Skinora.API.Tests), tüm modül testleri PASS |
| Güvenlik | ✓ Secret placeholder kullanılıyor, env üzerinden enjekte ediliyor |

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Var — `appsettings.json` JWT section, `docker-compose.yml` JWT env variables
- Docker değişikliği: Var — backend service'e JWT env variable'ları eklendi
- NuGet: `Microsoft.AspNetCore.Authentication.JwtBearer 9.0.3` (API), `System.IdentityModel.Tokens.Jwt 8.0.1` (test)

## Commit & PR
- Branch: `task/T06-authentication-infrastructure`
- Commit: 317a460
- Merge: main'e squash merge
- PR: (lokal akış)
- CI: (lokal — `dotnet build` + `dotnet test` ✓)

## Known Limitations / Follow-up
- Refresh token cookie set/read işlemi bu task'ta yok — T29 (Steam OpenID) ve T32 (Refresh token yönetimi) kapsamında
- Gerçek token üretimi bu task'ta yok — T29 kapsamında
- Admin permission seed data — T26 kapsamında
- MapInboundClaims=false: .NET default claim mapping kapatıldı, claim type'lar doküman ile uyumlu (sub, role, permission)

## Notlar
- `PermissionPolicyProvider`: "Permission:X" formatında herhangi bir policy adı dinamik olarak resolve edilir
- `PermissionAuthorizationHandler`: SuperAdmin rolü tüm permission kontrollerini otomatik bypass eder (05 §6.2 ile uyumlu)
- DiagnosticsController auth endpoint'leri development/test amaçlı — production'da restrict edilecek
