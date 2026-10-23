# T171a — Ortak sırlar Docker secret (⚠ GÜVENLİK)

**Faz:** F9 | **Durum:** ⏳ Yapım bitti — doğrulama bekliyor | **Tarih:** 2026-10-24

T171 (Kurulum günü) kabul kriterindeki "ortak sırlar env yerine secret dosyasından okunuyor" maddesinin **kod parçası**, kurulum gününden önce. Kaynak: DEFERRED_BACKLOG `SharedSecretsInPlainEnv` (T163 değerlendirmesi + T165 eki); desen T163'ün imza sırları (`T163_REPORT.md`, DEPLOY_RUNBOOK §L, `secrets/README.md`).

---

## Yapılan İşler

- **Kapsanan sırlar (koddan çıkarıldı — 12):**
  - Satırın altı sırrı: JWT imza anahtarı (`Jwt:Secret`), TRON webhook sırrı (`Webhook:BlockchainSharedSecret` ↔ TRON `WEBHOOK_SECRET`), Ethereum webhook sırrı (`Webhook:EthereumSharedSecret` ↔ Ethereum `WEBHOOK_SECRET`), üç iç anahtar (`SteamSidecar:` / `BlockchainSidecar:` / `EthereumSidecar:InternalKey` ↔ her sidecar'ın `INTERNAL_KEY`'i).
  - Satırın T165 eki: `Resend:ApiKey`, `Resend:WebhookSigningSecret`, `Telegram:BotToken` (backend + Grafana render adımı), `Telegram:WebhookSecretToken`, Grafana SMTP parolası, heartbeat ping URL'si.
  - Ek (koddan bulundu): `Jwt:PreviousSecret` (döndürme anahtarı) — backend dosyadan okur ve Production'da değişken olarak reddeder; compose varsayılan olarak bağlamaz (karar K3).
