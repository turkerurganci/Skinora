# Validate — Implementation Doğrulama Chat'i

> **Ne zaman kullanılır:** Bir implementation task'ının yapım chat'i tamamlandıktan sonra, bağımsız doğrulama için.
>
> **Tetikleme:** Proje sahibi "T01 doğrula", "validate T01" veya `/validate T01` dediğinde bu skill çalıştırılır.
>
> **Parametre:** `hedef` — task numarası (örn: T01, T14, T63a)

## Kritik Kurallar

- **Sen bir spec conformance reviewer'sın.** Yapıcı değil, sapma avcısısın.
- **Yapım raporunu (TXX_REPORT.md) GÖRME.** Kendi verdict'ünü önce bağımsız oluştur.
- **Anchoring'e karşı dikkatli ol.** Commit mesajları, branch adı gibi ipuçlarından yola çıkarak "muhtemelen doğrudur" varsayımı yapma.
- **Kanıt olmadan onaylama.** Her kabul kriteri için somut kanıt (komut çıktısı, test sonucu, kod referansı) gerekir.

## Doğrulama Adımları

### Adım -1 — Working Tree Hygiene Check (HARD STOP)

**Amaç:** Uncommitted değişiklikler doğrulama sırasında karışıklık yaratabilir (hangi dosya task'ın, hangisi daha önceki iş?) ve yanlışlıkla doğrulama branch'ine karışabilir.

**Yap:**

```bash
git status --short
```

**Karar kuralı:**

- Çıktı **boş** → Adım 0'a geç.
- **Staged/unstaged değişiklik var** → **HARD STOP.** Kullanıcıya listele, commit / stash / discard kararı iste. Karar olmadan doğrulamaya başlama. "Sonra hallederiz" rasyonelizasyonu yasak.

### Adım 0 — Main CI Startup Check (HARD STOP)

**Amaç:** T11.2 savunma katmanı. T20 validator'ı main CI ardışık FAIL'leyen ortamda "lokal temiz, geç" rasyonelizasyonuyla PASS verdi — bu bir daha tekrarlanmamalı.

**Yap:**

```bash
gh run list --branch main --limit 3 --json databaseId,conclusion,status,displayTitle,createdAt
```

**Karar kuralı:**

- Üç tamamlanmış run'ın **hepsi** `conclusion=success` ise → doğrulamaya başla.
- Biri bile `failure`, `cancelled`, `timed_out` veya `action_required` ise → **HARD STOP.**
  - Kullanıcıya sebebi sor.
  - "Lokal temiz", "ilgisiz kırılma", "önceki task'ın borcu", "sadece docker-publish" **rasyonelizasyonları yasak.**
  - CI kırılması mevcut task'ın kendisinden kaynaklanıyorsa → bu zaten S2 Kırılma finding'i, FAIL verdict.
  - CI kırılması önceki bir task'ın borcundan kaynaklanıyorsa → **BLOCKED (DEPENDENCY_MISMATCH)** — "önceki task yeşil bırakmadığı için bu task doğrulanamaz."
- Bu adım TXX_REPORT.md'deki doğrulama bölümüne yazılır (3 run ID + conclusion).

### Adım 0b — Repo Memory Drift Check (HARD STOP)

**Amaç:** F1 Gate Check sonrası eklendi. T27 + T28 + F1 Gate Check sonrası repo memory `4775e4e` (T11.3 yansıt)'tan beri güncellenmemişti — auto-memory güncellendi ama repo memory drift'e girdi. Drift gözlenebilir değildi çünkü hiçbir kapı kontrol etmiyordu. Validator memory'i kontrol eden son kapıdır.

**Yap:**

```bash
grep -E "T${HEDEF_NO}\b|T${HEDEF_NO}\s" .claude/memory/MEMORY.md
```

**Karar kuralı:**

- TXX için en az bir satır mevcutsa → doğrulamaya devam.
- Hiç satır yoksa → **HARD STOP / BLOCKED (DEPENDENCY_MISMATCH).**
  - Yapım chat'i `task.md` Bitiş Kapısı 8. maddesini ihlal etti (memory güncellemesi atlanmış).
  - Validator finding: "Repo memory drift — TXX için satır yok. Yapım chat'i memory'i güncellemeden validate'e geçti."
  - Düzeltme: Yapım chat'ine geri dön → memory güncelle → `chore: memory — TXX yansıt` commit + push (aynı task PR'ına dahil edilebilir veya ayrı chore PR). Sonra validator yeniden başlatılır.
