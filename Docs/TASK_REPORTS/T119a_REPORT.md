# T119a — `POST /transactions/:id/accept` — v3.0 alanları

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-10

---

## Yapılan İşler

T119a, T117 doğrulamasının açtığı **plan boşluğunu** kapatır: 07 §7.6 v3.0'da accept ucuna üç madde ekledi (zorunlu `steamTradeUrl`, format doğrulaması, alıcı MA kontrolü) ama F7 listesinde bu ucu üstlenen görev yoktu. T117 `BuyerTradeUrl` kolonunu ekledi, **yazan kod yoktu** — kolon her işlemde kalıcı NULL kalıyordu.

1. **`steamTradeUrl` zorunlu istek alanı oldu.** `AcceptTransactionRequest` positional record'una ikinci alan olarak eklendi — opsiyonel yapılmadı, çünkü opsiyonel alan mevcut çağıranları sessiz runtime 400'e düşürürdü; positional zorunluluk 21 çağrı yerini **derleme hatası** olarak yakaladı.

2. **Format + sahiplik doğrulaması.** Yeni parser yazılmadı; U17'nin üretimdeki `ITradeUrlParser`'ı (`Skinora.Users`) enjekte edildi — accept ucunun trade-URL sözleşmesi ile profil kaydının sözleşmesi artık **tanım olarak aynı**. Üstüne bir **sahiplik kapısı** eklendi (proje sahibi kararı, aşağıda).

3. **Alıcı MA kontrolü canlı probe ile.** `ITradeHoldChecker` (→ `GetTradeHoldDurations`, 08 §2.2) pipeline'ın **son** kapısı olarak eklendi. Kalıcı `User.MobileAuthenticatorVerified` bayrağı **kullanılmadı**: bayrak yalnız U17/A7 yollarında tazeleniyor, alıcı bunları hiç çalıştırmamış olabilir ve MA'sını sonradan kapatan alıcı bayrakla yakalanamaz.

4. **`BuyerTradeUrl` artık yazılıyor** — ham girdi değil, `Normalized` biçim (satıcının teslimat bağlantısı bu kolondan üretiliyor, 08 §2.2). Invariant `HasFieldsForAccepted` guard'ına da eklendi.

5. **Ön-doldurma kaynağı açıldı.** 07 §7.6 "istemci ön-doldurur" diyordu ama `GET /users/me` `steamTradeUrl` **döndürmüyordu** — yani ön-doldurulacak kaynak yoktu. Profil DTO'suna salt-okunur alan eklendi (yazma yolu hâlâ yalnız U17).

6. **Frontend + E2E harness aynı değişiklikte taşındı** (proje sahibi kararı) — zorunlu alan eklenip FE bırakılırsa alıcı akışı doğrudan 400 alırdı.

