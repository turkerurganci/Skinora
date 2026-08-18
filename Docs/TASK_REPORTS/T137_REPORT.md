# T137 — sidecar-fake sürülebilir envanter

**Faz:** F7 (P7, plan gereği P5 ile paralel) | **Durum:** ⏳ Düzeltme turu uygulandı — yeniden doğrulama bekliyor (tur 1 ✗ FAIL) | **Tarih:** 2026-08-18 (yapım) · 2026-08-18 (doğrulama tur 1) · 2026-08-18 (düzeltme turu)

---

## Yapılan İşler

Fake sidecar'ın `GET /api/inventory/:steamId` ucu `steamId` parametresini **yok sayıyordu**: tek sabit `INVENTORY_ITEMS` listesi dönüyordu, yani satıcı ve alıcı aynı envanteri görüyordu. P2P teslimat tam da bu farktan çıkarılır — asset satıcıdan çıkar, sınıfının bir kopyası alıcıda görünür (02 §9.2, 06 §3.5) — dolayısıyla steamId-kör bir envanter teslimatı **hiç** simüle edemez, yalnızca kalıcı bir "hiçbir şey değişmedi" üretir. T137a ölçümü bu boşluğu adıyla kaydetmişti (`sidecar-fake/src/routes/steam.ts:40`, handler `(_req, res)`).

