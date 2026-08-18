# T132 — Backend bot/dispatch/webhook/recovery yüzeyi silme [RİSKLİ]

**Faz:** F7 (P6 — Emeklilik) | **Durum:** ⏳ Devam ediyor (yapım bitti + CI ✓ PASS, doğrulama bekliyor) | **Tarih:** 2026-08-19

---

## Neden bu turda "silinecek entity" yok — kapsamın ölçülmesi

Plandaki birinci kabul kriteri (*"Bot entity'leri, dispatch job, recovery, admin
endpoint'leri kaldırıldı"*) **T117'de fiilen karşılanmıştı**: enum'dan değer silmek
136 dosyayı birden kırdığı için T117 + T118 + T132 proje sahibi onayıyla tek dalda
birleştirilmiş, `TradeOffer` / `PlatformSteamBot` / `BotRecoveryItem` entity'leri, bot
seçimi, dispatch, recovery ve `SteamWebhooksController` o dalda silinmişti (Steam modülü
35 → 11 dosya, 3 tablo düştü).

Göreve başlarken yapılan ölçüm, geriye **çalışmayan ama duran** bir yüzeyin kaldığını
gösterdi: yazıcısı olmayan sabitler ve hâlâ yayınlanan sözleşme girdileri. Bunlar
derlemeyi kırmadıkları için T117'nin radarına girmemişlerdi, ama bir sonraki turlarda
(T134 / T136 frontend, T133 sidecar) "neyi silelim" sorusunu bulandırıyorlardı.

Proje sahibi onayıyla T132'nin bu turdaki kapsamı **kalıntının kapatılması** olarak
netleştirildi (plan §P6 T132 "KAPSAM NETLEŞTİRMESİ" bloğuna yazıldı):

| # | Kalıntı | Ölçülen durum |
|---|---|---|
| **A** | 4 ölü `AuditAction` bot değeri + `AuditLogCategoryMap` girdileri | Üretimde **tek yazıcısı yok** |
| **B** | `VIEW_STEAM_ACCOUNTS` + `MANAGE_STEAM_RECOVERY` permission'ları | Hiçbir endpoint enforce etmiyor, ama **AD11 cevabında yayınlanıyor** |
| **C** | `AdminBotStatusChanged` realtime kanalı | Üretimde **tek çağıranı yok**, yalnız 5 test double'ı taşıyor |
| **D** | `/api/v1/webhooks/steam` middleware dalı + `SteamSharedSecret` | Ardında **serve edilen uç yok** (T117'den beri 404) |
| **E** | Emekli katmanı anlatan bayat XML doc / yorumlar | Kod ↔ yorum driftinin kaynağı |

---

## Yapılan İşler

### A — Ölü `AuditAction` bot değerleri (4)
- `BOT_STATUS_CHANGED` (T69), `BOT_SESSION_FAILED` (WP8), `BOT_RECOVERY_ITEM_CREATED`
  ve `BOT_RECOVERY_UPDATED` (T103b-2) enum'dan kaldırıldı → **33 → 29 değer**.
- `AuditLogCategoryMap`'teki 4 kategori girdisi (3 × `SECURITY_EVENT` + 1 ×
  `ADMIN_ACTION`) kaldırıldı.
- **Silme kodu spec'e hizalar, spec'ten uzaklaştırmaz:** [`06 §2.19`](../06_DATA_MODEL.md)
  `AuditAction` tablosu bu dördünü **zaten içermiyordu** (T115 doküman turunda
  düşmüşlerdi). Drift'in bayat tarafı koddu; 06'da tek satır değişmedi.

### B — Ölü permission'lar (katalog 14 → 12)
- `PermissionCatalog`'tan `VIEW_STEAM_ACCOUNTS` ve `MANAGE_STEAM_RECOVERY` kaldırıldı.
- Koruyacakları uçlar (AD10 `GET /admin/steam-accounts`, AD25 recovery-queue, AD26
  recovery PATCH) v3.0'da zaten kaldırılmıştı; katalogda kalmaları **var olmayan bir
  ekran (S18) için rol tanımlanabilmesine** yol açıyordu.
- **Sözleşme değişikliği olduğu için doküman yarısı aynı PR'da** (INSTRUCTIONS §4 —
  kod dokümanla çelişik bırakılmaz, bir task boyunca bile):
  - `07 §9` permission tablosundan `VIEW_STEAM_ACCOUNTS` satırı,
  - `07 §9.11` `availablePermissions` JSON'undan iki girdi + bayat `MANAGE_STEAM_RECOVERY`
    notu (yerine kaldırma notu yazıldı),
  - `04 §8.8` yetki matrisinden iki satır (+ kaldırma notu).
  - Sürüm notları: **07 v3.6 → v3.7**, **04 v4.3 → v4.4**.

