# T137 — sidecar-fake sürülebilir envanter

**Faz:** F7 (P7, plan gereği P5 ile paralel) | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-18

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
| Unit (sidecar-fake) | ✓ **38/38 passed** | `npm test` → `ids 4 · hmac 3 · inventoryStore 31`, 3 dosya. Taban 12'ydi (`tradeControl` 5 testi emeklilikle düştü, 31 yeni test geldi) |
| Build (sidecar-fake) | ✓ | `npx tsc --noEmit` exit 0 · `npm run build` exit 0 |
| Lint/format (sidecar-fake) | ✓ | `npm run lint` 0 · `npm run format:check` "All matched files use Prettier code style!" |
| Typecheck/lint (e2e) | ✓ | `npx tsc --noEmit` exit 0 · `npm run lint` 0 |
| Format (e2e) | ✓ | Lokal `format:check` 14/14 dosyayı uyarıyor — **dokunulmayanlar dahil**, yani bilinen CRLF artefaktı. Değiştirdiğim 6 dosya LF'e normalize edilip tek tek kontrol edildi (`tr -d '\r' \| prettier --check --stdin-filepath`) → **6/6 LF-clean**; CI'ın LF lint'i yetkili |
| Firsthand HTTP smoke | ✓ | Fake 5111/5222'de koşturuldu; seed / trade / rotasyon / 422 / 503 / trade-hold / 400 doğrulama / 404 emeklilik / reset uçtan uca doğrulandı |
| Backend | — | **Sıfır backend değişikliği** (`git diff main...HEAD` yalnız `sidecar-fake/`, `e2e/`, `docker-compose.e2e.yml`, `Docs/`, `.claude/`) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok (backend'e dokunulmadı).
- **Config/env değişikliği:** `FAKE_BOT_STEAM_ID` ve `FAKE_TRADE_ACCEPT_DELAY_MS` `docker-compose.e2e.yml`'den ve `config.ts`'den **kaldırıldı** — yalnız e2e stack'i etkiler, üretim yapılandırması değil. Yeni env değişkeni yok.
- **Docker değişikliği:** Yok (yalnız fake servisin iki env satırı).

## Commit & PR

- Branch: `task/T137-fake-drivable-inventory`
- Commit: `050620c` (kod) · `c0412f6` (rapor/status/memory) · CI açığı düzeltmesi (D5)
- PR: [#246](https://github.com/turkerurganci/Skinora/pull/246)
- CI: `050620c` run [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605) `success` (ama `3b. JS test` **skipped** — D5 bulgusu) · `c0412f6` run [`32142550427`](https://github.com/turkerurganci/Skinora/actions/runs/32142550427) `success` (docs-only) · D5 sonrası dal HEAD run'ı: aşağıya işlenecek

## Yapım İçinde Bulunan CI Açığı (D5 — proje sahibi onaylı düzeltme)

**Bulgu:** `3b. JS test (vitest)` job'ı `needs: [changes, build]` diyordu; `2. Build` ise `if: needs.changes.outputs.code == 'true'` ile koşuyor ve `code` filtresi `backend / frontend / sidecar-steam / sidecar-blockchain / .github/workflows` — **`sidecar-fake` yok**. GitHub, atlanan bir `needs` bağımlılığının ardındaki job'ı da atladığı için, yalnız `sidecar-fake/**` değişen bir PR'da `build` atlanıyor ve JS test job'ı da atlanıyordu — **job'ın kendi `if`'i `sidecar-fake == 'true'`u açıkça saydığı hâlde.** Yani fake'in unit testleri CI'da **hiç** koşamıyordu.

**Kanıt:** ilk dal run'ı [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605) (`050620c`) — `2. Build` **skipped**, `3b. JS test (vitest)` **skipped**, CI Gate yine `success`. Bu turda yazılan 38 test o run'da hiç çalışmadı; yeşil CI onları kanıtlamıyordu.

**Düzeltme (proje sahibi seçimi):** `frontend-test` job'ının `needs`'i `[changes, build]` → `[changes]`. Job build çıktısı tüketmiyor (her adım kendi `npm ci`'sini yapıyor), yani kenar bir bağımlılık değil sıralamaydı. Bloke edicilik değişmedi — `ci-gate.needs` listesi ve `if: always()` + `contains(needs.*.result, 'failure')` mantığı aynı. Alternatif (`code` filtresine `sidecar-fake/**` eklemek) reddedildi: fake-only bir PR'da backend+frontend build'ini de tetiklerdi.

**Kalıcı ders:** bir job'ın kendi `if`'inde bir filtreyi saymış olması, o filtre gerçekleştiğinde job'ın koşacağı anlamına gelmez — `needs` zincirindeki **atlanan** bir halka koşulu sessizce geçersiz kılar. "Test suite'i CI'a bağlandı" ile "CI o suite'i koşuyor" farklı şeylerdir ve ilki ikincisini göstermez (T129 B3'ün CI düzlemindeki ikizi: orada formülün tetikleyicisi eksikti, burada testin tetikleyicisi).

## Known Limitations / Follow-up

1. **Advisory e2e ölçümü düştü — beklenen ve proje sahibi onaylı (D1).** Sürülmemiş steamId artık **boş** okunduğu için ve hiçbir spec henüz envanter seed etmediği için, create çağrısı `ITEM_NOT_IN_INVENTORY` ile reddediliyor. **Ölçülen (run [`32142151605`](https://github.com/turkerurganci/Skinora/actions/runs/32142151605), 8/8 leg koştu):** 32 testin **4'ü pass / 28'i fail** — T137a tabanı 10/32'ydi.

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

   Kaybedilen 6 testin tamamı bir transaction **yaratıyor**; düşme imzası tek ve aynı: `create failed: {"code":"ITEM_NOT_IN_INVENTORY","message":"Item is not in the seller's Steam inventory."}` (happy-path leg logu). Yani kayıp yeni bir kırılma değil, **seed sorumluluğunun görünür hâle gelmesi**. Ayakta kalan 4 test admin-flows'un envantere dokunmayan bölümü. Zincir T138'e kadar zaten kırmızıydı (T137a: "bu görev hiçbir leg'i yeşile çevirmiyor... yeşil beklentisi T137 + T135 + T138 zincirinin sonunda doğar"), legler `continue-on-error` + `ci-gate.needs` dışında olduğu için **CI Gate etkilenmez**. **T138 için somut sonuç:** her spec artık satıcı envanterini `api.setFakeInventory(...)` ile seed etmek zorunda — bu, T138'in yeniden yazım listesine eklenmesi gereken mekanik bir ön adımdır.
2. **Emekli lever'ların senaryoları T138'e kaldı.** `timeout.spec.ts`'in iki testi (satıcı trade-offer timeout / delivery timeout) ve `downtime.spec.ts`'in Steam-outage testi custody durumlarına dayanıyordu; lever'ları kaldırıldı ve yerlerine P2P karşılıklarını **adıyla** söyleyen notlar bırakıldı (confirm-ready deadline'ı, "satıcı hiç trade etmez", ACCEPTED'da bekleyen Steam-bound durum). Yeniden yazım T138'in kabul kriterinde.
3. **Trade lock / cooldown modellenmedi** — bilinçli. Hiçbir tüketici lock durumunu okumuyor (`DeliveryVerificationService` neden okumaması gerektiğini uzun uzun belgeliyor: anonim okumada süre bilgisi yok), ölçülmemiş bir sinyali simüle etmek testi ona assert etmeye davet ederdi.
4. **`.claude/CONTEXT.md` dosya haritasında `sidecar-fake/` ve `e2e/` hiç yok** — T107'den beri var olan bir boşluk, T137 kusuru değil. Yapısal doküman değişikliği proje sahibi onayı gerektirdiği için (GUARDRAILS §3) bu turda açılmadı; ayrı bir `chore:` turu adayı.
5. **`C:/projects/Escrow-T137a` worktree'si duruyor** — T137a merge edildiği için gereksiz; proje sahibi bu turda yalnız `Escrow-T137`'nin kaldırılmasını onayladı.

## Notlar

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
