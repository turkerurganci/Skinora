# Skinora — Data Model

**Versiyon: v5.0** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `09_CODING_GUIDELINES.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-04-23 (T34 — User'a `PayoutAddressChangedAt` + `RefundAddressChangedAt`, SystemSetting'e iki yeni wallet cooldown parametresi eklendi)

---

## 1. Genel Bakış

Bu doküman, Skinora platformundaki tüm veri yapılarını tanımlar. Entity'ler, field'lar, ilişkiler, enum'lar ve indeks stratejisi dahildir. Tüm tanımlar ürün gereksinimlerinden (02), kullanıcı akışlarından (03) ve teknik mimariden (05) türetilmiştir.

### 1.1 Entity Envanteri

| # | Grup | Entity | Sorumluluk |
|---|------|--------|------------|
| 1 | Kullanıcı & Kimlik | **User** | Kullanıcı profili, Steam kimliği, cüzdan adresleri, itibar |
| 2 | | **UserLoginLog** | IP / cihaz parmak izi kaydı (çoklu hesap tespiti) |
| 3 | | **RefreshToken** | JWT refresh token, session yönetimi |
| 4 | | **UserNotificationPreference** | Bildirim kanalı tercihleri ve hesap bağlantıları |
| 5 | İşlem | **Transaction** | İşlem yaşam döngüsü — item, fiyat, durum, taraflar, timeout |
| 6 | | **TransactionHistory** | Her state geçişinin audit kaydı |
| 7 | Ödeme & Blockchain | **PaymentAddress** | İşlem başına üretilen benzersiz blockchain adresi |
| 8 | | **BlockchainTransaction** | Gelen/giden tüm blockchain transferleri |
| 9 | Steam Entegrasyonu | **TradeOffer** | Steam trade offer takibi |
| 10 | | **PlatformSteamBot** | Platform Steam bot hesapları |
| 11 | Güven & Güvenlik | **Dispute** | İtiraz kaydı, otomatik çözüm, eskalasyon |
| 12 | | **FraudFlag** | Fraud tespiti, admin inceleme |
| 13 | Bildirim | **Notification** | Platform içi bildirimler |
| 14 | Admin & Ayarlar | **AdminRole** | Admin rol tanımları |
| 15 | | **AdminRolePermission** | Rol başına yetki atamaları |
| 16 | | **AdminUserRole** | Kullanıcı-rol eşlemesi |
| 17 | | **SystemSetting** | Admin tarafından yönetilen parametreler |
| 18 | Altyapı | **OutboxMessage** | Event outbox — kayıp garantisi |
| 19 | | **ProcessedEvent** | Consumer idempotency takibi |
| 20 | | **ExternalIdempotencyRecord** | Dış servis (sidecar/blockchain) receiver-side idempotency (05 §5.1) |
| 21 | | **SystemHeartbeat** | Platform uptime takibi — outage window hesaplaması (05 §4.4) |
| 22 | Finans | **ColdWalletTransfer** | Hot→cold wallet transfer ledger kaydı (05 §3.3) |
| 23 | Audit | **AuditLog** | Fon hareketleri, admin aksiyonları, güvenlik olayları — kalıcı audit trail |
| 24 | Ödeme & Blockchain | **SellerPayoutIssue** | Satıcı payout sorun bildirimi ve çözüm takibi (02 §10.3) |
| 25 | Bildirim | **NotificationDelivery** | Dış kanal bildirim teslimat kaydı (email, Telegram, Discord) |

### 1.2 İlişki Diyagramı

```
User ─┬── 1:N ──→ Transaction (as Seller)
      ├── 1:N ──→ Transaction (as Buyer)
      ├── 1:N ──→ UserLoginLog
      ├── 1:N ──→ RefreshToken
      ├── 1:N ──→ UserNotificationPreference
      ├── 1:N ──→ Notification
      └── N:M ──→ AdminRole (via AdminUserRole)

Transaction ─┬── 1:1 ──→ PaymentAddress
             ├── 1:N ──→ BlockchainTransaction
             ├── 1:N ──→ TradeOffer
             ├── 1:N ──→ TransactionHistory
             ├── 1:N ──→ Dispute
             ├── 1:N ──→ FraudFlag
             ├── 1:N ──→ Notification
             ├── 1:N ──→ SellerPayoutIssue
             └── N:1 ──→ PlatformSteamBot (EscrowBot)

Notification ─── 1:N ──→ NotificationDelivery

AdminRole ─── 1:N ──→ AdminRolePermission

