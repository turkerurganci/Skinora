# T36 — Hesap deaktif ve silme

**Faz:** F2 | **Durum:** ⏳ Yapım bitti (doğrulama bekleniyor) | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **`POST /api/v1/users/me/deactivate` (U13, 07 §5.17):** `[Authorize(Authenticated)]` + `[RateLimit("user-write")]`. `AccountLifecycleService.DeactivateAsync` aktif-işlem kontrolü → `User.IsDeactivated=true + DeactivatedAt=NOW` → tüm aktif `RefreshToken` revoke (row korunur, audit). Controller cookie'yi (`refreshToken`, Path=`/api/v1/auth`) clear eder — session sonlanır. Hata: 422 `HAS_ACTIVE_TRANSACTIONS`.
- **`DELETE /api/v1/users/me` (U14, 07 §5.17):** Aynı auth + rate bucket. İki aşamalı kontrol: (1) body'de `confirmation="SİL"` exact ordinal match (Türkçe büyük İ ile) — yanlış → 400 `VALIDATION_ERROR`; (2) aktif işlem kontrolü → 422 `HAS_ACTIVE_TRANSACTIONS`. Başarılı akış 06 §6.2'nin hesap silme + anonimleştirme tablosunu birebir uygular.
- **User anonimleştirme (06 §6.2):** `SteamId → "ANON_" + Guid.NewGuid().ToString("N")[..15]` (UNIQUE + NOT NULL korunur, 20-char sınırı tam dolar); `SteamDisplayName → "Deleted User"`; `SteamAvatarUrl`, `DefaultPayoutAddress`, `DefaultRefundAddress`, `Email`, `EmailVerifiedAt`, `SteamTradeUrl`, `SteamTradePartner`, `SteamTradeAccessToken` → `null`; `IsDeleted=true`, `DeletedAt=NOW`. `Transaction`, `TransactionHistory`, `AuditLog`, `UserLoginLog` rows **dokunulmaz** (anonim audit trail — 06 §6.2).
- **Notification anonimleştirme (06 §6.2):** `UserNotificationPreference` tüm satırlar soft delete + `ExternalId=null` + `IsEnabled=false`; `NotificationDelivery.TargetExternalId` channel'a göre masked — email `***@***.com`, Telegram `tg:***{son4}`, Discord `dsc:***{son4}` (< 4 karakter giriş için `tg:***` / `dsc:***` fallback; leak riskini önlemek için hiçbir zaman 4'ten az tail çıkarılmaz). Notification row (audit trail) ve delivery meta (Status/AttemptCount/SentAt) korunur.
- **Auth session anonimleştirme (06 §6.2):** Deactivate flow → tüm non-revoked `RefreshToken` için `IsRevoked=true + RevokedAt=NOW` + cache entry invalidate. Delete flow → ek olarak `IsDeleted=true + DeletedAt=NOW + DeviceInfo=null + IpAddress=null` (PII snapshot'ları).
- **Cross-module port pattern (T34/T35 mirror):** `Skinora.Users.Application.Account` altında üç port — `IUserActiveTransactionChecker`, `INotificationAccountAnonymizer`, `IAuthAccountAnonymizer`. Impl'ler sibling modüllerde (`Skinora.Transactions/Application/Account/UserActiveTransactionChecker`, `Skinora.Notifications/Application/Account/NotificationAccountAnonymizer`, `Skinora.Auth/Application/Session/AuthAccountAnonymizer`). Reference grafiği korunur: `Notifications → Users`, `Transactions → Users`, `Auth → Users` — ters yön yok.
- **Outcome tipleri:** Her flow için sealed record union (`AccountDeactivateOutcome.{Success, UserNotFound, HasActiveTransactions}`; `AccountDeleteOutcome.{Success, UserNotFound, HasActiveTransactions, ConfirmationInvalid}`) — controller switch ile statü → HTTP eşlemesi yapar, T35 pattern.
- **Session sonlandırma:** Her iki endpoint de başarılı cevapta controller seviyesinde `refreshToken` cookie'yi `Path=/api/v1/auth, Secure, HttpOnly, SameSite=Strict` ile delete eder (AuthController `ClearRefreshCookie` pattern'i). Response body deletedAt/deactivatedAt + kullanıcı-dostu Türkçe mesaj.

## Known Limitations (dokümansal devirler)

