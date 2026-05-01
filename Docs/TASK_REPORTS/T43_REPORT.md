# T43 — User itibar skoru hesaplama

**Faz:** F2 | **Durum:** ⛔ BLOCKED | **Tarih:** 2026-05-01

---

## BLOCKED Bilgisi

- **Alt tür:** SPEC_GAP

- **Neden:** Composite `reputationScore` (0-5 ölçekli kullanıcı itibar puanı) hesaplama formülü hiçbir spec dokümanında tanımlı değil. Boşluk üç doküman arasında çelişki üretiyor:

  1. **`Docs/02_PRODUCT_REQUIREMENTS.md` §13** yalnızca girdileri sayar — *"Tamamlanan işlem sayısı, başarılı işlem oranı, platformdaki hesap yaşı"* — ama formülü tanımlamaz.
  2. **`Docs/06_DATA_MODEL.md` §3.1** sadece bileşenlerden biri olan `SuccessfulTransactionRate` formülünü verir (`completed / (completed + cancelled_seller + cancelled_buyer + cancelled_timeout)`). Composite 0-5 skoru ne `User` entity'sinde alan olarak ne de §8.2 denormalized tablosunda görünür.
  3. **`Docs/07_API_DESIGN.md` §5.1, §5.2, §5.5, §6.x** kontratları `"reputationScore": 4.8` (0-5 ölçeği) emit eder, ama backend bu değeri **hangi formülle ürettiği yazılı değildir**. T33 raporu (2026-04-23) bu boşluğu fark edip `reputationScore: null` döndürerek "T43 forward devir" notu bıraktı.

  Üstüne, **T43 plan kabul kriterleri composite skoru explicit talep etmiyor** (`Docs/11_IMPLEMENTATION_PLAN.md` §T43 → 6 madde: count, rate, age, cancel etkisi, wash trading, cooldown). Bu durum kontrat (07) ile plan (11) arasında bir drift'tir: kontrat alanı emit ediyor, plan üretim mantığını istemiyor. Tahminle yazılan formül DB'deki `User.SuccessfulTransactionRate` denormalized değerleri üzerinden T44+ caller'larca persist'e bağlanırsa, formül sonradan değiştiğinde retroactive recompute borcu doğar; bu yüzden formül kararı verilmeden başlanmamalıdır.

- **Etkilenen dokümanlar:**
  - `Docs/02_PRODUCT_REQUIREMENTS.md` §13 (girdi listesi yeterli, formül yok)
  - `Docs/06_DATA_MODEL.md` §3.1 (SuccessfulTransactionRate var, composite score yok), §8.2 (denormalized güncelleme tablosu — composite alan yok)
  - `Docs/07_API_DESIGN.md` §5.1 (UserProfileDto), §5.2 (UserStatsDto), §5.5 (PublicUserProfileDto), §6.x (TransactionDto seller/buyer profilleri içinde `reputationScore: 4.8`)
  - `Docs/11_IMPLEMENTATION_PLAN.md` §T43 (kabul kriterleri composite skoru talep etmiyor — kontrat ile plan arasında drift)

- **Etkilenen task'lar:**
  - **T33** (User profil servisi, ✓ 2026-04-23) — `reputationScore: null` döndürüyor, T43 ile gerçek değere bağlanması bekleniyor (rapor Known Limitations'da explicit forward-devir).
  - **T45** (İşlem oluşturma akışı) — plan T43'e bağımlı (`Docs/11_IMPLEMENTATION_PLAN.md` §T45 bağımlılık satırı: `T44, T34, T43`).
  - **T46–T63** transaction akışları — COMPLETED/CANCELLED state geçişleri sonrası denormalized güncelleme handler'ı çağırır. T43 servisi yoksa bu çağrılar T44+ task'larında açılır kalır.
  - **T93** (Profil sayfaları S08, S09) — UI'da `reputationScore` görüntüleniyor; backend null döndüğünde frontend fallback kuralı belirsiz.
  - **F2 Gate Check** — bu task fazı kapatan son adım; karar verilmeden gate öncesi yeniden tetiklenmesi gerek.

## Çözüm Önerileri

1. **(A) Formül 02/06'da yazılır:** Proje sahibi composite skor formülünü tanımlar (örn. `reputationScore = ROUND(SuccessfulTransactionRate × 5, 1)` + hesap yaşı kuralı) ve `Docs/02_PRODUCT_REQUIREMENTS.md` §13 ile `Docs/06_DATA_MODEL.md` §3.1'e eklenir; T43 plan kabul kriterleri composite skoru içerecek şekilde güncellenir; T43 implement edilir.
2. **(B) `reputationScore` 07'den kaldırılır:** Backend yalnız `successfulTransactionRate` ve `completedTransactionCount` döndürür; UI tarafı hesaplama yapar veya alanı göstermez. 07 §5.1/§5.2/§5.5/§6.x örneklerindeki `4.8` değerleri silinir; T93/T101 frontend tasarımı buna göre netleşir.
3. **(C) Frontend hesaplar (kontrat sınırlı tutulur):** Backend yalnız ham girdileri (count + rate + accountAge) verir; frontend `successfulTransactionRate × 5` veya başka bir kompozit kuralla skoru render eder. 07'de alan input olarak işaretlenir, "computed by client" notu eklenir.

**Önerim — (A):** 07'deki örnekler backend-emit gösteriyor (kontrat sözleşmesi backend'in alanı doldurduğunu ima ediyor); A en az kontrat kırılmasıyla ilerler. Formül kararı verildikten sonra T43 yeni branch'te implement edilir. Mevcut T43 plan kabul kriterleri (count + rate + age + cancel + wash + cooldown) zaten implementable; composite skor maddesi ek olarak plan'a girer ve aynı PR'da implement edilir.