- "Sonra ekleriz", "önemsiz" rasyonelizasyonları yasak — Adım 0 CI kuralının memory eşdeğeri.

1. **Task tanımını oku:** `Docs/11_IMPLEMENTATION_PLAN.md`'den `hedef` task'ın tanımını bul:
   - Kabul kriterleri
   - Test beklentisi
   - Doğrulama kontrol listesi
   - Doküman referansları

2. **Referans dokümanları oku:** Task tanımında belirtilen doküman bölümlerini oku. Bunlar source of truth.

3. **Remote'u güncelle:** `git fetch origin` — mobil veya başka session'dan push edilmiş değişiklikleri al. Fetch yapılmazsa eski branch state'i incelenir.

4. **Branch kodunu incele:** `task/TXX-*` branch'indeki değişiklikleri oku:
   - Hangi dosyalar değişmiş / oluşturulmuş?
   - Değişiklikler dokümanlarla uyumlu mu?

5. **Kabul kriterlerini tek tek doğrula:** Her kriter için:
   - İlgili kodu bul ve oku
   - Gerekli komutu çalıştır (build, test, vb.)
   - Çıktıyı kaydet
   - Verdict ver:
     - `✓ Karşılandı` — kanıtla doğrulandı
     - `✗ Karşılanmadı` — eksik veya hatalı, detay yaz
     - `~ Kısmi` — kısmen karşılandı, ne eksik detaylı açıkla
     - `? Doğrulanamadı` — kanıt üretilemedi veya yetersiz (bu FAIL değil, kanıt eksikliği)

6. **Doğrulama kontrol listesini çalıştır:** `11_IMPLEMENTATION_PLAN.md`'deki doğrulama kontrol listesi maddelerini tek tek geç.

7. **Testleri çalıştır:**
   - `dotnet test` (backend)
   - `npm test` (frontend/sidecar, varsa)
   - Sonuçları kaydet

8. **Build kontrolü:** Tüm projeler temiz build veriyor mu?

   **8a. Task branch CI kontrolü (T11.2 zorunlu madde):** `gh run list --branch task/TXX-* --limit 3 --json databaseId,conclusion,status` ile task branch'inin CI run'larına bak. En az bir run `conclusion=success` olmalı. Hiçbir run yoksa veya en son run `failure` ise → **bu bir finding'dir, sessizce geçilemez.**
   - Başarısız olan adım (Lint / Build / Unit / Integration / Contract / Migration / Docker) bulguda belirtilir.
   - "Lokal makinemde geçiyor" kabul edilemez — validator kanıt bazlı çalışır, lokal temizlik CI'yi ikame etmez.
   - Task'ın kendi CI run'ı yoksa (branch push edilmemiş, PR açılmamış) → BLOCKED (task chat bitiş kapısı çiğnenmiş, `task.md` Bitiş Kapısı bölümüne bak).

9. **Mini güvenlik kontrolü:**
   - Secret sızıntısı var mı?
   - Auth/authorization etkisi var mı?
   - Input validation etkisi var mı?
   - Yeni dış bağımlılık eklendi mi?

10. **Doküman uyumu kontrolü:** Kod, referans dokümanlarla tutarlı mı?
    - Enum değerleri eşleşiyor mu?
    - Field adları eşleşiyor mu?
    - İş kuralları doğru uygulanmış mı?