### C — Ölü realtime kanalı
- `NotificationRealtimePayloads.AdminBotStatusChanged` record'u,
  `INotificationRealtimePublisher.PublishAdminBotStatusChangedAsync` portu ve
  `SignalRNotificationRealtimePublisher`'daki `AdminBotStatusChanged` event sabiti +
  gönderim metodu kaldırıldı.
- 5 test double'ından (`AdminMaintenanceEndpointTests`, `HotWalletMonitorServiceTests`,
  `ReconciliationServiceTests`, Notifications + Steam `RecordingNotificationRealtimePublisher`)
  ve `NotificationRealtimePublisherTests`'in kanal testinden temizlendi.

### D — Steam webhook yüzeyinin backend yarısı
- `WebhookSignatureMiddleware`'den `SteamWebhookPathPrefix`, `SteamNonceSource`,
  `WebhookRoutes` satırı ve `SelectSecret`'in `"steam"` kolu kaldırıldı.
- `WebhookSettings.SteamSharedSecret` + `appsettings.json` girdisi +
  `docker-compose.yml` / `docker-compose.e2e.yml` env satırları kaldırıldı.
- **İkinci kabul kriteri (HMAC/nonce altyapısı KORUNDU) bozulmadı:** middleware'in
  kendisi, `ProcessedNonces` tablosu, `ProcessedNonceCleanupJob`, replay penceresi ve
  blockchain dalı yerinde. Çok-sidecar mimarisi de korundu — yeni bir sidecar hâlâ
  `WebhookRoutes`'a bir satır + bir secret.
- **T133'ü beklemiyor:** bu yollar T117'den beri (controller silindiğinden) zaten 404
  dönüyordu. Kaldırma, sidecar'ın gördüğü cevabı 401→404 dışında değiştirmez ve
  `SidecarWebhookRouteContractTests`'in adı konmuş istisnası ile bekçi testi
  (`RetiredPathsAreStillPublished_UntilT133`) etkilenmez — o test backend
  **route**'larına bakar, middleware'e değil.

### E — Bayat XML doc / yorumlar
- `IAdminDashboardService` ("platform Steam-bot snapshot" — DTO'da böyle bir alan
  T117'den beri yok), `IAdminRecipientResolver` (`ADMIN_STEAM_BOT_ISSUE` — bu
  `NotificationType` v3.0'da kaldırılmıştı), `Program.cs` (Steam modülü kaydı +
  dashboard composer + webhook middleware yorumları), `AuditAction` ve
  `AuditLogCategoryMap`'te kalan iki "bot-status" atfı düzeltildi.

---

## Etkilenen Modüller / Dosyalar

**Kaynak (12 dosya):**

| Dosya | Değişiklik |
|---|---|
| `backend/src/Skinora.Shared/Enums/AuditAction.cs` | 4 bot değeri kaldırıldı (33 → 29) |
| `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs` | 4 kategori girdisi kaldırıldı |
| `backend/src/Modules/Skinora.Admin/Application/Permissions/PermissionCatalog.cs` | 2 permission kaldırıldı (14 → 12) |
| `backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs` | `AdminBotStatusChanged` payload kaldırıldı |
| `backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs` | `PublishAdminBotStatusChangedAsync` portu kaldırıldı |
| `backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs` | Event sabiti + gönderim kaldırıldı |
| `backend/src/Skinora.API/Middleware/WebhookSignatureMiddleware.cs` | Steam prefix / nonce source / secret kolu kaldırıldı |
| `backend/src/Skinora.API/Middleware/WebhookSettings.cs` | `SteamSharedSecret` kaldırıldı |
| `backend/src/Skinora.API/appsettings.json` | `Webhook:SteamSharedSecret` kaldırıldı |
| `backend/src/Skinora.API/Services/IAdminDashboardService.cs` | XML doc |
| `backend/src/Skinora.Shared/Interfaces/IAdminRecipientResolver.cs` | XML doc |
| `backend/src/Skinora.API/Program.cs` | 3 bayat yorum |