User ─── 1:N ──→ AuditLog (as Actor)
User ─── 1:N ──→ AuditLog (as Subject, opsiyonel)
```

### 1.3 Silme Stratejisi

Tüm entity'ler silme davranışına göre üç kategoriye ayrılır:

| Kategori | Davranış | Entity'ler |
|----------|----------|-----------|
| **Soft Delete (Kalıcı)** | `IsDeleted` + `DeletedAt` field'ları. Kayıt silinmez, işaretlenir. Canlı tabloda kalıcı olarak kalır. | User, UserNotificationPreference, UserLoginLog, PlatformSteamBot, AdminRole, AdminRolePermission, AdminUserRole, RefreshToken, FraudFlag (ACCOUNT_LEVEL) |
| **Mutable Catalog (Delete Yasak)** | Seed ile oluşturulur, admin tarafından value güncellenebilir. Silme (soft veya hard) tanımlı değildir — seed contract ile startup fail-fast buna bağımlıdır. Key seti yalnızca migration ile değişir. | SystemSetting |
| **Soft Delete (Arşivlenebilir)** | Aynı mekanizma, ama transaction archive set ile birlikte archive tabloya taşınabilir (§8.8). Arşivleme öncesi ve sonrası soft delete semantiği geçerlidir. | Transaction, PaymentAddress, Dispute, FraudFlag (TRANSACTION_PRE_CREATE), Notification (TransactionId NOT NULL) |
| **Append-Only (Arşivlenebilir)** | INSERT sonrası güncellenmez. Gerçek immutable — her satır oluşturulduğu haliyle kalır. DELETE tanımlı değil. Transaction archive set ile birlikte archive tabloya taşınabilir (§8.8). | TransactionHistory |
| **Workflow Record (Arşivlenebilir)** | DELETE tanımlı değil, ama yaşam döngüsü boyunca state/status güncellemesi alır (ör: Status, RetryCount, ConfirmationCount). Terminal state'e ulaştıktan sonra fiilen frozen olur. Transaction archive set ile birlikte archive tabloya taşınabilir (§8.8). | BlockchainTransaction, TradeOffer, SellerPayoutIssue, NotificationDelivery |
| **Append-Only (Kalıcı)** | INSERT sonrası güncellenmez. Gerçek immutable. DELETE tanımlı değil. Arşivleme kapsamı dışında — süresiz olarak canlı tabloda kalır. | AuditLog, ColdWalletTransfer |
| **Retention-Based** | İşlendikten sonra belirli süre saklanır, sonra toplu temizlenir. Infrastructure verileri. | OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord |
| **Singleton** | Tek satır, güncellenir, silinmez. | SystemHeartbeat |

> **EF Core Global Query Filter:** Soft delete entity'leri için `HasQueryFilter(e => !e.IsDeleted)` kullanılır. Tüm sorgulara otomatik uygulanır, gerektiğinde `IgnoreQueryFilters()` ile bypass edilir.
>
> **Operasyonel kullanıcı predicate'i:** User tablosunda gerçek son kullanıcılar dışında sentinel (SYSTEM) ve deaktif/silinen kayıtlar bulunur. Sorgu tiplerine göre standart predicate'ler:
>
> | Sorgu tipi | Predicate | Açıklama |
> |-----------|-----------|----------|
> | User-facing listeler, seçiciler, metrikler | `WHERE IsDeleted = 0 AND IsDeactivated = 0` | Gerçek, aktif kullanıcılar. SYSTEM sentinel ve deaktif hesaplar dışlanır |
> | Tarihsel/audit sorgular (transaction detay, audit trail, dispute) | `IgnoreQueryFilters()` + tüm User kayıtları dahil | FK referansları çözümlenir. Silinen kullanıcı `SteamDisplayName = "Deleted User"` olarak görünür |
> | Admin kullanıcı yönetimi | `WHERE IsDeleted = 0` (IsDeactivated dahil görünür) | Admin deaktif hesapları görebilir ve yönetebilir |
>
> **Not:** Global query filter yalnızca `IsDeleted` kontrol eder. `IsDeactivated` kontrolü operasyonel sorgularda ayrıca uygulanmalıdır — bu, repository/query standardı olarak tüm user-facing endpoint'lerde zorunludur.
>
> **Soft delete + retention lifecycle:** Soft delete entity'lerinden bazıları ek olarak retention-based hard purge'a tabidir. İki aşamalı yaşam döngüsü:
> 1. **Operasyonel silme:** `IsDeleted = true`, `DeletedAt = NOW` — kayıt sorgulardan gizlenir ama DB'de kalır.
> 2. **Hard purge:** Retention süresi dolduktan sonra toplu hard delete ile kalıcı olarak silinir.
>
> | Entity | Retention sonrası hard purge | Süre |
> |--------|----------------------------|------|
> | UserLoginLog | Evet | 1 yıl (§6.1) |
> | RefreshToken | Evet | Süresi dolan + revoke edilenler periyodik temizlenir |
> | Notification (TransactionId NOT NULL) | Arşivleme | Transaction arşivlendiğinde bağlı bildirimler de archive set ile taşınır (§8.4) |
> | Notification (TransactionId = NULL) | Evet | Bağımsız bildirimler için ayrı retention (ör: 1 yıl, §6.1) |
> | Soft Delete (Arşivlenebilir) entity'ler | Arşivleme | Transaction archive set ile birlikte archive tabloya taşınır (§8.8) |
> | Soft Delete (Kalıcı) entity'ler | Hayır — soft delete kalıcıdır (UserLoginLog ve RefreshToken hariç — yukarıda ayrıca tanımlı) | — |
> | Mutable Catalog entity'ler (SystemSetting) | Hayır — silme tanımlı değil, key seti migration ile yönetilir | — |

---

## 2. Enum Tanımları

### 2.1 TransactionStatus

| Değer | Açıklama |
|-------|----------|
| `CREATED` | İşlem oluşturuldu, alıcı bekleniyor |
| `ACCEPTED` | Alıcı kabul etti, satıcıdan item bekleniyor |
| `TRADE_OFFER_SENT_TO_SELLER` | Satıcıya trade offer gönderildi |
| `ITEM_ESCROWED` | Item platforma emanet edildi, ödeme bekleniyor |
| `PAYMENT_RECEIVED` | Ödeme doğrulandı, item teslimi başlıyor |
| `TRADE_OFFER_SENT_TO_BUYER` | Alıcıya trade offer gönderildi |
| `ITEM_DELIVERED` | Item alıcıya teslim edildi, satıcıya ödeme gönderiliyor |
| `COMPLETED` | İşlem tamamlandı |
| `CANCELLED_TIMEOUT` | Timeout nedeniyle iptal |
| `CANCELLED_SELLER` | Satıcı tarafından iptal |
| `CANCELLED_BUYER` | Alıcı tarafından iptal |
| `CANCELLED_ADMIN` | Admin tarafından iptal (flag reddi veya admin doğrudan iptali — 03 §8.7) |
| `FLAGGED` | Fraud tespiti nedeniyle durduruldu, admin onayı bekleniyor |

> **Kaynak:** 03 §1.2 — birebir aynı durum listesi.

### 2.2 StablecoinType

| Değer | Açıklama |
|-------|----------|
| `USDT` | Tether (TRC-20) |
| `USDC` | USD Coin (TRC-20) |

### 2.3 BuyerIdentificationMethod

| Değer | Açıklama |
|-------|----------|
| `STEAM_ID` | Satıcı alıcının Steam ID'sini belirtir (MVP'de aktif) |
| `OPEN_LINK` | Açık link — ilk kabul eden alıcı olur (MVP'de pasif) |

### 2.4 CancelledByType

| Değer | Açıklama |
|-------|----------|
| `TIMEOUT` | Süre dolması |
| `SELLER` | Satıcı iptali |
| `BUYER` | Alıcı iptali |
| `ADMIN` | Admin tarafından iptal (flag reddi veya doğrudan iptal) |

### 2.5 BlockchainTransactionType

| Değer | Açıklama |
|-------|----------|
| `BUYER_PAYMENT` | Alıcının ödeme göndermesi |
| `SELLER_PAYOUT` | Satıcıya ödeme gönderimi |
| `BUYER_REFUND` | Alıcıya iade (iptal/timeout) |
| `EXCESS_REFUND` | Fazla tutar iadesi |
| `WRONG_TOKEN_INCOMING` | Yanlış token ile gelen transfer tespiti — desteklenen allowlist'teki token (USDT/USDC) ama beklenen tokenden farklı (audit kaydı) |
| `WRONG_TOKEN_REFUND` | Yanlış token iadesi |
| `SPAM_TOKEN_INCOMING` | Bilinmeyen/desteklenmeyen token transferi tespiti — allowlist dışı token. Otomatik iade yapılmaz, ignore + log (08 §3.4). Admin dashboard'da görünür. |
| `LATE_PAYMENT_REFUND` | Gecikmeli ödeme iadesi |
| `INCORRECT_AMOUNT_REFUND` | Eksik tutar iadesi |

### 2.6 BlockchainTransactionStatus

| Değer | Açıklama |
|-------|----------|
| `DETECTED` | Blockchain üzerinde ilk tespit — henüz onay süreci başlamadı |
| `PENDING` | Onay bekleniyor (< 20 blok) |
| `CONFIRMED` | Onaylandı (≥ 20 blok) |
| `FAILED` | Başarısız |

> **Kaynak:** 05 §3.3 — `pending → confirmed` akışı. `DETECTED` durumu ilk tespit ile onay sürecinin başlaması arasındaki geçiş anını temsil eder (05'te implicit, burada explicit).

### 2.7 TradeOfferDirection

| Değer | Açıklama |
|-------|----------|
| `TO_SELLER` | Satıcıdan item almak için (escrow) |
| `TO_BUYER` | Alıcıya item teslimi |
| `RETURN_TO_SELLER` | İptal/timeout durumunda satıcıya iade |

### 2.8 TradeOfferStatus

| Değer | Açıklama |
|-------|----------|
| `PENDING` | Oluşturuldu, gönderilmeyi bekliyor |
| `SENT` | Steam üzerinde gönderildi |
| `ACCEPTED` | Karşı taraf kabul etti |
| `DECLINED` | Karşı taraf reddetti |
| `EXPIRED` | Steam'de süresi doldu |
| `FAILED` | Gönderilemedi (API hatası) |

### 2.9 DisputeType

| Değer | Açıklama |
|-------|----------|
| `PAYMENT` | "Ödedim ama sistem görmüyor" |
| `DELIVERY` | "Item teslim edilmedi" |
| `WRONG_ITEM` | "Yanlış item geldi" |

### 2.10 DisputeStatus

| Değer | Açıklama |
|-------|----------|
| `OPEN` | Açıldı, otomatik kontrol yapılıyor/yapıldı |
| `ESCALATED` | Kullanıcı admin'e iletti |
| `CLOSED` | Çözüldü (herhangi bir yolla) |

### 2.11 FraudFlagType

| Değer | Açıklama |
|-------|----------|
| `PRICE_DEVIATION` | Piyasa fiyatından sapma |
| `HIGH_VOLUME` | Kısa sürede yüksek işlem hacmi |
| `ABNORMAL_BEHAVIOR` | Anormal davranış örüntüsü |
| `MULTI_ACCOUNT` | Çoklu hesap tespiti (cüzdan/IP/cihaz) |

### 2.12 ReviewStatus

| Değer | Açıklama |
|-------|----------|
| `PENDING` | Admin incelemesi bekleniyor |
| `APPROVED` | Admin onayladı (flag kaldırıldı, işlem devam) |
| `REJECTED` | Admin reddetti (işlem iptal) |

### 2.13 NotificationType

| Değer | Hedef | Açıklama |
|-------|-------|----------|
| `TRANSACTION_INVITE` | Alıcı | Yeni işlem daveti |
| `BUYER_ACCEPTED` | Satıcı | Alıcı işlemi kabul etti |
| `ITEM_ESCROWED` | Alıcı | Item emanete alındı, ödeme yapabilirsin |
| `PAYMENT_RECEIVED` | Satıcı | Ödeme doğrulandı |
| `TRADE_OFFER_SENT_TO_BUYER` | Alıcı | Item gönderildi, trade offer'ı kabul et |
| `TRANSACTION_COMPLETED` | Her ikisi | İşlem tamamlandı |
| `SELLER_PAYMENT_SENT` | Satıcı | Ödeme cüzdana gönderildi |
| `TIMEOUT_WARNING` | İlgili taraf | Süre dolmak üzere |
| `TRANSACTION_CANCELLED` | Her ikisi | İşlem iptal oldu |
| `TRANSACTION_FLAGGED` | Satıcı | İşlem incelemeye alındı |
| `PAYMENT_INCORRECT` | Alıcı | Eksik/fazla/yanlış ödeme |
| `LATE_PAYMENT_REFUNDED` | Alıcı | Gecikmeli ödeme iade edildi |
| `ITEM_RETURNED` | Satıcı | İptal/timeout sonrası item iade edildi |
| `PAYMENT_REFUNDED` | Alıcı | İptal/timeout sonrası ödeme iade edildi |
| `DISPUTE_RESULT` | Alıcı | Dispute sonucu |
| `FLAG_RESOLVED` | Satıcı | Flag sonuçlandı (onay veya red) |
| `ADMIN_FLAG_ALERT` | Admin | Flag'lenmiş işlem |
| `ADMIN_ESCALATION` | Admin | Yeni dispute eskalasyonu |
| `ADMIN_PAYMENT_FAILURE` | Admin | Satıcıya ödeme gönderim hatası (tekrarlayan) |
| `ADMIN_STEAM_BOT_ISSUE` | Admin | Platform Steam hesabı sorunu |

### 2.14 NotificationChannel

| Değer | Açıklama |
|-------|----------|
| `EMAIL` | Email bildirimi |
| `TELEGRAM` | Telegram bot |
| `DISCORD` | Discord bot |

> **Not:** Platform içi bildirim her zaman aktiftir ve kapatılamaz. Bu enum sadece opsiyonel dış kanalları tanımlar.

### 2.15 PlatformSteamBotStatus

| Değer | Açıklama |
|-------|----------|
| `ACTIVE` | Aktif, kullanılabilir |
| `RESTRICTED` | Steam tarafından kısıtlandı |
| `BANNED` | Steam tarafından banlandı |
| `OFFLINE` | Çevrimdışı, bağlantı yok |

### 2.16 MonitoringStatus

| Değer | Açıklama |
|-------|----------|
| `ACTIVE` | İşlem aktif — sürekli izleme (3 sn polling) |
| `POST_CANCEL_24H` | İptal sonrası ilk 24 saat (30 sn polling) |
| `POST_CANCEL_7D` | 1-7 gün arası (5 dk polling) |
| `POST_CANCEL_30D` | 7-30 gün arası (1 saat polling) |
| `STOPPED` | İzleme durduruldu (30 gün sonra) |

> **Kaynak:** 05 §3.3 — gecikmeli ödeme izleme takvimi.

### 2.17 OutboxMessageStatus

| Değer | Açıklama |
|-------|----------|
| `PENDING` | İşlenmemiş, dispatcher tarafından alınmayı bekliyor |
| `PROCESSED` | Başarıyla işlendi |
| `DEFERRED` | Geçici hata sonrası ertelendi — arka plan job'ı ile artan aralıklarla (30dk, 1sa, 4sa) yeniden denenecek. Email gönderim senaryosunda provider geçici olarak erişilemezse kullanılır (08 §4.3). |
| `FAILED` | Kalıcı hata — retry yapılmaz (422, email.failed, email.suppressed vb.) |

### 2.18 ActorType

| Değer | Açıklama |
|-------|----------|
| `USER` | Son kullanıcı (satıcı veya alıcı) |
| `SYSTEM` | Platform otomatik aksiyonu |
| `ADMIN` | Admin aksiyonu |

### 2.19 AuditAction

| Değer | Grup | Açıklama |
|-------|------|----------|
| `WALLET_DEPOSIT` | Fon | Platform adresine fon girişi (alıcı ödemesi) |
| `WALLET_WITHDRAW` | Fon | Platform adresinden fon çıkışı (satıcıya ödeme) |
| `WALLET_ESCROW_LOCK` | Fon | Escrow için fon kilitleme |
| `WALLET_ESCROW_RELEASE` | Fon | Satıcıya fon serbest bırakma |
| `WALLET_REFUND` | Fon | Alıcıya iade |
| `DISPUTE_RESOLVED` | Admin | Admin dispute çözümü |
| `MANUAL_REFUND` | Admin | Admin manual iade |
| `REFUND_BLOCKED` | Admin | Min iade eşiğinin altında kalan iade bloklandı — admin alert (09 §14.4, T53) |
| `USER_BANNED` | Admin | Kullanıcı engelleme |
| `USER_UNBANNED` | Admin | Kullanıcı engel kaldırma |
| `ROLE_CHANGED` | Admin | Kullanıcıya rol atama/kaldırma |
| `SYSTEM_SETTING_CHANGED` | Admin | Sistem parametresi değişikliği |
| `WALLET_ADDRESS_CHANGED` | Güvenlik | Kullanıcı cüzdan adresi değişikliği (payout veya refund) |

### 2.20 TimeoutFreezeReason

| Değer | Açıklama |
|-------|----------|
| `MAINTENANCE` | Planlı bakım penceresi |
| `STEAM_OUTAGE` | Steam API kesintisi |
| `BLOCKCHAIN_DEGRADATION` | Blockchain ağ performans düşüşü |
| `EMERGENCY_HOLD` | Admin tarafından acil durdurma |

> **Kaynak:** 02 §23, 05 §4.4 — downtime yönetimi. Transaction.TimeoutFreezeReason field'ında kullanılır.

### 2.21 FraudFlagScope

| Değer | Açıklama |
|-------|----------|
| `ACCOUNT_LEVEL` | Hesap seviyesi flag — mevcut işlemler etkilenmez, yeni işlem oluşturma engellenir |
| `TRANSACTION_PRE_CREATE` | İşlem seviyesi flag — Transaction kaydı FLAGGED state'inde oluşturulur, CREATED state'ine geçiş admin onayına bağlıdır (03 §7). "Pre-create" ifadesi CREATED state öncesini ifade eder, Transaction kaydının yokluğunu değil. TransactionId NOT NULL — flag'lenen işlem mevcuttur |

> **Kaynak:** 02 §14.0 — fraud detection kapsamı. FraudFlag.Scope field'ında kullanılır.

### 2.22 PayoutIssueStatus

| Değer | Açıklama |
|-------|----------|
| `REPORTED` | Satıcı sorunu bildirdi |
| `VERIFYING` | Sistem blockchain üzerinde doğrulama yapıyor |
| `RETRY_SCHEDULED` | Yeniden ödeme planlandı |
| `ESCALATED` | Admin'e eskale edildi |
| `RESOLVED` | Sorun çözüldü |

> **State geçişleri:** `REPORTED → VERIFYING → RETRY_SCHEDULED → RESOLVED` (başarılı retry) veya `REPORTED → VERIFYING → ESCALATED → RESOLVED` (admin müdahalesi). `RETRY_SCHEDULED` durumundan maksimum retry aşıldığında otomatik `ESCALATED`'a geçilir.
>
> **Kaynak:** 02 §10.3 — satıcı payout sorun bildirimi. SellerPayoutIssue.VerificationStatus field'ında kullanılır.

### 2.23 DeliveryStatus

| Değer | Açıklama |
|-------|----------|
| `PENDING` | Gönderim kuyruğunda, henüz denenmedi |
| `SENT` | Başarıyla gönderildi |
| `FAILED` | Maksimum retry sonrası başarısız |

> **Kaynak:** 02 §18 — dış kanal bildirimleri. NotificationDelivery.Status field'ında kullanılır.

---

## 3. Entity Tanımları

> **Tip kısaltmaları:** `guid` = uniqueidentifier, `string(N)` = nvarchar(N), `text` = nvarchar(max), `decimal(P,S)` = decimal(P,S), `int` = int, `long` = bigint, `bool` = bit, `datetime` = datetime2.

### 3.1 User

Kullanıcı profili, Steam kimliği, cüzdan adresleri ve itibar bilgileri.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `SteamId` | string(20) | UNIQUE, NOT NULL | Steam 64-bit ID |
| `SteamDisplayName` | string(100) | NOT NULL | Steam profil adı |
| `SteamAvatarUrl` | string(500) | NULL | Profil fotoğrafı URL |
| `DefaultPayoutAddress` | string(50) | NULL | Varsayılan satıcı cüzdan adresi (TRC-20) |
| `DefaultRefundAddress` | string(50) | NULL | Varsayılan alıcı iade adresi (TRC-20) |
| `PayoutAddressChangedAt` | datetime | NULL | Satıcı ödeme adresinin son değişiklik zamanı — `wallet.payout_address_cooldown_hours` parametresiyle birlikte cooldown penceresini tanımlar (02 §12.3, 03 §9.2). İlk tanımlamada da güncellenir |
| `RefundAddressChangedAt` | datetime | NULL | Alıcı iade adresinin son değişiklik zamanı — `wallet.refund_address_cooldown_hours` parametresiyle birlikte cooldown penceresini tanımlar (02 §12.3, 03 §9.2). İlk tanımlamada da güncellenir |
| `Email` | string(256) | NULL | Kullanıcının profil email adresi — iletişim ve hesap kurtarma amaçlı. **Email bildirim gönderimi için tek otorite `UserNotificationPreference` tablosudur** (EMAIL kanalı, ExternalId field'ı). Bu alan profil bilgisi olarak saklanır; gönderim kararı ve doğrulama durumu preference tablosundan okunur. Kullanıcı email adresini değiştirdiğinde her iki tablo da güncellenir — senkronizasyon uygulama katmanında sağlanır |
| `PreferredLanguage` | string(5) | NOT NULL, DEFAULT 'en' | UI dili (en, zh, es, tr) |
| `TosAcceptedVersion` | string(20) | NULL | Kabul edilen ToS versiyonu |
| `TosAcceptedAt` | datetime | NULL | ToS kabul tarihi |
| `MobileAuthenticatorVerified` | bool | NOT NULL, DEFAULT 0 | Steam Mobile Auth durumu |
| `CompletedTransactionCount` | int | NOT NULL, DEFAULT 0 | Tamamlanan işlem sayısı (denormalized) |
| `SuccessfulTransactionRate` | decimal(5,4) | NULL | Başarılı işlem oranı (denormalized, ör: 0.9500 = %95). Formül aşağıda |
| `CooldownExpiresAt` | datetime | NULL | İptal sonrası geçici yasak bitiş zamanı |
| `IsDeactivated` | bool | NOT NULL, DEFAULT 0 | Hesap deaktif mi |
| `DeactivatedAt` | datetime | NULL | Deaktif edilme zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | Hesap oluşturulma zamanı |
| `UpdatedAt` | datetime | NOT NULL | Son güncelleme zamanı |

> **İtibar skoru:** `CompletedTransactionCount`, `SuccessfulTransactionRate` ve `CreatedAt` (hesap yaşı) alanlarından oluşur. Denormalized field'lar işlem tamamlandığında veya iptal olduğunda güncellenir.
>
> **SuccessfulTransactionRate formülü:**
> ```
> completed / (completed + CANCELLED_SELLER + CANCELLED_BUYER + CANCELLED_TIMEOUT)
> ```
> - CANCELLED_ADMIN paydaya dahil **değildir** — tamamen platform kararı, kullanıcının kontrolünde değil.
> - **Sorumluluk prensibi:** İptal sadece sorumlu tarafın skorunu etkiler:
>   - `CANCELLED_SELLER` → sadece satıcının paydasına eklenir
>   - `CANCELLED_BUYER` → sadece alıcının paydasına eklenir
>   - `CANCELLED_TIMEOUT` → timeout'un düştüğü adıma göre sorumlu taraf belirlenir:
>     - Alıcı kabul timeout'u (adım 2) → alıcı
>     - Satıcı trade offer timeout'u (adım 3) → satıcı
>     - Ödeme timeout'u (adım 4) → alıcı
>     - Teslim trade offer timeout'u (adım 6) → alıcı
>
> **Composite reputationScore formülü (02 §13):**
> ```
> reputationScore =
>   IF (accountAgeDays < reputation.min_account_age_days)
>      OR (CompletedTransactionCount < reputation.min_completed_transactions)
>      OR (SuccessfulTransactionRate IS NULL)
>     → null
>   ELSE
>     → ROUND(SuccessfulTransactionRate × 5, 1)
> ```
> - **Tip:** `decimal(2,1)` aralık `[0.0, 5.0]`, 1 ondalık basamak. API kontratında nullable (`reputationScore: 4.8 | null`).
> - **Yuvarlama:** `MidpointRounding.ToZero` (8.3 §8.3 finansal hesaplama kuralıyla uyumlu — kesme/truncation, sıfıra doğru yuvarla). Örnek: `0.964 × 5 = 4.82 → 4.8`.
> - **Eşik kaynakları (SystemSetting):**
>   - `reputation.min_account_age_days` — int, default `30`, kategori `reputation`. Yeni hesap koruması: hesap yaşı eşiği.
>   - `reputation.min_completed_transactions` — int, default `3`, kategori `reputation`. İstatistiksel anlamlılık: minimum işlem sayısı.
> - **Hesaplama yeri:** Composite skor **denormalized değildir** — User entity'sinde alan olarak tutulmaz. Read path'te (`UserProfileService` ve diğer DTO mapper'lar) `CompletedTransactionCount`, `SuccessfulTransactionRate` ve `CreatedAt` üzerinden runtime hesaplanır. Sebep: eşikler değişebilir (admin SystemSetting), denormalized değer drift'e girer.
> - **Wash trading etkisi:** §3.1 wash trading penceresi `SuccessfulTransactionRate` paydasını etkiler — composite skor zincirleme bu filtreyi devralır (ek mantık gerekmez).

> **Örnek hesaplamalar:**
>
> | Senaryo | CompletedTx | rate | accountAge | reputationScore |
> |---|---|---|---|---|
> | Tipik aktif kullanıcı | 24 | 0.9600 | 6 ay | 4.8 |
> | Mükemmel kullanıcı | 50 | 1.0000 | 1 yıl | 5.0 |
> | Yeni başlayan | 5 | 0.8000 | 3 ay | 4.0 |
> | Az işlem | 2 | 1.0000 | 1 yıl | null (CompletedTx < 3) |
> | Yeni hesap | 5 | 1.0000 | 10 gün | null (accountAge < 30 gün) |
> | Hiç işlem yok | 0 | NULL | 6 ay | null (rate NULL) |
> | Düşük başarı | 10 | 0.5000 | 1 yıl | 2.5 |

### 3.2 UserLoginLog

IP ve cihaz parmak izi kaydı — çoklu hesap tespiti ve güvenlik audit'i için.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | long | PK, IDENTITY | |
| `UserId` | guid | FK → User, NOT NULL | |
| `IpAddress` | string(45) | NOT NULL | IPv4 veya IPv6 |
| `DeviceFingerprint` | string(256) | NULL | Cihaz parmak izi hash'i |
| `UserAgent` | string(500) | NULL | Browser user agent |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | Login zamanı |

### 3.3 RefreshToken

JWT refresh token — session yönetimi ve token rotation.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `UserId` | guid | FK → User, NOT NULL | |
| `Token` | string(256) | UNIQUE, NOT NULL | Refresh token'ın SHA-256 hash'i — plain text saklanmaz, DB breach'e karşı koruma |
| `ExpiresAt` | datetime | NOT NULL | Geçerlilik süresi |
| `IsRevoked` | bool | NOT NULL, DEFAULT 0 | İptal edildi mi |
| `RevokedAt` | datetime | NULL | İptal zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `ReplacedByTokenId` | guid | FK → RefreshToken (self), NULL | Token rotation — yeni token'ın ID'si |
| `DeviceInfo` | string(256) | NULL | Cihaz bilgisi |
| `IpAddress` | string(45) | NULL | Login IP adresi |
| `CreatedAt` | datetime | NOT NULL | Oluşturulma zamanı |

### 3.4 UserNotificationPreference

Kullanıcının bildirim kanalı tercihleri ve dış hesap bağlantıları.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `UserId` | guid | FK → User, NOT NULL | |
| `Channel` | int | NOT NULL | Enum: NotificationChannel |
| `IsEnabled` | bool | NOT NULL, DEFAULT 0 | Kanal aktif mi |
| `ExternalId` | string(256) | NULL | Email adresi, Telegram chat ID, Discord user ID |
| `VerifiedAt` | datetime | NULL | Bağlantı doğrulama zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |
| `UpdatedAt` | datetime | NOT NULL | |

> **Kısıt:** UserId + Channel çifti unique olmalı (aktif kayıtlar arasında — `WHERE IsDeleted = 0`).
>
> **Hesaplar arası sahiplik invariantı:** Aynı dış kanal hedefi (Channel + ExternalId) aktif kayıtlar arasında yalnızca tek hesaba bağlı olabilir — `UNIQUE(Channel, ExternalId) WHERE IsDeleted = 0 AND ExternalId IS NOT NULL`. Bu kural: (1) aynı email/Telegram/Discord hedefinin birden fazla hesabın bildirimini almasını önler, (2) verification/unlink akışlarında "bu kanal kime ait?" sorusunu tek cevaba indirir, (3) multi-account abuse tespitinde güçlü bir sinyal sağlar. Kullanıcı dış kanalını başka hesaba bağlamak isterse önce mevcut hesaptaki bağlantıyı kaldırmalıdır.

---

### 3.5 Transaction

İşlem yaşam döngüsünün merkezi entity'si. Item snapshot, fiyat, durum, taraflar ve timeout bilgilerini içerir.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| **Kimlik** | | | |
| `Id` | guid | PK | |
| `Status` | int | NOT NULL | Enum: TransactionStatus |
| **Taraflar** | | | |
| `SellerId` | guid | FK → User, NOT NULL | Satıcı |
| `BuyerId` | guid | FK → User, NULL | Alıcı — kabul edilene kadar null |
| `BuyerIdentificationMethod` | int | NOT NULL | Enum: BuyerIdentificationMethod |
| `TargetBuyerSteamId` | string(20) | NULL | Yöntem 1: Hedef alıcının Steam ID'si |
| `InviteToken` | string(64) | NULL, UNIQUE | Yöntem 2: Açık link token'ı |
| **Item Snapshot** | | | |
| `ItemAssetId` | string(20) | NOT NULL | Satıcının orijinal Steam asset ID'si — işlem oluşturma anında snapshot |
| `ItemClassId` | string(20) | NOT NULL | Steam class ID |
| `ItemInstanceId` | string(20) | NULL | Steam instance ID |
| `ItemName` | string(256) | NOT NULL | Item adı |
| `ItemIconUrl` | string(500) | NULL | Item görseli URL |
| `ItemExterior` | string(50) | NULL | Wear durumu (Factory New vb.) |
| `ItemType` | string(100) | NULL | Item türü (Rifle, Knife vb.) |
| `ItemInspectLink` | string(500) | NULL | CS2 inspect linki |
| **Item Asset Lineage** | | | |
| `EscrowBotAssetId` | string(20) | NULL | Bot envanterindeki asset ID — trade sonrası Steam yeni ID atar. ITEM_ESCROWED ve sonrası NOT NULL |
| `DeliveredBuyerAssetId` | string(20) | NULL | Alıcıya teslim sonrası asset ID — ITEM_DELIVERED ve sonrası NOT NULL |
| **Fiyat & Komisyon** | | | |
| `StablecoinType` | int | NOT NULL | Enum: StablecoinType |
| `Price` | decimal(18,6) | NOT NULL | Satıcının belirlediği fiyat |
| `CommissionRate` | decimal(5,4) | NOT NULL | Oluşturma anındaki komisyon oranı (snapshot) |
| `CommissionAmount` | decimal(18,6) | NOT NULL | ROUND(Price × CommissionRate, 6, ToZero) |
| `TotalAmount` | decimal(18,6) | NOT NULL | Price + CommissionAmount (rounding sonrası toplam) |
| `MarketPriceAtCreation` | decimal(18,6) | NULL | Oluşturma anındaki piyasa fiyatı (fraud kontrolü için) |
| **Cüzdan Adresleri (Snapshot)** | | | |
| `SellerPayoutAddress` | string(50) | NOT NULL | Satıcının cüzdan adresi — oluşturma anında sabitlenir |
| `BuyerRefundAddress` | string(50) | NULL | Alıcının iade adresi — kabul anında sabitlenir |
| **Timeout** | | | |
| `PaymentTimeoutMinutes` | int | NOT NULL | Satıcının seçtiği ödeme timeout süresi |
| `AcceptDeadline` | datetime | NULL | Alıcı kabul son tarihi |
| `TradeOfferToSellerDeadline` | datetime | NULL | Satıcı trade offer son tarihi |
| `PaymentDeadline` | datetime | NULL | Ödeme son tarihi |
| `TradeOfferToBuyerDeadline` | datetime | NULL | Alıcı trade offer son tarihi |
| `TimeoutFrozenAt` | datetime | NULL | Bakım/kesinti/blockchain degradasyonu — dondurma başlangıcı (null = aktif) |
| `TimeoutFreezeReason` | int | NULL | Enum: TimeoutFreezeReason (MAINTENANCE, STEAM_OUTAGE, BLOCKCHAIN_DEGRADATION, EMERGENCY_HOLD) |
| `TimeoutRemainingSeconds` | int | NULL | Freeze anında kalan süre (saniye). Resume'da bu süre ile yeni job schedule edilir (05 §4.4) |
| **Emergency Hold** | | | |
| `IsOnHold` | bit | NOT NULL, DEFAULT 0 | Emergency hold aktif mi — state değişmez, flag olarak çalışır (05 §4.5) |
| `EmergencyHoldAt` | datetime | NULL | Admin emergency hold başlangıcı (null = hold yok) |
| `EmergencyHoldReason` | string(500) | NULL | Hold sebebi (zorunlu) |
| `EmergencyHoldByAdminId` | guid | FK → User, NULL | Hold uygulayan admin |
| `PreviousStatusBeforeHold` | int | NULL | Hold uygulandığı andaki state (audit amaçlı). Flag-based modelde status değişmediği için operasyonel olarak kullanılmaz, ancak hold geçmişi için kayıt tutar (05 §4.5) |

> **Emergency hold ve timeout freeze ilişkisi (05 §4.5):** Emergency hold, timeout freeze mekanizmasının özel bir türüdür. Hold uygulandığında `TimeoutFreezeReason = EMERGENCY_HOLD` set edilir ve standart freeze akışı işler (timeout durur, `TimeoutRemainingSeconds` kaydedilir). Ek olarak `IsOnHold = true` flag'i state machine guard'ını ve kullanıcı aksiyonlarını engeller — bu, sıradan freeze'in yapmadığı ek katmandır. Hold kaldırıldığında `IsOnHold = false` yapılır ve standart freeze resume mekanizması devreye girer. Ayrı Emergency Hold field'ları (`EmergencyHoldAt`, `EmergencyHoldReason`, `EmergencyHoldByAdminId`, `PreviousStatusBeforeHold`) admin audit ve operasyonel ihtiyaçlar için tutulur.
| `PaymentTimeoutJobId` | string(50) | NULL | Hangfire ödeme timeout job ID'si — iptal/freeze'de silinir (09 §13.3) |
| `TimeoutWarningJobId` | string(50) | NULL | Hangfire timeout uyarı job ID'si — **yalnızca ITEM_ESCROWED (ödeme aşaması)** için geçerlidir. Diğer aşamalar poller ile çalışır, ayrı warning job kullanmaz. İptal/freeze'de silinir (09 §13.3) |
| `TimeoutWarningSentAt` | datetime | NULL | Timeout uyarısı gönderildi mi — **yalnızca ITEM_ESCROWED (ödeme aşaması)** için geçerlidir. Çift uyarı engeli (09 §13.3). **Reset kuralı:** ITEM_ESCROWED'a her girişte (ilk giriş veya freeze resume sonrası) NULL'a resetlenir — önceki aşamadan kalma stale değer taşınmaz |
| **İptal** | | | |
| `CancelledBy` | int | NULL | Enum: CancelledByType |
| `CancelReason` | string(500) | NULL | İptal sebebi (zorunlu) |
| **Dispute** | | | |
| `HasActiveDispute` | bool | NOT NULL, DEFAULT 0 | Aktif dispute var mı |
| **Steam Bot** | | | |
| `EscrowBotId` | guid | FK → PlatformSteamBot, NULL | Item'ı tutan bot — escrow sonrası atanır |
| **Concurrency** | | | |
| `RowVersion` | byte[] | NOT NULL, ROWVERSION | Optimistic concurrency token — EF Core `IsRowVersion()` |
| **Zaman Damgaları** | | | |
| `CreatedAt` | datetime | NOT NULL | İşlem oluşturulma |
| `AcceptedAt` | datetime | NULL | Alıcı kabul zamanı |
| `ItemEscrowedAt` | datetime | NULL | Item emanet zamanı |
| `PaymentReceivedAt` | datetime | NULL | Ödeme doğrulama zamanı |
| `ItemDeliveredAt` | datetime | NULL | Item teslim zamanı |
| `CompletedAt` | datetime | NULL | İşlem tamamlanma zamanı |
| `UpdatedAt` | datetime | NOT NULL | Son güncelleme zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CancelledAt` | datetime | NULL | İptal zamanı |