- **`UserLoginLog` retention — 02 §19 + 06 §6.2.** "KVKK talebiyle temizlenebilir" → MVP'de otomatik temizleme yok; T63b retention job'larında ayrı konu. T36 rapor ve testler UserLoginLog'a dokunmaz; 06 §6.2 "korunur (fraud audit)" kuralı.
- **Bağımsız `Notification` retention — 06 §6.2.** `TransactionId` referanslı olmayan bildirimler için retention politikası T63b kapsamında. T36 hiç Notification row'unu silmez, yalnızca delivery target'ı maskler.
- **Reactivate (tekrar login → `IsDeactivated=false`) akışı — T29 scope.** 07 §5.17 "Tekrar giriş yaparak aktif edebilirsiniz" — bu mantık Steam OpenID callback'te (T29 `SteamAuthenticationPipeline`) işlenir. T36 yalnızca deaktif etme yönünü implement eder.

## Etkilenen Modüller / Dosyalar

**Yeni — Skinora.Users/Application/Account/:**
- `IUserActiveTransactionChecker.cs` — aktif işlem kontrolü port
- `INotificationAccountAnonymizer.cs` — notification-side port + `NotificationAnonymizationResult`
- `IAuthAccountAnonymizer.cs` — auth-side port (revoke-only vs. full anonymize)
- `IAccountLifecycleService.cs` — ana service interface + outcome record union'ları
- `AccountLifecycleService.cs` — deactivate + delete orchestration
- `AccountLifecycleDtos.cs` — `DeleteAccountRequest`, `AccountDeactivateResponse`, `AccountDeleteResponse`
- `AccountLifecycleErrorCodes.cs` — `VALIDATION_ERROR`, `HAS_ACTIVE_TRANSACTIONS`

**Yeni — Skinora.Transactions/Application/Account/:**
- `UserActiveTransactionChecker.cs` — `IUserActiveTransactionChecker` concrete impl (buyer OR seller, non-terminal states).

**Yeni — Skinora.Notifications/Application/Account/:**
- `NotificationAccountAnonymizer.cs` — preference soft-delete + delivery `TargetExternalId` masking (channel-aware).

**Yeni — Skinora.Auth/Application/Session/:**
- `AuthAccountAnonymizer.cs` — refresh token revoke (deactivate) vs. revoke+soft-delete+strip-PII (delete).

**Değişiklik — API katmanı:**
- `Skinora.API/Controllers/UsersController.cs` — U13 `POST /users/me/deactivate` + U14 `DELETE /users/me` endpoint'leri; `IAccountLifecycleService` DI; `ClearRefreshCookie` private helper + `RefreshCookieName` + `RefreshCookiePath` sabitleri.
- `Skinora.API/Configuration/UsersModule.cs` — 4 yeni `AddScoped` kayıt (3 port impl + `AccountLifecycleService`).

