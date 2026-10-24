# T171b — Üçüncü taraf kimlik bilgileri Docker secret (⚠ GÜVENLİK)

**Faz:** F9 | **Durum:** ⏳ Yapım bitti — doğrulama bekliyor | **Tarih:** 2026-10-24

Kaynak: proje sahibi kararı (2026-10-24, modal) — DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv` satırındaki sırlar **kurulum gününden (T171) önce** env yerine Docker secret dosyasından okunur. Desen T171a (`SharedSecretFiles`, sidecar `sharedSecrets.ts`, DEPLOY_RUNBOOK §L.9).

---

## Yapılan İşler

- **Kapsanan sırlar (satırdan ve koddan çıkarıldı — 10 dosya):**
  - Veritabanı `sa` parolası → `mssql_sa_password` (SQL Server, yedek konteyneri `skinora-db-backup`, backend bağlantı dizesi).
  - Redis parolası → `redis_password` (Redis — eskiden **komut satırında**, sağlık yoklaması `redis-cli -a` dahil; backend bağlantı dizesi; Steam servisinin `REDIS_URL`'i — satırda yoktu, koddan bulundu).
  - Grafana yönetici parolası → `grafana_admin_password`.
  - Steam Web API anahtarı → `steam_api_key` (Steam servisi + backend `SteamOpenId:WebApiKey`, aynı dosya).
  - TronGrid anahtarları → `tron_api_key`, `tron_api_key_secondary` (TRON servisi).
  - Ethereum RPC URL'leri (sağlayıcı anahtarı yolda) → `ethereum_rpc_primary_url`, `ethereum_rpc_backup_url` (Ethereum servisi).
  - Discord istemci sırrı + bot token'ı → `discord_client_secret`, `discord_bot_token` (backend okur; kanal üretimde kapalı olduğu için compose **bağlamaz** — K3).
  - Taranıp kapsam dışı bulunanlar: Cloudflare tünel token'ı (T161'den beri dosya), Uptime Kuma (env sırrı yok), e2e sahte servisinin `DB_PASSWORD`'ü (test ikizi — K6).
- **Backend:** `Skinora.API/Configuration/ThirdPartyCredentialFiles.cs` — `Program.cs`'te `SharedSecretFiles`'ın hemen ardından, aynı dizinden (`SharedSecrets:Directory`, varsayılan `/run/secrets`). İki tür: düz değer (Steam anahtarı, Discord) dosyadan okunup üste konur; **bağlantı dizesi parolası** (SQL Server, Redis) — dizenin geri kalanı env'den **parolasız** gelir, parola `SqlConnectionStringBuilder` / StackExchange.Redis `ConfigurationOptions` ile süreç içinde eklenir. `Development` dışında: değer env/komut satırından ya da **parolalı bir bağlantı dizesi** → `CREDENTIAL_IN_ENV`; zorunlu (DB, Redis) dosya yok/boş → `CREDENTIAL_MISSING`; dizin/izin hatası → `CREDENTIAL_UNREADABLE` (her ortamda); Redis parolasında `A-Z a-z 0-9 . _ ~ + = -` dışı karakter ya da çözülemeyen dize → `CREDENTIAL_INVALID` (dize mesaja yazılmaz); dosya + değişken → `CREDENTIAL_AMBIGUOUS`. İsteğe bağlılar dosyasızsa boşaltılır (appsettings `REPLACE_IN_ENV` yer tutucuları üretime ulaşmaz). Açılış satırı yalnız kaynakları yazar: `Third-party credentials loaded from /run/secrets (environment path closed): ConnectionStrings:DefaultConnection (password)=file, …`.
- **TRON servisi:** `config/providerCredentials.ts` (`TRON_API_KEY(_SECONDARY)`, `*_FILE`); `config` nesnesinden anahtarlar çıktı; `TronGridClient`'ın varsayılan anahtarları `configureTronApiKeys` ile giriş noktasından verilir (handler'ların ve sağlık yoklamasının anahtarsız kurduğu istemciler dahil). İkisi de isteğe bağlı kaldı.
- **Steam servisi:** `config/providerCredentials.ts` — Steam anahtarı dosyadan; `REDIS_URL` parolasız (`redis://skinora-redis:6379`), parola dosyadan URL'ye süreç içinde eklenir; üretimde URL'de parola `CREDENTIAL_IN_ENV`, izinsiz karakter / redis olmayan URL `CREDENTIAL_INVALID` (URL yazılmaz).
- **Ethereum servisi:** `config/providerCredentials.ts` + `loadConfig` — iki RPC URL'si dosyadan; birincil yoksa yine `RPC_NOT_CONFIGURED`.
- **Üçüncü taraf imajlar — yöntem ve gerekçe:**
  - **SQL Server: imajın kendi `MSSQL_SA_PASSWORD_FILE` desteği + giriş denetimi.** Ölçüldü (aşağıda): imaj bu değişkeni kendisi okur, sondaki LF/CRLF'i atar. Ama dosya yokken / yerinde dizin varken de **açılır** — boş volume'de master bilinmeyen bir sa parolasıyla kurulur ve sonradan düzeltilen dosya bir şey değiştirmez. Bu yüzden `scripts/db/mssql-entrypoint.sh` önce dosyayı denetler, sonra imajın `launch_sqlservr.sh`'ını değiştirmeden çalıştırır; sağlık yoklaması aynı betiğin `--healthcheck` kipi (`SQLCMDPASSWORD`).
  - **Redis: giriş betiği.** Resmî imajda `*_FILE` yok. `scripts/redis/redis-entrypoint.sh` `requirepass`'i tmpfs'teki (`/run/skinora-redis`) yalnız redis kullanıcısına açık yapılandırma dosyasına yazar; `redis-server` o dosyayla başlar; yoklama `REDISCLI_AUTH`.
  - **Grafana: imajın `GF_SECURITY_ADMIN_PASSWORD__FILE`'ı + `first-boot.sh` denetimi.** Grafana dosya yok/boşken `admin/admin` ile açılırdı; `first-boot.sh` değişkeni, eksik/boş/okunamayan dosyayı ve CR'yi reddeder.
  - `scripts/db-backup/db-backup.sh` parolayı `MSSQL_SA_PASSWORD_FILE`'dan okur; değişkeni reddeder.
