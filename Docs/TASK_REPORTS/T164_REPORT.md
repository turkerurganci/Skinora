# T164 — Veritabanı yedeği ve geri dönüş planı

**Faz:** F9 | **Durum:** ✓ Tamamlandı — ilk doğrulama ✗ FAIL (1 × S1 ⚠ PARA, 3 × S3, 7 × S4 — bkz. "Düzeltme turu"), yeniden doğrulama ✓ PASS (5 × S4 — bkz. "Kapanış") | **Tarih:** 2026-10-22 (kapanış 2026-10-23)

---

## Yapılan İşler

- **Otomatik yedek — `skinora-db-backup` (compose).** SQL Server Express'te SQL Agent yok; zamanlayıcı compose'daki küçük bir konteyner (veritabanının kendi imajı, `sqlservr` başlatılmaz, betik koşar). [`scripts/db-backup/db-backup.sh`](../../scripts/db-backup/db-backup.sh): tam yedek **24 saatte bir**, log yedeği **15 dakikada bir** (betik veritabanını FULL kurtarma modeline alır), her dosya `WITH CHECKSUM` + `RESTORE VERIFYONLY`, yarım dosya `.inprogress` adıyla yazılır ve doğrulanınca yeniden adlandırılır. Takvim **yaşa göre** (en yeni dosya aralıktan eski mi?) — PC yeniden başlayınca `restart: unless-stopped` ile gelir ve ilk dakikada eksik yedeği alır. Veritabanı yoksa / `RESTORING` ise bekler. Log yedeği "tam yedek yok" (Msg 4214) ile reddedilirse tam yedek alır. Parola argv'de değil `SQLCMDPASSWORD` ile. Elle komutlarla zamanlayıcı kilitle ayrılır.
- **Saklama.** 14 gün; **en yeni 2 tam yedek her zaman kalır**; en eski kalan tam yedekten eski log yedekleri silinir; 1 günlük yarım dosyalar silinir; `msdb` geçmişi de budanır.
- **Ayrı yer.** Dosyalar veritabanı volume'ünde değil **host klasöründe** (bind mount, iki konteynerde aynı yol): geliştirme `backups/db`, deneme yayını `backups/trial/db` (`TRIAL_DB_BACKUP_DIR` ile başka diske). `backups/` `.gitignore`'da.
- **Geri yükleme komutları.** `restore <hedef> [stopat]`: en yeni tam (ya da stopat'tan önceki son tam) + sonraki bütün loglar, sırayla, **yeni bir ada** (canlının üstüne asla yazmaz; hedef varsa reddeder), point-in-time için `STOPAT`. `swap <geri-yüklenen>`: canlıyı `Skinora_before_restore_<zaman>` olarak **saklar**, geri yükleneni `Skinora` yapar, yeni zinciri başlatan `postrestore` tam yedeğini alır. Fiziksel dosyalar `<hedef>_<zaman>.mdf` — art arda geri yüklemeler çakışmaz (provada bulunup düzeltildi).
- **Deneme yayını Express.** `docker-compose.trial.yml` `MSSQL_PID=Express`'i zorlar (sahip kararı "SQL Server Express"; `.env.example`'dan kopyalanan `.env.trial` Developer derdi).
- **10 GB alarmı.** Backend `DatabaseStorageMonitorService` (5 dk'da bir, hosted loop — Hangfire değil: iş deposu izlenen veritabanının içinde) kendi bağlantısıyla `sys.database_files`, `SERVERPROPERTY('EngineEdition')` ve `msdb.dbo.backupset`'i okur; `skinora_backend_db_*` serilerini yayımlar. Express'te sınır 10.240 MB, başka sürümde sınır serisi yok (ayarla verilebilir). Dört Grafana kuralı: `db-data-size-warning` (%70, 15 dk, warning), `db-data-size-critical` (%85, 5 dk, critical), `db-backup-full-stale` (26 sa, critical; hiç yedek yoksa da çalar), `db-backup-log-stale` (45 dk, warning, yalnız FULL modelde).
- **Runbook.** DEPLOY_RUNBOOK yeni **§N**: sıklık/saklama/yer, zamanlayıcı seçimi ve gerekçesi, klasör izni (icacls / Linux uid 10001), BitLocker, haftalık şifreli ikinci kopya (GnuPG, §L.2 ile aynı ayarlar), günlük işletim, alarm tasarımı, geri yükleme adımları, **kötü sürüm / kötü migration geri dönüşü** (üç durum + karar kuralı), **geri yükleme sonrası para mutabakatı** (tekrar gönderim, depozit indeksinin yeniden verilmesi, gelen ödemeler; imza servisleri kapalıyken açılış sırası), prova, sınırlar. §K.4 adım 5 ve §K.8'e yedek notu.
- **Doküman.** 05 §2.4 / §8.6 "Backup", "DB backup", "Disaster recovery" satırlarına uygulandı notu + RPO önerisi (v3.32); DOC_CHANGELOG; DEFERRED_BACKLOG yeni satır `PostRestoreChainReconciliationTooling` 🟡 ⚠ PARA (53/159 → 54/159).

## Etkilenen Modüller / Dosyalar

- `docker-compose.yml` — `skinora-db` yedek bind mount'u, yeni servis `skinora-db-backup` (ayrı blok)
- `docker-compose.trial.yml` — `skinora-db` Express + kendi yedek klasörü, `skinora-db-backup` (ayrı blok)
- `.env.example` — yeni "Database backup" bloğu (`DB_BACKUP_*`); `.gitignore` — `backups/`
- `scripts/db-backup/db-backup.sh` (yeni), `scripts/db-backup/db-backup.test.mjs` (yeni), `scripts/db-backup/compose-backup.test.mjs` (yeni)
- `backend/src/Skinora.API/Monitoring/DatabaseStorageMonitorOptions.cs`, `DatabaseStorageProbe.cs`, `PrometheusDatabaseStorageObserver.cs`, `DatabaseStorageMonitorService.cs` (yeni); `Program.cs` (kayıt)
- `backend/tests/Skinora.API.Tests/Unit/Monitoring/PrometheusDatabaseStorageObserverTests.cs`, `Integration/Monitoring/SqlServerDatabaseStorageProbeTests.cs` (yeni)
- `infra/grafana/provisioning/alerting/rules.yml` — 4 kural
- `Docs/DEPLOY_RUNBOOK.md` (§N, §K.4, §K.8, başlık), `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/DOC_CHANGELOG.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Otomatik yedek (tam + gerekiyorsa log/fark; Express'te zamanlayıcı yolu seçilip gerekçelendi; PC yeniden başlayınca kendiliğinden sürer) | ✓ | `skinora-db-backup`, `restart: unless-stopped`; tam 24 sa + log 15 dk, fark yok (gerekçe §N.1). Seçim tablosu §N.2. Prova: konteyner durdurulup başlatılınca (17:10:47 → 17:22:44) ilk tick'te eksik log yedeği alındı; `down -v` + `up` sonrası yeni sunucuda kendiliğinden bekleme → geri yüklemeden sonra devam. Test: "catch-up after a restart", "first tick … full", "Msg 4214 → full" (`db-backup.test.mjs`) |
| 2 | Saklama süresi (eski yedeklerin silinmesi; öneri) | ✓ | 14 gün + en yeni 2 tam her zaman, eski loglar en eski kalan tamla birlikte (§N.1, ⚠). Test: "deletes fulls past the retention…", "never deletes the newest two fulls…", "removes a day-old half-written file"; mutasyon: `MIN_KEEP_FULL=0` ve log-budama kapatma testleri kırdı |
| 3 | Yedeğin veritabanı konteynerinden ayrı yere kopyalanması (host klasörü; çevrimdışı/ikinci kopya önerisi runbook'ta) | ✓ | Bind mount host klasörü; `compose-backup.test.mjs` iki konteynerin aynı host klasörünü aynı yolda gördüğünü, bind olduğunu, deneme yayınının kendi klasörünü kullandığını ve gitignored olduğunu doğrular. Prova adım 8: `down -v` veritabanı volume'ünü sildi, host'ta 31 dosya (23 MB) kaldı. İkinci kopya: §N.3 (haftalık, GnuPG, yer sahibin seçimi) |
| 4 | Geri yükleme provası (test verisi → yedek → DB sil/boz → geri yükle → satır sayıları/örnek kayıt aynı; migration geçmişi tutarlı) | ✓ | Aşağıda "Geri yükleme provası": 32 tablonun satır sayısı + `CHECKSUM_AGG(BINARY_CHECKSUM(*))` birebir, migration 62/62 son `20261021121631_T160c_PlatformNetworkCost`, örnek kayıt (kullanıcı #250) aynı, `DBCC CHECKDB` temiz; bozma + volume kaybı + point-in-time |
| 5 | SQL Server Express 10 GB sınırına alarm (metrik + Grafana kuralı, %70 / %85; metrik kaynağı gerekçeli; testle) | ✓ | Backend serileri (gerekçe §N.5: exporter yok). Unit testler kural ifadelerini metrik adlarıyla eşleştirir; integration test sorguları gerçek SQL Server'da koşar. Canlı: Express'te `data_used 9.699.328 / limit 10.485.760` (test ayarı 10 MB) → `db-data-size-critical` **firing** (17:15:40Z, alıcılar `telegram-critical,email-critical`), `db-data-size-warning` **firing**; `db-backup-log-stale` **firing** (17:17:10Z) ve yedek yeniden başlayınca **normal** |
| 6 | Geri dönüş planı (önceki imaj + son yedek) runbook'ta | ✓ | §N.7: kurulum öncesi zorunlu adımlar (`full predeploy`, commit + son migration notu), üç durum (kötü sürüm → yalnız imaj; kötü migration → ileri düzeltme / Down betiği / point-in-time; veri kaybı → geri yükleme), ghcr `sha-<commit>` imajları |
| 7 | Para işlemleri açısından yedek anı ile geri dönüş arasındaki zincir olaylarının mutabakatı; geri yüklemeden sonra hangi işler koşmalı | ✗ → ✓ (düzeltme turu) | **İlk sürüm doğrulamada ✗ (F1, S1 ⚠ PARA)** — geri yüklemeden sonra yeni kimlikle açılan satırlar görülmüyordu; düzeltme ve prova "Düzeltme turu" bölümünde. İlk sürümün metni: | §N.8: (a) tekrar gönderim (TRON `PENDING` satır yeniden gönderilir, Ethereum günlükten tekrar oynatır; yoksa yeni kimlikle ikinci ödeme) + karşılaştırma SQL'i + düzeltme yolları (AD33–AD37, EMERGENCY_HOLD); (b) depozit indeksinin yeniden verilmesi + SQL; (c) gelen ödemeler → `ReconciliationJob` hemen (`reconciliation.schedule_cron` geçici); açılış sırası imza servisleri ve tünel kapalı. İki eksik araç DEFERRED_BACKLOG `PostRestoreChainReconciliationTooling` |

## Geri yükleme provası (2026-10-22, bu PC)

Ortam: `docker compose -p skinora-t164 --env-file <scratch>/t164.env -f docker-compose.yml -f docker-compose.trial.yml` — yani deneme yayını overlay'i: **SQL Server Express Edition (64-bit) 16.0.4250.1**, `EngineEdition` 4. Yalnız `skinora-db` + `skinora-db-backup` (alarm adımında + redis, loki, backend, prometheus, grafana). Log aralığı ölçüm için 1 dk (`DB_BACKUP_LOG_INTERVAL_MINUTES=1`), tick 60 sn. Geliştirme projesi `escrow`'a dokunulmadı.

Parmak izi: her `dbo` tablosu için `COUNT_BIG(*)` + `CHECKSUM_AGG(BINARY_CHECKSUM(*))` (32 tablo), `__EFMigrationsHistory` sayısı + son kayıt, kullanıcı #250'nin satırı.

| # | Adım | Sonuç |
|---|---|---|
| 1 | `CREATE DATABASE Skinora` (boş) | 21 sn içinde `OK FULL Skinora_FULL_20261022T163603Z.bak bytes=2936832 verified` — host'ta görüldü (Docker Desktop bind mount'a SQL Server yazabiliyor) |
| 2 | Migration (§K.4 adım 4) | 62/62, son `20261021121631_T160c_PlatformNetworkCost`; şema yalnız log yedeklerinde |
| 3 | Test verisi **A** (16:57:13Z, tek transaction) | Users 501, UserLoginLogs 2.000, AuditLogs 1.000 — Türkçe karakterli adlar, `#250` `CompletedTransactionCount` 28 |
| 4 | `stopat` noktası 16:59:31Z, sonra test verisi **B** (16:59:53Z) | Users 601, AuditLogs 1.001, `#250` 1.028 |
| 5 | Bozma 17:01:31Z (anlık 17:01:27Z kaydedildi) | `DELETE FROM UserLoginLogs; DROP TABLE AuditLogs; UPDATE Users SET SteamDisplayName = N'BOZULDU'` |
| 6 | `restore Skinora_restored 2026-10-22T17:01:27Z` | 1 tam + 25 log, **5,6 sn**; parmak izi **B ile birebir** (`diff` boş), `DBCC CHECKDB` temiz |
| 7 | `swap Skinora_restored` | canlı → `Skinora_before_restore_20261022T170436Z` (Users 601, UserLoginLogs 0, AuditLogs yok — bozuk hâl saklandı); `Skinora` = B; `Skinora_FULL_20261022T170436Z_postrestore.bak` alındı; sonraki tick'te log zinciri devam etti (`17:05:56Z OK LOG`) |
| 8 | **Volume kaybı:** `down -v` | `skinora-t164_skinora-db-data` silindi (volume sayısı 0); `backups/trial/db`'de 31 dosya, 23 MB **kaldı** |
| 9 | Boş sunucu: `up -d --wait skinora-db skinora-db-backup` | `Skinora%` veritabanı 0, `msdb.dbo.backupset` 0 satır; zamanlayıcı `waiting: database Skinora does not exist yet` |
| 10 | `restore Skinora_restored` + `swap` (en yeni) | `postrestore` tam + 2 log; parmak izi **B ile birebir** |
| 11 | Point-in-time `restore Skinora_pit 2026-10-22T16:59:31Z` | ilk tam + 23 log; parmak izi **A ile birebir** (B'nin 100 kullanıcısı ve güncellemesi yok); iki veritabanında `DBCC CHECKDB` temiz |
| 12 | Backend açıkken bir geri yükleme daha (`Skinora_restored`, yeni dosya adı) | fiziksel dosya `Skinora_restored_20261022T172300Z.mdf` (önceki sürüm canlının `Skinora_restored.mdf`'iyle çakışırdı — bulundu, düzeltildi); canlıyla fark yalnız backend'in her saniye güncellediği `SystemHeartbeats` satırı |
| 13 | Express sıkıştırma | `BACKUP DATABASE WITH COMPRESSION is not supported on Express Edition (64-bit)` — betik sıkıştırma kullanmaz |

**Alarm ölçümü (aynı proje, backend Production modunda, test ayarı `DatabaseStorageMonitor__DataLimitMegabytes=10`, `__LogBackupMaxAgeMinutes=1`, `__IntervalSeconds=30` — yalnız prova overlay'inde, commit'lenmedi):**

| Zaman (UTC) | Olay |
|---|---|
| 17:10:20 | `/metrics`: `skinora_backend_db_data_used_bytes 9699328`, `…_data_allocated_bytes 75497472`, `…_data_limit_bytes 10485760`, `…_log_allocated_bytes 8388608`, `…_recovery_model_full 1`, `…_last_backup_timestamp_seconds{type="full"} 1792688866` (= 17:07:46, `postrestore` tam yedeği), `{type="log"} 1792689005`, eşikler `full 93600` / `log 60` |
| 17:10:47 | yedek konteyneri durduruldu |
| 17:15:40 | `db-data-size-critical` **Alerting** (pending 17:10:40 + 5 dk); Alertmanager alıcıları `telegram-critical,email-critical` |
| 17:17:10 | `db-backup-log-stale` **Alerting** (`type="log"`); alıcı `telegram-warning` |
| — | `db-backup-full-stale` normal (tam yedek dakikalar önce) — eşiği 1 saatin altına inemediği için canlı tetiklenmedi; ifade unit testte, aynı biçimdeki log kuralı canlı |
| 17:22:44 | yedek konteyneri başladı, ilk tick'te log yedeği |
| 17:25:04 | `db-backup-log-stale` yeniden **normal** (log yedeği geldi) |
| 17:27:10 | `db-data-size-warning` **Alerting** (pending 17:11:10 + 15 dk, warning grubu 2 dk aralıkla) |

Sonuç: dört kuraldan üçü canlıda doğru durumu aldı ve Grafana Alertmanager'a doğru alıcıyla ulaştı (critical → `telegram-critical`, `email-critical`; warning → `telegram-warning`); log kuralı kaynak düzelince kendiliğinden kapandı. (Aynı ölçümde `Steam Sidecar Down` / `Blockchain Sidecar Down` de çaldı — sidecar'lar bilerek açılmamıştı.)

**Temizlik:** `down -v --rmi local --remove-orphans` (yalnız `-p skinora-t164`) → konteyner 0, volume 0, ağ 0; prova yedek klasörü (`backups/`) ve geçici `.env`/migration betiği silindi; `escrow_*` volume'leri 8 (dokunulmadı).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Script (node --test) | ✓ 22/22 | `node --test scripts/db-backup/db-backup.test.mjs` — sahte `sqlcmd` ile takvim, saklama, geri yükleme zinciri, point-in-time, üstüne yazmama, parola argv'de değil. Mutasyon: 5/5 anlamlı bozma (en yeni 2 tamı koruma, tam aralığı, 4214 → tam, log budama, canlıya geri yükleme reddi) en az bir testi kırdı |
| Script (compose) | ✓ 14/14 | `node --test scripts/db-backup/compose-backup.test.mjs scripts/compose-exposure.test.mjs` (T161 testleri yeni servisle yeşil: port yok, sabit container adı yok) |
| Backend unit (CI filtresi) | ✓ 154/154 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Backend integration (yerel) | ✓ 775/775 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~.Integration"` (CI'da Lint yüzünden atlandı) |
| Backend yeni testler | ✓ 18/18 | `--filter FullyQualifiedName~DatabaseStorage` — 16 unit (sınır çözümü 6 satır, yayımlama, "hiç yedek yok = 0", okunamayan geçmiş son değeri korur, eşikler, 4 kural ifadesi, servis) + 2 integration (Testcontainers SQL Server: boyut/sürüm/model, copy-only sayılmaz, tam + log yaşı) |
| Build | ✓ | `dotnet build src/Skinora.API` 0 uyarı, 0 hata |
| CI | ✗ (dış neden) | Run `39822082262` (HEAD `95110e5c`, sonraki push iptal etti) ve `39824772868` (HEAD `3e2441c0`, **failure**): **yalnız** "1. Lint" → "Frontend npm audit (blocking — prod highs)" kırmızı — bugün yayımlanan **critical** Next.js danışması `GHSA-2464-fhr4-w9xw` (Image Optimization API, SVG ile kimliksiz uzaktan kod çalıştırma; `next 16.0.0 – 16.4.0`, düzeltme 16.4.2) ve `GHSA-p3m5-59mp-jv5r`. T164 frontend'e dokunmadı; aynı denetim T168'in 17:12Z run'ında yeşildi. Lint kırmızı olduğu için 2. Build, 3. Unit, 4. Integration, 6. Migration, 7. Docker atlandı; 3b JS test ✓, **3c Script test ✓** (T164'ün betik testleri), E2E advisory'ler ✓. Aynı adım aynı saatlerde T163 (`39821704801`) ve T169 (`39821996174`) dallarında da kırmızı. Atlanan backend kolları yerine yerelde: API integration **775/775** (`--filter FullyQualifiedName~.Integration`, 13 dk 27 sn). Ayrı bir `next` yükseltme PR'ı main'e girince dal yeniden koşulmalı |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Tur | HEAD | Verdict | Bulgular | Sonuç |
|---|---|---|---|---|
| 1. doğrulama (bağımsız alt ajan) | `3e2441c0` | ✗ FAIL | 11 — S1: 1 (⚠ PARA, F1) · S3: 3 (F2 ⚠ PARA, F3, F4) · S4: 7 (F5–F11) | "Düzeltme turu": S1 ve üç S3 kapandı; S4'lerden F6, F7 (ad çakışması), F9, F11 kapandı, F5/F8 belgede, F7 (kilit) ve F10 bilinen sınırlama |
| Yeniden doğrulama (bağımsız alt ajan, Opus high) | `99e109ab` | ✓ PASS | 5 — S4: 5 (S4-2, S4-3 ⚠ PARA belge doğruluğu) | S1 senaryosu T164 + T169 koduyla bağımsız yeniden üretildi: eski yordamla **4 ikinci gönderim** (TRON ödeme + iade, Ethereum ödeme + iade), §N.8 ile kapı kapalı da açık da **0**; "adım 5 atlanırsa" 1. Üç S3 canlı bozma testleriyle kapandı. S4'lerin dördü kapanışta kapandı, biri (S4-4) birleştirme — "Kapanış" |

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Var** — `.env.example` `DB_BACKUP_HOST_DIR`, `DB_BACKUP_FULL_INTERVAL_HOURS`, `DB_BACKUP_LOG_INTERVAL_MINUTES`, `DB_BACKUP_RETENTION_DAYS`; backend `DatabaseStorageMonitor__*` (varsayılanlar kodda, compose'a eklenmedi); deneme yayını `TRIAL_DB_BACKUP_DIR` (isteğe bağlı)
- Docker değişikliği: **Var** — yeni servis `skinora-db-backup` (port yok), `skinora-db`'ye host bind mount; deneme yayınında `MSSQL_PID=Express`

## Commit & PR

- Branch: `task/T164-veritabani-yedegi`
- Commit: `95110e5c` — T164: Veritabanı yedeği ve geri dönüş planı (+ rapor/status commit'i)
- Düzeltme turu: `d4644efd` (main #373 merge commit), `73ebdd87` (betik + izleme), `83e89299` (runbook §N.8 + belgeler) ve bu rapor commit'i
- Kapanış: S4 commit'i, main (`69c68df6`) merge commit'i ve kapanış kaydı commit'i (aşağıda "Kapanış")
- PR: #372
- CI: ✗ — run `39824772868` dış nedenle kırmızı (Next.js critical danışması, frontend audit kapısı); T164'e ait iş yeşil, backend iş kolları Lint'e bağlı olduğu için atlandı

## Kolay kararlar (öneriyle kaydedildi — ⚠)

1. **Zamanlayıcı compose konteyneri** (Görev Zamanlayıcı / Hangfire değil) — gerekçe §N.2.
2. **Tam 24 sa + log 15 dk, fark yedeği yok, FULL kurtarma modeli** — RPO ≤ 15 dk (⚠ PARA: kayıp penceresi).
3. **Saklama 14 gün, en yeni 2 tam her zaman** (⚠ PARA).
4. **Alarm eşikleri** %70 warning / %85 critical; tam yedek 26 sa critical; log 45 dk warning.
5. **Metrik backend'den** (`sys.database_files` + `msdb`), exporter yok.
6. **Deneme yayınında `MSSQL_PID=Express` zorlanır.**
7. **İkinci kopya haftalık, GnuPG simetrik** (imza sırları yedeğinin — T163 — ayarlarıyla), parola cüzdan arşivininkinden ayrı, son 8 hafta, **yalnız USB** (yer: sahip kararı 2026-10-22) (⚠ GÜVENLİK).
8. **Klasör izni** icacls ile yalnız kullanıcı + SYSTEM; BitLocker (⚠ GÜVENLİK).
9. **Geri yükleme karar kuralı:** yedek anından sonra para hareket ettiyse ve eski veritabanı okunabiliyorsa ileri düzeltme; geri yükleme son çare; geri yüklemeden sonra imza servisleri ve tünel mutabakat bitene kadar kapalı (⚠ PARA).
10. **Aylık geri yükleme provası** (`restore Skinora_drill` + `DBCC CHECKDB`, swap yok).

## Sahibe açık sorular

Üçü de 2026-10-22'de cevaplandı (işlendiği yer: "Düzeltme turu" → "Sahip kararları"):

1. ~~İkinci kopyanın yeri~~ → **yalnız USB, bulut yok.**
2. ~~Geri yükleme sonrası iki eksik araç~~ → **mainnet pilotundan (T173) önce, plan T172a.**
3. ~~05 §8.6 TDE~~ → **şimdilik BitLocker + şifreli makine dışı yedek; ücretli sürümde TDE.**

## Düzeltme turu (2026-10-22)

**Girdi:** bağımsız doğrulama ✗ FAIL (HEAD `3e2441c0`) — 11 bulgu: 1 × S1 ⚠ PARA, 3 × S3 (1'i ⚠ PARA), 7 × S4. Bu bölüm her bulgunun ne olduğunu, nasıl kapandığını ve kanıtını verir. Runbook bölümü **§M → §N** oldu (F9; aşağıda).

### S1 ⚠ PARA — F1: geri yüklemeden sonra yeniden açılan giden satırlar (kapandı: runbook + prova; kod değişikliği yok)

**Sorun (doğrulandı, yeniden üretildi):** geri yüklenen veritabanında yedekten sonra yapılmış ödemenin satırı yoktur. Backend açılır açılmaz `SellerPayoutQueueJob` aynı ödemeyi **yeni kimlikle** yeniden açar; ilk sürümün iki karşılaştırma sorgusu ("eski kopyada olup burada olmayan", "iki tarafta durumu farklı") bu satırı göstermez; önerdiği EMERGENCY_HOLD var olan `PENDING` satırı durdurmaz (`OutgoingTransferDispatchJob` hold'a bakmaz). İmza servisleri açılınca ikinci ödeme çıkar.

**Düzeltme — DEPLOY_RUNBOOK §N.8 yeniden yazıldı:**
- **Ön koşul ve kilit: T169 ödeme onay kapısı** (`outbound.auto_send_enabled` = `false`). Kapı kapalıyken `SELLER_PAYOUT` ve `*_REFUND` satırları admin onayı olmadan gönderilmez; geri yüklemeden sonra açılan her yeni satır onaysızdır. Kapısı olmayan sürümde geri yüklemeden sonra imza servisleri açılmaz. Geri yüklenen hâl kapı açıldıktan sonrasından ise (T175), backend açılır açılmaz, imza servisleri hâlâ kapalıyken kapı kapatılır (adım 2).
- **Açılış sırası:** (1) backend kapalıyken eski kopyayla karşılaştırma; (2) kapı sorgusu; (3) backend + frontend açılır, imza servisleri ve tünel kapalı, en az 5 dk beklenir (dakikalık işler ve outbox satırları yeniden açsın); (4) gönderilmeyi bekleyen **bütün** giden satırların listesi — her tür (payout, 5 iade türü, sweep, iade kesinti sweep'i), iki ağ, `Kaynak = YENI/ESKI` ve eski kopyadaki aynı işlemin aynı türdeki satırı (`OldAyniTur`); (5) satır satır karar: zincirde gönderilmişse Ethereum `ESKI` → AD33–AD37, diğerleri `FAILED` + `POST_RESTORE_DUPLICATE` notuyla kapatılır (gönderim işi yalnız `PENDING` okur; `SellerPayoutQueueJob` ve iade tüketicisi aynı işlemde aynı türde herhangi bir satır varken yenisini açmaz), sweep'ler eski kopyadaki değerlere getirilir; karar verilemeyen onaylı satırın onayı geri alınır; (6) imza servislerinden hemen önce son kontrol; (7) imza servisleri, mutabakat turu, tünel; (8) onaylar mutabakattan sonra.
- **Hold cümlesi düzeltildi:** "EMERGENCY_HOLD kilit değildir — yalnız yeni satır açılmasını durdurur."

**Neden kod koruması eklenmedi:** kilit T169 kapısıyla zaten kodda. Gönderim işinin hold'a da bakması ürün davranışını değiştirir: hold'daki bir işlemin iadesinin ne olacağı ayrı bir karar. Ayrıca T169'un dokunduğu dosyaya ikinci bir değişiklik olurdu. Kapı + liste + kapatma senaryoyu kapatıyor (prova aşağıda). Bağımlılık açık: **T164'ün §N.8'i T169'un kapısına dayanır** — T169 önce birleşirse metin hemen doğrudur. T164 önce birleşirse T169 girene kadar geri yüklemeden sonra imza servisleri açılmaz (§N.8 "Ön koşul").

**Prova (gerçek SQL Server Express 16.0.4250.1, `-p skinora-t164fix`, gerçek yedek konteyneri ve betik, gerçek `SellerPayoutQueueJob` + `OutgoingTransferDispatchJob`):**

Kod, T164 dalı ile `origin/task/T169-sahip-kararlari`'nın yalnız yerelde birleştirilmiş hâliydi (geçici worktree, push yok; 63 migration). İşler, gerçek veritabanına bağlanan geçici bir xunit harness'ında çağrıldı; harness commit'lenmedi. İmza servisi, çağrıları sayan bir test sahtesiydi. "İmza servisi kapalı" durumu, platform sağlık sinyaliyle (TRON kesintide) kuruldu. Log yedeği aralığı 1 dk'ydı.

| Adım | Zaman (UTC) | Sonuç |
|---|---|---|
| Kurulum | 19:23 | 63/63 migration; `outbound.auto_send_enabled` = `true` yapıldı (kapı T175'te açılmış, en zor durum) |
| Tohum | 19:24:51 | işlem `07c96676…`: ITEM_DELIVERED, sweep CONFIRMED, ödemeye uygun, ödeme satırı yok |
| `T_yedek` | 19:25:02 | — |
| Yedekten sonra gerçek ödeme | 19:25:11 | `SELLER_PAYOUT a241a228…` gönderildi (1 çağrı), CONFIRMED `tx-stub-1` |
| S3(a): `restore Skinora_late <şimdi+2dk>` | 19:27:05 | **FATAL** `stopat … is later than the newest backup (Skinora_LOG_…192656Z.trn …)`, rc=1, veritabanı açılmadı |
| Kuyruk log yedeği `$BK log` | 19:27:06 | `OK LOG` |
| `restore Skinora_restored 19:25:02Z` | 19:27:08 | `OK restored … to the 2026-10-22T19:25:02Z (STOPAT)`, CHECKDB temiz |
| `restore Skinora_oldproc 19:25:02Z` | 19:27:09 | eski yordamın gösterimi için ikinci kopya |
| `swap Skinora_restored` | 19:27:10 | `OLD = Skinora_before_restore_20261022T192710Z`, `postrestore` tam yedek |

**Eski yordam (`Skinora_oldproc`, ilk sürümün §M.8'i):**
- Kapı `true`.
- Backend imza servisi kapalı açıldı: 19:28:18'de **yeni satır `cf687b73…` PENDING**, 0 çağrı.
- Eski sorgu 1 yalnız `A241A228…` (eski ödeme) satırını, eski sorgu 2 hiçbir satırı göstermedi; yeni satır ikisinde de yok.
- İşlem EMERGENCY_HOLD'a alındı (19:28:22).
- İmza servisi açıldı: **1 çağrı `cf687b73…` → DETECTED**, yani **ikinci ödeme**. Bulgu yeniden üretildi; hold onu durdurmadı.

**Yeni yordam (`Skinora`, §N.8):**
- Adım 1 `A241A228… CONFIRMED tx-stub-1` satırını gösterdi.
- Adım 2: kapı `true`.
- Adım 3: backend açıldı, 19:28:51'de yeni satır `8340394d…` PENDING oluştu, 0 çağrı. Kapı `false` yapıldı ve doğrulandı.
- Adım 4 listesi: `8340394D…|SELLER_PAYOUT|…|ApprovedAt NULL|YENI|CONFIRMED tx-stub-1 A241A228…`.
- **Kilit sınaması:** kapatmadan önce imza servisi açıldı ve işler koştu, **0 çağrı** oldu (kapı). Adım 5 kapatma `(1 rows affected)` verdi. Adım 6 listesi boş. Adım 7'de imza servisi açık, iki tur koşuldu: **0 çağrı**, satır sayısı 1 (FAILED), yeni satır açılmadı.

Prova bir runbook hatası da buldu: `sqlq`'da `-I` (QUOTED_IDENTIFIER) yoktu ve kapatma `UPDATE`'i `Msg 1934` ile reddedildi (filtreli indeks). `sqlq`'ya `-I` eklendi, kapatma bundan sonra geçti. Kapatma SQL'i, liste SQL'i ve adım 1/2 sorguları provada **runbook'tan otomatik çıkarılıp** koşuldu, elle yazılmadı. Temizlik: `down -v`; konteyner, volume ve ağ 0. Yedek klasörü, env dosyası, migration betiği ve geçici worktree silindi; `escrow_*` volume'leri 8, dokunulmadı.

### S3'ler

- **F2 ⚠ PARA — stopat sessizce kısalıyordu (kapandı, kod + test + canlı).** `restore`, stopat'ta ya da ondan sonra alınmış bir yedek yoksa artık **durur**. Hata mesajı en yeni yedeği ve anını yazar ve önce `log` almayı ya da stopat'sız geri yüklemeyi söyler. Başarılı geri yükleme ulaşılan anı yazar (`to the end of <dosya> (taken <an>)` / `to the <stopat> (STOPAT)`); runbook bu anı `T_yedek` olarak kullanır. Karşılaştırma dosya adındaki zamanla yapılır (log yedeğinin başlangıcı, yani kapsadığı anın alt sınırı): ihtiyatlıdır, bir anı kapsıyor demeden önce dosyanın o anda ya da sonra alınmış olmasını ister. §N.6'ya adım 1 eklendi: canlı veritabanı okunabiliyorsa geri yüklemeden önce kuyruk log yedeği (`$BK log`). Testler: "refuses a stopat later than the newest backup…", "says which point it reached…". Canlıda da reddedildi (yukarıdaki tablo).
- **F3 — tam yedek düşünce log yedekleri duruyordu (kapandı, kod + test).** Tasarım şöyle: log yedeği yalnız **bir önceki** tam yedeğin zincirine ihtiyaç duyar. Tam yedek başarısız olunca zincir bozulmaz, o yüzden o tick'te log yedeği yine alınır ve kayıp penceresi 15 dk'da kalır. Tam yedek her dakika değil **60 dk'da bir** yeniden denenir (`FULL_RETRY_MINUTES`; işaret dosyası kilidin yanında, başarılı tam yedek siler). Her denemede yarım bir tam yedek yazılıp silinmez, dolu disk daha da dolmaz. Hiç tam yedek yoksa ya da kurtarma modeli yeni değiştiyse log denenmez (Msg 4214 olurdu). Msg 4214 ile tam yedeğe düşme de aynı geri çekilmeye uyar. Alarm: tam yedek düşük kaldıkça `db-backup-full-stale` 26 sa sonra çalar; log yedekleri sürdüğü için RPO bozulmaz. Testler: "a failing full does not stop the log backups, and is retried an hour later — not every tick", "a failing first full takes no log backup".
- **F4 — metrik yokken alarm sessizdi (kapandı, kod + kural + test).** Grafana `noDataState` yerine ayrı bir seri ve kural seçildi. `noDataState: Alerting` backend kapalıyken ve izleme bilerek kapatılmışken de çalardı; `backend-down` ile çift alarm olurdu. Repoda hiçbir kural `noDataState` kullanmıyor; hepsi `or vector(0)` deseninde. Backend artık her okumada `skinora_backend_db_probe_success{part="storage"|"backup_history"}` yayımlar: okunduysa 1, okunamadıysa 0; okuma tümden düşerse ikisi de 0. Yeni kural **`db-monitor-probe-failing`** (critical), bir bölüm 15 dk boyunca hiç okunamazsa 5 dk sonra çalar. İfadesi `(1 - max by (part) (max_over_time(…[15m]))) or vector(0)`. Runbook §N.5'teki yanlış cümle düzeltildi. Testler: `Every_Reading_Publishes_Which_Part_Was_Read_So_No_Data_Cannot_Silence_The_Backup_Alarms`, `The_Probe_Rule_Fires_When_A_Part_Was_Not_Read_For_15_Minutes`, servis testi (düşen okuma → 0). `rules.yml` yq 4.44.3 ile ayrıştırıldı: 5 `db-*` kuralı.

### S4'ler

| Bulgu | Durum |
|---|---|
| F5 — geri yüklemeden sonra `msdb` okunan yedekleri "alınmış" gösteriyor | §N.10'a not (zararsız; `postrestore` zinciri yeniler) |
| F6 — saklama fiilen 13–14 gün | **Kapandı:** kesimden önceki en yeni tam yedek (ve logları) da kalır; test güncellendi (16 günlük "köprü" tam yedek kalır) |
| F7 — kilit konteynerde; aynı saniyede iki LOG adı | **Ad çakışması kapandı:** ikinci yedek bir sonraki boş saniyeyi bekler, `mv -n` + kontrol; test "two backups in the same second…". Kilit: §N.10'a "`docker compose run` kullanmayın" notu (bilinen sınırlama) |
| F8 — `CHECKSUM_AGG` zayıf | §N.9 aylık prova `COUNT_BIG` + `SUM(CAST(BINARY_CHECKSUM(*) AS bigint))`; komut provada koşuldu (iki kopyada 32 tablo; fark yalnız beklenen iki tabloda) |
| F9 — bölüm harfi çakışması | **Kapandı:** bölüm **§N**. L ve M'yi açık T163 (imza sırları) ve T169 (onay kapısı + kurulum günü listesi) dalları kullanıyor; T163 atıfları adla yapılıyor ("imza sırları bölümü (T163)"). T163 ile T169 kendi aralarında L için çakışıyor; o yönetici kararında |
| F10 — disk alarmı yok | Bilinen sınırlama + yeni backlog ⚪ `BackupDiskSpaceAlarm`; §N.1'deki yanlış "o noktaya gelmeden alarm çalar" cümlesi düzeltildi, haftalık elle kontrol |
| F11 — işletim notları | **Kapandı:** başarısız geri yüklemede `DROP DATABASE [hedef]` ipucu (betik mesajı + §N.6, test); `.gitignore` bloğu `*.mafile`'dan sonraya; §N.6/§N.7 `sqlq` — parola komut satırında değil, konteynerin kendi ortamından (`SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"` konteyner içinde) |

Ek: §N.4'e `export MSYS_NO_PATHCONV=1` eklendi. Git Bash'te `$BK` komutları `/opt/skinora/…` yolunu Windows yoluna çeviriyordu; provada ölçüldü.

### Sahip kararları (2026-10-22) işlendi

1. İkinci kopya **yalnız USB**, bulut yok: §N.3 ve 05 §2.4/§8.6.
2. Geri yükleme sonrası araçlar **T173'ten önce, plan T172a**: satırı T169 plana ekledi (main); kapanışta plan T172a satırı backlog `PostRestoreChainReconciliationTooling`'i kaynak gösteriyor, backlog satırının hedefi T172a. §N.8 (b) güncellendi.
3. 05 §8.6 "Encryption at rest": şimdilik BitLocker + şifreli makine dışı yedek, ücretli sürümde TDE. Satır T169'daki metinle birebir aynıydı; main birleştirmesinde tek satır kaldı. 05 sürümü **v3.34** (main'de T163 v3.32, T169 v3.33).

### Birleştirme

- `origin/main` (`4b48c82d`, #373 next 16.4.2) dala **merge commit** ile alındı.
- Çakışmalar: `DEFERRED_BACKLOG.md` durum satırı ve `STATUS_CHANGELOG.md` en üst kayıt. İki taraf da korundu; main'in kaydı tarih sırasına göre üstte.
- T163 (#369) ve T169 (#371) henüz main'de değil. Onlarla beklenen çakışmalar yalnız belgelerde: runbook bölüm sonları, backlog/status/changelog başlıkları, 05 sürüm satırı, memory. Kodda çakışma yok; yerel deneme birleştirmesinde T169 ile kod temiz birleşti.

### Testler (düzeltme turu)

- **Script:** `node --test "scripts/**/*.test.mjs"` **79/79**. db-backup 28 test (+6), compose-backup 4. Mutasyon: 5/5 yeni davranış bozulunca en az bir test kırıldı (köprü tam yedek, stopat reddi, log'a düşme, ad bekleme, geri çekilme).
- **Backend (`Skinora.API.Tests`, dokunulan tek proje):**

| Kol | Sonuç |
|---|---|
| Unit (CI filtresi) | ✓ **156/156** (önce 154; +2 probe testi) |
| Integration — LocalDB (`INTEGRATION_TEST_SQL_SERVER=(localdb)\MSSQLLocalDB`) | ✓ **775/775** (5 dk 40 sn) |
| Contract | ✓ 24/24 |
| `dotnet format Skinora.sln --verify-no-changes --severity error` (CI'daki komut) | ✓ rc=0 |


## Kapanış (2026-10-23)

**Girdi:** yeniden doğrulama ✓ PASS (HEAD `99e109ab`), 5 × S4. Para mantığına dokunulmadı; değişiklik yalnız test ve belge.

| Bulgu | Durum |
|---|---|
| S4-1 — servis testi `ReportFailure()` silinmesini yakalamıyordu | **Kapandı (test):** `The_Service_…_Survives_A_Failing_Probe` önce başarılı okuma yapar (iki bölüm 1), sonra düşen okumada iki bölümün 0 olduğunu bekler. Mutasyon (çağrı silindi) yerelde ölçüldü: 1 fail / 18; geri alınınca 18/18 |
| S4-2 ⚠ PARA (belge) — §N.8 adım 3 "deneme hakkı harcanmaz" kesinti algılanana kadar doğru değil | **Kapandı (runbook):** ≈3 dk pencerede deneme harcanabileceği, tükenen TRON satırının `FAILED` olup adım 4 listesinde görünmeyeceği, kapı `true` ise backend açılmadan SQL ile kapatma seçeneği yazıldı |
| S4-3 ⚠ PARA (belge) — kapatmanın defter açığı, (c) yorumu, var olmayan "yeniden dene" | **Kapandı (runbook):** adım 5'e mutabakat / kesinti defteri / şikâyet anomalisi açığı ve satırı eski kopya değerlerine **aynı satır üzerinde** getiren SQL (filtreli tekil indeksler, sil-ekle yok); (c) eksik farkı kayıp ödeme saymaz; "yeniden dene" cümlesi "`FAILED`'i `PENDING`'e döndüren yol yoktur" oldu. Güvenli araç T172a (plan satırına üçüncü madde olarak eklendi) |
| S4-4 — main ilerledi; BYPASS_LOG commit'i push edilmemişti | **Kapandı:** `origin/main` `69c68df6` (T163 #369, T169 #371, T168 #370) merge commit ile alındı; `759b5bb5` push edildi |
| S4-5 — `db-monitor-probe-failing` zamanlaması | **Kapandı (runbook):** §N.5 metni ve tablo satırı: hiç okunmamış bölüm ≈5 dk, okunup düşen ≈20 dk |

**Birleştirme (main `69c68df6`):** çakışmalar yalnız belgelerde; kod (compose ×2, `.env.example`, `rules.yml`) kendiliğinden birleşti.
- DEPLOY_RUNBOOK: bölüm sırası **§L (T163) · §M (T169 onay kapısı) · §N (T164) · §O (T169 kurulum günü listesi)**; §0 özetine §N satırı; §N'deki T163/T169 atıfları harfle (§L, §M); başlık tek satır.
- 05: **v3.34**; §8.6 "Encryption at rest" tek satır (main'in metni).
- DOC_CHANGELOG, BYPASS_LOG (kronolojik), STATUS_CHANGELOG (main'in eski "Son güncelleme" satırı en üstte + merge teyitleri), IMPLEMENTATION_STATUS (main'in ✓ satırları aynen), DEFERRED_BACKLOG (iki tarafın satırları; **55 aktif / 162 çözülmüş**, main 53/162 + 2), plan/11_F9.md T172a ↔ backlog `PostRestoreChainReconciliationTooling`, MEMORY.

**Testler (kapanış, merge sonrası HEAD):** `node --test "scripts/**/*.test.mjs"` **79/79** · `dotnet build Skinora.sln -c Release` 0 hata · `Skinora.API.Tests` unit (CI filtresi) **156/156** · `PrometheusDatabaseStorageObserverTests` 18/18 (mutasyonla 17/18) · `dotnet format Skinora.sln --verify-no-changes --severity error` rc=0 · çakışma işareti 0. Docker kullanılmadı.

**Merge teyitleri (STATUS_CHANGELOG'a işlendi):** T169 (#371) main `21c93062` → CI `39850436226` ✓, Docker Publish `39850436112` ✓ · T168 (#370) main `69c68df6` → CI `39854740137` bloke edici joblar ✓ (CI Gate ✓; advisory UI E2E asılı), Docker Publish `39854740205` ✓.

## Known Limitations / Follow-up

- `PostRestoreChainReconciliationTooling` (DEFERRED_BACKLOG §4; sahip kararı: T172a, T173'ten önce).
- **§N.8 T169'un onay kapısına dayanır** (`outbound.auto_send_enabled`, §M); T169 main'de.
- **Kapatmanın defter açığı:** `FAILED POST_RESTORE_DUPLICATE` ile kapatılan satır, eski kopya değerlerine elle getirilene kadar mutabakatta eksik fark, iade kesinti defterinde "taşınmadı" ve ödeme şikâyetinde anomali olarak görünür (§N.8 adım 5; araç T172a).
- **Kesinti algılanana kadar deneme hakkı:** geri yüklemeden sonra imza servisi kesintisi ≈3 dk'da algılanır; o pencerede gönderim işi deneme hakkı harcar (para çıkmaz). Tükenen satır `FAILED` olur ve adım 4 listesinde görünmez (§N.8 adım 3).
- Disk doluluğu için alarm yok (backlog ⚪ `BackupDiskSpaceAlarm`); haftalık elle (§N.1).
- Kilit konteynerin içinde: elle komutlar `exec` ile; `docker compose run` ile açılan ikinci konteyner kilidi görmez (§N.10).
- Geri yüklemeden sonra `msdb` geri yüklenen yedeklerin satırlarını da gösterir (zararsız, §N.10).
- Yedek ile site aynı makinede; ikinci kopya elle (§N.3). Unutulursa disk kaybında yedek de gider.
- Yedeğin yaşı `msdb`'den okunur: dosyanın sonradan elle silinmesini görmez; `RESTORE VERIFYONLY`'den geçmeyen dosya silinir ama `msdb` kaydı kalır.
- `db-backup-full-stale` canlıda tetiklenmedi (eşik 1 saatin altına inmiyor); ifadesi unit testte, aynı biçimdeki log kuralı canlı tetiklendi.
- Express'te yedek sıkıştırması yok: disk ≈ 15 × veritabanı boyutu (saklama artık kesimden önceki tam yedeği de tutar).
- Backend `sa` dışında bir kullanıcıya geçerse `msdb` okuma izni gerekir. Yoksa yedek yaşı hiç yayımlanmaz ya da donar; düzeltme turundan beri `db-monitor-probe-failing` çalar.

## Notlar

- **Working tree:** temiz (worktree `f9-T164`, dal `origin/main` `403fe257`'den).
- **Main CI startup check:** son tamamlanmış 3 run `success` — CI `39615975640` (T167), Docker Publish `39615975624` (T167), Docker Publish `39813399137` (T160c); T160c'nin CI'ı `39813399071` başlangıçta kuyruktaydı, sonra `success`.
- **Devir kaydı:** STATUS_CHANGELOG'a T160c (#365) main `403fe257` merge teyidi (CI `39813399071` ✓, Docker Publish `39813399137` ✓) — T163 dalı aynı satırı yazmıştı; birleştirmede çift kayıt çıkmasın diye satır T163'teki metinle **birebir aynı** yazıldı.
- **Dış varsayımlar (kanıtlı):** (1) Express'te SQL Agent yok → zamanlayıcı dışarıda (sürüm `Express Edition (64-bit)`, EngineEdition 4 ölçüldü); (2) Express'te log yedeği ve FULL model var → ölçüldü (`OK LOG`, `recovery_model_desc FULL`); (3) Express'te sıkıştırma yok → ölçüldü (hata mesajı yukarıda); (4) Docker Desktop (Windows) bind mount'a SQL Server yedek yazabilir → ölçüldü (adım 1); (5) `BACKUP … TO DISK = '/dev/null'` Linux'ta çalışmaz (OS error 31) → integration test gerçek dosya adı kullanır; (6) mssql imajında `bash 5.1`, `flock`, GNU `date`/`find` var → `docker run … --entrypoint /bin/bash` ile ölçüldü; (7) Express sınırı veritabanı başına 10.240 MB, yalnız veri dosyaları — Microsoft'un sürüm karşılaştırması ("Maximum relational database size: 10 GB"); kural kullanılan alanı böler.
- **Paralel dallar:** T163 (#369) da `docker-compose*.yml`, `.env.example`, DEPLOY_RUNBOOK (§L) ve STATUS_CHANGELOG'a yazıyor; T164 compose değişiklikleri ayrı bloklarda, runbook bölümü §N (düzeltme turunda §M → §N: T163 §L, T169 §L ve §M kullanıyor).
- **RAM:** prova en çok 7 konteyner (db, backup, redis, loki, backend, prometheus, grafana); yalnız `-p skinora-t164` projesi açıldı.
