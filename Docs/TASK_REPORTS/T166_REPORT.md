# T166 — Küçük düzeltmeler (tek PR)

**Faz:** F9 | **Durum:** ✓ Tamamlandı (doğrulama PASS) | **Tarih:** 2026-10-21

---

## Yapılan İşler

1. **`_diag` uçları üretimde kapalı.** `DiagnosticsController` (`/api/v1/_diag/*` — keyfi istisna fırlatan, rate limit kovalarını açan test uçları) `[DevelopmentOnly]` ile işaretlendi. `DevelopmentOnlyControllerConvention` ortam `Development` değilse bu controller'ı **uygulama modelinden çıkarır**: rota hiç kaydedilmez, her istek düz 404. Kural "yalnız Development'ta aç"tır (Staging ya da yanlış yazılmış ortam adı da kapalı kalır).
2. **Kullanım şartları + 18+ onayı sunucu tarafında zorunlu.**
   - Tek kural: `TosAcceptanceRule.IsSatisfied(TosAcceptedAt, AgeConfirmedAt)` — iki damga birden.
   - Global `TosAcceptanceGateFilter` (MVC yetkilendirme filtresi): yetki gerektiren her uçta hesap kuralı karşılamıyorsa **403 `TOS_NOT_ACCEPTED`**. Kapsam dışı: anonim uçlar, `[SkipTosAcceptanceGate]` taşıyan oturum yüzeyi (`AuthController`: `me`, `tos/accept`, `refresh`, `logout`, `steam/re-verify`, `check-authenticator`), `sub`'ı bir kullanıcıya çözülmeyen token (o durum uçların kendi 401/404 yolunda).
   - `/auth/me` `tosAccepted` aynı kuralı okur → `false` tam olarak "API 403 verecek" demektir.
   - `TosAcceptanceService`: yalnız ToS damgası olup 18+ damgası olmayan hesap (T30 öncesi kabul) aynı sürümü gönderince **409 değil 200** alır ve beyan tamamlanır — aksi hâlde kapı + 409 çıkışı olmayan bir kilit olurdu.
   - Arayüz: global `TosRepromptGate` artık **ilk kez kabul etmemiş** hesaba da modal gösterir (önceden yalnız sürüm değişiminde). Neden gerekli: callback modalını kapatıp çıkan hesap bir sonraki girişte `status=success` alır ve modal bir daha hiç görünmezdi; sunucu kapısıyla bu hesap tamamen kilitlenirdi. Kapı `/auth/*`, `/terms`, `/privacy` sayfalarında uyur (callback kendi modalını gösterir; şartlar sayfası okunabilir kalmalı). Kabulde bütün sorgular yenilenir (arkadaki sayfa 403 almış olabilir). İlk kabulde "uygun değilim" çıkışı `/auth/age-gate`'e gider.
   - E2E tohum kullanıcıları (satıcı, alıcı, admin) iki damgayla eklenir.
3. **Ülke engeli istemci başlığına güvenmiyor** (tasarım aşağıda "Ülke başlığı güven zinciri").
   - Backend: `HeaderCountryResolver` `X-Country-Code`'a yalnız `ITrustedProxyVerifier` "bizim proxy'mizden geldi" derse inanır. `ReverseProxyTrustVerifier` kararı **doğrudan TCP eşine** göre verir; eş, `UseForwardedHeaders` `RemoteIpAddress`'i istemci IP'siyle değiştirmeden önce `DirectPeer` ara katmanıyla yakalanır. Liste F3a'nın `ReverseProxy:KnownNetworks/KnownProxies`'i — tek güven kararı. Yapılandırılmamışsa ya da eş yakalanmamışsa güvenilmez. Cloudflare'in `XX` (bilinmiyor) değeri "cevap yok" sayılır (MaxMind'e şans kalır); `T1` (Tor) olduğu gibi geçer (admin yasaklayabilsin).
   - nginx: `geo $skinora_trusted_edge` + `map` — `CF-IPCountry` backend'e `X-Country-Code` olarak **yalnız** nginx'in TCP eşi tünel bağlayıcısıysa taşınır; diğer her eşte başlık boş gönderilir (= istemcinin yazdığı değer düşer). `CF-IPCountry` backend'e hiç geçmez.
