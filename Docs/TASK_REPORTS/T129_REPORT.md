# T129 — Mutabakat süresi + trade geri alma koruması

**Faz:** F7 (P4 — Payout tamponu) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-16

---

## Yapılan İşler

Bu görev, teslim edilmiş bir işlemin parasının **ne zaman** ve **hangi koşulla** satıcıya gideceğini kuran son halkadır. Steam korumalı bir trade'i 7 gün boyunca geri alınabilir tutuyor ve bunu trade'in her iki tarafı da Steam Support'a başvurmadan yapabiliyor (02 §4.5.1); dolayısıyla teslimatın doğrulanmış olması ödemek için yeterli değil. Görev iki şeyi birden getirir: **bekleme** (`PayoutEligibleAt`) ve — asıl koruma olan — **sürenin sonundaki kontrol**.

1. **Üç `Settlement` SystemSetting'i** (06 §3.17, 02 §16.2): `payout_settlement_days` (8, validator 7'nin altını reddeder), `settlement.unreadable_escalation_hours` (48), `settlement.reversal_auto_refund_enabled` (false — launch kapısı). Okuma tarafı `SettlementSettingsProvider`; her fallback güvenli yönde (poisoned satır pencereyi kısaltamaz, kapıyı açamaz).
2. **`PayoutEligibleAt` yazıcısı** (`SettlementWindowStamper`) ve — daha önemlisi — kolonun **ITEM_DELIVERED giriş guard'ına** eklenmesi. `DeliverItem`'ın iki üretim çağıranı (T126 confirm-receipt, T127 timeout turu) stamper'dan geçmek zorunda; geçmezse teslimat hiç gerçekleşmez.
3. **`SettlementVerificationService`** — 02 §4.5.1'in son kontrolü, **iki taraflı** (K1). Alıcı tarafı önce `DeliveredBuyerAssetId` ile tam test edilir, o yoksa sınıf sayımı `baseline + 1` referansına karşı okunur. Item gitmişse satıcı tarafı okunur: orijinal asset geri döndüyse geri alma imzası, dönmediyse ayırt edilemeyen ayrılma.
4. **`SettlementVerificationJob`** (5 dakikada bir, batch 10) — dört verdict'i eyleme çevirir: `SettlementVerifiedAt` damgası / `delivery_reversed` (kapı açıksa) / admin eskalasyonu / eşiğe kadar sessiz retry.
5. **Geri alma dalı**: REFUNDED + `PaymentRefundToBuyerRequestedEvent` (WP2 boru hattı) + hesap düzeyinde `DELIVERY_REVERSED` fraud flag + iki taraf bildirimi + realtime status event + `TransactionHistory` satırı.
6. **Payout ve sweep kapıları**: her ikisi de artık `SettlementVerifiedAt NOT NULL ∧ DeliveryReversedAt NULL` ister. Sweep kapısı yeni bir açığı kapatır (aşağıda).
7. **İtibar** (K4 / planın açık maddesi): 06 §3.1 paydası `REFUNDED[DeliveryReversedAt NOT NULL]` ile genişledi; admin dispute iadesi dışarıda kaldı.
8. **İki yeni kolon** (`SettlementCheckedAt`, `SettlementEscalatedAt`) + migration `T129_SettlementCheckColumns`.

### Yapım öncesi kararlar (proje sahibi, 2026-08-16 — dördü de öneri yönünde onaylandı)

| # | Karar | Gerekçe |
|---|---|---|
| K1 | Kontrol **iki taraflı**; ayırt edilemeyen ayrılma admin'e | Dokümanın "item alıcıda yok = geri alınmış" eşitliği pencerenin tamamında geçerli değil: Steam trade ile edinilen item'ı **7 gün** kısıtlıyor (T122 runbook §6.1) ama mutabakat **8 gün** — son bir gün alıcı skini meşru devredebiliyor. Tek taraflı okuma o alıcıya tam iade verir, item'ı da bırakır, teslim etmiş satıcıya fraud flag koyar: kuralın satıcıya karşı kapattığı dolandırıcılığın **simetriği**. Geri alma item'ı satıcıya döndürür, devir döndürmez |
| K2 | Negatif dal **launch kapısı** arkasında | Geri alma imzası ölçülmemiş bir çıkarım (T122 gerçek rollback gözleyemedi, runbook §7). T125'in `delivery.inventory_evidence_auto_release_enabled` kapısının ikizi: kapalıyken imza kaydedilir + admin'e eskale edilir, para parkta |
| K3 | Okunamaz dal **ayarlı eşik** (48s) + admin kuyruğu | Sınırsız retry alıcının profilini gizleyerek satıcının ödemesini süresiz bloklamasına izin verirdi. Eşik yalnız "ne zaman insana sorulur"u belirler; ödeme her hâlükârda parkta |
| K4 | Yeni `FraudFlagType.DELIVERY_REVERSED`, **hesap** düzeyinde | 02 §4.5.1 "satıcı **hesabına**" diyor ve §14.2 tekrarı sayıyor — sayılabilmesi için vakanın kuyrukta ayırt edilebilir olması gerek. 06 §3.12: ACCOUNT_LEVEL satırları `TransactionId` taşımaz, işlem ID'si details payload'ında |