> **State-dependent CHECK constraint'ler:**
> - **İptal state'lerinde** (`Status IN (CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN)`): `CancelledBy NOT NULL`, `CancelReason NOT NULL`, `CancelledAt NOT NULL`.
> - **Emergency hold aktifken** (`IsOnHold = 1`): `EmergencyHoldAt NOT NULL`, `EmergencyHoldReason NOT NULL`, `EmergencyHoldByAdminId NOT NULL`.
> - **Timeout freeze aktifken** (`TimeoutFrozenAt IS NOT NULL`): `TimeoutFreezeReason NOT NULL`, `TimeoutRemainingSeconds NOT NULL`.
> - **Timeout freeze pasifken** (`TimeoutFrozenAt IS NULL`): `TimeoutFreezeReason IS NULL`, `TimeoutRemainingSeconds IS NULL` — yarım freeze kaydı engellenr.
> - **Freeze-hold karşılıklı bağıntı:** `TimeoutFreezeReason = EMERGENCY_HOLD` ise `IsOnHold = 1` zorunlu. `IsOnHold = 1` ise `TimeoutFrozenAt NOT NULL` ve `TimeoutFreezeReason = EMERGENCY_HOLD` zorunlu — emergency hold her zaman freeze mekanizması üzerinden çalışır.
>
> - **Alıcı belirleme yöntemi** (`BuyerIdentificationMethod`): `STEAM_ID` ise `TargetBuyerSteamId NOT NULL` ve `InviteToken NULL`; `OPEN_LINK` ise `InviteToken NOT NULL` ve `TargetBuyerSteamId NULL`.
> - **Status → zorunlu field matrisi** (explicit status set'leri — ordinal karşılaştırma **yasaktır**):
>
>   CANCELLED_* state'leri farklı aşamalardan gelebilir; iptal öncesi hangi milestone'lara ulaşıldıysa o field'lar dolu kalır ama iptal sonrası milestone'lar NULL'dır. Bu matriste her status için gerçekten zorunlu olan field'lar listelenir:
>
>   | Field | Zorunlu olduğu status'ler |
>   |-------|--------------------------|
>   | `BuyerId` | ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `BuyerRefundAddress` | ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `AcceptedAt` | ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `ItemEscrowedAt` | ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `EscrowBotAssetId` | ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `PaymentReceivedAt` | PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED |
>   | `ItemDeliveredAt` | ITEM_DELIVERED, COMPLETED |
>   | `DeliveredBuyerAssetId` | ITEM_DELIVERED, COMPLETED |
>   | `CompletedAt` | COMPLETED |
>
>   **CANCELLED_* kuralı:** İptal state'lerinde yukarıdaki field'lar iptalden önceki milestone'a göre kümülatif kalır. Örneğin ITEM_ESCROWED'dan CANCELLED_SELLER'a geçişte AcceptedAt, ItemEscrowedAt, EscrowBotAssetId dolu kalır; PaymentReceivedAt ve sonrası NULL'dır. Bu kural uygulama katmanında state machine guard'ı tarafından korunur — DB CHECK ile enforce edilmez (CANCELLED_* öncesi state bilgisi gerektirir).
>
>   **FLAGGED:** Tüm milestone field'ları NULL (BuyerId dahil — henüz alıcı kabul etmemiş olabilir).
> - **FLAGGED state kuralları (03 §7):** FLAGGED state'inde işlem admin onayı bekler, timeout motoru çalışmaz. Tüm deadline field'ları (`AcceptDeadline`, `TradeOfferToSellerDeadline`, `PaymentDeadline`, `TradeOfferToBuyerDeadline`) NULL kalır. Hangfire job'ları oluşturulmaz (`PaymentTimeoutJobId = NULL`, `TimeoutWarningJobId = NULL`). `CreatedAt` kayıt oluşturma anını (FLAGGED'a giriş) temsil eder. Admin onayı geldiğinde CREATED state'ine geçilir ve deadline/job initialization o anda yapılır.
> - **State → aktif deadline/job matrisi** (normatif kural — freeze ve FLAGGED istisna):
>
>   | Status | Aktif Deadline | Hangfire Job |
>   |--------|---------------|-------------|
>   | FLAGGED | — (tümü NULL) | — (tümü NULL) |
>   | CREATED | `AcceptDeadline NOT NULL` | — (opsiyonel, alıcı kabul bekleniyor) |
>   | ACCEPTED | `TradeOfferToSellerDeadline NOT NULL` | — |
>   | TRADE_OFFER_SENT_TO_SELLER | `TradeOfferToSellerDeadline NOT NULL` | — |
>   | ITEM_ESCROWED | `PaymentDeadline NOT NULL` | `PaymentTimeoutJobId NOT NULL` |
>   | PAYMENT_RECEIVED | `TradeOfferToBuyerDeadline NOT NULL` | — |
>   | TRADE_OFFER_SENT_TO_BUYER | `TradeOfferToBuyerDeadline NOT NULL` | — |
>   | Terminal state'ler (COMPLETED, CANCELLED_*) | — (tümü consumed) | — (tümü NULL) |
>
>   Freeze aktifken deadline'lar son değerlerinde kalır, job'lar iptal edilir. Bu matris uygulama katmanında state machine guard'ı tarafından zorunlu kılınır; DB CHECK ile tam enforce edilmesi pratik değildir (state sayısı × deadline kombinasyonu) ancak normatif referans olarak geçerlidir.
>
>   **Timeout enforcement mekanizması:** Yalnızca **ödeme aşaması** (ITEM_ESCROWED) per-transaction Hangfire delayed job ile yönetilir (`PaymentTimeoutJobId`, `TimeoutWarningJobId`). Diğer aşamaların deadline'ları (AcceptDeadline, TradeOfferToSellerDeadline, TradeOfferToBuyerDeadline) **periyodik scanner/poller job** ile enforce edilir — deadline geçmiş ama state ilerlememiş transaction'lar tespit edilip timeout tetiklenir. Ödeme aşamasının ayrıcalıklı olmasının sebebi: kullanıcının ödeme yapma niyetini ve uyarı zamanlamasını hassas kontrol etme ihtiyacı (09 §13.3). Diğer aşamalarda dakika hassasiyetinde poller yeterlidir.
>
>   **Warning field reset kuralı:** `TimeoutWarningJobId` ve `TimeoutWarningSentAt` yalnızca ITEM_ESCROWED aşamasında anlamlıdır. State geçişlerinde bu alanların davranışı: (1) ITEM_ESCROWED'a girişte her ikisi de initialize edilir (job schedule, SentAt = NULL), (2) ITEM_ESCROWED'dan çıkışta (PAYMENT_RECEIVED veya CANCELLED_*) job iptal edilir ve her iki alan NULL'a döner, (3) freeze resume sonrası yeni job schedule edilir ve SentAt NULL'a resetlenir. Diğer state'lerde bu alanlar NULL olmalıdır — state→deadline matrisindeki Hangfire Job sütunu bunu yansıtır.
>
> Bu constraint'ler veri bütünlüğünü DB seviyesinde garanti eder — uygulama katmanı kontrolüne ek güvence sağlar.