7. **`sidecar-fake` trade-hold defekti düzeltildi** — aşağıda "Keşifte çıkan defekt".

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `.../Lifecycle/TransactionLifecycleDtos.cs` | `AcceptTransactionRequest`'e `SteamTradeUrl`; `AcceptTransactionStatus`'a `InvalidTradeUrl`, `MobileAuthenticatorRequired`, `SteamUnavailable` |
| `.../Lifecycle/TransactionErrorCodes.cs` | `InvalidTradeUrl`, `SteamUnavailable` sabitleri (`MobileAuthenticatorRequired` zaten vardı — yeniden tanımlanmadı) |
| `.../Lifecycle/TransactionAcceptanceService.cs` | +2 bağımlılık (`ITradeUrlParser`, `ITradeHoldChecker`); Stage 4b (parse + sahiplik) ve Stage 5b (MA probe); Stage 6'da `BuyerTradeUrl` yazımı; `IsOwnedByBuyer` yardımcısı |
| `.../Lifecycle/ITransactionAcceptanceService.cs` | XML doc — iki yeni kapı |
| `.../Domain/StateMachine/TransactionStateMachine.cs` | `HasFieldsForAccepted` + `BuyerTradeUrl`; PermitIf mesajı |
| `Skinora.API/Controllers/TransactionsController.cs` | Accept switch: 400 `INVALID_TRADE_URL`, 403 `MOBILE_AUTHENTICATOR_REQUIRED`, 503 `STEAM_UNAVAILABLE` |
| `.../Users/Application/Profiles/UserProfileDtos.cs` + `UserProfileService.cs` | `GET /users/me` → `steamTradeUrl` (salt-okunur) |
| `frontend/.../detail/AcceptForm.tsx` | Zorunlu trade URL input'u (`accept-trade-url-input`), profil prefill'i, boş-alan guard'ı, 3 yeni hata kodu |
| `frontend/.../detail/StateActionPanel.tsx` · `(main)/transactions/[id]/page.tsx` · `(main)/invite/[token]/page.tsx` | `defaultSteamTradeUrl` prop zinciri (`profile.data?.steamTradeUrl`) |
| `frontend/src/lib/api/{transactions,users}.ts` | `AcceptTransactionRequest.steamTradeUrl`, `UserProfile.steamTradeUrl` |
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | 3 form metni + 3 hata kodu × 4 dil |
| `sidecar-fake/src/routes/steam.ts` | **Bugfix** — trade-hold `active: false` → `true` |
| `e2e/src/{api,db}.ts` | `seed.buyerTradeUrl`; `acceptTransaction` varsayılan parametre olarak gönderiyor (12 çağrı yeri dokunulmadı) |
| `Docs/07_API_DESIGN.md` | **v3.1** — §7.6 doğrulama sırası + sahiplik kuralı + 503; §5.1 `steamTradeUrl` |
| `Docs/06_DATA_MODEL.md` | **v6.3** — §3.5 `BuyerTradeUrl` için DB CHECK istisnası notu |
| Testler | `TransactionAcceptanceUnitTests` (+31 vaka, state machine dahil), `TransactionAcceptanceServiceTests` (+17), `TransactionLifecycleEndpointTests` (+3 + Factory `ITradeHoldChecker` swap) |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `steamTradeUrl` zorunlu alan olarak isteğe eklendi; format doğrulaması (partner + token ayrıştırılabilmeli) başarısızsa 400 `INVALID_TRADE_URL` | ✓ | Alan: `TransactionLifecycleDtos.cs:94` (positional, opsiyonel değil). Doğrulama: `TransactionAcceptanceService.cs` Stage 4b. Statü eşlemesi: `TransactionsController.cs` `InvalidTradeUrl → BadRequest`. Test: unit `Trade_Url_Parser_Contract_Is_The_Accept_Endpoint_Contract` (20 vaka — 6 kabul / 14 red), servis `Malformed_Trade_Url_Rejected_With_InvalidTradeUrl` (12 vaka; her birinde `Status=CREATED`, `BuyerId=null`, outbox boş, probe çağrılmadı), HTTP `Accept_Invalid_Trade_Url_Returns_400` |
| 2 | Değer `Transaction.BuyerTradeUrl`'e yazılıyor (06 §3.5: ACCEPTED ve sonrasında NOT NULL) | ✓ | Yazım: `TransactionAcceptanceService.cs` Stage 6 (`parsedTradeUrl.Normalized`). Invariant: `TransactionStateMachine.HasFieldsForAccepted` artık alanı arıyor. Test: `Accept_Persists_Normalized_BuyerTradeUrl_And_Probes_With_Its_Token` (ham `  http://STEAMCOMMUNITY.COM/...&l=turkish  ` → DB'de kanonik https/küçük-harf/parametresiz biçim), `Happy_Path_...` DB assert'i, HTTP `Accept_Happy_Path_...` DB assert'i, negatif `BuyerAccept_WithoutBuyerTradeUrl_ThrowsInvalidTransition` |
| 3 | Alıcının Mobile Authenticator'ı doğrulanıyor; hold süresi 0 değilse 403 `MOBILE_AUTHENTICATOR_REQUIRED` | ✓ | Probe: Stage 5b, `ITradeHoldChecker.CheckAsync(buyer.SteamId, parsedTradeUrl.Token, …)`. Test: servis `Mobile_Authenticator_Inactive_Rejects_And_Writes_Nothing` (403 + tx `CREATED`, `BuyerId`/`BuyerRefundAddress`/`BuyerTradeUrl` null, outbox boş), HTTP `Accept_Without_Mobile_Authenticator_Returns_403`, ayrıca `Accept_Persists_...` probe'un doğru SteamID + gövdeden gelen token ile **tam 1 kez** çağrıldığını doğruluyor |

