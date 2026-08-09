# T117 — Domain Çekirdeği: Enum, Transaction Alanları, State Machine, Bot Emekliliği

**Faz:** F7 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-08-09

---

## Kapsam neden birleşti

Plan T117 (enum + alanlar), T118 (state machine) ve T132 (bot kodu silme) için ayrı görevler öngörüyordu. Ölçtüğümde enum'dan değer silmenin **136 dosyayı** birden kırdığı ortaya çıktı: `ITEM_ESCROWED` ve `TRADE_OFFER_SENT_TO_*` 91 dosyada, bot alanları 62 dosyada geçiyordu.

Bunlar aynı anda derlenmek zorunda. Ayrı ayrı merge edilseydi ana dal derlenmez hâlde kalırdı — proje kuralı bunu yasaklıyor. Proje sahibi onayıyla tek dalda birleştirildi; içeride commit'ler ayrı tutuldu.

## Yapılan İşler

| Commit | İçerik |
|---|---|
| `b664362` | Enum'lar, `Transaction` alanları, EF yapılandırması |
| `59134d2` | State machine geçiş tablosu ve guard'lar |
| `f8857fa` | Bot custody katmanı silindi (Steam modülü 35 → 11 dosya) |
| `2ac3374` | Dispute uygunluk matrisi + işlem detay servisi |
| `e65f8a4` | Timeout servisleri yeni fazlara taşındı |
| `f9a0eb7` | Kalan modüller + API katmanı — kaynak derlemesi temiz |
| `5f4b005` | Bot testleri silindi, enum parity testleri güncellendi |
| `2de88ed` | Devam notu (yarım kalan iş kaydı) |
| _(bu oturum)_ | Test katmanı P2P modeline taşındı, migration üretildi, kaynak katmanında 5 kusur kapatıldı |

## Bu oturumda tamamlanan

### 1. Test katmanı (41 dosya)

Devam notu "16 test dosyası" diyordu; gerçek sayı **41**. Fark ölçüm hatasından değil, derleyicinin davranışından: Roslyn attribute (`[InlineData]`) seviyesindeki hatalar varken metot gövdelerini hiç bind etmiyor, dolayısıyla ilk `dotnet build` çıktısı buzdağının görünen kısmıydı. Doğru envanter enum/alan adları üzerinden `grep` ile çıkarıldı.

Mekanik olmayan, **davranış değişikliği** taşıyan düzeltmeler (devam notunun uyardığı sınıf):

| Konu | Eski | Yeni |
|---|---|---|
| Teslimat timeout'u kime yazılıyor | Alıcı | **Satıcı** — trade'i gönderen odur (02 §3.1) |
| `DELIVERY_EXPECTED` bildirimi kime gidiyor | Alıcı ("offer'ı kabul et") | **Satıcı** ("item'ı gönder") |
| `PAYMENT_RECEIVED`'da iptal | İki tarafa da kapalı | **Asimetrik** — satıcı edebilir, alıcı edemez (07 §7.7) |
| `ITEM_DELIVERED → COMPLETED` | Serbest | **Mutabakat kontrolü** şart (02 §4.5.1) |
| Dispute auto-checker'ları | `TradeOffers` tablosu | `DeliveryEvidence` bayrakları (02 §9.2) |
| Timeout yan etkileri | Item iadesi + para iadesi | **Yalnız para** — item hiç platformda olmadı |

### 2. Migration — `20260809162642_T117_P2P_Pivot`

Scaffolder'ın ürettiği hâli **kullanılmadı**. EF, düşen ve eklenen kolonları anlama göre değil tipe göre eşleştirdi ve üç yanlış rename üretti:

```
TradeOfferToSellerDeadline → SettlementVerifiedAt     ✗
TradeOfferToBuyerDeadline  → SellerReadyConfirmedAt   ✗
ItemEscrowedAt             → SellerConfirmDeadline    ✗
```

