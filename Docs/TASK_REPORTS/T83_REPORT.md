# T83 — Geo-block servisi

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-19

---

## Yapılan İşler

- **IP→ülke gerçek lookup (kabul kriteri 1):** MaxMind GeoLite2-Country MMDB tabanlı `MaxMindCountryResolver` eklendi. T30 `HeaderCountryResolver` ile zincirlenir (`ChainedCountryResolver`: header → MaxMind → null). MMDB dosyası ops tarafından `Geolocation:DatabasePath` env değişkeniyle mount edilir; dosya yoksa header-only fallback (`Provider=logging` patterni — fail-closed default).
- **Yasaklı bölge engelleme (kabul kriteri 2):** T30'dan devralındı — `SettingsBasedGeoBlockCheck` + `AuthenticationOutcome.GeoBlocked` + `AuthController` `error=geo_blocked` redirect. Yeni zincir aynı pipeline çıkışını kullanır.
- **Admin yönetimi (kabul kriteri 3):** T30'dan devralındı — `auth.banned_countries` SystemSetting (CSV ISO-3166-1 alpha-2, `NONE` sentinel). Admin endpoint'ler `PUT /api/v1/admin/settings/{id}` üzerinden (07 §9.16).
- **VPN/proxy destekleyici sinyal (kabul kriteri 4):** Yeni `IVpnProxyDetector` portu + `TorExitNodeVpnDetector` (torproject.org bulk exit list, 1 saat cache, soft-fail) + `NoOpVpnProxyDetector` default. `UserLoginLog.HasVpnSignal` bool kolonu eklendi (06 §3.2 + migration `20260519103444_T83_AddUserLoginLogVpnSignal`). Pipeline her login'de detector'ı çağırır; bayrak `LoginAuditService` üzerinden persist edilir. **Sinyal hiçbir koşulda login'i bloke etmez.**
- **Integration spec güncellemesi:** 08 §10 "Geolocation ve VPN Sinyali" yeni section (provider seçimi + MMDB lifecycle + VPN sinyal kontratı + hata senaryoları + bağımlılık riski).
- **Ops runbook:** `Docs/INTEGRATION_RUNBOOKS/GEOIP_SETUP.md` — MaxMind hesap açma, MMDB indirme/cron, backend bağlama, doğrulama, sorun giderme, lisans notları.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/MaxMindCountryResolver.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/MaxMindCountryResolver.cs)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ChainedCountryResolver.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ChainedCountryResolver.cs)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/GeolocationSettings.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/GeolocationSettings.cs)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/IVpnProxyDetector.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/IVpnProxyDetector.cs) (`NoOpVpnProxyDetector` aynı dosyada)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/TorExitNodeVpnDetector.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/TorExitNodeVpnDetector.cs)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/VpnDetectionSettings.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/VpnDetectionSettings.cs)
- [backend/src/Skinora.Shared/Persistence/Migrations/20260519103444_T83_AddUserLoginLogVpnSignal.cs](../../backend/src/Skinora.Shared/Persistence/Migrations/20260519103444_T83_AddUserLoginLogVpnSignal.cs)
- [backend/tests/Skinora.Auth.Tests/Unit/MaxMindCountryResolverTests.cs](../../backend/tests/Skinora.Auth.Tests/Unit/MaxMindCountryResolverTests.cs)
- [backend/tests/Skinora.Auth.Tests/Unit/ChainedCountryResolverTests.cs](../../backend/tests/Skinora.Auth.Tests/Unit/ChainedCountryResolverTests.cs)
- [backend/tests/Skinora.Auth.Tests/Unit/TorExitNodeVpnDetectorTests.cs](../../backend/tests/Skinora.Auth.Tests/Unit/TorExitNodeVpnDetectorTests.cs)
- [backend/tests/Skinora.Auth.Tests/TestData/GeoIP2-Country-Test.mmdb](../../backend/tests/Skinora.Auth.Tests/TestData/GeoIP2-Country-Test.mmdb) — MaxMind public test fixture, Apache 2.0
- [backend/tests/Skinora.Auth.Tests/TestData/README.md](../../backend/tests/Skinora.Auth.Tests/TestData/README.md) — Lisans + amaç notu
- [Docs/INTEGRATION_RUNBOOKS/GEOIP_SETUP.md](../INTEGRATION_RUNBOOKS/GEOIP_SETUP.md) — Ops runbook