**Altyapı (2 dosya):** `docker-compose.yml`, `docker-compose.e2e.yml` —
`Webhook__SteamSharedSecret` env satırı.

**Test (11 dosya):** `EnumTests` (33 → 29 + 4 InlineData), `AuditLogCategoryMapTests`
(4 InlineData + `ADMIN_ACTION` sayımı 18 → 17 + `SECURITY_EVENT` sıra listesi),
`AdminRolesEndpointTests` (AD11 katalog 14 → 12 + iki `DoesNotContain` guard'ı),
`BlockchainWebhookEndpointTests` (izolasyon testi yeniden çerçevelendi — aşağıya bkz.),
`ResendWebhookEndpointTests`, `NotificationRealtimePublisherTests` + 5 test double'ı.

**Doküman (4 dosya):** `07_API_DESIGN.md` (v3.7), `04_UI_SPECS.md` (v4.4),
`11_IMPLEMENTATION_PLAN.md` (T132 kapsam netleştirmesi + T133a'ya yeni kriter),
`DEFERRED_BACKLOG.md` (`P2P-BotCodeArchive` arşiv işaretçisi).

### Testte korunan kapsam — silinmedi, yeniden çerçevelendi

`BlockchainWebhookEndpointTests.PaymentDetected_SteamSecret_DoesNotAuthenticateBlockchain`
testi, "farklı bir secret ile **doğru biçimde** hesaplanmış HMAC blockchain yolunda kabul
edilmemeli" iddiasını kanıtlıyordu — bu, yanındaki `InvalidSignature` (çöp imza)
testinden **daha güçlüdür**: secret **seçicisinin** doğru secret'ı aldığını gösterir,
yalnızca bir imzanın doğrulandığını değil. `SteamSharedSecret` kaldırıldığı için test
silinmedi, `ForeignSidecarSecret` adıyla yeniden çerçevelendi (hiç register edilmeyen bir
secret — izolasyon iddiasını anlamlı kılan şey tam olarak budur).

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bot entity'leri, dispatch job, recovery, admin endpoint'leri kaldırıldı | ✓ | **Entity/job/uç katmanı T117'de** (`82bff4d`). Bu turda kalan ölü yüzey kapatıldı: `grep -rniE "PlatformSteamBot\|BotRecoveryItem\|TradeOfferDispatch\|BotPool\|SteamWebhooksController" backend/src --include=*.cs` (Migrations hariç) → **0 satır**; `grep -rn "AdminBotStatusChanged\|BOT_STATUS_CHANGED\|BOT_SESSION_FAILED\|BOT_RECOVERY" backend/src backend/tests --include=*.cs` → **0 satır**; `PermissionCatalog.All.Count` = 12 (`AdminRolesEndpointTests` AD11 cevabında assert eder) |
| 2 | Webhook HMAC/nonce altyapısı KORUNDU (blockchain sidecar paylaşımlı) | ✓ | `WebhookSignatureMiddleware` + `ProcessedNonces` + `ProcessedNonceCleanupJob` + `ReplayWindowSeconds`/`NonceRetentionSeconds` yerinde; blockchain dalı ve `BlockchainSharedSecret` dokunulmadı. `BlockchainWebhookEndpointTests` (imza doğrulama, replay/nonce, yabancı-secret izolasyonu, iki kritik yol) **yeşil**; `SidecarWebhookRouteContractTests` `CriticalSidecarRoute_IsServedByBackend` iki blockchain yolunu doğrulamaya devam ediyor |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ **0 Error / 0 Warning** | `dotnet build` (backend/Skinora.sln) |
| Unit | ✓ **1424/1424** | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — 11 proje. Enum değeri değiştiği için **tam** Unit-filter süiti koşuldu (kardeş projelerdeki parity testleri: `Shared.EnumTests` 388/388, `Platform.AuditLogCategoryMapTests` 120/120) |
| Integration | ✓ **1336/1337** (1 düşen = ortam, aşağıya bkz.) | `--filter "FullyQualifiedName~.Integration"` — 10 proje |
| Contract | ✓ **9/9** | `--filter "FullyQualifiedName~.Contract"` — `Shared` 5/5 + `API` 4/4 (`SidecarWebhookRouteContractTests` dahil) |

### Lokal ortam kaynaklı iki gürültü — ikisi de kod değil, kanıtıyla

**1. Docker kapalıyken 16 channel testi (unit).** İlk unit koşumunda
`Skinora.Notifications.Tests`'in Telegram/Discord channel testleri
`DockerEndpointAuthenticationProvider` hatasıyla düştü — Docker Desktop kapalıydı.
Docker açıldıktan sonra **aynı kod, aynı komut → 111/111**. Kayıt, "yeşil değildi ama
sonra yeşil oldu" adımının sessizce atlanmaması için burada duruyor.

**2. `AuthReVerifyEndpointTests.CheckAuthenticator_Authenticated_SidecarUnreachable_FailsClosed`
(integration) — T132 regresyonu DEĞİL, kanıtlandı.**

- **Baseline kanıtı (belirleyici):** T132 değişiklikleri `git stash`'lendi, **temiz main**
  derlenip aynı test koşuldu → **aynı hatayla düştü** (`Assert.False() Failure,
  Expected: False, Actual: True`). Dolayısıyla neden bu dalda değil.
- **Mekanizma (kök sebep bulundu):** Test "sidecar ayakta değil, probe fail-closed olur"
  varsayımına dayanıyor. `WebApplicationFactory` **Development** ortamında koşuyor ve
  [`appsettings.Development.json`](../../backend/src/Skinora.API/appsettings.Development.json)
  `SteamSidecar:BaseUrl`'ü **`http://localhost:5100`**'e çekiyor. Bu makinede o portta
  önceki bir oturumdan kalmış bir `node` süreci (PID 23548) dinliyor — yani probe
  gerçek bir cevap alıyor ve `active=true` dönüyor. Testin premisi lokalde ihlal,
  CI'da (izole container, port boş) geçerli.
- **Yetkili kanıt CI'dır:** main'in son üç run'ı `success` ve integration bacağı bu testi
  içeriyor. Dal CI'ı da aşağıdaki "Commit & PR" bölümünde kayıtlı.
- **Süreci öldürmedim** — T132'nin işi değil ve kullanıcının makinesinde istenmemiş bir
  yan etki olurdu. Baseline karşılaştırması zaten belirleyici.

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat — INSTRUCTIONS §3.3 izolasyon kuralı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

---

## Altyapı Değişiklikleri

- **Migration:** Yok. Hiçbir tablo/kolon/constraint değişmedi. `AuditAction` global
  `EnumToStringConverter` ile string olarak persist edilir (`AppDbContext.cs:51-66`) ve
  `AuditLogs.Action` şeması `nvarchar(100)` olarak sabit — enum değeri kaldırmak şemayı
  etkilemez.
- **Config/env değişikliği: VAR.** `Webhook__SteamSharedSecret` artık okunmuyor:
  `appsettings.json` girdisi ve iki compose dosyasındaki env satırı kaldırıldı.
  **Operasyonel etki yok** — `WEBHOOK_SECRET` değişkeni sidecar tarafında T133'e kadar
  yerinde kalır, backend tarafında yalnızca artık kimsenin okumadığı bir bağlama düştü.
  `DEPLOY_RUNBOOK` §Ortam Değişkenleri `WEBHOOK_SECRET`'i "backend + sidecar'lar" diye
  tanımlıyor; **T133 sidecar yarısını kapattığında bu satır da güncellenmeli**
  (aşağıda "Known Limitations").
- **Docker değişikliği:** Yalnız yukarıdaki iki env satırı; image/servis tanımı değişmedi.

---

## Commit & PR

- Branch: `task/T132-backend-bot-surface-removal` (güncel `main` `f9d9896` üzerinden kesildi)
- Commit: `9e64906` — T132: backend bot/webhook/recovery kalintisinin silinmesi
- PR: **[#247](https://github.com/turkerurganci/Skinora/pull/247)**
- Branch izolasyon check: `git log main..HEAD --format=%s | grep -oE ^T[0-9]+... | sort -u` → **yalnız `T132`**
- CI: ✓ **PASS** — dal HEAD `ad853e5` run [`32190325806`](https://github.com/turkerurganci/Skinora/actions/runs/32190325806)
  `conclusion=success`. Bloke edici jobların **hepsi** yeşil: `1. Lint` · `2. Build` ·
  `3. Unit test` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` ·
  `7. Docker build (backend)` · **`CI Gate`**. `0. Guard (direct push)` tasarım gereği
  `skipped`; `3b. JS test (vitest)` `skipped` (tur hiçbir JS paketine dokunmadı).
  İlk run [`32190127423`](https://github.com/turkerurganci/Skinora/actions/runs/32190127423)
  (`9e64906`) öz-denetim commit'i push'lanınca concurrency group tarafından
  **cancelled** edildi — task.md concurrency notu gereği `failure` sayılmaz, yetkili
  olan son tamamlanmış run'dır.

### Advisory E2E ölçümü — compose değişikliğinin inert olduğu KANITLANDI

8 advisory E2E leg'i kırmızı; bu T117'den beri bilinen ve **T138'e ait** olan durum
(spec'ler hâlâ emekli custody akışını sürüyor). Ama bu tur `docker-compose.e2e.yml`'a
dokunduğu için (`Webhook__SteamSharedSecret` env satırı) ölçümün **kaymadığını**
göstermek gerekiyordu — T137'nin dersi tam olarak budur: advisory sinyal bloke
etmediği için değil, **kimse bakmadığı için** ölür.

Run `32190325806`'nın leg loglarından sayıldı:

| Leg | T137 tabanı | T132 HEAD |
|---|---|---|
| happy-path | 0/1 | **0/1** |
| T108 cancellation | 0/4 | **0/4** |
| T109 timeout | 1/4 | **1/4** |
| T110 payment edge cases | 0/6 | **0/6** |
| T111 fraud-flags | 3/4 | **3/4** |
| T112 emergency-hold | 0/3 | **0/3** |
| T113 admin-flows | 6/7 | **6/7** |
| T114 downtime | 0/3 | **0/3** |
| **TOPLAM** | **10/32** | **10/32** |

Sayı **ve** leg dağılımı birebir aynı → `Webhook__SteamSharedSecret` env satırının
kaldırılması e2e yığınında hiçbir şeyi değiştirmedi; D'nin "davranış değişmez"
iddiası tahmin değil, ölçüm.

---

## Known Limitations / Follow-up

1. **`DEPLOY_RUNBOOK` `WEBHOOK_SECRET` satırı** hâlâ "backend + sidecar'lar" diyor.
   Backend yarısı bu turda düştü ama sidecar yarısı T133'e ait; satırın **tek turda**
   güncellenmesi doğru olacağından T133'e bırakıldı. Bugünkü hâli yanıltıcı değildir
   (değişken hâlâ blockchain sidecar'ı için gerekli).
2. **Frontend bot kalıntısı bu turda değil:** `frontend/src/types/enums.ts`'te
   `PlatformSteamBotStatus` enum'u ve 4 bot `AuditAction` değeri, `lib/api/admin.ts`
   `updateBotRecoveryItem`, `lib/hooks/useAdminSteamAccounts.ts`. Sahipleri **T134**
   (FE enum turu) ve **T136** (admin bot sayfaları). FE, S19 yetki matrisini AD11'den
   render ettiği için B'nin kaldırılması FE'yi kırmaz — `RoleFormModal` zaten
   veri-güdümlüdür.
3. **`sidecar-steam` hâlâ emekli yollara POST ediyor** (`/api/v1/webhooks/steam/bot-events`,
   `/trade-events`). Bu, T117'den beri bilinen ve `SidecarWebhookRouteContractTests`'te
   adı konmuş bir istisnayla sınırlanan drift; sahibi **T133**. T132 bunu ne büyüttü ne
   küçülttü — yollar önce de 404'tü, şimdi de 404.
4. **Yetki kataloğunun üç nüshası henüz birebir değil — ikisi T132 ÖNCESİNDEN eksik.**
   Normatif nüsha kod (`PermissionCatalog`, 12) ve 07 §9.11 onunla hizalı (12). Ama
   **07 §9 permission tablosu 11 satır** (`MANAGE_SANCTIONS` eksik, T82'den beri) ve
   **04 §8.8 matrisi 10 satır** (`VIEW_DISPUTES` / `MANAGE_DISPUTES` eksik, WP5'ten
   beri). İkisi de bu turda kapatılmadı çünkü **kaldırma değil ekleme** işidir ve
   03/04/07 hizalama turu T133a'dır — oraya **kabul kriteri olarak** yazıldı (plan
   §P7 T133a, doğrulama yöntemiyle birlikte); her iki tablonun altına "Bilinen açık"
   notu düşüldü ve notlar kriter kapandığında **silinmek üzere** işaretlendi.
   **Öz-denetimde yakalandı:** kapsam sunumunda iki drift de ölçülmüştü ama ilk
   yazımda yalnız 04 §8.8'e sahip atanmıştı — ölçülüp sahipsiz bırakılan açık,
   T137'nin B1 bulgusunun ta kendisidir.

---

## Notlar

### Kapılar (task.md Adım -1 / Adım 0)

- **Working tree:** temiz (Adım -1, `git status --short` boş).
- **Main CI startup check (Adım 0):** son 3 tamamlanmış run'ın üçü de `success` —
  [`32180658440`](https://github.com/turkerurganci/Skinora/actions/runs/32180658440),
  [`32180658381`](https://github.com/turkerurganci/Skinora/actions/runs/32180658381),
  [`32133727296`](https://github.com/turkerurganci/Skinora/actions/runs/32133727296).
- **Bağımlılıklar:** T127 ✓ Tamamlandı (2026-08-16), T130 ✓ Tamamlandı (2026-08-17).
- **Dal, güncel main'den kesildi:** `f9d9896` (T137 squash merge'ü, PR #246).

### Dış Varsayımlar (task.md Adım 4 — ön-uçuş kontrolü)

| # | Varsayım | Kanıt | Sonuç |
|---|---|---|---|
| 1 | `AuditLogs.Action` string olarak persist edilir; enum değeri silmek eski satırların **okunmasını** kırar | `AppDbContext.cs:51-66` global `EnumToStringConverter<>` | ✓ Doğrulandı — risk gerçek, aşağıdaki 2. maddeyle karşılandı |
| 2 | Prod veritabanı yok → bot `AuditAction` string'i taşıyan canlı satır yok | Deploy henüz yapılmadı (`DEPLOY_RUNBOOK` hazırlık aşamasında, MVP kapanışından sonra deploy sırası proje sahibi kararına bağlı). Kapsam önerisinde **"senin teyidin gerekiyor"** diye açıkça işaretlendi ve proje sahibi kapsamı bu maddeyle birlikte onayladı (2026-08-19) | ✓ Doğrulandı — **validator bu maddeyi bağımsız teyit etmeli** |
| 3 | B'nin kaldırılması frontend build'ini kırmaz | FE bu key'leri hardcode etmiyor; S19 matrisi AD11 `availablePermissions`'tan render ediliyor (`RoleFormModal.tsx` veri-güdümlü) | ✓ Doğrulandı |
| 4 | B'nin kaldırılması E2E'yi kırmaz | `e2e/tests/admin-flows.spec.ts:249` katalog uzunluğunu sayıdan bağımsız kontrol ediyor (`>= 2`) ve ilk iki key'i kullanıyor | ✓ Doğrulandı |
| 5 | D'nin kaldırılması sidecar'ı kırmaz / T133'ü beklemez | `SteamWebhooksController` T117'de silindi → yollar zaten 404; middleware dalı yalnız 401'e çeviriyordu | ✓ Doğrulandı |
| 6 | Dış paket / plan tier / API sürüm varsayımı | Yok — tur salt silme, yeni bağımlılık eklenmedi (`git diff` içinde `.csproj` / `package.json` değişikliği yok) | ✓ Yok |

### Mini Güvenlik Kontrolü (Katman 1)

- **Secret sızıntısı:** Yok — tur bir secret **kaldırıyor** (`SteamSharedSecret`); yeni
  değer eklenmedi, hiçbir gerçek secret koda girmedi. Testlerdeki secret'lar fixture
  sabitleridir.
- **Auth/authorization etkisi: VAR ve daraltıcı yönde.** Yetki kataloğu 14 → 12'ye indi.
  Kaldırılan iki key hiçbir `[Authorize(Policy = ...)]` tarafından okunmuyordu, dolayısıyla
  **hiçbir endpoint'in korunması gevşemedi**; aksine artık var olmayan bir ekran için rol
  tanımlanamıyor. Mevcut bir rolde bu key'ler duruyorsa `IsKnown` onları tanımaz —
  rol güncellemesi `INVALID_PERMISSION` döner (istenen davranış: ölü yetki yeniden
  yazılamaz). Prod veri olmadığı için migrasyon gerekmiyor.
- **Input validation:** Yeni kullanıcı girdisi yüzeyi yok.
- **Yeni dış bağımlılık:** Yok.
- **Webhook güvenliği:** HMAC + timestamp penceresi + nonce tekilliği blockchain yolunda
  aynen duruyor; kaldırılan dalın arkasında endpoint bulunmuyordu.