Birincisi tehlikeli olanı: `SettlementVerifiedAt`, `COMPLETED` guard'ının "item hâlâ alıcıda mı" kanıtı olarak okuduğu kolon. Eski bir deadline oraya taşınsaydı **mevcut her satır mutabakatı doğrulanmış görünür** ve item hiç kontrol edilmeden ödenebilir hâle gelirdi (02 §4.5.1). Migration faz eşlemesine göre elle yazıldı:

```
TradeOfferToSellerDeadline → SellerConfirmDeadline    ✓ (aynı faz)
TradeOfferToBuyerDeadline  → DeliveryDeadline         ✓ (aynı faz)
ItemEscrowedAt             → DROP                     ✓ (karşılığı yok)
```

Mutabakat ve teslimat-kanıtı kolonlarının hepsi gerçekten yeni; NULL başlıyorlar.

**Emekli status değerleri remap edilmedi.** `ITEM_ESCROWED` / `TRADE_OFFER_SENT_TO_*` taşıyan bir satır, item'ı fiziksel olarak bir platform botunun envanterinde olan bir işlemi tarif eder; buna karşılık gelen ve aynı zamanda **doğru** olan bir P2P durumu yok. T117 kabul kriteri migration'ı temiz DB'ye tanımlıyor; boş olmayan bir ortam uygulamadan önce uçuştaki custodial işlemlerini operasyonel olarak kapatmalı. Gerekçe migration dosyasının `<remarks>`'ında da duruyor.

### 3. Kaynak katmanında kapatılan beş kusur

Bunlar test düzeltmesi sırasında ortaya çıktı; hiçbiri test kaynaklı değil.

| # | Kusur | Sonucu |
|---|---|---|
| 1 | `TransactionCancellationService` post-payment guard'ı **rolden bağımsız** uygulanıyordu | `ResolveTrigger`'daki `(SELLER, PAYMENT_RECEIVED)` dalı ölü koddu; 07 §7.7'nin "yalnız alıcı için 422" kuralı fiilen uygulanmıyordu |
| 2 | `DeliveryReversed` tetikleyicisi `REFUNDED`'a geçiyor ama `CancelledBy`/`CancelReason` damgalamıyordu | T129 bu tetikleyiciyi çalıştırdığında `CK_Transactions_Cancel` DB'de reddederdi |
| 3 | `CountdownSyncBroadcaster.ActiveStatuses`'tan `PAYMENT_RECEIVED` düşmüştü | `DeliveryDeadline` geri sayımı hiçbir istemciye yayınlanmazdı — alıcının teslimat beklerken gördüğü sayaç budur (04 §7) |
| 4 | `RestartRecoveryService.activeStates`'ten `ACCEPTED` + `PAYMENT_RECEIVED` düşmüştü | Kesinti sonrası bu iki fazın deadline'ı kesinti süresi kadar **eksik** kalırdı; satıcı onayı ve teslimat pencereleri haksız yere kısalırdı |
| 5 | `NotificationTargetMapper`'da bot case'i silinirken yorumu kalmıştı, `EscrowedAndTradeOfferNotificationConsumer` XML doc'u emekli durum adlarını anlatıyordu | Yanıltıcı belge |

(3) ve (4) aynı sınıftan: yeniden adlandırma sırasında **adı değişen** durumlar listelerde korunmuş, **karşılığı başka bir ada taşınan** durumlar düşmüş. İkisi de yalnız çalışma zamanında görünür — derleyici sessiz kalır.

(2) için atıf kararı: geri alma 02 §4.5.1'de satıcı-taraflı dolandırıcılık yolu olarak tanımlı ve T129 fraud flag'ini de satıcıya açıyor → `CancelledBy = SELLER`. Bu bir ürün kararıdır, doğrulamada gözden geçirilmelidir.

### 4. Sidecar contract drift'i — dar, T133 referanslı istisna

Backend'in bot webhook uçları bu dalda silindi (`SteamWebhooksController` yok), ama `sidecar-steam` hâlâ `/api/v1/webhooks/steam/{bot-events,trade-events}` yollarına POST ediyor. PR #213'ün drift guard'ı `SidecarWebhookRouteContractTests` bunu doğru biçimde yakaladı — guard'ın var oluş sebebi tam olarak bu.

