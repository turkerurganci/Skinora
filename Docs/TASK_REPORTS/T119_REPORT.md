# T119 — Reputation + cooldown sorumluluk eşlemesi

**Faz:** F7 | **Durum:** ✓ Tamamlandı (yapım) | **Tarih:** 2026-08-10

---

## Yapılan İşler

T119 bir **kapsam denetimi** görevidir: sorumluluk eşlemesinin kodu T117 dalında yazılmıştı (`ReputationAggregator` + `CancelCooldownEvaluator`), bu görevin işi o kodu 02 §3.1 / §13 karşısında bağımsız olarak denetlemekti.

**Denetimin sonucu: kod doğru, doküman ve testler değildi.**

1. **Sorumluluk haritası doğrulandı (kod değişikliği YOK).** `ReputationAggregator.ResponsibleForTimeout` ve `CancelCooldownEvaluator.IsResponsibleFor` dört fazın hepsinde 02 §3.1 (v3.0) ile birebir: `CREATED`→alıcı, `ACCEPTED`→satıcı, `SELLER_CONFIRMED`→alıcı, `PAYMENT_RECEIVED`→**satıcı**. `git diff origin/main...HEAD -- backend/src/` **boş**.

2. **06 §3.1 sorumluluk listesi v3.0'a çekildi (06 v6.2).** Liste custodial (v2.0) kaldığı için *"Teslim trade offer timeout'u (adım 6) → **alıcı**"* diyordu. 02 §13 bu listeyi "sorumluluk prensibi burada tanımlıdır" diye **tek doğru kaynak** olarak refere ediyor; 02 §3.1 tablosu ve kod ise satıcı diyor. Üç yerden ikisi doğruyken kanonik olan yanlıştı. Ek olarak adım 3'ün adı düzeltildi ("satıcı trade offer" → **hazırlık onayı**, 03 §2.3) ve her satıra `PreviousStatus` çapası eklendi — denetimin zorluğu dokümanın "adım", kodun `PreviousStatus` dilinde konuşmasıydı; çapa drift'i mekanik olarak gözlenebilir yapar.