**Kriter dışı ama aynı kapının parçası — Steam erişilemezse.** 07 §7.6 bu vaka için kod tanımlamıyordu; 08 §2.2 ise "fail-closed, işlem ilerlemez" diyor. Proje sahibi kararıyla **503 `STEAM_UNAVAILABLE`** eklendi ve 07 §7.6 hata listesine yazıldı. Test: `Steam_Unreachable_Fails_Closed_With_SteamUnavailable`, HTTP `Accept_When_Steam_Unreachable_Returns_503`.

**Pipeline sırası kanıtı.** MA probe bilinçli olarak en son kapı: sidecar Steam'e 1 req/s kuyrukla gidiyor, dolayısıyla daha ucuz bir sebeple (flag, state, taraf, cüzdan, sanctions, cooldown, trade URL) zaten reddedilecek istek Steam turu harcamamalı. `Cheaper_Rejections_Never_Spend_A_Steam_Round_Trip` bunu `CallCount == 0` ile davranış olarak sabitliyor; malformed-URL ve third-party testleri de aynı sayacı doğruluyor.

## Proje Sahibi Kararları (2026-08-10, kapsam netleştirme turunda)

| # | Karar | Gerekçe |
|---|---|---|
| 1 | Steam erişilemezse **fail-closed + 503 `STEAM_UNAVAILABLE`** | 08 §2.2 yönetici kuralı "ilerlemez" diyor. 403 `MOBILE_AUTHENTICATOR_REQUIRED` yanlış bilgi verirdi — alıcının MA'sı sağlam olabilir ve düzeltemeyeceği bir işe yönlendirilir. 07 §7.6a (confirm-ready) aynı durumu zaten aynı kodla karşılıyor |
| 2 | Trade URL'in **sahiplik doğrulaması eklensin** (`partner` ↔ alıcının kendi SteamID64'ü) | Spec'te yoktu. P2P'de item'ın hedefini belirleyen tek alan bu; başkasının URL'i verilirse satıcı item'ı yabancıya gönderir, para yine satıcıya akar ve MA probe'u yanlış çift için cevap verir |
| 3 | **FE + profil prefill'i aynı değişikliğe dahil** | Backend zorunlu alanı ekler eklemez `AcceptForm` 400 alırdı. Ayrıca 07 §7.6'nın dayandığı "istemci ön-doldurur" varsayımının kaynağı yoktu (`GET /users/me` alanı döndürmüyordu) |

## Keşifte çıkan defekt (T119a'yı bloke ediyordu)

**`sidecar-fake` trade-hold ucu ters cevap veriyordu.** `sidecar-fake/src/routes/steam.ts` yorumunda *"MA-verified seller, no Steam escrow hold"* yazarken gövdesi `{ active: false }` dönüyordu. Backend `active` alanını **MA-aktif** bayrağı olarak okuyor (`HttpSteamTradeHoldClient.cs:112` → `payload.Active ? Active : Inactive`; `escrowEndDurationSeconds` parse ediliyor ama kullanılmıyor), yani fake "MA kapalı" diyordu. Bugüne kadar sessiz kalmasının sebebi E2E kullanıcılarının `MobileAuthenticatorVerified` ile doğrudan DB'ye seed edilmesi ve accept yolunun probe'a hiç dokunmamasıydı. T119a canlı probe eklediği anda **8 E2E suite'inin tamamı 403 ile düşerdi**. `active: true` yapıldı.

