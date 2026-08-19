# T133 — sidecar-steam salt-okunur proxy'ye küçültme [RİSKLİ]

**Faz:** F7 (P6) | **Durum:** ✓ Tamamlandı (doğrulama ✓ PASS) | **Tarih:** 2026-08-19

---

## Yapılan İşler

Bot custody katmanının **sidecar yarısı** kapatıldı. Backend yarısı T117'de
(entity/job/controller) ve T132'de (kalıntı sabitler + sözleşme girdileri)
gitmişti; bu tur `sidecar-steam`'i **salt-okunur bir Steam proxy'sine** indirir.

**Kalan yüzey — tamamı okuma:**

| Uç | Ne yapar |
|---|---|
| `GET /api/inventory/:steamId` (+`?refresh=`) | Anonim Community envanter okuması (08 §2.3) |
| `DELETE /api/inventory/:steamId/cache` | Cache invalidation |
| `GET /api/trade-hold/:steamId?accessToken=` | Web API trade-hold / MA probu (08 §2.2) |
| `/health`, `/metrics` | Healthcheck + Prometheus |

Bu dört uç, backend'in bu sidecar'dan **fiilen tükettiği her şeydir** — ölçüm
`HttpSteamSidecarInventoryClient` + `HttpSteamTradeHoldClient` üzerinden yapıldı;
`trade-offers/*` ve `bots/status`'ın üretimde çağıranı yoktu.

**A — Bot katmanı (kimlik bilgisi taşıyan tek yer):** `src/bot/` dizininin
tamamı (`BotConfig`, `BotManager`, `BotSession`, `BotHealthCheck` + testleri) ve
`sidecar-steam/scripts/link-authenticator.cjs` (bot hesabına Mobile
Authenticator bağlayıp `sharedSecret`/`identitySecret`'i `secrets/steam-bots.json`'a
yazan tek-seferlik araç).

**B — Trade offer katmanı:** `TradeOfferService`, `TradeOfferMonitor`,
`trade/types.ts` (tamamı trade sözleşmesi — `InventoryService`/`TradeHoldService`
hiçbirini kullanmıyor) + testleri.

**C — Webhook yayıncısı:** `src/webhook/` dizininin tamamı. `sendCallback`'in
bot/trade dışında **sıfır** çağıranı vardı ve **emekli iki yolu yayınlayan yer
burasıydı** — AC4 bu dizin gitmeden kapanamazdı.

**D — Kalan kod kalıntısı:** `routes.ts`'in üç bot/trade ucu +
`normalizeDeps` backward-compat shim'i · `HealthController.botStatusFactory` +
`buildBotSessionCheck` · `metrics.ts`'in `activeBotSessions` +
`tradeOffersTotal` metrikleri · `BotSessionExpiredError` (zaten sıfır kullanım) ·
`src/types/steam-tradeoffer-manager.d.ts` (lokal ambient declaration) ·
`config`'in `webhookSecret` + `steamTradeOfferLimitPerMinute` alanları.

**E — Bağımlılıklar:** `steam-tradeoffer-manager`, `steam-totp`, `steam-user` +
`@types/steam-totp`, `@types/steam-user` `package.json`'dan düştü.
**`steamcommunity` KALDI** — `InventoryService` anonim örneğiyle envanter okuyor,
yani 02 §9.2 teslimat doğrulamasının tek aracı.

**F — Sır ve config yüzeyi:** `secrets/steam-bots.json` (lokal, gitignored) ·
compose mount + `STEAM_BOTS_CONFIG_PATH` env · steam sidecar'ın `WEBHOOK_SECRET`
env'i (artık imzalayacağı bir şey yok) · `.env.example` bot bloğu ·
`sidecar-fake`'in `steamWebhookSecret` bağlaması + README atfı +
`docker-compose.e2e.yml`'daki `STEAM_WEBHOOK_SECRET` satırı.

**G — Bekçi testi:** `SidecarWebhookRouteContractTests`'in adı konmuş istisnası
(`RetiredWithBotCustodyLayer` + `RetiredPathsAreStillPublished_UntilT133`)
kaldırıldı; yerine `NoSidecarPublishesToTheRetiredSteamWebhookSurface` geldi.

**H — Doküman + ops yüzeyi (kapsam netleştirmesi, aşağıya bakınız):** 08 §9.2 /
§2 girişi / §2.5 / §2.4 / §2.7 · `scripts/bootstrap/02-register-bot.sql` +
README · DEPLOY_RUNBOOK §B/§G · iki Grafana paneli · 09 §4.4.1 dizin ağacı ·
`.claude/CONTEXT.md` dosya haritası.

---

## Kapsam Netleştirmesi (proje sahibi onaylı)

Planın dört kabul kriteri **korundu**; üstüne altı kalem eklendi ve
[`11_IMPLEMENTATION_PLAN.md`](../11_IMPLEMENTATION_PLAN.md) §P6 T133'e
"KAPSAM NETLEŞTİRMESİ" bloğu olarak **yazıldı** (T137'nin kalıcı dersi:
onaylanmış kapsam kaynak dokümana yazılmadıkça gerçekleşmemiştir).

