# T120 — Sidecar envanter: cache bypass + ayrı limiter + visibility

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-11

---

## Yapılan İşler

1. **`refresh` cache bypass (AC1).** `InventoryService.getInventory` ikinci bir `InventoryReadOptions` parametresi aldı; `refresh: true` cache **okumasını** atlar, taze sonucu yine de cache'e **yazar**. Write-through tercih edildi çünkü alternatifi (bypass eden okumanın cache'i hiç doldurmaması) her doğrulama turundan sonra sıradan listeleme okumalarını soğuk bırakır ve aynı Community bütçesini iki kez harcatır.

2. **Belirsiz `refresh` değeri 400 döner.** Route `true|false|1|0` (case-insensitive) dışındaki her değeri — boş string, `yes`, tekrarlanan parametre (Express dizi üretir) — reddeder ve servisi hiç çağırmaz. `refresh=false`'a sessizce düşmek **kasıtlı olarak** seçilmedi: bu bayrağı set eden çağıran bir teslimat doğrulamasıdır ve ona sessizce 2 dakikalık bayat veri sunmak, 08 §2.3'ün bayrağı eklemesinin tam sebebi olan hatadır. Yazım hatası görünür kalır.

3. **Community ucu için ayrı kuyruk (AC2).** Ölçüm şunu gösterdi: envanter yolu bugün **hiçbir** kuyrukta değil — tek `RateLimitedQueue` örneği yalnız `TradeHoldService`'e bağlıydı. Dolayısıyla bu AC "paylaşılan kuyruğu böl" değil, "kuyruğu ilk kez tak" işi oldu. `steamCommunityQueue` (10 istek/60 sn) `steamWebApiQueue`'dan (1 istek/sn) tamamen bağımsız kuruldu ve `InventoryService`'e enjekte edildi. Cache hit'leri kuyruğa **girmez** — cache'in varlık sebebi kuyruk yükünü azaltmaktır (08 §2.6); hit'i kuyruğa sokmak bunu tersine çevirirdi.

4. **Limit env'e açıldı, güvenli varsayılanla.** `STEAM_COMMUNITY_REQUESTS_PER_MINUTE`, varsayılan **10/dk**. 08 §2.6'nın verdiği 10-20/dk/IP aralığının muhafazakâr ucu seçildi çünkü aşımın cezası IP bloğu, eksik kalmanın cezası yalnız yavaşlık — asimetrik risk. `positiveIntFromEnv` yardımcısı eklendi: `parseInt` sonucu `NaN` olursa `timestamps.length >= NaN` **her zaman false** döner, yani bir yazım hatası throttling'i sessizce **tamamen kapatırdı**. Bu, fail-safe olması gereken bir yolda fail-open'dır; guard bunu keser.

5. **Üç değerli görünürlük bir değer oldu (AC3).** `getInventory` artık beklenen Steam durumları için **istisna fırlatmıyor**; `InventoryReadResult` discriminated union döndürüyor (`PUBLIC` / `PRIVATE` / `UNAVAILABLE`). 08 §2.3'ün ifadesi birebir budur: "okuma sonucu üç durumdan biri olarak döner". İstisna tabanlı akışta ayrım bir `catch` içinde kazara yutulabilirken, union'da tip sistemi çağıranı üç dalı da ele almaya zorlar.

6. **Wire sözleşmesi katmalı genişletildi, statü kodları korundu.** Her üç yanıtın gövdesine `visibility` alanı eklendi; HTTP statüleri (200 / 422 `INVENTORY_PRIVATE` / 503 `STEAM_UNAVAILABLE`) **aynen** korundu. "Her durumda 200 + visibility" alternatifi ölçülüp reddedildi — bkz. §"Reddedilen tasarım".

7. **Hata sınıfları silinmedi, union'ın içine taşındı.** `InventoryPrivateError` / `SteamUnavailableError` union'ın başarısızlık dallarında taşınıyor. Bunlar `SidecarError.retryable` bayrağını içeriyor ve bu bayrak 08 §2.7'nin retry kutuplanmasının kodlanmış hâli (`Private` → retry **yok**, kullanıcı aksiyonu gerekli; `Unavailable` → retry **var**). Sınıfları silip `code`/`retryable`'ı union'a literal olarak kopyalamak aynı bilgiyi iki yerde tutardı.