- **Compose:** taban — sekiz yeni top-level secret, her biri yalnız kullanan servise; `MSSQL_SA_PASSWORD`, `--requirepass`, `redis-cli -a`, `GF_SECURITY_ADMIN_PASSWORD`, `STEAM_API_KEY`, `SteamOpenId__WebApiKey`, `TRON_API_KEY*`, `ETHEREUM_RPC_*_URL` hiçbir serviste yok. Deneme yayını — hepsi `secrets/trial/`'dan; `GRAFANA_ADMIN_PASSWORD:?` render kapısı dosya denetimine dönüştü. e2e — DB ve Redis gerçek giriş betikleriyle, parolalar satır içi `configs`; backend'in bağlantı dizeleri parolasız.
- **Belgeler:** `plan/11_F9.md` v1.31 (T171b görevi); IMPLEMENTATION_STATUS; DEPLOY_RUNBOOK §L.9 genişledi (yeni alt bölüm "Üçüncü taraf kimlik bilgileri": tablo, yöntem gerekçeleri, doğrulama, kurulum günü altı adımı, geliştirme ortamından geçiş, döndürme, sınırlar) + §0/§B/§G.0/§G.2/§K.1–§K.4/§L.8/§N.6/§O #12b; 05 §3.5 (v3.41); `secrets/README.md` (yeni "Üçüncü taraf kimlik bilgileri" bölümü, sıfırdan kurulum ve `.env`'den geçiş komutları, Windows CRLF notu); `.env.example`; `scripts/bootstrap/README.md`; DOC_CHANGELOG; DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv` ✅ + yeni ⚪ `MssqlSaPasswordFileUnpinned`.
- **CI:** `scripts` filtresine `ThirdPartyCredentialFiles.cs` ve `sidecar-*/src/config/providerCredentials.ts` (compose testi onları okur).

## Etkilenen Modüller / Dosyalar

