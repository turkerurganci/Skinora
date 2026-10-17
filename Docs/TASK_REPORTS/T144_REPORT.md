# T144 — Ödenecek tutarın iki haneye sabitlenmesi [RİSKLİ]

**Faz:** F8 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Kusur yeniden ölçüldü.** `FinancialCalculator.CalculateCommission` komisyonu `ROUND(fiyat × oran, 6, ToZero)` ile tutuyordu; `TotalAmount = fiyat + komisyon`, `PaymentAddress.ExpectedAmount = TotalAmount`; `TransactionDetailService.FormatMoney` `"0.00"` ile 2 haneye yuvarlıyor, `AmountValidationService` `received == expected` istiyordu. %2'de komisyon `fiyat_kuruş × 2 / 100` olduğundan toplam ancak kuruş 50'nin katıysa 2 haneye sığar: 10,00–99,99 arasındaki 9 000 fiyatın **180'i (%2)**. 10,55 → beklenen 10,761, ekranda "10,76". Yeni testlerin eski koda karşı kırmızı olduğu aşağıda ("Eski koda karşı").
- **Kural seçildi (öneriyle kaydedildi — ⚠):** komisyon kuruşa **aşağı** kesilir — `ROUND(fiyat × oran, 2, ToZero)`. Fiyat zaten 2 ondalıklı girilir (07 §7.2, `TransactionCreationService.TryParsePositiveDecimal`), dolayısıyla toplam ve beklenen tutar her zaman 2 ondalıklıdır.
  - **Neden aşağı:** alıcıya en adil yön budur. Alıcı hiçbir zaman `fiyat × oran`dan fazla ödemez; platform işlem başına 1 sentten azını bırakır. Yarımı yukarı (`AwayFromZero`) ya da en yakına yuvarlama alıcıdan yarım sente kadar fazla alırdı (ör. 10,25 × %2 = 0,205 → 0,21). Bu, 09 §14.3'ün zaten yazılı "kullanıcı aleyhine yuvarlama yapılmaz" kuralıyla aynı yöndür; yalnız ölçek 6'dan 2'ye iner.
  - **Platformun bedeli:** komisyon başına < 0,01 USDT. %2'de 50 USDT'lik bir satışta komisyon zaten tam kuruştur; kayıp yalnız kuruşu 50'nin katı olmayan fiyatlarda ve en çok 0,0098'dir.
- **Backend (`FinancialCalculator`):**
  - `PayableScale = 2`, `IsWholeCents(decimal)`.
  - `CalculateCommission`: fiyat kuruştan küçük hane taşıyorsa `ArgumentException`; sonuç `Math.Round(fiyat × oran, 2, MidpointRounding.ToZero)`.
  - `CalculateTotal(price, commission, networkFeeAmount = 0)`: üç bileşenin de kuruş olduğunu zorlar. **T149 için hazırlık:** TRON ağ ücreti satırı (11 §F8 K8) bu parametreden girer; T149 satırı 2 haneye **yukarı** yuvarlayıp verir. Bugün çağıran 0 geçer — K8 uygulanmadı.
  - Depolama ölçeği `decimal(18,6)` değişmedi; 6 hane zincirden ölçülen tutarlar (gas fee, gelen transfer, iade) içindir.
