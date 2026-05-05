# T57 — Wash trading kontrolü

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (validator bekliyor) | **Tarih:** 2026-05-05

> **Bu rapor doc-only confirmation (no-op kod) raporudur.** T57 plan kabul kriterleri T43 implementasyonu (PR [#72](https://github.com/turkerurganci/Skinora/pull/72), commit `7bc3130`+`0deee0f`) tarafından kanıtlı şekilde karşılanmıştır. Bu PR yeni kod üretmez; T57 audit trail'ini ayrı bir TXX_REPORT + status satırı + plan §T57 absorbe notu ile diğer task'larla simetrik kapatır.
>
> **Karar gerekçesi:** Proje sahibi seçenek B (doc-only confirmation PR) — A (BLOCKED-DEPENDENCY_MISMATCH) ve C (scope genişletme) reddedildi. B audit trail'i sağlam tutar (her TXX'nin bir PR'ı + raporu olur), kod değişmediği için risk düşüktür, validator T43 kanıtları üzerinden kontrol eder.

---

## Yapılan İşler

- T57 kabul kriterlerinin T43 implementasyonu tarafından karşılandığı **kanıt mapping**'i yapıldı (aşağıda Kabul Kriterleri Kontrolü tablosu).
- `Docs/IMPLEMENTATION_STATUS.md` T57 satırı `⬚ Bekliyor` → `⏳ Devam ediyor` (validator T43 kanıtlarını teyit edecek).
- `Docs/11_IMPLEMENTATION_PLAN.md` §T57 bloğuna "T43 kapsamında implement edildi" absorbe notu eklendi (plan satırı silinmedi — historical record).
- `.claude/memory/MEMORY.md` Current Status + Next satırı güncellendi (T57 doc-only PR notu).
- **Yeni kod yok** — `WashTradingFilter`, `ReputationAggregator` wash filter wiring, ve test'leri T43 kapsamında merge edilmişti.

## Etkilenen Modüller / Dosyalar

**Yalnız doküman değişiklikleri:**

- `Docs/TASK_REPORTS/T57_REPORT.md` (yeni — bu dosya)
- `Docs/IMPLEMENTATION_STATUS.md` (T57 satırı + Açık Bulgular yok)
- `Docs/11_IMPLEMENTATION_PLAN.md` (§T57 absorbe notu)
- `.claude/memory/MEMORY.md` (Current Status + Next)

**Kod değişikliği yok.** T43 PR #72 kapsamında merge edilen aşağıdaki dosyalar T57 kabul kriterlerini karşılar:

- `backend/src/Modules/Skinora.Users/Application/Reputation/WashTradingFilter.cs` (pure helper, 30 gün penceresi, unordered pair)
- `backend/src/Modules/Skinora.Transactions/Application/Reputation/ReputationAggregator.cs` (filter'i `SuccessfulTransactionRate` denominator+numerator'a uygular — satır 103-110)
- `backend/tests/Skinora.Users.Tests/Unit/Reputation/WashTradingFilterTests.cs` (7 unit test)
- `backend/tests/Skinora.Transactions.Tests/Integration/Reputation/ReputationAggregatorTests.cs` (2 wash-trading integration testi)

## Kabul Kriterleri Kontrolü

| # | T57 Kriter | Sonuç | T43 Kanıt |
|---|---|---|---|
| 1 | Aynı alıcı-satıcı çifti arasında ardışık işlemler arasında min 1 ay kontrolü | ✓ | `WashTradingFilter.WashTradingWindow = TimeSpan.FromDays(30)` ([WashTradingFilter.cs:28](../../backend/src/Modules/Skinora.Users/Application/Reputation/WashTradingFilter.cs)). Unordered pair: `NormalizePair` ([WashTradingFilter.cs:68-69](../../backend/src/Modules/Skinora.Users/Application/Reputation/WashTradingFilter.cs)) `(A,B)` ve `(B,A)`'yı tek kanonik anahtara çevirir. Pencere son **sayılan** işlemden başlar (zincir reset değil) — tasarım kararı 02 §14.1 "ardışık" semantik'i ile uyumlu. Test: `Same_Pair_Within_Window_Drops_Subsequent_Rows` (10/20 gün içi → drop), `Same_Pair_Outside_Window_Resumes_Counting` (31/62 gün → counted), `Wash_Window_Restarts_From_Last_Counted_Not_From_First_Counted`, `Pair_Order_Does_Not_Matter`, `Different_Pairs_Are_Tracked_Independently`, `Out_Of_Order_Input_Is_Sorted_By_Timestamp_Internally`. |
| 2 | Bu süreden kısa → işlem engellenmez, skor etkisi kaldırılır | ✓ | **İşlem engellenmez:** `WashTradingFilter` ve `ReputationAggregator` salt **read path** servisleridir; `TransactionCreationService` veya state machine giriş noktalarında çağrılmazlar. Wash filtre transaction CREATE/CANCEL/COMPLETE state geçişlerine müdahale etmez. **Skor etkisi kaldırılır:** Filter `Counted=false` döndüğünde row hem denominator'dan hem numerator'dan düşer ([ReputationAggregator.cs:108-110](../../backend/src/Modules/Skinora.Transactions/Application/Reputation/ReputationAggregator.cs)) — net etki: skora katkı yok. `CompletedTransactionCount` (raw count, 02 §13) etkilenmez ([ReputationAggregator.cs:69-70](../../backend/src/Modules/Skinora.Transactions/Application/Reputation/ReputationAggregator.cs)). Integration test: `Recompute_Wash_Trading_Removes_Repeat_Pair_From_Rate_Denominator` (3 COMPLETED, ortadaki washed → rate=2/2=1.0, completedCount=3 raw), `Recompute_Wash_Trading_Hides_Cancelled_From_Denominator` (CANCELLED_SELLER washed → satıcıya penalty yok, rate 2/3=0.6666 yerine 2/2=1.0). |

## Doğrulama Kontrol Listesi (plan §T57)

- [x] **02 §14.1 kuralları birebir mi?** — Üç madde birebir uygulanmış:
  - "Aynı alıcı-satıcı çifti arasında ardışık işlemler arasında en az 1 ay olmalı" → `WashTradingWindow = 30 gün` + unordered pair normalize.
  - "Bu süreden kısa aralıkla yapılan işlemler skora etki etmez" → `Counted=false` row'lar denominator+numerator'dan düşer (net etki: yok).
  - "İşlem engellenmez, sadece skor etkisi kaldırılır" → Filter sadece read path; transaction state machine giriş noktalarında çağrılmaz.

## Test Sonuçları

T43 PR #72'de merge edilen wash-trading testleri (yeniden çalıştırılmadı; T43 raporu kanıtı + her main CI run'ında geçer):

- **Unit:** `WashTradingFilterTests` 7/7 PASS (T43 raporu satır 145, 168)
- **Integration:** `ReputationAggregatorTests` wash-trading senaryoları 2/2 PASS (`Recompute_Wash_Trading_Removes_Repeat_Pair_From_Rate_Denominator`, `Recompute_Wash_Trading_Hides_Cancelled_From_Denominator`)

Bu PR no-op kod olduğu için ek test çalıştırması gerekmez. Validator gerekli görürse local/CI sweep yapabilir.

## Altyapı / Migration / Bağımlılık

- Migration yok.
- Yeni package yok.
- DI değişikliği yok.
- API contract değişikliği yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** N/A — kod değişikliği yok.
- **Auth/authorization:** N/A.
- **Input validation:** N/A.
- **Yeni dış bağımlılık:** Yok.

## Doküman Yansıması

| Doküman | Bölüm | Değişiklik |
|---|---|---|
| `Docs/IMPLEMENTATION_STATUS.md` | T57 satırı | `⬚ Bekliyor` → `⏳ Devam ediyor`; status sütununa "T43 kapsamında implement, doc-only confirmation PR" notu |
| `Docs/11_IMPLEMENTATION_PLAN.md` | §T57 (satır 1262-1271) | Bloğa "**Not:** T43 kapsamında implement edildi (PR #72). T57 doc-only confirmation PR ile audit-trail'e kapatıldı (PR #XX)." absorbe notu eklendi (plan kabul kriterleri korundu — historical record) |
| `Docs/TASK_REPORTS/T57_REPORT.md` | (bu dosya) | Yeni |
| `.claude/memory/MEMORY.md` | Current Status + Next | T57 doc-only PR satırı + sonraki task (T58 Dispute sistemi) |

## Forward-Devir Notları

- **HIGH_VOLUME aggregate semantic'i** (T55 advisory A1) — bu T57 PR scope'unun **dışında**. T55 raporu `HighVolume` rolling window'unun `FLAGGED + CANCELLED_*` rows'ları dahil etmesini "T56/T57 follow-up'ta tekrar değerlendir" diye işaretlemişti. T56 değerlendirmesi (T56_REPORT) bu konuyu **scope dışı** bırakmıştı — T57 plan satırı sadece §14.1 wash trading'e odaklanır, AML aggregation semantic'i §14.4 konusudur. **Karar:** Bu tartışma T57'de açılmaz; gelecekte ayrı bir refinement/follow-up task'ında ele alınır (proje sahibi gündeme alırsa).
- **Wash filter'in fraud flag'e bağlanması yok** (kasıtlı): 02 §14.1 explicit "işlem engellenmez, skor etkisi kaldırılır" — wash trading bir FraudFlag değil, salt skor filtresi. T54 fraud flag sistemi (`FraudFlagType` enum) wash-trading değeri içermez; bu doğru tasarım.

## Working Tree + CI Kapı Kontrolü (skill task.md Adım -1, Adım 0)

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz (`git status --short` — boş çıktı) |
| Main CI startup (Adım 0) | ✓ son 3 run success: `25385809639` (T56 #87), `25385809617` (T56 #87), `25338999719` (T55) |
| Bağımlılıklar | ✓ T43 ✓ Tamamlandı (PR #72, validator PASS) |

## Dış Varsayımlar

- **Yok.** T57 kabul kriterleri saf iç-mantıktır (1 ay penceresi + skor formülü payda etkisi). Dış API/paket/plan-tier varsayımı yok.

## Notlar

- **No-op kod PR rasyonalitesi:** Bu task'ın kodu zaten T43 PR #72'de mevcut. Doc-only PR audit trail simetrisi (her TXX'nin raporu+PR'ı vardır) korur ve gate-check tetiklenebilirliğini sağlar. Alternatif "T57 satırını silmek" plan tarihselliğini bozardı (plan §T57 hâlâ kabul kriteri tablosunda anlamlıdır — sadece kapsama atıfla absorbe edildi).
- **Validator beklentisi:** T57 validator T43 kanıt zincirini gözden geçirir (`WashTradingFilter` + `ReputationAggregator` wash-filter wiring + test sayıları). Yeni kod olmadığı için "rapor uyumu tam mı" + "T43 kanıtları T57 kabul kriterleriyle 1:1 eşleşiyor mu" sorularına bakar. Validator gerekirse `WashTradingFilterTests` ve `ReputationAggregatorTests` wash-trading testlerini lokal yeniden çalıştırabilir.
- **Bundled-PR yasağı uyum:** Bu PR yalnız T57 referanslı değişiklik içerir (T57_REPORT + status + plan + memory). `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+...'` çıktısı sadece `T57` döndürür (Bitiş Kapısı 7. madde).

## Commit & PR

- Branch: `task/T57-wash-trading-doc-confirmation`
- Commit: `9970842`
- PR: [#88](https://github.com/turkerurganci/Skinora/pull/88)
- CI: <run id + sonuç buraya gelecek (CI watch sonrası)>

## Bitiş Kapısı (skill task.md — doc-only edition)

- [ ] Branch push edildi mi?
- [ ] PR açıldı mı?
- [ ] PR numarası rapor footer'a yazıldı mı?
- [ ] Rapor + status push edildi mi?
- [ ] CI run tamamlandı mı?
- [ ] CI run sonucu success mi?
- [ ] Branch izolasyon check temiz mi?
- [ ] Repo memory'de T57 satırı eklendi/güncellendi mi?
