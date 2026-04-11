# Task — Implementation Yapım Chat'i

> **Ne zaman kullanılır:** Bir implementation task'ına başlanacağında.
>
> **Tetikleme:** Proje sahibi "T01 yap", "task T01" veya `/task T01` dediğinde bu skill çalıştırılır.
>
> **Parametre:** `hedef` — task numarası (örn: T01, T14, T63a)

## Başlangıç Adımları

### Adım 0 — Main CI Startup Check (HARD STOP)

**Amaç:** T11.2 ile gelen savunma katmanı. T13-T20 döneminde main CI 5 task üst üste FAIL'leyerek sessizce kırık kalmıştı; "lokal temiz" rasyonelizasyonu kabul edilemez.

**Yap:**

```bash
gh run list --branch main --limit 3 --json databaseId,conclusion,status,displayTitle,createdAt
```

**Karar kuralı:**

- Üç run'ın **hepsi** `conclusion=success` ise → task'a başla.
- Hangi run'lardan biri `failure`, `cancelled`, `timed_out` veya `action_required` ise → **HARD STOP.**
  - Kullanıcıya sebebini sor.
  - Root cause çözülmeden (ayrı bir fix PR veya BLOCKED kaydı) task'a başlama.
  - "Lokal temiz", "benim task'ımla ilgisiz", "zaten biliyordum", "bu sefer küçük değişiklik" **rasyonelizasyonları yasak.**
- `conclusion` boş (`status=in_progress` veya `queued`) run'lar sayılmaz — sadece tamamlanmış son 3 run'a bak. Gerekiyorsa `--limit` arttır.
- `gh` CLI yoksa veya auth başarısız olursa → kullanıcıya bildir ve manuel doğrulama iste; varsayımla ilerleme.

**Kanıt zorunluluğu:** Startup check sonucu (3 run ID + conclusion) TXX_REPORT.md'nin "Notlar" bölümüne yazılır. Bu, retrospektif denetim için zorunlu audit trail'dir.

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

## Bitiş Kapısı (T11.2 savunma katmanı)

**Amaç:** T11.1 retrospektifi T15+T16 task chat'lerinin bittiği ama PR açılmadığını, kodlarının F0 Gate Check PR #10'a "bundled" olarak geldiğini ortaya çıkardı. T17-T19 ise T20 branch'ine gömüldü. Yapım chat'inin "bitti" sayılabilmesi için aşağıdaki dört kapı açık olmalı.

Aşağıdakilerin **beşi de ✓** olmadan task "yapım bitti" sayılmaz ve validate chat'ine geçilmez. Eksik varsa bir önceki adıma dön, tamamla:

- [ ] **Branch push edildi mi?** `git push -u origin task/TXX-*` başarılı.
- [ ] **PR açıldı mı?** `gh pr create --base main --title "TXX: ..." --body "..."` çağrıldı, PR numarası geri geldi.
- [ ] **PR numarası TXX_REPORT.md'ye yazıldı mı?** `Commit & PR` bölümünde `PR: #XX` satırı net.
- [ ] **CI run tamamlandı mı?** `gh run watch <RUN_ID> --exit-status` veya eşdeğer polling ile **concluded** olmasını bekle. `status=in_progress`/`queued` beklenir; "started, conclusion bekleniyor" yeterli değildir — T11.2 kurgusunun tam karşıtı.
- [ ] **CI run sonucu `success` mi?** `conclusion=success` değilse (failure, cancelled, timed_out, action_required, startup_failure) → **yapım bitti sayılmaz**, root cause çözülür, yeni push yapılır, CI tekrar beklenir. Validator'a kırık CI ile geçmek yasaktır.

**Otomatik BLOCKED trigger:** TXX_REPORT.md içinde "PR: Henüz oluşturulmadı", "PR: TBD", "PR: —" veya boş bırakılmış bir PR alanı görülürse **otomatik BLOCKED** (DEPENDENCY_MISMATCH alt türü) — yapım chat'i açılır, açık kalmaya devam eder ve bir sonraki task'a geçilmez.

**Concurrency notu:** Task dalına hızlı art arda push atılırsa yeni run öncekini cancel edebilir. Bu `failure` sayılmaz — son tamamlanmış run'a bak (`gh run list --branch task/TXX-* --limit 5`). Beklenen: en son tamamlanmış olanı `success`.

**Bundled PR yasağı:** Başka bir task'ın PR'ına "tek commit daha ne olacak" diyerek gömmek yasaktır. Küçük görünen düzeltmeler bile ayrı PR ister (tek istisna: aynı TXX numarasının düzeltmeleri aynı branch'e).

## BLOCKED Durumunda

Task ilerleyemiyorsa (doküman eksikliği, bağımlılık uyumsuzluğu, plan hatası):

1. Çalışmayı durdur
2. BLOCKED alt türünü belirle (SPEC_GAP / DEPENDENCY_MISMATCH / PLAN_CORRECTION_REQUIRED / EXTERNAL_BLOCKER)
3. `Docs/TASK_REPORTS/TXX_REPORT.md` dosyasını INSTRUCTIONS.md §3.9 BLOCKED şablonuna göre oluştur
4. `Docs/IMPLEMENTATION_STATUS.md`'de durumu `⛔ BLOCKED` olarak işaretle
5. Proje sahibine sun: neden, etki analizi, çözüm önerileri
