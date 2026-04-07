# T11 — CI/CD Pipeline

**Faz:** F0 | **Durum:** ⛔ BLOCKED (EXTERNAL_BLOCKER) | **Doğrulama:** ~ Kısmi (kod ✓, branch protection ⛔) | **Tarih:** 2026-04-08

---

## ⛔ BLOCKED Bilgisi

- **Alt tür:** EXTERNAL_BLOCKER
- **Neden:** Kabul kriteri 2 (`Branch protection: main'e doğrudan push yasağı, CI geçmeden merge yasağı`) sistem düzeyinde enforce edilemiyor. **Hem klasik branch protection hem de yeni Repository Rulesets özel repo'larda GitHub Pro paid feature** — current plan (Free) + private repo kombinasyonunda enforce yolu yok.
- **Hata yanıtı:** `gh api PUT branches/main/protection` → HTTP 403 `"Upgrade to GitHub Pro or make this repository public to enable this feature"`. Aynı yanıt `gh api POST rulesets` için de geliyor.
- **Etkilenen kabul kriteri:** Sadece kriter 2. Kriter 1 (workflow yapısı), kriter 3 (branch stratejisi + develop branch), kriter 4 (docker publish) tam karşılandı.
- **Etkilenen task'lar:** T12+ tüm task'lar — INSTRUCTIONS.md §3.2 "T11 sonrası rejim" sistem-enforced değil, **disiplin-enforced** olacak.
- **Etkilenen dokümanlar:** `Docs/CI_CD_SETUP.md` (gerçek durumu yansıtacak şekilde güncellendi), `Docs/INSTRUCTIONS.md §3.2` (rejim açıklaması güncellenebilir).

## Çözüm Önerileri

1. **GitHub Pro'ya yükselt** (~$4/ay) — branch protection + rulesets aktif olur, T11 tam kapanır
2. **Repo'yu organization'a taşı** — Free organization plan'da private repo rulesets desteği var (doğrulanmalı)
3. **Discipline-only protection** — sistem enforcement yok, manuel disiplin (every PR validator chat → `gh pr merge`). Lokal git pre-push hook ile main'e direkt push'u local'de bloklayabilirim
4. **Repo'yu public yap** — branch protection free olur, ama iş kuralları açığa çıkar (önerilmez)

## Proje Sahibi Kararı

- **Karar:** Henüz alınmadı (2026-04-08 close-out sırasında keşfedildi, kullanıcı bilgilendirildi)
- **Tarih:** —

---

## Yapılan İşler

### GitHub Actions workflow'ları