3. **Dört fazın tamamı testle kapatıldı.** Denetim öncesi `PAYMENT_RECEIVED`→satıcı eşlemesinin (yani AC1'in) **hiçbir testi yoktu**; `CancelCooldownEvaluator`'ın timeout dalının ise hiç testi yoktu.

4. **Kapsam dışı dört açık kayda geçirildi** (proje sahibi kararı: plan + backlog) — aşağıda §Known Limitations.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/06_DATA_MODEL.md` | §3.1 `CANCELLED_TIMEOUT` sorumluluk listesi v3.0'a çekildi + `PreviousStatus` çapaları + v3.0 notu; sürüm **v6.2** |
| `Docs/11_IMPLEMENTATION_PLAN.md` | T123/T124'e timeout SystemSetting adlandırma kararı, T129'a `REFUNDED` itibar kararı kabul kriteri + gerekçe notları |
| `Docs/DEFERRED_BACKLOG.md` | §9'a 2 yeni satır (`P2P-NonDeliveryAbuseWindow`, `P2P-DeliveryTimeoutWarning`); aktif satır 34 → **36** |
| `backend/tests/.../Integration/Reputation/ReputationAggregatorTests.cs` | +3 test, 2 test adı/yorumu emekli adlandırmadan arındırıldı |
| `backend/tests/.../Integration/Reputation/CancelCooldownEvaluatorTests.cs` | +6 test (4'ü `[Theory]` vakası), history helper'ı eklendi |

**`backend/src/` altında sıfır değişiklik.**

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `PAYMENT_RECEIVED` timeout'u SATICI'ya atfediliyor | ✓ | Kod: `ReputationAggregator.cs:184` (`PAYMENT_RECEIVED => TimeoutResponsibility.Seller`), `CancelCooldownEvaluator.cs:111` (`ACCEPTED or PAYMENT_RECEIVED => isSeller`). Test: `Recompute_Cancelled_Timeout_Delivery_Phase_Hits_Seller` (satıcı 0.5 / alıcı 1.0), `Timeout_Counts_Against_The_Phase_Owner_Only(PAYMENT_RECEIVED, 1, 0)`, `Delivery_Timeouts_Push_The_Seller_Into_Cooldown`. **Negatif prova:** harita alıcıya çevrildiğinde tam olarak bu 3 test kırıldı, başka hiçbiri etkilenmedi (26/29 geçti); mutasyon geri alındı ve `git diff src/` boş doğrulandı. Doküman: 06 §3.1 bu tura kadar **alıcı** diyordu → düzeltildi |
| 2 | `ACCEPTED` timeout'u satıcıya, `SELLER_CONFIRMED` alıcıya | ✓ | Kod: `ReputationAggregator.cs:176-177`, `CancelCooldownEvaluator.cs:111-112`. Test: `Recompute_Cancelled_Timeout_SellerConfirm_Phase_Hits_Seller`, `Recompute_Cancelled_Timeout_Payment_Phase_Hits_Buyer`, `Timeout_Counts_Against_The_Phase_Owner_Only(ACCEPTED, 1, 0)` ve `(SELLER_CONFIRMED, 0, 1)`. Dördüncü faz (`CREATED`→alıcı) de aynı Theory'de ve `Recompute_Cancelled_Timeout_Accept_Phase_Hits_Buyer`'da kapalı |

**Denetimin ikinci yarısı — sorumluluk haritasını besleyen yol.** Harita yalnızca `TransactionHistory.PreviousStatus` üzerinden çalışır; o satır yazılmazsa iptal **hiçbir tarafın** paydasına yazılmaz. Üretimdeki iki timeout yolu da satırı yazıyor ve ardından itibar/cooldown'u aynı DB transaction'ında tazeliyor: `TimeoutExecutor.cs:88` (ödeme fazı, per-tx Hangfire job) ve `DeadlineScannerJob.cs:148` (diğer üç faz, tarayıcı). Bu bağımlılık iki testle (`..._Without_History_Row_...`) davranış olarak sabitlendi.

**Denetimin komşu yüzeyleri (hepsi P2P-doğru bulundu, değişiklik gerekmedi):** `TransactionTimedOutNotificationConsumer` faz×taraf metinleri (teslimat → satıcıya *"Item'ı zamanında göndermediniz"*), `TimeoutSideEffectPublisher` teslimat fazı iade bacağı (alıcıya para iadesi, item iadesi yok), `TransactionCancellationService` cooldown'u yalnız iptali başlatan tarafa uyguluyor.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0 Error / 0 Warning | `dotnet build Skinora.sln -c Release` |
| Unit | ✓ 1330/1330 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — T118 ile aynı (yeni testlerin hepsi Integration) |
| Integration | ✓ 1086/1086 | `--filter "FullyQualifiedName~.Integration"` — T118'deki 1077 + 9 yeni |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` |
| Migration | Yok | Şema değişikliği yok |

Odaklı koşum (`FullyQualifiedName~Reputation`): **29/29** — aggregator 12 + cooldown 11 + refresher 6.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⬚ Bekliyor (ayrı chat — INSTRUCTIONS §3.3 izolasyon kuralı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**
- Yeni paket: **Yok**

## Commit & PR

- Branch: `task/T119-reputation-cooldown-audit`
- PR: _(push sonrası doldurulacak)_
- CI: _(run sonrası doldurulacak)_

## Known Limitations / Follow-up

Denetimde çıkan, T119'un kabul kriterleri dışında kalan dört açık. Proje sahibi kararı: **plan + backlog'a kaydedilsin** (sadece raporda bırakılmasın).

| # | Açık | Nereye kaydedildi |
|---|---|---|
| 1 | **02 §14.2'nin teslim etmeme yaptırımı kodda yok.** v3.0'da eklenen yuvarlanan pencere kuralı (eşiği aşan ilk tekrar → `ABNORMAL_BEHAVIOR` flag, sonraki → otomatik askı) hiçbir F7 görevine ait değil. Bugünkü caydırıcılık yalnız itibar düşüşü + iptal cooldown'u | `DEFERRED_BACKLOG` §9 → `P2P-NonDeliveryAbuseWindow` 🟡 |
| 2 | **Timeout uyarısı yalnız ödeme fazında var.** 03 §4.5 uyarıyı *"Tüm timeout'lar için"* diye tanımlıyor (02 §3.4 de faz ayrımı yapmıyor), ama `WarningDispatcher` yalnız `SELLER_CONFIRMED`'ı uyarıyor — T48 kapsamı. Dört fazın üçü, bu arada P2P'nin kritik teslimat penceresi, uyarısız | `DEFERRED_BACKLOG` §9 → `P2P-DeliveryTimeoutWarning` 🟡 |
| 3 | **`REFUNDED` itibar formülünün paydasında yok.** Bugün doğru (v2.0'da yalnız admin dispute iadesiydi), ama T129 `delivery_reversed` ile ikinci bir giriş açıyor: trade'ini geri alan satıcı. Formül değişmezse en ağır dolandırıcılık senaryosu itibara hiç yansımaz | `11_IMPLEMENTATION_PLAN` → T129 kabul kriteri + gerekçe notu |
| 4 | **İki timeout SystemSetting'i custodial adında.** `trade_offer_seller_timeout_minutes` / `trade_offer_buyer_timeout_minutes` bugün üretimde **hiç okunmuyor** (yalnız seed + katalog + 4 dil FE etiketi), ama T123/T124 bu iki fazın deadline'ını armlarken kullanacak. Admin panelinde satıcının teslimat penceresini "Alıcı trade offer timeout süresi" adlı kutu yönetir hâle gelir | `11_IMPLEMENTATION_PLAN` → T123 kabul kriteri + rename/koru karar notu, T124'e çapraz referans |

## Notlar

- **Working tree (Adım -1):** temiz — `git status --short` boş.
- **Main CI startup check (Adım 0):** son 3 run `success` — `31380447239`, `31380447166`, `31378243789`.
- **Dış varsayım (Adım 4): yok.** Salt backend denetimi; yeni paket, dış API, plan tier veya ortam varsayımı yok. Yeni SystemSetting/env değişkeni eklenmedi.
- **Kapsam kararı (proje sahibi, 2026-08-10):** "doküman + test" — kod zaten doğru olduğu için üretim kaynağına dokunulmadı; 06 §3.1 düzeltmesi T133a doküman turuna bırakılmadı, çünkü 02 §13'ün refere ettiği kanonik satır yanlış kalırdı.
- **Negatif prova yöntemi:** `PAYMENT_RECEIVED` eşlemesi iki serviste de alıcıya çevrildi, süit koşuldu (3 kırık / 26 geçen), mutasyon geri alındı, `git diff` ile kaynağın main ile birebir olduğu doğrulandı. Guard'ların gerçekten koruduğu bu şekilde kanıtlandı (T118'de kurulan standart).