8. **Metrikler wiring katmanından besleniyor (gözlemci enjeksiyonu).** `skinora_steam_inventory_cache_total{result=hit|miss|bypass}` ve `skinora_steam_queue_depth{queue=community|webapi}` eklendi. Domain modülleri (`InventoryService`, `RateLimitedQueue`) `metrics.js`'i **import etmiyor**; opsiyonel gözlemci callback'i alıyor ve `index.ts` bunları Prometheus nesnelerine bağlıyor. Gerekçe §"Keşifte çıkan defekt"te.

9. **`RateLimitedQueue` ilk kez test edildi.** Sınıf 158 testin sıfırında çalışıyordu. 9 test eklendi; en önemlisi iki kuyruğun gerçekten bağımsız olduğunu (doygun kuyruk diğerini durdurmuyor) kanıtlayan test — AC2'nin asıl iddiası budur.

10. **`sidecar-fake` sözleşme paritesi.** E2E yığınında `skinora-steam-sidecar` ağ alias'ı fake'e gider; yanıtına `visibility: 'PUBLIC'` eklendi ki T121 backend'i bu alanı okumaya başladığında E2E sessizce eski şekli görmesin. `?refresh` fake'te ele alınmadı — fake cache tutmaz, her okuma zaten tazedir.

## Reddedilen tasarım: "her durumda 200 + visibility"