**İkinci mayın — API test Factory'si.** `Skinora.API.Tests` host'u `SteamSidecar:BaseUrl` ayarlamıyor; `ITradeHoldChecker` override edilmeseydi `HttpSteamTradeHoldClient` fail-closed dönüp **her accept testini 503**'e çevirirdi. Factory'ye `ConfigurableTradeHoldStub` swap'i eklendi (U17 endpoint testlerinin stub'ı yeniden kullanıldı).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0 Error / 0 Warning | `dotnet build Skinora.sln -c Release` |
| `dotnet format` | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes --severity error` |
| Unit | ✓ **1361/1361** | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — T119'daki 1330 + **31** |
| Integration | ✓ **1106/1106** | `--filter "FullyQualifiedName~.Integration"` — T119'daki 1086 + **20** (Transactions 333, API 453). *İlk koşumda `Skinora.Notifications.Tests` 58/60 verdi; aynı süit hem tek başına (60/60) hem de ikinci tam koşumda (60/60, `EXIT=0`) geçti — F6'da belgelenen paralel-koşum artefaktı, T119a değişikliklerinin dokunmadığı bir assembly* |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` |
| FE lint | ✓ 0 bulgu | `npm run lint` |
| FE typecheck | ✓ temiz | `npx tsc --noEmit` |
| FE i18n parity | ✓ 4 dil × **1297** anahtar, aynı küme | `npm run i18n:check` — advisory uyarı sayısı 15 (T119 tabanıyla aynı; eklenen anahtarların ikisi "Mobile Authenticator" terimini birebir koruyacak şekilde düzeltildi) |
| FE vitest | ✓ 33/33 | `npm run test` (9 dosya) |
| FE build | ✓ başarılı | `npm run build` |
| e2e / sidecar-fake | ✓ lint + `tsc --noEmit` temiz | her iki pakette |
| Migration | Yok | Şema değişikliği yok |

