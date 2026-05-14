# T66 — Steam Sidecar Trade Offer Durum İzleme

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-14

---

## Yapılan İşler

- `BotSession.tradeManager` artık `pollInterval: 10_000` ile mount ediliyor (T64'te `-1` ile kapalıydı; T66'da 08 §2.4 polling stratejisi açıldı). `cancelTime: 15min` T65'ten beri korunuyor.
- `BotSession.bindTradeOfferEvents(handler)` köprüsü — `sentOfferChanged(offer, oldState)` ve `pollFailure(err)` event'lerini `TradeOfferEventHandler` interface'ine forward eder. Handler exception'larını sessizce yakalar (EventEmitter loop'u kırılmaz). Bu metod sayesinde `TradeOfferMonitor`, `BotSession`'ın `private readonly tradeManager` alanına erişmek zorunda kalmıyor — encapsulation korundu.
- Yeni `TradeOfferMonitor` modülü (`sidecar-steam/src/trade/TradeOfferMonitor.ts`):
  - `start()` → `botManager.allSessions()` üzerinde iterate eder, her session için `bindTradeOfferEvents` çağırır. Idempotent (ikinci `start()` no-op); `attached` Set bot accountName'leri tutuyor.
  - `attachToSession(session)` → dinamik bot eklenmesi durumunda manuel attach noktası (T69 capacity scaling için forward hook). Aynı accountName için tekrar çağrılırsa skip.
  - `handleSentOfferChanged(session, offer, oldState)` → `TRADE_OFFER_STATE_EVENT_MAP` üzerinden `offer.state` numeric kodunu webhook event ismine çevirir; mapping dışı state'ler (1/2/6/9/10/11) debug log'a düşer.
  - Idempotency: `Map<offerId, lastNewState>` — aynı offer için aynı state ikinci kez duyrulursa skip (built-in polling'in terminal state'i tekrar raporlama riskine karşı tampon).
  - `pollFailure` → log + sessiz (08 §2.7'ye uygun: built-in poller kendi retry'ını yapar; sidecar tarafında ek aksiyon yok).
- 08 §2.4 → webhook event mapping (yeni `TRADE_OFFER_STATE_EVENT_MAP` sabiti):
  - `Accepted (3)` → `trade_offer.accepted`
  - `Countered (4)` → `trade_offer.countered` (08 §2.4: "Counter offer desteklenmiyor, orijinal offer iptal sayılır" — backend iptal akışı tetikler)
  - `Expired (5)` → `trade_offer.expired`
  - `Declined (7)` → `trade_offer.declined`
  - `InvalidItems (8)` → `trade_offer.invalid_items`
