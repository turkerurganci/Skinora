# T133a — 03 + 04 + 07 custodial kalıntı turu (doküman hizalaması)

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-19

---

## Yapılan İşler

Bu tur **davranış değiştirmez** — dokümanı KODA hizalar. Kod bu bölgelerde T115–T133 arasında zaten P2P'ye taşınmıştı; geride kalan custodial dil, emekli statü adları ve kod ile doküman arasındaki sayım farkları kapatıldı.

**Keşif yöntemi:** dokuz paralel ajanla satır bazlı envanter + bağımsız tamlık eleştirisi → **193 bulgu**, her biri kod satırı veya doküman satırı kanıtıyla. Envanter `scratchpad/T133a_INVENTORY.md`'de tutuldu; bulgular uygulanmadan önce çelişkileri (aynı satıra iki farklı öneri) ve kapsam sızıntıları ayrıştırıldı.

### Doküman hizalaması

| Doküman | Sürüm | Başlıca değişiklik |
|---|---|---|
| `02_PRODUCT_REQUIREMENTS.md` | v3.7 → **v3.8** | §18.2 tetikleyici tablosu (bildirim kataloğunun **dördüncü** nüshası) — kapsam genişletmesi, D2 |
| `03_USER_FLOWS.md` | v3.6 → **v3.7** | 12 custodial kalıntı + 4 hijyen kalemi |
| `04_UI_SPECS.md` | v4.4 → **v4.5** | 24 kalıntı + §8.7 S18 gövdesi silindi + §8.8 yetki matrisi 10 → 12 satır (`Anahtar` kolonu eklendi) |
| `06_DATA_MODEL.md` | v6.11 → **v6.12** | §2.19 `AuditAction` tablosu 17 → **29 satır** + parity notu |
| `07_API_DESIGN.md` | v3.7 → **v3.8** | 37 nokta düzeltmesi + §8.1 kataloğu 20 → **26 tip** + §9.28/§9.29 gövdeleri silindi |

Beş dosyanın **altbilgisi** de başlıkla hizalandı (donmuş değerler: 02 "v3.1", 03 "v2.2", 04 "v3.0", 06 "v4.9", 07 "v2.2").

### Kaynak katmanı (yalnız XML doc / yorum + bir davranış-nötr yeniden adlandırma)

