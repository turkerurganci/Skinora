# Skinora Git Hooks

T11 close-out (2026-04-08) ile eklendi. **Discipline-only branch protection rejimi**'nin lokal koruma katmanı. GitHub Free + private repo'da branch protection ve rulesets paid feature olduğu için sistem-enforced koruma yok — bunun yerine bu hook'lar lokal disiplin sağlar.

## Hook'lar

| Hook | Ne yapar |
|---|---|
| `pre-push` | `main` veya `develop` branch'ine direct push'u bloklar. Tüm değişiklikler PR + CI üzerinden gitmelidir. |

## Kurulum (yeni clone sonrası tek seferlik)

```bash
bash scripts/git-hooks/install.sh
```

Bu komut şunları yapar:
1. `git config core.hooksPath scripts/git-hooks` — git'i bu klasöre yönlendirir
2. Hook dosyalarının executable bit'ini garanti altına alır
3. Doğrulama ve test komutlarını gösterir

> **Neden `cp` değil de `core.hooksPath`?** Hook dosyaları version-controlled — `scripts/git-hooks/`'taki edit'ler anında etkili. `.git/hooks/` ile stale kopya sync sorunu yok.

## Doğrulama

```bash
git config core.hooksPath
# beklenen: scripts/git-hooks
```

## Test

| Senaryo | Beklenen sonuç |
|---|---|
| `git push origin main` | ✗ BLOCKED, exit 1 |
| `SKINORA_ALLOW_DIRECT_PUSH=1 git push origin main` | ⚠ WARN + PASS |
| `git push origin task/T12-something` | ✓ PASS (feature branch'ler serbest) |

## Bypass (acil durum)

```bash
SKINORA_ALLOW_DIRECT_PUSH=1 git push origin main
# Sebep belirtmek icin (otomatik log'a yazilir):
SKINORA_ALLOW_DIRECT_PUSH=1 SKINORA_BYPASS_REASON="aciklama" git push origin main
```

**Kullanmadan iki kez düşün.** Hook her bypass'ta `Docs/BYPASS_LOG.md`'ye otomatik satır ekler (tarih, kullanıcı, branch, commit, sebep). Bypass commit'inden sonraki **ilk normal commit'te** log dosyasındaki değişikliği commit'le.

## CI Defansif Guard (R3)

Hook lokal koruma sağlar. Ek olarak `.github/workflows/ci.yml`'da **guard-direct-push** job'u server-side görünürlük sağlar:
- main'e `push` geldiğinde commit mesajında PR referansı `(#NN)` yoksa job FAIL eder
- Push zaten gerçekleşmiştir (engelleyemez) ama Actions sekmesinde kırmızı uyarı görünür
- `[skip-guard]` commit mesajına eklenerek bypass edilebilir (acil durum)

## Devre dışı bırakma

```bash
git config --unset core.hooksPath
```

Bu komut sadece lokal git config'i değiştirir; hook dosyaları hâlâ repo'da kalır.

## Yükseltme yolu (sistem-enforced rejim)

Eğer GitHub Pro'ya yükseltilirse (~$4/ay), branch protection ve rulesets aktif olur ve bu hook'lar gereksiz hale gelir. Yükseltme adımları:

1. GitHub repo → Settings → Billing → Pro plan
2. `Docs/CI_CD_SETUP.md` HEDEF tablosunu JSON'a çevir (`.github/protection-main.json`)
3. `gh api repos/turkerurganci/Skinora/branches/main/protection --method PUT --input .github/protection-main.json`
4. Aynı şey develop için
5. Hook'u devre dışı bırak: `git config --unset core.hooksPath`

## Kaynak

- T11 close-out: `Docs/TASK_REPORTS/T11_REPORT.md`
- Rejim açıklaması: `.claude/INSTRUCTIONS.md §3.2`
- Setup kılavuzu: `Docs/CI_CD_SETUP.md`
- Validator R1 önerisi: T11 re-validation report (2026-04-08)
