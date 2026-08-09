# T116 — DEFERRED_BACKLOG P2P Kayıtları

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-09

---

## Yapılan İşler

P2P geçişi sırasında bilinçli olarak kapsam dışında bırakılan işler `Docs/DEFERRED_BACKLOG.md`'ye kaydedildi. Ayrıca bot katmanının kaldırılmasıyla konusuz kalan eski kalemler işaretlendi.

## Etkilenen Dosyalar

- `Docs/DEFERRED_BACKLOG.md` — yeni §9 bölümü (6 kalem), başlık durum notu, Öne Çıkanlar tablosuna 3 satır

## Eklenen Kalemler

| ID | Öncelik | Konu |
|---|---|---|
| `P2P-SettlementTiering` | 🟡 | İtibarlı satıcılar için 8 günlük mutabakat süresinin kısaltılması |
| `P2P-HotWalletPolicyReview` | 🟡 | Sıcak/soğuk cüzdan politikasının yeni para tutma profiline göre gözden geçirilmesi |
| `P2P-DeliveryPollingJob` | 🟡 | Sürekli teslimat taraması (pasif alıcı senaryosunu hızlandırır) |
| `P2P-FloatVerification` | ⚪ | Aynı sınıf içindeki kalite farkının doğrulanması |
| `P2P-SellerDebtLedger` | ⚪ | Satıcı kusurlu iadelerde gas ücretinin kusurlu tarafa yazılması |
| `P2P-BotCodeArchive` | ⚪ | Bot kodu silme commit'inin sha'sının kayda geçirilmesi (T132/T133 sonrası) |

## Konusuz Kalan Eski Kalemler

Bot katmanı kaldırıldığı için (02 §15, 05 §3.2) şu açık kalemler artık iş üretmiyor:

- `T69-K4` — bot durumu yayınının admin grubuna daraltılması
- `T68-K1` — bot yaşam döngüsü olayı → admin bildirimi
- `T64-BotWebhookHandler` — bot oturum hatası webhook handler'ı

Satırlar tarihsel izlenebilirlik için **yerinde bırakıldı**, silinmedi. §9 sonuna açıklayıcı not eklendi. Aynı yaklaşım T115'te kaldırılan doküman bölümlerinde de uygulanmıştı.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bot-custody kodu için arşiv işaretçisi | ✓ | `P2P-BotCodeArchive` kalemi; sha T132/T133 merge'ünden sonra yazılacak |
| 2 | `DeliveryPollingJob` kaydı | ✓ | `P2P-DeliveryPollingJob` — servisin bu iş için hazır tasarlandığı notuyla |
| 3 | Float/inspect doğrulaması kaydı | ✓ | `P2P-FloatVerification` — neden kapsam dışı bırakıldığı gerekçesiyle |
| 4 | Satıcı borç defteri kaydı | ✓ | `P2P-SellerDebtLedger` |

Kriter listesinde olmayan ama 8 günlük mutabakat kararı (T115, 02 §4.5.1) sonrası ortaya çıkan iki kalem de eklendi: `P2P-SettlementTiering` ve `P2P-HotWalletPolicyReview`.

## Test Sonuçları

Kod değişikliği yok.

## Altyapı Değişiklikleri

Yok.

## Notlar

Satır sayısı 28 → 34 aktif kaleme çıktı. Yeni kalemlerin hiçbiri bir sonraki adımı bloklamıyor; hepsi post-MVP.