Gerekçe **T132 doğrulamasının B1 dersidir**: *bir sözleşme girdisi koddan
kaldırılırken "hangi doküman bu girdiyi VAAT ediyor" sorusu HER MADDE İÇİN AYRI
sorulmalıdır.* Altı kalemin hiçbirinin başka sahibi yoktu — T133a yalnız
03 + 04 + 07'yi kapsıyor, **08'i ve deploy/ops yüzeyini hiçbir görev
kapsamıyordu**.

| # | Kalem | Bu turun hangi silmesi bozuyordu |
|---|---|---|
| F | 08 §2.5 kütüphane tablosu | 4 satırın 3'ü `package.json`'dan silindi |
| G | 08 §2.4 polling + §2.7 hata tablosu | `steam-tradeoffer-manager` polling'i ve bot-session re-login satırı |
| H | `02-register-bot.sql` + README | `secrets/README.md` ona link veriyordu; tablosu T117'de düşmüştü |
| I | DEPLOY_RUNBOOK §B/§G | `STEAM_BOTS_CONFIG_PATH`, "1/1 bots ready", bot kaydı adımı |
| J | 2 Grafana paneli | Silinen iki metriğin TEK tüketicileri |
| K | `webhook/`, `types.ts`, health/metrics/errors kalıntısı | Kriterlerin adını anmadığı ama bot/trade katmanına ait kod |

**Ek kapsam kararları:**
- **Lokal sır:** `secrets/steam-bots.json` proje sahibi kararıyla **silindi**.
  Gitignored'dı, repo'ya hiç girmedi. Taşıdığı Steam hesabı parolası için
  **rotasyon önerildi** ve `secrets/README.md`'ye yazıldı.