Ölçüm: backend bugün **yalnız HTTP statüsüne** bakıyor, gövdedeki `code` alanını hiç okumuyor ([HttpSteamSidecarInventoryClient.cs:70-82](../../backend/src/Modules/Skinora.Steam/Application/Inventory/HttpSteamSidecarInventoryClient.cs#L70-L82)). Sidecar her durumda 200 dönseydi:

| Adım | Sonuç |
|---|---|
| Sidecar | 200 + `visibility: "PRIVATE"` + boş liste |
| `HttpSteamSidecarInventoryClient` | `IsSuccessStatusCode` → `SteamSidecarStatus.Success` |
| `SteamController.GetInventory` | 422 `INVENTORY_PRIVATE` yerine **200 + boş liste** |
| `TransactionCreationService` | **`ITEM_NOT_IN_INVENTORY`** — kullanıcıya "item envanterinde yok" der |
| Doğru mesaj | "Steam profilini public yap" |
| CI | **Yeşil kalır** — hiçbir test bunu görmez |

Sessiz olması bu senaryoyu en tehlikeli seçenek yapıyor. Ayrıca 07 §6.1'in **normatif** public sözleşmesini (200 / 422 / 503) bozardı. Buna karşılık gövdeye alan **eklemek** hiçbir şeyi kırmaz: System.Text.Json bilinmeyen üyeleri yok sayar ve `JsonUnmappedMemberHandling` tüm `backend/` ağacında hiç kullanılmıyor (grep: 0 eşleşme). Bu yüzden statü kodu + katmalı alan seçildi.

## Keşifte çıkan defekt: `metrics.ts` modül-yükleme yan etkisi

İlk implementasyonda `InventoryService` ve `RateLimitedQueue` metrikleri doğrudan import etti (repodaki `BotManager` emsali). Test suite **iki dosyada birden** patladı:

```
Error: A metric with the name skinora_steam_process_cpu_user_seconds_total has already been registered.
  ❯ src/metrics.ts:5:8   client.collectDefaultMetrics({ prefix: 'skinora_steam_' });
```

**Kök sebep:** `metrics.ts` modül yüklenirken `collectDefaultMetrics()` çağırıyor, prom-client registry'si ise **process-global**. Vitest her test dosyasını izole modül grafiğiyle değerlendirir ama process'i paylaşır (`pool: 'forks'`, `singleFork: true`) → ikinci yükleme çift kayıt yapar.

**Neden bugüne kadar sessiz kaldı:** `metrics.ts`'i import eden tek test dosyası `routes.test.ts` idi (`metricsHandler` üzerinden), diğer tüketicisi olan `BotManager`/`BotHealthCheck` testleri modülü `vi.mock` ile tamamen değiştiriyordu. Yani modül test sürecinde **hiç iki kez gerçek yüklenmemişti**.

**Düzeltme:** metriği import etmek yerine gözlemci enjekte etmek. Alternatif — `metrics.ts`'i idempotent yapmak (`register.getSingleMetric` fallback + `collectDefaultMetrics` guard) — reddedildi: gerçek çift-kayıt hatalarını da maskeler ve global durumu domain modüllerine yaymaya devam ederdi. Gözlemci deseni ayrıca `log` / `queue` / `now` enjeksiyon konvansiyonuyla tutarlı ve metriği deterministik biçimde test edilebilir kılıyor (paylaşılan registry'yi okumadan).

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `sidecar-steam/src/trade/InventoryService.ts` | `InventoryReadResult` union + `InventoryReadOptions.refresh` + `TaskQueue` ve `CacheOutcomeObserver` enjeksiyonu; throw tabanlı akış kaldırıldı |
| `sidecar-steam/src/api/routes.ts` | `parseRefreshParam` (katı doğrulama, 400) + union → statü/gövde eşlemesi + `visibility` alanı |
| `sidecar-steam/src/queue/RateLimitedQueue.ts` | `TaskQueue` arayüzü export'u + opsiyonel `onDepthChange` gözlemcisi + ayrı-kuyruk gerekçesi dokümantasyonu |
| `sidecar-steam/src/config/index.ts` | `steamCommunityRequestsPerMinute` + `positiveIntFromEnv` fail-safe guard |
| `sidecar-steam/src/index.ts` | `steamCommunityQueue` kurulumu + iki kuyruğun derinlik gözlemcisi + cache outcome sayacı bağlantısı |
| `sidecar-steam/src/metrics.ts` | `inventoryCacheTotal`, `rateLimitedQueueDepth` (+2 metrik) |
| `sidecar-steam/src/trade/InventoryService.test.ts` | 13 → **23** test (5'i union'a uyarlandı, +10 yeni: refresh, kuyruk, cache outcome) |
| `sidecar-steam/src/api/routes.test.ts` | 19 → **30** test (3'ü uyarlandı, +11 yeni: refresh ayrıştırma, visibility, uçtan uca bypass) |
| `sidecar-steam/src/queue/RateLimitedQueue.test.ts` | **YENİ** — 9 test (sınıfın ilk kapsaması) |
| `sidecar-fake/src/routes/steam.ts` | Envanter yanıtına `visibility: 'PUBLIC'` (sözleşme paritesi) |
| `docker-compose.yml` | `STEAM_COMMUNITY_REQUESTS_PER_MINUTE` env geçişi |
| `.env.example` | `STEAM_SIDECAR_COMMUNITY_REQUESTS_PER_MINUTE` + gerekçe yorumu |
| `Docs/DEPLOY_RUNBOOK.md` | Env tablosuna yeni satır (§C zorunlu/opsiyonel env) |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `refresh` parametresi cache'i atlıyor | ✓ | Üretim: `InventoryService.ts` `getInventory(steamId, options)` — `refresh` true iken `cache.get` hiç çağrılmaz; `routes.ts` `parseRefreshParam`. Testler: `skips the cache read and re-fetches when refresh is set` (fetcher 1→2 çağrı), `defaults to the cached read when no options are passed` (üç farklı çağrı biçimi, fetcher 1 çağrı), `writes the bypassed result back to cache`, `leaves an existing cache entry intact when the refresh fetch fails`, route seviyesinde `maps ?refresh=… to refresh=…` (5 vaka) ve **servis double'sız uçtan uca** `bypasses the cache end-to-end against a real InventoryService` |
| 2 | Community ucu için Web API'den ayrı kuyruk | ✓ | Üretim: `index.ts` `steamCommunityQueue = new RateLimitedQueue(config.steamCommunityRequestsPerMinute, 60_000, …)` — `steamWebApiQueue`'dan ayrı örnek, `InventoryService`'e enjekte. Testler: `dispatches upstream fetches through the injected queue`, `does not enqueue a cache hit`, `enqueues refresh reads`, `routes failing fetches through the queue too` ve AC'nin asıl iddiasını kanıtlayan `keeps two queues independent — one saturated queue does not stall the other` (doygun kuyruk 1 istek/80 ms iken diğeri <80 ms'de 3 iş bitiriyor) |
| 3 | Yanıt görünürlüğü Public/Private/Unavailable olarak ayrıştırıyor | ✓ | Üretim: `InventoryReadResult` union (`InventoryService.ts`) + `routes.ts` switch → 200/422/503 + gövdede `visibility`. Testler: `reports PRIVATE (not an exception)…` ve `reports UNAVAILABLE for any other fetch error` (her ikisi `code` **ve** `retryable` kutuplanmasını de assert ediyor), route'ta `returns 422 INVENTORY_PRIVATE…` / `returns 503 STEAM_UNAVAILABLE…` (`visibility` alanı dahil) ve para-güvenliği çekirdeğini kilitleyen iki test: `returns an empty envelope for a public-but-empty inventory` (boş ≠ private/unavailable) + `distinguishes UNAVAILABLE from a PUBLIC-but-empty inventory` |

**Kriter dışı ama aynı kapının parçası:** 08 §2.3 üç değeri PascalCase (`Public`/`Private`/`Unavailable`) yazıyor; kodda UPPER_SNAKE (`PUBLIC`/`PRIVATE`/`UNAVAILABLE`) kullanıldı. 06 §2'de (2.1–2.24 tarandı) envanter görünürlüğü için **enum sözlüğü yok**, dolayısıyla sidecar↔backend iç sözleşmesinde isimlendirme serbest. UPPER_SNAKE seçildi çünkü değer T121'de public API'ye taşınırsa 07 §2.8'in "Enum değerleri UPPER_SNAKE_CASE" kuralı devreye girer ve o noktada yeniden adlandırma gerekmez.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-steam typecheck | ✓ | `npx tsc --noEmit` — 0 hata (tsconfig `include: src/**/*`, test dosyaları dahil) |
| sidecar-steam vitest | ✓ **188/188** | `npm test` — 11 dosya. Baseline 158 → 188 (**+30**): InventoryService 13→23, routes 19→30, RateLimitedQueue 0→9 |
| sidecar-steam lint | ✓ | `npm run lint` — exit 0 |
| sidecar-steam format | ✓ | `npm run format:check` — "All matched files use Prettier code style!" |
| sidecar-fake typecheck | ✓ | `npx tsc --noEmit` — 0 hata |
| sidecar-fake lint | ✓ | `npm run lint` — exit 0 |
| sidecar-fake vitest | ✓ 12/12 | `npm test` — 3 dosya (değişmedi) |
| backend Skinora.Steam.Tests | ✓ **21/21** | `dotnet test tests/Skinora.Steam.Tests` — envanter istemcisi (`HttpSteamSidecarInventoryClient`) regresyon kontrolü. Backend'e hiç dokunulmadı; `sidecar-fake`'e eklenen `visibility` alanının sessizce yok sayıldığını teyit eder |

**Prettier notu (sidecar-fake).** Lokal `npm run format:check` 4 dosyada uyardı — 3'ü bu task'ta **dokunulmayan** dosyalar (`control.ts`, `tradeControl.ts`, `tradeControl.test.ts`). Kanıt bunun `core.autocrlf` artefaktı olduğunu gösteriyor: dosyalar working tree'de 154 CRLF / 0 LF taşıyor ve LF'e normalize edilmiş kopyaları (düzenlenen `steam.ts` dahil) Prettier'dan **temiz** geçiyor. Yetkili olan CI "1. Lint" adımıdır (LF checkout) — proje hafızası `e2e-prettier-crlf-local-artifact` ile aynı imza. sidecar-fake'e `--write` **çalıştırılmadı**: çalıştırmak dokunulmayan üç dosyanın satır sonlarını da yeniden yazar ve task diff'ini kirletirdi.

**Satır-sonu notu (sidecar-steam).** `npm run format` bu paketin working tree'sini LF'e çevirdi, bu yüzden `git status` 25 ek dosyayı `M` gösteriyor. `git diff --cached --stat` bunların **hiçbirini** listelemiyor: `.gitattributes` `* text=auto` ile index'te zaten LF tutuluyor, yani içerik değişikliği yok. Staged diff tam olarak 13 dosya.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı doğrulama chat'i — INSTRUCTIONS §3.3 izolasyon kuralı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

**Başlangıç kapıları:**
- Working tree (Adım -1): **temiz** — `git status --short` boş.
- Main CI (Adım 0): son 5 tamamlanmış run `success` — `31432878950`, `31432878831` (T119a #227), `31414178181`, `31414178436` (T119 #226), `31380447239` (chore #225).
- Bağımlılık: T115 ✓ Tamamlandı (`IMPLEMENTATION_STATUS.md`, 2026-08-08).
- Baseline ölçümü: değişiklik öncesi `npm test` → 158/158 yeşil (10 dosya).

## Altyapı Değişiklikleri

- **Migration:** Yok — task tamamen sidecar (Node/TS) kapsamında, hiçbir entity/DbContext'e dokunulmadı.
- **Config/env değişikliği:** **Var** — `STEAM_COMMUNITY_REQUESTS_PER_MINUTE` (konteyner içi) / `STEAM_SIDECAR_COMMUNITY_REQUESTS_PER_MINUTE` (host). Boş bırakılabilir; varsayılan 10/dk. `docker-compose.yml` + `.env.example` + `DEPLOY_RUNBOOK.md` env tablosu güncellendi. WP14 presedanına uyuldu: sidecar ayarı `SystemSetting` **değil** env; değişiklik restart gerektirir.
- **Docker değişikliği:** Yalnız env satırı; image/Dockerfile değişmedi.
- **Yeni paket:** Yok — `npm audit` dengesi (kalıcı advisory@high, owner accept-risk) bozulmadı.
- **Yeni metrik:** 2 adet (`skinora_steam_inventory_cache_total`, `skinora_steam_queue_depth`). İkisi de gerçekten besleniyor (`index.ts` wiring) — "tanımlı ama yazılmayan metrik" borcuna eklenmedi.

## Commit & PR

- Branch: `task/T120-inventory-refresh-queue-visibility`
- Commit: `73f2bdb` — T120: Sidecar envanter — cache bypass + ayrı limiter + üç değerli görünürlük (kod + testler + rapor + status + repo memory tek commit'te)
- PR: [#228](https://github.com/turkerurganci/Skinora/pull/228)
- Branch izolasyon kontrolü: ✓ temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+…'` → yalnız `T120`
- CI: **✓ PASS** — run [`31437273547`](https://github.com/turkerurganci/Skinora/actions/runs/31437273547), **CI Gate `success`**

**Bloke edici job'lar (9/9 yeşil):** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build (sidecar-steam) · CI Gate. (`0. Guard (direct push)` skipped — PR yolunda beklenen.)

**8 advisory E2E leg'i kırmızı — bu task kaynaklı değil, kanıtlı.** Kırılma T117'den beri sürüyor (`continue-on-error`, CI Gate'i bloke etmiyor; sahiplik T137 → T138). `gh run view 31437273547 --log-failed` (970 satır) üzerinde ölçüm:

| Arama | İz sayısı | Anlamı |
|---|---|---|
| `PlatformSteamBots` | **8** | T117'nin bıraktığı kök sebep — leg başına tam bir tane, imza önceki run'larla birebir |
| `visibility` | **0** | T120'nin eklediği alan hiçbir kırılmada geçmiyor |
| `refresh` | **0** | Cache bypass parametresi hiçbir kırılmada geçmiyor |
| `STEAM_COMMUNITY_REQUESTS_PER_MINUTE` | **0** | Yeni env hiçbir kırılmada geçmiyor |
| `queue_depth` / `inventory_cache_total` | **0** / **0** | Yeni metrikler hiçbir kırılmada geçmiyor |

Yani dokunulan iki yüzeyden (`sidecar-steam`, `sidecar-fake`) **yeni bir kırılma gelmedi**; `sidecar-fake`'e eklenen `visibility` alanı E2E'de hiçbir şeyi bozmadı (backend alanı sessizce yok sayıyor — `Skinora.Steam.Tests` 21/21 ile de teyitli).

> **Not:** Bu bölümü ekleyen doküman-only commit kendi CI run'ını tetikler; o run'ın kimliği raporlanmaz — aksi hâlde her rapor güncellemesi bir sonrakini gerektirir (sonsuz regresyon). Yetkili ölçüm, kodu taşıyan `31437273547` run'ıdır.

## Known Limitations / Follow-up

| # | Açık | Durum |
|---|---|---|
| 1 | Backend hâlâ `visibility` alanını okumuyor; `SidecarSteamInventoryReader.TryGetItemAsync` private/unavailable/item-yok üçünü tek `null`'a çöktürmeye devam ediyor | **T121'in AC'si** — sidecar ucu bu task'ta hazırlandı |
| 2 | 10/dk limiti ölçülmüş değil, 08 §2.6'nın "tahmini" aralığından seçildi | **T122** gerçek Steam probunda ölçülecek; env ile ayarlanabilir bırakıldı |
| 3 | `refresh=true` gönderen bir çağıran henüz yok | **T123 / T125 / T129** — sidecar ucu hazır, backend bağlantısı onların |
| 4 | `sidecar-fake` steamId başına Private/Unavailable süremiyor (hep PUBLIC) | **T137** — bu task yalnız alan paritesini ekledi |
| 5 | Private tespiti `steamcommunity`'nin mesaj string'ine bağlı (`This profile is private.`); kütüphane kod/eresult vermiyor | Mevcut stratejinin sınırı — tanınmayan hata `UNAVAILABLE`'a düşer, yani fail-safe |
| 6 | Kuyruk süreç-içi ve in-memory; çok replikalı çalışmada limit replika sayısıyla çarpılır | T67 K7'nin devamı — tek replika varsayımı |
| 7 | 07 §7.6a'nın tek boolean'ı (`buyerInventoryVisible`) ve 06 §3.5'in tek NULL'ı (`BuyerBaselineCapturedAt`) Private ile Unavailable'ı çöktürüyor; 08 §2.3'ün "Unavailable → karar verilmez" kuralıyla gerilimde | **Keşifte bulundu, T120 kapsamı dışı** — kalıcı katman T121/T123'ün; proje sahibine ayrıca raporlandı |

## Notlar

**Dış Varsayımlar**

| Varsayım | Kanıt |
|---|---|
| `steamcommunity` private profili ayırt edilebilir raporluyor | `steamcommunity@3.50.0`, `node_modules/steamcommunity/components/users.js:599-607` — "HTTP error 403" + `body === null` → `new Error("This profile is private.")`. Kod/eresult alanı **yok**, mesaj string'i tek sinyal |
| "Public ama boş" envanter, private'dan ayrılabiliyor | `users.js:625-637` — iki ayrı **başarı** dalı: `total_inventory_count === 0` ve CS2'ye özel `appID 730 && !body.assets` → her ikisi `callback(null, [], [], count)` |
| Rate limit değerleri normatif değil | 08 §2.6 açılış cümlesi: "Steam resmi rate limit belgeleri yayınlamaz. Aşağıdaki değerler topluluk deneyimi ve pratik gözlemlere dayanır." Community satırı: "~10-20 istek/dakika (IP başına)". 08'in atıf yaptığı 10 §4'te sayısal tavan yok |
| Sidecar iç HTTP sözleşmesi normatif dokümanda kayıtlı değil | 08 §1 kapsam dışı bırakıyor; 05 §3.4 yalnız `X-Internal-Key`/ağ katmanını veriyor. Buna karşılık 07 §6.1'in public sözleşmesi (200 / 422 `INVENTORY_PRIVATE` / 503 `STEAM_UNAVAILABLE`) normatif → korundu |
| 06'da envanter görünürlüğü enum'u yok | `06_DATA_MODEL.md` §2.1–§2.24 tarandı; TransactionStatus…DeliveryEvidence arasında görünürlük sözlüğü yok |
| Gövdeye yeni alan eklemek backend'i kırmaz | System.Text.Json varsayılanı bilinmeyen üyeleri yok sayar; `JsonUnmappedMemberHandling` tüm `backend/` ağacında 0 eşleşme; `JsonSerializerDefaults.Web` de Disallow açmaz |

**Mini güvenlik kontrolü.** Secret sızıntısı yok — eklenen tek env bir sayısal limit, log'lanmıyor ve secret değil. Auth/authorization etkisi yok — uç zaten `internalKeyAuth` arkasında, yeni uç eklenmedi. Input validation **güçlendi**: `refresh` katı allowlist ile doğrulanıyor ve tanınmayan değer servise ulaşmadan 400 alıyor; `steamId` regex'i değişmedi. Yeni dış bağımlılık yok. Ek olarak `positiveIntFromEnv` bir fail-open yolunu kapatıyor: geçersiz env değeri artık throttling'i sessizce devre dışı bırakamaz.

**Kapsam dışı bırakılanlar (bilinçli).** Backend port değişikliği (T121) · gerçek Steam ölçümü (T122) · `refresh` çağıranlarının bağlanması (T123/T125/T129) · bot/trade-offer modüllerinin silinmesi (T133 — `SidecarWebhookRouteContractTests.RetiredPathsAreStillPublished_UntilT133` bu yolların hâlâ yayınlanmasını şart koşuyor, fırsatçı temizlik contract leg'ini kırardı) · `sidecar-fake` envanter sürme kontrolü (T137) · 8 advisory E2E leg'inin kırmızılığı (T137/T138).

**Doküman güncellemesi gerekmedi.** 08 §2.3 ve §2.6 bu davranışı zaten **normatif olarak dayatıyordu** (v3.0/T115); T120 onları uygulayan koddur, değiştiren değil. Değişen tek doküman `DEPLOY_RUNBOOK.md` env tablosudur (yeni operasyonel ayar).