### Yapım sırasında bulunan ve kapatılan açık

**`SweepQueueJob`'da mutabakat kapısı yoktu.** Job'ın kendi gerekçesi (WP3, owner kararı 2026-06-15) "depozit, iade çekilebilecek yerde kalmalı" olduğu hâlde kapısı yalnız `ITEM_DELIVERED`'dı. Yedinci günde geri alınan bir trade tam da o iadeyi üretir; sweep teslimatta çalıştığı için depozit, ihtiyaç duyulmasına en yakın anda boşalmış olurdu. Kapı payout'unkiyle aynı çifte bağlandı.

## Etkilenen Modüller / Dosyalar

**Yeni (Transactions/Settlement):** `ISettlementSettingsProvider.cs`, `SettlementSettingsProvider.cs`, `SettlementWindowStamper.cs`, `ISettlementVerificationService.cs`, `SettlementVerificationService.cs`, `SettlementVerificationResult.cs`, `SettlementVerificationJob.cs`
**Yeni (Shared/Events):** `SettlementReversalDetectedEvent.cs`, `SettlementReviewRequiredEvent.cs`
**Yeni (Notifications):** `SettlementReversalNotificationConsumer.cs`, `SettlementReviewAdminNotificationConsumer.cs`
**Yeni (migration):** `20260816140704_T129_SettlementCheckColumns`

