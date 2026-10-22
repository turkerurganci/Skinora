# T164 — Veritabanı yedeği ve geri dönüş planı

**Faz:** F9 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-22

---

## Yapılan İşler

- **Otomatik yedek — `skinora-db-backup` (compose).** SQL Server Express'te SQL Agent yok; zamanlayıcı compose'daki küçük bir konteyner (veritabanının kendi imajı, `sqlservr` başlatılmaz, betik koşar). [`scripts/db-backup/db-backup.sh`](../../scripts/db-backup/db-backup.sh): tam yedek **24 saatte bir**, log yedeği **15 dakikada bir** (betik veritabanını FULL kurtarma modeline alır), her dosya `WITH CHECKSUM` + `RESTORE VERIFYONLY`, yarım dosya `.inprogress` adıyla yazılır ve doğrulanınca yeniden adlandırılır. Takvim **yaşa göre** (en yeni dosya aralıktan eski mi?) — PC yeniden başlayınca `restart: unless-stopped` ile gelir ve ilk dakikada eksik yedeği alır. Veritabanı yoksa / `RESTORING` ise bekler. Log yedeği "tam yedek yok" (Msg 4214) ile reddedilirse tam yedek alır. Parola argv'de değil `SQLCMDPASSWORD` ile. Elle komutlarla zamanlayıcı kilitle ayrılır.
- **Saklama.** 14 gün; **en yeni 2 tam yedek her zaman kalır**; en eski kalan tam yedekten eski log yedekleri silinir; 1 günlük yarım dosyalar silinir; `msdb` geçmişi de budanır.
- **Ayrı yer.** Dosyalar veritabanı volume'ünde değil **host klasöründe** (bind mount, iki konteynerde aynı yol): geliştirme `backups/db`, deneme yayını `backups/trial/db` (`TRIAL_DB_BACKUP_DIR` ile başka diske). `backups/` `.gitignore`'da.
- **Geri yükleme komutları.** `restore <hedef> [stopat]`: en yeni tam (ya da stopat'tan önceki son tam) + sonraki bütün loglar, sırayla, **yeni bir ada** (canlının üstüne asla yazmaz; hedef varsa reddeder), point-in-time için `STOPAT`. `swap <geri-yüklenen>`: canlıyı `Skinora_before_restore_<zaman>` olarak **saklar**, geri yükleneni `Skinora` yapar, yeni zinciri başlatan `postrestore` tam yedeğini alır. Fiziksel dosyalar `<hedef>_<zaman>.mdf` — art arda geri yüklemeler çakışmaz (provada bulunup düzeltildi).
- **Deneme yayını Express.** `docker-compose.trial.yml` `MSSQL_PID=Express`'i zorlar (sahip kararı "SQL Server Express"; `.env.example`'dan kopyalanan `.env.trial` Developer derdi).
- **10 GB alarmı.** Backend `DatabaseStorageMonitorService` (5 dk'da bir, hosted loop — Hangfire değil: iş deposu izlenen veritabanının içinde) kendi bağlantısıyla `sys.database_files`, `SERVERPROPERTY('EngineEdition')` ve `msdb.dbo.backupset`'i okur; `skinora_backend_db_*` serilerini yayımlar. Express'te sınır 10.240 MB, başka sürümde sınır serisi yok (ayarla verilebilir). Dört Grafana kuralı: `db-data-size-warning` (%70, 15 dk, warning), `db-data-size-critical` (%85, 5 dk, critical), `db-backup-full-stale` (26 sa, critical; hiç yedek yoksa da çalar), `db-backup-log-stale` (45 dk, warning, yalnız FULL modelde).
- **Runbook.** DEPLOY_RUNBOOK yeni **§M**: sıklık/saklama/yer, zamanlayıcı seçimi ve gerekçesi, klasör izni (icacls / Linux uid 10001), BitLocker, haftalık şifreli ikinci kopya (GnuPG, §L.2 ile aynı ayarlar), günlük işletim, alarm tasarımı, geri yükleme adımları, **kötü sürüm / kötü migration geri dönüşü** (üç durum + karar kuralı), **geri yükleme sonrası para mutabakatı** (tekrar gönderim, depozit indeksinin yeniden verilmesi, gelen ödemeler; imza servisleri kapalıyken açılış sırası), prova, sınırlar. §K.4 adım 5 ve §K.8'e yedek notu.
- **Doküman.** 05 §2.4 / §8.6 "Backup", "DB backup", "Disaster recovery" satırlarına uygulandı notu + RPO önerisi (v3.32); DOC_CHANGELOG; DEFERRED_BACKLOG yeni satır `PostRestoreChainReconciliationTooling` 🟡 ⚠ PARA (53/159 → 54/159).

## Etkilenen Modüller / Dosyalar

- `docker-compose.yml` — `skinora-db` yedek bind mount'u, yeni servis `skinora-db-backup` (ayrı blok)
- `docker-compose.trial.yml` — `skinora-db` Express + kendi yedek klasörü, `skinora-db-backup` (ayrı blok)
- `.env.example` — yeni "Database backup" bloğu (`DB_BACKUP_*`); `.gitignore` — `backups/`
- `scripts/db-backup/db-backup.sh` (yeni), `scripts/db-backup/db-backup.test.mjs` (yeni), `scripts/db-backup/compose-backup.test.mjs` (yeni)
- `backend/src/Skinora.API/Monitoring/DatabaseStorageMonitorOptions.cs`, `DatabaseStorageProbe.cs`, `PrometheusDatabaseStorageObserver.cs`, `DatabaseStorageMonitorService.cs` (yeni); `Program.cs` (kayıt)
- `backend/tests/Skinora.API.Tests/Unit/Monitoring/PrometheusDatabaseStorageObserverTests.cs`, `Integration/Monitoring/SqlServerDatabaseStorageProbeTests.cs` (yeni)
- `infra/grafana/provisioning/alerting/rules.yml` — 4 kural
- `Docs/DEPLOY_RUNBOOK.md` (§M, §K.4, §K.8, başlık), `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/DOC_CHANGELOG.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Otomatik yedek (tam + gerekiyorsa log/fark; Express'te zamanlayıcı yolu seçilip gerekçelendi; PC yeniden başlayınca kendiliğinden sürer) | ✓ | `skinora-db-backup`, `restart: unless-stopped`; tam 24 sa + log 15 dk, fark yok (gerekçe §M.1). Seçim tablosu §M.2. Prova: konteyner durdurulup başlatılınca (17:10:47 → 17:22:44) ilk tick'te eksik log yedeği alındı; `down -v` + `up` sonrası yeni sunucuda kendiliğinden bekleme → geri yüklemeden sonra devam. Test: "catch-up after a restart", "first tick … full", "Msg 4214 → full" (`db-backup.test.mjs`) |
| 2 | Saklama süresi (eski yedeklerin silinmesi; öneri) | ✓ | 14 gün + en yeni 2 tam her zaman, eski loglar en eski kalan tamla birlikte (§M.1, ⚠). Test: "deletes fulls past the retention…", "never deletes the newest two fulls…", "removes a day-old half-written file"; mutasyon: `MIN_KEEP_FULL=0` ve log-budama kapatma testleri kırdı |
| 3 | Yedeğin veritabanı konteynerinden ayrı yere kopyalanması (host klasörü; çevrimdışı/ikinci kopya önerisi runbook'ta) | ✓ | Bind mount host klasörü; `compose-backup.test.mjs` iki konteynerin aynı host klasörünü aynı yolda gördüğünü, bind olduğunu, deneme yayınının kendi klasörünü kullandığını ve gitignored olduğunu doğrular. Prova adım 8: `down -v` veritabanı volume'ünü sildi, host'ta 31 dosya (23 MB) kaldı. İkinci kopya: §M.3 (haftalık, GnuPG, yer sahibin seçimi) |
| 4 | Geri yükleme provası (test verisi → yedek → DB sil/boz → geri yükle → satır sayıları/örnek kayıt aynı; migration geçmişi tutarlı) | ✓ | Aşağıda "Geri yükleme provası": 32 tablonun satır sayısı + `CHECKSUM_AGG(BINARY_CHECKSUM(*))` birebir, migration 62/62 son `20261021121631_T160c_PlatformNetworkCost`, örnek kayıt (kullanıcı #250) aynı, `DBCC CHECKDB` temiz; bozma + volume kaybı + point-in-time |
| 5 | SQL Server Express 10 GB sınırına alarm (metrik + Grafana kuralı, %70 / %85; metrik kaynağı gerekçeli; testle) | ✓ | Backend serileri (gerekçe §M.5: exporter yok). Unit testler kural ifadelerini metrik adlarıyla eşleştirir; integration test sorguları gerçek SQL Server'da koşar. Canlı: Express'te `data_used 9.699.328 / limit 10.485.760` (test ayarı 10 MB) → `db-data-size-critical` **firing** (17:15:40Z, alıcılar `telegram-critical,email-critical`), `db-data-size-warning` **firing**; `db-backup-log-stale` **firing** (17:17:10Z) ve yedek yeniden başlayınca **normal** |
| 6 | Geri dönüş planı (önceki imaj + son yedek) runbook'ta | ✓ | §M.7: kurulum öncesi zorunlu adımlar (`full predeploy`, commit + son migration notu), üç durum (kötü sürüm → yalnız imaj; kötü migration → ileri düzeltme / Down betiği / point-in-time; veri kaybı → geri yükleme), ghcr `sha-<commit>` imajları |
| 7 | Para işlemleri açısından yedek anı ile geri dönüş arasındaki zincir olaylarının mutabakatı; geri yüklemeden sonra hangi işler koşmalı | ✓ | §M.8: (a) tekrar gönderim (TRON `PENDING` satır yeniden gönderilir, Ethereum günlükten tekrar oynatır; yoksa yeni kimlikle ikinci ödeme) + karşılaştırma SQL'i + düzeltme yolları (AD33–AD37, EMERGENCY_HOLD); (b) depozit indeksinin yeniden verilmesi + SQL; (c) gelen ödemeler → `ReconciliationJob` hemen (`reconciliation.schedule_cron` geçici); açılış sırası imza servisleri ve tünel kapalı. İki eksik araç DEFERRED_BACKLOG `PostRestoreChainReconciliationTooling` |

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
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Var** — `.env.example` `DB_BACKUP_HOST_DIR`, `DB_BACKUP_FULL_INTERVAL_HOURS`, `DB_BACKUP_LOG_INTERVAL_MINUTES`, `DB_BACKUP_RETENTION_DAYS`; backend `DatabaseStorageMonitor__*` (varsayılanlar kodda, compose'a eklenmedi); deneme yayını `TRIAL_DB_BACKUP_DIR` (isteğe bağlı)
- Docker değişikliği: **Var** — yeni servis `skinora-db-backup` (port yok), `skinora-db`'ye host bind mount; deneme yayınında `MSSQL_PID=Express`

## Commit & PR

- Branch: `task/T164-veritabani-yedegi`
- Commit: `95110e5c` — T164: Veritabanı yedeği ve geri dönüş planı (+ rapor/status commit'i)
- PR: #372
- CI: ✗ — run `39824772868` dış nedenle kırmızı (Next.js critical danışması, frontend audit kapısı); T164'e ait iş yeşil, backend iş kolları Lint'e bağlı olduğu için atlandı

## Kolay kararlar (öneriyle kaydedildi — ⚠)

1. **Zamanlayıcı compose konteyneri** (Görev Zamanlayıcı / Hangfire değil) — gerekçe §M.2.
2. **Tam 24 sa + log 15 dk, fark yedeği yok, FULL kurtarma modeli** — RPO ≤ 15 dk (⚠ PARA: kayıp penceresi).
3. **Saklama 14 gün, en yeni 2 tam her zaman** (⚠ PARA).
4. **Alarm eşikleri** %70 warning / %85 critical; tam yedek 26 sa critical; log 45 dk warning.
5. **Metrik backend'den** (`sys.database_files` + `msdb`), exporter yok.
6. **Deneme yayınında `MSSQL_PID=Express` zorlanır.**
7. **İkinci kopya haftalık, GnuPG simetrik** (§L.2 ayarlarıyla), parola cüzdan arşivininkinden ayrı, son 8 hafta (⚠ GÜVENLİK).
8. **Klasör izni** icacls ile yalnız kullanıcı + SYSTEM; BitLocker (⚠ GÜVENLİK).
9. **Geri yükleme karar kuralı:** yedek anından sonra para hareket ettiyse ve eski veritabanı okunabiliyorsa ileri düzeltme; geri yükleme son çare; geri yüklemeden sonra imza servisleri ve tünel mutabakat bitene kadar kapalı (⚠ PARA).
10. **Aylık geri yükleme provası** (`restore Skinora_drill` + `DBCC CHECKDB`, swap yok).

## Sahibe açık sorular

1. **İkinci kopyanın yeri (⚠ PARA):** öneri haftalık şifreli arşiv §L'nin USB'lerinden **ayrı** bir USB/harici diske, ayda bir de şifreli arşiv bir bulut klasörüne. Bulut kabul mü, yalnız USB mi?
2. **Geri yükleme sonrası iki eksik araç (⚠ PARA, `PostRestoreChainReconciliationTooling`):** TRON'da gönderilmiş satırı kapatma aracı ve depozit adres indeksine taban. Mainnet pilotundan (T173) önce mi yapılsın, yoksa bugünkü elle yol (TRON servisi kapalı + eski kopyadan düzeltme; yeni satış açmamak) kabul mü?
3. **05 §8.6 "Encryption at rest — SQL Server TDE":** TDE Express'te yoktur. Öneri: satırı "BitLocker (disk şifreleme) + şifreli makine dışı yedek" olarak değiştirmek (karar değişikliği olduğu için sahibin onayı gerekir; bu görevde değiştirilmedi).

## Known Limitations / Follow-up

- `PostRestoreChainReconciliationTooling` (yukarıda, DEFERRED_BACKLOG §4).
- Yedek ile site aynı makinede; ikinci kopya elle (§M.3). Unutulursa disk kaybında yedek de gider.
- Yedeğin yaşı `msdb`'den okunur: dosyanın sonradan elle silinmesini görmez; `RESTORE VERIFYONLY`'den geçmeyen dosya silinir ama `msdb` kaydı kalır.
- `db-backup-full-stale` canlıda tetiklenmedi (eşik 1 saatin altına inmiyor); ifadesi unit testte, aynı biçimdeki log kuralı canlı tetiklendi.
- Express'te yedek sıkıştırması yok: disk ≈ 14 × veritabanı boyutu.
- Backend `sa` dışında bir kullanıcıya geçerse `msdb` okuma izni gerekir (yoksa yedek yaşı donar ve zamanla eskime alarmına düşer).

## Notlar

- **Working tree:** temiz (worktree `f9-T164`, dal `origin/main` `403fe257`'den).
- **Main CI startup check:** son tamamlanmış 3 run `success` — CI `39615975640` (T167), Docker Publish `39615975624` (T167), Docker Publish `39813399137` (T160c); T160c'nin CI'ı `39813399071` başlangıçta kuyruktaydı, sonra `success`.
- **Devir kaydı:** STATUS_CHANGELOG'a T160c (#365) main `403fe257` merge teyidi (CI `39813399071` ✓, Docker Publish `39813399137` ✓) — T163 dalı aynı satırı yazmıştı; birleştirmede çift kayıt çıkmasın diye satır T163'teki metinle **birebir aynı** yazıldı.
- **Dış varsayımlar (kanıtlı):** (1) Express'te SQL Agent yok → zamanlayıcı dışarıda (sürüm `Express Edition (64-bit)`, EngineEdition 4 ölçüldü); (2) Express'te log yedeği ve FULL model var → ölçüldü (`OK LOG`, `recovery_model_desc FULL`); (3) Express'te sıkıştırma yok → ölçüldü (hata mesajı yukarıda); (4) Docker Desktop (Windows) bind mount'a SQL Server yedek yazabilir → ölçüldü (adım 1); (5) `BACKUP … TO DISK = '/dev/null'` Linux'ta çalışmaz (OS error 31) → integration test gerçek dosya adı kullanır; (6) mssql imajında `bash 5.1`, `flock`, GNU `date`/`find` var → `docker run … --entrypoint /bin/bash` ile ölçüldü; (7) Express sınırı veritabanı başına 10.240 MB, yalnız veri dosyaları — Microsoft'un sürüm karşılaştırması ("Maximum relational database size: 10 GB"); kural kullanılan alanı böler.
- **Paralel dallar:** T163 (#369) da `docker-compose*.yml`, `.env.example`, DEPLOY_RUNBOOK (§L) ve STATUS_CHANGELOG'a yazıyor; T164 compose değişiklikleri ayrı bloklarda, runbook bölümü §M (dosya sonuna, §L'den sonra gelecek şekilde adlandırıldı).
- **RAM:** prova en çok 7 konteyner (db, backup, redis, loki, backend, prometheus, grafana); yalnız `-p skinora-t164` projesi açıldı.