- **Backend:** `Skinora.API/Configuration/SharedSecretFiles.cs` — `Program.cs`'te `CreateBuilder`'ın hemen ardından, hiçbir modül sırrı okumadan önce çalışır. Her sırrı `{SharedSecrets:Directory}/<ad>` (varsayılan `/run/secrets`) dosyasından okur ve **bütün diğer kaynakların üstüne** koyar. `Development` dışında: env değişkeni ya da komut satırı (`EnvironmentVariablesConfigurationProvider` / `CommandLineConfigurationProvider`, `ASPNETCORE_`/`DOTNET_` önekleri dahil) → `SHARED_SECRET_IN_ENV`; zorunlu dosya yok/boş → `SHARED_SECRET_MISSING`; 32'den kısa → `SHARED_SECRET_TOO_SHORT`; dosyanın yerinde dizin/izin hatası → `SHARED_SECRET_UNREADABLE`; isteğe bağlı sır dosyasızsa boşaltılır (appsettings `REPLACE_IN_ENV` yer tutucuları ve `REPLACE_IN_ENV_minimum_32_characters` JWT yer tutucusu üretime ulaşmaz). Bütün sorunlar tek istisnada, değer yazmadan. Development'ta env/appsettings yolu çalışır, dosya varsa kazanır, dosya + değişken `SHARED_SECRET_AMBIGUOUS`. Açılış satırı yalnız kaynakları yazar.
- **TRON servisi:** `config/sharedSecrets.ts` (iç anahtar + webhook sırrı; `INTERNAL_KEY_FILE` / `WEBHOOK_SECRET_FILE`); env yolu yalnız `NODE_ENV` tam `development`/`test`; webhook sırrı artık **zorunlu** (önceden boşsa açılıp her gönderimde reddediyordu → `WEBHOOK_SECRET_MISSING` / `_TOO_SHORT`, karar K2). `config` nesnesinden sırlar çıkarıldı; `WebhookClient` sırrı `configureWebhookSecret` ile alır.
- **Ethereum servisi:** `config/sharedSecrets.ts` + `loadConfig`: iç anahtar ve webhook sırrı dosyadan; webhook boş kalabilir (T151 davranışı — `acceptsPayments=false`), doluysa ≥ 32 (`WEBHOOK_SECRET_TOO_SHORT`).
- **Steam servisi:** `config/sharedSecrets.ts` + `config/secretFile.ts` (TRON'dan kopya): iç anahtar dosyadan.
- **Compose:** taban — her sır **tek** top-level secret, backend + karşı uçtaki tek servise; Grafana `GF_SMTP_PASSWORD__FILE`; render adımı bot token'ını dosyadan okur (yerinde dizin → `TELEGRAM_BOT_TOKEN_UNREADABLE`, Grafana açılmaz). Deneme yayını — on iki dosya `secrets/trial/`'dan, heartbeat `HEARTBEAT_URL_FILE`. e2e — Production backend'e sabit test değerleri satır içi `configs` ile `/run/secrets/<ad>`'a (iç anahtarlar 32 karaktere uzatıldı). Hiçbir servis bu sırları env olarak almıyor.
- **Heartbeat betiği:** `HEARTBEAT_URL_FILE` desteği (`HEARTBEAT_URL_UNREADABLE`, `HEARTBEAT_URL_AMBIGUOUS`; URL yazdırılmaz).
- **Belgeler:** `plan/11_F9.md` v1.30 (T171a görevi + T171 atfı); IMPLEMENTATION_STATUS; DEPLOY_RUNBOOK yeni **§L.9** (dosya tablosu, kurallar, doğrulama, kurulum günü altı adım, geliştirme ortamından geçiş, döndürme, ölçüm, sınırlar) + §0/§B/§G.1/§G.6/§J/§K.3/§K.4/§L.1/§L.8/§O #12a/§P; 05 §3.5 (v3.40); `secrets/README.md` "Ortak sırlar" (sıfırdan kurulum + `.env`'den geçiş komutları, değer yazdırmadan); `.env.example` (sırlar kalktı, dosya yönlendirmesi); DOC_CHANGELOG; DEFERRED_BACKLOG `SharedSecretsInPlainEnv` ✅ + yeni 🟡 `ThirdPartyCredentialsInPlainEnv`.
- **CI:** `scripts` filtresine `docker-compose.e2e.yml`, `SharedSecretFiles.cs`, `sidecar-*/src/config/sharedSecrets.ts`, `e2e/src/config.ts` (yeni compose testi bunları okur).

## Etkilenen Modüller / Dosyalar

- Backend: `src/Skinora.API/Configuration/SharedSecretFiles.cs` (yeni), `src/Skinora.API/Program.cs`; testler `Unit/Configuration/SharedSecretFilesTests.cs` (yeni), `Integration/SharedSecretFilesHostTests.cs` (yeni), `Common/SharedSecretTestDirectory.cs` (yeni), `Integration/DiagnosticsEnvironmentGateTests.cs` (Production istemcisine sır dizini).
- TRON: `src/config/sharedSecrets.ts` (+test), `src/config/index.ts`, `src/config/internalKey.ts` (mesaj), `src/index.ts`, `src/webhook/WebhookClient.ts` (+test yeni), `src/config/signingSecretsStartup.test.ts`, `src/config/startupWiring.test.ts`.
- Ethereum: `src/config/sharedSecrets.ts` (yeni), `src/config/index.ts`, `src/index.ts`, `src/config/index.test.ts`, `src/config/startupWiring.test.ts`, `src/config/composeWiring.test.ts`.
- Steam: `src/config/sharedSecrets.ts` (+test), `src/config/secretFile.ts` (yeni), `src/config/index.ts`, `src/config/internalKey.ts`, `src/index.ts`, `src/config/startupWiring.test.ts`.
- Compose/altyapı: `docker-compose.yml`, `docker-compose.trial.yml`, `docker-compose.e2e.yml`, `.env.example`, `scripts/heartbeat/heartbeat.sh` (+test), `scripts/compose-secrets.test.mjs` (yeni), `scripts/compose-exposure.test.mjs`, `infra/grafana/provisioning/alerting/contactpoints.yml` (yorum), `.github/workflows/ci.yml` (filtre).
- Belgeler: yukarıdaki liste + `Docs/BYPASS_LOG.md` (pre-commit sır koruması: kod tanımlayıcıları ve `test-`/`e2e-` önekli sabit test değerleri yanlış pozitif — her commit gerekçeyle kayıtlı).

## Kabul Kriterleri Kontrolü

| # | Kriter | Durum | Kanıt |
|---|---|---|---|
| 1 | Plan: `11_F9.md`'de T171'den önce T171a satırı (kriterler, kaynak, T171 atfı); STATUS satırı | ✓ | `plan/11_F9.md` v1.30; IMPLEMENTATION_STATUS `T171a` satırı |
| 2a | Backend ve sidecar'larda her ortak sır dosyadan | ✓ | `SharedSecretFiles.Specs` (11 anahtar), üç `sharedSecrets.ts`; host testi dosya değerinin `IOptions<JwtSettings>` ve `IConfiguration`'a ulaştığını gösterir |
| 2b | Production/Staging'de env yolu reddedilir | ✓ | `Every_Environment_But_Development_Closes_The_Variable_Path` (Production, Staging, `production`, `Developmentt`), komut satırı testi; sidecar süreç testleri `NODE_ENV=production` ile `SHARED_SECRET_IN_ENV`; konteyner ölçümü (b) |
| 2c | Dosya yok/boş/kısa → açılış durur (≥ 32 korunur) | ✓ | Host testleri (Production: dört zorunlu dosya tek tek eksik, boş, 31 karakter, dizin), sidecar süreç testleri (TRON 7 durum, Ethereum 5, Steam 4) |
| 2d | Sabit zamanlı karşılaştırma korunur | ✓ | Karşılaştırma koduna dokunulmadı (`WebhookSignatureMiddleware`, `TelegramWebhookSignatureMiddleware`, sidecar `internalKeyAuth`); yalnız değerin kaynağı değişti |
| 2e | Sırlar log'a/yanıta sızmaz | ✓ | Hata mesajı / açılış satırı testleri değer içermediğini doğrular (backend 3 test, TRON/Ethereum/Steam süreç çıktıları); konteyner ölçümünde JWT değeri log'da 0 kez |
| 3a | Compose (taban, deneme, e2e): yalnız gereken servislere | ✓ | `scripts/compose-secrets.test.mjs` — derlenmiş modelden tam servis→secret haritası, iki ucun aynı top-level secret'ı/dosyası, env'de sır yok, `/run/secrets/<ad>` hedefi, deneme `secrets/trial/`, e2e değerleri harness + fake ile aynı |
| 3b | `.env.example`, deneme env şablonu, `secrets/README.md`, DEPLOY_RUNBOOK (T171 adımları + geçiş), 05 | ✓ | Deneme yayınının env şablonu `.env.example`'dır (DEPLOY_RUNBOOK §K.4 adım 1) — aynı dosya; §L.9 + §K.4 adım 2; 05 §3.5 |
| 4a | Açılış testleri (yok/boş/kısa → açılmaz; Production'da env reddi; Development'ta env çalışır) | ✓ | Aşağıda "Test Sonuçları" |
| 4b | Compose testleri | ✓ | `compose-secrets.test.mjs` 11 test |
| 4c | Kritik satırları bozup yakalandığını say | ✓ | 30 bozma: **29 yakalandı**, 1 yaşadı (aşağıda) |
| 5 | Rapor, STATUS, backlog ✅ (sayaç dosyadan), MEMORY | ✓ | Bu dosya; backlog **47 aktif / 173 çözülmüş** (dosyanın `awk`'iyle; main 47/172 → `SharedSecretsInPlainEnv` ✅ + yeni 🟡 `ThirdPartyCredentialsInPlainEnv`); `.claude/memory/MEMORY.md` |

### Konteyner ölçümü (2026-10-24, bu PC, Docker Desktop + Compose v5.1.0)

Bu dalın backend imajı, ayrı compose projesi (`t171a-probe`), Production, DB'siz:

| Durum | Sonuç |
|---|---|
| (a) dört zorunlu sır dosyadan | Açılış satırı: `Directory=/run/secrets`, `EnvironmentPath=closed`, `Jwt:Secret=file, Webhook:BlockchainSharedSecret=file, SteamSidecar:InternalKey=file, BlockchainSidecar:InternalKey=file`, diğerleri `none`. JWT değeri log'da **0** kez |
| (b) aynı + `Jwt__Secret` değişkeni | `SHARED_SECRET_IN_ENV: Jwt:Secret (/run/secrets/jwt_secret)`, konteyner sıfırdan farklı kodla çıktı |
| (c) `jwt_secret.txt` silindi | Docker host'ta `jwt_secret.txt/` **dizini** açtı → `SHARED_SECRET_UNREADABLE: Jwt:Secret` |

Grafana render adımı (`t171a-render` projesi, `.env.example` ile): dosya yokken Docker'ın açtığı dizinde `TELEGRAM_BOT_TOKEN_UNREADABLE`, çıkış **1**; dosya varken `grafana provisioning rendered`, token `contactpoints.yml`'deki üç `bottoken` alanına yazıldı. İmaj, volume ve geçici dosyalar silindi.

### Bozma (kritik satırları bozup testleri koşma)

| # | Bozma | Sonuç |
|---|---|---|
| B1–B10 | Backend: env yolu her ortamda açık · env reddi kaldırıldı · uzunluk tabanı · zorunlu kontrolü · isteğe bağlı boşaltma · dizin kontrolü · değerlerin katmanlanmaması · komut satırının izlenmemesi · belirsizlik izni · `Program.cs` çağrısı | 10/10 yakalandı |
| T1–T5 | TRON: env reddi · webhook yok izni · webhook kısa izni · okunamayanın yutulması · `configureWebhookSecret` çağrısının silinmesi | 5/5 (T5 ilk turda **yaşadı** → `WebhookClient.test.ts` eklendi, yeniden koşuda yakalandı) |
| E1–E3 | Ethereum: env reddi · kısa webhook izni · webhook'un yeniden env'den okunması | 3/3 |
| S1–S2 | Steam: env reddi · belirsizlik izni | 2/2 |
| C1–C7 | Compose: Steam'e `INTERNAL_KEY` değişkeni · TRON'dan webhook mount'u düşmesi · JWT'nin frontend'e bağlanması · deneme JWT'sinin geliştirme dosyasından gelmesi · e2e JWT'nin harness'tan farklılaşması · Grafana SMTP parolasının değişkene dönmesi · heartbeat URL'sinin değişkene dönmesi | 7/7 |
| H1 | Heartbeat dizin kontrolü | 1/1 |
| C8 | Grafana render adımının okunamaz-dosya koşulu (`if false`) | **Yaşadı** — compose testi yalnız koşulun metnini görür; davranışı yukarıdaki Docker ölçümüyle doğrulandı. Komutu testte çalıştırmak (`rm -rf /out/*` içerir) yol değiştirme riski taşıdığı için yapılmadı |

**Toplam: 30 bozma, 29 yakalandı, 1 yaşadı.**

## Test Sonuçları

| Komut | Sonuç |
|---|---|
| `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~SharedSecretFiles\|FullyQualifiedName~DiagnosticsEnvironmentGateTests"` | 33/33 |
| `dotnet test tests/Skinora.API.Tests` (tam, LocalDB) | __APITESTS__ |
| `sidecar-blockchain: npx vitest run` | 41 dosya, 813/813 (+ `WebhookClient.test.ts` 3/3 sonradan) |
| `sidecar-ethereum: npx vitest run` | 37 dosya, 949/949 |
| `sidecar-steam: npx vitest run` | 13 dosya, 178/178 |
| `node --test scripts/compose-secrets.test.mjs scripts/compose-exposure.test.mjs` | 26/26 |
| `node --test scripts/heartbeat/heartbeat.test.mjs` | 11/11 |
| `tsc --noEmit` (üç sidecar), `eslint` (değişen dosyalar), `prettier --check` (değişen dosyalar, LF) | temiz |
| `docker compose -f docker-compose.e2e.yml config` / taban `--profile ethereum` render | temiz |

Yerel prettier'ın bütün dosyalarda verdiği uyarılar `core.autocrlf` CRLF artifaktıdır (CI LF'te koşar).

## Doğrulama

Bekliyor (yönetici düzeni: CI'ı yönetici izler).

## Altyapı Değişiklikleri

- **Geliştirme ortamı sözleşmesi değişti:** `docker compose -f docker-compose.yml up` artık `secrets/` altında on bir ortak sır dosyasının **var olmasını** ister (backend hepsini bağlar; eksik olan Docker'ın açtığı dizinle backend'i durdurur). Geçiş: `secrets/README.md` "Mevcut bir geliştirme `.env`'inden geçiş" (değer yazdırmadan; eski `WEBHOOK_SECRET` / tek `INTERNAL_KEY` geri düşüşleri orada bir kez çözülür).
- **TRON servisi** webhook sırrı olmadan artık açılmaz (önceden açılıp her ödeme bildiriminde reddediyordu).
- **e2e:** backend iç anahtarları 27 → ≥ 32 karakter (yeni taban); fake sidecar değişmedi (iç anahtar kontrolü yok, webhook sırlarını env'den okur — test ikizi).

## Commit & PR

- Dal: `task/T171a-ortak-sirlar-secret` (origin/main `51faec90`'dan)
- PR: #__PR__
- Commit'ler: `53f305de` backend · `b8d79d86` TRON · `8ab3109d` Ethereum + Steam · `118511ab` compose + testler · `4a07662e` belgeler + WebhookClient testi · rapor/status commit'i
- CI: yönetici izliyor (task.md Bitiş Kapısı — yönetici düzeni istisnası)

## Kararlar (öneriyle kaydedildi — ⚠)

- **K1 — Kapsam satırın tamamı (12 sır):** görev metni "JWT, iki webhook, üç iç anahtar"ı sayıyordu; backlog satırının T165 eki aynı taşımaya dahildi ve satırın ✅ olabilmesi için gerekliydi (öneriyle kaydedildi — ⚠ GÜVENLİK).
- **K2 — TRON webhook sırrı zorunlu:** dosya yok/boş/kısa → açılmaz (görev: "dosya yoksa/boşsa/kısaysa açılış dursun"). Ethereum'unki boş kalabilir (T151 davranışı, Ethereum kapalı) ama doluysa ≥ 32 (öneriyle kaydedildi — ⚠).
- **K3 — `jwt_previous_secret` compose'da bağlı değil:** kullanılmayan bir sırrı konteynerde taşımamak için; döndürme sırasında backend'in `secrets:` listesine eklenir (DEPLOY_RUNBOOK §L.9) (öneriyle kaydedildi — ⚠ GÜVENLİK).
- **K4 — Backend env yolu yalnız `Development`:** `Staging`, `production` (küçük harf ASP.NET'te Production sayılır), yazım hataları kapalı; sidecar'larda T163'ün tam eşleşme kuralı (`development`/`test`). Development'ta 32 karakter tabanı yalnız dosya değerine uygulanır (`appsettings.Development.json`'daki `dev-internal-key` bozulmasın) (öneriyle kaydedildi — ⚠).
- **K5 — Komut satırı da env sayılır:** `ps` gösterdiği için Production'da `--Jwt:Secret=…` de reddedilir (öneriyle kaydedildi — ⚠ GÜVENLİK).
- **K6 — e2e sırları satır içi `configs` ile:** e2e backend'i Production'dır; dosya yaratmadan aynı `/run/secrets/<ad>` yolunu kullanmak için (Compose ≥ 2.23, deneme overlay'i zaten ≥ 2.24 ister). Fake sidecar test ikizi olduğu için env'de kaldı (öneriyle kaydedildi — ⚠).
- **K7 — Boş Telegram token'ı davranışı korundu:** render adımı boş token'ı yine boş yazar ve Grafana açılmaz (önceki env davranışıyla aynı; dış heartbeat bunu görür). Yer tutucuya düşmek Grafana'yı açar ama Telegram alarmlarını sessizce düşürürdü (öneriyle kaydedildi — ⚠).

## Proje sahibine açık sorular

- **⚠ SAHİBE SORU — `ThirdPartyCredentialsInPlainEnv` (🟡, ⚠ GÜVENLİK):** DB/Redis parolası (Redis'inki ayrıca komut satırında), Grafana yönetici parolası, `STEAM_API_KEY`, `TRON_API_KEY`, Ethereum RPC URL'leri ve üretimde kapalı Discord sırları hâlâ env'de (`docker inspect` gösterir). En güvenli seçenek (öneri): gerçek para girmeden (T173) aynı desenle dosyaya taşımak. Karar: T171 kurulum gününe mi, ayrı göreve mi, kabul edilen risk mi.

## Known Limitations / Follow-up

- C8 (render adımının okunamaz-dosya koşulu) yalnız Docker ölçümüyle sabit; otomatik test metni görür.
- Sırlar host'ta düz metin dosya (DEPLOY_RUNBOOK §L.8 "Disk" — disk şifrelemesi); `docker inspect` artık göstermez.
- TRON servisi 4xx'i kalıcı saydığı için döndürmede sıra önemli (önce karşı uç durdurulur — §L.9 "Döndürme").
- Fake sidecar (yalnız e2e) webhook sırlarını env'den okur; üretim yolunda değil.

## Notlar

- Pre-commit sır koruması `[A-Za-z_]*SECRET[A-Za-z_]*` genel kalıbıyla kod tanımlayıcılarına (`SharedSecretFiles`, `webhookSecret`) takılıyor; her commit `SKINORA_ALLOW_SECRET=1` + gerekçeyle geçti ve `Docs/BYPASS_LOG.md`'ye yazıldı — staged içerikte gerçek sır yok (test değerleri `test-`/`e2e-` önekli).