**Değişen:** `Transaction.cs` (+2 kolon) · `TransactionStateMachine.cs` (giriş invariantı) · `TransactionConfiguration.cs` (index yorumu) · `DeliveryConfirmationService.cs`, `DeliveryTimeoutRound.cs` (stamper) · `SellerPayoutQueueJob.cs`, `SweepQueueJob.cs` (kapı) · `ReputationAggregator.cs` (payda) · `ITransactionFraudFlagWriter.cs` + `TransactionFraudFlagWriter.cs` (hesap flag'i portu) · `FraudFlagType.cs` (+1 değer) · `SystemSettingsCatalog.cs`, `SystemSettingSeed.cs`, `SystemSettingsValidator.cs` · `TransactionsModule.cs`, `OutgoingTransferJobsRegistrar.cs` (DI + cron)

**Frontend:** `lib/admin/settingsCatalog.ts` (`settlement` grubu — aksi hâlde üç ayar "Diğer" kovasına düşerdi) + 4 dil `adminSettings.groups.settlement`

**Dokümanlar:** 02 §4.5.1/§16.2 · 03 §2.4 · 05 §4.2 · 06 §3.1/§3.5/§3.17 · 07 §9.8 · 11 (T129 kararları) · DEPLOY_RUNBOOK §C + yeni §I

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `payout_settlement_days` SystemSetting (varsayılan 8) eklendi | ✓ | `SystemSettingSeed.cs` satır 61 (`Default(61, ..., "8")`), `SystemSettingsCatalog.cs` (`settlement` API kategorisi), migration `InsertData`. Validator 7 altını reddeder — `SystemSettingsValidator.MinimumSettlementDays` |
| 2 | ITEM_DELIVERED girişinde `PayoutEligibleAt` hesaplanıyor | ✓ | `SettlementWindowStamper.Stamp` iki çağıranda; **guard koşulu** olduğu için atlanamaz (`HasDeliveryEntryInvariant`). Testler: `DeliverItem_WithoutPayoutEligibleAt_ThrowsInvalidTransition`, confirm-receipt ve timeout-round happy path'lerinde `PayoutEligibleAt = now + 8g` assertion'ı |
| 3 | `SellerPayoutQueueJob` yalnız `PayoutEligibleAt` geçmiş işlemleri alıyor | ✓ | T126-F1'de erken uygulanmıştı; T129 kapıyı tamamladı (`SettlementVerifiedAt != null && DeliveryReversedAt == null`). Testler: `ElapsedWindow_WithoutSettlementVerification_IsSkipped`, `ReversedDelivery_IsSkipped_EvenWithSettlementStamp` |
| 4 | Ödeme öncesi son kontrol (item / yok / okunamıyor) | ✓ | `SettlementVerificationService` + `SettlementVerificationJob`. K1 gereği dört dal: 8 servis testi + 10 job testi |
| 5 | COMPLETED guard'ı: `SettlementVerifiedAt NOT NULL && DeliveryReversedAt NULL` | ✓ | T117/T118'de yazılmıştı (`HasSettlementClearance`), T129'da kanıtlandı ve aynı çift payout+sweep sorgularına yansıtıldı (guard tek başına yetmez: ikisi de COMPLETED'dan önce para hareket ettirir) |
| 6 | Süre içinde açılan dispute ödemeyi bloklar | ✓ | Üç sorguda da `!HasActiveDispute` (settlement job + payout + sweep). Test: `IneligibleTransactions_AreNotEvenRead(kind: "dispute")` |
| 7 | `SweepQueueJob` aynı kapıya bağlandı | ✓ | Sorgu + döngü içi yeniden doğrulama. Testler: `UnverifiedSettlement_IsSkipped`, `ReversedDelivery_IsSkipped` |
| 8 | `delivery_reversed` → REFUNDED'ın itibara etkisine karar verildi ve 06 §3.1 yazıldı | ✓ | K4 kararı: paydaya girer. 06 §3.1 formülü + `ReputationAggregator` + 2 test (`..._By_Delivery_Reversal_Counts_Against_Seller_Only`, `..._By_Admin_Dispute_Excludes_Both_Parties`) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (`Category=Unit`) | ✓ 236/236 | `dotnet test --filter "Category=Unit"` — Transactions 131, API 46, Notifications 40, Disputes 17, Users 2. Enum değeri eklendiği için tam Unit suite koşuldu (parity kuralı) |
| Backend tam suite | ✓ 2643/2643 | Assembly başına seri koşum: Transactions **995** · API **540** · Shared **400** · Platform **189** · Notifications **171** · Auth 120 · Fraud **91** · Disputes 68 · Realtime 40 · Steam 39 · Users 22 · Admin 22 · Payments 6 |
| Build | ✓ 0 warning / 0 error | `dotnet build -c Debug` |
| FE lint | ✓ exit 0 | `npm run lint` |
| FE i18n parity | ✓ 1303 × 4 | `npm run i18n:check` — "identical key sets"; T128 tabanı 1302 + 1 (`adminSettings.groups.settlement`) |
| FE vitest | ✓ 33/33 | 9 dosya |
| FE tsc | ✓ exit 0 | `npx tsc --noEmit` |

**Yeni testler:** `SettlementVerificationServiceTests` (8), `SettlementVerificationJobTests` (16 — 6'sı theory vakası), `SettlementWindowStamperTests` (3), + mevcut suite'lere 7 kapı/regresyon testi.

**Paralel koşum artefaktı (kanıtlı, T129 kaynaklı değil).** İlk tam suite koşumu 13 assembly'yi **paralel** çalıştırdı ve 26 başarısızlık verdi. Bunların 9'u gerçekti (seed satır sayısı 60→63, `payout_settlement_days`/`settlement.*` anahtar listeleri, `FraudFlagType` 5→6 parity) ve düzeltildi; kalan 17'si tek paylaşımlı SQL Server üzerindeki çekişmeden geliyordu — assembly başına **seri** yeniden koşumda Fraud 91/91, Notifications 171/171, Platform 189/189, Shared 400/400 hepsi temiz geçti (F6 gate'inde de kaydedilmiş olan "integration timeout artefaktı" ile aynı sınıf). FE vitest'in ilk koşumu da aynı yükün altında worker başlatamadı; backend suite bitince 33/33 geçti.

## Altyapı Değişiklikleri

