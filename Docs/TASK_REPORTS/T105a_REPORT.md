# T105a — Hesap Askıya Alma (backend + S03d)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-06-05 (doğrulama 2026-06-06)

---

## Bağlam

Hesap askıya alma 02 §14.0/§16.2 + 03 §2.1/§8.3 + 04 §8.3/§16.2/S03d'de tarif edilir ama F5'te **hiçbir task'a atanmamıştı** (plan boşluğu). T100 S14 "Askıya Al" butonunu bu task'a devretti. Proje sahibi onayı (2026-06-05): **ayrı adanmış task** (T105 salt-okunur S20 olarak korundu — plan düzeltmesi), `main`'den dallandı (T100'den bağımsız), **kısıtlı oturum** enforcement modeli, **geçici blok dahil**.

## Yapılan İşler

- **User suspension state:** `User.IsSuspended/SuspendedAt/SuspensionReason/SuspensionExpiresAt` + EF migration `T105a_AddUserSuspension` (4 kolon, SYSTEM seed update).
- **AD20** `POST /admin/users/:userId/suspend` + **AD21** `DELETE /admin/users/:userId/suspend` (AdminController) — `MANAGE_FLAGS` yetkisi. Yeni `AdminUserSuspensionService` (Skinora.API/Services/UserSuspension): reason ≥10 + durationDays>0 validasyon, `IsSuspended` guard (409 AlreadySuspended / NotSuspended), audit `USER_BANNED`/`USER_UNBANNED` (mevcut enum reuse, ADMIN_ACTION map'li), outbox `AccountSuspendedEvent`/`AccountUnsuspendedEvent`, tek `SaveChanges`.
- **Enforcement (kısıtlı oturum):** fund-flow mutation guard'larına `&& !u.IsSuspended` eklendi — `TransactionCreationService` (seller), `TransactionAcceptanceService` (buyer, açık link dahil), `WalletAddressService` (defense-in-depth), **`TransactionCancellationService` (suspended caller reddedilir — `ACCOUNT_SUSPENDED` 403; review bulgusu, aşağıda)**. Read'ler + non-fund-flow ayarlar serbest. `/auth/me` `isSuspended` flag'i (`CurrentUserDto`). Her NotificationType'ın email kategori eşlemesi zorunlu (`EmailCategoryMapTests`) → 2 yeni tip `EmailCategory.Security` eklendi.
- **Geçici blok:** `AutoUnsuspendJob` (Hangfire recurring, 6 saatte bir) `SuspensionExpiresAt <= now` olanları SYSTEM aktörü ile otomatik kaldırır (audit + bildirim AD21 ile aynı yoldan). Kalıcı (ExpiresAt null) hiç dokunulmaz.
- **Bildirim:** yeni `ACCOUNT_SUSPENDED`/`ACCOUNT_UNSUSPENDED` NotificationType + EN/TR resx template (`{Reason}`) + 2 NotificationConsumer (outbox → MediatR → INotificationDispatcher).
- **AD16 zenginleştirme:** `AdminUserDetailProfileDto` suspension alanları + `SUSPENDED` status (admin observability).
- **Frontend:** `getMe()` + `MeResponse` (auth.ts) + `AuthInitializer` (token'ı localStorage'dan hydrate eder + `/auth/me` → `setProfile({isSuspended})`). S03d ekranı + auth-store `isSuspended` + MainShell SuspendedHeader switch **zaten vardı** (T85/T87) — sadece wiring eklendi.

## Etkilenen Modüller / Dosyalar

**Backend:** `User.cs`, `UserConfiguration.cs`, migration `T105a_AddUserSuspension`, `NotificationType.cs`, `AccountSuspendedEvent.cs`, `AccountUnsuspendedEvent.cs`, `Account{Suspended,Unsuspended}NotificationConsumer.cs`, `NotificationTemplates.resx` + `.tr.resx`, `IAdminUserSuspensionService.cs`, `AdminUserSuspensionService.cs`, `AutoUnsuspendJob.cs`, `AutoUnsuspendJobRegistrar.cs`, `Program.cs` (DI), `AdminController.cs` (AD20/AD21), `CurrentUserService.cs` (/auth/me), `TransactionCreationService.cs`, `TransactionAcceptanceService.cs`, `WalletAddressService.cs`, `AdminUserDtos.cs`, `AdminUserService.cs`. Tests: `AdminUserSuspensionEndpointTests.cs` (yeni, 11), `EnumTests.cs` (27), `TransactionCreationServiceTests.cs` (+1), `TransactionAcceptanceServiceTests.cs` (+1).

**Frontend:** `lib/api/auth.ts`, `lib/auth/AuthInitializer.tsx` (yeni), `lib/providers.tsx`.

**Docs:** `07_API_DESIGN.md` (§4.5 isSuspended, §9.26 AD20, §9.27 AD21), `06_DATA_MODEL.md` (User fields), `11_IMPLEMENTATION_PLAN.md` (T105a satırı).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | User suspension state + migration | ✓ | 4 kolon + `T105a_AddUserSuspension` Up/Down doğrulandı |
| 2 | POST suspend (reason≥10, durationDays null/N) — MANAGE_FLAGS | ✓ | AD20 + `AdminUserSuspensionEndpointTests` (permanent/temp/validation/409) |
| 3 | DELETE unsuspend — MANAGE_FLAGS | ✓ | AD21 + test (clears fields / 409 NotSuspended) |
| 4 | Enforcement: kısıtlı oturum (login serbest, fund-flow mutation reddedilir, read serbest, /auth/me isSuspended) | ✓ | 3 guard + `/auth/me` testi + create/accept enforcement testleri (CI) |
| 5 | Geçici blok auto-unsuspend job | ✓ | `AutoUnsuspendJob` + test (expired lifted, permanent/future untouched) |
| 6 | ACCOUNT_SUSPENDED/UNSUSPENDED bildirim + USER_BANNED/UNBANNED audit | ✓ | 2 event + 2 consumer + resx; audit reuse |
| 7 | S03d + /auth/me isSuspended → SuspendedHeader | ✓ | T85/T87 S03d + AuthInitializer wiring |

## Doğrulama (Bağımsız Validator — 2026-06-06)

> Validator yapım raporunu **görmeden** bağımsız verdict oluşturdu: HARD STOP kapıları → 25-ajanlı (7 boyut + adversarial verify, refute-default) spec-conformance + güvenlik review → sentez. Sonra rapor karşılaştırıldı.

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (bloklayıcı yok) |
| Verdict yöntemi | Bağımsız; 7/7 kabul kriteri fonksiyonel ✓; S1/S2 yok; kritik güvenlik yok; CI 11/11 |
| Reconciliation | T105a `main`@T99 üzerine kuruluydu → main (T100 #148 + T100a #150) merge edildi (3 doc/memory çatışması çözüldü); reconciled CI `27067117970` 11/11 ✓ |
| Bulgular | 9 (5 S3 + 4 advisory); **#1–#7 aynı branch'te düzeltildi**, #8/#9 KL'ye devredildi |

**"ayar/settings guard'sız" sorusu — spec-uygun (bulgu değil):** AC4 literal "cüzdan/ayar" dese de spec 04 §6.7:694 kısıtlı-oturum fund-flow setini "yeni işlem, kabul, açık link" ile sınırlar; settings (Discord/Telegram/dil/bildirim-tercihi/email/Steam-trade-URL) non-fund-flow → doğru biçimde serbest ("read'ler serbest"). Kod spec'ten *fazlasını* bile guard'lar (wallet adres + cancel). 02-10 source-of-truth (INSTRUCTIONS §4) → settings guard'sızlığı doğru; "ayar" = cüzdan ayarı (guard'lı adres).

**Düzeltilen bulgular (validator review → aynı branch fix):**
- **#1 (S3)** `durationDays` üst sınır yok → `AddDays` overflow → 500. **Fix:** `MaxDurationDays=10_000` validasyonu → temiz 400 (`AdminUserSuspensionService`). Test: `SuspendUser_DurationDaysTooLarge_Returns400`.
- **#6 (adv)** `reason` üst sınır yok → >500 char 500 (sibling `AdminSanctionsService`'te guard vardı). **Fix:** `MaxReasonLength=500` validasyonu → 400. Test: `SuspendUser_LongReason_Returns400`.
- **#2 (S3)** wallet-adres enforcement guard'ı testsizdi (4 fund-flow guard'ından tek testsiz). **Fix:** `WalletAddressEndpointTests.UpdateSellerWallet_SuspendedUser_IsRejected_AndLeavesAddressUnchanged`.
- **#3 (S3)** AD20/AD21 testleri audit + notification event yan etkilerini assert etmiyordu. **Fix:** `Suspend/UnsuspendUser_WritesAudit_AndPublishesNotificationEvent` (USER_BANNED/UNBANNED + AccountSuspended/Unsuspended outbox).
- **#4 (S3)** Account(Un)Suspended consumer davranış testi yoktu. **Fix:** yeni `AccountSuspensionNotificationConsumerTests` (4 test — ACCOUNT_SUSPENDED+Reason / ACCOUNT_UNSUSPENDED + idempotency).
- **#5 (S3)** S06 (yeni işlem) suspended kullanıcıda form gate edilmemişti (S07 ile tutarsız). **Fix:** `transactions/new/page.tsx` suspended'da SuspendedBanner gösterir, formu render etmez (backend create-guard defense-in-depth korunur).
- **#7 (adv)** `SuspendedHeader` default `supportUrl='/support'` = 404 route. **Fix:** `NEXT_PUBLIC_SUPPORT_URL ?? "mailto:..."` (sibling info-ekranlarıyla hizalı) + mailto scheme handling.

**Known Limitations (devredilen, bloklamaz):**
- **#8 (adv)** Suspend/unsuspend testleri `MANAGE_FLAGS` permission'ını izole etmiyor (happy-path SuperAdmin token → bypass; sadece NonAdmin 403 var); cancel suspended-**buyer** yolu testsiz. Prod gate doğru; konvansiyon-aykırı test boşluğu → follow-up.
- **#9 (adv)** S03d route `/auth/suspended` vs spec 04 §2.2 `/account-suspended` — T87'den miras, tüm S03a-d ailesi `/auth/*` konvansiyonu izliyor; T105a'ya özgü değil → doc-vs-kod uzlaştırma T-future.

**Adversarial verify ile elenen (bilgi):** 401-vs-403 tutarsızlığı (spec status dayatmıyor), notification reason echo (spec-conformant, cross-user değil), auto-unsuspend loop izolasyonu (design-as-intended self-healing), mid-session SignalR redirect (plan K2 onaylı deferral), S03d başlık paraphrase. **Temiz boyutlar (0 gerçek bulgu):** migration/entity/enum, AutoUnsuspendJob, suspension servis correctness.

**Güvenlik:** auth değişmedi (Admin + MANAGE_FLAGS policy, bare [Authorize] değil); IDOR yok (server-türetimli userId); kalıcı ban auto-lift edilemez (AutoUnsuspendJob `SuspensionExpiresAt != null` guard); notification yalnız affected user'a (cross-user PII yok); 0 yeni dış bağımlılık.

**Rapor karşılaştırması:** Verdict uyumlu (ikisi de PASS). Rapor #1/#2/#3/#4/#5/#7'yi belgelememişti; validator review yüzeye çıkardı ve aynı branch'te düzeltildi. Raporun kendi review'ı 2 gerçek defect yakalamış (ITEM_ESCROWED cancel guard — önemli güvenlik fix'i + 06 NotificationType doc parity) ✓.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| dotnet format | ✓ Δ=0 | whole-solution `--verify-no-changes` exit 0 |
| AdminUserSuspensionEndpointTests (SQLite, **validator +4**) | ✓ 15/15 | AD20/AD21 + temp + /auth/me + AutoUnsuspendJob + LongReason/DurationDaysTooLarge 400 + Suspend/Unsuspend audit+outbox side-effect (lokal) |
| WalletAddressEndpointTests (SQLite, **validator +1**) | ✓ 12/12 | + `UpdateSellerWallet_SuspendedUser` (401, adres değişmez) |
| AccountSuspensionNotificationConsumerTests (**validator yeni**) | ✓ 4/4 | ACCOUNT_SUSPENDED+Reason / ACCOUNT_UNSUSPENDED + idempotency (lokal) |
| Shared EnumTests | ✓ 194/194 | NotificationType 27 + 2 yeni InlineData |
| Frontend | ✓ | `tsc` 0 + eslint 0 + prettier clean + `next build` 25 route (S06 suspended-gate + `/auth/suspended`) |
| Reconciled CI (main merged + validator fixes) | ✓ 11/11 | run [`27067117970`](https://github.com/turkerurganci/Skinora/actions/runs/27067117970) — Lint/Build/Unit/Integration/Contract/Migration/Docker×2/Gate (SQL Server) |

## Altyapı Değişiklikleri
- Migration: **Var** — `T105a_AddUserSuspension` (Users: +4 nullable/bool kolon).
- Hangfire: yeni recurring job `auto-unsuspend` (cron `0 */6 * * *`, hardcoded — SystemSetting yok).
- Yeni dış bağımlılık: Yok. Yeni permission: Yok (MANAGE_FLAGS reuse). Yeni AuditAction: Yok (USER_BANNED/UNBANNED reuse). Yeni NotificationType: 2.

## Commit & PR
- Branch: `task/T105a-account-suspension`
- PR: [#149](https://github.com/turkerurganci/Skinora/pull/149)
- CI: ✓ PASS — run [`27037408161`](https://github.com/turkerurganci/Skinora/actions/runs/27037408161) (HEAD `8cba072`, tüm job'lar: Build/Unit/Integration/Contract/Migration dry-run/Docker/CI Gate). Integration test SQL Server'da suspension endpoint + create/accept/cancel enforcement + AutoUnsuspendJob testlerini doğruladı.
- BYPASS_LOG: 1 kayıt (`ci-failure` Layer 2) — önceki run'ın CI Unit-test failure'ını (EmailCategoryMap) düzelten commit'i push ederken; bu commit o failure'ı çözdü.

## Known Limitations / Follow-up
- **K1 — S14 "Askıya Al" buton wiring T100 merge sonrası:** T100 (PR #148) merge olunca `FlagDetailView`'daki disabled buton AD20'ye bağlanır (dal main'e rebase + 1 dosyalık follow-up). Şu an T105a `main`'den dallandığı için `FlagDetailView` bu dalda yok.
- **K2 — SignalR canlı force-restrict ertelendi:** Oturumdaki bir kullanıcı askıya alındığında anlık push yok; sonraki istek/login (`/auth/me`) suspended'ı algılar.
- **K3 — Enforcement kapsamı:** 02 §14.0 fund-flow (oluştur/kabul) + cüzdan + **iptal (cancel)**. Create/accept reddedilen mutation'lar mevcut `*NotFound` outcome'unu döndürür (kısıtlı oturum birincil UX kapısı; backend defense-in-depth); cancel ise `ACCOUNT_SUSPENDED` (403) döner. **Not (review düzeltmesi):** İlk taslakta cancel guard'sızdı; çok-ajanlı review ITEM_ESCROWED iptalinin `ItemRefundToSellerRequestedEvent` ile emanetteki item'ı satıcının Steam envanterine geri çektiğini (cüzdan freeze'i bu kanalı kapsamaz) tespit etti → guard eklendi (suspended caller iptal edemez, item emanette kalır).

## Çok-Ajanlı Diff Review (ultracode)
4-boyutlu (security/enforcement, backend correctness, spec-conformance, frontend) paralel review + adversarial verify: **6 bulgu → 2 gerçek defect** (ikisi de düzeltildi):
1. **[medium, security]** Suspended kullanıcı ITEM_ESCROWED işlemini iptal edip emanetteki item'ı geri çekebiliyordu (cancel guard yoktu; cüzdan freeze'i Steam envanter kanalını kapsamaz). → `TransactionCancellationService` suspended-caller guard + `CancelTransactionStatus.AccountSuspended`/403 + `TransactionErrorCodes.AccountSuspended`; test `Rejects_Cancel_When_Caller_Suspended_And_Does_Not_Release_Escrow` (item-refund event yayınlanmaz).
2. **[medium, spec]** 06 §2.13 NotificationType doc tablosu güncel değildi (enum 27, tablo 20). → 7 eksik satır eklendi (T59 ×2 + T72 ×3 + T105a ×2), 1:1 parity restore.
Diğer 4 bulgu low/false-positive (verify ile elendi). **CI Unit-test bulgusu (review'dan bağımsız):** `EmailCategoryMapTests.Resolve_EveryNotificationTypeHasMapping` 2 yeni NotificationType için eşleme istedi → `EmailCategoryMap`'e eklendi.
- **K4 — resx es/zh:** ACCOUNT_SUSPENDED/UNSUSPENDED yalnız EN+TR (mevcut partial-resx konvansiyonu — es/zh default EN'e fallback eder).
- **K5 — T105 (S20) ayrı:** Salt-okunur kullanıcı detay sayfası kendi task'ında kalır; AD16 zaten suspension alanlarını döndürür (hazır).

## Notlar
- **Working tree (Adım -1):** temiz (T100 commit'li, `main`'e checkout + pull sonrası dallanıldı).
- **Main CI startup (Adım 0):** son merge'ler T99 #147 + T98 #146 success (T100 #148 henüz merge değil — bağımsız doğrulama/merge bekliyor).
- **Dış varsayımlar (Adım 4):** /auth/me (T32) + CurrentUserService User entity yükler ✓; INotificationDispatcher + NotificationConsumerBase + resx (T37) ✓; MANAGE_FLAGS PermissionCatalog'da ✓; USER_BANNED/UNBANNED enum + AuditLogCategoryMap ADMIN_ACTION ✓; S03d /auth/suspended + auth-store isSuspended + MainShell SuspendedHeader (T85/T87) ✓; SteamAuthenticationPipeline suspended login'i engellemez (değişiklik yok, kısıtlı oturum modeli) ✓.
- **Scope kararları (proje sahibi onayı 2026-06-05):** ayrı task (T105 read-only korundu), main'den başla + buton wiring sonra, temp-block dahil, kısıtlı oturum enforcement, yeni NotificationType + MANAGE_FLAGS.
