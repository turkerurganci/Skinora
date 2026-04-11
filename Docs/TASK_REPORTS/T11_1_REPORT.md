# T11.1 — CI Close-out

**Faz:** F0 (T11 kapanış borcu) | **Durum:** ⏳ Devam ediyor (yapım ✓, validator bekleniyor) | **Tarih:** 2026-04-11

---

## Bağlam

T20 validator chat'inde (2026-04-11) ortaya çıktı: main CI pipeline T13 chore'dan (2026-04-09) itibaren kesintisiz FAIL. 7+ merge, CI kırıkken main'e indi. T11 "merge için CI PASS zorunlu" discipline'ı fiilen düşmüştü çünkü CI zaten kırıktı, hiç kimse green beklemedi.

Root cause: `frontend/package-lock.json` Windows dev makinesinde oluşturuldu, `@parcel/watcher-linux-x64-glibc` Linux optional binding'i lockfile'a dahil edilmedi. CI runner Linux olduğu için `npm ci` (strict mode) binding'i bulamıyor → Next.js 16 `next.config.ts` yüklemesi patlıyor → `build` job FAIL → `unit/integration/contract/migration/docker` step'leri SKIPPED.

İronik kontrast: aynı commit'te `docker-publish.yml` frontend Docker image'ını başarıyla build ediyor. Çünkü `frontend/Dockerfile` `npm install` kullanıyor (strict değil) ve içerisi zaten Linux.

F1 veri katmanı için migration dry-run kritik: T17-T20 entity şemaları CI pipeline'da **hiç doğrulanmadı**. Bu blind spot T11.1 öncesi F1 Gate Check'in geçerliliğini tehlikeye atıyordu.

---

## Yapılan İşler

### Frontend lockfile
- `frontend/package-lock.json`: Linux `node:20-slim` container'da (internal FS, bind mount değil — Windows Docker Desktop bind mount ~10 dk alıyordu) yeniden oluşturuldu. `@parcel/watcher` optional binding'leri tüm platformlar için dahil edildi: linux-{x64,arm,arm64}-{glibc,musl}, darwin-{x64,arm64}, win32-{x64,ia32,arm64}, freebsd-x64, android-arm64.
- `scripts/regen-frontend-lockfile.sh`: Windows dev için lockfile regenerate helper. Internal container FS kullanır (bind mount yok), MSYS_NO_PATHCONV ile path conversion sorunlarını aşar, idempotent cleanup trap'i içerir.

### CI workflow
`.github/workflows/ci.yml` güncellendi:

