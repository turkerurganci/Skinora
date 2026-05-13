# T65 — Steam Sidecar Trade Offer Gönderme

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-13

---

## Yapılan İşler

- `steam-tradeoffer-manager@^2.13.0` BotSession'a wire edildi: `webSession` event'inde cookies hem `steamcommunity` hem `TradeOfferManager`'a aynı anda set ediliyor. `pollInterval: -1` ile T66'ya kadar manager'ın kendi polling'i kapalı (T66 `10_000`'e çekecek); `cancelTime: 15min` Steam default "never"a karşı emniyet kuşağı.
- `BotSession.getTradeManager()` accessor: yalnızca `READY` state'te non-null döner, terminal/initializing state'lerde `null` — caller'ı zorla null-check'e iter.
- `BotSession.acceptTradeConfirmation(offerId)`: `community.acceptConfirmationForObject(identitySecret, offerId, cb)` üzerinde Promise wrapper; inline auto-accept (T64'teki 20 saniyelik checker fallback olarak çalışmaya devam ediyor).
- Yeni `TradeOfferService` (sidecar-steam/src/trade/TradeOfferService.ts) 3 yön ile çalışıyor:
  - `SELLER_TO_BOT`: bot **alıcı** → `offer.addTheirItem(items)` → seller accept eder → bot envantere düşer. MA confirmation **yok** (bot envanterden item göndermiyor).
  - `BOT_TO_BUYER`: bot **gönderici** → `offer.addMyItem(items)` → buyer accept eder. MA confirmation **var** (offer state 9 dönerse `acceptTradeConfirmation` çağrılır).
  - `BOT_TO_SELLER_REFUND`: aynı şekilde `addMyItem` + MA confirmation.
- 08 §2.7 retry: `[5_000, 15_000, 45_000]` ms backoff, **yalnızca transient error**'larda. Transient set: EResult `{10, 16, 41, 84}` (NoConnection / Timeout / RemoteCallFailed / RateLimitExceeded) + Node network code'ları `{ECONNRESET, ETIMEDOUT, ECONNREFUSED, EAI_AGAIN, ENETUNREACH}`. Diğer her şey permanent → tek deneme + `trade_offer.failed` webhook.
- Webhook callback'leri 05 §3.4'e uygun HMAC-SHA256 imzalı (`WebhookClient.sendCallback`) — varsayılan endpoint `/api/v1/sidecar/steam/trade-offer-events`. İki yeni event:
  - `trade_offer.sent` — payload `{transactionId, direction, partnerSteamId, botSteamId, botAccountName, offerId, status: 'pending'|'sent'|'confirmed', attempts}`
  - `trade_offer.failed` — payload `{transactionId, direction, partnerSteamId, botAccountName?, reason, eresult?, retryable, attempts}`
  - Backend handler T66/T68 görevi — şimdilik 404 graceful log'a iniyor (T64 ile aynı pattern).
- HTTP route: `POST /api/trade-offers/send` 501 stub'u kaldırıldı, `TradeOfferService.sendOffer` handler'ı bağlandı. Schema validation gönderiminden önce: `transactionId`/`direction`/`partnerSteamId`/`items[]` zorunlu, `direction` whitelist, her `item` için `assetid`/`appid`/`contextid` tip kontrolü. `internalKeyAuth` middleware'i mevcut.
- Response sözleşmesi: `{status: 'sent'|'pending'|'confirmed'|'failed', offerId?, reason?, retryable?, attempts}`. HTTP: success → 200, failure → 502 (transient → retryable=true), schema → 400, service yok → 503.
- `buildRouter` backward-compat: `buildRouter(botManager)` (T64 signature) hâlâ destekleniyor, `buildRouter({botManager, tradeOfferService})` yeni form. `index.ts` yeni forma geçti.
- 23 yeni test (TradeOfferService 13 + BotSession T65 alt-suite 4 + route 6) Vitest ile pass; toplam sidecar test sayısı 42 → 65.
- Lokal `@types/steam-tradeoffer-manager` yok (DefinitelyTyped'da yayınlanmamış) — minimal ambient declaration `src/types/steam-tradeoffer-manager.d.ts`'e yazıldı (yalnızca kullandığımız API: `createOffer`, `addMyItem`/`addTheirItem`, `setMessage`, `send`, `cancel`, `setCookies`, `ETradeOfferState`, error tipi).

## Etkilenen Modüller / Dosyalar

### Oluşturulan
- `sidecar-steam/src/trade/types.ts` — `TradeDirection`, `SendTradeOfferRequest`, `SendTradeOfferResponse`, retry/transient constant'ları, `requiresMobileConfirmation()` helper
- `sidecar-steam/src/trade/TradeOfferService.ts` — ana servis (3 direction + retry + MA confirm + webhook)
- `sidecar-steam/src/trade/TradeOfferService.test.ts` — 13 contract testi
- `sidecar-steam/src/api/routes.test.ts` — 6 HTTP integration testi (express in-process)
- `sidecar-steam/src/types/steam-tradeoffer-manager.d.ts` — minimal ambient module declaration

### Güncellenen
- `sidecar-steam/src/bot/BotSession.ts` — `TradeOfferManager` mount, `getTradeManager()`, `acceptTradeConfirmation()`, `webSession`'da manager.setCookies
- `sidecar-steam/src/bot/BotSession.test.ts` — `FakeTradeOfferManager` fake + T65 alt-suite (4 yeni test)
- `sidecar-steam/src/api/routes.ts` — `RouterDeps` parametresi, `/trade-offers/send` 501→handler, request schema validator (`parseSendRequest`)
- `sidecar-steam/src/webhook/WebhookPayloads.ts` — `TradeOfferEventName`, `TradeOfferSentData`, `TradeOfferFailedData`
- `sidecar-steam/src/index.ts` — `TradeOfferService` instantiation + `buildRouter({...})` yeni form
- `sidecar-steam/src/trade/TradeOfferService.ts` ↔ `sidecar-steam/src/trade/InventoryService.ts` (sonuncu T67 stub, dokunulmadı)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Trade offer gönderme (satıcıya item emanet, alıcıya item teslim, satıcıya iade) | ✓ | 3 yön `TradeDirection` enum + `TradeOfferService.attemptSend()` direction-aware addItem routing. Test: `TradeOfferService.test.ts` "SELLER_TO_BOT uses addTheirItem...", "BOT_TO_BUYER uses addMyItem...", "BOT_TO_SELLER_REFUND uses addMyItem and auto-confirms" |
| 2 | steam-tradeoffer-manager ile offer oluşturma ve gönderme | ✓ | `BotSession`'da `new TradeOfferManager({steam, community, language, pollInterval: -1, cancelTime: 15min})` mount; `tradeManager.createOffer(partnerSteamId)` + `offer.send(cb)` akışı. Test: BotSession.test.ts "webSession event refreshes cookies on the trade manager", "getTradeManager returns null when not READY, manager when READY" |
| 3 | Mobile confirmation otomatik onayı | ✓ | `TradeOfferService.maybeConfirm()` → status='pending' && `requiresMobileConfirmation(direction)` → `bot.acceptTradeConfirmation(offerId)` → `community.acceptConfirmationForObject(identitySecret, offerId, cb)`. Test: TradeOfferService.test.ts "BOT_TO_BUYER ... auto-confirms when status is pending", "still emits sent webhook even if mobile confirmation fails (20s checker is fallback)"; BotSession.test.ts "acceptTradeConfirmation calls community.acceptConfirmationForObject with identity_secret" |
| 4 | Retry: exponential backoff (5s, 15s, 45s), timeout süresi içinde | ✓ | `TRADE_OFFER_BACKOFF_MS = [5_000, 15_000, 45_000]`; `isTransientError()` ile transient/permanent ayrımı; permanent → tek deneme. Test: "retries transient eresult (84 RateLimitExceeded) with 08 §2.7 backoff" (3 deneme, 5s+15s sleep), "retries transient network code (ECONNRESET)", "does not retry permanent eresult (15 AccessDenied)", "marks failed with retryable=true when transient retries exhaust" |
| 5 | Counter offer handling: desteklenmiyor, orijinal offer iptal | ~ Kısmi → T66 devir | T65 send-only; counter offer Steam tarafında **status değişikliği** (state 4 Countered) — `steam-tradeoffer-manager`'ın `sentOfferChanged` event'i tetiklenir, ki bu **polling** sonucudur (08 §2.4 polling stratejisi). T66 "trade offer durum izleme" task'ı `pollInterval: 10_000`'i açıp `sentOfferChanged` handler'ında `offer.cancel(...)` + webhook publish edecek. Proje sahibi onayı (2026-05-13) ile **Known Limitation K1** olarak devir. |
| 6 | Webhook callback: trade offer durumu backend'e bildirilir | ✓ | `trade_offer.sent` ve `trade_offer.failed` event'leri `WebhookClient.sendCallback` üzerinden HMAC-imzalı. Endpoint default `/api/v1/sidecar/steam/trade-offer-events`. Test: tüm `TradeOfferService.test.ts` test'leri `recordedWebhook` mock üzerinden event name + payload shape doğruluyor. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar build | ✓ | `npm run build` (`tsc`) — 0 error |
| Sidecar unit/integration (Vitest) | ✓ 65/65 PASS | `npm test` — TradeOfferService 13 + BotSession 17 (T64 13 + T65 4) + BotManager 10 + BotHealthCheck 6 + BotConfig 9 + routes 6 + WebhookPayloads 4 |
| Sidecar lint | ✓ | `npm run lint` (ESLint) — 0 error |
| Sidecar tsc --noEmit | ✓ | `npx tsc --noEmit` — 0 error (CI lint job karşılığı) |
| Backend Release build | ✓ 0W/0E | `dotnet build Skinora.sln --configuration Release` — regresyon yok |
| Sidecar format:check | ⚠ K2 carry | Prettier 25 dosyada drift raporluyor — K1 (T14 carry) + T65 yeni dosyalar formatlandı ama önceki drift sürüyor. CI `lint` job'u prettier enforce etmiyor (sadece `tsc --noEmit`), CI gate engellenmiyor. Ayrı `chore/prettier-sweep` PR'a devir. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS bağımsız validator (2026-05-13) |
| Bulgu sayısı | 0 S-bulgu, 1 ~ Kısmi forward-defer (AC5 → T66, K1) |
| Düzeltme gerekli mi | Hayır |

**Validator notu (bağımsız chat):** 6/6 kabul ✓ (1 ~ Kısmi T66 yapısal devir — counter offer aksiyon mekaniği polling-driven, plan tanımı T65 send + T66 monitör kapsam ayrımına uyumlu) + 1/1 doğrulama listesi ~ kısmi (08 §2.4 send-side eksiksiz, monitör T66). Test: 65/65 sidecar Vitest + 780/780 backend unit (Shared 185 + Auth 57 + Notifications 49 + Transactions 333 + Platform 102 + Fraud 14 + API 15 + Realtime 25) + backend Release 0W/0E (regresyon yok). Adım -1 working tree clean, Adım 0 main CI 3/3 success (`25780470892/880`, `25756831861`), Adım 0b repo memory T65 satırı mevcut. Task branch CI son run `25822603610` HEAD `df703fa` 10/10 ✓ + `25821363758` HEAD `fc95036` 10/10 ✓. Yapım raporu uyumu tam (0 uyuşmazlık). Güvenlik: secret sızıntısı yok, `internalKeyAuth` mevcut, schema validation iki katmanlı, yeni bağımsız dep yok (T14 mirası `steam-tradeoffer-manager@^2.13.0`).

## Altyapı Değişiklikleri

- **Migration:** Yok (DB değişikliği yok)
- **Config/env değişikliği:** Yok (BotConfig şeması T64'te belirlendi, T65 yeni alan eklemiyor; webhook secret + internal key zaten mevcut)
- **Docker değişikliği:** Yok
- **Yeni paket:** Yok — `steam-tradeoffer-manager@^2.13.0` T14'te zaten eklenmişti, T65 ilk kez kullanıyor

## Commit & PR

- Branch: `task/T65-steam-trade-offer-send`
- Commit: `fc95036`
- PR: [#105](https://github.com/turkerurganci/Skinora/pull/105)
- CI: ✓ PASS — [run 25821363758](https://github.com/turkerurganci/Skinora/actions/runs/25821363758) 9/9 job success (1 skipped Guard, PR akışı için beklenen)

## Known Limitations / Follow-up

- **K1 — Counter offer iptal handler'ı T66 devir:** Plan kabul kriterindeki "Counter offer handling: desteklenmiyor, orijinal offer iptal" maddesi T66 polling task'ında implement edilecek. Steam tarafında counter offer **status değişikliği** (state 4) → polling event'i → handler `offer.cancel()` + webhook publish. T65 send-only scope'ta kaldı (proje sahibi onayı 2026-05-13).
- **K2 — Prettier drift carry-over:** Sidecar-steam'de toplam 25 dosya prettier --check warning veriyor. T14'ten miras (K1 T64 raporunda da listeli), T65 yeni 4 dosyayı formatladı; geri kalan 21 dosya ayrı `chore/prettier-sweep` PR'a devir. CI gate engellenmiyor (sidecar lint job sadece `tsc --noEmit`).
- **K3 — Backend trade offer event handler T66/T68 devir:** `trade_offer.sent`/`trade_offer.failed` event'leri sidecar'dan gönderiliyor ama backend tarafında handler henüz yok — şu an 404 graceful log'a iniyor (T64'te `bot.session_failed` ile aynı pattern). T66 polling event'leriyle birlikte T68'de backend endpoint'leri açılacak.
- **K4 — `@types/steam-tradeoffer-manager` upstream eksikliği:** DefinitelyTyped'da yayınlanmamış. Lokal minimal `.d.ts` deklare edildi; ileride T66'da polling event'leri (`sentOfferChanged`, `pollData`) ve `EOfferFilter` enum'u eklenecek. Upstream paketinin maintainership riski 08 §2.8'de zaten kayıtlı.
- **K5 — `steam-totp.getConfirmationKey` log truncation:** `acceptTradeConfirmation`'da debug log'una confirmation key'in **ilk 6 karakteri** + ellipsis yazılıyor (audit trail için yeterli, full secret leak değil). Loki redaction policy bunu zaten yakalar ama defansif log hijyeni.

## Notlar

- **Working tree:** Adım -1 temiz (T64 merge sonrası fast-forward'lı main).
- **Main CI startup check (Adım 0):** Son 3 main run hepsi `success` — `25780470892`, `25780470880` (T64 #104 merge), `25756831861` (F3 Gate Check). HARD STOP yok.
- **Dış varsayımlar (Adım 4):**
  - `steam-tradeoffer-manager@^2.13.0` — ✓ T14'te `package.json`'da pinned, T64 hiç import etmedi, T65 ilk kullanım.
  - `steamcommunity.acceptConfirmationForObject` v3.x API — ✓ T64'te `startConfirmationChecker(20s, identitySecret)` aynı paketin yerleşik metodunu kullanıyor.
  - `steam-totp.getConfirmationKey` v2.x — ✓ Aynı paket, `generateAuthCode` T64'te kullanıldı.
  - `@types/steam-tradeoffer-manager` DT'de yok — ⚠ Lokal `.d.ts` ile çözüldü (K4).
  - BotSession internals (cookies, community) trade manager ile paylaşılabilir — ✓ T64 implementation'ı `community.setCookies(cookies)` zaten `webSession` event'inde yapıyor; manager için aynı cookie set'i `tradeManager.setCookies(cookies, cb)` ile genişletildi.
- **Doğrulama önerisi:** Yapım raporu görülmeden bağımsız validate chat açılır.