- **Gösterim (`Skinora.Shared.Domain.MoneyFormat.Display`):** `"0.00####"` (invariant), en az 2, en çok 6 hane, **yuvarlamaz**. Kullanıldığı yerler:
  - İşlem detayı (07 §7.5): `price`, `commissionAmount`, `totalAmount`, `payment.expectedAmount`. 2 haneli değerde çıktı `"0.00"` ile birebir aynıdır (ızgara testi 0,00–200,00 arası her kuruşu karşılaştırır). T144 öncesi kalmış 6 haneli bir satır `"10.761"` diye **tam** gösterilir — eski `"0.00"` onu `"10.76"`ya çeviriyordu, kusurun kendisi buydu.
  - Bütün bildirim tutarları (8 tüketici): `PAYMENT_WINDOW_OPEN` "Send {Amount} USDT", eksik/fazla ödeme, geç ödeme, yanlış token, ödeme alındı, payout, davet. Eskiden `"0.######"` ile "102" / "10.5" yazıyordu, ekran "102.00" / "10.50"; artık aynı metin. Zincirden gelen tutar (`10.761`) olduğu gibi kalır.
  - Bilgi amaçlı, zincirden ölçülen tutarlar (gas fee, iade net, payout dökümü) `FormatMoney` `"0.00"` ile yuvarlanmaya devam eder (07 §2.8'e yazıldı).
- **Frontend:** `lib/utils/money.ts` — `calculateCommission` / `calculateTotal` tamsayı kuruşla, aynı kesmeyle. `Step2Details` ve `Step4Summary` önizlemesi buna geçti. Eskisi `Number(price) × rate` + `Intl.NumberFormat` (yarım-yukarı) idi: 10,25'te "0,21" gösterip sunucu 0,20 alıyordu; 10,55'te "0,21" gösterip sunucu 0,211 alıyordu.
- **Doğrulama (değişmedi):** tam eşitlik. 2'den fazla haneli zincir tutarı mevcut kollara düşer ve bu artık testle sabit:
  - 10,761 → fazla ödeme: işlem `PAYMENT_RECEIVED`; fazla 0,001 en düşük iade eşiğinin altında → iade yok, `RefundBlockedAdminAlertEvent`.
  - 10,759 → eksik ödeme: işlem `SELLER_CONFIRMED`'da kalır, 10,759 `INCORRECT_AMOUNT_REFUND` ile iade sırasına girer.
- **Sidecar:** tutar karşılaştırmıyor (yalnız ham değeri 6 haneli metne çeviriyor — `formatTokenAmount`); değişiklik yok. `sidecar-fake` beklenen tutarı veritabanından okuyor; değişiklik yok.
- **Mevcut veri:** migration ya da backfill **yok**. Canlı veri yok; T144 öncesi açılmış Nile/yerel işlemler `DEPLOY_RUNBOOK §G.0b` (T143) sıfırlamasıyla gider. Sıfırlanmamış bir ortamda eski işlem tutarını tam gösterir ve 2 haneli borsa ödemesiyle eşleşmez — iptal edilip yeniden açılır. Karar 06 §8.3 "Mevcut veri" satırına ve §G.0b sonuna yazıldı.
- **Doküman (tek kural, GUARDRAILS §5):** 02 §5 (v4.5) · 06 §3.5 `CommissionAmount`/`TotalAmount`, §3.7 `ExpectedAmount`, §8.3 (ölçek / yuvarlama / hesaplama sırası / doğrulama / yeni "Mevcut veri" satırı) (v6.21) · 07 §1.3 "Tutarlar", §2.8, §2.10 (v4.6) · 09 §7.2, §14.2, §14.3, §14.4, §14.5 (v1.3) · 05 §3.3 finansal hesaplama notu (v3.13 — main'e T142 ile gelen v3.12'nin üstüne) · DEPLOY_RUNBOOK §G.0b notu. **Çelişki çözüldü:** 06 §8.3 "her şey 6 hane" ile 07/09 "API 2 hane" çelişkisi — artık depolama ölçeği (6) ile ödenecek tutar ölçeği (2) ayrı yazılı; ödenecek tutarlarda API metni tutarın kendisi.

## Etkilenen Modüller / Dosyalar

- `backend/src/Modules/Skinora.Transactions/Domain/Calculations/FinancialCalculator.cs`
- `backend/src/Skinora.Shared/Domain/MoneyFormat.cs` (yeni)
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs`
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/` — 8 tüketici (`BuyerPaymentExcessRefunded`, `BuyerPaymentInsufficient`, `HappyPathMilestone`, `LatePaymentRefundRequested`, `PaymentReceived`, `PayoutCompleted`, `TransactionInvite`, `WrongTokenRefundRequested`)
- `frontend/src/lib/utils/money.ts` (yeni), `frontend/src/components/transactions/new/Step2Details.tsx`, `Step4Summary.tsx`
- Testler: `Skinora.Transactions.Tests/Unit/Calculations/PayableAmountGridTests.cs` (yeni), `FinancialCalculatorTests.cs`; `Skinora.Shared.Tests/Unit/MoneyFormatTests.cs` (yeni); `Skinora.Notifications.Tests/Unit/BuyerPaymentAmountNotificationConsumerTests.cs` (yeni), `HappyPathNotificationConsumerTests.cs`, `LatePaymentRefundRequestedNotificationConsumerTests.cs`, `TransactionInviteNotificationConsumerTests.cs`; `Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs`, `TransactionDetailServiceTests.cs`; `Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs`, `TransactionLifecycleEndpointTests.cs`; `frontend/src/lib/utils/money.test.ts` (yeni)
- Doküman: `Docs/02_PRODUCT_REQUIREMENTS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `09_CODING_GUIDELINES.md`, `DEPLOY_RUNBOOK.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Alıcının ödeyeceği toplam her zaman 2 ondalıklı; ekranda gösterilen tutar ile beklenen tutar birebir aynı (10,55 → 10,761 / "10,76" kusuru) | ✓ | `PayableAmountGridTests` — 10,00–99,99 arası her fiyat + 17 uç fiyat × 7 oran: komisyon ve toplam kuruş, `MoneyFormat.Display(total)` geri ayrıştırınca `total`, tam 2 hane. `TransactionCreationServiceTests.Persists_Whole_Cent_Commission_And_Total` (10,55 → 0,21 / 10,76). `TransactionLifecycleEndpointTests.Create_Then_Detail_Shows_The_Persisted_Two_Decimal_Total` (gerçek oluştur + detay uçları: `"10.76"` = kalıcı 10,76). `TransactionDetailServiceTests.Payment_ExpectedAmount_Is_Shown_Exactly_As_Validation_Expects`. `BlockchainWebhookEndpointTests.T144_TwoDecimalPayment_Of_The_Shown_Total_Is_Accepted_Exactly` (10,76 ödemesi tam kabul, iade yok) |
| 2 | Çözüm daha fazla hane göstermek DEĞİL, toplamı 2 haneli yapmak | ✓ | Toplam kaynağında 2 haneli (`CalculateCommission` → `ROUND(…, 2, ToZero)`); `MoneyFormat.Display` 2 haneli değerde `"0.00"` ile aynı metni verir (ızgara testi 20 001 kuruş). Yalnız T144 öncesi kalmış satır ve zincirden gelen tutar tam gösterilir — ödenecek yeni bir tutar 2 haneden fazla göstermez |
| 3 | Komisyon yuvarlama kuralı 02/06/07/09'da tek ve aynı | ✓ | 02 §5 notu · 06 §3.5 + §8.3 · 07 §2.8 (+ §1.3, §2.10) · 09 §7.2 + §14.3 + §14.4: hepsi `ROUND(fiyat × oran, 2, ToZero)` / "kuruşa aşağı" ve "(öneriyle kaydedildi — ⚠)". 05 §3.3 notu da aynı. Eski "scale 6 komisyon" ve "F8 hedefi (T144)" ibareleri kaldırıldı (`grep -n "ROUND(Price × CommissionRate, 6" Docs/` → boş) |
| Test | Unit (sınır değerler) | ✓ | `FinancialCalculatorTests` (yarım kuruş 10,25 → 0,20; 10,75 → 0,21; 99,99 × %2,5 → 2,49; 0,49 → 0; kuruştan küçük fiyat/komisyon/ağ ücreti reddi; ağ ücreti satırı toplama), `PayableAmountGridTests`, `MoneyFormatTests`, FE `money.test.ts` |
| Test | Integration (ödeme eşleşmesi) | ✓ (API) / CI (SQL Server) | `BlockchainWebhookEndpointTests` T144 üçlüsü (10,76 tam · 10,761 fazla · 10,759 eksik) + `TransactionLifecycleEndpointTests` yerelde 75/75. Docker gerektiren SQL Server testleri (`TransactionCreationServiceTests`, `TransactionDetailServiceTests`, `HappyPathNotificationConsumerTests`) CI'da — aşağıdaki "Notlar" |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend unit (tüm `*.Tests`, CI filtresi `FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract`) | ✓ 1 710 geçti, 0 kırmızı; 16 test Docker olmadan koşamadı | Transactions 693 · Shared 431 · Platform 139 · Notifications 117/133 · Auth 92 · API 74 · Steam 59 · Realtime 40 · Disputes 25 · Users 22 · Fraud 18. Notifications'taki 16 kırmızının hepsi `Discord/TelegramNotificationChannelHandlerTests` — `DockerUnavailableException` (Testcontainers; bu makinede Docker kapalı), T144 ile ilgisiz, CI'da koşar |
| Backend integration — API (SQLite) | ✓ 75/75 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~BlockchainWebhookEndpointTests\|FullyQualifiedName~TransactionLifecycleEndpointTests"` |
| Backend integration — SQL Server (Testcontainers) | CI | Yerelde Docker kapalı; CI "Integration" işi koşar |
| Frontend vitest | ✓ 289/289 | `npx vitest run` (28 dosya; `money.test.ts` dahil) |
| Frontend tsc / eslint | ✓ | `npx tsc --noEmit`, `npm run lint` temiz |
| Frontend prettier | ✓ (LF) | Değişen iki `.tsx` çalışma kopyasında CRLF (core.autocrlf artifaktı, dokunulmamış `NewTransactionForm.tsx` de aynı uyarıyı veriyor); LF kopyaları repo `.prettierrc` ile temiz |
| `dotnet format --verify-no-changes --severity error` | ✓ | çıkış 0 |
| `dotnet build -c Release` | ✓ | 0 hata, 0 yeni uyarı |

**Eski koda karşı (ayırt edici ölçüm):** komisyon satırı geçici olarak eski `RoundMoney(price × rate)` (6 hane, koruma yok) hâline döndürülüp yeni testler koşuldu: `FinancialCalculatorTests` + `PayableAmountGridTests` **28/65 kırmızı** (ızgaranın 7 oranının 7'si, uç fiyatların 7'si, 10,55 kusuru, yarım kuruş, kuruştan küçük reddi), API testlerinden **3/4 kırmızı** (`T144_TwoDecimalPayment…`, `T144_ThirdDecimalOverpayment…`, `Create_Then_Detail…`). `T144_ThirdDecimalUnderpayment…` eski kodda da yeşil (10,759 her iki beklenen tutarın da altında) — bu test kusuru değil, 2 haneden fazla zincir tutarının davranışını sabitler. Kod geri yüklendi, aynı testler yeşil.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (F8 yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok** (kolon tipleri `decimal(18,6)` aynı; backfill yok — gerekçe yukarıda "Mevcut veri")
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T144-iki-haneli-tutar`
- Commit: `d386a1e` — T144: Ödenecek tutar iki haneye sabitlendi — komisyon kuruşa aşağı kesilir
- PR: #340
- CI: rapor commit'inden sonraki run izleniyor (sonuç PR'da ve repo memory satırında)

## Known Limitations / Follow-up

- **Fiyat girişinde 2'den fazla hane:** frontend form doğrulaması (`NewTransactionForm.priceError`) hane sayısına bakmıyor; `10.555` girilirse önizleme boş kalır, sunucu 400 `VALIDATION_ERROR` döner. T144 öncesinden; ayrı bir hata metni (4 dilde yeni anahtar) gerektirdiği için kapsam dışı bırakıldı.
- **Bilgi tutarlarının gösterimi:** gas fee, iade net tutarı ve payout dökümü 2 haneye yuvarlanarak gösterilir (`"0.00"`, yarımı yukarı). Ödenecek tutar değiller; 07 §2.8'e yazıldı. Alıcının cüzdanına düşen iade tutarı 6 haneli olabilir ve ekranda kuruşa yuvarlanmış görünür.
- **Admin ekranı:** admin işlem detayı tutarları JSON number olarak alır (`admin.ts` `totalAmount: number`) ve `formatStablecoin(number)` ile 2 haneye biçimler. Yeni işlemlerde tutar zaten 2 haneli olduğu için sonuç aynı; admin yüzeyi alıcıya dönük değil.
- **T149:** TRON ağ ücreti satırı `CalculateTotal`'ın `networkFeeAmount` parametresinden girecek ve 2 haneye **yukarı** yuvarlanmış olmak zorunda (kuruştan küçük değer `ArgumentException`).

## Notlar

- **Working tree:** temiz (oturum başı `git status --short` boş).
- **Main CI startup check (2026-10-18):** son tamamlanmış run'lar `39075036529` (Docker Publish, T143) ✓ · `39070845422` (CI, T141) ✓ · `39070845359` (Docker Publish, T141) ✓; T143'ün CI run'ı `39075036563` o an `in_progress` (sayılmadı). Dal `origin/main` = `b9b42da`'dan (T143 merge) kesildi.
- **Bağımlılık:** T141 ✓ Tamamlandı.
- **Dış varsayımlar:**
  - *BtcTurk ve Paribu USDT'yi en çok 2 haneyle gönderir.* Kanıt: görev tanımı (11 §F8 T144) ve F8 araştırması (2026-10-04/05, iki kez doğrulandı — görev talimatı). Bu görevde yeniden ölçülmedi; kural 2 hane varsayımına bağlı, hane sayısı daha da düşük bir borsa bilinmiyor.
  - *Fiyat 2 ondalıklıdır.* Kanıt: `TransactionCreationService.TryParsePositiveDecimal` (`decimal.Round(parsed, 2) != parsed → false`) ve 07 §7.2 `price` "String, 2 ondalık". Yeni koruma `CalculateCommission`'da da var.
  - *Komisyon oranı 4 ondalıklıdır.* Kanıt: 06 §3.5 `CommissionRate decimal(5,4)`. Frontend tamsayı hesabı oranı 1/10 000 birimine yuvarlar.
  - *Sidecar tutar karşılaştırmaz.* Kanıt: `grep -rni "expectedAmount\|commission" sidecar-blockchain/src` → yalnız ilgisiz bir yorum; `PaymentMonitorRules.formatTokenAmount` yalnız biçimler.
  - Paket / plan / API varsayımı: yok (yeni bağımlılık eklenmedi).
- **Mini güvenlik kontrolü:** secret yok · yeni uç ya da yetki değişikliği yok · yeni kullanıcı girdisi yok (fiyat ayrıştırması aynı; hesaplayıcıya ek koruma) · yeni dış bağımlılık yok. Para etkisi: alıcı aleyhine değişiklik yok; platform komisyon başına < 0,01 USDT bırakır.
- **(öneriyle kaydedildi — ⚠) kararları — F8 Gate Check listesine:** (1) komisyonun yuvarlama yönü **aşağı** (alıcı lehine; alternatif yarımı yukarı alıcıdan ≤ 0,005 fazla alırdı); (2) migration/backfill yok, T144 öncesi Nile satırları §G.0b sıfırlamasıyla gider; (3) bildirim tutarları `MoneyFormat.Display` (en az 2 hane, yuvarlamasız) — "Send 102 USDT" artık "Send 102.00 USDT".