1. **Build job, Frontend build step:** `npm ci` → `npm ci --include=optional`. Artık lockfile'daki Linux binding'leri pozitif şekilde install edilir.
2. **Unit test job, filter:** `FullyQualifiedName!~.Integration` → `FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract`. Contract testleri ayrı job'a kaydırıldı, duplicate execution yok.
3. **Contract test job:** Placeholder `echo ::notice::T12 sonrası` → gerçek `dotnet test --filter "FullyQualifiedName~.Contract"`. T12 `ContractTestBase` smoke testleri (5 test) CI'da koşar.
   - `needs: integration-test` → `needs: build` (paralelizasyon, integration-test'i beklemiyor)
   - TRX logger + artifact upload (7 gün retention)
4. **Migration dry-run job:** Placeholder `echo ::notice::T28 sonrası` → gerçek `dotnet ef dbcontext info`. `Skinora.Shared` DbContext project + `Skinora.API` startup project, `AppDbContext` context. Model validation (OnModelCreating çağırır, T17-T20 entity konfigürasyonlarını doğrular). Gerçek migration script dry-run T28 sonrası (initial migration oluşturulunca) follow-up olarak eklenecek.
   - `dotnet tool install --global dotnet-ef --version 9.0.3` adımı eklendi (EF Core 9 ile uyumlu)
   - `needs: integration-test` → `needs: build`

### Cycle 2: SQLite compatibility fix (6c61b5b)
İlk CI run'ında (24287178908) integration-test job SQLite `near 'max': syntax error` ile FAIL. Root cause: T19'da eklenen `TransactionHistoryConfiguration.AdditionalData` property'si için `HasColumnType("nvarchar(max)")` kullanılıyordu. `Skinora.API.Tests.Integration` Outbox testleri SQLite kullanıyor ve `AppDbContext.OnModelCreating` tüm modül assembly'lerini yüklediği için TransactionHistory config SQLite tablo oluşturmaya çalışırken patlıyordu. Windows lokal'de passing olması SQLite native library sürüm farkından kaynaklıydı (Windows toleranslı, Linux CI strict).

Fix: `HasColumnType("nvarchar(max)")` kaldırıldı, EF Core default'a bırakıldı (`string?` → SQL Server `nvarchar(max)`, SQLite `TEXT` — her ikisi de max capacity, SQL Server şema davranışı değişmez). Lokal validasyon: 20/20 Outbox SQLite tests ✓ + 4/4 TransactionHistory TestContainers MsSql tests ✓. 2. CI run'ında (24287464091) tüm 7 step ✓ PASS.

Bu fix T11.1'in "T17-T20 blind spot" gerekçesini retroaktif olarak kanıtladı: CI pipeline'ı açılır açılmaz pre-existing bir bug yakalandı.

### BYPASS_LOG retro-kayıt
`Docs/BYPASS_LOG.md`'ye 2026-04-09/10 döneminden gelen direct push ihlalleri retro-aktif eklendi:
- `0a50389` (T14 Steam Sidecar)
- `6314591` (T15 Blockchain Sidecar)
- `e8ddd38` (T16 Monitoring altyapısı)

Ayrıca T17-T19'un task isolation ihlali notu (T20 PR #11 içine gömüldü, INSTRUCTIONS.md §3.1 "her task ayrı chat'te" kuralı ihlal) dokümante edildi — bu direct-push bypass değil, process violation.

---

## Etkilenen Modüller / Dosyalar

**Değişen:**
- `.github/workflows/ci.yml` (frontend build step, unit-test filter, contract-test job, migration-dry-run job)
- `frontend/package-lock.json` (cross-platform lockfile regenerate)
- `Docs/BYPASS_LOG.md` (T14-T16 retro kayıtları + T17-T19 process violation notu)
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionHistoryConfiguration.cs` (cycle 2 SQLite fix — `HasColumnType("nvarchar(max)")` kaldırıldı)

**Yeni:**
- `scripts/regen-frontend-lockfile.sh` (Windows dev için lockfile regenerate helper)
- `Docs/TASK_REPORTS/T11_1_REPORT.md` (bu dosya)

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Lint: ✓ PASS | ✓ | CI run #24287464091 "1. Lint" 1m10s ✓ (dotnet format + frontend lint + 2 sidecar typecheck) |
| 2 | Build: frontend `@parcel/watcher` sorunu çözülür | ✓ | CI run #24287464091 "2. Build" 1m6s ✓. 1. run'da FAIL, lockfile regenerate + `npm ci --include=optional` sonrası 2. run'da ✓ |
| 3 | Unit test: dotnet test `!~.Integration&!~.Contract` filter | ✓ | CI run #24287464091 "3. Unit test" 50s ✓ |
| 4 | Integration test: TestContainers MsSql GHA runner'da | ✓ | CI run #24287464091 "4. Integration test" 7m1s ✓. 1. run'da SQLite `nvarchar(max)` syntax error (T19 pre-existing bug), fix sonrası 2. run'da ✓ (TestContainers MsSql + SQLite hibrit Outbox testleri) |
| 5 | Contract test: sidecar ↔ backend JSON schema doğrulaması | ~ Kısmi | CI run #24287464091 "5. Contract test" 51s ✓ (5 T12 smoke test). Gerçek sidecar schema doğrulaması F4 T65/T68 follow-up |
| 6 | Migration dry-run: EF Core migration CI'da çalışır — T17-T20 şemaları doğrulanır | ~ Kısmi | CI run #24287464091 "6. Migration dry-run" 46s ✓ (`dotnet ef dbcontext info` ile AppDbContext + model validation; T17-T20 entity konfigürasyonları CI'da ilk kez doğrulandı). Gerçek migration script dry-run T28 follow-up |
| 7 | Docker build: 4-component matrix temiz build verir | ✓ | CI run #24287464091 "7. Docker build (×4)": backend 2m55s, frontend 17s, sidecar-steam 19s, sidecar-blockchain 18s, hepsi ✓ |
| 8 | Main branch üzerinde en az 1 ardışık CI run tamamen ✓ PASS olmalı | (post-merge) | Squash merge sonrası main CI run — validator chat'inde doğrulanacak |
| 9 | F0 Gate Check yeniden değerlendirilir | (post-merge) | T11.1 validator PASS + main CI green sonrası F0 Gate Check rapor güncellemesi (validator chat scope'u) |

---

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 7 CI step (Lint, Build, Unit, Integration, Contract, Migration, Docker) sırasıyla ✓ | (TBD) | |
| 2 | Bir özellik branch'inde + main push'unda CI yeşil | (TBD) | |
| 3 | T17-T20 migration script'leri CI migration dry-run'dan temiz geçiyor | ~ | `dotnet ef dbcontext info` ile DbContext + model validation (migration script değil). Gerçek script dry-run T28 sonrası |
| 4 | BYPASS_LOG.md T14-T19 disiplin ihlal kayıtları retro-aktif not düşüldü | ✓ | T14/T15/T16 direct push retro + T17-T19 process violation notu eklendi |

---

## Test Sonuçları

### Lokal

| Tür | Sonuç | Detay |
|---|---|---|
| dotnet format | ✓ clean | `dotnet format --verify-no-changes` exit 0 |
| Contract test filter | ✓ 5 test | `dotnet test --filter "FullyQualifiedName~.Contract" --list-tests` → 5 `Skinora.Shared.Tests.Contract.*` |
| Unit test filter (yeni) | ✓ 160 test | `dotnet test --filter "!~.Integration&!~.Contract" --list-tests` → 160 test |
| Integration test filter | ✓ 146 test | `dotnet test --filter "~.Integration" --list-tests` → 146 test |
| `dotnet ef dbcontext info` | ✓ exit 0 | AppDbContext + OnModelCreating + 2 uyarı (Transaction query filter — pre-existing) |
| Frontend lockfile | ✓ cross-platform | Linux container'da regenerate, 13 binding platform dahil |

### CI Runs (PR #12)

| # | Run ID | Conclusion | Özet |
|---|---|---|---|
| 1 | [24287178908](https://github.com/turkerurganci/Skinora/actions/runs/24287178908) | ✗ FAIL | Build ✓ (lockfile fix çalıştı), Integration test ✗ SQLite `near 'max': syntax error` (T19 TransactionHistoryConfiguration `HasColumnType("nvarchar(max)")` pre-existing bug, fix için cycle 2) |
| 2 | [24287464091](https://github.com/turkerurganci/Skinora/actions/runs/24287464091) | ✓ PASS | 7 step + CI Gate hepsi ✓. Tüm job'lar aktivasyon sonrası CI'da ilk kez koştu — pre-existing bug yakalandı ve düzeltildi, T11.1 "blind spot" gerekçesi kanıtlandı |

### Job Detay (Run #24287464091)

| Job | Süre | Sonuç |
|---|---|---|
| 1. Lint | 1m10s | ✓ |
| 2. Build | 1m6s | ✓ |
| 3. Unit test | 50s | ✓ |
| 4. Integration test | 7m1s | ✓ |
| 5. Contract test | 51s | ✓ (5 smoke) |
| 6. Migration dry-run | 46s | ✓ (`dotnet ef dbcontext info`) |
| 7. Docker build × 4 | 17-2m55s | ✓ (4 component paralel) |
| CI Gate | 3s | ✓ |

---

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env: Yok
- Docker: Yok (mevcut Dockerfile'lar değişmedi, sadece CI'da test ediliyor)
- CI/GHA: `dotnet-ef` 9.0.3 global tool CI runner'a kurulumu eklendi (migration-dry-run job)

---

## Mini Güvenlik Kontrolü

| Kontrol | Sonuç | Detay |
|---|---|---|
| Secret sızıntısı | ✓ Yok | CI workflow değişiklikleri, lockfile — secret yok |
| Auth/authorization | ✓ N/A | Workflow değişikliği, production auth etkilenmedi |
| Input validation | ✓ N/A | — |
| Yeni bağımlılık | ✓ dotnet-ef 9.0.3 | Resmi Microsoft tool, major version pinli |

---

## Commit & PR

- **Branch:** `task/T11.1-ci-closeout`
- **PR:** [#12](https://github.com/turkerurganci/Skinora/pull/12)
- **Commits (yapım):**
  - `985007f` — T11.1: CI close-out — frontend lockfile + contract/migration job aktivasyon
  - `6c61b5b` — T11.1 fix: TransactionHistory.AdditionalData — HasColumnType('nvarchar(max)') kaldirildi
- **CI:** ✓ PASS — run [24287464091](https://github.com/turkerurganci/Skinora/actions/runs/24287464091) (2. cycle, 9m toplam)

---

## Known Limitations / Follow-up

| # | Konu | Hangi task | Açıklama |
|---|---|---|---|
| 1 | Contract test sadece smoke test (5 test) | F4 T65/T68 | Gerçek sidecar ↔ backend JSON schema doğrulaması sidecar entegrasyonları (T65 Steam trade offer, T68 webhook) içinde eklenecek. ContractTestBase altyapısı T12'de hazır |
| 2 | Migration dry-run `dotnet ef dbcontext info` ile sınırlı | T28 | Gerçek `dotnet ef migrations script --dry-run` F1 T28 (initial migration) sonrası eklenecek. Şu an sadece DbContext + model validation yapılıyor (T17-T20 entity konfigürasyonları doğrulanır, ancak migration script üretilmiyor) |
| 3 | Windows dev'in frontend lockfile regenerate akışı | — | `scripts/regen-frontend-lockfile.sh` ile dokümante. Windows'ta `npm install` çalıştırıldıktan sonra lockfile drift olabilir — dev disiplini gerektiriyor. Follow-up: husky pre-commit hook ile lockfile platform drift detection |
| 4 | `dbcontext info` 2 EF Core query filter uyarısı veriyor | Değerlendir | `Transaction` ↔ `BlockchainTransaction` ve `Transaction` ↔ `TransactionHistory` navigation'larında "required navigation + query filter" uyarısı. Pre-existing (T19-T20'den), T11.1 sonrası ayrı audit konusu olabilir — şu an job'u FAIL yapmıyor |

---

## Notlar

- **Lockfile regenerate yaklaşımı:** İlk denemede Windows Docker Desktop bind mount (`docker run -v c:/...:/app`) kullandım. 7+ dk sonra hâlâ 330/392 paketteydi (Windows WSL2 mount overhead inanılmaz yavaş). Copy in/out yaklaşımına geçildi: `docker run -d` ile idle container, `docker cp` ile package.json in, `docker exec` ile `npm install`, `docker cp` ile package-lock.json out. Aynı iş 7 dk'da tamamlandı — internal FS I/O hızı ile sınırlı. `scripts/regen-frontend-lockfile.sh` bu yaklaşımı standardize eder.
- **`--include=optional` ilavesi:** Aslında npm 10+ için `optional` default'ta `true`. Açıkça belirtmek, ileride npm config değişse de CI davranışını güvenceye alır (belt-and-suspenders).
- **Contract/migration job'larının `needs: integration-test` → `needs: build` değişimi:** Paralelizasyon için. Contract test ve migration dry-run integration test sonuçlarına bağımlı değil; 09 §21.4 sıralama mantıksal (visual ordering job adlarında), runtime bağımlılık değil.
- **T28'e kadar migration dry-run "kısmi":** Strict kabul kriteri metni "migration CI'da çalışır" diyor. `dbcontext info` bir migration komutu değil; model inspection. T28'de initial migration oluşturulunca `dotnet ef migrations script --no-build --idempotent --output migrations.sql` eklenecek ve schema snapshot'ı artifact olarak upload edilecek.