- **Migration:** `20260816140704_T129_SettlementCheckColumns` — `Transactions`'a 2 nullable `datetime2` kolon + 3 `SystemSettings` seed satırı. Down temiz (DeleteData + DropColumn).
- **Config:** 3 yeni SystemSetting (hepsi `Default(...)` → `IsConfigured = true`, env ile override edilemez).
- **Hangfire:** yeni recurring job `settlement-verification` (`*/5 * * * *`), `OutgoingTransferJobsRegistrar`'da kayıtlı.
- **Docker:** değişiklik yok.

## Güvenlik Kontrolü

- Secret sızıntısı: yok (yeni secret/credential yok).
- Auth/authorization: yeni endpoint yok; job SYSTEM aktörü ile çalışır, admin yüzeyi mevcut `ADMIN_ESCALATION` bildirimidir.
- Input validation: yeni kullanıcı girdisi yok; SystemSetting değerleri validator'dan geçer (`payout_settlement_days ≥ 7`).
- Yeni dış bağımlılık: yok.

## Known Limitations / Follow-up

- **Geri alma sonrası asset ID davranışı ölçülmedi.** Servis "satıcının orijinal `ItemAssetId`'si geri döndü" imzasını arar; Steam rollback'te ID'yi koruyor mu bilinmiyor (T122-B7 kapanmadı). Korumuyorsa imza oluşmaz ve vaka **ayırt edilemeyen ayrılma** olarak admin'e düşer — güvenli yön. DEPLOY_RUNBOOK §I.3 adım 2 bunu ilk gerçek vakada kapatmayı öngörür.
- **Otomatik iade dalı launch'ta kapalı** (K2). Kapı açılana kadar geri alma vakaları admin kararıyla `admin_resolve_refund` üzerinden kapatılır.
- **Alıcı sayım rotası zayıf kalır.** `DeliveredBuyerAssetId` NULL olan (alıcı onayıyla kapanmış, envanteri okunmamış) teslimatlarda kontrol sınıf sayımına düşer; alıcı aynı skinden başka kopya edinirse geri alma maskelenebilir. Tam test için asset ID gerekir, o da yalnız envanter kanıtı üretilebilen teslimatlarda vardır (06 §8.4 best-effort).
- **Bildirim tipi yeniden kullanıldı.** Geri alma bildirimi `TRANSACTION_CANCELLED` şablonuyla (taraf-özel `Reason` metniyle) gider; yeni `NotificationType` + 4 dil resx maliyeti yerine mevcut zarf tercih edildi. Admin tarafı `ADMIN_ESCALATION` + fraud flag'in kendi `ADMIN_FLAG_ALERT`'i ile iki kanaldan haberdar olur.
- **Eskale satırlar için admin aksiyon yüzeyi yeni değil.** İşlem admin işlem listesinde/detayında görünür ve karar `admin_resolve_refund` ile verilir; T129 ayrı bir "mutabakat incelemesi" ekranı eklemez. Eskalasyonun kendisi bildirim + `SettlementEscalatedAt` kolonu üzerinden izlenir (DEPLOY_RUNBOOK §I.3 sorgusu).

## Notlar

- **Working tree:** temiz (Adım -1 ✓).
- **Adım 0 — main CI son 3 run:** `31944080697` success · `31944080720` success · `31909528316` success.
- **Dış varsayımlar (Adım 4):**
  - *Steam trade geri alma penceresi 7 gün ve item bu süre boyunca kısıtlı* — T122 runbook §6.1 ile doğrulandı (`market_tradable_restriction: 7`, sınıf politikası). **Sonucu scope'u değiştirdi:** 8 > 7 farkı K1'in gerekçesi oldu.
  - *Geri alma item'ı satıcıya döndürür* — 02 §4.5.1'in kendi tanımı; asset ID'nin korunup korunmadığı ölçülmedi (yukarıdaki known limitation).
  - *Yeni paket/servis gerekmiyor* — evet, tüm bağımlılıklar mevcut (`ISteamInventoryReader` T121, refund boru hattı WP2, fraud staging T54).
- **Enum değişikliği:** `FraudFlagType.DELIVERY_REVERSED` eklendi → memory kuralı gereği push öncesi tam `Category=Unit` suite koşuldu.

## Commit & PR

- Branch: `task/T129-settlement-window-reversal-guard`
- Commit: `2813daf` — T129: mutabakat süresi + trade geri alma koruması
- PR: [#240](https://github.com/turkerurganci/Skinora/pull/240)
- CI: run `31959182411` (izleniyor — sonuç bu satıra işlenecek)
- Dal izolasyonu: `git log main..HEAD` → yalnız `T129` ✓