- **Yeni `sidecar-fake/src/inventoryStore.ts`** — steamId başına holdings + 08 §2.3 üç değerli visibility + steamId başına trade-hold durumu. İki Express yüzeyi tek process'te koştuğu için (`index.ts`) modül düzeyi state hem backend'e bakan 5100'den hem kontrol yüzeyi 5200'den görünür.
- **`GET /api/inventory/:steamId` store'dan servis ediliyor** ve statü kodları **gerçek sidecar ile birebir**: `PUBLIC`→200, `PRIVATE`→422 `INVENTORY_PRIVATE`, `UNAVAILABLE`→503 `STEAM_UNAVAILABLE`; `visibility` gövdede de taşınıyor (`sidecar-steam/src/api/routes.ts:121-140` paritesi). Okunamaz cevaplar **items dizisi taşımıyor** — o çöküş "profil gizli"yi "item envanterde yok"a çeviren şeydir.
- **`GET /api/trade-hold/:steamId` sürülebilir** — varsayılan bugünkü değer (MA açık, hold 0), test `active: false` sürerek T119a'nın accept ucundaki 403 `MOBILE_AUTHENTICATOR_REQUIRED` dalını tetikleyebiliyor.
- **Kontrol yüzeyi (`/__e2e/steam/*`)** — `POST inventory` (seed: items ve/veya visibility) · `GET inventory/:steamId` (store'u geri okuma) · `POST trade` (asset'i taşır, varış assetId'sini **döndürür** — 06 §8.4 rotasyonu; class + instance korunur; ters yön çağrısı T129 geri alma senaryosu) · `POST trade-hold` · `POST reset`.
- **Custody dönemi trade yüzeyi emekli edildi** — `POST /api/trade-offers/send` (self-drive webhook'ları dahil), `/__e2e/trade/suppress-accept`, `/__e2e/trade/reset`, `tradeControl.ts` (+testi), `ids.ts:fakeOfferId` (+test bloğu), `config.botSteamId`, `config.tradeAcceptDelayMs`, compose'daki `FAKE_BOT_STEAM_ID` + `FAKE_TRADE_ACCEPT_DELAY_MS`.
- **Harness** — `e2e/src/api.ts`'e `setFakeInventory` / `getFakeInventory` / `simulateFakeTrade` / `setFakeTradeHold` / `resetFakeSteamState` (+`fakeGet`); emekli `suppressTradeAccept` / `resetTradeControl` ve 10 çağrı yeri 4 spec'ten kaldırıldı, `resetTradeControl()` hook'ları `resetFakeSteamState()`'e çevrildi.
- **Girdi allow-list'i** — `resolveItem` çağıranın anahtarları üzerinde değil, sabit alan listesi üzerinde dönüyor: bilinmeyen bir alan **400** ile reddediliyor (sessizce yutulan bir `assetid` yazım hatası yanlış envanter seed'ler ve üç adım sonra başka bir dosyada patlar), ve `__proto__` bir alan yerine prototipe atanamıyor.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `sidecar-fake/src/inventoryStore.ts` | **yeni** — store + katalog + trade + visibility + HTTP cevabı |
| `sidecar-fake/src/inventoryStore.test.ts` | **yeni** — 31 unit test |
| `sidecar-fake/src/routes/steam.ts` | store'dan servis; custody trade route'u kaldırıldı (169→41 satır) |
| `sidecar-fake/src/routes/control.ts` | `/__e2e/trade/*` çıktı, `/__e2e/steam/*` girdi |
| `sidecar-fake/src/config.ts` | `botSteamId` + `tradeAcceptDelayMs` kaldırıldı |
| `sidecar-fake/src/ids.ts` · `ids.test.ts` | `fakeOfferId` kaldırıldı |
| `sidecar-fake/src/tradeControl.ts` · `tradeControl.test.ts` | **silindi** |
| `sidecar-fake/README.md` | yeni yüzey + emeklilik notu + env listesi |
| `docker-compose.e2e.yml` | iki custody env değişkeni düştü |
| `e2e/src/api.ts` · `e2e/src/config.ts` | yeni helper'lar; emekli helper'lar çıktı |
| `e2e/tests/{timeout,downtime,emergency-hold,payment-edge-cases}.spec.ts` | emekli lever çağrıları kaldırıldı / reset'e çevrildi |
| `.github/workflows/ci.yml` | `frontend-test` job'ının `needs`'inden `build` çıkarıldı (D5 — aşağıda) |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `steamId` başına envanter kontrol edilebiliyor | ✓ | Unit: `inventoryStore.test.ts` "seeds items and reports them back", "replaces the previous holdings", "keeps items when only visibility is driven", "reads an undriven steamId as PUBLIC and EMPTY" · **Canlı HTTP** (5111 steam / 5222 kontrol, tek process): satıcıya seed → `GET /api/inventory/<satıcı>` `200` 1 item, `GET /api/inventory/<alıcı>` `200` `items: []` — iki steamId **farklı** cevap veriyor (T137 öncesi imkânsızdı) |
| 2 | Trade simüle | ✓ | Unit: "moves the asset out of the seller and into the buyer under a NEW id", "adds to the class count the buyer already had", "is deterministic in the id it mints", "supports the reverse leg (T129)", "refuses to move an asset the sender does not hold" · **Canlı HTTP**: `POST /__e2e/steam/trade` → `{"ok":true,"newAssetId":"417536255474257315"}`; sonrasında satıcı `items: []`, alıcı **rotasyonlu** assetId + **aynı** classId/instanceId |
| 3 | (D3, proje sahibi kararı) visibility'de gerçek sidecar paritesi | ✓ | Unit: 422/503 blokları + "no items" assertion'ları · **Canlı HTTP**: `PRIVATE` → `422 {"visibility":"PRIVATE","code":"INVENTORY_PRIVATE",...}`, `UNAVAILABLE` → `503 {"code":"STEAM_UNAVAILABLE",...}`, ikisinde de `items` alanı yok |
| 4 | (D4, proje sahibi kararı) trade-hold per-steamId sürülebilir | ✓ | Unit: "drives the MA flag per steamId", "keeps the untouched half" · **Canlı HTTP**: sürülen steamId `{"active":false,...}`, sürülmemiş steamId `{"active":true,"escrowEndDurationSeconds":0}` |
| 5 | (D2, proje sahibi kararı) custody trade yüzeyi emekli | ✓ | **Canlı HTTP**: `POST /api/trade-offers/send` → **404**, `POST /__e2e/trade/suppress-accept` → **404** · `grep -rn "trade-offers\|trade-events" backend/src --include=*.cs` → çağıran istemci yok, webhook controller'ı yok (yalnız `BlockchainWebhooksController` / `ResendWebhooksController` / `WebhooksController`) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (sidecar-fake) | ✓ **38/38 passed** | Lokal `npm test` → `ids 4 · hmac 3 · inventoryStore 31`, 3 dosya. Taban 12'ydi (`tradeControl` 5 testi emeklilikle düştü, 31 yeni test geldi). **CI'da da koştu** (D5 düzeltmesinden sonra): run `32143961035` → `Sidecar-fake vitest` adımı `Tests 38 passed (38)` |
| Build (sidecar-fake) | ✓ | `npx tsc --noEmit` exit 0 · `npm run build` exit 0 |
| Lint/format (sidecar-fake) | ✓ | `npm run lint` 0 · `npm run format:check` "All matched files use Prettier code style!" |
| Typecheck/lint (e2e) | ✓ | `npx tsc --noEmit` exit 0 · `npm run lint` 0 |
| Format (e2e) | ✓ | Lokal `format:check` 14/14 dosyayı uyarıyor — **dokunulmayanlar dahil**, yani bilinen CRLF artefaktı. Değiştirdiğim 6 dosya LF'e normalize edilip tek tek kontrol edildi (`tr -d '\r' \| prettier --check --stdin-filepath`) → **6/6 LF-clean**; CI'ın LF lint'i yetkili |
| Firsthand HTTP smoke | ✓ | Fake 5111/5222'de koşturuldu; seed / trade / rotasyon / 422 / 503 / trade-hold / 400 doğrulama / 404 emeklilik / reset uçtan uca doğrulandı |
| Backend | — | **Sıfır backend değişikliği** (`git diff main...HEAD` yalnız `sidecar-fake/`, `e2e/`, `docker-compose.e2e.yml`, `Docs/`, `.claude/`) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✗ **FAIL** (tur 1, 2026-08-18 — ayrıntı: §Doğrulama — Tur 1) → **düzeltme turu uygulandı** (§Düzeltme Turu — Uygulandı); yeniden doğrulama bekliyor |
| Bulgu sayısı | 1 bloke edici (**B1**) + 2 bloke etmeyen (**N1**, **N2**) — **üçü de kapatıldı** |
| Düzeltme gerekli mi | Uygulandı — B1 doküman yarısı `33cd1e4` + harness yarısı `d4149b1` · N1 `33cd1e4` · N2 `d4149b1` |

## Altyapı Değişiklikleri

- **Migration:** Yok (backend'e dokunulmadı).
- **Config/env değişikliği:** `FAKE_BOT_STEAM_ID` ve `FAKE_TRADE_ACCEPT_DELAY_MS` `docker-compose.e2e.yml`'den ve `config.ts`'den **kaldırıldı** — yalnız e2e stack'i etkiler, üretim yapılandırması değil. Yeni env değişkeni yok.
- **Docker değişikliği:** Yok (yalnız fake servisin iki env satırı).

## Commit & PR

- Branch: `task/T137-fake-drivable-inventory`
- Commit: `050620c` (kod) · `c0412f6` (rapor/status/memory) · CI açığı düzeltmesi (D5) · `33cd1e4` (düzeltme turu — plan/doküman yarısı) · **`d4149b1` (düzeltme turu — harness seed + N2)** · `423ec21` (düzeltme turu ölçümü — rapor/status/memory)
- PR: [#246](https://github.com/turkerurganci/Skinora/pull/246)
- CI: ✓ **kodun tamamını kapsayan run — `fd969ef` → [`32143961035`](https://github.com/turkerurganci/Skinora/actions/runs/32143961035) `conclusion=success`** — bloke edici **14/14** yeşil (`1. Lint` · `2. Build` · `3. Unit test` · **`3b. JS test (vitest)`** · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build` ×4 · `CI Gate`; `0. Guard` skipped). Bu push `.github/workflows`'a dokunduğu için tüm path filtreleri açıldı → run tam kapsamlı koştu.
  - **D5 düzeltmesinin kanıtı run içinde:** `3b. JS test (vitest)` job'ının `Sidecar-fake vitest` adımı **çalıştı** ve `Tests 38 passed (38)` / `Test Files 3 passed (3)` bastı — düzeltme öncesi aynı job `skipped`'dı.
  - **Sonraki (docs-only) run:** `7f7f15d` → [`32145477001`](https://github.com/turkerurganci/Skinora/actions/runs/32145477001) `success`, yine bloke edici 14/14 (yalnız rapor/status/memory değişti; `.github/workflows` farkı base'e göre hâlâ diff'te olduğu için filtreler açık kaldı ve run tam kapsamlı koştu). Bu commit'ten sonraki her commit **doküman-only**'dir — kod kanıtı yukarıdaki run'dır.
  - Önceki run'lar: `050620c` [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605) `success` (ama `3b. JS test` **skipped** — D5 bulgusunun kanıtı) · `c0412f6` [`32142550427`](https://github.com/turkerurganci/Skinora/actions/runs/32142550427) `success` (docs-only) · `1af5404` [`32143864749`](https://github.com/turkerurganci/Skinora/actions/runs/32143864749) **`cancelled`** (sonraki push iptal etti — task.md concurrency notu; o run'da `3b. JS test` artık `skipped` değil, koşarken iptal oldu).

## Yapım İçinde Bulunan CI Açığı (D5 — proje sahibi onaylı düzeltme)

**Bulgu:** `3b. JS test (vitest)` job'ı `needs: [changes, build]` diyordu; `2. Build` ise `if: needs.changes.outputs.code == 'true'` ile koşuyor ve `code` filtresi `backend / frontend / sidecar-steam / sidecar-blockchain / .github/workflows` — **`sidecar-fake` yok**. GitHub, atlanan bir `needs` bağımlılığının ardındaki job'ı da atladığı için, yalnız `sidecar-fake/**` değişen bir PR'da `build` atlanıyor ve JS test job'ı da atlanıyordu — **job'ın kendi `if`'i `sidecar-fake == 'true'`u açıkça saydığı hâlde.** Yani fake'in unit testleri CI'da **hiç** koşamıyordu.

**Kanıt:** ilk dal run'ı [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605) (`050620c`) — `2. Build` **skipped**, `3b. JS test (vitest)` **skipped**, CI Gate yine `success`. Bu turda yazılan 38 test o run'da hiç çalışmadı; yeşil CI onları kanıtlamıyordu.

**Düzeltme (proje sahibi seçimi):** `frontend-test` job'ının `needs`'i `[changes, build]` → `[changes]`. Job build çıktısı tüketmiyor (her adım kendi `npm ci`'sini yapıyor), yani kenar bir bağımlılık değil sıralamaydı. Bloke edicilik değişmedi — `ci-gate.needs` listesi ve `if: always()` + `contains(needs.*.result, 'failure')` mantığı aynı. Alternatif (`code` filtresine `sidecar-fake/**` eklemek) reddedildi: fake-only bir PR'da backend+frontend build'ini de tetiklerdi.

**Kalıcı ders:** bir job'ın kendi `if`'inde bir filtreyi saymış olması, o filtre gerçekleştiğinde job'ın koşacağı anlamına gelmez — `needs` zincirindeki **atlanan** bir halka koşulu sessizce geçersiz kılar. "Test suite'i CI'a bağlandı" ile "CI o suite'i koşuyor" farklı şeylerdir ve ilki ikincisini göstermez (T129 B3'ün CI düzlemindeki ikizi: orada formülün tetikleyicisi eksikti, burada testin tetikleyicisi).

## Known Limitations / Follow-up

1. **(DÜZELTME TURUNDA KAPATILDI — bkz. §Düzeltme Turu — Uygulandı; ölçüm `d4149b1`'de 10/32'ye döndü.)** **Advisory e2e ölçümü düştü — beklenen ve proje sahibi onaylı (D1).** Sürülmemiş steamId artık **boş** okunduğu için ve hiçbir spec henüz envanter seed etmediği için, create çağrısı `ITEM_NOT_IN_INVENTORY` ile reddediliyor. **Ölçülen (run [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605), 8/8 leg koştu):** 32 testin **4'ü pass / 28'i fail** — T137a tabanı 10/32'ydi.

   | Leg | T137a tabanı | T137 sonrası |
   |---|---|---|
   | happy-path | 0/1 | 0/1 |
   | T108 cancellation | 0/4 | 0/4 |
   | T109 timeout | 1/4 | **0/4** |
   | T110 payment edge cases | 0/6 | 0/6 |
   | T111 fraud-flags | 3/4 | **0/4** |
   | T112 emergency-hold | 0/3 | 0/3 |
   | T113 admin-flows | 6/7 | **4/7** |
   | T114 downtime | 0/3 | 0/3 |
   | **Toplam** | **10/32** | **4/32** |

   **Tekrarlanabilirlik:** ölçüm **iki tam run'da birebir aynı** çıktı — [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605) (`050620c`) ve dal HEAD [`32143961035`](https://github.com/turkerurganci/Skinora/actions/runs/32143961035) (`fd969ef`); her ikisinde de 8/8 leg sonuç üretti ve leg başına sayılar aynı.

   Kaybedilen 6 testin tamamı bir transaction **yaratıyor**; düşme imzası tek ve aynı: `create failed: {"code":"ITEM_NOT_IN_INVENTORY","message":"Item is not in the seller's Steam inventory."}` (happy-path leg logu). Yani kayıp yeni bir kırılma değil, **seed sorumluluğunun görünür hâle gelmesi**. Ayakta kalan 4 test admin-flows'un envantere dokunmayan bölümü. Zincir T138'e kadar zaten kırmızıydı (T137a: "bu görev hiçbir leg'i yeşile çevirmiyor... yeşil beklentisi T137 + T135 + T138 zincirinin sonunda doğar"), legler `continue-on-error` + `ci-gate.needs` dışında olduğu için **CI Gate etkilenmez**. **T138 için somut sonuç:** her spec artık satıcı envanterini `api.setFakeInventory(...)` ile seed etmek zorunda — bu, T138'in yeniden yazım listesine eklenmesi gereken mekanik bir ön adımdır.
2. **Emekli lever'ların senaryoları T138'e kaldı.** `timeout.spec.ts`'in iki testi (satıcı trade-offer timeout / delivery timeout) ve `downtime.spec.ts`'in Steam-outage testi custody durumlarına dayanıyordu; lever'ları kaldırıldı ve yerlerine P2P karşılıklarını **adıyla** söyleyen notlar bırakıldı (confirm-ready deadline'ı, "satıcı hiç trade etmez", ACCEPTED'da bekleyen Steam-bound durum). Yeniden yazım T138'in kabul kriterinde.
3. **Trade lock / cooldown modellenmedi** — bilinçli. Hiçbir tüketici lock durumunu okumuyor (`DeliveryVerificationService` neden okumaması gerektiğini uzun uzun belgeliyor: anonim okumada süre bilgisi yok), ölçülmemiş bir sinyali simüle etmek testi ona assert etmeye davet ederdi.
4. **`.claude/CONTEXT.md` dosya haritasında `sidecar-fake/` ve `e2e/` hiç yok** — T107'den beri var olan bir boşluk, T137 kusuru değil. Yapısal doküman değişikliği proje sahibi onayı gerektirdiği için (GUARDRAILS §3) bu turda açılmadı; ayrı bir `chore:` turu adayı.
5. **`C:/projects/Escrow-T137a` worktree'si duruyor** — T137a merge edildiği için gereksiz; proje sahibi bu turda yalnız `Escrow-T137`'nin kaldırılmasını onayladı.

## Notlar

### Öz-denetim (yapım turu içi — bağımsız doğrulamanın yerini TUTMAZ)

Bulguları bağımsız validator'a bırakmadan önce dört mercek yapım chat'inde koşuldu (INSTRUCTIONS §3.3 gereği bu bir ön-kontroldür, doğrulama ayrı chat'te yapılır). Hayatta kalan bulgu **yok**; üretilen kanıt:

- **Backend kapıları.** `SidecarSteamInventoryReader.GetItemAsync` asset id'yi ordinal eşliyor, `IsTradeable`'ı sidecar'ın `tradable`'ından alıyor ve `MarketHashName`'i fraud ön-kontrolüne taşıyor (`TransactionCreationService` Stage 5 → `ItemNotInInventory` / `ItemNotTradeable` / `InventoryPrivate` / `SteamUnavailable`). Seed üçünü birden karşılıyor; **ampirik teyit:** sekiz leg logunda `ITEM_NOT_TRADEABLE` · `INVENTORY_PRIVATE` · `STEAM_UNAVAILABLE` **sıfır** eşleşme (fraud-flags'teki iki `PRICE_DEVIATION` senaryonun kendisidir ve `marketHashName` ↔ `ItemPriceCaches` eşleşmesinin çalıştığını gösterir).
- **Alıcı gerçekten dokunulmamış.** Repo genelinde `setFakeInventory`'nin tek çağıranı `seedHappyPath` (satıcı); hiçbir spec envanter sürmüyor.
- **Import döngüsü.** `db.ts` ve `api.ts` karşı modüle yalnız fonksiyon gövdesinde dokunuyor (`api.ts`'te tek kullanım satır 63'teki varsayılan parametre); `npx playwright test --list` dokuz spec dosyasını da yükleyip 33 testi listeliyor. `db.ts`'i Playwright dışında koşturan script veya CI adımı yok, dolayısıyla yeni `throw` yalnız fake ayakta olması gereken bağlamda ateşlenebilir.
- **Seed'e karşı saldırı merceği.** Hiçbir spec satıcının item'ı **taşımamasına** dayanmıyor (envanter kaynaklı bir reddi assert eden test yok), yani koşulsuz seed hiçbir senaryonun anlamını sessizce değiştirmiyor. `setInventory` item listesini **replace** ediyor ve aynı envanterde tekrarlı `assetId`'yi reddediyor; `playwright.config.ts` `workers: 1` + `fullyParallel: false` → biriken kopya ya da yarış yok.
- **Kapsam.** Düzeltme turu commit'leri (`33cd1e4..HEAD`) yalnız `e2e/src/db.ts` + üç dokümana dokundu; **hiçbir spec dosyası yok** — "hiçbir spec senaryosuna dokunulmaz" kriteri harfiyle karşılandı.

**Working tree hygiene (Adım -1):** temiz — `git status --short` 0 satır.

**Main CI startup check (Adım 0):** son 3 tamamlanmış run'ın hepsi `success` — [`32133727296`](https://github.com/turkerurganci/Skinora/actions/runs/32133727296) (Docker Publish, `787b1b3`) · [`32133727298`](https://github.com/turkerurganci/Skinora/actions/runs/32133727298) (CI, `787b1b3`) · [`32057012508`](https://github.com/turkerurganci/Skinora/actions/runs/32057012508) (Docker Publish, `669e5bb`).

**Dal anomalisi (kayda geçirildi):** `task/T137-fake-drivable-inventory` dalı ve `C:/projects/Escrow-T137` izole worktree'si bu session'dan **önce** açılmıştı (HEAD `669e5bb` = T131 öncesi main, **kendi commit'i yok**, working tree temiz) — T131 o sırada ana worktree'de sürdüğü için hazırlanmış, ama içinde hiç iş yapılmamış. Proje sahibi kararıyla boş worktree kaldırıldı, dal ana worktree'de `origin/main`'e (`787b1b3`) ff-lendi ve iş orada yürüdü.

**Dış varsayımlar (Adım 4 — hepsi kanıtla doğrulandı):**

| # | Varsayım | Kanıt |
|---|---|---|
| 1 | Envanter sözleşmesi (alan adları, visibility değerleri, statü kodları) bilinen ve sabit | `HttpSteamSidecarInventoryClient.cs:195-240` `JsonPropertyName` pinleri + `ParseVisibility` (`PUBLIC`→Success, `PRIVATE`→InventoryPrivate, **tanınmayan**→Unavailable); `sidecar-steam/src/api/routes.ts:121-140` → 200/422/503 |
| 2 | Fake'in custody trade yüzeyi ölü | `grep -rn "trade-offers\|api/trade" backend/src --include=*.cs` → çağıran istemci **0**; webhook controller taraması → `/api/v1/webhooks/steam/trade-events` **yok** |
| 3 | Tek process iki portu servis ediyor → in-memory store her iki yüzeyden görünür | `index.ts:6-16` tek `buildApp()` + iki `listen`; **canlı doğrulandı**: 5222'den seed → 5111'den okundu |
| 4 | Mutable global store yarış üretmez | `e2e/playwright.config.ts` `workers: 1`, `fullyParallel: false`, `retries: 0` |
| 5 | Yeni npm bağımlılığı gerekmiyor | Store saf `Map` + mevcut `express`; `package.json` dependencies **değişmedi** |
| 6 | `e2e` tsc/lint/format bloke edici CI adımı | `.github/workflows/ci.yml:222-224` — bu yüzden `api.ts` imzası değişince 4 spec de derlenir hâlde tutuldu |

**Yapım öncesi dört karar soruldu, dördü de cevaplandı:**

- **D1 — sürülmemiş steamId'nin varsayılanı:** proje sahibi **"bilinmeyen steamId → boş envanter"**i seçti (önerilen "eski 2 item herkese" seçeneği değil). Bedeli seçenek metninde açıkça yazılıydı ve kabul edildi: harness seed etmediği sürece bugün geçen 10 advisory test de düşer (bkz. Known Limitations #1). Kazancı: alıcı **sıfır** baseline ile başlar, yani teslimat deltası gerçek dünyadaki hâliyle ölçülür ve seed sorumluluğu görünür kalır.
- **D2 — custody trade yüzeyi:** **T137'de emekli edilsin** (önerilen). Gerekçe kanıtla: backend'de çağıran da yok, webhook ucu da yok.
- **D3 — visibility raporlaması:** **gerçek sidecar paritesi** (önerilen) — 200/422/503.
- **D4 — trade-hold:** **sürülebilir olsun** (önerilen); varsayılan bugünkü değer korundu, hiçbir mevcut akış etkilenmedi.
- **D5 — yapım sırasında bulunan CI açığı** (ilk dal run'ında `3b. JS test` skipped çıkınca soruldu): **T137'de düzeltilsin, `frontend-test.needs`'ten `build` çıkarılsın** (önerilen). Ayrıntı ve kalıcı ders yukarıdaki §Yapım İçinde Bulunan CI Açığı bölümünde.

**Mini güvenlik kontrolü (Katman 1):**

- **Secret sızıntısı:** yok — aksine bir sabit (`FAKE_BOT_STEAM_ID`) kaldırıldı. Yeni sabit/secret eklenmedi.
- **Auth/authorization:** kontrol yüzeyi mevcut `/__e2e/*` deseniyle aynı — **kimliksiz**, çünkü çağıran testin kendisi ve servis üretime **hiç** deploy edilmiyor (`docker-compose.e2e.yml`'e özel). Backend'e bakan `/api/*` route'ları `internalKeyAuth` arkasında kalmaya devam ediyor (`app.ts` sırası değişmedi).
- **Input validation:** yeni uçların hepsi doğruluyor — bilinmeyen `visibility`, bilinmeyen katalog adı, bilinmeyen/yazım hatalı item alanı, `assetId`/`classId` eksikliği, aynı envanterde tekrarlı `assetId`, dizi olmayan `items`, boş `steamId`, sahip olunmayan asset'in trade'i, kendine trade, negatif `escrowEndDurationSeconds` → hepsi **400**. Alan kopyalama sabit allow-list üzerinden yapıldığı için `__proto__` bir alan yerine prototipe atanamıyor (hem unit hem canlı HTTP ile doğrulandı).
- **Yeni dış bağımlılık:** yok.

---

## Doğrulama — Tur 1 (2026-08-18, ✗ FAIL)

**Validator:** bağımsız chat, yapım raporu görülmeden. **Dal HEAD:** `f8cdf4e` (lokal = `origin/task/T137-fake-drivable-inventory`). **Merge-base:** `787b1b3` (= `origin/main` HEAD).

### Kapı adımları

| Adım | Sonuç |
|---|---|
| −1 Working tree hygiene | ✓ `git status --short` 0 satır |
| 0 Main CI startup check | ✓ son 3 run `success` — [`32133727296`](https://github.com/turkerurganci/Skinora/actions/runs/32133727296) (Docker Publish, `787b1b3`) · [`32133727298`](https://github.com/turkerurganci/Skinora/actions/runs/32133727298) (CI, `787b1b3`) · [`32057012508`](https://github.com/turkerurganci/Skinora/actions/runs/32057012508) |
| 0b Repo memory drift | ✓ `.claude/memory/MEMORY.md:56` T137 satırı mevcut |
| 7a Dal CI | ✓ **dal HEAD'in kendi run'ı** [`32146723383`](https://github.com/turkerurganci/Skinora/actions/runs/32146723383) (`f8cdf4e`) `conclusion=success`, bloke edici 14/14 yeşil (`3b. JS test (vitest)` dahil) |

### Kabul kriterleri — bağımsız yeniden üretim

Validator fake'i lokalde `dist/`'ten koşturdu (5199 steam / 5198 blockchain, tek process) ve her kriteri **kendi** HTTP çağrılarıyla üretti.

| # | Kriter | Sonuç | Validator kanıtı |
|---|---|---|---|
| 1 | `steamId` başına envanter kontrol edilebiliyor | ✓ Karşılandı | Sürülmemiş `…060` → `200 {"visibility":"PUBLIC","items":[],"totalCount":0}` · seed sonrası `…060` → 1 item / `totalCount:1`, aynı anda `…061` → `items:[]`. İki steamId **farklı** cevap veriyor |
| 2 | Trade simüle | ✓ Karşılandı | `POST /__e2e/steam/trade` `…060→…061` `11111111001` → `{"ok":true,"newAssetId":"388965514727569895"}`; sonrasında satıcı `items:[]`, alıcıda **aynı** `classId 310776767` + **rotasyonlu** assetId. Ters bacak (T129 geri alma) da çalıştı → `913066708457036972`. Bu, `DeliveryVerificationService`'in okuduğu kanıtın **tam** şeklidir (satıcı tarafı: `ItemAssetId` gitti · alıcı tarafı: `ItemClassId` sayısı arttı + baseline'da olmayan yeni assetId → `candidateDeliveredAssetId`) |
| 3 | (D3) visibility'de gerçek sidecar paritesi | ✓ Karşılandı | `PRIVATE` → `422 {"visibility":"PRIVATE","code":"INVENTORY_PRIVATE",…}` · `UNAVAILABLE` → `503 {"code":"STEAM_UNAVAILABLE",…}`, ikisinde de `items` yok. Gerçek sidecar ile karşılaştırıldı: `sidecar-steam/src/api/routes.ts:121-140` (200/422/503 + `visibility` gövdede) ve kod sabitleri `InventoryService.ts:314,321` — kodlar **birebir** aynı; 200 gövdesi `items/totalCount/tradeableCount` ile `HttpSteamSidecarInventoryClient.SidecarInventoryEnvelope` alan adlarını karşılıyor |
| 4 | (D4) trade-hold per-steamId sürülebilir | ✓ Karşılandı | Sürülen `…060` → `{"active":false,…}` · sürülmemiş steamId → `{"active":true,"escrowEndDurationSeconds":0}` |
| 5 | (D2) custody trade yüzeyi emekli | ✓ Karşılandı | Canlı: `POST /api/trade-offers/send` → **404**, `POST /__e2e/trade/suppress-accept` → **404**. Bağımsız kontrol: `grep -rn "trade-offers\|TradeOfferDispatch\|trade-events" backend/src --include=*.cs` → **0 satır**; repo genelinde kalan atıflar yalnız tarihsel task raporları |

**Girdi doğrulama (Katman 1 mini güvenlik):** validator kendi 400 probe'larını koştu — bilinmeyen alan (`assetid`) → `400 unknown item field 'assetid'`, sahip olunmayan asset trade'i → `400 A does not hold asset nope`. `resolveItem` sabit allow-list üzerinde döndüğü için `__proto__` alan yerine prototipe atanamıyor (unit testi de bunu kapsıyor). Secret sızıntısı yok, yeni dış bağımlılık yok, backend'e bakan `/api/*` route'ları `internalKeyAuth` arkasında; `/__e2e/*` kimliksiz kalması pre-existing tasarım ve servis yalnız `docker-compose.e2e.yml`'de.

**Testler (validator'ın kendi koşumu):** `sidecar-fake` `npm test` → **38/38 passed** (3 dosya: ids 4 · hmac 3 · inventoryStore 31) · `npm run build` exit 0 · `npm run lint` 0 · `e2e` `npx tsc --noEmit` exit 0.

### Bulgular

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| B1 | S3 Eksik (bloke edici) | Onaylanan D1–D5 kararları ve ölçülen advisory e2e gerilemesi **kaynak dokümana yazılmadı**; seed yükümlülüğünün **sahibi yok** | `Docs/11_IMPLEMENTATION_PLAN.md` §T137 · §T138 |
| N1 | S1 Sapma (bloke etmeyen) | Plan §T138 "yalnız admin-flows T137'den bağımsız" diyor; ölçüm bunu yalanlıyor (6/7 → **4/7**) | `Docs/11_IMPLEMENTATION_PLAN.md` §T138 |
| N2 | S1 Sapma (bloke etmeyen) | `seed.itemAssetId` yorumu bayat: "Must match the fake's inventory item" — fake'in artık varsayılan envanter item'ı **yok**, sabit yalnız `ITEM_CATALOG` şablonuyla eşleşiyor | `e2e/src/db.ts:43-44` |

#### B1 — ayrıntı

Validator, yapım raporunu görmeden önce advisory e2e sinyalini **merge-base'e karşı** ölçtü ve gerilemeyi bağımsız olarak buldu:

| Leg | main `787b1b3` ([`32133727298`](https://github.com/turkerurganci/Skinora/actions/runs/32133727298)) | dal HEAD `f8cdf4e` ([`32146723383`](https://github.com/turkerurganci/Skinora/actions/runs/32146723383)) |
|---|---|---|
| happy-path | 0/1 | 0/1 |
| T108 cancellation | 0/4 | 0/4 |
| T109 timeout | **1/4** | 0/4 |
| T110 payment edge cases | 0/6 | 0/6 |
| T111 fraud-flags | **3/4** | 0/4 |
| T112 emergency-hold | 0/3 | 0/3 |
| T113 admin-flows | **6/7** | 4/7 |
| T114 downtime | 0/3 | 0/3 |
| **Toplam** | **10/32** | **4/32** |

Mekanizma tek ve deterministik — dal CI logunda 8 leg'de aynı imza: `create failed: {"code":"ITEM_NOT_IN_INVENTORY","message":"Item is not in the seller's Steam inventory."}`. Kök: `TransactionCreationService` Stage 5 satıcı envanterini okuyor; sürülmemiş steamId artık **boş** dönüyor ve **hiçbir spec/harness `setFakeInventory` çağırmıyor** (repo genelinde helper'ın çağıranı `0`).

Bu sonucun **kendisi** bulgu değildir — D1'de proje sahibi "bilinmeyen steamId → boş envanter"i bedeli yazılı olarak seçmiştir ve validator bu kararı sorgulamaz. Bulgu, kararın **nereye yazıldığıdır**:

1. `11_IMPLEMENTATION_PLAN.md` §T137'nin kabul kriteri hâlâ yalnız "steamId başına envanter kontrol edilebiliyor, trade simüle"dir; D1–D5'in hiçbiri, ölçülen 10/32 → 4/32 bedeli de dahil, planda **yok**. Projenin kendi kalıcı dersi (T122, T123 girişinde kayıtlı): *"onaylanmış kapsam değişikliği, kabul kriterlerinin KAYNAK dokümanına yazılmadıkça gerçekleşmemiştir."* Bugün planı okuyan biri, altı testin bilinçli ve fiyatlandırılmış bir kararla düştüğünü göremez.
2. Seed yükümlülüğü yalnız T137 raporunun "Known Limitations #1" maddesinde duruyor. §T138'in kabul kriterleri — ki T137a ölçümüyle bir kez zaten güncellendi — seed'den **hiç** söz etmiyor. Bu, T129 tur 3'ün kalıcı dersinin birebir tekrarıdır: *"advisory bir sinyal 'bloke etmediği' için değil sahibi olmadığı için ölür; bir sahibi ve bir kapatma tarihi olmalıdır."* Legler `continue-on-error` olduğu için T138 seed'i hiç eklemeden kapanabilir ve kimse fark etmez.

**Kapsam sorusu (proje sahibi kararı — validator karar vermez):** seed T137'de mi kapatılsın, T138'e mi kalsın? Validator'ın ölçtüğü olgu: 9 spec'in **tamamı** `seedHappyPath()` (`e2e/src/db.ts`) çağırıyor ve bu fonksiyon spec'lerin `beforeEach` reset'inden **sonra** koşuyor; dolayısıyla satıcı envanterini oraya seed etmek tek noktalı bir harness değişikliğidir, hiçbir spec senaryosuna dokunmaz ve D1'in kazancını (alıcı **sıfır** baseline'la başlar) korur — seed yalnız satıcıya yapılır.

#### N1 — ayrıntı

Plan §T138 "T137 bağımlılığının ölçülen gerekçesi" bloğu: *"8 spec'in 7'si bu yüzden T137'siz yeşile dönemez; yalnız admin-flows T137'den bağımsız."* Ölçüm bunu yalanlıyor: admin-flows **6/7 → 4/7**, düşen üç testin üçü de `ITEM_NOT_IN_INVENTORY` (AC1 satır 100 · AC2 satır 137 · AC3 satır 174). B1'in doküman turunda birlikte düzeltilmeli.

### Yapım raporu karşılaştırması

Validator kendi verdict'ini oluşturduktan **sonra** raporu okudu.

- **Uyum:** Kabul kriterleri 1–5'te tam uyum — validator beşini de bağımsız olarak yeniden üretti, rapordaki her kanıt doğrulandı, abartı veya boşluk yok.
- **Ölçüm uyumu:** Rapor Known Limitations #1 aynı gerilemeyi (10/32 → 4/32), aynı mekanizmayı ve aynı leg dağılımını **kendisi** kaydetmiş; validator'ın merge-base ölçümü rapordaki T137a tabanıyla birebir örtüştü. Rapor bu noktada dürüst ve eksiksizdir — B1 raporun bir şeyi gizlemesi değil, **raporda kalmış olmasıdır**.
- **Tek uyuşmazlık:** Rapor seed'i "T138'in yeniden yazım listesine eklenmesi gereken mekanik bir ön adım" diye niteliyor; plan §T138'de böyle bir madde yok ve rapor onu eklemiyor. B1 tam olarak budur.
- **D5 (CI açığı) bağımsız doğrulandı:** `code` filtresi gerçekten `sidecar-fake/**` içermiyor (`ci.yml:60-65`); ilk dal run'ı [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605)'te `3b. JS test` **skipped**, düzeltmeden sonra dal HEAD run'ında **success** + `Tests 38 passed (38)`. `ci-gate.needs` listesi değişmedi (`ci.yml:723-736`), yani bloke edicilik korunmuş. Düzeltme doğru ve gerekçesi doğru.

### Verdict

**✗ FAIL** — kod tarafı temiz ve beş kabul kriterinin beşi de bağımsız olarak karşılandı; bloke eden şey **doküman/sahiplik** boşluğudur (B1). Düzeltme turu `11_IMPLEMENTATION_PLAN.md` §T137 + §T138 üzerinde, proje sahibinin kapsam kararının ardından yapılır. Dal merge **edilmez**.

### Düzeltme turu — kaynak dokümana işlendi (2026-08-18)

Proje sahibi kararı: **"T137'de kapat + plana yaz"**. `Docs/11_IMPLEMENTATION_PLAN.md` §P7 T137'ye **DÜZELTME TURU** bloğu yazıldı — D1–D5 kararları **NİHAİ ŞEKİL** olarak, D1'in ölçülen bedeli (10/32 → 4/32 leg tablosuyla) ve B1/N1/N2'nin düzeltme turu kabul kriterleri. §T138'e **envanter seed sorumluluğu** kabul kriteri eklendi ve N1 (admin-flows bağımsızlık iddiası) aynı blokta düzeltildi.

**Kalan iş (ayrı yapım chat'i — INSTRUCTIONS §3.3 izolasyon):** `e2e/src/db.ts` `seedHappyPath()` satıcı envanterini seed eder (yalnız satıcı; alıcının SIFIR baseline'ı korunur), N2 yorumu güncellenir, ölçüm yeniden alınır (hedef: taban 10/32'nin geri gelmesi). Ardından yeniden doğrulama turu açılır.

---

## Düzeltme Turu — Uygulandı (2026-08-18)

Doğrulama tur 1'in üç maddesinin **kod/harness yarısı** bu turda kapatıldı; doküman yarısı (B1'in plana yazılması + N1) `33cd1e4`'te inmişti (§Düzeltme turu — kaynak dokümana işlendi). Proje sahibi kararı: **"T137'de kapat + plana yaz"**. Kabul kriterleri `11_IMPLEMENTATION_PLAN.md` §P7 T137 "DÜZELTME TURU KABUL KRİTERLERİ" bloğundan geliyor.

### B1 — `seedHappyPath()` satıcının fake envanterini seed ediyor

**Değişen tek dosya:** `e2e/src/db.ts` (+39/−1). Hiçbir spec senaryosuna dokunulmadı, backend'e dokunulmadı, yeni bağımlılık yok.

- `seedHappyPath()` sonunda tek çağrı: `setFakeInventory(seed.sellerSteamId, { items: [{ catalog: 'AK47_REDLINE', assetId: seed.itemAssetId, name/marketHashName: seed.itemMarketHashName }] })`.
- **Yalnız satıcı.** Alıcı hiç seed edilmiyor — SIFIR baseline D1'in kazancıdır ve teslimat deltasının (06 §3.5) ölçüldüğü zemindir.
- **Neden burası:** dokuz spec'in **tamamı** bu fonksiyonu çağırıyor ve fonksiyon spec'lerin `beforeEach` `resetFakeSteamState()` çağrısından **sonra** koşuyor — tek noktalı bir harness değişikliği create'i suite genelinde geri getiriyor.
- **Sabitler seed'i sürüyor:** `assetId` + `marketHashName` `seed` objesinden geliyor, yani `ItemPriceCaches` satırıyla tek kaynaktan tutarlı; katalog şablonu yalnız kalan alanları (class, instance, type, exterior, tradable/marketable) veriyor.
- **Sessiz başarısızlık yok:** seed 2xx dönmezse harness `Error` fırlatıyor — T137a'nın dersi (sessizce no-op'a düşen bir setup adımı dört tur boyunca fark edilmedi) bu adımda tekrarlanamaz.
- **Import döngüsü:** `db.ts → api.ts → db.ts` oluştu; iki taraf da karşı modüle **yalnız fonksiyon gövdesinde** dokunduğu için çalışma anında sorun yok. Kanıt: `npx playwright test --list` dokuz spec dosyasını da yükleyip 33 testi listeliyor.

### N2 — bayat yorum düzeltildi

`db.ts`'teki item sabitlerinin yorumu "Must match the fake's inventory item" diyordu; fake'in artık varsayılan envanteri yok (T137 D1). Yeni yorum sabitlerin **seed'i sürdüğünü**, `ItemPriceCaches` satırının aynı ada bağlı olduğunu ve kalan alanların `AK47_REDLINE` katalog şablonundan geldiğini söylüyor.

### Ölçüm — hedef karşılandı: taban geri geldi (10/32)

**Run:** [`32156212760`](https://github.com/turkerurganci/Skinora/actions/runs/32156212760) (`d4149b1`) — 8/8 leg sonuç üretti.

| Leg | T137a tabanı (`787b1b3`) | T137 tur 1 (`f8cdf4e`) | **Düzeltme sonrası (`d4149b1`)** |
|---|---|---|---|
| happy-path | 0/1 | 0/1 | 0/1 |
| T108 cancellation | 0/4 | 0/4 | 0/4 |
| T109 timeout | 1/4 | 0/4 | **1/4** |
| T110 payment edge cases | 0/6 | 0/6 | 0/6 |
| T111 fraud-flags | 3/4 | 0/4 | **3/4** |
| T112 emergency-hold | 0/3 | 0/3 | 0/3 |
| T113 admin-flows | 6/7 | 4/7 | **6/7** |
| T114 downtime | 0/3 | 0/3 | 0/3 |
| **Toplam** | **10/32** | **4/32** | **10/32** |

**Tekrarlanabilirlik — ölçüm üç tam run'da birebir aynı.** Rapor/status commit'i `423ec21` push edildiğinde path filtresi (dal ↔ main farkı `e2e/**` içerdiği için) legleri yeniden açtı ve aynı kod ikinci kez ölçüldü: [`32163260494`](https://github.com/turkerurganci/Skinora/actions/runs/32163260494) → yine **10/32**, leg başına aynı dağılım (timeout 1/4 · fraud-flags 3/4 · admin-flows 6/7 · kalan beş leg 0/N) ve `ITEM_NOT_IN_INVENTORY` yine yalnız `downtime` leg'inde (2 eşleşme). Öz-denetim commit'i `bc7410b`'nin run'ı [`32165358912`](https://github.com/turkerurganci/Skinora/actions/runs/32165358912) üçüncü kez ölçtü: **yine 10/32**, aynı leg dağılımı, `ITEM_NOT_IN_INVENTORY` yine yalnız downtime (2); o run da `success` ve bloke edici jobların hepsi yeşil. Tur 1'in 4/32'si de iki run'da aynı çıkmıştı — ölçüm bu görevde beş run boyunca deterministik.

**Sayı değil, küme aynı.** Karışık üç leg'in (timeout · fraud-flags · admin-flows) başarısız test **başlıkları** tabanla birebir karşılaştırıldı ve aynı çıktı — timeout: satıcı trade-offer / payment / delivery timeout · fraud-flags: high volume · admin-flows: AC1 (tabanda da kırmızı, T137 ile ilgisiz). Tek fark `timeout.spec.ts`'te T137'nin kaydırdığı satır numaraları. Kalan beş leg tabanda da 0/N. Yani 10/32 "tesadüfen aynı sayı" değil, **aynı on test**.

**Mekanizma doğrulaması.** `ITEM_NOT_IN_INVENTORY` imzası sekiz leg'in **yedisinde tamamen kayboldu** (job loglarında 0 eşleşme; tur 1'de sekizinde de vardı). Kalan başarısızlıkların imzası artık `Error: timeout awaiting ITEM_ESCROWED ... (last status=ACCEPTED)` — yani create **ve** accept geçiyor, işlem T117'de emekli edilen **custody durumunda** takılıyor. Bu, T137a'nın ölçtüğü tablonun aynısıdır (22 test custody durumlarında takılı) ve T138'in yeniden yazım kapsamıdır.

**Tek istisna — `downtime` leg'i (2 eşleşme).** `downtime.spec.ts`'in iki testi `resetFakeSteamState()`'i **test gövdesinin içinde**, `seedHappyPath()`'ten **sonra** çağırıyor (satır 167 · 238) ve seed'i siliyor; create yine `ITEM_NOT_IN_INVENTORY` alıyor. Leg tabanda da **0/3** olduğu için 10/32 hedefi etkilenmiyor. Düzeltmesi spec senaryosuna dokunmayı gerektirir, bu turun kabul kriteri ise "hiçbir spec senaryosuna dokunulmaz" diyor — dolayısıyla **T138'in envanter-seed kabul kriterine dahildir** (plan §T138).

### Kanıt

| Kapı | Sonuç |
|---|---|
| `npx tsc --noEmit` (e2e) | ✓ temiz |
| `npm run lint` (e2e / eslint) | ✓ 0 bulgu |
| prettier (`--config .prettierrc.json`, LF kopya) | ✓ temiz — lokal CRLF uyarıları bilinen artefakt, yetkili kapı CI "1. Lint" |
| `npx playwright test --list` | ✓ 9 spec / 33 test yükleniyor (import döngüsü probu) |
| CI (rapor/status commit'i `423ec21`) [`32163260494`](https://github.com/turkerurganci/Skinora/actions/runs/32163260494) | ✓ `conclusion=success` — bloke edici jobların hepsi yeşil; ikinci ölçüm de 10/32 |
| CI [`32156212760`](https://github.com/turkerurganci/Skinora/actions/runs/32156212760) | ✓ `conclusion=success` — bloke edici jobların hepsi yeşil (`Detect changed paths` · `1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build` ×4 · `CI Gate`; `0. Guard` tasarım gereği skipped) |

**Working tree hygiene (Adım -1):** temiz — `git status --short` 0 satır. **Main CI startup check (Adım 0):** son 3 tamamlanmış main run `success` — [`32133727296`](https://github.com/turkerurganci/Skinora/actions/runs/32133727296) · [`32133727298`](https://github.com/turkerurganci/Skinora/actions/runs/32133727298) · [`32057012508`](https://github.com/turkerurganci/Skinora/actions/runs/32057012508). **Dış varsayım:** yeni yok — kullanılan kontrol ucu (`POST /__e2e/steam/inventory`) ve `AK47_REDLINE` şablonu bu görevin yapım turunda canlı HTTP ile doğrulanmıştı; katalog `assetId`'si (`11111111001`) `seed.itemAssetId` ile birebir.
