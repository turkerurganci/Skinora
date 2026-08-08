# T115 — Docs P2P Geçişi (02/03/04/05/06/07/08/10/11)

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-08

---

## Yapılan İşler

MVP'nin custodial bot escrow modeli, Steam tarafındaki bir kural değişikliği nedeniyle çalışamaz hâle geldi. Bu task, P2P modeline geçişi 9 spec dokümanına yansıtır. Kod yazılmadı — F7'nin geri kalanı (T116–T138) bu dokümanları referans alacak.

**Değişimin gerekçesi (02 §2.1):** Steam Trade Protection (16.07.2025) ve trade cooldown reworku (02.2026) sonrası bir CS2 item'ı trade ile bir envantere girdiğinde 7 gün boyunca transfer edilemiyor. Platform botu item'ı emanete aldığı anda alıcıya gönderemez — çift-trade modeli bu kural altında çalışamaz. Tek trade ile item doğrudan alıcıya gider; oluşan kilit alıcının kendi envanterinde kalır ve akışı bloklamaz.

**Model:** Item custody kaldırıldı, escrow edilen artık para. Sıra tersine döndü: önce ödeme emanete girer, sonra satıcı item'ı doğrudan alıcıya gönderir.

## Etkilenen Modüller / Dosyalar

| Doküman | Versiyon | Ana değişiklik |
|---|---|---|
| `Docs/02_PRODUCT_REQUIREMENTS.md` | v2.6 → v3.0 | Escrow modeli (§2.1 yeni), 8 adımlı akış, teslimat doğrulama kuralları (§9.2 yeni), iki taraflı MA (§9.1 yeni), trade reversal riski (§20.2), §15 kaldırıldı |
| `Docs/03_USER_FLOWS.md` | v2.2 → v3.0 | 12 akış; satıcı hazırlık onayı (§2.3), iki yollu teslimat doğrulama (§3.5), timeout sorumluluk değişimi (§4.2/§4.4), §8.5 ve §11.2a kaldırıldı |
| `Docs/04_UI_SPECS.md` | v3.0 → v4.0 | StatusBadge haritası, S07 state×rol matrisi, iki yeni buton, S18 kaldırıldı |
| `Docs/05_TECHNICAL_ARCHITECTURE.md` | v2.3 → v3.0 | State machine (§4.1/§4.2 yeniden yazım), sidecar salt-okunur proxy'ye küçültüldü (§3.2), event listesi (§5.3) |
| `Docs/06_DATA_MODEL.md` | v5.1 → v6.0 | TransactionStatus, DeliveryEvidence enum'u (§2.24 yeni), Transaction alanları, 3 entity kaldırıldı, invariant matrisleri, 2 yeni indeks |
| `Docs/07_API_DESIGN.md` | v2.2 → v3.0 | 2 yeni endpoint (T6a/T6b), T6 ve T7 sözleşme değişikliği, availableActions, 3 admin endpoint kaldırıldı |
| `Docs/08_INTEGRATION_SPEC.md` | v2.5 → v3.0 | Envanter okuma (§2.3), trade offer yönetimi kaldırıldı (§2.4), rate limit (§2.6), risk matrisi (§2.8) |
| `Docs/10_MVP_SCOPE.md` | v1.3 → v2.0 | Escrow akışı, item yönetimi, yeni sınırlar, §4.1 Kabul Edilen Riskler (yeni) |
| `Docs/11_IMPLEMENTATION_PLAN.md` | v0.5 → v0.6 | F7 fazı + T115–T138 task listesi |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Dokümanlar arası tutarlılık (GUARDRAILS §5) | ✓ | Durum adları 9 dokümanda tek küme: `CREATED, ACCEPTED, SELLER_CONFIRMED, PAYMENT_RECEIVED, ITEM_DELIVERED, COMPLETED, CANCELLED_*, REFUNDED, FLAGGED`. Emekli adların kalıntısı için grep taraması yapıldı (`ITEM_ESCROWED`, `TRADE_OFFER_SENT`, `EscrowBot`, `PlatformSteamBot`, `TradeOffer`, `BotRecovery`) — kalan tek geçişler "kaldırılmıştır" notları içinde |
| 2 | Belirsiz ifade yok | ✓ | "muhtemelen/belki/olabilir" kullanılmadı; her kaldırma kararı gerekçelendirildi |
| 3 | Traceability matrisleri güncel | ✓ | 06 §7 (veri modeli→kaynak), 04 §2/§3, 11 §5 güncellendi |
| 4 | Kaldırılan bölümlerin numaraları korundu | ✓ | 02 §15, 03 §8.5/§11.2a, 04 §8.7, 06 §2.7/§2.8/§2.15/§3.9/§3.10/§3.10a, 07 §9.10/§9.28/§9.29 — hepsi "kaldırılmıştır" notuyla yerinde duruyor |