4. **Sabit "%99,9 çalışma süresi" kalktı (4 dil).** Açılış sayfası güven kartı, `landing.trust.uptime` anahtarı (en/es/tr/zh), P1 `platformUptimePercent` alanı, `PlatformOptions` + `Platform:UptimePercent` ayarı kaldırıldı. Kart ızgarası 3 → 2 kolon.
5. **USDC ürün yüzeyinden kalktı (K2).** `GET /transactions/params` → `supportedStablecoins: ["USDT"]`; `POST /transactions` `stablecoin: "USDC"` → 400 `VALIDATION_ERROR` ("Only USDT is supported for new transactions."); işlem formundan stablecoin seçimi kalktı (04 §7.2 "toggle kalkar"), oturumda saklı eski bir taslak USDC dese de form USDT kullanır; 4 dilde iki metin USDT'ye çevrildi (ToS modal özeti `auth.tos.summary.crypto`, gizlilik metnindeki "(USDT, USDC)").
6. **`auth.min_steam_account_age_days` üretimde 30.** Repodaki yetkili değer zaten 30: `SystemSettingSeed` + T30 migration + model snapshot (ölçüldü, aşağıda); hiçbir compose/env/appsettings bunu ezmiyor. Eksik olan, elle düşürülmüş bir veritabanının **hiçbir yerde görünmemesiydi** (yerel prova DB'si 2026-08-29'dan beri 1). Yeni `AgeGateConfigDiagnosticHook` her açılışta değeri okur; `Development` dışında 30'un altındaysa `Age gate BelowProduction / Disabled / Unreadable …` uyarısı basar. Yalnız teşhis, açılışı durdurmaz (prova yığını `Production` ortamıyla çalışıyor ve 1 değeri sahip kararıyla duruyor). Yerel prova DB'sine dokunulmadı.

### Ülke başlığı güven zinciri (deneme yayını: Cloudflare Tunnel → nginx → backend)