- 6 state mapping dışında (`Invalid` 1, `Active` 2, `Canceled` 6, `CreatedNeedsConfirmation` 9, `CanceledBySecondFactor` 10, `InEscrow` 11) — kasıtlı skip, kod yorumunda + test'te gerekçeli (T65 send event'leri 2/9 zaten karşılıyor; 6 sidecar-initiated; 10 BotSession seviyesi; 11 trade hold gelecek task).
- Yeni `TradeOfferStatusChangedData` payload sözleşmesi (`sidecar-steam/src/webhook/WebhookPayloads.ts`): `{offerId, partnerSteamId, botSteamId?, botAccountName, newState, oldState}`. `transactionId` sidecar tarafında bilinmiyor — backend `offerId → transactionId` mapping'ini T65'in `trade_offer.sent` event'inden DB'de tutuyor (T68 lookup yapacak). `partnerSteamId` cross-routing bug sanity check'i için dahil.
- `index.ts` wiring: `BotManager.initialize()` sonrası `TradeOfferMonitor.start()` çağrılıyor (pool dolu, TradeOfferManager instance'ları hazır; `webSession` event'i polling'i tetikler).
- Steam-tradeoffer-manager `.d.ts` deklarasyonu genişletildi: `on('sentOfferChanged', ...)`, `on('pollFailure', ...)`, `on('pollSuccess', ...)` overload'ları + generic `on(string, listener)` fallback.
- `routes.ts` `/trade-offers/:offerId/status` placeholder mesajı güncellendi — T66'nın **push-based** olduğu, pull endpoint'inin ops için reserved olduğu netleştirildi (501 status korundu).
- 38 yeni test (TradeOfferMonitor 20 + WebhookPayloads contract 15 + BotSession bridge 3) Vitest ile pass; toplam sidecar test sayısı 65 → 103.

## Etkilenen Modüller / Dosyalar

### Oluşturulan
- `sidecar-steam/src/trade/TradeOfferMonitor.ts` — Steam → backend status monitor
- `sidecar-steam/src/trade/TradeOfferMonitor.test.ts` — 20 monitor unit/integration testi

### Güncellenen
- `sidecar-steam/src/bot/BotSession.ts` — `pollInterval: 10_000` + yeni `bindTradeOfferEvents()` metodu + `TradeOfferEventHandler` import
- `sidecar-steam/src/bot/BotSession.test.ts` — T66 bridge alt-suite (3 yeni test)
- `sidecar-steam/src/trade/types.ts` — `TradeOfferEventHandler` interface
- `sidecar-steam/src/webhook/WebhookPayloads.ts` — `TradeOfferEventName` 7 değer (yeni 5), `TradeOfferStatusChangedData` payload, `TRADE_OFFER_STATE_EVENT_MAP` runtime sabiti
- `sidecar-steam/src/webhook/WebhookPayloads.test.ts` — T66 contract testleri (15 yeni)
- `sidecar-steam/src/types/steam-tradeoffer-manager.d.ts` — `on()` event overload'ları (sentOfferChanged / pollFailure / pollSuccess + generic fallback)
- `sidecar-steam/src/index.ts` — `TradeOfferMonitor` instantiation + `start()` çağrısı
- `sidecar-steam/src/api/routes.ts` — placeholder 501 endpoint mesajı netleştirildi (push-based mekanizma açıklaması)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 10sn aralıkla polling (steam-tradeoffer-manager built-in) | ✓ | `BotSession.ts` TradeOfferManager ctor `pollInterval: 10_000` (T64'te `-1`'di). Library built-in polling job'u her 10sn'de bir `sentOfferChanged` event'i emit eder. |
| 2 | Durum değişikliğinde webhook callback: Accepted, Declined, Expired, Countered, InvalidItems | ✓ | `TRADE_OFFER_STATE_EVENT_MAP` 5 entry (3→accepted, 7→declined, 5→expired, 4→countered, 8→invalid_items). `TradeOfferMonitor.handleSentOfferChanged()` mapping'i uygular ve `WebhookClient.sendCallback` ile HMAC-imzalı publish eder. Test: `TradeOfferMonitor.test.ts` "state 3 Accepted → trade_offer.accepted" (+ 4 paralel state testi), `WebhookPayloads.test.ts` "TRADE_OFFER_STATE_EVENT_MAP maps the 08 §2.4 status codes". |
| 3 | InvalidItems → kullanıcıya bilgi, işlem iptal | ~ Kısmi → T68 devir | Sidecar tarafı: `trade_offer.invalid_items` event'i yayınlanıyor (state 8 mapping ✓). "Kullanıcıya bilgi + işlem iptal" backend state machine'in işi — T68 webhook handler + 03 §2.3/5 iptal akışı tetikleyecek. K2 olarak forward-defer. |
| 4 | FAILED durumu: retry geçerli | ✓ | Polling failure (network/HTTP/auth) `pollFailure` event'ine düşer — built-in poller kendi retry cycle'ını sürdürür (08 §2.7). `TradeOfferMonitor.onPollFailure` handler log + sessiz (sidecar tarafında ek aksiyon yok); 08 §2.7 retry semantics zaten T65 send-side'da uygulandı (transient eresult'larda 5s/15s/45s backoff). Test: `TradeOfferMonitor.test.ts` "logs but does not propagate or emit a webhook (08 §2.7 — built-in poller retries)". |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar build | ✓ | `npm run build` (`tsc`) — 0 error |
| Sidecar unit/integration (Vitest) | ✓ 103/103 PASS | `npm test` — TradeOfferService 13 + BotSession 20 (T64 13 + T65 4 + T66 3) + TradeOfferMonitor 20 + BotManager 10 + routes 6 + WebhookPayloads 19 (4 + 15 T66) + BotHealthCheck 6 + BotConfig 9 |
| Sidecar lint | ✓ | `npm run lint` (ESLint) — 0 error |
| Backend Release build | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Release` — regresyon yok (T66 sidecar-only) |
| Sidecar format:check | ⚠ K3 carry | Prettier 28 dosyada drift; T66 yeni dosyalardan `TradeOfferMonitor.ts` formatlandı, `TradeOfferMonitor.test.ts` zaten temiz. Önceki K1 (T14) + K2 (T65) drift'i hâlâ devam ediyor — ayrı `chore/prettier-sweep` PR'a devir. CI gate engellenmiyor. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator, 2026-05-14) |
| Bulgu sayısı | 0 S-bulgu (S1/S2/S3 yok); 1 AK ~ Kısmi (AK3 InvalidItems kullanıcı bildirimi T68 devir, sidecar wire ✓) |
| Düzeltme gerekli mi | Hayır |

**Adım -1 working tree:** temiz (commit `15ac139` sonrası clean).
**Adım 0 main CI startup:** son 3 run `success` — `25824801565` + `25824801517` (T65 #105 squash), `25780470892` (T64 #104). HARD STOP yok.
**Adım 0b repo memory:** `.claude/memory/MEMORY.md` T66 satırı mevcut (T64 sonrası "Next: T66 doğrulama" satırı + T66 detay satırları).
**Adım 7a task branch CI:** `25845938819` 10/10 job `success` (Lint + Build + Unit + Integration + Contract + Migration dry-run + Docker sidecar-steam + CI Gate).
**Verdict gerekçesi:** AK1/AK2/AK4 ✓ (pollInterval 10_000, 5 state mapping → 7 webhook event, pollFailure log+swallow built-in retry); AK3 ~ Kısmi (sidecar wire ✓, kullanıcı bildirimi backend T68 devir K2); doğrulama kontrol listesi 2/2 ✓ (08 §2.4 5 task-listed state + 6 kasıtlı unmapped gerekçeli, 08 §2.7 InvalidItems Hayır-retry + session expire auto re-login + pollFailure built-in retry). Mini güvenlik: 4/4 temiz (yeni secret yok, yeni endpoint yok, payload trusted-source Steam, yeni dep yok).

## Altyapı Değişiklikleri

- **Migration:** Yok (DB değişikliği yok — sidecar runtime/in-memory state)
- **Config/env değişikliği:** Yok (BotConfig şeması T64'ten beri stabil; webhook secret + internal key zaten mevcut)
- **Docker değişikliği:** Yok
- **Yeni paket:** Yok — `steam-tradeoffer-manager@^2.13.0` T14'ten beri mevcut, T66 yalnızca event API'sini kullanıyor

## Commit & PR

- Branch: `task/T66-steam-trade-offer-monitor`
- Commit: `15ac139` — T66: Steam Sidecar — trade offer durum izleme
- PR: [#106](https://github.com/turkerurganci/Skinora/pull/106)
- CI: ✓ PASS (run [25845938819](https://github.com/turkerurganci/Skinora/actions/runs/25845938819) 10/10 job)

## Known Limitations / Follow-up

- **K1 — Backend trade offer status handler T68 devir:** `trade_offer.{accepted,declined,expired,countered,invalid_items}` event'leri sidecar'dan publish ediliyor ama backend tarafında henüz handler yok — şu an 404 graceful log'a iniyor (T64/T65 ile aynı pattern). T68 "webhook callback ve backend entegrasyonu" task'ı 5 event için endpoint açıp `Transaction` state machine'i tetikleyecek. Idempotency anahtarı `offerId + newState` kombinasyonu olarak backend tarafında ProcessedNonce / dedup tablosu üzerinden kurulur.
- **K2 — InvalidItems "kullanıcıya bilgi + işlem iptal" backend devir:** Sidecar yalnızca event'i yayınlıyor; kullanıcı bildirimi (Notification entity) ve işlem iptali (Transaction.Status → CANCELLED akışı, 03 §2.3/5) T68 + ilgili Realtime/Notifications modüllerinin işi.
- **K3 — Prettier drift carry-over:** Sidecar-steam'de 28 dosya prettier --check warning veriyor (T14 K1 + T65 K2 mirası). T66 yeni `TradeOfferMonitor.ts` formatlandı; `TradeOfferMonitor.test.ts` zaten temizdi. Ayrı `chore/prettier-sweep` PR'a devir. CI gate engellenmiyor (sidecar lint job ESLint + `tsc --noEmit` kullanıyor, prettier enforce etmiyor).
- **K4 — Dinamik bot pool (T69 capacity scaling) devir:** `TradeOfferMonitor.start()` `botManager.allSessions()` üzerinden iterate ediyor; pool yalnızca bootstrap'ta sabit. T69 capacity-based scaling bot'ları runtime'da ekler/çıkarırsa `monitor.attachToSession(newBot)` ile manuel attach gerekecek. `attachToSession()` idempotent yazıldı, çağrılması güvenli.
- **K5 — Sidecar restart sonrası "missed during downtime" status değişiklikleri:** Sidecar kapalıyken Steam tarafında state değişen offer'lar için event tekrar emit edilmez (steam-tradeoffer-manager polling kontekst-içi). Backend tarafında bir reconciliation job'u (T76 Blockchain mirror) veya sidecar restart'ta `tradeManager.pollData` snapshot'ı üzerinden diff ileride gerekirse açılır. T68 idempotency mekanizması bu durumda da çift emit'i absorbe eder.
- **K6 — Cross-restart idempotency in-memory:** `TradeOfferMonitor.handledTransitions` Map sidecar restart'ta sıfırlanır. Steam tarafı state durable, yani aynı offer için aynı state tekrar polling ile gelirse yeni bir webhook tetiklenebilir. Backend T68'in `offerId + newState` dedup ile bunu absorbe etmesi planlandı. Sidecar tarafında Redis-backed persistence şu an scope dışı.

## Notlar

- **Working tree:** Adım -1 temiz (T65 merge sonrası fast-forward'lı main: `df45d05`).
- **Main CI startup check (Adım 0):** Son 3 main run hepsi `success` — `25824801565` + `25824801517` (T65 #105 merge), `25780470892` (T64 #104 merge). HARD STOP yok.
- **Repo memory check:** `MEMORY.md` T65 satırı mevcut (`fc95036` + PR #105 + 1-cümle özet) — drift yok. T66 satırı bu PR'da eklenecek.
- **Dış varsayımlar (Adım 4):**
  - `steam-tradeoffer-manager@^2.13.0` `sentOfferChanged(offer, oldState)` event'i — ✓ Kütüphane v2.x event sözleşmesi stabil; T65'te paket zaten kuruldu, ambient `.d.ts` T66'da event overload'ları için genişletildi.
  - `pollInterval` numeric (>0) verildiğinde otomatik `doPoll()` tetikler — ✓ TradeOfferManagerOptions doğrudan Steam'in built-in poll mekanizması; 08 §2.4 referansı.
  - `ETradeOfferState` numeric kodları (2/3/4/5/7/8) Steam tarafında stabil — ✓ 08 §2.4 tablosu + mevcut `.d.ts` deklarasyonu uyumlu.
  - Backend `/api/v1/sidecar/steam/trade-offer-events` handler henüz yok — ✓ T68 görevi, 404 beklenir; webhook publisher T65'te kurulan `.catch` log pattern'ı ile graceful (`Backend handler is wired in T68`).
- **Mini güvenlik kontrolü:**
  - Secret sızıntısı: Yok — T66 hiçbir yeni secret kullanmıyor, `identitySecret`/`sharedSecret` BotSession seviyesinde T64'ten beri encapsulated.
  - Auth/authorization: Yeni HTTP endpoint yok; webhook callback HMAC-SHA256 imzalı (T65'ten miras `WebhookClient.sendCallback`).
  - Input validation: Yeni kullanıcı girdisi yok; webhook payload data Steam tarafından gelen `TradeOffer` objesi (trusted source), backend HMAC ile doğrular.
  - Yeni dış bağımlılık: Yok.
- **Adım 0b — Bundled-PR check:** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` çıktısı = yalnızca `T66` (commit aşamasında doğrulanacak).
- **Doğrulama önerisi:** Yapım raporu görülmeden bağımsız validate chat açılır.