Sidecar tarafındaki yayıncıların silinmesi **T133**'e ait (~4000 satır, `sidecar-steam` bot + trade-offer modülleri + `sidecar-fake` trade yolu + E2E süitlerinin custodial akıştan çıkarılması). Proje sahibi kararıyla T117 kapsamda tutuldu ve guard'a **açıkça listelenmiş, adı konmuş** bir istisna eklendi:

- `RetiredWithBotCustodyLayer` — yalnız bu iki yol; diğer her yayınlanan yol sıkı korunmaya devam ediyor.
- `CriticalSidecarRoute_IsServedByBackend` teorisinden iki emekli yol çıkarıldı (backend onları bilinçli olarak artık sunmuyor).
- Yeni `RetiredPathsAreStillPublished_UntilT133` testi istisnayı **iki yönden** dürüst tutuyor: bir yol artık hiçbir sidecar tarafından yayınlanmıyorsa (T133 bitmiş) veya backend onu tekrar sunuyorsa test kırılır. İstisna gerekçesinden uzun yaşayamaz.

Platform henüz deploy edilmedi; üretimde 404 atan bir çağrı yok.

### 5. Benzersizlik kısıtının test yüzeyine etkisi

`UQ_Transactions_SellerId_ItemAssetId_Active` (T117'de eklendi, T128 uygulama katmanını yazacak) aynı satıcının aynı item'ı için ikinci bir açık işleme izin vermiyor. Test fixture'larının çoğu sabit `ItemAssetId` ile birden fazla aktif satır seed ediyordu → 19 seed helper'ı satır başına benzersiz asset id üretecek şekilde güncellendi. Kısıtın kendisi doğru: teslimat item **sınıfı** üzerinden doğrulandığı için iki açık işlem, gelen item'ı yanlış işleme atfeder ve parayı yanlış satıcıya gönderirdi.

## Etkilenen Modüller / Dosyalar

- **Kaynak (bu oturum):** `TransactionCancellationService` · `TransactionStateMachine` · `CountdownSyncBroadcaster` · `RestartRecoveryService` · `NotificationTargetMapper` · `EscrowedAndTradeOfferNotificationConsumer`
- **Migration:** `src/Skinora.Shared/Persistence/Migrations/20260809162642_T117_P2P_Pivot.cs` (+ Designer, snapshot)
- **Test:** 41 dosya — Transactions, API, Notifications, Realtime, Disputes, Fraud
- **Önceki commit'ler:** 86 kaynak dosyası (38 silme), Steam modülü 35 → 11 dosya

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `TransactionStatus`/`Trigger`/`TimeoutPhase` yeniden tanımlandı | ✓ | `EnumTests` parity 356/356 ✓; `TransactionStatus` 12 değer, `TransactionTrigger` 14, `TimeoutPhase` 4 |
| 2 | `DeliveryEvidence` enum'u eklendi | ✓ | `[Flags]` NONE/BUYER_CONFIRMED/INVENTORY_DELTA/SELLER_ASSET_GONE + `IsSufficientForDelivery()` / `IsMisdeliverySignature()`; guard testleri `TransactionStateMachineTests` |
| 3 | Teslimat doğrulama alanları + deadline rename | ✓ | `Transaction` +11 alan; `SellerConfirmDeadline` / `DeliveryDeadline` rename; EF config + migration |
| 4 | Tek forward migration temiz DB'ye uygulanıyor, snapshot regenerate | ✓ | `InitialMigrationTests` taze SQL Server DB'ye 32 migration'ı uyguluyor: `PendingMigrations_IsEmpty_AfterInitialApply` ✓ · `Migrate_SecondRun_IsIdempotent` ✓ · `Model_HasNoPendingChanges` (`HasPendingModelChanges()==false` → model ↔ snapshot senkron) ✓ · `Schema_ContainsAllExpectedTables` (model-türetilmiş liste) ✓ |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build Skinora.sln -c Debug` |
| Unit | ✓ 1274/1274 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` (Docker'a bağlı 16 Discord/Telegram kanal testi lokalde hariç — CI'da koşar) |
| Integration | ✓ 630/630 | Proje bazında Testcontainers SQL Server: Transactions 305 · API 449 (bkz. not) · Notifications 60 · Fraud 73 · Disputes 41 · Platform 65 · Auth 37 · Admin 22 · Shared 21 · Payments 6 |
| Contract | ✓ 4/4 | `SidecarWebhookRouteContractTests` |

> **Ölçüm notu:** Tüm solution'ı tek `dotnet test` ile koşturmak lokalde 42 sahte hata üretti — her test assembly'si kendi SQL Server container'ını açıyor ve 9.6 GB'lık Docker ayrılmış belleği yetmiyor; düşen testler 1 ms'de ve fixture kurulumunda kırılıyordu. Proje bazında seri koşumda hepsi geçti. CI bunları ayrı leg'lerde ve sağlanan connection string ile koşuyor, bu sınıfa yapısal olarak girmiyor. (F6 Gate Check'te de aynı artefakt kayıtlı.)

## Altyapı Değişiklikleri

- **Migration:** Var — `T117_P2P_Pivot`. 3 tablo düşüyor (`TradeOffers`, `PlatformSteamBots`, `BotRecoveryItems`), `Transactions`'ta 3 kolon düşüyor, 2 rename, 11 yeni kolon, 3 yeni index (1'i unique filtered).
- **Config/env değişikliği:** Yok
- **Docker değişikliği:** Yok

## Alınan kararlar

Bunlar sonraki görevleri bağlar:

1. **Durum adları yeniden adlandırıldı, yeniden anlamlandırılmadı.** Amaç eski adı taşıyan testlerin sessizce yeşil kalıp geçersiz akışı doğrulamasını engellemek. Derleme hatası burada maliyet değil, güvenlik.

2. **`HasDeliveryEvidence` guard'ı `DeliveredBuyerAssetId`'ye bakmıyor.** Alıcı onayıyla kapanan teslimatta envanter hiç okunmamış olabilir; kanıt `DeliveryEvidence`'tır.

3. **`HasSettlementClearance` sürenin dolmasına değil kontrole bakıyor.** Beklemek geri alma penceresinin kapanmasını sağlar ama geri alınıp alınmadığını söylemez. Bu ikisi ayrılamaz.

4. **`ItemWasOnPlatform` yardımcıları tamamen kaldırıldı** (admin cancel, dispute resolve). Her zaman `false` dönecekleri için tutmanın anlamı yoktu.

5. **`ISteamTradeOfferUrlResolver` silindi.** Plan "korunur" diyordu ama kodda karşılığı kalmadı: silinen `TradeOffer` tablosunu sorguluyordu. Satıcıya gösterilecek bağlantı artık doğrudan `Transaction.BuyerTradeUrl`.

6. **`(SellerId, ItemAssetId)` benzersizlik kısıtı eklendi.** Teslimat item sınıfı üzerinden doğrulandığı için, aynı item'ı hedefleyen iki açık işlem gelen item'ı yanlış işleme atfeder ve parayı yanlış satıcıya gönderirdi.

7. **`DeliveryReversed` → `CancelledBy = SELLER`.** Yukarıda gerekçesi verildi; doğrulamada gözden geçirilecek ürün kararıdır.

8. **STEAM_OUTAGE freeze kapsamı `{ACCEPTED, PAYMENT_RECEIVED}`.** P2P'de taraflar Steam ayaktayken trade'i yapabilir; bozulan şey platformun **doğrulama** yeteneğidir. Doğrulanamayan teslimat fazı donmazsa satıcı haksız yere teslim etmemiş sayılır.

## Known Limitations / Follow-up

- **`SellerConfirmDeadline` / `DeliveryDeadline` armlanmıyor.** Alanlar ve state machine hazır; bunları dolduran kod T123/T124'te yazılacak. Bilinçli — T117 domain çekirdeği, akış değil.
- **`trade_offer_seller_timeout_minutes` / `trade_offer_buyer_timeout_minutes` SystemSetting anahtarları eski adlarıyla duruyor.** Değer semantiği doğru (satıcı onay süresi / teslimat süresi) ama adlar emekli modelden. Yeniden adlandırma migration + seed + admin UI etkisi taşır; T123/T124 kapsamında ele alınmalı.
- **Emekli status değerleri için veri migration'ı yok** — gerekçe yukarıda (§Migration).
- **8 advisory E2E leg'i kırmızı — beklenen, planda öngörülmüş.** Kök sebep CI logundan doğrulandı: `RequestError: Invalid object name 'PlatformSteamBots'` — `e2e/src/db.ts` içindeki `seedHappyPath` bu migration'ın düşürdüğü tabloyu temizlemeye çalışıyor, ilk seed çağrısında patlıyor ve leg'in kalan testleri artık-tamamlanmamış seed'in üzerine `PK_Users` çakışmasıyla yığılıyor. Kısmi bir düzeltme (yalnız o `DELETE`'i kaldırmak) hatayı taşır, çözmez: spec'lerin kendisi custodial akışı (`ITEM_ESCROWED`, trade offer, bot) sürüyor ve o model artık yok. Plan bunu zaten ayırmış — **T137** (`sidecar-fake` sürülebilir envanter, notu: *"Tüm E2E'yi bloklar"*) → **T138** (E2E spec'lerinin yeniden yazımı). E2E leg'leri `continue-on-error` olduğu için CI Gate'i bloke etmiyorlar; F7 boyunca T138'e kadar kırmızı kalacaklar.
- **Sidecar bot/trade yayıncıları hâlâ ölü backend yollarına POST ediyor** — T133'e kadar sürecek bilinen drift; guard'da adı konmuş istisna + kendini iptal eden bekçi testi (§4). T133 bu istisnayı silmek zorunda, aksi hâlde `RetiredPathsAreStillPublished_UntilT133` kırılır.
- **T118 kapsamı daraldı.** State machine bu dalda yeniden yazıldı ve geçiş tablosunun tamamı test edildi; T118'e kalan iş, 05 §4.2 karşısında bağımsız bir kapsam denetimi.

## Notlar

- **Working tree:** Oturum başında temiz.
- **Main CI startup check:** Son 3 run `success` — `31316632955`, `31316632950`, `31316362102`.
- **Dış varsayım:** Yok. Görev tamamen repo içi; yeni paket, plan tier veya dış API bağımlılığı eklenmedi.
- **Branch izolasyon:** `git log main..HEAD` yalnız `T117` üretiyor.
- `Skinora.Steam.Tests` geriye yalnız envanter ve trade-hold testleriyle kaldı (3 dosya) — modülün yeni kapsamıyla birebir örtüşüyor.
- Migration dosyalarına (T117 öncesi) dokunulmadı: içlerinde enum kod referansı yok, hepsi metin. Tarihsel kayıt olarak korunuyorlar.

## Commit & PR

- Branch: `task/T117-enum-transaction-fields`
- PR: [#222](https://github.com/turkerurganci/Skinora/pull/222)
- CI: ✓ PASS — HEAD `07c86fa`, run [`31330229732`](https://github.com/turkerurganci/Skinora/actions/runs/31330229732), `conclusion=success`

| Job | Sonuç |
|---|---|
| 1. Lint · 2. Build · 3. Unit · 4. Integration · 5. Contract · 6. Migration dry-run · 7. Docker build (backend) | ✓ success |
| **CI Gate** | **✓ success** |
| 3b. JS test (vitest) | skipped (JS yolu değişmedi) |
| 8× E2E (advisory) | ✗ failure — kök sebep doğrulandı, bkz. Known Limitations |

> Bir önceki run (`31330207219`, HEAD `f360c68`) `cancelled`: rapor düzeltmesi push'u onu iptal etti. Bu bir başarısızlık değil — concurrency davranışı; yetkili olan son tamamlanmış run'dır.