| Halka | Kime güvenir | Neyi yapar |
|---|---|---|
| Cloudflare | — | Her isteğe `CF-IPCountry` yazar, istemcinin gönderdiğini ezer |
| nginx (`$skinora_trusted_edge`) | TCP eşi tünel bağlayıcısı (varsayılan compose ağı `172.20.0.0/16`, **ağ geçidi `172.20.0.1` hariç**) | `X-Country-Code = CF-IPCountry` ya da boş; `CF-IPCountry`'yi backend'e geçirmez |
| backend (`ReverseProxyTrustVerifier`) | TCP eşi `ReverseProxy:KnownNetworks` içinde (= nginx) | `X-Country-Code`'a ancak o zaman inanır; değilse ülke MaxMind'e (forwarded-header ile çözülmüş istemci IP'si) düşer, o da yoksa geo-block açık kalır (belgelenmiş duruş: "eksik bilgi, yanlış güven değil") |

Ağ geçidinin hariç tutulması ölçüme dayanıyor: Docker Desktop'ta yayımlanmış porta gelen istek nginx'e ağ geçidi adresinden (`10.166.0.1` deneme ağında) ulaştı.

## Etkilenen Modüller / Dosyalar

- **Backend — yeni:** `Skinora.API/Filters/DevelopmentOnlyAttribute.cs`, `Skinora.API/Filters/TosAcceptanceGateFilter.cs`, `Skinora.API/Configuration/DirectPeer.cs`, `Skinora.API/Configuration/ReverseProxyTrustVerifier.cs`, `Skinora.API/Startup/AgeGateConfigDiagnosticHook.cs`, `Skinora.Auth/Application/TosAcceptance/TosAcceptanceRule.cs`, `Skinora.Auth/Application/SteamAuthentication/ITrustedProxyVerifier.cs`
- **Backend — değişen:** `Program.cs` (convention, global filtre, `DirectPeer` ara katmanı, yaş teşhisi, `PlatformOptions` kaydı silindi), `DiagnosticsController.cs`, `AuthController.cs`, `TosAcceptanceService.cs`, `CurrentUserService.cs`, `HeaderCountryResolver.cs`, `ChainedCountryResolver.cs` (yorum), `ReverseProxySettings.cs` (paylaşılan ayrıştırma), `SteamAuthenticationModule.cs`, `PlatformPublicDtos.cs`, `PlatformPublicService.cs`, `appsettings.json`, `TransactionParamsService.cs`, `TransactionCreationService.cs`; **silinen:** `Services/PlatformOptions.cs`
- **Backend testleri — Auth:** `TosAcceptanceServiceTests` (tohum + yaş damgası eksik testi)
- **Backend testleri — yeni:** `DiagnosticsEnvironmentGateTests`, `TosAcceptanceGateTests`, `ReverseProxyTrustVerifierTests`, `AgeGateConfigDiagnosticHookTests`; **değişen:** `TosAcceptEndpointTests` (+ yaş damgası eksik testi), `AuthSteamEndpointTests` (güvenilen/güvenilmeyen eş + TCP eşi test seam'i), `HeaderCountryResolverTests`, `PlatformPublicEndpointTests`, `TransactionParamsServiceTests`, `TransactionCreationServiceTests` (+ USDC reddi), 20 uç test sınıfının kullanıcı tohumu (kabul etmiş hesap) + `AuthSessionEndpointTests`
- **Frontend:** `components/auth/TosRepromptGate.tsx` (+ yeni `TosRepromptGate.test.tsx`), `components/landing/TrustSignals.tsx`, `lib/api/platform.ts`, `components/transactions/new/{Step2Details,NewTransactionForm}.tsx`, `Step2Details.test.tsx`, `i18n/messages/{en,es,tr,zh}.json`
- **Altyapı:** `nginx/nginx.conf`, `e2e/src/db.ts`
- **Doküman:** `07_API_DESIGN.md` v4.17, `02_PRODUCT_REQUIREMENTS.md` v4.17, `04_UI_SPECS.md` v4.12, `DEPLOY_RUNBOOK.md` (§B, §F, §G.5), `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md` (`uptime-cache-scaleout` satırı), `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `_diag` uçları üretimde kapalı | ✓ | `DiagnosticsEnvironmentGateTests` 5/5: `Production`, `Staging`, `production` ortamında `ping`, `throw/unhandled`, `auth/public`, `ratelimit/public` → 404; negatif kontrol `Development`'ta `ping` → 200; Production'da başka controller (`/auth/me`) hâlâ eşleşiyor (≠ 404) |
| 2 | ToS + 18+ sunucu tarafında zorunlu (istemci atlasa da reddedilir; mevcut kullanıcılar ve sürüm düşünüldü) | ✓ | `TosAcceptanceGateTests` 9/9: kabul etmemiş hesap gerçek uç (`/users/me/settings`) ve prob uçta 403 `TOS_NOT_ACCEPTED`; yalnız ToS damgalı (18+ yok) hesap 403; kabul etmiş hesap 200; 403 → `POST /auth/tos/accept` → 200 zinciri; `/auth/me` ve `logout` kabul etmemiş hesapta açık, `tosAccepted=false`; anonim uç kapı dışı. `TosAcceptEndpointTests.Accept_SameVersion_WhenAgeStampMissing_Returns200AndStampsAge`. Arayüz: `TosRepromptGate.test.tsx` 9/9 (ilk kabul modalı, yaş reddi yönlendirmesi, kabul çağrısı, callback ve `/terms`'te uyuma, güncel sürümde sessizlik). Mevcut kullanıcılar: kabul etmiş hesap etkilenmez; kabul etmemiş ya da yaş damgası eksik hesap bir sonraki sayfada modalla karşılaşır ve tek tıkla çıkar (kilit yok). Sürüm: sunucu zorunlu tutmuyor (⚠ GÜVENLİK kararı aşağıda) |
| 3 | Ülke engeli istemcinin başlığına güvenmiyor (Cloudflare başlığı yalnız güvenilir iç proxy zincirinden) | ✓ | Backend: `AuthSteamEndpointTests.Callback_CountryHeaderFromUntrustedPeer_IsIgnored` (eş `203.0.113.7` ve eş yok; yasaklı liste başlığın KENDİ değeri `IR`, sahte `X-Forwarded-For` + `CF-IPCountry` eklenmiş → `geo_blocked` YOK) + güvenilen eşten (`10.0.0.5`) aynı başlık → `geo_blocked`; `ReverseProxyTrustVerifierTests` 7/7 (yapılandırılmamış → güvenme, ağ içi/dışı, `::ffff:` eşleme, tam proxy eşleşmesi, yakalanmamış eş, istemci başlıklarıyla sahte güven yok); `HeaderCountryResolverTests` 10/10. nginx: Docker'da canlı ölçüm (aşağıda) — güvenilen eşten `CF-IPCountry: TR` + sahte `X-Country-Code: US` → backend `xcc=[TR]`; güvenilmeyen ağdan ve yayımlanmış porttan (ağ geçidi) `US`/`US` → `xcc=[]`; `/hubs` güvenilen eşten `DE` → `xcc=[DE]`; `CF-IPCountry` backend'e hiç geçmedi (`cf=[]`) |
| 4 | Sabit "%99,9 çalışma süresi" yazısı kalktı (4 dil) | ✓ | 4 dilde `landing.trust.uptime` silindi, `npm run i18n:check` → "4 locales, 1576 keys each, identical key-set"; `grep -rni uptime frontend/src` → yalnız açıklama yorumu; `PlatformPublicEndpointTests.Stats_Anonymous_Returns200_WithZeroCount_AndNoUptimeField` (`platformUptimePercent` yok) |
| 5 | USDC ürün yüzeyinden kalktı (arayüz, metinler, seçim listeleri, kullanıcıya açık API seçenekleri) — mevcut kayıtlar/iade yolları bozulmadan | ✓ | `TransactionParamsServiceTests` (`["USDT"]`), `TransactionCreationServiceTests.Rejects_Usdc_Stablecoin` (ValidationFailed, satır yazılmadı); `grep -c USDC frontend/src/i18n/messages/*.json` → 0/0/0/0; işlem formunda toggle yok (`Step2Details`). Kalanlar aşağıdaki "USDC — kalan yüzey" listesinde; yanlış token iadesi testleri (`AmountValidationServiceTests`, `BlockchainWebhookEndpointTests` USDC, e2e `payment-edge-cases` §5.3) koda dokunulmadan duruyor |
| 6 | `auth.min_steam_account_age_days` üretimde 30 | ✓ | Seed `Default(30, "auth.min_steam_account_age_days", …, "30")`; T30 migration `InsertData … "30"`; `AppDbContextModelSnapshot` `Value = "30"`; compose/.env.example/appsettings'te geçersiz kılan yok (`grep`). `AgeGateConfigDiagnosticHookTests` 10/10 (30/45 üretim, 1/29 altında, 0/-3 kapalı, boş/çöp okunamaz, **seed değeri = sabit 30**). Yerel prova DB'si (1) değiştirilmedi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend — API.Tests | ✓ (bkz. not) | Etkilenen 28 sınıf: `dotnet test tests/Skinora.API.Tests --filter …` → **426/426 passed**. Tam proje koşusu: bkz. "Tam koşular" satırı |
| Backend — API.Tests (tam) | ✓ 924/924 | `dotnet test tests/Skinora.API.Tests` (7 dk 53 sn) |
| Backend — Auth.Tests (tam) | ✓ 137/137 | İlk tam koşuda 1 kırmızı: `TosAcceptanceServiceTests.AcceptAsync_SameVersionAlreadyAccepted_ThrowsDomainException` — tohum yalnız ToS damgası koyuyordu, yeni kural iki damga istiyor. Tohuma 18+ damgası eklendi + yeni test `AcceptAsync_SameVersion_WhenAgeStampMissing_CompletesAttestation`; yeniden koşu 137/137 |
| Backend — Transactions.Tests (tam) | ✓ 1735/1735 | `dotnet test tests/Skinora.Transactions.Tests` |
| Backend — Platform.Tests (tam) | ✓ 235/235 | seed testleri dahil |
| Frontend — vitest | ✓ 400/400 (42 dosya) | `npx vitest run` |
| Frontend — tsc | ✓ | `npx tsc --noEmit` çıkış 0 |
| Frontend — eslint | ✓ | `npx eslint src/...` çıkış 0 |
| Frontend — i18n | ✓ | `npm run i18n:check` — parity OK, 1576 anahtar × 4; 20 advisory uyarı önceden var olan `Mobile Authenticator`/`Gas fee` uyarıları |
| Frontend — prettier | ✓ | Değişen dosyalar `--end-of-line auto` ile temiz (yerel CRLF artefaktı, CI LF yetkili) |
| Frontend — build | ✓ | `npm run build` çıkış 0 |
| nginx | ✓ | `nginx -t` başarılı + canlı başlık ölçümü (aşağıda) |
| E2E | CI | Tohum kullanıcılarına damga eklendi; CI E2E işinde koşar |

**nginx canlı ölçümü (2026-10-21, Docker, geçici ağlar `10.166.0.0/24` güvenilen / `10.167.0.0/24` dış; `nginx.conf` kopyasında yalnız alt ağ değiştirildi; arka uç başlıkları geri yansıtan bir nginx):**

```
1) güvenilen eş 10.166.0.20, CF-IPCountry=TR + sahte X-Country-Code=US  → xcc=[TR] cf=[]
2) dış ağdaki eş 10.167.0.3, CF-IPCountry=US + X-Country-Code=US        → xcc=[]   cf=[]
3) host → yayımlanmış port (nginx'e 10.166.0.1 ağ geçidinden ulaştı)     → xcc=[]   cf=[]
4) /hubs/ güvenilen eş 10.166.0.21, CF-IPCountry=DE                     → xcc=[DE] cf=[]
```

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS — bağımsız doğrulama (Opus high, izole alt ajan), HEAD `52e705cb`, 6/6 kabul kriteri |
| Bulgu sayısı | 4 × S4 (B1–B4), bloke eden yok. Rapor: `T166_VALIDATION.md` (f9-handoff) |
| Düzeltme gerekli mi | B2 kapanışta düzeltildi (`docker-compose.yml` `skinora-network` `ipam` alt ağı 172.20.0.0/16 sabitlendi + DEPLOY_RUNBOOK §B cümlesi; `docker compose config --quiet` geçerli). B1: T161 backend portunu kapatınca kapanır (öneri: backend tarafında da ağ geçidini dışarıda tut, §F(b)'ye doğrudan backend portu sınaması). B3: cloudflared T162. B4: DEFERRED_BACKLOG 🟡 `TosNotAcceptedAccountCannotSelfDelete` (hedef T168) |

Kapanışta `origin/main` (T160a #363 + T160b #364, `2e502e99`) merge commit ile dala alındı; 6 belge çakışması iki tarafı koruyarak çözüldü, kod çakışması yoktu.

## Altyapı Değişiklikleri

- Migration: **Yok** (yaş eşiği seed/migration'da zaten 30; şema değişmedi)
- Config/env değişikliği: **Var** — `appsettings.json`'dan `Platform:UptimePercent` kalktı (bağlanan sınıf da silindi; ortamda `Platform__UptimePercent` verilmişse artık yok sayılır). Yeni env yok.
- Docker / nginx değişikliği: **Var** — `nginx/nginx.conf`'a `geo $skinora_trusted_edge` + `map $skinora_edge_country`; `/api/` ve `/hubs/`'ta `X-Country-Code` üzerine yazılır, `CF-IPCountry` boşaltılır, `/health`'te ikisi de boş. Compose değişmedi.

## Commit & PR

- Branch: `task/T166-kucuk-duzeltmeler`
- Commit'ler: `54cbefc3` _diag · `58457761` ToS/18+ · `34647588` ülke başlığı · `20927cf9` yaş teşhisi · `c3cae541` uptime + USDC · `bda58a8e` dokümanlar · Auth test düzeltmesi + rapor/status/memory commit'i bunun üstünde
- PR: #366
- CI: bkz. PR (yapım ajanı son run'ı izliyor; sonuç yöneticiye bildirilir)

## USDC — kalan yüzey (bilerek dokunulmadı)

| Yer | Neden kalıyor |
|---|---|
| `StablecoinType.USDC` enum değeri (backend + FE `types/enums.ts`) | Mevcut kayıtlar ve yanlış token iadesi bu değeri taşır; enum parity testleri |
| `StablecoinContractOptions`, `AmountValidationService` USDC kontratları (TRON + Ethereum) | **Yanlış token iadesi** (02 §4.4): yanlışlıkla gönderilen USDC'nin tanınıp alıcıya iade edilmesi buna bağlı |
| `OutgoingTransferDispatchJob` / `OutboundTransferExpectation` / `HttpBlockchainTransferClient` USDC kolları | Aynı iade yolunun gönderimi ve onayı |
| `HotWalletMonitorService`, `ReconciliationService`, `AdminWalletOverviewService` USDC bakiyeleri | Yanlış token iadesi sweep sonrası sıcak/soğuk cüzdanda USDC bırakabilir; görünür kalmalı |
| Admin: S15 işlem listesi stablecoin süzgeci (USDT/USDC), soğuk aktarım formu TRON'da USDT/USDC, `AdminWalletsController` token doğrulaması | Eski USDC işlemleri ve cüzdanda kalan USDC için operasyon aracı (04 §1523'teki önceki ⚠ kararıyla tutarlı) |
| `i18n/untranslatable.ts` "USDC" | Lint listesi; admin ekranları hâlâ "USDC" gösteriyor |
| e2e `payment-edge-cases` §5.3 (yanlış token USDC) | İade yolunun uçtan uca testi |

## Known Limitations / Follow-up

- **ToS sürümü sunucuda zorunlu değil.** Sunucu "geçerli sürüm"ü bilmiyor (`NEXT_PUBLIC_TOS_VERSION` yalnız istemcide). Sürüm değişince yeniden onay istemci tarafında yumuşak kalıyor. T168 (hukuk metinleri) sürümü artırırsa sunucu tarafı sürüm kontrolü ayrıca kararlaştırılmalı.
- **SignalR hub'ları kapı dışında.** MVC filtresi hub'lara uygulanmaz. Hub'lar yalnız hesabın kendi işlem/bildirim olaylarını iter; kapıdan geçmemiş hesabın işlemi olamaz, ama bildirim (ör. hoş geldin) bağlantısı açılabilir.
- **Tünel arkasında istemci IP'si (T162'ye girdi, ⚠ GÜVENLİK).** Cloudflare Tunnel → nginx → backend zincirinde nginx'in eşi tünel bağlayıcısıdır; mevcut `X-Forwarded-For` + `ForwardLimit=1` kurgusu backend'e **bağlayıcının IP'sini** istemci IP'si olarak verir. Ülke artık `CF-IPCountry`'den güvenle geldiği için geo-block çalışır; ama IP tabanlı rate limit, VPN sinyali, MaxMind yedeği ve denetim kayıtlarındaki IP için nginx'te `set_real_ip_from <bağlayıcı>` + `real_ip_header CF-Connecting-IP` (aynı güvenilen eş listesiyle) gerekir. Bu T166'nın kabul kriteri değil; T162'de tünel kurulurken yapılmalı. DEPLOY_RUNBOOK §B satırı da T162'de `geo` listesinin bağlayıcının adresine daraltılmasını söylüyor.
- **`geo` varsayılanı compose ağıdır (`172.20.0.0/16`, ağ geçidi hariç).** Ağ başka bir alt ağ alırsa hiçbir şey eşleşmez → ülke MaxMind'e düşer (yanlış güven yok, eksik bilgi var). Aynı ağdaki her container "kenar" sayılır; üretimde yalnız bizim container'larımız var.
- **Yaş eşiği teşhisi yalnız uyarır.** Prova DB'sinde değer 1 kaldıkça her açılışta uyarı görülecek (beklenen). Geri alma kararı sahipte (2026-09-30), T166 dokunmadı.

## Notlar

- **Working tree:** temiz (dal `50868b13`'ten açık, `git status` boş).
- **Main CI startup check:** son 3 tamamlanmış main run'ı `39546534805` ✓, `39546534823` ✓, `39545306202` ✓ (başlangıçta T160a merge run'ları `39572533142`/`39572533083` sürüyordu; sonra ikisi de ✓).
- **Bağımlılık:** T166'nın plan bağımlılığı yok (F9 küçük işler); F8 ✓ (`phase/F8-pass`).
- **Dış varsayımlar:**
  1. *Cloudflare `CF-IPCountry` başlığını her isteğe yazar ve istemcinin değerini ezer; bilinmeyen için `XX`, Tor için `T1`.* — Cloudflare'in belgelenmiş "IP Geolocation" davranışı; T166 bunu nginx'te yalnız güvenilen eşten aktarıyor, doğruluk T162'de gerçek tünelle ölçülmeli (bu ortamda Cloudflare hesabı yok — bilinen sınır).
  2. *Docker'da yayımlanmış porta gelen istek container'a ağ geçidi adresinden ulaşır (userland proxy).* — Ölçüldü (yukarıdaki ölçüm 3: `10.166.0.1`).
  3. *nginx `proxy_set_header` boş değerle başlığı göndermez; `map` değeri değişken taşıyabilir.* — Ölçüldü (ölçüm 1–3).
  4. *`WebApplicationFactory` varsayılan ortamı `Development`.* — Mevcut uç testleri `_diag`'a 200 almaya devam ediyor (`MiddlewarePipelineTests` 13/13; `AuthenticationTests` ve `RateLimitTests` kapı eklendikten sonraki ilk tam koşuda başarısızlar listesinde yok).
  5. *Üretim compose'unda ortam `Production`.* — `docker-compose.yml:35` `ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}`; e2e `Production`; yalnız `docker-compose.override.yml` ve `.env.example` `Development` (T171 `.env` tuzağı listesinde zaten var).
- **Kararlar (öneriyle kaydedildi — ⚠):**
  - ⚠ GÜVENLİK — ToS kapısı **sürüm değil kabul** ister (iki damga). Sürüm yeniden onayı istemcide yumuşak kalır; sunucuda yetkili bir "geçerli sürüm" yok ve uydurmak iki taraf ayrıştığında herkesi kilitlerdi.
  - ⚠ GÜVENLİK — Kapı **adminler dahil** bütün rollere uygulanır (adminler de Steam hesabı; kabul etmemiş admin arayüzde modalı görüp tek tıkla geçer).
  - ⚠ GÜVENLİK — Kapı dışı bırakılanlar: anonim uçlar, `AuthController`'ın tamamı (oturum yüzeyi), `sub`'ı kullanıcıya çözülmeyen token. Hesap silme gibi uçlar kapı **içinde** (kabul etmemiş hesap önce kabul eder ya da destekle görüşür).
  - ⚠ GÜVENLİK — Kapı her yetkili istekte tek PK okuması yapar, önbelleksiz (5–20 satış/gün hacminde ihmal edilebilir; önbellek veritabanıyla ayrışabilecek bir şey daha olurdu).
  - ⚠ GÜVENLİK — nginx güvenilen kenar varsayılanı compose ağı eksi ağ geçidi; T162'de bağlayıcının adresine daraltılmalı.
  - ⚠ GÜVENLİK — Cloudflare `XX` = bilinmiyor (MaxMind'e düşer), `T1` = olduğu gibi (admin yasaklayabilir).
  - ⚠ — `_diag` "Development dışında kapalı" (yalnız Production değil).
  - ⚠ — `platformUptimePercent` yalnız arayüzden değil **API'den ve ayardan da** kaldırıldı (sabit bir sayıyı sözleşmede tutmanın anlamı yok); gerçek ölçüm T170.
  - ⚠ PARA — USDC admin araçlarında (süzgeç, soğuk aktarım, bakiyeler) ve yanlış token iadesinde kalır; yeni ilanda 400.
  - ⚠ — Yaş eşiği için açılışı durduran kontrol değil, uyarı (kasıtlı admin kararı ve prova yığını meşru).
- **Mini güvenlik kontrolü:** sır yok; yeni uç yok; yetkilendirme sıkılaştı (yeni 403 yolu); girdi doğrulaması sıkılaştı (USDC reddi, ülke başlığı güveni); yeni dış bağımlılık yok.