- Backend: `src/Skinora.API/Configuration/ThirdPartyCredentialFiles.cs` (yeni), `src/Skinora.API/Program.cs`; testler `Unit/Configuration/ThirdPartyCredentialFilesTests.cs` (yeni), `Integration/ThirdPartyCredentialFilesHostTests.cs` (yeni), `Common/SharedSecretTestDirectory.cs` (Production host'ları için DB/Redis parola dosyaları).
- TRON: `src/config/providerCredentials.ts` (+test yeni), `src/config/index.ts`, `src/index.ts`, `src/tron/TronGridClient.ts` (+test), `src/config/signingSecretsStartup.test.ts`.
- Steam: `src/config/providerCredentials.ts` (+test yeni), `src/config/index.ts`, `src/index.ts`, `src/config/startupWiring.test.ts`.
- Ethereum: `src/config/providerCredentials.ts` (yeni), `src/config/index.ts`, `src/index.ts`, `src/config/index.test.ts`, `src/config/startupWiring.test.ts`, `src/config/composeWiring.test.ts`.
- Betikler: `scripts/db/mssql-entrypoint.sh` (+test yeni), `scripts/redis/redis-entrypoint.sh` (+test yeni), `scripts/grafana/first-boot.sh` (+test), `scripts/db-backup/db-backup.sh` (+test, `compose-backup.test.mjs`), `scripts/compose-secrets.test.mjs`, `scripts/compose-exposure.test.mjs`.
- Compose/altyapı: `docker-compose.yml`, `docker-compose.trial.yml`, `docker-compose.e2e.yml`, `.env.example`, `.github/workflows/ci.yml` (filtre).
- Belgeler: yukarıdaki liste + `Docs/BYPASS_LOG.md` (pre-commit sır koruması yanlış pozitifleri — her commit gerekçeyle kayıtlı).

## Kabul Kriterleri Kontrolü

| # | Kriter | Durum | Kanıt |
|---|---|---|---|
| 1 | Plan: `11_F9.md`'de T171a'dan sonra T171b (kriterler, kaynak); STATUS satırı | ✓ | `plan/11_F9.md` v1.31; IMPLEMENTATION_STATUS `T171b` satırı |
| 2a | Satırdaki bütün sırlar (tam liste koddan) dosyadan | ✓ | Yukarıdaki on dosya; backend `ThirdPartyCredentialFiles.Specs`, üç `providerCredentials.ts`, üç giriş denetimi; host testi dosya parolasının bağlantı dizelerine ulaştığını gösterir; konteynerde backend `/health` DB + Redis `healthy` |
| 2b | Env yolu yalnız Development/test; Production'da değişken (parolalı bağlantı dizesi dahil) → açılmaz | ✓ | `Every_Environment_But_Development_Closes_The_Variable_Path` (Production, Staging, `production`, `Developmentt`), `Production_Refuses_A_Sql_Connection_String_Variable_That_Carries_A_Password` (`Password`/`Pwd`/`password`), komut satırı testi; sidecar süreç testleri `NODE_ENV=production` ile `CREDENTIAL_IN_ENV`; betikler değişkeni her zaman reddeder; konteyner ölçümü #5, #8, #11, #12 |
| 2c | Dosya yok/boş/dizin → açılış durur (zorunlular) | ✓ | Host testleri (DB/Redis dosyası yok, boş, dizin), betik testleri (yok/boş/dizin/okunamaz), konteyner ölçümü #10 |
| 2d | SQL Server / Redis / Grafana: `*_FILE` varsa o, yoksa giriş betiği; seçim ve gerekçe | ✓ | Yukarıda "Üçüncü taraf imajlar"; DEPLOY_RUNBOOK §L.9; konteyner ölçümü #1, #2, #6, #7 |
| 2e | Değer log / `docker inspect` / komut satırı / hata mesajında görünmez | ✓ | `Production_Startup_Logs_Never_Carry_A_Credential_Value` (gerçek `Program.cs` açılışının bütün log satırları), hata mesajı testleri, betik testleri (argv'de ve çıktıda parola yok); konteyner ölçümü #4, #6, #9, #12 |
| 3a | Compose (taban, deneme, e2e): yalnız gereken servise; env'de sır adı kalmadı | ✓ | `scripts/compose-secrets.test.mjs` — tam servis→secret haritası, `CREDENTIAL_READERS`, yasak değişken kalıbı (T171a'nınkine eklendi), değerlerde parola yok (`Password=`/`password=`/`redis://:p@`/`requirepass`/`-a`/`-P`), deneme `secrets/trial/`, e2e backend'in DB/Redis değerleri konteynerlerinkiyle aynı |
| 3b | `.env.example`, `secrets/README.md`, DEPLOY_RUNBOOK §L.9 (kurulum günü + geçiş), 05 | ✓ | §L.9 "Üçüncü taraf kimlik bilgileri"; `secrets/README.md` yeni bölüm; 05 §3.5 v3.41 |
| 4a | Açılış testleri | ✓ | Aşağıda "Test Sonuçları" |
| 4b | Compose testleri | ✓ | `compose-secrets.test.mjs` 22 test (T171a'da 13; +9), `compose-exposure`, `compose-backup` güncellendi |
| 4c | Log'da değer yok | ✓ | 2e |
| 4d | Kritik satırlar bozularak sayıldı | ✓ | Aşağıda "Bozma" |
| 4e | Docker'da (ayrı proje) DB/Redis/Grafana dosyadan açıldı, `docker inspect`'te değer yok | ✓ | "Konteyner ölçümü" |
| 5 | Rapor, STATUS, backlog ✅ (sayaç dosyadan), STATUS_CHANGELOG merge teyitleri, MEMORY | ✓ | Bu dosya; backlog **47 aktif / 174 çözülmüş** (dosyanın `awk`'iyle; main 47/173 → `ThirdPartyCredentialsInPlainEnv` ✅ + yeni ⚪ `MssqlSaPasswordFileUnpinned`); STATUS_CHANGELOG: chore #382 main `e83d0737` CI `40041122721` ✓ / Docker Publish `40041122804` ✓, T171a #381 main `b2a0a285` CI `40046709448` ✓ / Docker Publish `40046709444` ✓; `.claude/memory/MEMORY.md` |

### Konteyner ölçümü (2026-10-24, bu PC, Docker Desktop 29.2.1 + Compose v5.1.0)

Ayrı compose projesi `t171b-probe` (gerçek `docker-compose.yml` + yalnız konteyner adlarını/portlarını sıfırlayan bir overlay), `escrow` volume'lerine dokunulmadı (önce/sonra 8); sonunda `down -v`, imaj ve geçici dosyalar silindi. Backend bu dalın imajı, `ASPNETCORE_ENVIRONMENT=Production`, şema `dotnet ef migrations script` ile uygulandı (65 migration).

| # | Durum | Sonuç |
|---|---|---|
| 1 | SQL Server `MSSQL_SA_PASSWORD_FILE` (çıplak imaj, betiksiz) | Dosyadaki parola giriş yaptı, yanlış parola `Login failed`. Dosya `…\n` ya da `…\r\n` ile bittiğinde geçerli parola satır sonu **olmadan**dır (`parola\r` ile giriş reddedildi) |
| 2 | Aynı imaj, yerinde dizin / hiç parola yok | İkisinde de sunucu **açıldı** (giriş betiğinin gerekçesi) |
| 3 | Stack: DB + Redis + backend + Grafana, dosyalarla | Dördü `healthy`. Backend `/health` → `sqlserver: healthy`, `redis: healthy` (parolasız env dizesi + dosyadaki parola). Açılış satırı: `environment path closed`, `ConnectionStrings:DefaultConnection (password)=file, Redis:ConnectionString (password)=file` |
| 4 | DB içinden | `SQLCMDPASSWORD` (secret dosyasından) ile `SUSER_SNAME()` = `sa`; `Config.Env`'de ve PID 1'in çalışma ortamında `MSSQL_SA_PASSWORD` **0** |
| 5 | Yedek konteyneri | `full t171b` → `OK FULL … verified` (dosyadan parola); `-e MSSQL_SA_PASSWORD=…` ile → `FATAL CREDENTIAL_IN_ENV`, çıkış 1 |
| 6 | Redis | Parolasız `PING` → `NOAUTH`; dosyadaki parola (`REDISCLI_AUTH`) → `PONG`; yanlış parola reddedildi; `CONFIG GET requirepass` dosyaya eşit; `redis-server` argv'si `redis-server *:6379` (parola yok); yapılandırma dosyası tmpfs'te, `redis 600` |
| 7 | Grafana, dosya CRLF (Git Bash `openssl` çıktısı) | **Bulgu:** imajın `/run.sh`'ı CR'yi parolada tuttu — görünen parola 401, `parola\r` 200. Düzeltme: `first-boot.sh` CR'yi reddeder → yeniden ölçüldü: `CREDENTIAL_INVALID`, Grafana başlamadı |
| 8 | Grafana, boş volume, LF dosya | Dosyadaki parola `/api/user` 200, `admin/admin` 401; `Config.Env`'de `GF_SECURITY_ADMIN_PASSWORD` 0; değişkenle (`-e GF_SECURITY_ADMIN_PASSWORD=…`) → `CREDENTIAL_IN_ENV` |
| 9 | `docker inspect` (backend, DB, Redis, Grafana) + log'lar + `docker compose config` | Üç parolanın hiçbiri **0** kez |
| 10 | Eksik kaynak dosya (DB, Redis, Grafana yönetici) | Docker host'ta dizin açtı; konteyner içinde bağlama `-f` geçti ama okuma **I/O hatası** verdi (bulgu) → betikler bir kez okuyup `CREDENTIAL_UNREADABLE`, çıkış 1 (düzeltmeden önce kod `CREDENTIAL_MISSING`/boş çıkıyordu — yine açılmıyordu) |
| 11 | DB/Redis değişkenle (`-e MSSQL_SA_PASSWORD` / `-e REDIS_PASSWORD`) | `CREDENTIAL_IN_ENV`, açılmadı |
| 12 | Backend, Production, `ConnectionStrings__DefaultConnection` içinde `Password=` | `CREDENTIAL_IN_ENV: ConnectionStrings:DefaultConnection (password)`, sıfırdan farklı çıkış, değer çıktıda 0 |
| 13 | e2e DB + Redis (`configs` ile) | İkisi `healthy`, Redis `REDISCLI_AUTH` ile `PONG` |

### Bozma (kritik satırları bozup testleri koşma)

Betikle (bir satır bozulur, onu yakalaması gereken testler koşulur, dosya geri yüklenir):

| # | Bozma | Sonuç |
|---|---|---|
| B1–B11 | Backend: env yolu her ortamda açık · değişken reddi kaldırıldı · bağlantı dizesindeki parola görülmüyor · zorunlu dosya kontrolü · isteğe bağlının boşaltılması · okunamayanın yokmuş sayılması · Redis karakter kuralı · SQL parolasının dizeye konmaması · `Program.cs` çağrısı · açılış log satırına bağlantı dizesi eklenmesi · dosya + değişken belirsizliği | 11/11 yakalandı |
| T1–T3 | TRON: değişken reddi · okunamayanın yutulması · giriş noktasının anahtarları `TronGridClient`'a vermemesi | 3/3 (T3 ilk turda **yaşadı** → süreç testi eklendi: dosyadaki anahtar ilk solid-head isteğinin `TRON-PRO-API-KEY` başlığında; yeniden koşuda yakalandı) |
| S1–S4 | Steam: URL'deki parolanın üretimde kabulü · dosya parolasının URL'ye konmaması · `STEAM_API_KEY` değişkeni · Redis karakter kuralı | 4/4 |
| E1–E2 | Ethereum: değişken reddi · birincil URL'nin yeniden env'den okunması | 2/2 |
| M1–M5 | SQL Server betiği: değişken reddi · dizin kontrolü · denetimsiz başlatma · yoklamada parolanın argv'ye (`-P`) konması · içerdeki CR'nin kabulü | 5/5 |
| R1–R4 | Redis betiği: parolanın komut satırına dönmesi · karakter kuralı · boş dosyanın kabulü · yapılandırma dosyasının herkese açık yazılması (`umask 000`) | 3/4 — **R4 yaşadı (yalnız bu PC'de):** test dosya modunu Windows'ta denetlemez (NTFS'te anlamsız); CI (Linux) aynı testi mod denetimiyle koşar |
| G1–G3 | `first-boot.sh`: değişkenin kabulü · boş yönetici dosyasının kabulü · CR'nin kabulü | 3/3 |
| D1 | `db-backup.sh`: değişkenin kabulü | 1/1 |
| C1–C8 | Compose: DB parolasının değişkene dönmesi · Redis yoklamasında `-a` · Steam servisinin `redis_password`'ü kaybetmesi · TRON anahtarının backend'e de bağlanması · `.env.example`'da parolalı bağlantı dizesi · deneme Grafana parolasının geliştirme dosyasından gelmesi · e2e backend'in DB parolasının konteynerinkinden farklılaşması · Ethereum RPC URL'sinin değişkene dönmesi | 8/8 |

**Toplam: 40 bozma, 39 yakalandı** (T3 testle kapandıktan sonra), **1 yaşadı** (R4 — platform: Windows'ta mod denetimi atlanır, CI'da koşar).

## Test Sonuçları

| Komut | Sonuç |
|---|---|
| `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~ThirdPartyCredentialFiles"` | 45/45 (birim + host) |
| `dotnet test tests/Skinora.API.Tests --filter "…ThirdPartyCredentialFiles\|…SharedSecretFiles\|…DiagnosticsEnvironmentGateTests"` | 82/82 |
| `dotnet test tests/Skinora.API.Tests` (tam, LocalDB) | **1213/1213** (T171a sonrası 1168; +45) |
| `sidecar-blockchain: npx vitest run` | 43 dosya, 836/836 |
| `sidecar-ethereum: npx vitest run` | 37 dosya, 961/961 |
| `sidecar-steam: npx vitest run` | 14 dosya, 203/203 |
| `node --test "scripts/**/*.test.mjs"` (CI'nın kalıbı) | 222/222 (compose-secrets 22, mssql-entrypoint + redis-entrypoint + db-backup + first-boot 106) |
| `tsc --noEmit` (üç sidecar), `eslint` (değişen dosyalar), `prettier --check --end-of-line auto` (üç sidecar `src`), `dotnet format --verify-no-changes` (değişen backend dosyaları) | temiz |
| `docker compose config` — taban (`--profile ethereum`), deneme (`--profile ethereum --profile tunnel`), e2e | temiz |

Yerel prettier'ın bütün dosyalarda verdiği uyarılar `core.autocrlf` CRLF artifaktıdır (CI LF'te koşar).

## Altyapı Değişiklikleri

- **Geliştirme ortamı sözleşmesi değişti:** `docker compose -f docker-compose.yml up` artık `secrets/` altında `mssql_sa_password.txt`, `redis_password.txt`, `grafana_admin_password.txt` (dolu), `steam_api_key.txt`, `tron_api_key.txt`, `tron_api_key_secondary.txt` (boş olabilir) dosyalarını ister (`--profile ethereum` ile iki RPC dosyası). `.env`'deki `DB_CONNECTION_STRING`, `REDIS_CONNECTION_STRING`, `STEAM_SIDECAR_REDIS_URL` parolasız olmalı — parolalı biçim backend'i (Development'ta dosya varken `CREDENTIAL_AMBIGUOUS`, dışında `CREDENTIAL_IN_ENV`) ve Steam servisini durdurur. Geçiş: `secrets/README.md` "Üçüncü taraf kimlik bilgileri — geçiş" (veritabanı ve Grafana volume'leri eski parolayı saklar: dosyaya **aynı** değer yazılır).
- **Grafana `admin` geri düşüşü kalktı:** geliştirmede de dosya zorunlu.
- **Redis parola karakter kuralı:** eski `.env.example` yer tutucusu `YourRedisPassword123` kurala uyar; uymayan bir geliştirme parolası `CREDENTIAL_INVALID` ile durur.
- **e2e:** DB ve Redis gerçek giriş betikleriyle; değerler değişmedi (Playwright harness'ı aynı DB parolasını kullanır).

## Commit & PR

- Dal: `task/T171b-ucuncu-taraf-sirlar` (origin/main `b2a0a285`'ten)
- PR: #383
- Commit'ler: `2ae05dc8` plan/status · `928d23bc` backend · `ee162568` sidecar'lar · `6f549cff` compose + betikler + testler + runbook · `373338b4` ölçüm düzeltmeleri · belgeler/rapor commit'i
- CI: yönetici izliyor (task.md Bitiş Kapısı — yönetici düzeni istisnası)

## Kararlar (öneriyle kaydedildi — ⚠)

- **K1 — SQL Server'da imajın `MSSQL_SA_PASSWORD_FILE`'ı + denetim betiği** (yerine giriş betiğinin parolayı `MSSQL_SA_PASSWORD`'e koyması değil): parola hiçbir ortam değişkenine girmez (ölçüldü: PID 1 ortamında 0). Desteğin resmî belgede doğrulanmaması ve etiketin kayan olması yeni ⚪ `MssqlSaPasswordFileUnpinned`'a yazıldı — bozulursa sessiz değil (sağlık yoklaması giriş yapamaz) (öneriyle kaydedildi — ⚠ GÜVENLİK).
- **K2 — Redis parola karakter kuralı `A-Z a-z 0-9 . _ ~ + = -`:** parola üç yere kaçışsız girer (redis.conf tırnaklı satır, virgülle ayrılan bağlantı dizesi, `redis://` URL); `openssl rand -hex 32` uyar. Kural backend, Steam servisi ve betikte aynı (öneriyle kaydedildi — ⚠).
- **K3 — Discord sırları compose'da bağlı değil:** kanal üretimde kapalı (sahip kararı 2026-10-21); kullanılmayan sırrı konteynerde taşımamak için. Backend dosyayı okur, değişkeni üretimde reddeder; kanal açılırsa iki dosya backend'in `secrets:` listesine eklenir (T171a K3 ile aynı gerekçe) (öneriyle kaydedildi — ⚠ GÜVENLİK).
- **K4 — DB, Redis ve Grafana yönetici parolası zorunlu, diğerleri isteğe bağlı:** önceki davranış korundu (Steam anahtarı yoksa probe 503, TronGrid anahtarı yoksa 429, yedek RPC yoksa geçiş yok); parolasız bir veritabanı/Redis/Grafana kurulum hatasıdır (öneriyle kaydedildi — ⚠).
- **K5 — Satır sonu kuralı:** dosyada **tek** sondaki LF ya da CRLF kabul, başka her CR reddedilir (DB/Redis betikleri); Grafana dosyalarında **hiç** CR yok — ölçüldü: SQL Server sondaki CRLF'i atar, Grafana `/run.sh` CR'yi parolada tutar. Bu PC'de Git Bash `openssl` CRLF yazdığı için belgelerdeki komutlar `tr -d '\r'` ile yazar. Aynı CR kuralı T171a'nın Grafana SMTP parolasına da uygulandı (SMTP açıkken) — T171a'da gizli kalan bir hataydı: CRLF'li bir Resend anahtarı e-posta alarmlarını yalnız gönderimde düşürürdü (öneriyle kaydedildi — ⚠ GÜVENLİK, alarm yolu).
- **K6 — e2e sahte servisi DB parolasını env'den okumaya devam eder:** test ikizi, üretim yolunda değil (T171a K6 ile aynı) (öneriyle kaydedildi — ⚠).
- **K7 — Bağlantı dizesi değişkenleri kaldı ama parolasız:** `DB_CONNECTION_STRING` / `REDIS_CONNECTION_STRING` adları korundu; eski `.env`'deki parolalı değer sessizce yutulmak yerine açılışı durdurur (değişkenin adını değiştirmek eski değeri sessizce geçersiz kılardı) (öneriyle kaydedildi — ⚠).

## Proje sahibine açık sorular

- Yok. (Güvenlik riski taşıyan açık karar kalmadı; K1'in sabitleme seçeneği ⚪ satırda — imaj digest'e sabitlenirse güncellemeler elle olur.)

## Known Limitations / Follow-up

- Dosyalar host'ta düz metin (DEPLOY_RUNBOOK §L.8 "Disk" — disk şifrelemesi). SQL Server ve Grafana parolayı kendi süreçlerinde tutar; Redis'in kopyası tmpfs'te; tam bağlantı dizesi backend belleğinde (eskisi gibi).
- Linux sunucuda SQL Server (uid 10001) ve Grafana (uid 472) secret dosyasını okuyabilmeli — `chmod 444` (DEPLOY_RUNBOOK §L.9 kurulum adımı 3); okuyamazlarsa açılmazlar.
- Veritabanı ve Grafana yönetici parolası yalnız boş volume'ün ilk açılışında uygulanır; döndürme `ALTER LOGIN` / `grafana cli admin reset-admin-password` ister (§L.9 "Döndürme").
- ⚪ `MssqlSaPasswordFileUnpinned` (K1).

## Notlar

- Pre-commit sır koruması kod tanımlayıcılarına (`REDIS_PASSWORD`, `SqlPassword`, `ConnectionStrings:DefaultConnection`) ve `t171b-`/`fake-`/`e2e-` önekli sabit test değerlerine takıldı; her commit `SKINORA_ALLOW_SECRET=1` + gerekçeyle geçti ve `Docs/BYPASS_LOG.md`'ye yazıldı — staged içerikte gerçek sır yok. Ölçümün geçici `secrets/*.txt` dosyaları gitignored ve iş bitince silindi.