**Prettier notu.** Lokal `format:check` her iki JS paketinde de dokunulmamış dosyalar dahil uyarı veriyor — bilinen `core.autocrlf` CRLF artefaktı. Değiştirdiğim dosyaların LF kopyaları paket içinde `npx prettier --check` ile ayrı ayrı denendi: **hepsi temiz** (CI "1. Lint" işi LF gördüğü için yetkili olan budur).

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız doğrulama chat'i bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok** — `BuyerTradeUrl` kolonu T117'de eklenmişti; nullable bırakılması bilinçli (bkz. Known Limitations #1)
- Config/env değişikliği: **Yok** — yeni `SystemSetting`/`SKINORA_SETTING_*` yok
- Docker değişikliği: **Yok**
- Yeni paket: **Yok**
- DI değişikliği: **Yok** — `ITradeUrlParser` (Singleton) ve `ITradeHoldChecker` (Scoped stub → `SteamModule` Replace) `Skinora.Users`'ta zaten kayıtlıydı; `Skinora.Transactions → Skinora.Users` proje referansı mevcut

## Commit & PR

- Branch: `task/T119a-accept-v3-fields`
- Commit: `<hash>` — accept v3.0 alanları
- PR: #<no>
- CI: <sonuç>

## Known Limitations / Follow-up

| # | Açık | Durum |
|---|---|---|
| 1 | **`BuyerTradeUrl` DB'de nullable kalıyor.** `CREATED`'da alıcı yok, dolayısıyla NOT NULL yapılamaz; CHECK constraint de eklenmedi. Invariant yalnız `HasFieldsForAccepted` guard'ında korunuyor. Prod'da NULL kalacak geçmiş `ACCEPTED` satırı **yok** (canlı stack'te `Transactions = 0`, `IMPLEMENTATION_STATUS.md` §Post-MVP), yani backfill sorunu doğmuyor | 06 §3.5'e istisna notu olarak yazıldı (**v6.3**) |
| 2 | **`INVALID_TRADE_URL` iki uçta farklı statü.** Accept 400 (07 §7.6), U17 profil kaydı 422 (07 §5.16a). Bu turda spec'e uyuldu; ortaklaştırma T133a doküman turunun konusu | 07 §7.6'ya not düşüldü |
| 3 | **Satıcının teslimat CTA'sı hâlâ ekrana gelmiyor.** Backend `steamTradeOfferUrl`'ü `PAYMENT_RECEIVED + satıcı` için dolduruyor (`TransactionDetailService.cs:227-234`) ama FE bu alanı yalnız emekli `TRADE_OFFER_SENT_TO_*` dallarında render ediyor (`StateActionPanel.tsx:300, :327`); `PAYMENT_RECEIVED` dalı linksiz. T119a bu kolonu **doldurdu**, gösterimi **T135**'e ait | T135 kapsamında |
| 4 | **Profil ekranında trade URL yüzeyi yok.** U17 `PUT /users/me/settings/steam/trade-url` üretimde ama FE'de hiçbir yerde çağrılmıyor (`grep tradeUrl frontend/src` → 0 eşleşme). Alıcı URL'i yalnız kabul formunda girebiliyor; prefill ancak URL başka bir yoldan kaydedilmişse çalışır | T134/T136 yüzeyi — bu turda kapsam dışı bırakıldı |
| 5 | **`AcceptForm` için FE birim testi yok.** `components/transactions/detail/` altında hiç `.test.tsx` yok; yeni alanın FE davranışı vitest'le değil yalnız tip/lint/build ile korunuyor | Mevcut FE test stratejisinin sınırı |

## Notlar

- **Working tree (Adım -1):** temiz — `git status --short` boş.
- **Main CI startup check (Adım 0):** son 3 tamamlanmış run `success` — `31414178181`, `31414178436` (T119 #226), `31380447239` (chore #225).
- **Bağımlılık:** T118 ✓ Tamamlandı (PR #224, main `549e401`); T119a'nın planda tanımlı tek bağımlılığı.
- **Dış varsayımlar (Adım 4) — hepsi kanıtlandı, kırık yok:**
  | Varsayım | Kanıt |
  |---|---|
  | Trade URL parser'ı sıfırdan yazmaya gerek yok | `ITradeUrlParser.cs:22-41` — `Parse` → `TradeUrlComponents(Normalized, Partner, Token)`, U17'de üretimde |
  | MA probe portu hazır ve üç değerli (Steam kesintisini koruyor) | `ITradeHoldChecker.cs:13-26` → `TradeHoldResult(Available, Active, SetupGuideUrl)`; `IMobileAuthenticatorCheck` (Auth) **kullanılmadı**, çünkü `SidecarMobileAuthenticatorCheck.cs:35-41` `Unavailable`'ı `Active=false`'a eziyor |
  | Modül referans yönü uygun | `Skinora.Transactions.csproj:5` → `Skinora.Users`; ters yön (`Skinora.Steam.csproj:7` → `Skinora.Transactions`) olduğu için Steam'e referans verilemezdi |
  | `MOBILE_AUTHENTICATOR_REQUIRED` sabiti zaten var | `TransactionErrorCodes.cs:15` — yeniden tanımlanmadı |
  | Prod'da NULL `BuyerTradeUrl` taşıyan `ACCEPTED` satırı yok | `IMPLEMENTATION_STATUS.md` §Post-MVP tablosu: canlı stack'te `Transactions = 0` |
  | Yeni paket / plan tier / env varsayımı | **Yok** |
- **Mini güvenlik kontrolü:** Secret sızıntısı yok (diff'te anahtar/parola/mnemonic yok; testlerdeki trade URL token'ları uydurma sabitler). **Auth/authorization:** yeni uç yok; mevcut accept ucuna iki kapı **eklendi** (daraltma yönünde). **Input validation:** yeni kullanıcı girdisi `steamTradeUrl` merkezi parser'dan geçiyor — şema/host/path allow-list'i subdomain ve suffix saldırılarını kapatıyor, saklanan değer normalize edilmiş biçim; ayrıca sahiplik kapısı üçüncü şahsa yönlendirmeyi engelliyor. `IsOwnedByBuyer` çıkarma yönünde çalışıyor ki 20 haneye kadar izin verilen `partner` değeri `ulong` taşmasıyla sahte eşleşme üretemesin. **Yeni dış bağımlılık:** yok.
- **Kapsam dışı bırakılanlar (bilinçli):** 07 §7.6a/§7.6b uçları (T123), FE state×rol matrisi (T135), doküman custodial kalıntı turu (T133a), profil ekranı trade URL yüzeyi (T134/T136).