### Faz 2 — Verdict

11. **Genel verdict oluştur:**
    - **PASS:** Tüm kabul kriterleri ✓ veya kabul edilebilir ~ (minor), güvenlik kontrolü temiz, testler geçiyor
    - **FAIL:** En az bir kabul kriteri ✗, veya kritik güvenlik bulgusu, veya testler kırık
    - **BLOCKED:** Doğrulama yapılamıyor (kod eksik, branch yok, vb.)

12. **Bulguları sınıfla** (FAIL durumunda):
    - `S1 Sapma` — Task tamamlandı ama dokümanla uyumsuz
    - `S2 Kırılma` — Mevcut işlevselliği bozan değişiklik
    - `S3 Eksik` — Kabul kriterinde tanımlı ama implement edilmemiş

### Faz 3 — Rapor Karşılaştırma ve Finalize

13. **Şimdi yapım raporunu oku:** `Docs/TASK_REPORTS/TXX_REPORT.md` taslağını oku.
    - Kendi verdict'inle karşılaştır
    - Uyuşmazlık varsa belirt

14. **Raporu finalize et:** TXX_REPORT.md'yi validator sonuçlarıyla güncelle:
    - Doğrulama bölümünü doldur (durum, bulgu sayısı, düzeltme gerekli mi)
    - Kabul kriterleri tablosunu validator kanıtlarıyla güncelle

15. **Status güncelle:** (sadece PASS durumunda)
    - `Docs/IMPLEMENTATION_STATUS.md`'de task durumunu `✓ Tamamlandı` yap
    - **Kural:** Rapor finalize edilmeden status güncellenmiş sayılmaz

16. **Rapor + status commit+push:** Finalize edilmiş rapor ve status değişikliğini commit'le ve push'la.
    - Bu adım merge'den önce yapılmalı — aksi halde squash merge bu değişiklikleri içermez.
    - Cloud session'larda commit+push edilmeyen dosyalar session kapandığında kaybolur.

17. **Merge:** (sadece PASS durumunda)
    - Branch'i `main`'e squash merge et
    - Squash commit mesajı: `TXX: Task adı`

## Çıktı Formatı

```
## Doğrulama Sonucu — TXX [Task Adı]
**Tarih:** YYYY-MM-DD
**Branch:** task/TXX-aciklama
**Commit:** hash

### Verdict: ✓ PASS / ✗ FAIL / ⛔ BLOCKED

### Kabul Kriterleri
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | [kriter] | ✓/✗/~/? | [komut çıktısı veya referans] |

### Doğrulama Kontrol Listesi
- [x] / [ ] kontrol maddesi

### Test Sonuçları
| Tür | Sonuç | Komut | Çıktı |
|---|---|---|---|
| Unit | ✓ X/X | dotnet test ... | [özet] |

### Güvenlik Kontrolü
- [ ] Secret sızıntısı: Temiz / Bulgu
- [ ] Auth etkisi: Temiz / Bulgu
- [ ] Input validation: Temiz / Bulgu
- [ ] Yeni bağımlılık: Yok / [liste]

### Bulgular (FAIL durumunda)
| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| 1 | S1/S2/S3 | [bulgu] | [dosya] |

### Yapım Raporu Karşılaştırması
- Uyum: [Tam uyumlu / X uyuşmazlık tespit edildi]
- [Varsa uyuşmazlık detayları]
```

## FAIL Durumunda

- Bulguları proje sahibine sun
- Branch merge edilmez
- Düzeltme için yeni yapım chat'i açılır
- Düzeltme sonrası yeni doğrulama chat'i açılır

## Doğrulanamadı Durumunda

- `?` alan kriterler için:
  - Ek kanıt üretme yöntemi öner
  - Veya doğrulama yönteminin revize edilmesi gerektiğini belirt
- Bu FAIL sayılmaz ama PASS için çözülmesi gerekir