**Düzenlenen:**
- [backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj](../../backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj) — `MaxMind.GeoIP2 5.3.0` paketi (transitif olarak `Microsoft.Extensions.Options 9.x` ile uyumlu)
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ILoginAuditService.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ILoginAuditService.cs) — `hasVpnSignal` parametresi eklendi
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/LoginAuditService.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/LoginAuditService.cs) — `HasVpnSignal` UserLoginLog'a yazılır
- [backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamAuthenticationPipeline.cs](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamAuthenticationPipeline.cs) — `IVpnProxyDetector` enjekte edildi, happy-path'te `IsVpnOrProxyAsync` çağrılır + audit servisine geçer
- [backend/src/Modules/Skinora.Users/Domain/Entities/UserLoginLog.cs](../../backend/src/Modules/Skinora.Users/Domain/Entities/UserLoginLog.cs) — `HasVpnSignal bool` alanı
- [backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserLoginLogConfiguration.cs](../../backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserLoginLogConfiguration.cs) — Required + DEFAULT 0
- [backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs](../../backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs) — Chain factory + fail-closed DI swap (MMDB yok → header-only; `VpnDetection:Enabled=false` → NoOp)
- [backend/src/Skinora.API/appsettings.json](../../backend/src/Skinora.API/appsettings.json) — `Geolocation` + `VpnDetection` sectionları
- [backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — `HasVpnSignal` model snapshot
- [backend/tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj](../../backend/tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj) — TestData mmdb content copy
- [backend/tests/Skinora.Auth.Tests/Unit/SteamAuthenticationPipelineTests.cs](../../backend/tests/Skinora.Auth.Tests/Unit/SteamAuthenticationPipelineTests.cs) — `Mock<IVpnProxyDetector>` + yeni 1 test (VPN sinyali persist)
- [backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs](../../backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs) — `FakeVpnProxyDetector` + 2 yeni integration test (HasVpnSignal true/false persist)
- [Docs/06_DATA_MODEL.md](../06_DATA_MODEL.md) — §3.2 `HasVpnSignal` field satırı
- [Docs/08_INTEGRATION_SPEC.md](../08_INTEGRATION_SPEC.md) — §10 yeni section + içindekiler tablosu

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | IP adresinden coğrafi konum tespiti | ✓ | `MaxMindCountryResolver` + `ChainedCountryResolver` zinciri; MMDB yokken header katmanı çalışır, MMDB ile gerçek IP→ülke lookup. `MaxMindCountryResolverTests` 7/7 PASS (3 known IP + 4 fail-open path) |
| 2 | Yasaklı bölge → bilgilendirme + erişim engeli | ✓ | T30'dan devralındı + regresyon doğrulandı. `AuthSteamEndpointTests.Callback_VpnSignalDetected_StillSucceeds...` yeni testler T30 outcome'unu kırmadığını gösterir; `SettingsBasedGeoBlockCheck` `Blocked(country)` outcome'u `AuthController` `error=geo_blocked` redirect'i ile FE bilgilendirme sayfasına gider (07 §4.3) |
| 3 | Yasaklı ülke listesi admin yönetilebilir | ✓ | T30'dan devralındı — `auth.banned_countries` SystemSetting + `SystemSettingsValidator` ISO-3166-1 alpha-2 / NONE format kontrolü + `PUT /api/v1/admin/settings/{id}` (07 §9.16 + MANAGE_SETTINGS yetkisi) |
| 4 | VPN/proxy tespiti destekleyici sinyal (tek başına engelleme değil) | ✓ | `IVpnProxyDetector` + `TorExitNodeVpnDetector` + `UserLoginLog.HasVpnSignal` kolonu. Pipeline detector'ı her login'de çağırır ancak **outcome'u etkilemez** — `Callback_VpnSignalDetected_StillSucceedsAndPersistsFlag` testi bunu kanıtlar (HasVpnSignal=true + status=new_user redirect aynı response'ta). 02 §21.1 "destekleyici sinyal — tek başına engelleme sebebi değil" madde madde karşılanır |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit — Auth | ✓ 115/115 PASS | `dotnet test tests/Skinora.Auth.Tests` — 9 yeni T83 testi (5 ChainedCountryResolver + 7 MaxMindCountryResolver + 9 TorExitNodeVpnDetector + 1 pipeline VPN signal + 6 mevcut testin `RecordLoginAsync` 5-arg uyumu) |
| Unit — Shared | ✓ 373/373 PASS | Regresyon, 0 yeni test |
| Unit — Platform | ✓ 113/113 PASS | Regresyon (SystemSettingsValidator + seed) |
| Unit — Notifications | ✓ 86/86 PASS | Regresyon |
| Unit — Realtime | ✓ 25/25 PASS | Regresyon |
| Unit — Users | ✓ 16/16 PASS | Regresyon |
| Integration — API | ✓ 417/417 PASS | AuthSteamEndpointTests 10/10 (2 yeni VPN signal entegrasyonu, mevcut 8 + 2) |
| Integration — Steam | ✓ 33/33 PASS | Regresyon |
| Integration — Fraud | ✓ 62/62 PASS | UserLoginLog yapısal değişiklik regresyonsuz |
| Integration — Disputes | ✓ 25/25 PASS | Regresyon |
| Integration — Transactions | ✓ 641/641 PASS | Regresyon |
| **Toplam** | **✓ 1906/1906 PASS** | |
| dotnet build -c Release | ✓ 0W/0E | 42.5 saniye |
| dotnet format --verify-no-changes | ✓ Δ=0 | exit 0 |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS bağımsız validator (2026-05-19, ayrı chat) |
| Bulgu sayısı | 0 S-bulgu, 0 advisory |
| Düzeltme gerekli mi | Hayır |