## Proje Sahibi Kararı

- **Karar:** Henüz alınmadı
- **Tarih:** —

## Working Tree + CI Kapı Kontrolü (skill task.md Adım -1, Adım 0)

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz (`git status --short` — boş çıktı) |
| Main CI startup (Adım 0) | ✓ son 3 run success: `25230739077` (chore #70), `25230739065` (chore #70), `25229419559` (T42 #69) |
| Bağımlılıklar | ✓ T18 ✓ + T19 ✓ — implementation hazır, kontrat boşluğu yüzünden başlanmadı |

## Notlar

- Bu BLOCKED rapor, plan kabul kriterlerinin implementasyon edilebilir olmasına rağmen `reputationScore` kontratının çelişki üretmemesi için **proaktif** olarak açılmıştır. Karar yolu olarak kullanıcı **"Tam C"** seçti: plan kapsamındaki maddeler implement edilebilse bile çelişki kapanmadan T43'e dokunulmaması. Alternatif "C-light" (plan kapsamına daralt + M2 açık bulgu olarak bayrakla) önerildi ama kabul edilmedi.
- F2 Gate Check'in tetiklenebilmesi için karar zorunludur.
- Karar verildikten sonra T43 yeni bir yapım chat'inde baştan başlar; bu rapor o zaman finalize edilir (BLOCKED → ✓ Tamamlandı geçişi `T43_REPORT.md`'nin yeniden yazımıyla yapılır, mevcut BLOCKED bölümü "Önceki Karar Geçmişi" başlığı altında tutulur).

## Commit & PR

- Branch: `task/T43-blocked-spec-gap`
- Commit: `42e5420` — "T43: BLOCKED (SPEC_GAP) — composite reputationScore formülü tanımsız" (rapor + status + memory + M2 açık bulgu, kod yok)
- PR: [#71](https://github.com/turkerurganci/Skinora/pull/71)
- CI: ✓ PASS — run [`25231718147`](https://github.com/turkerurganci/Skinora/actions/runs/25231718147) (Detect changed paths ✓ + Lint ✓ + CI Gate ✓; Build/Unit/Integration/Contract/Migration/Docker paths-filter ile doc-only PR'da skip — beklenen davranış)
- Branch isolation (Layer 3): ✓ temiz — `git log main..HEAD` yalnızca T43 commit'i

## Bitiş Kapısı (skill task.md — BLOCKED edition)

BLOCKED akışında skill 8-madde kapısının kod-merkezli maddeleri (build/test) uygulanamaz; aşağıdaki adapte versiyon uygulandı:

- [x] Branch push edildi mi? — `task/T43-blocked-spec-gap` push'landı
- [x] PR açıldı mı? — PR [#71](https://github.com/turkerurganci/Skinora/pull/71)
- [x] PR numarası rapor footer'a yazıldı mı? — bu bölüm
- [x] Rapor + status push edildi mi? — `42e5420` ile aynı commit
- [x] CI run tamamlandı mı? — run `25231718147` concluded
- [x] CI run sonucu success mi? — ✓ PASS (CI Gate yeşil)
- [x] Branch izolasyon check temiz mi? — yalnızca T43 commit subject (`git log main..HEAD --format='%s' | grep -oE '^T[0-9]+...'` → `T43`)
- [x] Repo memory'de T43 satırı eklendi/güncellendi mi? — `.claude/memory/MEMORY.md` Current Status + Next + T43 detay satırı eklendi
