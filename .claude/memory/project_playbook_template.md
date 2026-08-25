---
name: project_playbook_template
description: Skinora metodunu uygulama-bağımsız GitHub template'ine dönüştürme projesi — üretim tamamlandı (2026-07-27), bağımsız doğrulama bekliyor
type: project
---

Skinora MVP'si bittikten sonra proje sahibi, Skinora'nın discovery → MVP yönteminin son hâlini uygulama-bağımsız bir **public GitHub template repository**'sine dönüştürmeye karar verdi. Klasör: `c:\projects\project-playbook`.

**Durum (2026-07-27): ÜRETİM TAMAMLANDI.** Lokal repo `git init` edildi, tek commit `364551e` (57 dosya, ~5.4K satır), working tree temiz. Playbook v1.0.

**Üretilen katmanlar:** L1 `Docs/00_PROJECT_METHODOLOGY.md` (konsolide: 10 doküman aşaması + implementation + **MVP borç kapatma** + **ertelenmiş işler disiplini** + kapanış ritüeli + numaralandırma konvansiyonları + **öğrenim terfisi**) · L2 `Docs/01–12` iskeletleri + 8 süreç dosyası + rapor şablonları · L3 INSTRUCTIONS/GUARDRAILS/CONTEXT · L4 8 skill + doküman-aşaması checklist'i + hafıza katmanı (terfi kuralıyla) · L5 3 katmanlı git hook + CI iskeleti + **eksik komut kapısı** + PR şablonu.

**C-kategorisi düzeltmeleri uygulandı:** PROMPTS.md taşınmadı · CONTEXT.md klasör düzeyi harita oldu · doküman aşaması skill değil checklist · öğrenim terfisi gate maddesine dönüştü.

**Kanıt (üretim chat'inde çalıştırıldı):** hook'lar scratch repo'da 8 senaryo ile test edildi (commit-msg blok/geçiş/bypass + pre-push Layer 1/3 + bypass log) ✓ · `ci-run.sh` 6 senaryo ✓ (bu sırada **gerçek bir kusur bulundu ve düzeltildi**: env dosyası `source` edildiği için tırnaksız çok kelimeli değer sessizce komut çalıştırıyordu → parse'a çevrildi) · workflow'lar js-yaml ile geçerli ✓ · 29 iç bağlantı ✓ · public dosyalarda kaynak proje sızıntısı yok ✓ · `.gitattributes` ile LF zorlandı (CRLF hook'ları Linux runner'da kırardı).

**Gizlilik:** `_archaeology/` (DECISIONS.md + RULE_INVENTORY.md + VERIFICATION_BRIEF.md) `.gitignore`'da — Skinora referansları public'e sızmaz.

**Why:** Metodun son hâli hiçbir yerde konsolide değildi; kural katmanı beş yere dağılmıştı ve önemli kısmı yalnız hafızada yaşıyordu. Template'in asıl değeri, Skinora'da acıyla geç edinilen savunma katmanlarına yeni projede gün 0'da sahip olmak.

**Kesinleşen parametreler (owner, 2026-07-27):** repo adı `project-playbook` · lisans **MIT** (commit `a73ce1b`) · görünürlük public · **yayın sırası: önce bağımsız doğrulama, sonra yayın**. Açık kalem kalmadı.

**Doğrulama turu 1 (2026-07-30):** Bağımsız chat verdict **FAIL** verdi (`_archaeology/VERIFICATION_REPORT.md`, 5 bulgu). Üretim tarafı adversarial ikinci tur çalıştırdı (10 ajan) → raporun **F-1 bulgusunun kronolojisi yarı yanlış** çıktı (`secrets/*`, `*.maFile` freeze'den 3 gün SONRA doğdu; rapor aynı commit'i W-1'de doğru, F-1'de yanlış tarihliyor) **ve rapor iki daha ağır freeze-öncesi kayıp kuralı kaçırmıştı**: (a) doğrulama matrisinin `Durum`/`BEKLEMEDE` alanı, (b) mock/gerçek kanıt-ortamı kaydı. Ayrıca "iç tutarlılık 1 kusur" iddiası gerçekte 7'ydi (üç eksen: config→doküman, ignore→yol, tanımsız terim).

**Düzeltme turu tamam — v1.0.1, commit `d37ea7e`** (23 dosya, +512 satır). A+B+C listesinin tamamı uygulandı: matris Durum+Kanıt ortamı sütunları · `.gitignore` sır varsayılanları · `11 §7` geri izlenebilirlik · **jenerik `pre-commit` sır guard'ı** · **`core.hooksPath` oturum kapısı** · **gerçek konfigürasyonla boot provası** (`00 §L.1`) · ön-uçuş tetikleyici+zaman kutusu · secret blast radius · süreç baseline valfi · 7 hedefsiz referans. Üretim sırasında bulunan ek kusur: sır guard'ının ilk sürümü bypass log'una **sırrın kendisini** yazıyordu (fonksiyonel testte yakalandı, `tip:dosya`ya indirildi). Üretim yanıtı: `_archaeology/PRODUCTION_RESPONSE.md`.

**How to apply / SIRADA:** **Delta yeniden doğrulama, AYRI chat'te** ([[feedback_validation_separate_chat]]). Kapsam + iki yeni eksen `PRODUCTION_RESPONSE.md §5`'te: (1) sistematik hedefsiz-referans taraması (markdown ile sınırlı olmadan), (2) SETUP kayıt tablosu × adım eşlemesi. Çıkış kriteri: iki eksende 0 kusur. **Verdict'i üretim tarafı çeviremez** (`12 §10.2`) — FAIL→PASS kararı doğrulayıcının. PASS sonrası yayın: public repo + push + "Template repository" işareti (`VERIFICATION_BRIEF.md §6`). Tek atlanan kalem **C8** (kaynak repo'daki bayat `.sh`→`.ps1` referansı) — başka repo'ya yazım gerektirir, ayrı onay + ayrı chore PR ister. Bu iş Skinora repo'suna dokunmadı.