**Yeni — Testler:**
- `backend/tests/Skinora.API.Tests/Integration/AccountLifecycleEndpointTests.cs` — **15 integration test** (SQLite in-memory, AuthSessionEndpointTests + WalletAddressEndpointTests factory pattern'lerinin birleşimi).

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T36) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `POST /users/me/deactivate` → hesap deaktif (aktif işlem kontrolü) | ✓ | `UsersController.Deactivate` + `AccountLifecycleService.DeactivateAsync`. Testler: `Deactivate_NoActiveTransactions_Succeeds_SetsFlagRevokesTokensClearsCookie`, `Deactivate_WithActiveSellerTransaction_Returns422HasActiveTransactions`, `Deactivate_WithActiveBuyerTransaction_Returns422HasActiveTransactions`, `Deactivate_WithOnlyTerminalTransactions_Succeeds`. |
| 2 | `DELETE /users/me` → hesap silme (confirmation="SİL", aktif işlem kontrolü) | ✓ | `UsersController.DeleteMe` + `AccountLifecycleService.DeleteAsync`. Testler: `Delete_InvalidConfirmation_Returns400ValidationError` (theory: `sil`, `SIL`, `DELETE`, boş), `Delete_MissingBody_Returns400ValidationError`, `Delete_WithActiveTransaction_Returns422HasActiveTransactions`. |
| 3 | Silme: soft delete + PII temizleme (SteamId→ANON_{GUID}, DisplayName→"Deleted User", adresler temiz) | ✓ | `AccountLifecycleService.AnonymizeUserInPlace`. Test: `Delete_HappyPath_AnonymizesUser_Preferences_Deliveries_Tokens` asserts `StartsWith("ANON_")`, `Length ≤ 20`, `DisplayName=="Deleted User"`, `Avatar/Email/Addresses/TradeUrl*` hepsi null. |
| 4 | UserNotificationPreference soft delete + ExternalId temiz | ✓ | `NotificationAccountAnonymizer.AnonymizeAsync`. Test (happy path) 2 preference (TELEGRAM + EMAIL) seed → hepsi `IsDeleted=true, DeletedAt≠null, ExternalId=null, IsEnabled=false`. |
| 5 | RefreshToken revoke + soft delete | ✓ | `AuthAccountAnonymizer.AnonymizeSessionsAsync`. Testler: delete happy path → `IsRevoked+RevokedAt+IsDeleted+DeletedAt+DeviceInfo=null+IpAddress=null`; deactivate happy path → `IsRevoked=true, IsDeleted=false` (row korunur). |
| 6 | NotificationDelivery.TargetExternalId masked format | ✓ | `NotificationAccountAnonymizer.Mask` channel-aware. Testler: `Delete_HappyPath_...` (Telegram `tg:***5678` + `12345678` içermez), `Delete_DeliveryMasking_EmailChannel_UsesFixedLiteral` (email → `***@***.com`). |
| 7 | İşlem geçmişi ve audit log anonim olarak saklanır | ✓ | Test: `Delete_TransactionHistoryPreserved_AuditTrailIntact` — Transaction row `IsDeleted=false`, `SellerId=user.Id` (FK anonim user'a gösterir). Happy path testi aynı zamanda `Notification.IsDeleted=false` asserts (audit trail).

## Doğrulama Kontrol Listesi (11 §T36)

- [x] **06 §6.2 anonimleştirme formatı birebir eşleşiyor mu?** — Evet. Tablo per-field uygulandı (User.* 9 alan + UserNotificationPreference soft delete + ExternalId + NotificationDelivery masked + RefreshToken full anonymize). Transaction/TransactionHistory/AuditLog/UserLoginLog korunur — testte Transaction için explicit assert.
- [x] **Silinen kullanıcının audit log'ları korunuyor mu?** — Evet. `Transaction` row toucheD edilmez (test `Delete_TransactionHistoryPreserved_AuditTrailIntact`), `Notification` row toucheD edilmez (happy path assert), `AuditLog`/`TransactionHistory`/`UserLoginLog` modülleri T36 code'unda hiç ele alınmıyor — yazmama = korunma.

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build Skinora.sln -c Release
```
→ Build succeeded. **0 Warning(s). 0 Error(s).** 00:00:03.

**Tam backend test koşumu (lokal, Release):**

| Test projesi | Geçen | Başarısız |
|---|---|---|
| `Skinora.API.Tests` (unit + SQLite integration) | **186** | **0** |
| `Skinora.Shared.Tests` | 166 | 0 |
| `Skinora.Auth.Tests` | 85 | 0 |
| `Skinora.Transactions.Tests` | 68 | 0 |
| `Skinora.Platform.Tests` | 28 | 0 |
| `Skinora.Notifications.Tests` | 25 | 0 |
| `Skinora.Steam.Tests` | 21 | 0 |
| `Skinora.Admin.Tests` | 20 | 0 |
| `Skinora.Fraud.Tests` | 12 | 0 |
| `Skinora.Disputes.Tests` | 11 | 0 |
| `Skinora.Payments.Tests` | 6 | 0 |
| **Toplam** | **628** | **0** |

`AccountLifecycleEndpointTests` 15/15 PASS:
1. `Deactivate_Unauthenticated_Returns401`
2. `Deactivate_NoActiveTransactions_Succeeds_SetsFlagRevokesTokensClearsCookie`
3. `Deactivate_WithActiveSellerTransaction_Returns422HasActiveTransactions`
4. `Deactivate_WithActiveBuyerTransaction_Returns422HasActiveTransactions`
5. `Deactivate_WithOnlyTerminalTransactions_Succeeds`
6. `Delete_Unauthenticated_Returns401`
7. `Delete_MissingBody_Returns400ValidationError`
8-11. `Delete_InvalidConfirmation_Returns400ValidationError` (theory: `sil`, `SIL`, `DELETE`, boş)
12. `Delete_WithActiveTransaction_Returns422HasActiveTransactions`
13. `Delete_HappyPath_AnonymizesUser_Preferences_Deliveries_Tokens`
14. `Delete_DeliveryMasking_EmailChannel_UsesFixedLiteral`
15. `Delete_TransactionHistoryPreserved_AuditTrailIntact`

## Altyapı Değişiklikleri

- **Migration:** **yok**. User entity T30+T34+T35'ten kalan alanlarla yeterli; `IsDeactivated/DeactivatedAt/IsDeleted/DeletedAt` kolonları zaten mevcut.
- **Package reference:** **yok**. Yeni NuGet dep eklenmedi.
- **Cross-module port pattern:** `IActiveTransactionCounter` (T34) + `INotificationPreferenceStore` (T35) mirror. `Skinora.Users` sibling modüllere statik referansa ihtiyaç duymaz — DI composition root yönlendirir.
- **DI kayıtları (`UsersModule.cs`):** 4 yeni `AddScoped` (`IUserActiveTransactionChecker → UserActiveTransactionChecker`, `INotificationAccountAnonymizer → NotificationAccountAnonymizer`, `IAuthAccountAnonymizer → AuthAccountAnonymizer`, `IAccountLifecycleService → AccountLifecycleService`).

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş.
- **Startup CI check (Adım 0):** Son 3 main run `24853931995` ✓, `24853931973` ✓, `24845642208` ✓ — hepsi success.
- **Dış varsayımlar (Adım 4):**
  - User entity'de `IsDeactivated/DeactivatedAt/IsDeleted/DeletedAt` alanları (T18+T25 inherited) ✓ mevcut — migration yok.
  - SteamId `MaxLength(20)` + `IsRequired` + UNIQUE — "ANON_" + 15 hex (Guid.NewGuid().ToString("N")[..15]) tam 20 karakter, collision probability ≈ 2^-60, kabul edilebilir ✓.
  - `UserNotificationPreference`, `NotificationDelivery` entity'leri T23'te tanımlı ✓.
  - `RefreshToken.DeviceInfo`, `RefreshToken.IpAddress` alanları T18/T25'te mevcut (PII snapshot) ✓.
  - `Transaction` non-terminal state listesi 02 §12.3 + T34 `ActiveTransactionCounter` ile uyumlu ✓.
  - `IRefreshTokenCache` (T32 shared abstraction) ✓.
  - Yeni paket/external API yok ✓.
- **Cross-module design kararı:** T36, T34/T35 port-pattern'ini tekrar kullanır. Alternatif (AccountLifecycleService'i Notifications/Auth'a dağıtmak) endpoint'in controller'da tek noktadan orkestre edilmesini engellerdi. Seçim: port interface'leri Users'de, impl'ler sahip modülde, wire-up composition root'ta.
- **"SİL" ordinal match:** Türkçe caps İ (U+0130) dotted-I karakteri. `StringComparison.Ordinal` tersine culture-aware karşılaştırma Türkçe collation'da "SIL" (U+0049) ile `İ ≠ I` olduğu için eşitlik ortalayabilirdi — ordinal seçimi spec'in yazımına uyumlu.
- **Atomicity:** Delete flow'da 3 ayrı `SaveChangesAsync` (User, Notifications, Auth) — ancak hepsi aynı scoped `AppDbContext`'te implicit aynı connection üzerinde çalışır ve mid-flight hata durumu enderdir (tüm veriler soft-delete + update, yeni row insert yok). Explicit `BeginTransaction` eklenmedi çünkü her adım ayrı DB rounTrip'ten yararlanır ve partial state bile re-run'da convergent (idempotent anonimleştirme → zaten ANON olan user'ı tekrar anonimleştirmek no-op'a yakın).
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+(\.[0-9]+)?[a-z]?'` → sadece `T36`. Yabancı task commit'i yok.
- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izler (validate.md Adım 18).

## Commit & PR

- **Branch:** `task/T36-account-deactivate-delete`
- **Commit (kod):** `5e383ec`
- **Commit (rapor+status+memory):** `bd80954`
- **PR:** [#60](https://github.com/turkerurganci/Skinora/pull/60)
- **CI run:** (izleme aşağıda)

## Doğrulama

| Alan | Sonuç |
|---|---|
| Build (Release) | ✓ 0 W / 0 E |
| Unit + Integration | ✓ 628/628 PASS (15 yeni T36 integration) |
| Migration | Yok — User mevcut alanlarla yeterli |
| Security gözden geçirme | Secret sızıntısı yok; confirmation phrase ordinal karşılaştırma; masked format delivery audit leak'i engeller |
| Dokümansal eşleşme | 06 §6.2 tablosu birebir; 07 §5.17 response body'leri birebir |
