# T133a — 03 + 04 + 07 custodial kalıntı turu (doküman hizalaması)

**Faz:** F7 | **Durum:** ✓ Tamamlandı (doğrulama ✓ PASS, 2026-08-20) | **Tarih:** 2026-08-19

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
| 9 | Kaynak katmanındaki emekli-status XML doc kalıntıları temizlendi (~14 dosya); adı konan iki bulgu kapatıldı | ✓ | **20 dosya** (tahminden fazla). `grep -rn "ITEM_ESCROWED\|TRADE_OFFER_SENT" backend/src` → **3 isabet, üçü de belgeleyici** (`NotificationType.cs:9` ve `:16` emekli tipin yerini alan v3.0 tipini anlatıyor — "Replaces ITEM_ESCROWED / TRADE_OFFER_SENT_TO_BUYER"; `20260809162642_T117_P2P_Pivot.cs:31` emekli değerlerin **neden remap edilmediğini** anlatan migration doc'u). Kalıntı yok; üçü de emekliliği BELGELEYEN satır — kriterin 03/04/07 için kullandığı ölçütün kaynak katmanındaki karşılığı. **(Doğrulama düzeltmesi: yapım turu bu satırı "0 isabet" diye yazmıştı; gerçek 3'tür ve kriter yine karşılanır — T132'nin B2 dersi, "silmenin gerekçesi olarak yazılan iddia da ölçülmelidir".)** Adı konan iki bulgu yukarıda ayrıntılı; `DisputeEligibility.cs:19-43` okunarak matrisin doğru olduğu teyit edildi (kriterin "WRONG_ITEM maddesi eksik" tarifi T130'da zaten kapanmıştı) |
| 10 | 07 + 03 sürüm notları yazıldı | ✓ (sapma kayıtlı) | Kriterin "v3.1 / v3.2" numaraları T118 dönemine ait ve **bayat** — harfiyen uygulansa sürüm geri giderdi. Beş doküman bump'landı ve sapma plan §P6 T133a → **D3**'e yazıldı (T122'nin kalıcı dersi) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 Warning / 0 Error | `dotnet build -warnaserror`, exit 0 — taban ölçümüyle (tur öncesi) birebir aynı |
| Unit + non-integration | ✓ 1417 passed / 16 Docker-bağımlı | `dotnet test --no-build --filter "FullyQualifiedName!~Integration"` — 11 assembly. **16 test Docker'sız koşamadı** (`DockerUnavailableException`, `Unit.Channels` Testcontainers kullanıyor); Docker dışı **hiçbir** hata yok. Lokalde Docker daemon kapalı — bu legler CI'da koşar. *(Doğrulama notu: "1417/1417" gösterimi düzeltildi — 16 düşen dahil toplam **1433**'tür.)* |
| Integration | ✓ CI | `4. Integration test` job'ı **success** (run `32295024930`). Yeniden adlandırılan sınıfın testleri (`HappyPathNotificationConsumerTests`, 7 satır) bu leg'de koştu |
| Doküman parity (programatik) | ✓ 6/6 | `scratchpad/parity.js`: yetki 12/12/12 (sıra birebir), bildirim 26/26 (07 ≡ 06 sıra dahil), AuditAction 29/29 (sıra birebir) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Bulgu sayısı | **0 bloke edici** + 3 bloke etmeyen (ikisi bu turda düzeltildi, biri gözlem) |
| Düzeltme gerekli mi | Hayır |

**Tarih:** 2026-08-20 · **Dal HEAD:** `90d5637` · **Bağımsız chat** (yapım raporu yalnız Faz 3'te okundu)

### Kapı kontrolleri

| Kapı | Sonuç |
|---|---|
| Adım -1 Working tree | ✓ `git status --short` boş |
| Adım 0 Main CI | ✓ son 3 run `success` — `32267172619`, `32267172521` (T133 #248), `32248307699` (T132 #247) |
| Adım 0b Repo memory | ✓ `.claude/memory/MEMORY.md:58` T133a satırı mevcut |
| Adım 8a Task branch CI | ✓ run [`32297236830`](https://github.com/turkerurganci/Skinora/actions/runs/32297236830), dal HEAD `90d5637`, `conclusion=success`; bloke edici **9/9 yeşil** (`CI Gate` dahil), `0. Guard` + `3b. vitest` skipped |

### Bağımsız olarak yeniden üretilen kanıtlar

**On kabul kriterinin onu da bağımsız kanıtla karşılandı.** Validator kendi ölçüm script'lerini yazdı (yapım turunun `scratchpad/parity.js`'i kullanılmadı):

- **Kriter 1–2 (custodial dil):** `ITEM_ESCROWED|TRADE_OFFER_SENT_TO` → 03/04/07'de **5 isabet, beşi de belgeleyici**; `çift iade` + `Item'ınız iade` gövdede **0**. Ek olarak kriterin adlandırmadığı iki geniş tarama yapıldı: **`emanet`** → kalan isabetlerin hepsi ya **para** emaneti (02 §20.1 ile hizalı) ya kaldırma notu; **`bot`** → hepsi Telegram bot'u veya kaldırma notu. §8.6 Timeout Süreleri tablosu main ile **birebir aynı** (`diff` boş) — kriterin "tur o satırları tekrar açmamalı" şartı karşılandı.
- **Kriter 3 (yetki parity):** dört nüsha da **12/12, sıra birebir**, ve **etiket metinleri de** kod kataloğuyla birebir (kod `PermissionCatalog.All` ↔ 07 §9 tablosu ↔ 07 §9.11 JSON ↔ 04 §8.8 `Anahtar` kolonu). İki "Bilinen açık" notu → grep **0**.
- **Kriter 4 (AuditAction):** kod enum **29** ↔ 06 §2.19 **29**, küme + **sıra birebir**. Ayrıca kriterin istemediği bir kontrol daha koşuldu: **`Grup` kolonu ↔ `AuditLogCategoryMap` 29/29 birebir**, ve haritada olup tabloda olmayan değer **yok**. `EnumTests` "NOT full parity" bloğu → grep **0**.
- **Kriter 5 (bildirim kataloğu):** 06 §2.13 ↔ 07 §8.1 **26/26, ad + hedef + sıra birebir** (satır satır karşılaştırıldı, 0 fark). 07 §8.1'in `targetType` kolonu `NotificationTargetMapper` ile teyit edildi (`ADMIN_PLATFORM_OUTAGE`→null, `ADMIN_FLAG_ALERT`→flag, `ACCOUNT_SUSPENDED`→null çünkü `AccountSuspendedNotificationConsumer:31-41` `TransactionId` yazmıyor).
- **Kriter 6–7 (§7.5 / §7.1):** kod okunarak doğrulandı — `TransactionDetailService:229-231` (`PAYMENT_RECEIVED && role=="seller"` → `BuyerTradeUrl`), `BuildRefundAsync:666-696` (alıcı + `BUYER_REFUND` kaydı), `cancelInfo` (`:261` `CancelledAt`+`CancelledBy`), `TransactionListService:29-46` (`_activeStatuses` 6 · `_cancelledStatuses` 5 + `REFUNDED`). `TransactionStatus` enum'u **12** değer → üç sekmenin birleşimi tam, kesişim boş. `escrowBotAssetId` → grep **0**.
- **Kriter 8 (§9.20/§9.22):** item-iadesi bacakları kaldırılmış; `CANNOT_CANCEL_AT_DELIVERY_STAGE` kodda mevcut (`AdminTransactionErrorCodes.cs`) — doküman var olmayan bir hata kodu vaat etmiyor.
- **Kriter 9 (kaynak katmanı):** `DisputeService` XML doc matrisi `DisputeEligibility.AllowedStatesByType` ile **birebir** ve tek doğru kaynağı adıyla gösteriyor. Yeniden adlandırma **saf**: eski dosya ile yeni dosya yorumlar çıkarılıp sınıf adı normalize edilerek `diff`'lendi → **gövde birebir aynı**; `ConsumerName` sabiti main'deki değerle **aynı** (`"notifications.transaction-status-changed"`), DI kaydı sınıf adına bağlı değil (`INotificationHandler<TEvent>` üzerinden). Kriterin AC9 kanıt satırındaki "0 isabet" iddiası **düzeltildi** (bkz. yukarıdaki tablo).
- **Kriter 10 (sürüm):** beş dokümanın **başlığı ile altbilgisi** programatik olarak karşılaştırıldı → 5/5 hizalı.
- **Davranış nötrlüğü (turun ana iddiası):** `git diff origin/main...HEAD -- backend/src/**/*.cs` üzerinden yorum/XML-doc satırları elendiğinde **geriye sıfır satır kalıyor** — yeniden adlandırma dışında hiçbir çalıştırılabilir satır değişmemiş. Migration/config/bağımlılık dosyası diff'te **yok**.
- **Plan bütünlüğü:** `11_IMPLEMENTATION_PLAN.md` diff'i **saf ekleme** (95+ / 1−, tek silinen satır sürüm başlığı) → orijinal on kabul kriteri **korunmuş**, KAPSAM NETLEŞTİRMESİ üstüne eklenmiş; sessiz kriter yeniden yazımı yok.
- **T134/T136'ya devredilen ölçümler denetlendi (yanlış ölçüm sonraki görevin kriterini zehirler):** FE `enums.ts` `NotificationType` gerçekten **28 değer** (4 emekli duruyor, 2 v3.0 tipi eksik) · FE `permissionCatalog.ts` gerçekten **14 anahtar** (`VIEW_STEAM_ACCOUNTS` + `MANAGE_STEAM_RECOVERY` duruyor) · dört i18n dosyasının `EMERGENCY_HOLD` etiketinin dördü de kod kataloğundan sapıyor. Üçü de doğru.
- **`T133a-ActiveCounterRefunded` gerçek:** `ActiveTransactionCounter:48-54` ve `UserActiveTransactionChecker:30-36` dışlama listelerinde `REFUNDED` **yok** → terminal bir işlem "aktif" sayılıyor. İki XML doc bu açığı **adıyla** yazıyor; tur davranışa dokunmadı — doğru karar.

### Test sonuçları (validator'ın kendi koşumu)

| Tür | Sonuç | Komut |
|---|---|---|
| Build | ✓ **0 Warning / 0 Error** | `dotnet build Skinora.sln -c Release` |
| Unit (CI filtresiyle) | ✓ **1408/1424** | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration | ✓ CI | `4. Integration test` job `success` (yeniden adlandırılan sınıfın testleri `tests/Skinora.Notifications.Tests/Integration/`'da, bu leg'de koştu) |
| Advisory E2E | 10 passed / 32 — **tabanla birebir** | Run `32297236830` leg log'ları bağımsız sayıldı: 1 + 3 + 6 = **10** (timeout · fraud-flags · admin-flows), T133/T137a tabanının aynısı |

**16 düşen test T133a regresyonu DEĞİL:** hepsi `Notifications.Tests.Unit.Channels` (Discord 10 + Telegram 6) ve hepsinin hatası `Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'` — `docker info` ile bu makinede daemon'ın **kapalı** olduğu teyit edildi. Toplam **1424**, T132/T133 raporlarının kaydettiği taban ile birebir; CI'da aynı leg **success**.

### Güvenlik kontrolü

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | ✓ Temiz — diff'te sır deseni yok (tek grep isabeti `IMPLEMENTATION_STATUS.md` changelog metni) |
| Auth/authorization | ✓ Temiz — `PermissionCatalog` **kodu değişmedi**; tur dokümanı koda hizaladı, ters yöne değil |
| Input validation | ✓ Temiz — çalıştırılabilir satır değişikliği yok |
| Yeni bağımlılık | ✓ Yok — `.csproj` / `package.json` diff'te yok |

### Bloke etmeyen bulgular

| # | Seviye | Açıklama | Durum |
|---|---|---|---|
| N1 | Kanıt doğruluğu | AC9'un kanıt satırı `grep … backend/src` → "**0 isabet**" diyordu; gerçek **3** (üçü de belgeleyici, kriter yine karşılanıyor). Aynı iddia `IMPLEMENTATION_STATUS.md` satır 3'e de yansımıştı | **Bu turda düzeltildi** (rapor + status) |
| N2 | Gösterim | Test satırı "1417/1417" diyordu ama aynı satır 16 düşen testi de sayıyordu — toplam **1433** | **Bu turda düzeltildi** |
| N3 | Gözlem (değişiklik yok) | Plan §P7 T134 kriterindeki "`EMERGENCY_HOLD` etiketi **dört dilde de** 'Emergency hold uygula/kaldır' diyor" ifadesi harfiyen yalnız `tr.json` için doğru; `en/es/zh` aynı **eski** ifadenin çevirisini taşıyor. Kriterin **özü** doğru ve ölçülebilir ("dört dilin etiketi kod kataloğuyla hizalı") — dördü de sapıyor, T134 aksiyonu değişmiyor | Kayda geçti, düzeltme önerilmedi |

### Yapım raporu karşılaştırması

**Uyum: yüksek — 10/10 kriterde verdict aynı, iki kanıt uyuşmazlığı (N1, N2).** Rapor, kriterin tarifiyle gerçeğin ayrıştığı yerleri (AC9'da `DisputeService` matrisinin T130'da zaten düzelmiş olması; AC10'da bayat sürüm numaraları) kendi kendine bildirmiş ve `NotificationType` sıra gözlemini de bulgu saymadan kaydetmiş — validator bağımsız olarak aynı sonuçlara ulaştı. Raporun **abartmadığı** ve kendi aleyhine kaydettiği kalemler (20 dosya > tahmini 14; D4'ün "yalnız XML doc" beklentisini aştığının açıkça yazılması) doğrulandı. Tek gerçek uyuşmazlık N1'dir: turun kendisi T132'nin "yazılan iddia ölçülmelidir" dersini kaynak dokümana taşırken, kendi kanıt satırında aynı sınıftan bir ölçülmemiş iddia bırakmış.

## Altyapı Değişiklikleri

- **Migration:** Yok
- **Config/env değişikliği:** Yok
- **Docker değişikliği:** Yok
- **API sözleşmesi:** Değişmedi — 07'deki tüm düzeltmeler dokümanı kodun **fiilî** davranışına hizaladı. Silinen alanlar (`escrowBotAssetId`, `itemReturned`) DTO'larda zaten yoktu; eklenen alanlar (`canConfirmReady`, `canConfirmReceipt`, `disputableTypes`) kod zaten döndürüyordu

## Commit & PR

- Branch: `task/T133a-doc-custodial-alignment`
- Commit: `c937d00` — T133a: 03 + 04 + 07 custodial kalıntı turu (doküman hizalaması)
- PR: [#249](https://github.com/turkerurganci/Skinora/pull/249)
- CI: ✓ **PASS** — run [`32295024930`](https://github.com/turkerurganci/Skinora/actions/runs/32295024930), **CI Gate `success`**, bloke edici **9/9 job yeşil** (1. Lint · 2. Build · 3. Unit test · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build · Detect changed paths · CI Gate). `0. Guard (direct push)` ve `3b. JS test (vitest)` **skipped** (bu turda FE değişikliği yok).

### Advisory E2E — T133a kaynaklı DEĞİL (üç kanıt)

8 advisory leg T117'den beri kırmızı. Ölçüm **T133'ün ve T137a'nın tabanıyla birebir**:

1. **Sayım aynı:** **10 passed / 22 failed = 32** — T137a'nın main run'ı `32050987594` ve T133'ün ölçümüyle **birebir**.
2. **T133a yüzeylerinden sıfır iz** (1016 satırlık `--log-failed` üzerinde): `HappyPathMilestone` 0 · `EscrowedAndTradeOffer` 0 · `PermissionCatalog` 0 · `AuditAction` 0 · `VIEW_DISPUTES` 0 · `MANAGE_SANCTIONS` 0. Yeniden adlandırılan sınıf da, eklenen katalog satırları da log'da hiç geçmiyor.
3. **Mekanizma değişmedi:** `PlatformSteamBots` **0** (T137a'nın onardığı katman geçiliyor) · `Invalid object name` / `Invalid column name` **0** · 18 poll timeout'unun **18'i de** `(last status=ACCEPTED)` — yani create ve accept geçiyor, işlem T117'de emekli edilen custody durumunda takılıyor · `ITEM_NOT_IN_INVENTORY` **2** (yalnız downtime leg'inde, T137 düzeltme turunun ölçtüğü dağılımın aynısı).

Kalan 22 testin sahibi **T138** (E2E spec'lerinin yeniden yazımı).

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
