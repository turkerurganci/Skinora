# Skinora — CI/CD Setup Kılavuzu

**Son güncelleme:** 2026-04-07 (T11 ile oluşturuldu)

Bu doküman, T11 ile kurulan CI/CD pipeline'ının repo ayarlarında nasıl aktifleştirileceğini ve operasyonel bilgileri içerir. Workflow dosyaları kod tabanında olduğu için git ile yönetilir; ama **branch protection kuralları, secret'lar ve repo ayarları GitHub UI/API'den manuel** yapılmalıdır.

---

## 1. Workflow Envanteri

| Dosya | Ne Yapar | Trigger |
|---|---|---|
| `.github/workflows/ci.yml` | 09 §21.4'teki 6 adımlı pipeline + Docker build doğrulaması | `pull_request` ve `push` (main, develop) |
| `.github/workflows/docker-publish.yml` | 4 servis image'ini ghcr.io'ya push | `push` (main) + `workflow_dispatch` |
| `.github/pull_request_template.md` | PR şablonu (09 §21.3 kuralları) | PR açılınca otomatik |

### `ci.yml` job sırası

```
1. lint              → 09 §21.4 step 1
2. build             → 09 §21.4 step 2
3. unit-test         → 09 §21.4 step 3
4. integration-test  → 09 §21.4 step 4
5. contract-test     → 09 §21.4 step 5  [PLACEHOLDER — T12 sonrası dolacak]
6. migration-dry-run → 09 §21.4 step 6  [PLACEHOLDER — T28 sonrası dolacak]
7. docker-build-check → image build doğrulaması (push yok)
ci-gate              → tüm job'ların sonucunu toplar (branch protection için tek required check)
```

**Job bağımlılıkları:**
- `lint` → `build` → `unit-test`, `integration-test`, `docker-build-check` (paralel)
- `integration-test` → `contract-test`, `migration-dry-run`
- Hepsi → `ci-gate`

---

## 2. İlk Kurulum Adımları (T11 sonrası bir kez)

### 2.1 Repo Ayarları

GitHub repo → **Settings** sekmesi:

**Actions → General:**
- "Workflow permissions" → **Read and write permissions** seç (docker-publish workflow'unun ghcr.io'ya yazabilmesi için)
- "Allow GitHub Actions to create and approve pull requests" → Kapalı bırak

**Actions → General → Fork pull request workflows:**
- "Run workflows from fork pull requests" → Kapalı (yalnızca repo collaborator'ların PR'ları CI tetiklesin — secret sızıntısı önlemi)

### 2.2 Secret'lar

Şu an T11 için **ek secret gerekmiyor** — `docker-publish.yml` `GITHUB_TOKEN`'ı kullanır (otomatik sağlanır).

İleride eklenecek secret'lar (T78+, F4 entegrasyonları için):
- `RESEND_API_KEY` (T78)
- `STEAM_API_KEY` (T64+)
- `TRONGRID_API_KEY` (T70+)
- `TELEGRAM_BOT_TOKEN` (T79)
- `DISCORD_BOT_TOKEN` (T80)

### 2.3 Branch'leri Hazırla

```bash
# main zaten mevcut
# develop branch'ini ilk defa olusturmak icin (T11 ile birlikte):
git checkout main
git pull
git checkout -b develop
git push -u origin develop
```

`main` ve `develop` artık ikisi de "long-lived" branch — feature branch'ler `develop`'tan ayrılır, `develop`'a merge olur, sonra `main`'e promote edilir.

> **Not:** T01–T10 dönemine ait squash merge'ler doğrudan `main`'e yapıldı (T11 öncesi rejim). T11 sonrası feature → develop → main akışı uygulanır.

### 2.4 Branch Protection Kuralları

**main** branch için (en sıkı):

GitHub repo → **Settings → Branches → Add branch protection rule**

| Ayar | Değer |
|---|---|
| Branch name pattern | `main` |
| Require a pull request before merging | ✅ |
| → Require approvals | ✅ (1) |
| → Dismiss stale pull request approvals when new commits are pushed | ✅ |
| Require status checks to pass before merging | ✅ |
| → Require branches to be up to date before merging | ✅ |
| → Required status checks | `CI Gate` (ci.yml içindeki ci-gate job) |
| Require conversation resolution before merging | ✅ |
| Require signed commits | Opsiyonel |
| Require linear history | ✅ (squash merge ile uyumlu) |
| Do not allow bypassing the above settings | ✅ |
| Restrict who can push to matching branches | ✅ (yalnızca admin/maintainer) |
| Allow force pushes | ❌ |
| Allow deletions | ❌ |