- **`.github/workflows/ci.yml`** ([.github/workflows/ci.yml](.github/workflows/ci.yml)): 09 §21.4'teki 6 adımlı sıralamayı bire bir uygulayan PR/push CI pipeline'ı.
  - **Job 1 — `lint`:** `dotnet format Skinora.sln --verify-no-changes --severity error` (backend) + `node --check server.js` (frontend, sidecar-steam, sidecar-blockchain placeholder'larında syntax doğrulama). Lint başarısızsa pipeline durur.
  - **Job 2 — `build`:** `dotnet restore` + `dotnet build Skinora.sln --configuration Release`. NuGet cache'i `actions/cache@v4` ile `~/.nuget/packages` üzerinden tutulur (key: csproj hash).
  - **Job 3 — `unit-test`:** `dotnet test --filter "FullyQualifiedName!~.Integration"`. Sonuçlar TRX olarak `backend/TestResults/`'a yazılır, `actions/upload-artifact@v4` ile 7 gün saklanır.
  - **Job 4 — `integration-test`:** `dotnet test --filter "FullyQualifiedName~.Integration"`. Aynı upload-artifact yapısı.
  - **Job 5 — `contract-test` [PLACEHOLDER]:** Şu an `echo "::notice::T12 sonrası dolacak"` çıkarır. Job sırası 09 §21.4'e uymak için tanımlı; gerçek implementasyon T12 (test altyapısı) ile gelecek. `needs: integration-test`.
  - **Job 6 — `migration-dry-run` [PLACEHOLDER]:** Aynı şekilde placeholder. Gerçek implementasyon F1 T28 (initial migration) ile gelecek. `needs: integration-test`.
  - **Job 7 — `docker-build-check`:** 4 servis (backend, frontend, sidecar-steam, sidecar-blockchain) için matrix-strategy ile paralel `docker/build-push-action@v5` (push: false). Her image için ayrı GHA cache scope'u. `fail-fast: false`.
  - **`ci-gate`:** Tüm job'ların sonucunu toplayan tek "required check". Branch protection bu job'u required olarak işaretler — yapısal değişikliklerde required check listesini güncellemeye gerek kalmaz. `if: always()`, `contains(needs.*.result, 'failure' || 'cancelled')` kontrolüyle exit 1.
  - **Concurrency:** `ci-${{ github.workflow }}-${{ github.ref }}` group + `cancel-in-progress: true` — aynı PR'a yeni push gelirse önceki run iptal edilir, runner kotası korunur.
  - **Trigger:** `pull_request` ve `push` (main, develop). Develop branch henüz yok ama workflow ileride hazır.

- **`.github/workflows/docker-publish.yml`** ([.github/workflows/docker-publish.yml](.github/workflows/docker-publish.yml)): main'e merge sonrası 4 servis image'ini ghcr.io'ya push eden ayrı workflow.
  - **Trigger:** `push` (main) + `workflow_dispatch` (manuel re-run için). PR'larda çalışmaz — registry kirliliği ve fork PR'larından secret sızıntısı önlemi.
  - **Permissions:** Job-level explicit `contents: read` + `packages: write`. Repo geneli "Read and write" yerine bu sıkı izin tercih edildi (least privilege).
  - **Image isimleri:** `ghcr.io/${{ github.repository }}-<component>` formatında (`docker/metadata-action@v5` lowercase normalize eder).
  - **Tag stratejisi:** `latest` (rolling) + `<short-sha>` (immutable, iz sürülebilirlik) + `main-<run-number>` (sıralı, rollback için). 3 tag her image'a aynı anda yapışır.
  - **Authentication:** `secrets.GITHUB_TOKEN` (otomatik sağlanır, ek secret kurulumu gerektirmez).
  - **Cache:** `cache-from`/`cache-to` GHA cache + scope per component. CI'daki `docker-build-check` cache'i ile karışmasın diye `publish-` prefix.

### Yardımcı dosyalar

- **`.github/pull_request_template.md`** ([.github/pull_request_template.md](.github/pull_request_template.md)): 09 §21.3 PR kurallarına uygun şablon. Bölümler: task/konu, ne/neden yapıldı, etkilenen modüller, doküman referansları, test checklist (unit/integration/migration/para hesaplaması), INSTRUCTIONS.md §3.6 Katman 1 mini güvenlik checklist, validator durumu, notlar.

- **`Docs/CI_CD_SETUP.md`** ([Docs/CI_CD_SETUP.md](Docs/CI_CD_SETUP.md)): Repo ayarlarında manuel yapılması gereken adımların kılavuzu — workflow envanteri, branch protection kuralları (main + develop için ayrı tablo), secret roadmap (T78+ için), repository settings (squash merge default'ı), operasyonel notlar (image isimleri, tag stratejisi, CI süre tahmini), follow-up listesi.

### dotnet format whitespace cleanup

- **`backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs`**: T11 lint kapısı kurulduğunda T01–T10 birikmiş whitespace borcu açığa çıktı. `dotnet format whitespace` ile tek dosyada 84 satırlık (`-42/+42`) indent düzeltmesi yapıldı (case bloğu içindeki `{ }` indent stili). `git diff -w` boş çıkar — sadece whitespace, davranış değişmez. Tüm 136 test format sonrası geçer.

### Branch stratejisi notu

T11 kapsamında **`develop` branch'i fiziksel olarak oluşturulmadı** — bu task sadece kod tabanı değişikliğidir, branch oluşturma `Docs/CI_CD_SETUP.md §2.3`'te manuel adım olarak belgelendi. Workflow'lar develop'a karşı çalışacak şekilde hazır (`branches: [main, develop]`). T11 main'e merge sonrası kullanıcı bir kez `git checkout -b develop && git push -u origin develop` çalıştırarak develop'u açacak.

---

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `.github/workflows/ci.yml`
- `.github/workflows/docker-publish.yml`
- `.github/pull_request_template.md`
- `Docs/CI_CD_SETUP.md`
- `Docs/TASK_REPORTS/T11_REPORT.md`

**Değişen:**
- `backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs` (whitespace-only autofix)
- `Docs/IMPLEMENTATION_STATUS.md` (T11 satırı `⏳ Devam ediyor`'a güncellenecek — doğrulama PASS sonrası `✓ Tamamlandı`)

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | GitHub Actions workflow: Lint → Build → Unit test → Integration test → Contract test → Migration dry-run | ✓ | `.github/workflows/ci.yml` 7 job (5 gerçek + 2 placeholder), 09 §21.4 sırası `needs:` zinciri ile garanti. Job adları "1. Lint", "2. Build", "3. Unit test", "4. Integration test", "5. Contract test", "6. Migration dry-run" — sıra workflow UI'da görünür |
| 2 | Branch protection: main'e doğrudan push yasağı, CI geçmeden merge yasağı | ⛔ EXTERNAL_BLOCKER | Workflow tarafı hazır (`ci-gate` aggregate job required check için hedef). T11 close-out sırasında `gh api PUT branches/main/protection` çağırıldığında **HTTP 403 "Upgrade to GitHub Pro"** yanıtı geldi — özel repo'larda branch protection ve rulesets paid feature. Sistem enforcement mümkün değil, sadece manuel disiplin uygulanabilir. Bkz. `## ⛔ BLOCKED Bilgisi` bölümü |
| 3 | Branch stratejisi: main, develop, feature branches | ~ | Workflow `branches: [main, develop]` ile her ikisini izler. `develop` branch'i fiziksel olarak henüz yok — `Docs/CI_CD_SETUP.md §2.3`'te kullanıcının tek satır git komutu ile oluşturacağı belgelendi. 09 §21.1 strateji tablosu kod değişikliği gerektirmez; T11 sonrası akış aktiftir |
| 4 | Docker image build ve push (ghcr.io) | ✓ | `.github/workflows/docker-publish.yml` 4 servis için matrix strategy ile build & push, `secrets.GITHUB_TOKEN` ile authenticate, 3 tag stratejisi (latest + short-sha + main-runnumber). PR'da çalışmaz, sadece main push'unda |

**Özet:** 4 kriterden 3'ü tam ✓ (1, 3, 4), 1'i ⛔ EXTERNAL_BLOCKER (2 — branch protection paid feature). Kod tarafı tüm değişiklikler main'e merge edildi, develop branch oluşturuldu, CI yeşil. Sadece sistem-enforced branch protection eksik.

---

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 09 §21.4'teki 6 adımlı sıralama doğru mu? | ✓ | `ci.yml` job adları ve `needs:` graph'ı: lint → build → (unit-test, integration-test, docker-build-check) → integration-test → (contract-test, migration-dry-run). 6 adım sırası workflow yaml'da görünür ve job adlarında numaralı |
| 2 | Branch protection kuralları aktif mi? | ⛔ EXTERNAL_BLOCKER | T11 close-out sırasında aktifleştirme denendi → HTTP 403 "Upgrade to GitHub Pro". Hem `gh api PUT branches/main/protection` hem `gh api POST rulesets` aynı yanıtı verdi. Free plan + private repo'da enforce yolu yok |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ 52/52 passed | `dotnet test Skinora.sln --configuration Release --no-build --filter "FullyQualifiedName!~.Integration"` → Skinora.Shared.Tests: 37 passed (233ms), Skinora.API.Tests (Logging): 15 passed (156ms). Diğer test projeleri: matched 0 (henüz unit test yok) |
| Integration | ✓ 84/84 passed | `dotnet test Skinora.sln --configuration Release --no-build --filter "FullyQualifiedName~.Integration"` → Skinora.API.Tests (Integration): 84 passed (3m 22s). TestContainers/Hangfire bypass çalışıyor |
| Lint | ✓ Clean | `dotnet format Skinora.sln --verify-no-changes --severity error` → exit 0 (whitespace cleanup sonrası) |
| Build | ✓ Clean | `dotnet build Skinora.sln --configuration Release` → 0 warning, 0 error, 9.88s |
| **Toplam** | **✓ 136/136** | Format değişikliği regression oluşturmadı |

**CI workflow'unun gerçek run'ı:** Bu task local environment'ta yazıldı. İlk gerçek GHA run T11 PR açıldığında olacak — workflow validation GHA UI'da doğrulanacak.

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ~ Kısmi: kod tarafı ✓ PASS (validator chat'i, 2026-04-07), branch protection ⛔ EXTERNAL_BLOCKER |
| Bulgu sayısı | 0 (validator) + 1 (close-out keşfi: GitHub Pro gerekiyor) |
| Düzeltme gerekli mi | Evet — proje sahibi karar bekliyor (Pro upgrade / org transfer / discipline-only) |

**Validator notları (2026-04-07):**
- Kabul kriterleri 1 ve 4: ✓ tam karşılandı (workflow yapısı + ghcr.io push)
- Kabul kriterleri 2 ve 3: validator tarafından `~ Kısmi` olarak işaretlendi ("UI tarafı validator chat'inden gözlemlenemez")
- Validator önceden uyardı: "PASS için owner kanıtı gerekli (gh api repos/.../branches/main/protection)"
- Tek metrik farkı: rapor "52 unit + 84 integration" (filter ile), validator "37 + 99" (full sweep) — toplam 136/136 PASS aynı, anlamsal farksız

**Close-out (2026-04-08) keşifleri:**
- ✓ Kabul kriteri 3 (develop branch): `gh api POST git/refs` ile `develop` branch `0327315`'ten oluşturuldu, push edildi
- ⛔ Kabul kriteri 2 (branch protection): aktifleştirme denendi → HTTP 403, GitHub Pro gerekiyor. Validator'ın "owner kanıtı bekler" notu artık "owner kararı bekler" haline geldi (paid feature kararı)

---

## Altyapı Değişiklikleri

- **Migration:** Yok
- **Config/env değişikliği:** Yok (CI workflow'ları)
- **Docker değişikliği:** Yok (mevcut Dockerfile'lar değiştirilmedi, sadece build doğrulaması eklendi)
- **GitHub repo settings değişikliği gerekli:** Evet — `Docs/CI_CD_SETUP.md` kılavuzu ile elle yapılacak (T11 merge sonrası tek seferlik):
  1. Settings → Actions → Workflow permissions: Read and write
  2. Settings → Branches → main/develop için branch protection rule
  3. `git checkout -b develop && git push -u origin develop`

---

## Mini Güvenlik Kontrolü (INSTRUCTIONS.md §3.6 Katman 1)

| Kontrol | Sonuç | Detay |
|---|---|---|
| Secret sızıntısı | ✓ Yok | `grep -ri "password\|secret\|api_key\|private_key\|token"` → tek match `secrets.GITHUB_TOKEN` referansı (otomatik sağlanan, hardcoded değil) |
| Auth/authorization | ✓ Sıkı | `packages: write` yalnızca `docker-publish.yml` job-level'da, `ci.yml`'da yok. Repo-level "Read and write" yerine job-level least privilege tercih edildi |
| Input validation | ✓ N/A | Workflow'larda kullanıcı input'u yok (sadece git ref'leri ve matrix değerleri) |
| Yeni dış bağımlılık | ✓ Güvenli | Resmi GitHub Actions: `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/setup-node@v4`, `actions/cache@v4`, `actions/upload-artifact@v4`, `docker/setup-buildx-action@v3`, `docker/login-action@v3`, `docker/build-push-action@v5`, `docker/metadata-action@v5`. Hepsi major version pinli |

**Ek güvenlik notu:** `ci.yml` `pull_request` trigger'ı kullanır (`pull_request_target` değil) — fork PR'larından gelen kod secret'lara erişemez. Repo settings'te "Run workflows from fork pull request" kapatılmasıyla ek koruma `CI_CD_SETUP.md §2.1`'de belirtildi.

---

## Commit & PR

- **Branch:** `task/T11-cicd-pipeline`
- **Pre-merge commits (branch):**
  - `3c542f9` — chore: dotnet format whitespace autofix on T01-T10 codebase
  - `ef5499d` — T11: CI/CD pipeline workflows ve setup kilavuzu
- **PR:** [#1](https://github.com/turkerurganci/Skinora/pull/1) — squash merged
- **Squash commit (main):** `8869872` — "T11: CI/CD pipeline (#1)"
- **CI run:** [#24103601508](https://github.com/turkerurganci/Skinora/actions/runs/24103601508) — ✓ PASS (10 job + CI Gate, ~4 dk)
  - 1. Lint ✓ 38s
  - 2. Build ✓ 49s
  - 3. Unit test ✓ 45s
  - 4. Integration test ✓ 1m 03s
  - 5. Contract test (placeholder) ✓ 2s
  - 6. Migration dry-run (placeholder) ✓ 3s
  - 7. Docker build × 4 (matrix) ✓ paralel
  - CI Gate ✓ 2s

---

## Known Limitations / Follow-up

| # | Konu | Hangi task | Açıklama |
|---|---|---|---|
| 1 | Contract test job'u placeholder | T12 | T12 test altyapısı kurulduktan sonra `contract-test` job'u sidecar↔backend JSON schema doğrulama çalıştıracak. Şu an `echo` ile geçer (`::notice::` ile UI'da banner görünür) |
| 2 | Migration dry-run job'u placeholder | T28 | F1 T28 initial migration sonrası `migration-dry-run` job'u staging DB'ye karşı `dotnet ef database update --dry-run` çalıştıracak |
| 3 | Test filter trait migration | T12 sonrası | Şu an `FullyQualifiedName~.Integration` namespace bazlı filter. T12 `[Trait("Category", "Integration")]` attribute'larını eklediğinde `Category=Integration` formuna geçirilebilir (daha açık) |
| 4 | Frontend/sidecar gerçek lint | T13/T14/T15 | Şu an `node --check server.js` placeholder syntax check. Gerçek ESLint T13 (Next.js), T14/T15 (sidecar) iskeletleri kurulduktan sonra eklenecek |
| 5 | CD (deploy) workflow | F0 sonrası | 05 §8.4'te `SSH → docker compose pull && up -d` tanımlı ama T11 kabul kriterlerinde yok. Staging deploy F0 sonrası ayrı task olarak ele alınacak |
| 6 | ~~Develop branch fiziksel olarak yok~~ | ✓ T11 close-out | `gh api POST git/refs` ile `develop` branch main HEAD'inden (`0327315`) oluşturuldu, push edildi |
| 7 | **Branch protection UI ayarları** | ⛔ EXTERNAL_BLOCKER | `gh api PUT branches/main/protection` ve `gh api POST rulesets` çağrıldığında HTTP 403 "Upgrade to GitHub Pro". Free plan + private repo'da branch protection ve rulesets çalışmıyor. Çözüm proje sahibi kararı bekliyor — bkz. `## ⛔ BLOCKED Bilgisi` |
| 8 | T09/T10 commit hash drift discovery | Follow-up | T11 close-out sırasında keşfedildi: `IMPLEMENTATION_STATUS.md`'de T10 commit hash'i `34794a0` olarak yazılı ama bu hash sadece local'deydi (squash merge yapıldı ama push edilmedi). T09 ve T10'un kod içeriği T11 squash merge commit'i `8869872` içine gömülü olarak origin/main'e geldi. T10 satırı `8869872 (T11 ile)` olarak güncellenebilir veya `(squash)` haline çevrilebilir — bu T11 scope'u dışında, ayrı bir housekeeping task olarak ele alınabilir. **Veri kaybı yok**, sadece traceability noksanı |
| 9 | Node.js 20 actions deprecation uyarısı | F0 sonrası | İlk CI run'da GHA `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/setup-node@v4`, `actions/cache@v4` için "Node.js 20 deprecated, June 2026'da Node.js 24 default olacak" uyarısı verdi. Şu an bir şey kırılmıyor. Action major version'ları güncellendiğinde otomatik düzelir; gerekirse `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` env ile opt-in yapılabilir |

**T11 sonrası rejim:** INSTRUCTIONS.md §3.2 — "T11 tamamlandıktan sonra kalan tüm task'lar için zorunlu rejim haline gelir, istisnasız". Yani T12'den itibaren her task için PR mecbur, CI PASS mecbur, branch protection aktif (kullanıcı UI ayarlarını yaptıktan sonra).

---

## Notlar

- **T01–T10 birikmiş whitespace borcu:** T01–T10 sırasında `dotnet format` kapısı yoktu. T11 lint kapısını kurar kurmaz `ExternalIdempotencyService.cs`'de 84 satırlık whitespace sapması açığa çıktı. Proje sahibi onayıyla `dotnet format whitespace` ile autofix çalıştırıldı, davranış değişikliği yok (`git diff -w` boş, 136 test PASS). Bu T01–T10 retroaktif düzeltmesi olarak T11 commit'inin parçası — INSTRUCTIONS.md §5 "tam çözümü ilk seferde sun" prensibi gereği.
- **Lint severity seçimi:** `--severity error` kullanıldı (info/warn değil) — ileride yeni kurallar (örn: `var` zorunluluğu) eklendiğinde mevcut kodu kırmasın diye konservatif. Strict format kuralları T12 ile birlikte `Directory.Build.props` üzerinden eklenebilir.
- **`ci-gate` pattern'ı:** Tek required check approach'u tercih edildi (her job ayrı required check yerine). Avantaj: workflow yapısı değişince (yeni job ekle/çıkar) branch protection ayarlarını her seferinde güncellemek gerekmez. Dezavantaj: hangi job fail ettiğini görmek için workflow run'a tıklamak gerek — küçük bir UX trade-off.
- **Concurrency cancel:** PR'a yeni push geldiğinde önceki CI run iptal edilir. Bu hem runner kotasını korur hem de "stale CI green" durumunu önler. Main push'larında da aynı, ama main'e push nadir (sadece merge).