### 3.6 TransactionHistory

Her state geçişinin tam kaydı — audit trail.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | long | PK, IDENTITY | |
| `TransactionId` | guid | FK → Transaction, NOT NULL | |
| `PreviousStatus` | int | NULL | Önceki durum (ilk kayıtta null) |
| `NewStatus` | int | NOT NULL | Yeni durum |
| `Trigger` | string(100) | NOT NULL | Tetikleyici (ör: "BuyerAccepted", "TimeoutExpired") |
| `ActorType` | int | NOT NULL | Enum: ActorType |
| `ActorId` | guid | NOT NULL | Aksiyonu tetikleyen kullanıcı/admin/system ID — SYSTEM aksiyonlarında §8.9 sentinel GUID kullanılır |
| `AdditionalData` | text | NULL | JSON — ek bağlam bilgisi |
| `CreatedAt` | datetime | NOT NULL | Geçiş zamanı |

> **Kaynak:** 05 §5.4 — "Önceki durum, yeni durum, trigger, timestamp, tetikleyen aktör, ek veri."
>
> **Silme politikası:** Append-Only (Arşivlenebilir) — INSERT sonrası UPDATE/DELETE tanımlı değil. Transaction archive set ile birlikte archive tabloya taşınabilir (§8.8).

---

### 3.7 PaymentAddress

Her işlem için üretilen benzersiz blockchain ödeme adresi ve izleme durumu.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, UNIQUE, NOT NULL | 1:1 ilişki |
| `Address` | string(50) | UNIQUE, NOT NULL | Tron (TRC-20) adresi |
| `HdWalletIndex` | int | UNIQUE, NOT NULL | BIP-44 derivation index — monoton artan, asla reuse edilmez |
| `ExpectedAmount` | decimal(18,6) | NOT NULL | Beklenen toplam tutar (fiyat + komisyon) |
| `ExpectedToken` | int | NOT NULL | Enum: StablecoinType |
| `MonitoringStatus` | int | NOT NULL | Enum: MonitoringStatus |
| `MonitoringExpiresAt` | datetime | NULL | İzleme bitiş zamanı (ACTIVE durumda null) |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |

### 3.8 BlockchainTransaction

Tüm blockchain transferlerinin kaydı — gelen ödemeler, iadeler ve satıcı payoutları.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, NOT NULL | |
| `PaymentAddressId` | guid | FK → PaymentAddress, NULL | Gelen ödemelerde ilgili adres |
| `Type` | int | NOT NULL | Enum: BlockchainTransactionType |
| `TxHash` | string(100) | NULL, UNIQUE | Blockchain transaction hash (broadcast sonrası) |
| `FromAddress` | string(50) | NOT NULL | Gönderen adres |
| `ToAddress` | string(50) | NOT NULL | Alıcı adres |
| `Amount` | decimal(18,6) | NOT NULL | Transfer tutarı |
| `Token` | int | NOT NULL | Enum: StablecoinType |
| `ActualTokenAddress` | string(50) | NULL | Yanlış token senaryosunda gelen/refund edilen token'ın contract adresi. `WRONG_TOKEN_INCOMING` ve `WRONG_TOKEN_REFUND` türlerinde dolu |
| `GasFee` | decimal(18,6) | NULL | Gas fee tutarı (TRX) |
| `Status` | int | NOT NULL | Enum: BlockchainTransactionStatus |
| `BlockNumber` | long | NULL | Blockchain blok numarası |
| `ConfirmationCount` | int | NOT NULL, DEFAULT 0 | Onay sayısı |
| `RetryCount` | int | NOT NULL, DEFAULT 0 | Giden transfer yeniden deneme sayısı (SELLER_PAYOUT, BUYER_REFUND vb.) — 05 §3.3 retry stratejisi ile tutarlı |
| `ErrorMessage` | string(500) | NULL | Giden transfer hata durumunda mesaj |
| `CreatedAt` | datetime | NOT NULL | Tespit/oluşturma zamanı |
| `ConfirmedAt` | datetime | NULL | Onaylanma zamanı (≥ 20 blok) |

> **Token semantiği:** Token field'ı normal işlemlerde gerçek stablecoin türünü temsil eder. `WRONG_TOKEN_INCOMING` ve `WRONG_TOKEN_REFUND` kayıtlarında ise işlemin **beklenen** token'ını (USDT veya USDC) tutar; gerçekte gelen/refund edilen yanlış token'ın kimliği `ActualTokenAddress` field'ından takip edilir.
>
> **Silme politikası:** Workflow Record (Arşivlenebilir) — DELETE tanımlı değil; yaşam döngüsü boyunca Status, ConfirmationCount, RetryCount güncellenir. Terminal state sonrası frozen. Archive tabloya taşınabilir (§8.8).
>
> **Retry notu:** Giden transferlerde (SELLER_PAYOUT, BUYER_REFUND, EXCESS_REFUND, WRONG_TOKEN_REFUND, LATE_PAYMENT_REFUND, INCORRECT_AMOUNT_REFUND) başarısız olursa exponential backoff ile yeniden denenir (3 deneme: 1dk, 5dk, 15dk — 05 §3.3). RetryCount ve ErrorMessage bu süreci takip eder.
>
> **Type-dependent CHECK constraint'ler:**
> - **Gelen ödeme** (`BUYER_PAYMENT`): `PaymentAddressId NOT NULL`, `ActualTokenAddress NULL`.
> - **Wrong-token incoming** (`WRONG_TOKEN_INCOMING`): `ActualTokenAddress NOT NULL`, `PaymentAddressId NOT NULL` — yanlış token belirli bir ödeme adresine gelmiştir, reconciliation ve refund takibi için bağ zorunludur.
> - **Wrong-token refund** (`WRONG_TOKEN_REFUND`): `ActualTokenAddress NOT NULL`, `PaymentAddressId NULL` — giden refund transfer, ödeme adresine değil alıcının refund adresine gider.
> - **Spam-token incoming** (`SPAM_TOKEN_INCOMING`): `ActualTokenAddress NOT NULL`, `PaymentAddressId NOT NULL` — bilinmeyen token belirli bir ödeme adresine gelmiştir. Otomatik iade yapılmaz, yalnızca audit kaydı. Status doğrudan `CONFIRMED` olarak yazılır (terminal).
> - **Giden transfer** (`SELLER_PAYOUT`, `BUYER_REFUND`, `EXCESS_REFUND`, `LATE_PAYMENT_REFUND`, `INCORRECT_AMOUNT_REFUND`): `PaymentAddressId NULL`, `ActualTokenAddress NULL`.
>
> **Status-dependent CHECK constraint'ler:**
> - **CONFIRMED**: `ConfirmationCount >= 20`, `ConfirmedAt NOT NULL`.
> - **PENDING**: `ConfirmationCount < 20`.
> - **DETECTED**: `ConfirmationCount = 0`.
> - **FAILED**: `ConfirmedAt NULL` — başarısız transfer onaylanmış olamaz. `ConfirmationCount` son bilinen değerde kalabilir (kısmi ilerleme sonrası failure); sıfırlanmaz, audit amaçlı korunur.

---

### 3.8a SellerPayoutIssue

Satıcının payout sorununu bildirmesi ve çözüm takibi (02 §10.3).

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, NOT NULL | İlgili işlem |
| `SellerId` | guid | FK → User, NOT NULL | Bildiren satıcı |
| `Detail` | string(2000) | NOT NULL | Satıcının açıklaması |
| `PayoutTxHash` | string(100) | NULL | Doğrulanan blockchain tx hash |
| `VerificationStatus` | int | NOT NULL | Enum: PayoutIssueStatus — `REPORTED`, `VERIFYING`, `RETRY_SCHEDULED`, `ESCALATED`, `RESOLVED` |
| `RetryCount` | int | NOT NULL, DEFAULT 0 | Yeniden deneme sayısı |
| `EscalatedToAdminId` | guid | FK → User, NULL | Eskale edilen admin |
| `AdminNote` | string(2000) | NULL | Admin çözüm notu |
| `CreatedAt` | datetime | NOT NULL | Bildirim zamanı |
| `ResolvedAt` | datetime | NULL | Çözüm zamanı |

> **Silme politikası:** Workflow Record (Arşivlenebilir) — DELETE tanımlı değil; VerificationStatus, RetryCount, EscalatedToAdminId güncellenir. RESOLVED sonrası frozen. Archive tabloya taşınabilir (§8.8).
>
> **Tek aktif issue kuralı:** Bir transaction için aynı anda en fazla bir aktif (RESOLVED olmayan) SellerPayoutIssue olabilir. Filtered unique index: `UNIQUE(TransactionId) WHERE VerificationStatus != RESOLVED`. Yeni issue ancak önceki RESOLVED olduktan sonra açılabilir.
>
> **State-dependent CHECK constraint'ler:**
> - **ESCALATED**: `EscalatedToAdminId NOT NULL`.
> - **RESOLVED**: `ResolvedAt NOT NULL`.
> - **RETRY_SCHEDULED**: `RetryCount > 0`.

---

### 3.9 TradeOffer

Steam trade offer yaşam döngüsü takibi.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, NOT NULL | |
| `Direction` | int | NOT NULL | Enum: TradeOfferDirection |
| `SteamTradeOfferId` | string(20) | NULL | Steam tarafındaki trade offer ID |
| `PlatformSteamBotId` | guid | FK → PlatformSteamBot, NOT NULL | Gönderen bot |
| `Status` | int | NOT NULL | Enum: TradeOfferStatus |
| `RetryCount` | int | NOT NULL, DEFAULT 0 | Yeniden deneme sayısı |
| `ErrorMessage` | string(500) | NULL | Hata durumunda mesaj |
| `CreatedAt` | datetime | NOT NULL | |
| `SentAt` | datetime | NULL | Steam'de gönderilme zamanı |
| `RespondedAt` | datetime | NULL | Kabul/ret zamanı |

