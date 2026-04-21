# Gate Check — Faz Sonu Doğrulama

> **Ne zaman kullanılır:** Bir fazın tüm task'ları tamamlandıktan sonra, sonraki faza geçiş onayı için.
>
> **Tetikleme:** Proje sahibi "F0 gate check", "faz kontrolü" veya `/gate-check F0` dediğinde bu skill çalıştırılır.
>
> **Parametre:** `hedef` — faz numarası (örn: F0, F1, F2)

## Ön Kontrol

1. **Faz tamamlanmış mı?** `Docs/IMPLEMENTATION_STATUS.md`'den `hedef` fazın tüm task'larını kontrol et:
   - Tüm task'lar `✓ Tamamlandı` mı?
   - `⛔ BLOCKED` veya `✗ FAIL` durumunda task varsa → gate check başlatılmaz, önce çözülmeli

2. **Rapor tutarlılığı:** Her task için `Docs/TASK_REPORTS/TXX_REPORT.md` mevcut mu ve finalize edilmiş mi?
   - Status tablosu ile rapor durumları eşleşiyor mu?

## Gate Check Adımları

### Adım 1 — Regresyon Testi

3. **Mevcut fazın testleri:**
   ```
   dotnet test (tüm backend test projeleri)
   npm test (frontend, varsa)
   npm test (sidecar'lar, varsa)
   ```
   - Tüm testler geçmeli
   - Sonuçları kaydet (test sayısı, süre, başarısız varsa detay)

4. **Önceki fazların testleri:** (F1'den itibaren)
   - Önceki fazlarda yazılan testleri tekrar çalıştır
   - Kırılan test = S2 (Kırılma) bulgusu → gate check FAIL

### Adım 2 — Build ve Environment

5. **Temiz build:**
   ```
   dotnet build (backend)
   npm run build (frontend)
   npm run build (sidecar'lar)
   ```

6. **Docker Compose smoke test:**
   ```
   docker compose down -v
   docker compose build
   docker compose up -d
   ```
   - Tüm servisler ayağa kalkıyor mu?
   - Health check endpoint'leri 200 dönüyor mu?
   - Sonra temizle: `docker compose down -v`

### Adım 3 — Migration (F1'den itibaren)

7. **Migration rehearsal:**
   - Temiz veritabanı üzerinde migration çalıştır
   - Hata var mı?
   - Seed data yükleniyor mu?

### Adım 4 — Traceability ve Boşluk Taraması

8. **Traceability matrix kontrolü:** `Docs/11_IMPLEMENTATION_PLAN.md` §7'deki traceability matrix'i kontrol et:
   - Bu fazda implement edilmesi gereken her kaynak öğe implement edilmiş mi?
   - Eşlenip de implement edilmeyen öğe = S3 (Eksik) bulgusu

9. **Doküman uyumu taraması:** Bu fazda yazılan kodun referans dokümanlarla uyumunu toplu kontrol et:
   - Enum değerleri tutarlı mı?
   - API sözleşmeleri doğru mu? (F2'den itibaren)
   - Entity field'ları eşleşiyor mu? (F1'den itibaren)

### Adım 5 — Güvenlik Özeti

10. **Faz güvenlik özeti:** Bu fazda eklenen tüm task'ların mini güvenlik kontrollerini derle:
    - Açık kalan güvenlik bulgusu var mı?
    - Yeni eklenen dış bağımlılıklar listesi
    - Auth/authorization değişiklikleri özeti

## Verdict

11. **Gate check verdict:**
    - **PASS:** Tüm testler geçiyor, build temiz, docker ayağa kalkıyor, traceability boşluk yok, S2 kırılma yok
    - **FAIL:** En az bir kritik bulgu var (kırık test, build hatası, traceability boşluğu, S2)
    - Bulgu varsa seviye ve düzeltme önerisiyle birlikte sun

12. **PASS durumunda:**
    - `Docs/IMPLEMENTATION_STATUS.md`'de faz gate check durumunu `✓ PASS` yap
    - **Repo memory revizyonu** ([`.claude/memory/MEMORY.md`](../memory/MEMORY.md)) — drift önleme (F1 Gate Check sonrası eklendi):
      - "Current Status" bloğunda fazın özet satırı eklenir/güncellenir (Gate Check tarihi, PR no, squash hash, tag, ana metrikler).
      - "Next" satırı bir sonraki fazı işaret edecek şekilde güncellenir.
      - Tarih başlığı bugünün tarihine çekilir.
      - Faz içindeki son task'lar (T(X-2), T(X-1), TX) için memory satırı varsa yeterli; yoksa ekle.
      - **Kanıt:** chore PR description'ında "memory: FX Gate Check yansıt" notu olur.
    - Yukarıdaki iki güncelleme + Gate Check raporu (`Docs/CHECKPOINT_REPORTS/GATE_CHECK_FX.md`) **aynı chore PR**'da merge edilir.
    - Faz tag'i at: `git tag phase/FX-pass` (chore PR merge sonrasındaki main commit'i üzerinde)
    - Tag'i push'la: `git push origin phase/FX-pass`

13. **FAIL durumunda:**
    - Bulguları listele (S1/S2/S3 sınıflaması)
    - Etkilenen task'ları belirt
    - Düzeltme planı öner
    - Düzeltmeler tamamlandıktan sonra gate check tekrar çalıştırılır

## Çıktı Formatı

```
## Gate Check Sonucu — FX [Faz Adı]
**Tarih:** YYYY-MM-DD
**Task aralığı:** TXX–TYY
**Toplam task:** N

### Verdict: ✓ PASS / ✗ FAIL

### Test Sonuçları
| Katman | Tür | Sonuç | Detay |
|---|---|---|---|
| FX (mevcut) | Unit | ✓ X/X passed | — |
| FX (mevcut) | Integration | ✓ X/X passed | — |
| F(X-1) (önceki) | Unit | ✓ X/X passed | — |
| F(X-1) (önceki) | Integration | ✓ X/X passed | — |

### Build
| Proje | Sonuç |
|---|---|
| Backend | ✓ / ✗ |
| Frontend | ✓ / ✗ |
| Steam Sidecar | ✓ / ✗ |
| Blockchain Sidecar | ✓ / ✗ |

### Docker Compose
- Servisler ayağa kalktı mı: ✓ / ✗
- Health check'ler: ✓ / ✗

### Migration (F1+)
- Temiz migration: ✓ / ✗ / N/A
- Seed data: ✓ / ✗ / N/A

### Traceability
- Eşlenen öğe sayısı: X
- Implement edilen: X
- Boşluk (S3): 0

### Güvenlik Özeti
- Açık bulgu: 0
- Yeni dış bağımlılıklar: [liste veya yok]

### Bulgular (FAIL durumunda)
| # | Seviye | Açıklama | Etkilenen task | Düzeltme önerisi |
|---|---|---|---|---|
| 1 | S1/S2/S3 | [bulgu] | TXX | [öneri] |

### Faz Tag
- Tag: `phase/FX-pass`
- Commit: hash
```