- **Arşiv kaydı:** `DEFERRED_BACKLOG` `P2P-BotCodeArchive` satırı **kapatıldı**
  (aktif 38 → 37). Üç halkanın üçünün de işaretçisi satıra yazıldı: T117 `82bff4d` ·
  T132 PR #247 · T133 PR #248. **İşaretçi olarak PR numarası seçildi, squash sha'sı
  değil** — sha merge anında doğar, yani satır kendi kapanışını hiçbir zaman
  yazamıyordu (T132 turunda da tam bu yüzden boş kaldı ve iş T133'e devretti).
  `git log --grep "(#248)"` squash commit'i sha'dan bağımsız bulur, yani satırın
  amacı (git geçmişinde yeri işaretlemek) sha olmadan da karşılanıyor.
- **Kapsam dışı, sahibi işaretlendi:** DEPLOY_RUNBOOK §G.4 kontrol 10'un happy
  path anlatısı hâlâ custodial. Bu bir **yeniden yazımdır**, bu turun sildiği bir
  şeyin sonucu değil → yeni **T133b** görevi olarak plana yazıldı.

---

## Etkilenen Modüller / Dosyalar

**Silinen (19 dosya, 4.016 satır — dal toplamı 5.021 silme):**

`sidecar-steam/src/bot/` (8) · `sidecar-steam/src/webhook/` (3) ·
`sidecar-steam/src/trade/{TradeOfferService,TradeOfferMonitor}{,.test}.ts` (4) ·
`sidecar-steam/src/trade/types.ts` · `sidecar-steam/src/types/steam-tradeoffer-manager.d.ts` ·
`sidecar-steam/scripts/link-authenticator.cjs` · `scripts/bootstrap/02-register-bot.sql` ·
(git dışı) `secrets/steam-bots.json`

**Değişen:**

| Dosya | Değişiklik |
|---|---|
| `sidecar-steam/src/api/routes.ts` | 3 bot/trade ucu + `normalizeDeps` shim'i kaldırıldı; `RouterDeps` iki alana indi |
| `sidecar-steam/src/api/routes.test.ts` | trade-offer blokları düştü; **T133 salt-okunur bekçisi eklendi** |
| `sidecar-steam/src/index.ts` | Bot wiring + `botManager.initialize()` + shutdown bacağı kaldırıldı; `shutdown` artık senkron |
| `sidecar-steam/src/health/HealthController.ts` | `botStatusFactory` + `buildBotSessionCheck` kaldırıldı |
| `sidecar-steam/src/metrics.ts` | `activeBotSessions` + `tradeOffersTotal` kaldırıldı |
| `sidecar-steam/src/errors/SidecarError.ts` | `BotSessionExpiredError` kaldırıldı |
| `sidecar-steam/src/config/index.ts` | `webhookSecret` + `steamTradeOfferLimitPerMinute` kaldırıldı |
| `sidecar-steam/package.json` + `package-lock.json` | 3 dependency + 2 `@types` düştü |
| `sidecar-fake/src/config.ts` · `README.md` | `steamWebhookSecret` bağlaması + atıflar |
| `docker-compose.yml` | `STEAM_BOTS_CONFIG_PATH`, `WEBHOOK_SECRET`, `secrets/` mount |
| `docker-compose.e2e.yml` | `STEAM_WEBHOOK_SECRET` satırı + yorumu |
| `.env.example` · `secrets/README.md` · `scripts/bootstrap/README.md` | Bot credential yüzeyi |
| `backend/.../SidecarWebhookRouteContractTests.cs` | İstisna listesi → sıkı guard + yeni diriliş testi |
| `Docs/08_INTEGRATION_SPEC.md` | **v3.1 → v3.2** (§2 girişi, §2.4, §2.5, §2.7, §9.2) |
| `Docs/09_CODING_GUIDELINES.md` | **v0.9 → v1.0** (§4.4.1 dizin ağacı) |
| `Docs/11_IMPLEMENTATION_PLAN.md` | §P6 T133 kapsam bloğu + yeni T133b görevi |
| `Docs/DEPLOY_RUNBOOK.md` | §B env tablosu, §G.0/§G.1/§G.2/§G.4/§G.5 |
| `infra/grafana/.../{integration,business}-metrics.json` | 2 ölü panel + satır yeniden akıtıldı |
| `.claude/CONTEXT.md` | Steam sidecar dosya haritası |
| `Docs/IMPLEMENTATION_STATUS.md` | Başlık + **Post-MVP §G kontrol listesi**: adım 5/6/8 ve #214 operasyonel tuzağı silinen script'e/dosyaya yönlendiriyordu |
| `Docs/DEFERRED_BACKLOG.md` | `P2P-BotCodeArchive` kapatıldı (aktif 38 → 37) |

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sidecar Steam hesap kimlik bilgisi olmadan boot ediyor | ✓ | Kimlik bilgisi okuyan kod **artık yok**: `grep -rn "STEAM_BOTS\|BotConfig\|loadBotCredentials" sidecar-steam/src` → 0 satır. `index.ts` yalnız `config.steamApiKey` (Web API) ve anonim `SteamCommunity` kullanır. `/health` **`healthy`** döner ve tek check'i `steam-api`'dir — eskiden kimlik bilgisi yokken `bot-session: degraded` yüzünden servis `degraded` raporluyordu. Bekçi testi bunu doğrudan assert eder (`routes.test.ts` — "still serves the two read-only routes and /health") |
| 2 | `secrets/`, compose ve 08 §9'dan bot credential'ları düştü | ✓ | `secrets/steam-bots.json` silindi (lokal) · `secrets/README.md` "Beklenen dosyalar" bölümü boşaldı · `docker-compose.yml`'dan `STEAM_BOTS_CONFIG_PATH` env'i **ve** `./secrets/steam-bots.json:ro` mount'u kalktı · `.env.example` bot bloğu kalktı · 08 §9.2 tablosundan "Steam Bot Credentials (×N)" + 4 alt satırı kalktı. `grep -rn "STEAM_BOTS\|steam-bots" --include="*.yml" --include="*.example" .` → 0 (task raporları/plan hariç) |
| 3 | Steam webhook secret'ının SIDECAR yarısı da düştü | ✓ | `sidecar-fake/src/config.ts` `steamWebhookSecret` bağlaması kalktı (tsc 0 hata → başka okuyucusu olmadığı derleyiciyle de doğrulandı) · `sidecar-fake/README.md`'nin iki `Webhook__SteamSharedSecret` / `STEAM_WEBHOOK_SECRET` atfı kalktı · `docker-compose.e2e.yml:69` satırı kalktı. `grep -rn "STEAM_WEBHOOK_SECRET\|steamWebhookSecret" sidecar-fake/src sidecar-fake/README.md docker-compose*.yml` → 0 |
| 4 | `RetiredWithBotCustodyLayer` listesi BOŞALDI + bekçi kaldırıldı | ✓ | Dizi, `RetiredPathsAreStillPublished_UntilT133` ve `EverySidecarPublishedPath`'teki istisna filtresi silindi. Emekli iki yolu yayınlayan üç sabit (`BotManager.ts:15`, `TradeOfferMonitor.ts:17`, `TradeOfferService.ts:26`) ile birlikte gitti. Contract süiti **4/4** yeşil ve **sıkı guard'ın geri geldiği ölçüldü** — aşağıdaki diriliş probu |

### AC4 — bekçinin boş geçmediği kanıtlandı

Silme turunda "test yeşil" tek başına zayıf kanıttır (guard vacuous da olabilir).
`sidecar-steam/src/config/index.ts`'e **geçici** bir satır enjekte edildi:

```ts
export const RESURRECTED = '/api/v1/webhooks/steam/bot-events';
```

Sonuç — **iki guard da ateşledi** (probe geri alındı, dal temiz):

```
Skinora.API.Tests.Contract.SidecarWebhookRouteContractTests
  .NoSidecarPublishesToTheRetiredSteamWebhookSurface [FAIL]
  .EverySidecarPublishedPath_IsServedByBackend        [FAIL]
Failed! - Failed: 2, Passed: 2, Total: 4
```

İkincisinin düşmesi kritiktir: **istisna listesi dururken bu test o yolu
görmezden geliyordu.** Yani AC4 yalnız "liste boşaldı" değil, "boşalan listenin
koruması gerçekten geri geldi" olarak doğrulandı.

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-steam build | ✓ | `npm ci && npm run build` (tsc) — 0 hata. **Temiz `node_modules`** ile koşuldu: üç Steam paketi lockfile'dan da düştü |
| sidecar-steam lint | ✓ | `npm run lint` — 0 bulgu |
| sidecar-steam unit | ✓ **83/83** (5 dosya) | Taban `eb0e49d`: **204/204** (12 dosya). Fark: 125 bot/trade testi silindi, **4 bekçi testi eklendi** |
| sidecar-fake tsc + unit | ✓ **38/38** | Değişmedi (taban da 38) |
| backend build | ✓ **0 Warning / 0 Error** | `dotnet build` |
| backend contract | ✓ **4/4** | `--filter "FullyQualifiedName~Contract"` |
| backend unit | ✓ **1408/1424** | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` (CI'nin kendi filtresi). 16 düşen = Docker-bağımlı, **T133 regresyonu değil** — bkz. aşağıda |
| backend integration | — | Lokalde Docker/Testcontainers yok; yetkili kanıt CI |

### Backend unit — 1408/1424, 16 düşen T133 regresyonu DEĞİL

Assembly dağılımı: Transactions 518 · Shared 388 · Platform 120 · Auth 83 ·
API 61 · Realtime 39 · Steam 39 · Disputes 25 · Users 22 · Fraud 18 ·
**Notifications 95/111**.

Düşen 16'nın 16'sı da `Notifications.Tests.Unit.Channels` altındaki
Discord (10) + Telegram (6) handler testleri ve hepsi aynı sebeple düşüyor —
Testcontainers Docker daemon'ına bağlanamıyor:

```
DotNet.Testcontainers.Builders.DockerEndpointAuthenticationProvider.IsAvailable
Docker.DotNet.DockerClient.MakeRequestAsync ...
```

Bu makinede Docker Desktop kapalı. Üç bağımsız kanıt bunun T133'e ait
olmadığını gösterir:

1. **T133 bu assembly'e tek satır dokunmadı** — `git diff --stat -- backend/`
   toplam **1 dosya** listeler: `SidecarWebhookRouteContractTests.cs`.
2. O dosya `.Contract` filtresindedir, yani bu koşuya **hiç girmez**.
3. **Toplam sayı tabanla birebir aynı: 1424.** T132 raporu da lokalde
   1408/1408 + 16 Docker-bağımlı, CI'da 1424/1424 kaydetmişti. Backend unit
   yüzeyi bu turda ne büyüdü ne küçüldü.

Yetkili kanıt CI'dır — izole container'da Docker vardır.

---

## Altyapı Değişiklikleri

- **Migration: Yok.** Şema değişmedi; silinen katmanın tabloları T117'de
  düşürülmüştü (`AppDbContextModelSnapshot` içinde `PlatformSteamBots` → 0 eşleşme).
- **Config/env değişikliği: VAR.**
  - `STEAM_BOTS_CONFIG_PATH` / `STEAM_BOTS_JSON` **artık okunmuyor** — set edilse
    bile etkisiz.
  - Steam sidecar'a geçilen `WEBHOOK_SECRET` kaldırıldı (imzalayacağı yüzey yok).
    `.env`'deki `WEBHOOK_SECRET`'in kendisi **duruyor** — blockchain sidecar'ın
    fallback'i ve backend `Webhook__BlockchainSharedSecret`.
  - `docker-compose.e2e.yml`'dan `STEAM_WEBHOOK_SECRET` kalktı.
  - `.env` dosyasında bu satırların kalması **zararsızdır**; kimse okumaz.
- **Docker değişikliği: VAR.** `skinora-steam-sidecar` servisinin `volumes:`
  bloğu tamamen kalktı — container artık host'tan hiçbir dosya mount etmiyor.
- **Sır rotasyonu (operasyonel takip):** silinen `secrets/steam-bots.json`
  içindeki Steam hesabı parolası için rotasyon önerildi (`secrets/README.md`).

---

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — tur secret **kaldırıyor**. Yeni secret eklenmedi;
  aksine bir dosya-tabanlı sır yüzeyi (bot kimlik bilgileri) tamamen ortadan
  kalktı ve tek kalan Steam credential'ı env'deki `STEAM_API_KEY`'dir.
- **`pre-commit` sır tarayıcısı bilinçli olarak KORUNDU.** `NAMED_KEYS` hâlâ
  `sharedSecret|identitySecret` içeriyor ve yorumu geçmişteki `steam-bots.json`
  vakasına atıf yapıyor. Bunlar bir **savunma katmanıdır**: bir silme turunun
  yan etkisi olarak sır tarayıcısını daraltmak GUARDRAILS §4'ün tam olarak
  uyardığı harekettir. Kalıp zaten `GENERIC_KEYS`'in `*SECRET*` dalına da
  düşüyor; bırakmanın maliyeti sıfır, kaldırmanın maliyeti kapsam kaybı.
- **Auth/authorization etkisi:** Yok. `internalKeyAuth` middleware'i ve
  backend'in HMAC/nonce hattı (middleware, `ProcessedNonces`, cleanup job,
  blockchain dalı) **dokunulmadan** duruyor.
- **Input validation:** Değişmedi — kalan iki ucun `STEAM_ID64_REGEX`,
  `parseRefreshParam` ve `accessToken` kontrolleri aynen korundu.
- **Yeni dış bağımlılık:** Yok — üç bağımlılık **kaldırıldı** (saldırı yüzeyi ve
  `npm audit` yüzeyi küçüldü).

---

## Commit & PR

- Branch: `task/T133-sidecar-steam-readonly-proxy`
- Commit: `c4a66bd` — T133: sidecar-steam salt-okunur proxy'ye küçültme
- PR: [#248](https://github.com/turkerurganci/Skinora/pull/248)
- Branch izolasyon check: ✓ temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+...'` → yalnız `T133`
- CI: ✓ **PASS** — dal HEAD `437c895` run [`32257465991`](https://github.com/turkerurganci/Skinora/actions/runs/32257465991) `conclusion=success`; bloke edici jobların **hepsi** yeşil (`1. Lint`, `2. Build`, `3. Unit test`, `3b. JS test (vitest)`, `4. Integration test`, `5. Contract test`, `6. Migration dry-run`, `7. Docker build` ×2, **`CI Gate`**). Önceki run [`32257305901`](https://github.com/turkerurganci/Skinora/actions/runs/32257305901) (`c4a66bd`) rapor/status commit'i push edilince concurrency'den **cancelled** — task.md gereği failure sayılmaz.

### Advisory E2E ölçümü — compose değişikliğinin inert olduğu KANITLANDI

Tur `docker-compose.e2e.yml`'a dokunduğu için (`STEAM_WEBHOOK_SECRET` satırı)
sekiz advisory leg'in logları sayıldı. T137'nin kalıcı dersi gereği bakıldı:
advisory sinyal bloke etmediği için değil, **kimse bakmadığı için** ölür.

| Leg | Sonuç | Taban (T132, run `32194023638`) |
|---|---|---|
| happy-path | 0/1 | 0/1 |
| T108 cancellation | 0/4 | 0/4 |
| T109 timeout | **1/4** | 1/4 |
| T110 payment edge cases | 0/6 | 0/6 |
| T111 fraud-flags | **3/4** | 3/4 |
| T112 emergency-hold | 0/3 | 0/3 |
| T114 downtime | 0/3 | 0/3 |
| T113 admin-flows | **6/7** | 6/7 |
| **Toplam** | **10/32** | **10/32** |

Sayı **ve** leg dağılımı birebir → `STEAM_WEBHOOK_SECRET` satırının kaldırılması
davranışı değiştirmiyor. Bu bir tahmin değil ölçümdür. Bu dalda koşacak her
sonraki run bir tekrar daha ekler (sayı burada sabitlenmedi ki atıf
bayatlamasın — T137 tur 2'nin N3 dersi).

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Bulgu sayısı | Bloke edici **0** · bloke etmeyen **6** (N1–N6, hepsi bu turda kapatıldı) · kapsam dışı güvenlik gözlemi **1** (G1, kapatıldı) |
| Düzeltme gerekli mi | Hayır — düzeltmeler proje sahibi onayıyla aynı dala işlendi |
| Doğrulama tarihi | 2026-08-19 · dal HEAD `0176c7e` (düzeltmeler öncesi) |

**Hard-stop kapıları:** working tree temiz · main son 3 run `success`
(`32248307699` Docker Publish · `32248307712` CI · `32180658381` CI) · repo
memory'de T133 satırı var · dal izolasyonu temiz (3 commit, hepsi `T133`).

### Kabul kriterleri — validator'ın kendi ürettiği kanıt

Dört kriterin **dördü de** yapım raporundan bağımsız olarak yeniden üretildi.

| # | Kriter | Sonuç | Validator kanıtı |
|---|---|---|---|
| 1 | Kimlik bilgisiz boot | ✓ | Sidecar `env -u STEAM_API_KEY -u INTERNAL_KEY -u REDIS_URL` ile **canlı** ayağa kaldırıldı (`node dist/index.js`, port 5199): `Steam sidecar listening` → `GET /health` **200** `{"status":"healthy", checks:[steam-api]}`, `bot-session` check'i yok. Envanter ucu Steam Community'ye **anonim olarak gerçekten ulaştı** — dönen hata `HTTP error 429` (Steam'in kendi rate limit'i), yani okuma yolu credential olmadan **çalışıyor**, lokal olarak kırık değil. Tek degradasyon trade-hold: `503 STEAM_API_KEY_MISSING` — `index.ts`'in vaat ettiği davranış birebir |
| 2 | `secrets/` + compose + 08 §9 | ✓ | `ls secrets/` → yalnız `README.md` · `git log --all -- secrets/steam-bots.json` → **boş** (repo'ya hiç girmemiş) · compose'da `STEAM_BOTS_CONFIG_PATH` env'i **ve** `volumes:` bloğunun tamamı yok · 08 §9.2'de bot credential dörtlüsü yok |
| 3 | Webhook secret'ının sidecar yarısı | ✓ | `sidecar-fake/src/config.ts` + `sidecar-steam/src/config/index.ts`'te bağlama yok; `docker-compose.e2e.yml`'da env satırı yok. Kalan üç atıf yalnız "bu kaldırıldı" açıklaması (bkz. rapor karşılaştırması) |
| 4 | Emeklilik listesi boşaldı + bekçi kalktı | ✓ | Dizi + `RetiredPathsAreStillPublished_UntilT133` + istisna filtresi silinmiş. **Bekçinin boş geçmediği validator tarafından bağımsız ölçüldü:** `sidecar-steam/src/`'e emekli bir yol taşıyan geçici dosya enjekte edildi → **iki guard da FAIL** (`Failed: 2, Passed: 2`), probe silindi, `git status` temiz |

### Regresyon kontrolü (silme turunun asıl riski)

| Kontrol | Sonuç |
|---|---|
| Backend'in fiilen çağırdığı uçlar | ✓ Kaynak taraması: `api/inventory/{id}` · `api/inventory/{id}/cache` (DELETE) · `api/trade-hold/{id}` · `/health` — **hepsi** canlı probda **200/204/503**, hiçbiri 404 değil |
| Emekli uçlar | ✓ `/api/bot/status`, `/api/bots`, `/api/trade-offers/{send,escrow,delivery}` → **404** |
| Silinen metrikler | ✓ Canlı `/metrics` çıktısında `active_bot_sessions` / `trade_offers_total` **yok**; `grep -rn ... infra/` → 0 (J'nin premisi doğrulandı) |
| Advisory E2E | ✓ Dal HEAD **10/32**, taban `eb0e49d` **10/32** — 8 leg'in **8'i de** birebir; iki run'ın job logları tek tek sayıldı. `STEAM_WEBHOOK_SECRET` satırının kaldırılması inert |

### Testler (validator'ın kendi koşusu)

| Tür | Sonuç | Not |
|---|---|---|
| sidecar-steam build + lint | ✓ 0 hata / 0 bulgu | Düzeltmeler sonrası tekrarlandı |
| sidecar-steam unit | ✓ **83/83** | Düzeltmeler sonrası tekrarlandı |
| sidecar-fake tsc + unit | ✓ **38/38** | Düzeltmeler sonrası tekrarlandı |
| backend contract | ✓ **4/4** | Düzeltmeler sonrası tekrarlandı |
| backend unit | ✓ **1408/1424** | 16 düşen = `DockerUnavailableException` (Testcontainers, lokalde Docker kapalı). Bağımsız teyit: T133 `backend/` altında **tek** dosyaya dokundu ve o dosya `.Contract` filtresinde — bu koşuya hiç girmiyor |
| Dal CI (HEAD `0176c7e`) | ✓ `CI Gate = success` | run `32259792321`; bloke edici 11 job'un 11'i yeşil. Önceki `c4a66bd` run'ı concurrency `cancelled` — failure değil |

### Güvenlik kontrolü

- **Secret sızıntısı:** Temiz — tur sır **kaldırıyor**; dalda eklenen satırlarda gerçek değer yok
- **Auth:** Temiz — `internalKeyAuth` ve backend HMAC/nonce hattı dokunulmamış
- **Input validation:** Temiz — `STEAM_ID64_REGEX`, `parseRefreshParam`, `accessToken` kontrolleri korunmuş
- **Yeni bağımlılık:** Yok — üç tanesi kaldırıldı; kalan `steam-totp` **yalnız** `steamcommunity`'nin transitifi (`node_modules/steamcommunity/node_modules/steam-totp`), üst düzey bağımlılık değil

### Bloke etmeyen bulgular — hepsi bu turda kapatıldı (proje sahibi onaylı)

| # | Bulgu | Kapanış |
|---|---|---|
| N1 | **08 §2.4 "Trade offer durumları" tablosu duruyordu.** Bölümün kendi başlığı "Bu entegrasyon kaldırılmıştır" derken tablonun "Skinora karşılığı" kolonu emekli davranış vaat ediyordu ("Accepted → state geçişi tetiklenir", "Canceled → **Platform tarafından** iptal edildi", "CreatedNeedsConfirmation → Mobile confirmation bekleniyor") — sonuncusu iki satır altındaki "**Mobile confirmation: Kaldırıldı**" paragrafıyla **doğrudan çelişiyordu**. Kriter G'nin kendi gerekçesi ("başlıklar emekliye ayrıldı, ALT TABLOLAR kaldı") tam bu tabloyu tarif ediyor ama madde yalnız polling tablosunu adlandırmıştı; 08'in başka sahibi yok (planın kendi ifadesi: "T133a yalnız 03+04+07'yi kapsar") → sahipsiz kalıyordu | Bölümün kendi v3.0 kalıbına çevrildi (`Kaldırıldı (v3.0). …`). Kodlar adıyla korundu ama platform karşılığı olmadığı yazıldı. 03 satır 280 zaten "counter offer artık platformun sorunu değil" diyordu — iki taraf hizalandı |
| N2 | **08 §2.7'de yetim tablo satırı.** `\| Envanter private \| … \|` 503 karar ağacı kod bloğunun hemen ardında başlıksız duruyordu → markdown'da tablo değil **düz metin** olarak render oluyordu. Tur öncesi 5 yetim satır vardı, T133 dördünü kaldırdı, beşincisi kaldı | Ait olduğu §2.7 hata tablosuna taşındı |
| N2b | §2.7'nin "**Session yönetimi retry**" başlığı altındaki tablo v3.0'dan beri oturumu değil **okuma sonucunu** anlatıyordu (hemen altındaki not "sidecar'ın Steam oturumu yoktur" diyor) | Başlık "Okuma sonucunun karar karşılığı" olarak içeriğine çekildi |
| N3 | **Silinen tipleri canlı sayan iki yorum.** `TradeHoldService.ts:19` "…which is why it lives outside the **BotSession/TradeOfferManager** stack" — ikisi de bu turda silindi. `sidecar-fake/src/hmac.ts:12` "sidecar-steam / sidecar-blockchain **WebhookClient.ts**" — sidecar-steam'inki bu turda silindi. T132'nin **E** maddesi tam bu sınıfı kapatıyordu | İkisi de silinen tipe atıf yapmayacak biçimde yeniden yazıldı; gerekçe korundu |
| N4 | **DEPLOY_RUNBOOK §G girişi kendi §G.0'ıyla çelişiyordu.** Satır 161 hâlâ "gerçek Steam hesabı + **gerçek bot** + Nile testnet ile tam stack" diyordu; sekiz satır aşağıda §G.0 "bot hesabı gerekmez (T133)" diyor. Kriter I §G.0/1/2/4/5'i adlandırdı, §G girişini adlandırmadı; T133b yalnız §G.4/10 anlatısını kapsıyor → sahipsizdi | Tarihsel bağlam korunarak "gerçek bot" düşürüldü + T133 notu eklendi |
| N5 | **Dosya sayısı yanlıştı.** Rapor + STATUS + DEFERRED_BACKLOG + repo memory "**16 dosya**" diyordu; `git diff --diff-filter=D --name-only eb0e49d..HEAD` → **19 dosya**. Raporun kendi listesi de 8+3+4+1+1+1+1 = **19** veriyor, yani toplama hatası. Arşiv satırı kalıcı kayıt | Dört yerde de **19 dosya / 4.016 satır** olarak düzeltildi (dal toplamı 5.021 silme) |
| N6 | **Arşiv kriterinin sapması plana yazılmamıştı.** Kriter "bu turun **squash sha'sı** yazılarak KAPATILIR" diyor; yazılan **PR #248**. Gerekçe sağlam ve DEFERRED_BACKLOG'a yazılmıştı — ama plan metni hâlâ "squash sha'sı" diyordu. T122'nin kalıcı dersi: onaylanan sapma KAYNAK dokümana geçmedikçe gerçekleşmemiş sayılır | Plana **"İŞARETÇİ SAPMASI — NİHAİ ŞEKİL"** bloğu yazıldı: kriterin harfi bu turda **karşılanamaz** (sha merge anında doğar), özü karşılanıyor (`git log --grep "(#248)"`), geçmiş halkalar sha ile yazılabiliyor (T117 `82bff4d`, T132 `eb0e49d`) |

### Kapsam dışı güvenlik gözlemi — G1 (T133'ün bulgusu değil, bu turda kapatıldı)

`scripts/git-hooks/pre-commit`'in bir yorumu **gerçek olduğu belirtilen** Steam
bot parolasını **açık metin** taşıyordu (`password: <literal>` + "(the Steam bot
password)"). Commit `2db26c5` (PR #216) — **T133 öncesi**, bu tur dosyaya
dokunmamıştı. Ağırlığı T133'ün kendi "LOKAL SIR" kararından geliyor: o karar
parolanın **lokal diskte** açık metin durduğu için rotasyon öneriyordu; parola
**takip edilen** bir dosyada, yani **git geçmişinde** de duruyordu.

**Kapanış:** literal `<redacted>` ile maskelendi ve yorumda "canlı bir sırrı,
canlı sır alıntılamayı yasaklayan kuralı belgelemek için alıntılamak kuralın
kendisini bozar" gerekçesi yazıldı. `secrets/README.md`'nin rotasyon notu
"rotasyon isteğe bağlı değildir — maskeleme geçmişi temizlemez" ile
güçlendirildi. Tarayıcının `NAMED_KEYS`/`GENERIC_KEYS` kapsamına **dokunulmadı**
(GUARDRAILS §4).

### Yapım raporu karşılaştırması

**Uyum: yüksek.** Dört AC, F–K kapsamı, test sayıları (83/83 · 38/38 · 4/4 ·
1408/1424), Docker-bağımlı 16 düşenin teşhisi ve advisory E2E 10/32 ölçümü —
hepsi bağımsız olarak yeniden üretildi ve **birebir** tuttu. AC4'ün diriliş
probu rapordan bağımsız kuruldu (rapor `config/index.ts`'e enjekte etmiş,
validator ayrı bir dosya kullandı), aynı sonucu verdi.

İki kanıt düzeltmesi (kriterleri değiştirmiyor, yalnız rapordaki `grep → 0`
iddiaları bugün sıfır dönmüyor — açıklama yorumları sonradan yazıldığı için):

- AC3'ün grep'i bugün **3 satır** döner: `sidecar-fake/src/config.ts:27`,
  `sidecar-fake/README.md:118`, `docker-compose.e2e.yml:67` — üçü de
  "bu kaldırıldı" açıklaması, **bağlama değil**. Kriter karşılanıyor.
- AC2'nin grep'i bugün **1 satır** döner: `.env.example:63` — yine açıklama.
  Kriter karşılanıyor.

Rapor N1–N4'ü anmıyor (validator'ın bağımsız bulguları); N5 raporun kendi
toplama hatası; N6 raporda karar olarak yazılı ama kaynak dokümana yansımamıştı.

---

## Known Limitations / Follow-up

- **DEPLOY_RUNBOOK §G.4 kontrol 10** hâlâ custodial happy path'i anlatıyor
  (`trade offer → ITEM_ESCROWED`). Kaldırma değil **yeniden yazım** işi olduğu
  için bu turda kapatılmadı; **T133b** olarak plana kabul kriteriyle yazıldı.
- **Gözlem (yeni iş üretmez, sahibi yok):** `sidecar-steam` iki metrik yayınlıyor
  ama hiçbir Grafana paneli okumuyor — `skinora_steam_queue_depth` (T120) ve
  `skinora_steam_inventory_cache_total`. Birincisi 10 §4'te *eşzamanlı teslimat
  doğrulama tavanı* olarak kayıtlı, yani gözlemlenmeye değer. Bu tur iki **ölü**
  paneli kaldırdı; **panel EKLEMEK** onaylanan kapsamda değildi, o yüzden
  yapılmadı ve buraya karar için bırakıldı.

---

## Notlar

- **Working tree:** temiz (Adım -1 hygiene check — `git status --short` boş).
- **Adım 0 — Main CI startup check:** son 3 tamamlanmış run `success` —
  `32248307699` (Docker Publish), `32248307712` (CI), `32180658381` (CI).
- **Bağımlılık:** T132 ✓ Tamamlandı (`eb0e49d`, PR #247).

### Dış Varsayımlar (Adım 4 — ön-uçuş kontrolü)

| # | Varsayım | Kanıt |
|---|---|---|
| 1 | `steamcommunity` tek başına, `steam-user`/`steam-totp`/`steam-tradeoffer-manager` olmadan çalışır | `steamcommunity@3.x`'in kendi `dependencies`'i incelendi — `steam-totp@^1.5.0`'ı **transitif** taşıyor, üst düzey `steam-totp@^2.1.2`'ye ihtiyacı yok. Ölçüm: `node_modules` **silinip** `npm ci` ile sıfırdan kuruldu → `npm run build` 0 hata, 83/83 test yeşil |
| 2 | `@types/steamcommunity` silinen `@types` paketlerine bağlı değil | `@types/steamcommunity@3.50.0` `dependencies`: `@types/node`, `@types/request`, `@types/steamid` — üçü de duruyor. `tsc --noEmit` 0 hata |
| 3 | `sendCallback`'in bot/trade dışında çağıranı yok (⇒ `webhook/` tümüyle silinebilir) | `grep -rn "sendCallback\|WebhookClient\|WebhookPayloads" sidecar-steam/src` → yalnız `bot/` ve `trade/TradeOffer*` dosyaları |
| 4 | `PlatformSteamBots` tablosu gerçekten yok (⇒ `02-register-bot.sql` ölü) | `AppDbContextModelSnapshot.cs`'te `PlatformSteamBots` → **0** eşleşme; T117 migration'ı (`20260809162642_T117_P2P_Pivot`) düşürmüş |
| 5 | Emekli iki yolu `sidecar-fake` yayınlamıyor (contract testin XML doc'u öyle diyordu) | `grep` ile ölçüldü: yalnız `sidecar-steam`'in üç sabiti yayınlıyordu. Testin doc'u T137'den beri **bayattı** ve bu turda düzeltildi |

### Lokal ortam notu — prettier CRLF artefaktı

`npm run format:check` lokalde 8 dosyaya uyarı verir; bunların arasında bu turun
**hiç dokunmadığı** `RateLimitedQueue.ts` / `InventoryService.ts` de vardır.
Sebep `core.autocrlf=true` + `.gitattributes`'ın `* text=auto` kuralı: working
tree CRLF, repo LF. Yetkili kanıt CI'nin LF checkout'udur. Yine de bu turun
değiştirdiği **yedi dosyanın yedisi** LF'e normalize edilip tek tek
`prettier --check`'ten geçirildi → hepsi temiz (bir bulgu çıktı ve düzeltildi).