**Validator kanıtları:**
- 4/4 kabul kriteri ✓ (1 MaxMind+Chained resolver IP→ülke lookup; 2 T30'dan devralındı + regresyon temiz; 3 T26+T30+T63 admin SystemSetting endpoint zinciri; 4 supportive only — `Callback_VpnSignalDetected_StillSucceedsAndPersistsFlag` Redirect+HasVpnSignal=true).
- 1/1 doğrulama kontrol listesi ✓ (02 §21.1 dört madde tam karşılandı).
- Yeniden çalıştırılan testler: Auth T83 filter 29/29 (`MaxMind` 6 + `Chained` 5 + `TorExit` 9 + `Pipeline` 9 dahil), API `AuthSteamEndpointTests` 10/10, Build Release 0W/0E (12s), dotnet format Δ=0.
- Task branch CI HEAD `40e47f6` run [`26093016874`](https://github.com/turkerurganci/Skinora/actions/runs/26093016874) **10/10 SUCCESS** (Detect+Guard skipped+Lint+Build+Unit+Integration+Contract+Migration+Docker backend+CI Gate).
- Main CI startup 3/3 success ([`26090069234`](https://github.com/turkerurganci/Skinora/actions/runs/26090069234) + [`26090069233`](https://github.com/turkerurganci/Skinora/actions/runs/26090069233) + [`26084081478`](https://github.com/turkerurganci/Skinora/actions/runs/26084081478)).
- Mini güvenlik temiz (license key ops-only env, MaxMind.GeoIP2 5.3.0 Apache 2.0 first-party, Tor exit list public/no-auth/soft-fail, fail-open misconfiguration, IPAddress.TryParse + 2-char ISO normalize + Tor parse defensive).
- Doküman uyumu tam (02 §21.1 + 03 §11a.1 + 06 §3.2 `HasVpnSignal` + 08 §10 yeni section + GEOIP_SETUP.md runbook kodla birebir).
- Rapor uyumu: Bağımsız validator verdict ile yapım raporu **tam uyumlu** — kabul kriteri matrisi, test isimleri, migration adı, K1–K8 forward-deferred sınırları eşleşiyor.

## Altyapı Değişiklikleri

- **Migration:** `20260519103444_T83_AddUserLoginLogVpnSignal` — `UserLoginLogs.HasVpnSignal bit NOT NULL DEFAULT 0`. Tek sütun ekleme, idempotent. CI fresh DB rehearsal'da T30 zincirine eklenir.
- **Config/env değişikliği:** `appsettings.json` — yeni `Geolocation:DatabasePath` (default boş) + `VpnDetection:{Enabled,TorExitListUrl,CacheDurationMinutes,RefreshTimeoutSeconds}` (default kapalı/torproject/60/10). Production deploy MaxMind MMDB için `Geolocation__DatabasePath` env değişkenini set eder (`GEOIP_SETUP.md` §3).
- **Docker değişikliği:** Yok — MMDB volume mount runtime config, base image değişmez.
- **Yeni dış dep:** `MaxMind.GeoIP2 5.3.0` (Apache 2.0, transitive `MaxMind.Db 4.2.0` + `Microsoft.Extensions.Options 9.0.0` ile uyumlu). T78/T79/T80 raw HttpClient paterni Tor list için korunur (yeni NuGet yok). Test fixture `MaxMind-DB` public test mmdb (Apache 2.0) — `tests/Skinora.Auth.Tests/TestData/` altında, 19KB.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok. MMDB lisans key kodda saklanmaz (ops `.env`/Vault'tan okur). Test mmdb production credential içermez (public test data).
- **Auth/authorization:** Geo-block etkisi T30'dan devralındı; T83 yeni endpoint eklemiyor. `MANAGE_SETTINGS` admin yetkisi `auth.banned_countries` için zaten gerekli (T26+T30).
- **Input validation:** Tor exit list parsing constrained — `IPAddress.TryParse` geçirenler eklenir, yorumlar + boş satırlar skip. MaxMind `IPAddress.TryParse` ön-validasyon + exception swallow ile fail-open. `Geolocation:DatabasePath` file existence check fail-closed.
- **Yeni dış bağımlılık:** `MaxMind.GeoIP2` (Apache 2.0, well-maintained, MaxMind first-party). Tor exit list public/no-auth, soft-fail.

## Commit & PR

- Branch: `task/T83-geo-block-service`
- HEAD: `40e47f6` ("T83: update status with PR #126 link") + `e712ab3` ("T83: Geo-block servisi — real IP geolocation + VPN supportive signal")
- PR: [#126](https://github.com/turkerurganci/Skinora/pull/126) MERGEABLE
- CI: ✓ run [`26093016874`](https://github.com/turkerurganci/Skinora/actions/runs/26093016874) **10/10 SUCCESS**

## Known Limitations / Follow-up

- **K1 — `Geolocation:DatabasePath` production deploy gerektirir:** Default boş, MMDB yoksa header-only fallback. Production'da `GEOIP_SETUP.md §2-§3` adımları ops sorumluluğundadır. Backend startup info log "header-only resolution" satırı izleme uyarısı.
- **K2 — `VpnDetection:Enabled` default kapalı:** T78/T79/T80 `Provider=logging` patterni — production'da `VpnDetection__Enabled=true` set edilir. MVP gözlemlemek için fraud module entegrasyonu T-future (HasVpnSignal kolonu hazır).
- **K3 — MaxMind MMDB redistribution kısıtı:** Repo'ya commit edilmez (license terms). Ops ayda 1 cron ile günceller (`GEOIP_SETUP.md §6`). Backend restart yeni MMDB için gerekli.
- **K4 — VPN detection scope:** Sadece Tor exit node listesi (free, public). Datacenter ASN / commercial VPN provider listeleri T-future genişletme. 02 §21.1 "destekleyici sinyal" MVP kabul kriteri karşılanır.
- **K5 — Tor list cache stale-on-failure:** Refresh fail olduğunda eski snapshot sunulmaya devam eder (better-stale-than-locked-out). Network outage süresince yeni eklenen exit nodelar yakalanmaz; soft-fail davranışı 02 §21.1 "tek başına engelleme değil" gereksiniminde sorun değil.
- **K6 — `HeaderCountryResolver` öncelikli olarak Cloudflare/CloudFront varsayar:** Self-hosted edge `X-Country-Code` header'ı set etmiyorsa MaxMind katmanına düşer (MMDB varsa). Hiçbir katman ülke çözemezse geo-block fail-open (T30 semantiği, INSTRUCTIONS.md §3.6 "süreç tıkanmazı engelle"). Bu davranış doc'lu (08 §10.4).
- **K7 — `auth.banned_countries` UI surface:** 04 §8.6 admin S17 ekranı T84+ frontend kapsamında. Backend endpoint hazır (`PUT /api/v1/admin/settings/{id}`).
- **K8 — Test mmdb fixture 19KB ek repo boyutu:** MaxMind public test data Apache 2.0, vendored. Lisans `tests/Skinora.Auth.Tests/TestData/README.md` not edildi.

## Notlar

### Çalışma akışı kanıtları

**Adım -1 (Working tree hygiene):** `git status` → clean (main).

**Adım 0 (Main CI startup check):** 3 son main run hepsi `success`:
- `26090069234` ("T82: Sanctions screening servisi + admin liste yönetimi (#125)")
- `26090069233` (Docker push aynı)
- `26084081478` ("docs: T82 sanctions spec …")

**Dış varsayım doğrulama (Adım 4):**

| Varsayım | Doğrulama |
|---|---|
| `MaxMind.GeoIP2` .NET NuGet paketi (Apache 2.0) | `curl https://api.nuget.org/v3-flatcontainer/maxmind.geoip2/index.json` → 5.4.1 latest, 5.3.0 .NET 9 Options 9.0.0 transitif uyumlu (4.1 Options 10.0.0 net10 preview-only). 5.3.0 seçildi |
| GeoLite2-Country MMDB MaxMind hesap + license key gerektirir | https://www.maxmind.com/en/geolite2/signup ücretsiz, EULA → redistribution kısıtlı, repo'ya commit edilmez |
| MaxMind public test mmdb (Apache 2.0) | https://github.com/maxmind/MaxMind-DB/blob/main/test-data/GeoIP2-Country-Test.mmdb erişilebilir, 19KB |
| Tor exit list endpoint | `curl -I https://check.torproject.org/torbulkexitlist` → 200 + ETag + Last-Modified (cacheable). Public, no auth |
| 08 spec gap (geolocation provider section yok) | `grep -n "^##" Docs/08_INTEGRATION_SPEC.md` → §1-§9; §10 olarak eklenecek (T81 §7 precedent), §8 (bağımlılık riski) numarası korunur (07 §8 referansları kırılmaz) |

**Kullanıcı onayı (2026-05-19):**
- Provider seçimi: **MaxMind GeoLite2 embedded MMDB** (Recommended) ✓
- VPN sinyal yerleşim: **UserLoginLog.HasVpnSignal kolonu** (migration) (Recommended) ✓
- VPN scope: **Tor exit list (torproject.org)** (Recommended) ✓

### Tasarım kararları

1. **`ChainedCountryResolver` paterni** — strict precedence chain (header→MaxMind→null). Cloudflare CF-IPCountry kullanıcıya en yakın "truth" olduğu için ilk; MMDB self-hosted fallback. T30 testleri kırılmadan zincire yeni katman eklendi.
2. **Fail-closed default + fail-open semantics ayrımı** — Provider seçimi (`Geolocation:DatabasePath` boş ise MMDB kayıt edilmez, `VpnDetection:Enabled=false` ise NoOp) **fail-closed default** (T78/T79/T80 paterni). Ama login akışı içinde resolver/detector başarısız olursa **fail-open** (kullanıcı engellenmez) çünkü 02 §21.1 destekleyici sinyal + misconfiguration kullanıcıyı kilitlememeli (T30 semantiği).
3. **Tor list cache `stale-better-than-locked-out`** — Refresh fail olduğunda eski snapshot sunulur (CDN-style). Detector zaten "supportive signal" olduğu için doğruluk yerine availability tercih edilir.
4. **`UserLoginLog.HasVpnSignal` kolonu vs. AuditLog event** — Kolon tercih edildi çünkü 04 §8.6 admin S17 "risk skorlama ve flag değerlendirmesi" için query-friendly bir alan gerekiyor. AuditLog append-only + index'siz, fraud module konsumu pahalı. Kolon `bool NOT NULL DEFAULT 0` zero-migration-risk (mevcut row'lar otomatik false).
5. **08 §10 placement** — Doc sonuna eklendi (yeni section), §8 (Bağımlılık Risk Matrisi) ve §9 (Ortam Konfigürasyonu) referansları kırılmadı. 07 §8 referansları "08 §8" mevcut riskmatrisini gösterir, T83 değişikliği yok.

### MaxMind paket versiyon pivot'u

İlk denemede `MaxMind.GeoIP2 5.4.1` seçildi (latest stable). Release build'de NU1605 downgrade hatası: 5.4.1 → `Microsoft.Extensions.Options [10.0.0, )` (net10 preview), Skinora.Shared zincirinde `9.0.3` pinned. `5.3.0`'a düşüldü, transitif olarak `Options [9.0.0, )` ile uyumlu (`net9.0` target group). T11 dersi: paket versiyonu seçimi pre-flight doğrulanmalı — bu kez build-time yakalandı (savunma katmanı). Test mmdb hatalı path'e yazılmıştı (`/c/projects/Escrow/backend/backend/...`); düzeltildi.

### F4 ilerleme

T83 tamamlandığında F4 (Entegrasyonlar) tüm taskları (T64–T83) ✓ olur. Sırada F4 Gate Check.