**develop** branch için (daha gevşek):

| Ayar | Değer |
|---|---|
| Branch name pattern | `develop` |
| Require a pull request before merging | ✅ |
| → Require approvals | Opsiyonel (proje sahibi kararı) |
| Require status checks to pass before merging | ✅ |
| → Required status checks | `CI Gate` |
| Allow force pushes | ❌ |
| Allow deletions | ❌ |

> **Önemli:** `CI Gate` required status check olarak işaretlenebilmesi için workflow'un en az bir kez çalışmış olması gerekir. T11 PR'ı açıldığında ilk çalışma olur, sonra bu check listeden seçilebilir.

### 2.5 Repository Settings → General → Pull Requests

| Ayar | Değer |
|---|---|
| Allow merge commits | ❌ |
| Allow squash merging | ✅ (default — INSTRUCTIONS.md §3.2) |
| → Default to PR title and description | ✅ |
| Allow rebase merging | ❌ |
| Always suggest updating pull request branches | ✅ |
| Automatically delete head branches | ✅ |

---

## 3. Operasyonel Notlar

### 3.1 Image İsimleri

ghcr.io'da yayınlanan image'lar:

- `ghcr.io/<owner>/<repo>-backend`
- `ghcr.io/<owner>/<repo>-frontend`
- `ghcr.io/<owner>/<repo>-sidecar-steam`
- `ghcr.io/<owner>/<repo>-sidecar-blockchain`

Tag stratejisi:
- `latest` — main'in en son hali
- `<short-sha>` — commit'e bağlı iz sürülebilirlik (örn: `a1b2c3d`)
- `main-<run-number>` — workflow run sayısı (rollback için sıralı tag)

### 3.2 Image'lara İlk Erişim

İlk push sonrası ghcr.io'daki paketler **private** olarak başlar. Public yapmak için:

GitHub profil/org → **Packages** → ilgili paket → **Package settings** → **Change visibility** → Public (veya private bırakılıp deploy makinesine pull token verilir).

### 3.3 CI Çalışma Süresi (Tahmin)

| Job | Tahmini süre |
|---|---|
| lint | ~1 dk |
| build | ~2 dk (ilk run, sonra cache) |
| unit-test | ~1-2 dk |
| integration-test | ~3-5 dk (TestContainers SQL Server start) |
| contract-test | <10 sn (placeholder) |
| migration-dry-run | <10 sn (placeholder) |
| docker-build-check (4 paralel) | ~3-4 dk |
| **Toplam (paralel)** | **~6-8 dk** |

### 3.4 Test Filtreleme

Workflow şu filtreleri kullanır:

| Job | Filter | Açıklama |
|---|---|---|
| unit-test | `FullyQualifiedName!~.Integration` | Namespace'i `.Integration` içermeyen tüm testler |
| integration-test | `FullyQualifiedName~.Integration` | Namespace'i `.Integration` içeren testler |

Bu, mevcut klasör/namespace yapısına dayanır (`*.Tests.Integration`, `*.Tests.Unit`, vb.). T12 test altyapısı `[Trait("Category", "Integration")]` attribute'larını eklediğinde filter `Category=Integration` formuna geçirilebilir.

---

## 4. Bakım

### Workflow güncellemesi
Workflow değişiklikleri normal PR akışıyla yapılır. `develop` üzerinde test edilir, `main`'e promote edilir.

### Action versiyonları
Pinned versiyonlar (örn: `actions/checkout@v4`) güvenlik için major version'da pinli. Dependabot'a açık tutulabilir (gelecekte).

### Cache temizleme
GitHub Actions cache'i 7 gün dokunulmayınca expire olur. Manuel temizlik gerekirse: repo → Actions → Caches.

---

## 5. T11 Sonrası Yapılacaklar (Follow-up)

| # | Konu | Hangi task | Ne yapılacak |
|---|---|---|---|
| 1 | Contract test gerçek implementasyon | T12 | `contract-test` job'u dolacak — sidecar↔backend JSON schema doğrulama |
| 2 | Migration dry-run gerçek implementasyon | T28 | `migration-dry-run` job'u dolacak — staging DB'ye karşı `dotnet ef database update --dry-run` |
| 3 | Frontend/sidecar ESLint | T13/T14/T15 | `lint` job'una `npm run lint` adımları eklenecek |
| 4 | CD (deploy) workflow | F0 sonrası | `docker compose pull && up -d` SSH ile staging'e otomatik deploy |
| 5 | Test trait migration | T12 sonrası | Filter'ı `FullyQualifiedName~.Integration` yerine `Category=Integration` yapmak |