20 dosyada emekli statü adları (`ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_SELLER/BUYER`), emekli kavramlar (bot durumu push'u, trade offer üretimi, "iki bacaklı custodial trade") ve bayat sayımlar düzeltildi. Kabul kriterinde **adı konan iki bulgu**:

1. **`DisputeService` per-type dispute matrisi** — durum kriterin tarif ettiğinden farklı çıktı: matrisin üç satırı da T130'da (`523dc97`) düzeltilmişti ve `WRONG_ITEM@PAYMENT_RECEIVED` maddesi **yerindeydi**. Kalan kusur üçtü: (a) emekli iki ad hâlâ anılıyordu, (b) "named three retired ones" diyip parantezde **iki** ad sayıyordu, (c) "enforced here" ifadesi matrisin sahibini yanlış gösteriyordu — matris `DisputeEligibility.AllowedStatesByType`'ta. Doc artık tek doğru kaynağı adıyla gösteriyor ve listeyi "yalnız yönlendirme için" kopyalıyor.
2. **`EscrowedAndTradeOfferNotificationConsumer` özeti** — "buyer-facing" iddiası **iki ayrı yerde** geçiyordu (özet satır 15 + remarks satır 30) ve ikisi de kendi listesiyle çelişiyordu: `PAYMENT_RECEIVED` bacağı koddaki satır 89'da `data.SellerId`'ye gidiyor. Ayrıca "two Steam orchestration legs" custodial dönem terimiydi.

**Sınıf yeniden adlandırıldı (D4, proje sahibi kararı):** `EscrowedAndTradeOfferNotificationConsumer` → **`HappyPathMilestoneNotificationConsumer`** (dosya adı dahil). Adın her iki yarısı da emekli kavramdı; sınıf bugün `SELLER_CONFIRMED` ve `PAYMENT_RECEIVED` bacaklarını işliyor. **Davranış nötr:** idempotency anahtarı `ConsumerName` sabiti (`"notifications.transaction-status-changed"`) değişmedi ve DI kaydı sınıf adına bağlı değil (grep: kaynak dışında yalnız 7 test satırı).

## Etkilenen Modüller / Dosyalar

**Doküman (7):** `Docs/02_PRODUCT_REQUIREMENTS.md` · `03_USER_FLOWS.md` · `04_UI_SPECS.md` · `06_DATA_MODEL.md` · `07_API_DESIGN.md` · `11_IMPLEMENTATION_PLAN.md` · `DEFERRED_BACKLOG.md`

**Kaynak (20 + 1 test):**
`Skinora.Shared/Events/`: `TimeoutWarningEvent.cs` · `LatePaymentMonitorRequestedEvent.cs` · `BuyerPaymentInsufficientEvent.cs` · `BuyerPaymentExcessRefundedEvent.cs` · `PaymentRefundToBuyerRequestedEvent.cs`
`Skinora.Shared/`: `Persistence/Outbox/ExternalIdempotencyRecord.cs`
`Skinora.API/`: `BackgroundJobs/Timeouts/IRestartRecoveryService.cs`
`Skinora.Users/`: `Application/Account/IUserActiveTransactionChecker.cs` · `Application/Wallet/IActiveTransactionCounter.cs`
`Skinora.Notifications/`: `EventHandlers/TimeoutWarningNotificationConsumer.cs` · `EventHandlers/HappyPathMilestoneNotificationConsumer.cs` **(yeniden adlandırıldı)**
`Skinora.Realtime/`: `Infrastructure/SignalRNotificationRealtimePublisher.cs` · `Hubs/NotificationsHub.cs`
`Skinora.Transactions/`: `Application/Timeouts/ITimeoutFreezeService.cs` · `Application/Webhooks/IAmountValidationService.cs` · `Application/PostCancel/PostCancelMonitorStarter.cs` · `Application/Lifecycle/TransactionDetailDto.cs` · `Application/Lifecycle/TransactionDetailService.cs` · `Application/Transfers/PaymentRefundToBuyerConsumer.cs`
`Skinora.Disputes/`: `Application/Disputes/DisputeService.cs`
`backend/tests/`: `Skinora.Shared.Tests/Unit/EnumTests.cs` (parity yorumu) · `Skinora.Notifications.Tests/Integration/HappyPathNotificationConsumerTests.cs` (yeniden adlandırma)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 03, 04 ve 07'de item-custody dili kalmadı; emekli status adları yalnız "v3.0'da kaldırıldı" biçiminde, emekliliği BELGELEYEN satırlarda geçiyor | ✓ | `grep -n "ITEM_ESCROWED\|TRADE_OFFER_SENT" Docs/03_USER_FLOWS.md Docs/04_UI_SPECS.md Docs/07_API_DESIGN.md` → **5 isabet, beşi de belgeleyici**: 03:39 ve 04:334 v3.0 durum notları; 07:1664/1666 katalog satırlarındaki "(v3.0: eski `X`)" atfı; 07:1691 parity çapası notu |
| 2 | 04'te akış eşleme tablosunda emekli adlar, "çift iade", üç "Item'ınız iade edildi", üç "CREATED → TRADE_OFFER_SENT_TO_BUYER", bot recovery / emanet ekranları kaldırıldı; §16 Timeout Süreleri tablosunun T119'da düzeltilen iki satırı **tekrar açılmadı** | ✓ | `grep -n "çift iade\|Item'ınız iade" Docs/04_UI_SPECS.md` → **0 isabet**. Üç `CREATED → TRADE_OFFER_SENT_TO_BUYER` (satır 1574 §8.5, 1750 §8.7, 1789 §8.8) kapatıldı: ikisi `CREATED → PAYMENT_RECEIVED`'a çekildi, üçüncüsü §8.7 gövdesiyle birlikte silindi. §8.6 Timeout Süreleri tablosuna **dokunulmadı** (T119/T123 notu satır 1602 yerinde) |
| 3 | Yetki kataloğunun üç doküman nüshası kod `PermissionCatalog` ile birebir (12 giriş); T132'nin iki "Bilinen açık" notu silindi | ✓ | Programatik: `scratchpad/parity.js` → 07 §9.11 **12/12 sıra birebir** · 07 §9 tablosu **12/12 sıra birebir** (`MANAGE_SANCTIONS` eklendi) · 04 §8.8 **12/12 sıra birebir** (`VIEW_DISPUTES` + `MANAGE_DISPUTES` eklendi). Fark kümesi **boş**. İki "Bilinen açık" notu (07:1746-1747, 04:1794-1795) silindi; ayırıcı olmayan v3.0 notları korundu |
| 4 | 06 §2.19 `AuditAction` tablosu kod enum'uyla birebir (29 değer); `EnumTests` yorumundaki "NOT full parity" bloğu silindi | ✓ | `scratchpad/chk_audit.js` → doc **29** / enum **29**, `enum\doc` = `[]`, `doc\enum` = `[]`, **sıra birebir: true**. `Grup` kolonu `AuditLogCategoryMap` ile satır satır doğrulandı (tek asimetri `COLD_WALLET_TRANSFER_INITIATED` = Fon, map satır 72). `EnumTests.cs:399-408` "NOT full parity" bloğu "Full parity … since T133a" ile değiştirildi |
| 5 | 07 §8.1 bildirim tipi kataloğu 06 §2.13 ile birebir (26 tip) | ✓ | `scratchpad/parity.js` → 07 §8.1 **26 giriş**, 06 §2.13 **26 giriş**, ikisi de kod enum'uyla küme olarak birebir; **07 §8.1 ≡ 06 §2.13 sıra dahil `true`**. `targetType` değerleri `NotificationTargetMapper` okunarak verildi (`ADMIN_FLAG_ALERT`→flag, `ADMIN_PLATFORM_OUTAGE`→null, `ACCOUNT_*`→null çünkü işlem referansı taşımıyorlar — `AccountSuspendedNotificationConsumer:31-38`) |
| 6 | 07 §7.5 detay blok koşulları güncel durumlara göre; `steamTradeOfferUrl` satırı kodun FİİLEN ürettiği davranışı anlatıyor | ✓ | `TransactionDetailService.cs:227-234` okundu: alan yalnız `Status == PAYMENT_RECEIVED && role == "seller"` iken doluyor ve değer **alıcının kendi** `BuyerTradeUrl`'i. §7.5 satırı bu hâle yazıldı; ayrıca `payment`/`paymentEvents` eşiği `SELLER_CONFIRMED`, `deliveredBuyerAssetId` `ITEM_DELIVERED`, `refund`/`cancelInfo` damga tabanlı (+`REFUNDED`), `holdInfo` `IsOnHold` bayrağı, `timeout.type` beş değeri (`settlement` dahil) yazıldı. `escrowBotAssetId` (DTO'da yok) tablo satırı + örnek + not atfı silindi. DTO yorumu (`TransactionDetailDto.cs:44-47`) da düzeltildi |
| 7 | 07 §7.1 `active` sekmesi "terminal olmayan" tanımına çekildi; EMERGENCY_HOLD status olarak listelenmiyor | ✓ | `TransactionListService.cs:29-37` (`_activeStatuses` = 6 değer) ve `:39-47` (`_cancelledStatuses` = 5, `REFUNDED` dahil) okundu. §7.1 `active` = terminal olmayan 6 statü, `cancelled`'a `REFUNDED` eklendi; EMERGENCY_HOLD listeden çıkarıldı ve projeksiyon notuna "**sekme filtresinde yer almaz**" cümlesi + terminal statü listesi eklendi (`ProjectStatus`, `:178-184`). Üç sekmenin birleşimi 12 statünün tamamı |
| 8 | 07 §9.20/§9.22 iade kuralları tablosundan item-iadesi bacakları kaldırıldı | ✓ | §9.20: "İade kuralları" yalnız para; iptal edilebilir state listesi `TransactionStateMachine.cs:221/232/253/268/309`'a hizalandı (`SELLER_CONFIRMED` eklendi, üç emekli ad düştü); "İptal edilemez"e `REFUNDED` eklendi ve hold'un statü olmadığı yazıldı. §9.22 CANCEL tablosu 6 → 4 satır, çift-iade satırı ve iki emekli-statü satırı silindi. `itemReturned` **üç** örnek yanıttan (§7.5, §9.20, §9.22) kaldırıldı — `AdminTransactionDtos.cs:9-14` "dropped in v3.0" diyor |
| 9 | Kaynak katmanındaki emekli-status XML doc kalıntıları temizlendi (~14 dosya); adı konan iki bulgu kapatıldı | ✓ | **20 dosya** (tahminden fazla). `grep -rn "ITEM_ESCROWED\|TRADE_OFFER_SENT" backend/src` → **0 isabet**. Adı konan iki bulgu yukarıda ayrıntılı; `DisputeEligibility.cs:19-43` okunarak matrisin doğru olduğu teyit edildi (kriterin "WRONG_ITEM maddesi eksik" tarifi T130'da zaten kapanmıştı) |
| 10 | 07 + 03 sürüm notları yazıldı | ✓ (sapma kayıtlı) | Kriterin "v3.1 / v3.2" numaraları T118 dönemine ait ve **bayat** — harfiyen uygulansa sürüm geri giderdi. Beş doküman bump'landı ve sapma plan §P6 T133a → **D3**'e yazıldı (T122'nin kalıcı dersi) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 Warning / 0 Error | `dotnet build -warnaserror`, exit 0 — taban ölçümüyle (tur öncesi) birebir aynı |
| Unit + non-integration | ✓ 1417/1417 | `dotnet test --no-build --filter "FullyQualifiedName!~Integration"` — 11 assembly. Ayrıca **16 test Docker'sız koşamadı** (`DockerUnavailableException`, `Unit.Channels` Testcontainers kullanıyor); Docker dışı **hiçbir** hata yok (`grep` ile doğrulandı: 16 Failed / 32 DockerUnavailableException satırı, başka hata mesajı türü yok). Lokalde Docker daemon kapalı — bu legler CI'da koşar |
| Integration | ⏳ CI | Lokalde Testcontainers kullanılamadığı için CI'ya bırakıldı. Yeniden adlandırılan sınıfın testleri (`HappyPathNotificationConsumerTests`, 7 satır) Integration namespace'inde — CI kanıtı zorunlu |
| Doküman parity (programatik) | ✓ 6/6 | `scratchpad/parity.js`: yetki 12/12/12 (sıra birebir), bildirim 26/26 (07 ≡ 06 sıra dahil), AuditAction 29/29 (sıra birebir) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok
- **Config/env değişikliği:** Yok
- **Docker değişikliği:** Yok
- **API sözleşmesi:** Değişmedi — 07'deki tüm düzeltmeler dokümanı kodun **fiilî** davranışına hizaladı. Silinen alanlar (`escrowBotAssetId`, `itemReturned`) DTO'larda zaten yoktu; eklenen alanlar (`canConfirmReady`, `canConfirmReceipt`, `disputableTypes`) kod zaten döndürüyordu

## Commit & PR

- Branch: `task/T133a-doc-custodial-alignment`
- Commit: (aşağıda)
- PR: (aşağıda)
- CI: (aşağıda)

## Known Limitations / Follow-up

`Docs/DEFERRED_BACKLOG.md` §9'a **altı satır** açıldı (turda bilinçli olarak değiştirilmedi):

| ID | Neden turda kapatılmadı |
|---|---|
| `T133a-PaymentDetailNulls` 🟡 | 07 §7.5 `payment.status`/`txHash`/`confirmedAt` sözleşmede vaat ediliyor, `TransactionDetailService.cs:602-609` üçünü de sabit `null` döndürüyor. **Sözleşme kasıtlı olarak korundu** — bugünkü eksik davranışa çekmek hatayı kalıcılaştırırdı; eksik olan doküman değil kod |
| `T133a-DisputeBlockNulls` 🟡 | Aynı sınıf: `dispute` bloğu sözleşmede tanımlı, `TransactionDetailService.cs:322` `Dispute: null` döndürüyor ve `DisputeSummaryDto`'nun repoda hiç üreticisi yok (T58 açığı) |
| `T133a-ActiveCounterRefunded` ⚪ | **Davranış açığı:** `ActiveTransactionCounter` ve `UserActiveTransactionChecker` "aktif"i dışlama ile tanımlıyor ve `REFUNDED` dışlama listesinde yok → alıcı lehine karara bağlanmış işlemi olan kullanıcı cüzdan adresi değiştiremiyor (02 §12.3) ve hesap kapatamıyor (02 §19). Bu tur davranışa dokunmuyor; XML doc'lar **gerçeği** yazacak şekilde düzeltildi |
| `T133a-FeI18nEmergencyHoldLabel` ⚪ | 04 §8.8 koda hizalandığı için FE i18n'in dört dildeki `EMERGENCY_HOLD` etiketinin doküman dayanağı kalmadı → **T134 kabul kriterine yazıldı** |
| `T133a-FePermissionCatalogKeys` ⚪ | FE `permissionCatalog.ts` **14 anahtar** taşıyor (kod 12); T132'de silinen iki anahtar duruyor → **T136 kabul kriterine yazıldı** |
| `T133a-Doc02NotificationCopy` 🟡 | FE `enums.ts` `NotificationType` **28 değer** (kod 26): iki v3.0 tipi eksik, dört emekli tip duruyor → **T134 kabul kriterine yazıldı**. Katalog beş nüshalı ve parity'yi zorlayan bekçi testi yok |

**Gözlem (bulgu değil):** 06 §2.13'ün satır sırası kod enum'unun sırasından bir noktada ayrılıyor (`ADMIN_PLATFORM_OUTAGE` doküman içinde `EMERGENCY_HOLD_APPLIED`'dan önce). Bu T118'de yerleşmiş bir tercih; kriter 07 §8.1'in **06 §2.13 ile** birebir olmasını istiyor ve o sağlanıyor. Küme farkı üç nüshada da boş.

## Notlar

- **Working tree:** temiz (`git status --short` boş çıktı)
- **Main CI startup check:** son 5 run `success` — `32267172619`, `32267172521` (T133 #248), `32248307699`, `32248307712` (T132 #247), `32180658440` (T137 #246)
- **Dış varsayımlar:** yok — bu tur yalnız repo içi doküman ve XML doc değişikliği yapar; paket sürümü, plan tier, dış API veya ortam durumu varsayımı içermiyor
- **Karakter hijyeni:** düzenleme sırasında beş dokümana kıvrık kesme işareti (U+2019) girdiği fark edildi ve normalize edildi — beş dosyanın hepsi **yalnız düz `'`** kullanıyor (kontrol: `Docs/0{2,3,4,6,7}` → kıvrık 0)
- **04 §8.8 `EMERGENCY_HOLD` etiketi** proje sahibi kararıyla koda hizalandı ("Emergency hold uygula/kaldır" → "İşlemleri acil dondurma/kaldırma"); alternatif (04'ün metnini koruyup sapmayı not düşmek) reddedildi
- **Kriterin doğrulama yönteminin bakımı:** kriter "üç tablodaki key kümesini karşılaştır" diyordu ama 04 §8.8'de key yoktu — yani kriter WP5'ten beri o nüshada **ölçülemez** durumdaydı ve iki eksik satır tam olarak bu yüzden aylarca görünmedi. `Anahtar` kolonu eklenerek kriter makinece koşabilir hale getirildi
