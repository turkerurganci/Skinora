# Task — Implementation Yapım Chat'i

> **Ne zaman kullanılır:** Bir implementation task'ına başlanacağında.
>
> **Tetikleme:** Proje sahibi "T01 yap", "task T01" veya `/task T01` dediğinde bu skill çalıştırılır.
>
> **Parametre:** `hedef` — task numarası (örn: T01, T14, T63a)

## Başlangıç Adımları

1. **Task tanımını oku:** `Docs/11_IMPLEMENTATION_PLAN.md`'den `hedef` task'ın tanımını bul ve oku:
   - Task adı
   - Bağımlılıklar
   - Doküman referansları
   - Kabul kriterleri
   - Test beklentisi
   - Doğrulama kontrol listesi

2. **Bağımlılık kontrolü:** `Docs/IMPLEMENTATION_STATUS.md`'den bağımlı task'ların durumunu kontrol et:
   - Tüm bağımlılıklar `✓ Tamamlandı` mı?
   - Değilse → proje sahibine bildir, task başlatılmaz

3. **Doküman referanslarını oku:** Task tanımındaki doküman referanslarını oku (örn: 09 §4.1, §4.2). Sadece belirtilen bölümleri oku, tüm dokümanı yükleme.

4. **Scope netleştirme:** Proje sahibine şunu sun:
   - Etkilenen modüller / dosyalar
   - Beklenen çıktı / artifact listesi
   - "Bu task bittiğinde sistemde tam olarak ne değişmiş olacak"

5. **Branch oluştur:** `task/TXX-kisa-aciklama` formatında feature branch aç.

## Yapım Süreci

6. **Kodu yaz:** Kabul kriterlerini tek tek karşılayacak şekilde implement et.
   - `09_CODING_GUIDELINES.md` standartlarına uy
   - Dokümanlarla çelişki fark edersen → **DURMA**, proje sahibine bildir (BLOCKED akışı, bkz. INSTRUCTIONS.md §3.5)
   - Varsayımla ilerleme, doğaçlama yapma

7. **Testleri yaz ve çalıştır:** Task tanımındaki test beklentisine göre:
   - Unit testler
   - Integration testler
   - Tüm testlerin geçtiğini doğrula

8. **Build kontrolü:** `dotnet build` (backend), `npm run build` (frontend/sidecar) başarılı olmalı.

9. **Mini güvenlik kontrolü:**
   - Secret sızıntısı var mı?
   - Auth/authorization etkisi var mı?
   - Input validation etkisi var mı?
   - Yeni dış bağımlılık eklendi mi?

10. **Kabul kriterleri self-check:** Her kabul kriterini tek tek gözden geçir. Karşılanmayan varsa tamamla.

## Tamamlama

11. **Commit ve push:** Değişiklikleri commit'le ve branch'i push'la.

12. **Rapor taslağı hazırla:** `Docs/TASK_REPORTS/TXX_REPORT.md` dosyasını INSTRUCTIONS.md §3.8 şablonuna göre oluştur:
    - Yapılan işler
    - Etkilenen modüller / dosyalar
    - Kabul kriterleri kontrolü (kanıtlı)
    - Test sonuçları (komut + çıktı)
    - Altyapı değişiklikleri
    - Commit bilgisi
    - Known limitations

13. **Status güncelleme:** `Docs/IMPLEMENTATION_STATUS.md`'de task durumunu `⏳ Devam ediyor` olarak işaretle.
    - **Not:** `✓ Tamamlandı` yapma — bu doğrulama chat'inin işi.

14. **Proje sahibine bildir:** "TXX tamamlandı, doğrulama chat'ine geçebiliriz." de.

## BLOCKED Durumunda

Task ilerleyemiyorsa (doküman eksikliği, bağımlılık uyumsuzluğu, plan hatası):

1. Çalışmayı durdur
2. BLOCKED alt türünü belirle (SPEC_GAP / DEPENDENCY_MISMATCH / PLAN_CORRECTION_REQUIRED / EXTERNAL_BLOCKER)
3. `Docs/TASK_REPORTS/TXX_REPORT.md` dosyasını INSTRUCTIONS.md §3.9 BLOCKED şablonuna göre oluştur
4. `Docs/IMPLEMENTATION_STATUS.md`'de durumu `⛔ BLOCKED` olarak işaretle
5. Proje sahibine sun: neden, etki analizi, çözüm önerileri