> **Silme politikası:** Workflow Record (Arşivlenebilir) — DELETE tanımlı değil; Status, RetryCount, SentAt, RespondedAt güncellenir. Terminal state (ACCEPTED/DECLINED/EXPIRED/FAILED) sonrası frozen. Archive tabloya taşınabilir (§8.8).
>
> **State-dependent CHECK constraint'ler:**
> - **SENT, ACCEPTED, DECLINED, EXPIRED**: `SentAt NOT NULL`.
> - **ACCEPTED, DECLINED, EXPIRED**: `RespondedAt NOT NULL`.
> - **FAILED**: `SentAt` zorunlu değil — pre-send aşamasında (Steam API'ye ulaşılamadan) başarısız olabilir. `SentAt NOT NULL` ise offer Steam'e iletilmiş ama sonrasında hata almış demektir.

---

### 3.10 PlatformSteamBot

Platform Steam bot hesapları ve durumları.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `SteamId` | string(20) | UNIQUE, NOT NULL | Bot'un Steam ID'si |
| `DisplayName` | string(100) | NOT NULL | Bot adı |
| `Status` | int | NOT NULL | Enum: PlatformSteamBotStatus |
| `ActiveEscrowCount` | int | NOT NULL, DEFAULT 0 | Aktif emanet item sayısı (denormalized) |
| `DailyTradeOfferCount` | int | NOT NULL, DEFAULT 0 | Günlük trade offer sayısı (denormalized) |
| `LastHealthCheckAt` | datetime | NULL | Son sağlık kontrolü zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |
| `UpdatedAt` | datetime | NOT NULL | |

> **Bot seçimi:** Yeni trade offer gönderirken `ActiveEscrowCount` en düşük olan aktif bot seçilir (capacity-based, 05 §3.2).

---

### 3.11 Dispute

Alıcı tarafından açılan itiraz kaydı.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, NOT NULL | |
| `OpenedByUserId` | guid | FK → User, NOT NULL | Sadece alıcı açabilir |
| `Type` | int | NOT NULL | Enum: DisputeType |
| `Status` | int | NOT NULL | Enum: DisputeStatus |
| `SystemCheckResult` | text | NULL | Otomatik kontrol sonucu (JSON) |
| `UserDescription` | string(2000) | NULL | Kullanıcının eskalasyon açıklaması |
| `AdminId` | guid | FK → User, NULL | İnceleyen admin |
| `AdminNote` | string(2000) | NULL | Admin notu |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |
| `ResolvedAt` | datetime | NULL | Çözüm zamanı |

> **Kural:** Bir işlem için aynı türde dispute tekrar açılamaz (02 §10.2). Kontrol: TransactionId + Type çifti unique — status'tan ve IsDeleted'dan bağımsız (unfiltered unique index). Bir dispute kapatıldıktan veya soft delete edildikten sonra da aynı türde yenisi açılamaz.
>
> **Çoklu aktif dispute:** 02 §10.2 yalnızca aynı türde tekrarı yasaklar; farklı türlerde eşzamanlı aktif dispute'lar (ör: PAYMENT + WRONG_ITEM) mümkündür. `Transaction.HasActiveDispute` boolean'ı en az bir dispute OPEN/ESCALATED olduğunda true, tümü CLOSED olduğunda false olur. Birden fazla aktif dispute'un admin kuyruğunda birlikte görünmesi beklenen davranıştır.
>
> **State-dependent CHECK constraint'ler:**
> - **CLOSED**: `ResolvedAt NOT NULL`.

### 3.12 FraudFlag

Fraud tespiti sonucu oluşturulan flag kaydı.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `TransactionId` | guid | FK → Transaction, NULL | İşlem bazlı flag (pre-create: işlem CREATED öncesi durdurulur) |
| `UserId` | guid | FK → User, NULL | Kullanıcı bazlı flag (hesap flag'i: yeni işlem engeli) |
| `Scope` | int | NOT NULL | Enum: FraudFlagScope — `ACCOUNT_LEVEL` (hesap flag'i, mevcut işlemler etkilenmez), `TRANSACTION_PRE_CREATE` (işlem flag'i, CREATED öncesi durdurulur) (02 §14.0) |
| `Type` | int | NOT NULL | Enum: FraudFlagType |
| `Details` | text | NOT NULL | JSON — flag detayları (piyasa fiyatı, sapma oranı vb.) |
| `Status` | int | NOT NULL | Enum: ReviewStatus |
| `ReviewedByAdminId` | guid | FK → User, NULL | İnceleyen admin |
| `AdminNote` | string(2000) | NULL | Admin notu |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |
| `ReviewedAt` | datetime | NULL | İnceleme zamanı |

> **Scope-based CHECK constraint:**
> - `ACCOUNT_LEVEL`: UserId NOT NULL, TransactionId NULL — hesap seviyesi flag, belirli bir işleme bağlı değil.
> - `TRANSACTION_PRE_CREATE`: UserId NOT NULL, TransactionId NOT NULL — flag'lenen işlem FLAGGED state'inde mevcut (03 §7). TransactionId flag'lenen Transaction kaydına referans verir.
>
> Her iki scope'ta da UserId zorunlu. TransactionId yalnızca TRANSACTION_PRE_CREATE'te dolu olmalı.
>
> **State-dependent CHECK constraint'ler:**
> - **APPROVED / REJECTED**: `ReviewedAt NOT NULL`, `ReviewedByAdminId NOT NULL`.

---

### 3.13 Notification

Platform içi bildirimler — kullanıcı dashboard'unda gösterilir.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `UserId` | guid | FK → User, NOT NULL | Bildirimi alan kullanıcı |
| `TransactionId` | guid | FK → Transaction, NULL | İlgili işlem (varsa) |
| `Type` | int | NOT NULL | Enum: NotificationType |
| `Title` | string(256) | NOT NULL | Lokalize edilmiş başlık |
| `Body` | string(2000) | NOT NULL | Lokalize edilmiş içerik |
| `IsRead` | bool | NOT NULL, DEFAULT 0 | Okundu mu |
| `ReadAt` | datetime | NULL | Okunma zamanı |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |

> **Dış kanallar (email, Telegram, Discord):** Notification entity'si sadece platform içi bildirimleri saklar. Dış kanal teslimat durumu NotificationDelivery entity'sinde kalıcı olarak takip edilir (aşağıda).

### 3.13a NotificationDelivery

Dış kanal bildirim teslimat kaydı — her Notification için her dış kanala gönderim girişimini izler.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `NotificationId` | guid | FK → Notification, NOT NULL | İlgili platform içi bildirim |
| `Channel` | int | NOT NULL | Enum: NotificationChannel (EMAIL, TELEGRAM, DISCORD) |
| `TargetExternalId` | string(256) | NOT NULL | Gönderim anındaki hedef (email adresi, chat ID vb.) — snapshot |
| `Status` | int | NOT NULL | Enum: DeliveryStatus — PENDING, SENT, FAILED |
| `AttemptCount` | int | NOT NULL, DEFAULT 0 | Deneme sayısı |
| `LastError` | string(2000) | NULL | Son hata mesajı |
| `CreatedAt` | datetime | NOT NULL | İlk gönderim girişimi |
| `SentAt` | datetime | NULL | Başarılı gönderim zamanı |

> **Silme politikası:** Workflow Record (Arşivlenebilir) — DELETE tanımlı değil; Status ve AttemptCount güncellenir. Terminal state (SENT/FAILED max retry sonrası) sonrası frozen. **Transaction-linked delivery:** Parent Notification transaction archive set ile arşivlendiğinde NotificationDelivery child'ları da birlikte taşınır (§8.8). **Bağımsız bildirim delivery:** Parent Notification'ın TransactionId = NULL ise, Notification retention-based hard purge edildiğinde bağlı NotificationDelivery kayıtları da aynı batch'te birlikte purge edilir — orphan row oluşmaz. Purge sırası: önce NotificationDelivery, sonra Notification (FK bağımlılık sırası).
>
> **Status-dependent CHECK constraint'ler:**
> - **SENT**: `SentAt NOT NULL`.
> - **FAILED**: `LastError NOT NULL`.
>
> **Retry:** Outbox event → Hangfire consumer → gönderim denemesi → NotificationDelivery güncelleme. Maksimum retry sonrası FAILED'da kalır, admin alert tetiklenir.

---

### 3.14 AdminRole

Admin rol tanımları.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `Name` | string(100) | UNIQUE, NOT NULL | Rol adı |
| `Description` | string(500) | NULL | Rol açıklaması |
| `IsSuperAdmin` | bool | NOT NULL, DEFAULT 0 | Süper admin rolü mü |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | |
| `UpdatedAt` | datetime | NOT NULL | |

### 3.15 AdminRolePermission

Rol başına yetki atamaları.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `AdminRoleId` | guid | FK → AdminRole, NOT NULL | |
| `Permission` | string(100) | NOT NULL | Yetki tanımlayıcı (ör: "transactions.view", "flags.review") |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |
| `CreatedAt` | datetime | NOT NULL | Oluşturulma zamanı |

> **Kısıt:** AdminRoleId + Permission çifti unique olmalı (aktif kayıtlar arasında — `WHERE IsDeleted = 0`).

### 3.16 AdminUserRole

Kullanıcı-rol eşlemesi (N:M).

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `UserId` | guid | FK → User, NOT NULL | |
| `AdminRoleId` | guid | FK → AdminRole, NOT NULL | |
| `AssignedAt` | datetime | NOT NULL | Atama zamanı |
| `AssignedByAdminId` | guid | FK → User, NULL | Atamayı yapan admin |
| `IsDeleted` | bool | NOT NULL, DEFAULT 0 | Soft delete flag |
| `DeletedAt` | datetime | NULL | Silinme zamanı |

> **Kısıt:** UserId + AdminRoleId çifti unique olmalı (aktif kayıtlar arasında — `WHERE IsDeleted = 0`). Composite PK yerine surrogate PK kullanılır, böylece soft delete sonrası aynı rol tekrar atanabilir.

### 3.17 SystemSetting

Admin tarafından yönetilen platform parametreleri.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `Key` | string(100) | UNIQUE, NOT NULL | Parametre anahtarı |
| `Value` | string(500) | NULL | Parametre değeri (string olarak saklanır). NULL = henüz yapılandırılmamış |
| `IsConfigured` | bool | NOT NULL, DEFAULT 0 | Admin tarafından yapılandırıldı mı |
| `DataType` | string(20) | NOT NULL, CHECK IN ('int', 'decimal', 'bool', 'string') | Veri tipi — yalnızca bu dört değer geçerlidir |
| `Category` | string(50) | NOT NULL | Kategori (Timeout, Commission, Limit, Fraud vb.) |
| `Description` | string(500) | NULL | Açıklama |
| `UpdatedAt` | datetime | NOT NULL | Son güncelleme |
| `UpdatedByAdminId` | guid | FK → User, NULL | Son güncelleyen admin |

> **Silme politikası:** Mutable Catalog (Delete Yasak) — silme tanımlı değildir (§1.3). Key seti seed ile oluşturulur ve yalnızca migration ile değişir. Admin yalnızca Value güncelleyebilir. Bu kısıtlama, startup fail-fast (§8.9) ve seed contract'ın bütünlüğünü korur.

**İki katmanlı doğrulama (startup + update):**
1. **Tip doğrulaması:** Value, DataType'a göre parse edilebilir olmalıdır (int → int.TryParse, decimal → decimal.TryParse vb.). Parse başarısız → kayıt geçersiz.
2. **Alan aralığı ve çapraz doğrulama:** Key bazlı min/max/range kuralları uygulanır:
   - Timeout süreleri: `> 0`
   - `payment_timeout_min_minutes < payment_timeout_max_minutes`
   - `commission_rate`: `0 < x < 1`
   - `timeout_warning_ratio`: `0 < x < 1`
   - Monitoring süreleri: `> 0`, mantıksal sıra (24h < 7d < 30d polling aralıkları)
3. **Uygulama noktaları:** Startup fail-fast (§8.9) hem `IsConfigured = false` hem doğrulama hatalarını yakalar. Admin güncelleme API'si de aynı doğrulamayı uygular — geçersiz değer kaydedilemez.

**Başlangıç parametreleri (02 §16.2):** Varsayılan sütununda "—" olan parametreler seed'de `Value = NULL, IsConfigured = false` olarak oluşturulur; lansman öncesi admin tarafından yapılandırılması zorunludur.

| Key | Category | DataType | Varsayılan | Açıklama |
|-----|----------|----------|------------|----------|
| `accept_timeout_minutes` | Timeout | int | — | Alıcı kabul timeout süresi |
| `trade_offer_seller_timeout_minutes` | Timeout | int | — | Satıcı trade offer timeout süresi |
| `payment_timeout_min_minutes` | Timeout | int | — | Ödeme timeout minimum |
| `payment_timeout_max_minutes` | Timeout | int | — | Ödeme timeout maksimum |
| `payment_timeout_default_minutes` | Timeout | int | — | Ödeme timeout varsayılan |
| `trade_offer_buyer_timeout_minutes` | Timeout | int | — | Alıcı trade offer timeout süresi |
| `timeout_warning_ratio` | Timeout | decimal | — | Uyarı gönderim oranı (ör: 0.75) |
| `commission_rate` | Commission | decimal | 0.02 | Komisyon oranı (%2) |
| `min_transaction_amount` | Limit | decimal | — | Minimum işlem tutarı |
| `max_transaction_amount` | Limit | decimal | — | Maksimum işlem tutarı |
| `max_concurrent_transactions` | Limit | int | — | Eşzamanlı aktif işlem limiti |
| `new_account_transaction_limit` | Limit | int | — | Yeni hesap işlem limiti |
| `new_account_period_days` | Limit | int | — | Kaç gün "yeni hesap" sayılır |
| `cancel_limit_count` | Limit | int | — | Belirli sürede izin verilen iptal sayısı |
| `cancel_limit_period_hours` | Limit | int | — | İptal limit periyodu |
| `cancel_cooldown_hours` | Limit | int | — | İptal sonrası cooldown süresi |
| `gas_fee_protection_ratio` | Commission | decimal | 0.10 | Gas fee koruma eşiği (%10) |
| `price_deviation_threshold` | Fraud | decimal | — | Piyasa fiyat sapma eşiği |
| `high_volume_amount_threshold` | Fraud | decimal | — | Yüksek hacim tutar eşiği |
| `high_volume_count_threshold` | Fraud | int | — | Yüksek hacim işlem sayısı eşiği |
| `high_volume_period_hours` | Fraud | int | — | Yüksek hacim kontrol periyodu |
| `monitoring_post_cancel_24h_polling_seconds` | Monitoring | int | 30 | İptal sonrası ilk 24 saat polling aralığı (saniye) |
| `monitoring_post_cancel_7d_polling_seconds` | Monitoring | int | 300 | 1-7 gün arası polling aralığı (saniye) |
| `monitoring_post_cancel_30d_polling_seconds` | Monitoring | int | 3600 | 7-30 gün arası polling aralığı (saniye) |
| `monitoring_stop_after_days` | Monitoring | int | 30 | İzleme durdurma süresi (gün) |
| `min_refund_threshold_ratio` | Monitoring | decimal | 2.0 | Minimum iade eşiği — iade < gas fee × bu oran ise iade yapılmaz, admin alert |
| `open_link_enabled` | Feature | bool | false | Açık link yöntemi aktif mi |
| `hot_wallet_limit` | Wallet | decimal | — | Hot wallet maksimum bakiye limiti — aşıldığında admin alert, cold wallet transfer gerekir (05 §3.3) |
| `wallet.payout_address_cooldown_hours` | Wallet | int | 24 | Satıcı ödeme adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma engellenir; mevcut CREATED davetler eski snapshot adresle devam eder (02 §12.3) |
| `wallet.refund_address_cooldown_hours` | Wallet | int | 24 | Alıcı iade adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma ve işlem kabul etme engellenir (02 §12.3) |

---

### 3.18 OutboxMessage

Event outbox — state geçişlerinde event kaybını önler.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | EventId olarak da kullanılır |
| `EventType` | string(200) | NOT NULL | Event adı (ör: "TransactionCreatedEvent") |
| `Payload` | text | NOT NULL | JSON — event verisi |
| `Status` | int | NOT NULL, DEFAULT 0 | Enum: OutboxMessageStatus |
| `RetryCount` | int | NOT NULL, DEFAULT 0 | Yeniden deneme sayısı |
| `ErrorMessage` | string(2000) | NULL | Son hata mesajı |
| `CreatedAt` | datetime | NOT NULL | |
| `ProcessedAt` | datetime | NULL | İşlenme zamanı |

> **Kaynak:** 05 §5.1 — "State geçişi + OutboxMessages tablosuna event yazma → AYNI DB TRANSACTION."
>
> **Status-dependent CHECK constraint'ler:**
> - **PENDING**: `ProcessedAt NULL` — henüz işlenmemiş.
> - **PROCESSED**: `ProcessedAt NOT NULL` — başarıyla işlenmiş.
> - **FAILED**: `ProcessedAt NULL` — işleme başarısız, retry beklenir. `ErrorMessage` NOT NULL (hata sebebi zorunlu).
>
> **Retry semantiği:** Outbox dispatcher PENDING **ve** FAILED kayıtları birlikte çeker (performans indeksi: `WHERE Status IN (PENDING, FAILED)`). FAILED kayıtlar PENDING'e geri dönmez — dispatcher doğrudan FAILED kayıtları da işler. Maksimum retry sayısı aşıldığında kayıt FAILED'da kalır ve admin alert tetiklenir. RetryCount her denemede artırılır.
>
> **Silme politikası:** Retention-based — işlenen kayıtlar 30 gün sonra toplu temizlenir (hard delete).

### 3.19 ProcessedEvent

Consumer idempotency — aynı event'in birden fazla işlenmesini önler.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | guid | PK | |
| `EventId` | guid | NOT NULL | OutboxMessage.Id mantıksal referansı (DB-level FK **değil** — aşağıdaki nota bkz.) |
| `ConsumerName` | string(200) | NOT NULL | Consumer adı (ör: "NotificationConsumer") |
| `ProcessedAt` | datetime | NOT NULL | İşlenme zamanı |

> **Kısıt:** EventId + ConsumerName çifti unique olmalı.
> **Kaynak:** 05 §5.1 — "Consumer event'i işledikten sonra EventId'yi kaydeder."
>
> **FK kararı:** ProcessedEvent.EventId → OutboxMessage.Id arasında DB-level FK tanımlanmaz. Sebep: her iki tablo da retention-based olup aynı batch cleanup job'ıyla birlikte temizlenir (30 gün). FK constraint cleanup sırasında silme sırası bağımlılığı yaratır ve batch işlemi zorlaştırır. İlişki uygulama seviyesinde korunur.
>
> **Silme politikası:** Retention-based — ilgili OutboxMessage temizlendiğinde birlikte temizlenir (hard delete). Cleanup job'ı önce ProcessedEvent, sonra OutboxMessage sırasıyla temizler.

### 3.20 AuditLog

Fon hareketleri, admin aksiyonları ve güvenlik olaylarının kalıcı, immutable audit kaydı. Loki log retention'ından bağımsız olarak DB'de süresiz saklanır.

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | long | PK, IDENTITY | |
| `UserId` | guid | NULL | İlgili kullanıcı (system event ise null) |
| `ActorId` | guid | NOT NULL | Aksiyonu gerçekleştiren (user/admin/system service account) |
| `ActorType` | ActorType | NOT NULL | Enum: User, Admin, System |
| `Action` | AuditAction | NOT NULL | Enum: AuditAction (§2.19) |
| `EntityType` | string(100) | NOT NULL | Etkilenen entity tipi (ör: "Wallet", "Transaction", "User") |
| `EntityId` | string(50) | NOT NULL | Etkilenen entity ID |
| `OldValue` | text | NULL | Önceki değer (JSON) |
| `NewValue` | text | NULL | Yeni değer (JSON) |
| `IpAddress` | string(45) | NULL | İşlemi yapanın IP adresi (IPv6 desteği) |
| `CreatedAt` | datetime | NOT NULL | Immutable timestamp |

> **İmmutable:** AuditLog kayıtları asla güncellenmez ve silinmez. UPDATE/DELETE operasyonu tanımlı değildir.
> **Kaynak:** Cüzdan bakiye değişiklikleri ve admin aksiyonları şu anda sadece Loki'de tutuluyor. Loki retention süresi sonrasında bu veriler kaybolur — finansal ve yasal uyumluluk için kalıcı DB kaydı gereklidir.
>
> **Silme politikası:** Append-Only (Kalıcı) — INSERT sonrası UPDATE/DELETE tanımlı değil. Arşivleme kapsamı dışında, süresiz canlı tabloda kalır.

### 3.21 ExternalIdempotencyRecord

Sidecar ve blockchain servislerinin receiver-side idempotency kaydı. Dış servislere gelen komutların tekrarlanmasını önler (05 §5.1).

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | long | PK, IDENTITY | |
| `IdempotencyKey` | string(200) | NOT NULL | `X-Idempotency-Key` header değeri (EventId veya TransactionId + action) |
| `ServiceName` | string(100) | NOT NULL | Hangi servis (SteamSidecar, BlockchainService) |
| `Status` | string(20) | NOT NULL, CHECK IN ('in_progress', 'completed', 'failed') | İşlem durumu — yalnızca bu üç değer geçerlidir |
| `ResultPayload` | text | NULL | Side effect sonucu (JSON) — replay'de bu döndürülür |
| `LeaseExpiresAt` | datetime | NULL | in_progress lease süresi — bu zamandan sonra kayıt stale kabul edilir ve reclaim edilebilir |
| `CreatedAt` | datetime | NOT NULL | İlk istek zamanı |
| `CompletedAt` | datetime | NULL | Tamamlanma zamanı |

> **Kaynak:** 05 §5.1 — "Sidecar ve blockchain servisleri gelen key'i paylaşılan SQL Server'da kaydeder."
>
> **Benzersizlik:** `UNIQUE(ServiceName, IdempotencyKey)` — aynı key farklı servisler tarafından bağımsız kullanılabilir. Global `UNIQUE(IdempotencyKey)` kullanılmaz; servisler arası gereksiz çakışma ve coupling'i önlemek için scope servis bazlıdır.
>
> **Status-dependent CHECK constraint'ler:**
> - **in_progress**: `CompletedAt NULL`, `ResultPayload NULL`, `LeaseExpiresAt NOT NULL` — işlem devam ediyor, lease süresi set edilmiş.
> - **completed**: `CompletedAt NOT NULL`. `ResultPayload` NULL olabilir — side effect'siz komutlarda (ör: delete) sonuç payload'ı olmayabilir.
> - **failed**: `CompletedAt NULL` — başarısız istek tamamlanmamıştır, retry beklenir.
>
> **Retry semantiği:** Aynı idempotency key ile yeni istek geldiğinde mevcut kayıdın durumuna göre davranılır:
> - **in_progress**: İstek bloke edilir — önceki işlem devam ediyor, sonucu beklenir.
> - **completed**: Önceki `ResultPayload` döndürülür — side effect tekrarlanmaz (replay).
> - **failed**: Kayıt `failed → in_progress` olarak güncellenir ve işlem yeniden denenir. Başarılı olursa `completed`'a geçer.
>
> UNIQUE(ServiceName, IdempotencyKey) constraint bu akışı destekler — aynı key için yeni satır oluşturulamaz, mevcut satır güncellenir.
>
> **Concurrency acquisition kuralı:** Aynı key ile eşzamanlı isteklerde yarış koşulu oluşabilir. Atomik davranış:
> - **İlk kayıt oluşturma:** `INSERT ... IF NOT EXISTS` (veya INSERT + unique constraint catch). Kazanan istek `in_progress` kaydı oluşturur; kaybeden istek mevcut kaydı okur ve durumuna göre davranır (in_progress → bekle/block, completed → replay, failed → claim).
> - **Failed → in_progress claim:** Conditional update ile atomik: `UPDATE ... SET Status = 'in_progress' WHERE Status = 'failed'`. Etkilenen satır = 1 ise claim başarılı, 0 ise başka istek zaten claim etmiş — kaybeden mevcut sonucu bekler.
> - **Genel prensip:** Her state geçişi tek bir atomik SQL statement ile yapılır; application-level lock kullanılmaz. Bu, aynı anda iki worker'ın aynı komutu çalıştırma riskini ortadan kaldırır.
>
> **Exactly-once sınırı:** Bu tablo tek başına kesin exactly-once garantisi vermez. External side effect uygulandıktan sonra ama completed status yazılmadan servis çökerse, lease süresi sonunda kayıt stale → failed → reclaim akışına girer ve side effect tekrar tetiklenebilir. Bu nedenle **downstream provider'da da idempotent işlem zorunludur:**
> - Blockchain transferleri: aynı nonce veya operation key ile tekrar gönderim provider tarafında reddedilir.
> - Steam trade offer: aynı asset + alıcı kombinasyonu için duplicate offer Steam API tarafından engellenir.
> - Stale reclaim öncesi read-before-retry: servis mümkünse external state'i kontrol eder (ör: blockchain'de tx hash var mı?) ve zaten tamamlanmışsa kayıdı completed'a çeker.
>
> Bu çift katmanlı yaklaşım (receiver-side idempotency + provider-side idempotency) birlikte exactly-once semantiğini sağlar.
>
> **Stale in_progress recovery:** Servis çökmesi veya timeout nedeniyle in_progress'te kalan kayıtlar için lease mekanizması:
> - Kayıt in_progress'e geçerken `LeaseExpiresAt = NOW + lease_duration` set edilir (varsayılan: 5 dakika, servis tipine göre ayarlanabilir).
> - Yeni istek geldiğinde mevcut kayıt in_progress ise: `LeaseExpiresAt < NOW` kontrolü yapılır. Lease dolmuşsa kayıt **stale** kabul edilir ve `failed`'a düşürülüp normal retry akışına girer (conditional update: `WHERE Status = 'in_progress' AND LeaseExpiresAt < NOW`).
> - Lease dolmamışsa istek bloke edilir — önceki işlem hâlâ devam ediyor.
>
> **Silme politikası:** Retention-based — 30 gün sonra temizlenebilir.

### 3.22 ColdWalletTransfer

Hot wallet'tan cold wallet'a yapılan fon transferlerinin platform ledger kaydı. Reconciliation'da false mismatch önlemek için kullanılır (05 §3.3).

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | long | PK, IDENTITY | |
| `Amount` | decimal(18,6) | NOT NULL | Transfer edilen tutar |
| `Token` | int | NOT NULL | Enum: StablecoinType — USDT veya USDC |
| `FromAddress` | string(50) | NOT NULL | Hot wallet adresi |
| `ToAddress` | string(50) | NOT NULL | Cold wallet adresi |
| `TxHash` | string(100) | NOT NULL, UNIQUE | Blockchain transaction hash |
| `InitiatedByAdminId` | guid | FK → User, NOT NULL | Transferi başlatan admin |
| `CreatedAt` | datetime | NOT NULL | Transfer zamanı |

> **Kaynak:** 05 §3.3 — "Hot→cold transfer yapıldığında platform ledger'ında kayıt oluşturulur."
>
> **Silme politikası:** Append-Only (Kalıcı) — INSERT sonrası UPDATE/DELETE tanımlı değil. Arşivleme kapsamı dışında, süresiz canlı tabloda kalır.

### 3.23 SystemHeartbeat

Platform uptime takibi. Beklenmedik kesinti sonrası outage window hesaplaması için kullanılır (05 §4.4).

| Field | Tip | Kısıt | Açıklama |
|-------|-----|-------|----------|
| `Id` | int | PK, CHECK (Id = 1) | Tek satır (singleton) — Id sabit 1, ikinci satır DB seviyesinde engellenir |
| `LastHeartbeat` | datetime | NOT NULL | Son başarılı heartbeat zamanı |
| `UpdatedAt` | datetime | NOT NULL | Son güncelleme |

> **Kaynak:** 05 §4.4 — "Periyodik heartbeat job'ı LastHeartbeat timestamp'ını DB'ye yazar."
>
> **Silme politikası:** Güncellenir, silinmez — tek satırlık sistem tablosu.

---

## 4. İlişkiler

### 4.1 Foreign Key Referansları

| Kaynak Entity | Field | Hedef Entity | Kardinalite |
|---------------|-------|--------------|-------------|
| UserLoginLog | UserId | User | N:1 |
| RefreshToken | UserId | User | N:1 |
| RefreshToken | ReplacedByTokenId | RefreshToken (self) | N:1 (opsiyonel) |
| UserNotificationPreference | UserId | User | N:1 |
| Transaction | SellerId | User | N:1 |
| Transaction | BuyerId | User | N:1 |
| Transaction | EscrowBotId | PlatformSteamBot | N:1 |
| Transaction | EmergencyHoldByAdminId | User | N:1 (opsiyonel) |
| TransactionHistory | TransactionId | Transaction | N:1 |
| TransactionHistory | ActorId | User | N:1 |
| PaymentAddress | TransactionId | Transaction | 1:1 |
| BlockchainTransaction | TransactionId | Transaction | N:1 |
| BlockchainTransaction | PaymentAddressId | PaymentAddress | N:1 (opsiyonel) |
| TradeOffer | TransactionId | Transaction | N:1 |
| TradeOffer | PlatformSteamBotId | PlatformSteamBot | N:1 |
| Dispute | TransactionId | Transaction | N:1 |
| Dispute | OpenedByUserId | User | N:1 |
| Dispute | AdminId | User | N:1 (opsiyonel) |
| FraudFlag | TransactionId | Transaction | N:1 (opsiyonel) |
| FraudFlag | UserId | User | N:1 (opsiyonel) |
| FraudFlag | ReviewedByAdminId | User | N:1 (opsiyonel) |
| Notification | UserId | User | N:1 |
| Notification | TransactionId | Transaction | N:1 (opsiyonel) |
| NotificationDelivery | NotificationId | Notification | N:1 |
| AdminRolePermission | AdminRoleId | AdminRole | N:1 |
| AdminUserRole | UserId | User | N:1 |
| AdminUserRole | AdminRoleId | AdminRole | N:1 |
| AdminUserRole | AssignedByAdminId | User | N:1 (opsiyonel) |
| SystemSetting | UpdatedByAdminId | User | N:1 (opsiyonel) |
| SellerPayoutIssue | TransactionId | Transaction | N:1 |
| SellerPayoutIssue | SellerId | User | N:1 |
| SellerPayoutIssue | EscalatedToAdminId | User | N:1 (opsiyonel) |
| ColdWalletTransfer | InitiatedByAdminId | User | N:1 |
| AuditLog | ActorId | User | N:1 |
| AuditLog | UserId | User | N:1 (opsiyonel) |

### 4.2 Cascade Kuralları

| İlişki | ON DELETE | Gerekçe |
|--------|-----------|---------|
| User → Transaction (Seller/Buyer) | NO ACTION | Soft delete — kullanıcı silinmez, deaktif olur |
| User → UserLoginLog | NO ACTION | Audit kaydı korunmalı |
| User → RefreshToken | NO ACTION | Soft delete — kullanıcı silindiğinde token'lar da soft delete edilir (uygulama seviyesinde) |
| User → Notification | NO ACTION | Bildirim geçmişi korunmalı |
| Transaction → TransactionHistory | NO ACTION | Audit kaydı asla silinmez |
| Transaction → PaymentAddress | NO ACTION | Ödeme adresi kaydı korunmalı |
| Transaction → BlockchainTransaction | NO ACTION | Blockchain kaydı korunmalı |
| Transaction → TradeOffer | NO ACTION | Trade offer kaydı korunmalı |
| Transaction → Dispute | NO ACTION | Dispute kaydı korunmalı |
| AdminRole → AdminRolePermission | NO ACTION | Soft delete — rol silindiğinde yetkileri de soft delete edilir (uygulama seviyesinde) |
| AdminRole → AdminUserRole | NO ACTION | Soft delete — rol silindiğinde atamalar da soft delete edilir (uygulama seviyesinde) |
| Transaction → SellerPayoutIssue | NO ACTION | Payout issue kaydı asla silinmez |
| User → AuditLog | NO ACTION | Audit kaydı asla silinmez |

> **Genel prensip:** Tüm entity'ler NO ACTION cascade kullanır. Silme işlemleri soft delete ile uygulama seviyesinde yönetilir. Append-only entity'lerde (TransactionHistory, AuditLog, ColdWalletTransfer) INSERT sonrası UPDATE/DELETE tanımlı değildir. Workflow record entity'lerde (BlockchainTransaction, TradeOffer, SellerPayoutIssue) DELETE tanımlı değildir; state/status güncellemesi alırlar ama terminal state sonrası fiilen frozen olurlar. Arşivlenebilir entity'ler transaction archive set ile birlikte archive tabloya taşınabilir (§8.8); kalıcı entity'ler (AuditLog, ColdWalletTransfer) süresiz canlı tabloda kalır.

---

## 5. İndeks Stratejisi

### 5.1 Unique Indeksler

| Entity | Field(s) | Açıklama |
|--------|----------|----------|
| User | SteamId | Benzersiz Steam hesabı |
| RefreshToken | Token | Token lookup |
| PaymentAddress | TransactionId | 1:1 ilişki garantisi |
| PaymentAddress | Address | Benzersiz blockchain adresi |
| PaymentAddress | HdWalletIndex | Derivation index reuse engeli — monoton artan, arşivleme sonrası da global tekillik korunur |
| BlockchainTransaction | TxHash (WHERE NOT NULL) | Blockchain transaction tekrar kontrolü |
| TradeOffer | SteamTradeOfferId (WHERE NOT NULL) | Steam trade offer tekrar kontrolü — retry/entegrasyon hatalarında çift kayıt engeli |
| Transaction | InviteToken (WHERE NOT NULL) | Açık link token benzersizliği |
| PlatformSteamBot | SteamId | Benzersiz bot hesabı |
| AdminRole | Name | Benzersiz rol adı |
| SystemSetting | Key | Benzersiz parametre anahtarı |
| UserNotificationPreference | UserId + Channel (WHERE IsDeleted = 0) | Kullanıcı başına kanal başına tek kayıt |
| UserNotificationPreference | Channel + ExternalId (WHERE IsDeleted = 0 AND ExternalId IS NOT NULL) | Aynı dış kanal hedefi (email, Telegram chat ID, Discord user ID) aynı anda yalnızca tek hesaba bağlı olabilir — multi-account fraud sinyali ve bildirim karışıklığını önler |
| AdminRolePermission | AdminRoleId + Permission (WHERE IsDeleted = 0) | Rol başına yetki tekrarı engelleme |
| AdminUserRole | UserId + AdminRoleId (WHERE IsDeleted = 0) | Kullanıcı başına rol tekrarı engelleme |
| Dispute | TransactionId + Type (unfiltered) | Aynı işlem için aynı türde dispute tekrar açılamaz — IsDeleted dahil tüm kayıtlara uygulanır (02 §10.2) |
| ProcessedEvent | EventId + ConsumerName | Idempotency garantisi |
| ExternalIdempotencyRecord | ServiceName + IdempotencyKey | Servis bazlı idempotency — aynı key farklı servisler için bağımsız |
| SellerPayoutIssue | TransactionId (WHERE VerificationStatus != RESOLVED) | Bir transaction için aynı anda en fazla bir aktif payout issue |
| NotificationDelivery | NotificationId + Channel | Bir bildirim için kanal başına tek delivery kaydı — tek satır workflow modeli (§3.13a) |

### 5.2 Performans İndeksleri

| Entity | Field(s) | Tip | Açıklama |
|--------|----------|-----|----------|
| Transaction | Status | Filtered (aktif durumlar) | Aktif işlem sorguları — `WHERE Status NOT IN (COMPLETED, CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN)` |
| Transaction | SellerId | Standard | Satıcının işlemleri |
| Transaction | BuyerId | Standard | Alıcının işlemleri |
| Transaction | CreatedAt | Standard | Tarih bazlı sorgular, ileride partitioning |
| Transaction | EscrowBotId | Standard | Bot bazlı işlem sorguları |
| TransactionHistory | TransactionId | Standard | İşlem geçmişi sorguları |
| BlockchainTransaction | TransactionId | Standard | İşleme ait blockchain transferleri |
| BlockchainTransaction | Status | Filtered (Pending) | Onay bekleyen transferler |
| BlockchainTransaction | FromAddress | Standard | Çoklu hesap tespiti — gönderim adresi çapraz kontrol |
| TradeOffer | TransactionId | Standard | İşleme ait trade offer'lar |
| TradeOffer | PlatformSteamBotId | Standard | Bot bazlı trade offer sorguları |
| Notification | UserId + IsRead | Composite | Okunmamış bildirim sorguları |
| Notification | CreatedAt | Standard | Kronolojik listeleme |
| FraudFlag | Status | Filtered (Pending) | Admin inceleme kuyruğu |
| FraudFlag | TransactionId | Standard | İşleme ait flag'ler |
| FraudFlag | UserId | Standard | Kullanıcıya ait flag'ler |
| Dispute | TransactionId | Standard | İşleme ait dispute'lar |
| Dispute | Status | Filtered (Open, Escalated) | Aktif dispute sorguları |
| UserLoginLog | UserId | Standard | Kullanıcı login geçmişi |
| UserLoginLog | IpAddress | Standard | Çoklu hesap tespiti — IP bazlı |
| UserLoginLog | DeviceFingerprint | Standard | Çoklu hesap tespiti — cihaz bazlı |
| User | DefaultPayoutAddress | Standard | Çoklu hesap tespiti — cüzdan bazlı |
| User | DefaultRefundAddress | Standard | Çoklu hesap tespiti — cüzdan bazlı |
| OutboxMessage | Status + CreatedAt | Filtered (Pending + Failed) | İşlenmemiş ve retry bekleyen event'leri sıralı çekme — `WHERE Status IN (PENDING, FAILED)` |
| PaymentAddress | MonitoringStatus | Filtered (aktif izleme) | İzlenen adresleri çekme — `WHERE MonitoringStatus IN (ACTIVE, POST_CANCEL_24H, POST_CANCEL_7D, POST_CANCEL_30D)` |
| RefreshToken | UserId | Standard | Kullanıcı session'ları |
| SystemSetting | Category | Standard | Kategori bazlı listeleme |
| AuditLog | ActorId | Standard | Aktörün tüm aksiyonları |
| AuditLog | UserId | Standard | Kullanıcıya ait tüm audit kayıtları |
| AuditLog | EntityType + EntityId | Composite | Belirli bir entity'nin audit geçmişi |
| AuditLog | Action | Standard | Aksiyon tipine göre filtreleme |
| SellerPayoutIssue | TransactionId | Standard | İşleme ait payout sorunları |
| SellerPayoutIssue | SellerId | Standard | Satıcının payout sorunları |
| SellerPayoutIssue | VerificationStatus | Filtered (aktif durumlar) | Çözülmemiş payout sorunları — `WHERE VerificationStatus NOT IN (RESOLVED)` |
| AuditLog | CreatedAt | Standard | Tarih bazlı sorgular, kronolojik listeleme |

> **Not:** Filtered index'ler SQL Server'a özgüdür. EF Core migration'larında `HasFilter()` ile tanımlanır.
>
> **SQL Server filtered index predicate kısıtı:** Filtered index WHERE predicate'i `NOT IN`, `BETWEEN`, fonksiyon çağrısı ve CASE ifadesi desteklemez (Microsoft Docs — "Create Filtered Indexes"). Yukarıdaki tabloda semantic amaçla `NOT IN (...)` gösterilen filtreler (örn. Transaction.Status aktif durumlar filter'ı) `HasFilter()` içinde `[Col] <> 'A' AND [Col] <> 'B' AND ...` zinciri olarak yazılmalıdır. `IN (...)` ise desteklenir. Kaynak: T27 doğrulama sırasında tespit edildi (PR #41).

---

## 6. Veri Saklama ve Anonimleştirme

### 6.1 Saklama Politikası

| Veri Türü | Süre | Kaynak |
|-----------|------|--------|
| İşlem geçmişi (Transaction, TransactionHistory) | Süresiz | 02 §21 |
| Blockchain kayıtları (BlockchainTransaction) | Süresiz | Finansal audit |
| Bildirimler (Notification + NotificationDelivery) | Transaction arşivlemesi ile paralel — işlem arşivlendiğinde ilgili bildirimler ve delivery kayıtları birlikte arşivlenir. Bağımsız bildirimler (TransactionId = NULL) ve bağlı delivery kayıtları için ayrı retention belirlenir (ör: 1 yıl) — birlikte purge edilir (önce delivery, sonra notification). | — |
| Login logları (UserLoginLog) | 1 yıl (önerilen) | Fraud tespiti yeterli periyodu |
| Refresh token'lar (RefreshToken) | Süresi dolan token'lar periyodik temizlenir | — |
| Outbox/ProcessedEvent/ExternalIdempotencyRecord | İşlenen kayıtlar 30 gün sonra temizlenebilir (retention-based, toplu hard delete) | — |
| Audit logları (AuditLog) | Süresiz | Finansal ve yasal uyumluluk |

### 6.2 Hesap Silme ve Anonimleştirme

Kullanıcı hesabını sildiğinde (02 §19, 05 §6.5):

| Field | İşlem |
|-------|-------|
| User.SteamId | `ANON_{kısa GUID}` formatında unique anonymized değerle değiştirilir (UNIQUE + NOT NULL constraint'leri korunur) |
| User.SteamDisplayName | "Deleted User" ile değiştirilir |
| User.SteamAvatarUrl | Temizlenir |
| User.DefaultPayoutAddress | Temizlenir |
| User.DefaultRefundAddress | Temizlenir |
| User.Email | Temizlenir |
| User.IsDeleted | true |
| User.DeletedAt | Silme zamanı |
| Transaction kayıtları | Korunur (UserId referansı kalır ama kişisel veri anonimleştirilmiş) |
| TransactionHistory | Korunur (audit trail) |
| AuditLog | Korunur (immutable audit trail — cüzdan, admin, güvenlik kayıtları) |
| UserLoginLog | Korunur (fraud audit) — ancak KVKK talebiyle temizlenebilir |
| **Bağlı Entity Cleanup** | |
| UserNotificationPreference | Tüm kayıtlar soft delete edilir + `ExternalId` temizlenir (email, Telegram chat ID, Discord user ID kişisel veridir) |
| RefreshToken | Tüm aktif token'lar revoke edilir (`IsRevoked = true`, `RevokedAt = NOW`) + soft delete. `DeviceInfo` ve `IpAddress` temizlenir |
| Notification | Korunur (TransactionId referanslı olanlar audit trail, bağımsız olanlar retention politikasına tabi) |
| NotificationDelivery | `TargetExternalId` masked formata dönüştürülür (`***@***.com`, `tg:***{son 4}` vb.) — gönderim anı snapshot'ı kişisel veri içerdiğinden anonimleştirme kapsamındadır. Delivery kaydının kendisi (Status, AttemptCount, SentAt) korunur — audit trail |

> **Prensip:** Hesap silme yalnızca User satırını değil, tüm bağlı kişisel veriyi kapsar. Kişisel veriler temizlenir, işlem geçmişi ve audit logları anonim olarak saklanır (audit trail).
>
> **Query davranışı:** Silinen kullanıcı `IsDeleted = true` olduğundan global query filter ile gizlenir. Tarihsel sorgularda (transaction detay, audit trail, admin inceleme) User navigation'ı `IgnoreQueryFilters()` ile çözümlenmelidir — aksi halde FK referansı mevcut ama navigation null döner. Anonimleştirilmiş kullanıcı `SteamDisplayName = "Deleted User"` olarak görüntülenir (§1.3).

---

## 7. Traceability Matrix

### 7.1 Gereksinim → Entity Eşlemesi

| Kaynak | Bölüm | Entity | Notlar |
|--------|-------|--------|--------|
| 02 §2 | İşlem akışı | Transaction, TransactionHistory | 8 adımlı akış, state machine |
| 02 §3 | Timeout | Transaction (deadline fields) | Ödeme: Hangfire delayed job; diğer aşamalar: scanner/poller |
| 02 §4.1-4.3 | Ödeme altyapısı | PaymentAddress | Her işlem için benzersiz adres |
| 02 §4.4-4.7 | Ödeme edge case / iade / gas fee | BlockchainTransaction | Tüm transfer türleri |
| 02 §5 | Komisyon | Transaction (CommissionRate, CommissionAmount) | Oluşturma anında snapshot |
| 02 §6 | Alıcı belirleme | Transaction (BuyerIdentificationMethod, InviteToken) | 2 yöntem |
| 02 §7 | İptal | Transaction (CancelledBy, CancelReason) | İptal sebebi zorunlu |
| 02 §8 | İşlem limitleri | SystemSetting | Admin tarafından dinamik |
| 02 §9 | Item yönetimi | Transaction (Item* fields) | Snapshot olarak saklanır |
| 02 §10 | Dispute | Dispute | 3 itiraz türü, otomatik + eskalasyon |
| 02 §10.3 | Payout sorun bildirimi | SellerPayoutIssue | Satıcı payout issue takibi |
| 02 §11 | Kullanıcı kimlik | User | Steam login, Mobile Auth |
| 02 §12 | Cüzdan adresi güvenliği | User (DefaultPayoutAddress, DefaultRefundAddress), Transaction (snapshot) | Snapshot prensibi |
| 02 §13 | İtibar skoru | User (CompletedTransactionCount, SuccessfulTransactionRate, CreatedAt) | Denormalized |
| 02 §14.1 | Wash trading | Transaction (sorgu bazlı) | Aynı çift, 1 ay kuralı — entity gerekmez |
| 02 §14.2 | Sahte işlem | User (CooldownExpiresAt), SystemSetting | Cooldown tracking |
| 02 §14.3 | Hesap güvenliği | FraudFlag, UserLoginLog | IP/cihaz çapraz kontrol |
| 02 §14.4 | Kara para | FraudFlag, Transaction (MarketPriceAtCreation) | Piyasa fiyatı snapshot |
| 02 §15 | Platform Steam hesapları | PlatformSteamBot | Durum, kapasite izleme |
| 02 §16 | Admin paneli | AdminRole, AdminRolePermission, AdminUserRole, SystemSetting | Dinamik rol ve parametre |
| 02 §17 | Dashboard | — (sorgu bazlı) | Mevcut entity'lerden türetilir |
| 02 §18 | Bildirimler | Notification, UserNotificationPreference, NotificationDelivery | Platform içi + dış kanal teslimat takibi |
| 02 §19 | Hesap yönetimi | User (IsDeactivated, IsDeleted) | Soft delete, anonimleştirme |
| 02 §23 | Downtime | Transaction (TimeoutFrozenAt) | Timeout dondurma |
| 03 §1.2 | İşlem durumları | TransactionStatus enum | 13 durum |
| 03 §2.1 | Kayıt / ToS | User (TosAcceptedVersion, TosAcceptedAt) | Versiyon takibi |
| 03 §2.3, §3.5 | Trade offer | TradeOffer | Gönderim, kabul, ret takibi |
| 03 §6 | Dispute akışları | Dispute | Open → Escalated → Closed |
| 03 §7 | Fraud akışları | FraudFlag | 4 flag türü |
| 03 §8.6 | Rol/yetki yönetimi | AdminRole, AdminRolePermission, AdminUserRole | Süper admin + dinamik roller |
| 05 §5.1 | Outbox pattern | OutboxMessage, ProcessedEvent | Event garantisi + idempotency |
| 05 §5.1 | Receiver-side idempotency | ExternalIdempotencyRecord | Sidecar/blockchain servislerinde tekrar engeli |
| 05 §3.3 | Hot→cold wallet transfer | ColdWalletTransfer | Fon transfer ledger kaydı, reconciliation |
| 05 §4.4 | Platform uptime takibi | SystemHeartbeat | Outage window hesaplaması |
| 05 §5.4 | Audit trail | TransactionHistory, AuditLog | TransactionHistory: state geçişleri; AuditLog: cüzdan, admin, güvenlik olayları |
| 05 §6.1 | Authentication | RefreshToken | JWT refresh + rotation |
| 02 §19, 02 §21 | Hesap yönetimi, veri saklama | AuditLog | Audit logları süresiz saklanır, hesap silinse bile korunur |
| 05 §6.3 | Güvenlik katmanları — audit logging | AuditLog | Cüzdan, admin, güvenlik olayları DB'ye kalıcı yazılır |

### 7.2 Geri İzlenebilirlik

Tüm 25 entity'nin en az bir kaynak gereksinime (02/03/05) dayandığı §7.1 matrix'inde izlenebilir. Kaynağı olmayan entity yoktur.

---

## 8. Implementasyon Notları

### 8.1 Timeout Dondurma (Downtime)

Bakım veya Steam kesintisinde (02 §3.3, §23). Resume modeli: **TimeoutRemainingSeconds tabanlı reschedule** (05 §4.4 ile uyumlu).

**Freeze (dondurma):**
1. `TimeoutFrozenAt = NOW`, `TimeoutFreezeReason` set edilir
2. Aktif deadline'dan kalan süre hesaplanır ve `TimeoutRemainingSeconds` kaydedilir
3. **ITEM_ESCROWED aşamasında** (per-transaction Hangfire job): `PaymentTimeoutJobId` ve `TimeoutWarningJobId` job'ları iptal edilir
4. **Diğer aşamalarda** (scanner/poller): ek aksiyon gerekmez — poller zaten `TimeoutFrozenAt IS NOT NULL` kayıtları atlar

**Resume (devam ettirme):**
1. Aktif deadline field'ları güncellenir: `yeni deadline = NOW + TimeoutRemainingSeconds`
2. **ITEM_ESCROWED aşamasında**: yeni Hangfire timeout job schedule edilir (`PaymentTimeoutJobId` güncellenir)
3. **Diğer aşamalarda**: deadline güncellenir, poller yeni deadline'ı doğal olarak görür
4. `TimeoutFrozenAt = NULL`, `TimeoutFreezeReason = NULL`, `TimeoutRemainingSeconds = NULL`

> **Otorite:** Reschedule'ın kaynağı `TimeoutRemainingSeconds`'tır. Deadline field'ları bu değerden türetilir, tersi değil.
> **Enforcement modeli referansı:** Ödeme aşaması = per-transaction Hangfire delayed job; diğer aşamalar = periyodik scanner/poller (§3.5 state→deadline/job matrisi).

### 8.2 Denormalized Field'lar

| Entity | Field | Güncelleme Zamanı | Consistency |
|--------|-------|-------------------|-------------|
| User | CompletedTransactionCount | İşlem COMPLETED olduğunda | Eventual (cross-module) |
| User | SuccessfulTransactionRate | İşlem COMPLETED veya CANCELLED olduğunda — sadece sorumlu tarafın skoru güncellenir (§3.1 formül detayı) | Eventual (cross-module) |
| User | CooldownExpiresAt | İptal limiti aşıldığında | Eventual (cross-module) |
| PlatformSteamBot | ActiveEscrowCount | Item escrow/release olduğunda | Atomic (same-module) |
| PlatformSteamBot | DailyTradeOfferCount | Trade offer gönderildiğinde, gece yarısı sıfırlanır | Atomic (same-module) |
| Transaction | HasActiveDispute | Dispute açıldığında true, tüm dispute'lar kapandığında false | Atomic (same-module) |

> **Consistency modeli:** Same-module güncellemeler aynı DB transaction'da atomik yapılır. Cross-module güncellemeler Outbox dispatcher üzerinden eventual consistency ile gerçekleşir — idempotent event handler + reconciliation job gerektirir. Detay: 09 §9.6.

### 8.3 Finansal Hesaplama ve Rounding Kuralları

Tüm finansal hesaplamalar tek bir normatif kurala tabidir:

| Konu | Kural |
|------|-------|
| Scale | `decimal(18,6)` — 6 ondalık basamak, tüm finansal alanlarda aynı |
| Rounding modu | `MidpointRounding.ToZero` (truncation — kesme, sıfıra doğru yuvarla, 09 §14.3 ile uyumlu) |
| Hesaplama sırası | 1. `CommissionAmount = ROUND(Price × CommissionRate, 6)` → 2. `TotalAmount = Price + CommissionAmount` → 3. `PaymentAddress.ExpectedAmount = TotalAmount` |
| Payment validation tolerance | Yok — gelen tutar `ExpectedAmount` ile tam eşleşmeli. Eksik veya fazla tutar ayrı akışlara yönlendirilir (02 §4.4) |
| Payout hesaplama | `SellerPayout = Price` (komisyon düşülmüş). Gas fee ayrı blockchain transaction'da |

> **Kritik:** Bu hesaplama sırası ve rounding modu backend, UI ve blockchain doğrulama katmanında birebir aynı olmalıdır. Farklı yuvarlama uygulanması micro-unit fark → ödeme doğrulama hatası üretir.

### 8.4 Item Asset Lineage

Steam'de trade yapıldığında asset ID değişir. Item'ın yaşam döngüsü boyunca üç ayrı asset ID'si olur:

| Field | Set Edilme Anı | Kaynak |
|-------|---------------|--------|
| `ItemAssetId` | İşlem oluşturma | Satıcının envanterinden snapshot |
| `EscrowBotAssetId` | Trade offer kabul (seller → bot) | Steam trade receipt'ten alınır |
| `DeliveredBuyerAssetId` | Trade offer kabul (bot → buyer) | Steam trade receipt'ten alınır |

> **Neden gerekli:** Aynı classId/instanceId'ye sahip birden fazla item bot envanterinde olabilir. Trade offer oluşturulurken doğru item'ı seçmek için `EscrowBotAssetId` zorunludur. İade akışında da aynı alan kullanılır. `DeliveredBuyerAssetId` teslim sonrası audit ve dispute doğrulaması için tutulur.

### 8.5 Admin Aksiyonu Invariantı

Veri modelinde admin tarafından gerçekleştirilen aksiyonları kaydeden FK alanları vardır:

`EmergencyHoldByAdminId`, `ReviewedByAdminId`, `AssignedByAdminId`, `UpdatedByAdminId`, `InitiatedByAdminId`, `Dispute.AdminId`, `SellerPayoutIssue.EscalatedToAdminId`

Bu alanların tümü `FK → User` olarak tanımlıdır. DB seviyesinde "bu kullanıcı admin mi?" kontrolü yapılamaz (rol bilgisi ayrı tabloda). Bu nedenle aşağıdaki invariant **uygulama katmanında zorunlu** olarak uygulanır:

> **İnvariant:** Bu alanlara yazılan kullanıcı, aksiyon anında aktif bir admin rolüne sahip olmalıdır (`AdminUserRole` tablosunda aktif kaydı bulunmalıdır). Command handler / service layer guard'ı bu kontrolü yapar.
>
> **Tarihsel audit güvencesi:** Admin rolü sonradan kaldırılabilir. Tarihsel kayıtlarda "bu aksiyon admin tarafından yapıldı" bilgisi, `TransactionHistory` ve `AuditLog` kayıtlarındaki `ActorType = ADMIN` field'ından doğrulanır — FK hedefinin şu anki rol durumundan değil, aksiyon anındaki ActorType kaydından okunur.

### 8.6 Transaction Tarafı Aktör Invariantları

Admin aksiyonları §8.5'te tanımlanmıştır. Transaction tarafındaki kullanıcı aksiyonları da aynı netlikte uygulama katmanında zorunlu kılınır:

| Aksiyon | Invariant | Doğrulama |
|---------|-----------|-----------|
| Dispute açma | `Dispute.OpenedByUserId = Transaction.BuyerId` | Yalnızca alıcı dispute açabilir (02 §10.2) |
| Payout issue bildirme | `SellerPayoutIssue.SellerId = Transaction.SellerId` | Yalnızca satıcı payout sorunu bildirebilir (02 §10.3) |
| İşlem oluşturma | `Transaction.SellerId = authenticated user` | Satıcı kendi işlemini oluşturur |
| İşlem kabul etme | `BuyerId = authenticated user` | Alıcı kendi kabulünü yapar |
| İşlem iptali (kullanıcı) | `authenticated user IN (SellerId, BuyerId)` | Sadece taraflar iptal edebilir (02 §7) |
| Cüzdan adresi değişikliği | `authenticated user = User.Id` | Kullanıcı kendi adresini değiştirir |

> **DB seviyesinde enforce edilemez** — cross-table invariant (FK hedefi başka tablodaki field ile eşleşme). Command handler / service layer guard'ı bu kontrolü yapar. Yetkisiz aksiyon girişimi `AuthorizationException` fırlatır ve AuditLog'a kaydedilir.

### 8.6a Audit Aktör İnvariantı

TransactionHistory ve AuditLog kayıtlarındaki `ActorType + ActorId` çifti audit trail'in güvenilirliğini belirler. Aşağıdaki invariantlar **uygulama katmanında zorunlu** olarak uygulanır:

| ActorType | ActorId Kuralı | Doğrulama |
|-----------|---------------|-----------|
| `SYSTEM` | `ActorId = 00000000-0000-0000-0000-000000000001` (§8.9 sentinel) | Yalnızca platform otomatik aksiyonları (timeout tetikleme, otomatik iade, ödeme gönderimi). Sentinel GUID sabittir — başka değer kabul edilmez |
| `ADMIN` | `ActorId` aksiyon anında aktif admin rolüne sahip olmalıdır (`AdminUserRole` tablosunda aktif kaydı bulunmalıdır) | §8.5 admin invariantı ile aynı kural. Admin rolü sonradan kaldırılsa bile tarihsel kayıtta `ActorType = ADMIN` kalır |
| `USER` | `ActorId = authenticated user ID` ve kullanıcı aksiyonun gerçekleştiği bağlamda yetkili olmalıdır (§8.6 invariantları) | Kullanıcı kendi adına aksiyon yapar — başka kullanıcı adına kayıt oluşturulamaz |

> **Genel kural:** `ActorType` ve `ActorId` her zaman birlikte set edilir, hiçbir zaman biri dolu diğeri boş olamaz (her iki alan NOT NULL). ActorType enum değeri ile ActorId'nin işaret ettiği kaydın türü tutarlı olmalıdır — mismatch (ör: ActorType = SYSTEM ama ActorId normal kullanıcı) audit trail bütünlüğünü bozar.
>
> **Enforcement:** Bu invariantlar DB CHECK ile enforce edilemez (cross-table doğrulama gerektirir). Audit kayıt oluşturma tek bir merkezi servis/method üzerinden yapılır — doğrudan INSERT yasaktır. Bu merkezi nokta ActorType-ActorId tutarlılığını garanti eder.

### 8.7 Concurrency Control

> **Concurrency:** Transaction entity'sinde `RowVersion` field'ı EF Core optimistic concurrency sağlar. Concurrent state geçişlerinde (ör: timeout job + ödeme doğrulama aynı anda) ilk yazma kazanır, ikinci yazma `DbUpdateConcurrencyException` fırlatır ve retry mekanizması devreye girer.

### 8.8 DB Büyüme Stratejisi

| Konu | Karar | Kaynak |
|------|-------|--------|
| Partitioning | `CreatedAt` bazlı — 10M+ satırdan sonra | 05 §2.4 |
| Filtered index | Aktif işlemler ve pending kayıtlar için | Bu doküman §5.2 |

**Arşivleme stratejisi (05 §2.4):**

Transaction ve tüm FK-bağımlı kayıtları **birlikte** arşiv tablolarına taşınır (archive set). Arşivleme birimi tek bir Transaction ve ona bağlı tüm child kayıtlardır:

| Arşivlenen (birlikte) | Koşul |
|------------------------|-------|
| Transaction | `Status IN (COMPLETED, CANCELLED_*)` ve `CreatedAt < 6 ay önce` |
| TransactionHistory | Arşivlenen Transaction'a bağlı |
| BlockchainTransaction | Arşivlenen Transaction'a bağlı |
| TradeOffer | Arşivlenen Transaction'a bağlı |
| PaymentAddress | Arşivlenen Transaction'a bağlı |
| Dispute | Arşivlenen Transaction'a bağlı |
| FraudFlag (TRANSACTION_PRE_CREATE) | Arşivlenen Transaction'a bağlı |
| SellerPayoutIssue | Arşivlenen Transaction'a bağlı |
| Notification | Arşivlenen Transaction'a bağlı (`TransactionId NOT NULL` olanlar) |
| NotificationDelivery | Arşivlenen Notification'a bağlı |

| Arşivlenmeyen | Sebep |
|---------------|-------|
| User, UserLoginLog, RefreshToken | Kullanıcı verisi — transaction'dan bağımsız yaşam döngüsü |
| AuditLog | Immutable, süresiz saklama — arşivleme dışı |
| ColdWalletTransfer | Transaction'a bağlı değil, bağımsız finansal kayıt |
| FraudFlag (ACCOUNT_LEVEL) | Transaction'a bağlı değil — kullanıcı bazlı, canlı tabloda kalıcı. Resolved olduktan sonra soft delete ile yönetilir, fraud geçmişi korunur |
| Notification (`TransactionId = NULL`) | Bağımsız bildirimler — ayrı retention politikası |
| Altyapı tabloları | OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord — kendi retention'ı var |

> **Arşiv tabloları:** Her entity için `Archive_{EntityName}` tablosu (aynı şema). Arşiv tabloları read-only, FK constraint'siz (arşivleme sonrası referans bütünlüğü uygulama seviyesinde korunur). Taşıma sırası: child'lar önce, Transaction en son (FK bağımlılık sırası tersine).
>
> **Atomiklik:** Her transaction archive set'i tek DB transaction içinde taşınır (copy to archive + delete from live). Set ikiye bölünemez — ya tümü taşınır ya hiçbiri. Batch arşivleme job'ı transaction'ları tek tek işler; bir set başarısız olursa sadece o set rollback edilir, diğerleri etkilenmez. Retry: idempotent — arşiv tablosunda zaten mevcut olan kayıtlar `INSERT ... WHERE NOT EXISTS` ile atlanır, canlı tabloda artık olmayan kayıtlar skip edilir.
>
> **Global benzersizlik ve arşivleme:** Canlı tablodaki unique constraint'ler arşiv tablolarını kapsamaz. Aşağıdaki alanlar için bu risk değerlendirilmiştir:
> - **PaymentAddress.Address:** HD wallet derivation path'ten üretilir — aynı adres matematiksel olarak tekrar üretilemez. `HdWalletIndex` UNIQUE constraint ile canlı tabloda derivation index reuse engellenir; arşivleme sonrası yeni index her zaman öncekinden büyük olacağı için (monoton artan allocator) arşivlenmiş index'lerle çakışma imkansızdır.
> - **BlockchainTransaction.TxHash:** Blockchain'de globally unique — aynı hash iki farklı transfer için oluşamaz.
> - **TradeOffer.SteamTradeOfferId:** Steam tarafından atanır — platform kontrolü dışında, reuse riski yok.
> - **ColdWalletTransfer.TxHash:** Aynı blockchain garantisi geçerli.
>
> - **Transaction.InviteToken:** Platform tarafından üretilen kriptografik rastgele token (64 karakter). Collision olasılığı matematiksel olarak ihmal edilebilir düzeydedir (CSPRNG). Ek güvence: arşivlenen transaction'lar terminal state'te olduğundan invite token'ları artık aktif değildir ve yeni işlem akışında kullanılamaz. Arşiv sonrası aynı token'ın yeniden üretilmesi pratik olarak imkansızdır.
>
> Bu alanlar için canlı + arşiv çapraz unique kontrole gerek yoktur. Diğer unique alanlar (User.SteamId, AdminRole.Name vb.) arşivleme kapsamı dışındadır.

### 8.9 Seed Data

| Kayıt | Tablo | Açıklama |
|-------|-------|----------|
| SYSTEM service account | User | Sabit GUID (`00000000-0000-0000-0000-000000000001`) ile oluşturulur. Sentinel değerler: `SteamId = "00000000000000001"` (format-uyumlu ama gerçek olmayan 17 haneli değer), `SteamDisplayName = "System"`, `MobileAuthenticatorVerified = false`, `IsDeactivated = true`. `AuditLog` ve `TransactionHistory` kayıtlarında `ActorType = SYSTEM` olduğunda `ActorId` bu kayda referans verir. Platform otomatik aksiyonlarının (timeout tetikleme, otomatik iade, ödeme gönderimi vb.) audit kaydında aktör olarak kullanılır. **Tasarım notu:** Ayrı bir actor/principal tablosu yerine User tablosunda sentinel kayıt tutmak bilinçli bir trade-off'tur — AuditLog.ActorId'nin tek bir FK hedefi olmasını sağlar, polymorphic ilişki karmaşıklığını önler. **Domain istisnası:** SteamId alanı normalde gerçek Steam 64-bit ID tutar. Bu kuralın bilinen iki istisnası vardır: (1) SYSTEM sentinel kaydı (`00000000000000001`, `IsDeactivated = true`), (2) hesabı silinen kullanıcılar (`ANON_{kısa GUID}`, `IsDeleted = true` — §6.2). **Dışlama mekanizması:** §1.3 operasyonel kullanıcı predicate'ine göre: user-facing sorgularda `IsDeactivated = 0` şartı ile otomatik dışlanır. Tarihsel/audit sorgularda dahil edilir (`ActorType = SYSTEM` ile tanınır). |
| SystemHeartbeat | SystemHeartbeat | Sabit `Id = 1` ile seed edilir. `CHECK (Id = 1)` constraint ikinci satır oluşmasını engeller. Uygulama yalnızca bu satırı UPDATE eder, INSERT yapmaz. |
| Platform parametreleri | SystemSetting | §3.17'deki tüm parametreler seed edilir. Varsayılanı olan parametreler `Value` dolu + `IsConfigured = true` olarak oluşturulur. Varsayılanı "—" olan parametreler `Value = NULL`, `IsConfigured = false` olarak seed edilir. **Startup fail-fast:** Uygulama başlatılırken `IsConfigured = false` olan zorunlu parametreler kontrol edilir; yapılandırılmamış olanlar varsa uygulama hata vererek durur — sessiz runtime hatası önlenir. **Bootstrap konfigürasyon yolu:** İlk kurulumda admin paneli henüz erişilemez olduğundan, yapılandırılmamış zorunlu parametrelerin ilk kez set edilmesi için ayrı bir mekanizma gereklidir. Desteklenen yol: environment variable override — her SystemSetting key'i `SKINORA_SETTING_{KEY_UPPER}` formatında env var ile hydrate edilebilir. Startup sırası: (1) migration + seed çalışır, (2) env var override'lar uygulanır (`Value` güncellenir, `IsConfigured = true` set edilir), (3) fail-fast kontrolü çalışır. Env var yalnızca `IsConfigured = false` olan parametreleri set eder — zaten yapılandırılmış parametreleri override etmez (güvenlik). Lansman sonrası parametreler admin panelinden yönetilir, env var bootstrap mekanizması yalnızca ilk kurulum ve CI/CD pipeline'ları içindir. |

### 8.10 Hangfire Tabloları

Hangfire kendi tablolarını otomatik oluşturur (`Hangfire.Job`, `Hangfire.State` vb.). Bu tablolar bu dokümanın kapsamı dışındadır — Hangfire SQL Server storage tarafından yönetilir.

### 8.11 Redis Veri Yapıları

Redis'te saklanan veriler (session cache, rate limiting counter'ları, envanter cache) bu dokümanın kapsamı dışındadır — kalıcı veri modeli değil, geçici cache'lerdir.

---

*Skinora — Data Model v4.9*