## Test Sonuçları

Kod değişikliği yok — otomatik test kapsamı dışında. Doğrulama grep tabanlı tutarlılık taraması ile yapıldı (yukarıda kriter 1).

## Altyapı Değişiklikleri

- Migration: Yok (bu task doküman-only; şema değişikliği T117'de)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Alınan Tasarım Kararları

Bunlar T117–T138'i doğrudan bağlar:

1. **Durum adları yeniden adlandırıldı, yeniden anlamlandırılmadı.** `ITEM_ESCROWED` adını korumak, hiçbir item'ın emanette olmadığı bir duruma o adı yüklemek olurdu. `TransactionHistory`, audit log ve dispute delili kalıcı kayıttır. Ek gerekçe: rename'in ürettiği derleme hatası bir maliyet değil, güvenlik — eski adı taşıyan testlerin sessizce yeşil kalıp geçersiz akışı doğrulamasını engeller. Şema maliyeti sıfır (status string olarak saklanıyor, CHECK constraint değerleri listelemiyor, prod verisi yok).

2. **Kaldırılan bölüm numaraları korundu.** Silinseydi sonraki bölümler kayar ve dokümanlar arası `02 §16+` gibi tüm çapraz referanslar sessizce yanlış hedefi gösterirdi.

3. **`DeliveredBuyerAssetId` zorunlu alan matrisinden çıkarıldı.** Alıcı onayıyla kapanan işlemde envanter hiç okunmamış olabilir. Guard artık `DeliveryEvidence`'a bakıyor.

4. **`BuyerBaselineCapturedAt` zorunlu değil.** Alıcının envanteri gizliyse anlık görüntü alınamaz; bu işlemi bloklamamalı, yalnız kanıt yolunu kapatmalı.

5. **Üç değerli envanter görünürlüğü (Public/Private/Unavailable).** "Okunamadı" ile "okundu, item yok" karıştırılırsa Steam kesintisi sırasında teslim edilmiş bir işlem haksız yere iade edilir.

6. **`PAYMENT_RECEIVED`'da satıcı iptali açık, alıcı iptali kapalı.** Kapatılsaydı göndermek istemeyen satıcı hiçbir şey yapmayıp timeout'u beklerdi; alıcı parasına daha geç kavuşurdu.

7. **`WRONG_ITEM` dispute'una `PAYMENT_RECEIVED` eklendi.** Satıcı farklı item gönderirse beklenen sınıfın sayısı artmaz, işlem `ITEM_DELIVERED`'a hiç ulaşmaz — alıcının o noktada itiraz edebilmesi gerekir.

8. **Yeni indeks `(SellerId, ItemAssetId)` filtered unique.** Teslimat doğrulaması item sınıfı üzerinden yapıldığı için, aynı item'ı hedefleyen iki açık işlem gelen item'ı yanlış işleme atfeder ve parayı yanlış satıcıya gönderirdi.

## Known Limitations / Follow-up

- **T122 (gerçek Steam probu) kritik bağımlılıktır.** Teslimat doğrulamasının tasarımı, gerçek envanter davranışına dayanıyor: item alıcıda ne kadar sürede görünüyor, `classid`/`instanceid` beklendiği gibi mi geliyor, Trade Protection envanter yanıtında nasıl işaretleniyor. `sidecar-fake` bunu kanıtlayamaz. Bu ölçüm T125 yazılmadan yapılmalı ve gerekirse 02 §9.2 revize edilmeli.
- 02 §22 (Kullanıcı Sözleşmesi) hâlâ içerik bekliyor; P2P ile zorunlu hale gelen üç konu maddelendi ama metin yazılmadı.
- 09 Coding Guidelines bu taskta güncellenmedi — P2P'ye özgü kod standardı değişikliği tespit edilmedi.

## Notlar

Planda "02 §15 silinir" yazıyordu; uygulamada numara korunarak içerik boşaltıldı (karar 2). Aynı yaklaşım tüm kaldırılan bölümlere uygulandı.
