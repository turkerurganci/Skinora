# T117 — Domain Çekirdeği: Enum, Transaction Alanları, State Machine, Bot Emekliliği

**Faz:** F7 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-08-09

> **Bu rapor yarım bir iş için yazıldı.** Dal `task/T117-enum-transaction-fields`, 7 commit, uzağa gönderildi. Kaynak katmanı derleniyor, test katmanı derlenmiyor. PR **açılmadı** — derleme temiz olmadan açılsa CI kırmızı olurdu.

---

## Kapsam neden birleşti

Plan T117 (enum + alanlar), T118 (state machine) ve T132 (bot kodu silme) için ayrı görevler öngörüyordu. Ölçtüğümde enum'dan değer silmenin **136 dosyayı** birden kırdığı ortaya çıktı: `ITEM_ESCROWED` ve `TRADE_OFFER_SENT_TO_*` 91 dosyada, bot alanları 62 dosyada geçiyordu.

Bunlar aynı anda derlenmek zorunda. Ayrı ayrı merge edilseydi ana dal derlenmez hâlde kalırdı — proje kuralı bunu yasaklıyor. Proje sahibi onayıyla tek dalda birleştirildi; içeride commit'ler ayrı tutuldu.

## Tamamlanan

| Commit | İçerik |
|---|---|
| `b664362` | Enum'lar, `Transaction` alanları, EF yapılandırması |
| `59134d2` | State machine geçiş tablosu ve guard'lar |
| `f8857fa` | Bot custody katmanı silindi (Steam modülü 35 → 11 dosya) |
| `2ac3374` | Dispute uygunluk matrisi + işlem detay servisi |
| `e65f8a4` | Timeout servisleri yeni fazlara taşındı |
| `f9a0eb7` | Kalan modüller + API katmanı — **kaynak derlemesi temiz** |
| `5f4b005` | Bot testleri silindi, enum parity testleri güncellendi |

## Kalan iş

**1. 16 test dosyası derlenmiyor.** Güncel listeyi almak için:

```
cd backend && dotnet build Skinara.sln -c Debug --nologo -v q 2>&1 | grep "error CS"
```

Çoğu mekanik (silinen enum adı, kaldırılan alan). Ancak bir kısmı **gerçek davranış değişikliğini** yakalayacak — bunlar tek tek okunmalı, körlemesine düzeltilmemeli:

- Teslimat timeout'unun sorumlusu alıcıdan **satıcıya** geçti
- `DELIVERY_EXPECTED` bildirimi alıcıdan **satıcıya** geçti
- `PAYMENT_RECEIVED`'da iptal yetkisi **asimetrik** (satıcı edebilir, alıcı edemez)
- `ITEM_DELIVERED` → `COMPLETED` artık mutabakat kontrolü istiyor

**2. Migration henüz üretilmedi.** Şema değişiklikleri EF yapılandırmasında var ama `dotnet ef migrations add P2P_Pivot` çalıştırılmadı. Test derlemesi temizlendikten sonra yapılmalı.

**3. Testler çalıştırılıp doğrulanmadı.** 2570 testin ne kadarının kırıldığı bilinmiyor.

**4. `SellerConfirmDeadline` / `DeliveryDeadline` armlanmıyor.** Alanlar ve state machine hazır ama bunları dolduran kod T123/T124'te yazılacak. Bu bilinçli — T117 domain çekirdeği, akış değil.

## Alınan kararlar

Bunlar sonraki görevleri bağlar:

1. **Durum adları yeniden adlandırıldı, yeniden anlamlandırılmadı.** Amaç eski adı taşıyan testlerin sessizce yeşil kalıp geçersiz akışı doğrulamasını engellemek. Derleme hatası burada maliyet değil, güvenlik.

2. **`HasDeliveryEvidence` guard'ı `DeliveredBuyerAssetId`'ye bakmıyor.** Alıcı onayıyla kapanan teslimatta envanter hiç okunmamış olabilir; kanıt `DeliveryEvidence`'tır.

3. **`HasSettlementClearance` sürenin dolmasına değil kontrole bakıyor.** Beklemek geri alma penceresinin kapanmasını sağlar ama geri alınıp alınmadığını söylemez. Bu ikisi ayrılamaz.

4. **`ItemWasOnPlatform` yardımcıları tamamen kaldırıldı** (admin cancel, dispute resolve). Her zaman `false` dönecekleri için tutmanın anlamı yoktu.

5. **`ISteamTradeOfferUrlResolver` silindi.** Plan "korunur" diyordu ama kodda karşılığı kalmadı: silinen `TradeOffer` tablosunu sorguluyordu. Satıcıya gösterilecek bağlantı artık doğrudan `Transaction.BuyerTradeUrl`.

6. **`(SellerId, ItemAssetId)` benzersizlik kısıtı eklendi.** Teslimat item sınıfı üzerinden doğrulandığı için, aynı item'ı hedefleyen iki açık işlem gelen item'ı yanlış işleme atfeder ve parayı yanlış satıcıya gönderirdi.

## Notlar

`Skinora.Steam.Tests` geriye yalnız envanter ve trade-hold testleriyle kaldı (3 dosya) — modülün yeni kapsamıyla birebir örtüşüyor.

Migration dosyalarına dokunulmadı: içlerinde enum kod referansı yok, hepsi metin. Tarihsel kayıt olarak korunuyorlar.
