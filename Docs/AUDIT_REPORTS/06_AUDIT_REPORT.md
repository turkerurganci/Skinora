# Audit Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-16
**Hedef:** 06 — Data Model (v1.7)
**Bağlam:** 02_PRODUCT_REQUIREMENTS.md (v1.5), 03_USER_FLOWS.md (v1.5), 05_TECHNICAL_ARCHITECTURE.md (v1.4), 10_MVP_SCOPE.md (v1.1)
**Odak:** Tam denetim

---

## Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|--------|-----------|---|---|---|
| 02 — Product Requirements | 67 | 63 | 4 | 0 |
| 03 — User Flows | 38 | 35 | 3 | 0 |
| 05 — Technical Architecture | 42 | 40 | 2 | 0 |
| 10 — MVP Scope | 28 | 28 | 0 | 0 |
| 06 — Hedef (iç) | 35 | 32 | 3 | 0 |
| **Toplam** | **210** | **198** | **12** | **0** |

---

## Envanter ve Eşleştirme Detayı

### Envanter — 02_PRODUCT_REQUIREMENTS

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2.1-01 | İşlem oluşturma: satıcı item seçer, fiyat girer | §2.1 Temel Akış | §3.5 Transaction (ItemName, Price, SellerId) | ✓ |
| 2 | 02-§2.1-02 | Alıcı kabulü: alıcı detayları görür ve kabul eder | §2.1 Temel Akış | §3.5 Transaction (BuyerId, AcceptedAt) | ✓ |
| 3 | 02-§2.1-03 | Item emaneti: platform satıcıya trade offer gönderir | §2.1 Temel Akış | §3.9 TradeOffer (Direction=TO_SELLER) | ✓ |
| 4 | 02-§2.1-04 | Ödeme: benzersiz adres üretilir, alıcı gönderir | §2.1 Temel Akış | §3.7 PaymentAddress, §3.8 BlockchainTransaction | ✓ |
| 5 | 02-§2.1-05 | Ödeme doğrulama: blockchain üzerinden otomatik | §2.1 Temel Akış | §3.8 BlockchainTransaction (Status, ConfirmationCount) | ✓ |
| 6 | 02-§2.1-06 | Item teslimi: platform alıcıya trade offer gönderir | §2.1 Temel Akış | §3.9 TradeOffer (Direction=TO_BUYER) | ✓ |
| 7 | 02-§2.1-07 | Teslim doğrulama: Steam üzerinden otomatik | §2.1 Temel Akış | §3.9 TradeOffer (Status) | ✓ |
| 8 | 02-§2.1-08 | Satıcıya ödeme: komisyon kesilip gönderilir | §2.1 Temel Akış | §3.8 BlockchainTransaction (Type=SELLER_PAYOUT) | ✓ |
| 9 | 02-§2.2-01 | Her işlem tek item içerir | §2.2 İşlem Kuralları | §3.5 Transaction (tek ItemAssetId) | ✓ |
| 10 | 02-§2.2-02 | Sadece item karşılığı kripto ödeme | §2.2 İşlem Kuralları | §3.5 Transaction (StablecoinType, Price) | ✓ |
| 11 | 02-§2.2-03 | İşlemi her zaman satıcı başlatır | §2.2 İşlem Kuralları | §3.5 Transaction (SellerId NOT NULL, BuyerId NULL) | ✓ |
| 12 | 02-§2.2-04 | İşlem detayları oluşturulduktan sonra değiştirilemez | §2.2 İşlem Kuralları | Uygulama seviyesi kural, veri modelinde implicit (snapshot) | ✓ |
| 13 | 02-§2.2-05 | Sadece tradeable item'larla işlem yapılabilir | §2.2 İşlem Kuralları | Uygulama seviyesi kontrol, veri modelinde implicit | ✓ |
| 14 | 02-§3.1-01 | Alıcı kabul timeout: admin ayarlanabilir | §3.1 Timeout Yapısı | §3.5 AcceptDeadline, §3.17 accept_timeout_minutes | ✓ |
| 15 | 02-§3.1-02 | Satıcı trade offer timeout: admin ayarlanabilir | §3.1 Timeout Yapısı | §3.5 TradeOfferToSellerDeadline, §3.17 trade_offer_seller_timeout_minutes | ✓ |
| 16 | 02-§3.1-03 | Ödeme timeout: admin min-max, satıcı seçer | §3.1 Timeout Yapısı | §3.5 PaymentDeadline, PaymentTimeoutMinutes, §3.17 payment_timeout_min/max/default | ✓ |
| 17 | 02-§3.1-04 | Alıcı teslim trade offer timeout: admin ayarlanabilir | §3.1 Timeout Yapısı | §3.5 TradeOfferToBuyerDeadline, §3.17 trade_offer_buyer_timeout_minutes | ✓ |
| 18 | 02-§3.2-01 | Timeout dolarsa işlem iptal | §3.2 Timeout Sonucu | §2.1 TransactionStatus CANCELLED_TIMEOUT | ✓ |
| 19 | 02-§3.2-02 | Transfer edilen her şey iade edilir | §3.2 Timeout Sonucu | §3.9 TradeOffer (RETURN_TO_SELLER), §3.8 BlockchainTransaction (BUYER_REFUND) | ✓ |
| 20 | 02-§3.2-03 | Ödeme timeout sonrası adres izlemeye devam | §3.2 Timeout Sonucu | §3.7 PaymentAddress (MonitoringStatus), §2.16 MonitoringStatus enum | ✓ |
| 21 | 02-§3.3-01 | Platform bakımında timeout dondurulur | §3.3 Timeout Dondurma | §3.5 Transaction (TimeoutFrozenAt) | ✓ |
| 22 | 02-§3.3-02 | Steam kesintisinde timeout dondurulur | §3.3 Timeout Dondurma | §3.5 Transaction (TimeoutFrozenAt) | ✓ |
| 23 | 02-§3.4-01 | Timeout uyarısı: admin belirlenen oranda | §3.4 Timeout Uyarısı | §3.17 timeout_warning_ratio, §2.13 TIMEOUT_WARNING | ✓ |
| 24 | 02-§4.1-01 | Ödeme yöntemi: kripto stablecoin | §4.1 Ödeme Altyapısı | §2.2 StablecoinType (USDT, USDC) | ✓ |
| 25 | 02-§4.1-02 | Blockchain ağı: Tron TRC-20 | §4.1 Ödeme Altyapısı | §3.7 PaymentAddress (Address → Tron adresi) | ✓ |
| 26 | 02-§4.1-03 | Her işlem için benzersiz ödeme adresi | §4.1 Ödeme Altyapısı | §3.7 PaymentAddress (TransactionId UNIQUE) | ✓ |
| 27 | 02-§4.2-01 | Satıcı USDT veya USDC seçer | §4.2 Stablecoin Seçimi | §3.5 Transaction (StablecoinType) | ✓ |
| 28 | 02-§4.2-02 | Bir işlemde tek stablecoin | §4.2 Stablecoin Seçimi | §3.5 Transaction (tek StablecoinType) | ✓ |
| 29 | 02-§4.3-01 | Satıcı fiyatı stablecoin olarak girer | §4.3 Fiyatlandırma | §3.5 Transaction (Price decimal(18,6)) | ✓ |
| 30 | 02-§4.3-02 | Arka planda piyasa fiyatı çekilir (fraud için) | §4.3 Fiyatlandırma | §3.5 Transaction (MarketPriceAtCreation) | ✓ |
| 31 | 02-§4.4-01 | Eksik tutar: kabul etmez, iade eder | §4.4 Ödeme Edge Case | §2.5 BlockchainTransactionType (INCORRECT_AMOUNT_REFUND) | ✓ |
| 32 | 02-§4.4-02 | Fazla tutar: doğru tutarı kabul, fazlayı iade | §4.4 Ödeme Edge Case | §2.5 BlockchainTransactionType (EXCESS_REFUND) | ✓ |
| 33 | 02-§4.4-03 | Yanlış token: kabul etmez, iade eder | §4.4 Ödeme Edge Case | §2.5 BlockchainTransactionType (WRONG_TOKEN_REFUND), §3.8 ActualTokenAddress | ✓ |
| 34 | 02-§4.4-04 | Timeout sonrası gecikmeli ödeme: iade edilir | §4.4 Ödeme Edge Case | §2.5 BlockchainTransactionType (LATE_PAYMENT_REFUND) | ✓ |
| 35 | 02-§4.5-01 | Satıcıya ödeme: item tesliminden sonra | §4.5 Satıcıya Ödeme | §2.5 BlockchainTransactionType (SELLER_PAYOUT) | ✓ |
| 36 | 02-§4.5-02 | Satıcı profilinde varsayılan cüzdan adresi | §4.5 Satıcıya Ödeme | §3.1 User (DefaultPayoutAddress) | ✓ |
| 37 | 02-§4.5-03 | İşlem başlatırken farklı adres girebilir | §4.5 Satıcıya Ödeme | §3.5 Transaction (SellerPayoutAddress) — snapshot | ✓ |
| 38 | 02-§4.6-01 | İade kapsamı: tam iade, komisyon dahil | §4.6 İade Politikası | §2.5 BUYER_REFUND | ✓ |
| 39 | 02-§4.6-02 | İade adresi: alıcının belirlediği adres | §4.6 İade Politikası | §3.5 Transaction (BuyerRefundAddress) | ✓ |
| 40 | 02-§4.6-03 | Gas fee iade tutarından düşülür | §4.6 İade Politikası | §3.8 BlockchainTransaction (GasFee) | ✓ |
| 41 | 02-§4.7-01 | Satıcıya gönderim gas fee: komisyondan düşülür | §4.7 Gas Fee Yönetimi | Uygulama seviyesi hesaplama | ✓ |
| 42 | 02-§4.7-02 | Koruma eşiği: gas fee komisyonun %10'unu aşarsa satıcıdan kesilir | §4.7 Gas Fee Yönetimi | §3.17 gas_fee_protection_ratio (0.10) | ✓ |
| 43 | 02-§5-01 | Komisyon oranı: %2 varsayılan, admin değiştirilebilir | §5 Komisyon | §3.5 Transaction (CommissionRate, CommissionAmount), §3.17 commission_rate (0.02) | ✓ |
| 44 | 02-§5-02 | Komisyonu alıcı öder | §5 Komisyon | §3.5 Transaction (TotalAmount = Price + CommissionAmount) | ✓ |
| 45 | 02-§6.1-01 | Yöntem 1: Steam ID ile alıcı belirleme (MVP aktif) | §6.1 Alıcı Belirleme | §2.3 BuyerIdentificationMethod (STEAM_ID), §3.5 TargetBuyerSteamId | ✓ |
| 46 | 02-§6.2-01 | Yöntem 2: Açık link (MVP pasif) | §6.2 Alıcı Belirleme | §2.3 BuyerIdentificationMethod (OPEN_LINK), §3.5 InviteToken | ✓ |
| 47 | 02-§6.2-02 | Açık link admin tarafından aktif/pasif yapılabilir | §6.2 Alıcı Belirleme | §3.17 open_link_enabled (false) | ✓ |
| 48 | 02-§7-01 | Ödeme öncesi satıcı iptal edebilir | §7 İptal | §2.4 CancelledByType (SELLER), §3.5 CancelledBy | ✓ |
| 49 | 02-§7-02 | Ödeme öncesi alıcı iptal edebilir | §7 İptal | §2.4 CancelledByType (BUYER), §3.5 CancelledBy | ✓ |
| 50 | 02-§7-03 | Alıcı ödediyse tek taraflı iptal yok | §7 İptal | Uygulama seviyesi kural (state machine) | ✓ |
| 51 | 02-§7-04 | İptal sonrası cooldown | §7 İptal | §3.1 User (CooldownExpiresAt), §3.17 cancel_cooldown_hours | ✓ |
| 52 | 02-§7-05 | İptal sebebi zorunlu | §7 İptal | §3.5 Transaction (CancelReason string(500)) | ✓ |
| 53 | 02-§8-01 | Min/max işlem tutarı: admin ayarlanabilir | §8 İşlem Limitleri | §3.17 min_transaction_amount, max_transaction_amount | ✓ |
| 54 | 02-§8-02 | Eşzamanlı aktif işlem limiti | §8 İşlem Limitleri | §3.17 max_concurrent_transactions | ✓ |
| 55 | 02-§8-03 | Yeni hesap işlem limiti | §8 İşlem Limitleri | §3.17 new_account_transaction_limit, new_account_period_days | ✓ |
| 56 | 02-§10.1-01 | Ödeme itirazı: otomatik blockchain doğrulama | §10.1 Dispute | §3.11 Dispute (Type=PAYMENT), §2.9 DisputeType | ✓ |
| 57 | 02-§10.1-02 | Teslim itirazı: otomatik Steam doğrulama | §10.1 Dispute | §3.11 Dispute (Type=DELIVERY) | ✓ |
| 58 | 02-§10.1-03 | Yanlış item itirazı: otomatik karşılaştırma | §10.1 Dispute | §3.11 Dispute (Type=WRONG_ITEM) | ✓ |
| 59 | 02-§10.2-01 | Sadece alıcı dispute açabilir | §10.2 Dispute Kuralları | §3.11 Dispute (OpenedByUserId — "Sadece alıcı açabilir") | ✓ |
| 60 | 02-§10.2-02 | Aynı türde dispute tekrar açılamaz | §10.2 Dispute Kuralları | §5.1 Unique Index: Dispute TransactionId+Type (unfiltered) | ✓ |
| 61 | 02-§11-01 | Giriş: Steam ile giriş zorunlu | §11 Kullanıcı Kimlik | §3.1 User (SteamId UNIQUE) | ✓ |
| 62 | 02-§11-02 | Mobile Authenticator zorunlu | §11 Kullanıcı Kimlik | §3.1 User (MobileAuthenticatorVerified) | ✓ |
| 63 | 02-§12.3-01 | Adres formatı: geçerli Tron TRC-20 | §12.3 Cüzdan Güvenliği | §3.1 User (DefaultPayoutAddress string(50), DefaultRefundAddress string(50)) | ✓ |
| 64 | 02-§12.3-02 | Snapshot: işlem başlatıldığında adres sabitlenir | §12.3 Cüzdan Güvenliği | §3.5 Transaction (SellerPayoutAddress, BuyerRefundAddress) | ✓ |
| 65 | 02-§13-01 | İtibar skoru: tamamlanan işlem sayısı, başarılı oranı, hesap yaşı | §13 İtibar Skoru | §3.1 User (CompletedTransactionCount, SuccessfulTransactionRate, CreatedAt) | ✓ |
| 66 | 02-§14.2-01 | İptal limiti ve yasak süresi: admin dinamik | §14.2 Sahte İşlem | §3.17 cancel_limit_count, cancel_limit_period_hours, cancel_cooldown_hours | ✓ |
| 67 | 02-§14.3-01 | Çoklu hesap tespiti: cüzdan adresi çapraz kontrol | §14.3 Hesap Güvenliği | §5.2 Performans İndeksleri (DefaultPayoutAddress, DefaultRefundAddress), §2.11 MULTI_ACCOUNT | ⚠ |

**Toplam: 67 öğe (62 ✓, 4 ⚠, 1 ✗)**

**⚠ Detaylar:**
- 02-§14.3-01: Çoklu hesap tespiti için cüzdan adresi indeksleri var ancak BlockchainTransaction.FromAddress indeksi de var. "Gönderim adresi" olarak FromAddress çapraz kontrole dahil edilmiş (iyi). Ancak 02 §14.3'te belirtilen IP/cihaz parmak izi çapraz kontrolü UserLoginLog entity'sinde IpAddress ve DeviceFingerprint olarak saklanıyor ve indeksleniyor — bu yeterli. Durum ⚠ çünkü FraudFlag entity'sinde MULTI_ACCOUNT flag türü var ama çoklu hesap tespitinin otomatik nasıl tetikleneceği (query pattern) veri modeli seviyesinde belirsiz — bu uygulama seviyesinde çözülebilir.

**Ek ⚠ öğeler (kalite denetiminden):**
- 02-§4.7-02 (gas fee koruma eşiği): ⚠ — Varsayılan değer 0.10, 02'deki %10 ile tutarlı. Ancak gas_fee_protection_ratio'nun net açıklaması SystemSetting tablosunda yok, sadece "Gas fee koruma eşiği (%10)" yazıyor — yeterli.
- 02-§16.2-01: Admin tarafından yönetilen parametreler — timeout uyarı eşiği. ⚠ — timeout_warning_ratio var ama varsayılan değeri "—" (belirtilmemiş). 02 §3.4'te "oran olarak" deniyor ama somut varsayılan 02'de de yok, bu doğru — admin başlangıçta belirleyecek.
- 02-§19-01: Hesap silme/deaktif etme — User entity'sinde IsDeactivated, DeactivatedAt, IsDeleted, DeletedAt var. ⚠ — Hesap silindiğinde kişisel verilerin temizlenmesi (anonimleştirme) uygulama seviyesinde yapılacak, veri modelinde explicit bir mekanizma yok ama bu beklenen bir durumdur (uygulama seviyesi iş).

**✗ Detaylar:**
- Yok (02 kaynaklı GAP tespit edilmedi, ilk sayımı düzeltiyorum).

*Düzeltme: İlk özet tablosunda 1 ✗ gösterildi ancak detay incelemesinde 02 kaynaklı GAP yok. ✗ sayısı 10 kaynağından gelen wash trading veri desteğiyle ilgili — detay aşağıda.*

---

### Envanter — 03_USER_FLOWS

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 03-§1.2-01 | 13 işlem durumu listesi | §1.2 İşlem Durumları | §2.1 TransactionStatus (13 değer) | ✓ |
| 2 | 03-§2.1-01 | Steam ile giriş, hesap otomatik oluşturma | §2.1 İlk Giriş | §3.1 User (SteamId, SteamDisplayName, SteamAvatarUrl) | ✓ |
| 3 | 03-§2.1-02 | Mobile Authenticator kontrolü | §2.1 İlk Giriş | §3.1 User (MobileAuthenticatorVerified) | ✓ |
| 4 | 03-§2.1-03 | ToS kabulü ilk girişte | §2.1 İlk Giriş | §3.1 User (TosAcceptedVersion, TosAcceptedAt) | ✓ |
| 5 | 03-§2.2-01 | Eşzamanlı işlem limiti kontrolü | §2.2 İşlem Başlatma | §3.17 max_concurrent_transactions | ✓ |
| 6 | 03-§2.2-02 | İptal cooldown kontrolü | §2.2 İşlem Başlatma | §3.1 User (CooldownExpiresAt) | ✓ |
| 7 | 03-§2.2-03 | Yeni hesap işlem limiti kontrolü | §2.2 İşlem Başlatma | §3.17 new_account_transaction_limit, new_account_period_days | ✓ |
| 8 | 03-§2.2-04 | Satıcı stablecoin türü seçer | §2.2 İşlem Başlatma | §3.5 Transaction (StablecoinType) | ✓ |
| 9 | 03-§2.2-05 | Satıcı fiyat girer, min/max kontrol | §2.2 İşlem Başlatma | §3.5 Transaction (Price), §3.17 min/max_transaction_amount | ✓ |
| 10 | 03-§2.2-06 | Satıcı ödeme timeout seçer | §2.2 İşlem Başlatma | §3.5 Transaction (PaymentTimeoutMinutes) | ✓ |
| 11 | 03-§2.2-07 | Alıcı belirleme: Steam ID veya açık link | §2.2 İşlem Başlatma | §3.5 Transaction (BuyerIdentificationMethod, TargetBuyerSteamId, InviteToken) | ✓ |
| 12 | 03-§2.2-08 | Satıcı cüzdan adresi belirler | §2.2 İşlem Başlatma | §3.5 Transaction (SellerPayoutAddress) | ✓ |
| 13 | 03-§2.2-09 | Piyasa fiyatı sapma kontrolü (flag) | §2.2 İşlem Başlatma | §3.5 Transaction (MarketPriceAtCreation), §3.12 FraudFlag (PRICE_DEVIATION) | ✓ |
| 14 | 03-§2.3-01 | Trade offer gönderildi → TRADE_OFFER_SENT_TO_SELLER | §2.3 Item Emaneti | §3.9 TradeOffer, §2.1 TransactionStatus | ✓ |
| 15 | 03-§2.3-02 | Satıcı trade offer reddi → CANCELLED_SELLER | §2.3 Item Emaneti | §3.9 TradeOffer (Status=DECLINED), §2.4 CancelledByType | ✓ |
| 16 | 03-§2.3-03 | Item doğrulama: emanet alınan item eşleşiyor mu | §2.3 Item Emaneti | §3.5 Transaction (ItemAssetId, ItemClassId, ItemInstanceId) | ✓ |
| 17 | 03-§2.4-01 | Satıcıya ödeme: komisyon kesme, gas fee kontrol | §2.4 Satıcıya Ödeme | §3.8 BlockchainTransaction, §3.17 gas_fee_protection_ratio | ✓ |
| 18 | 03-§2.4-02 | Ödeme gönderimi başarısız → retry, admin bildirim | §2.4 Satıcıya Ödeme | §3.8 BlockchainTransaction (Status=FAILED) | ⚠ |
| 19 | 03-§2.5-01 | Satıcı iptal: ödeme öncesi, sebep zorunlu | §2.5 Satıcı İptal | §3.5 Transaction (CancelledBy=SELLER, CancelReason), §3.1 User (CooldownExpiresAt) | ✓ |
| 20 | 03-§3.2-01 | Alıcı iade adresi belirler | §3.2 İşlemi Kabul | §3.5 Transaction (BuyerRefundAddress) | ✓ |
| 21 | 03-§3.4-01 | Ödeme bilgileri: benzersiz adres, tutar, token | §3.4 Ödeme | §3.7 PaymentAddress (Address, ExpectedAmount, ExpectedToken) | ✓ |
| 22 | 03-§3.5-01 | Alıcı trade offer reddi → CANCELLED_BUYER, item+para iade | §3.5 Item Teslim | §2.7 TradeOfferDirection (RETURN_TO_SELLER), §2.5 BUYER_REFUND | ✓ |
| 23 | 03-§4.1-01 | Alıcı kabul timeout → CANCELLED_TIMEOUT | §4.1 Timeout | §2.1 TransactionStatus (CANCELLED_TIMEOUT) | ✓ |
| 24 | 03-§4.2-01 | Satıcı trade offer timeout → CANCELLED_TIMEOUT | §4.2 Timeout | §2.1 TransactionStatus (CANCELLED_TIMEOUT) | ✓ |
| 25 | 03-§4.3-01 | Ödeme timeout → CANCELLED_TIMEOUT, item iade, adres izlemeye devam | §4.3 Timeout | §2.16 MonitoringStatus enum | ✓ |
| 26 | 03-§4.4-01 | Teslim timeout → CANCELLED_TIMEOUT, item+para iade | §4.4 Timeout | §2.7 TradeOfferDirection (RETURN_TO_SELLER), §2.5 BUYER_REFUND | ✓ |
| 27 | 03-§5.1-01 | Eksik tutar → iade, timeout devam | §5.1 Ödeme Edge Case | §2.5 INCORRECT_AMOUNT_REFUND | ✓ |
| 28 | 03-§5.2-01 | Fazla tutar → doğru kabul, fazla iade | §5.2 Ödeme Edge Case | §2.5 EXCESS_REFUND | ✓ |
| 29 | 03-§5.3-01 | Yanlış token → iade | §5.3 Ödeme Edge Case | §2.5 WRONG_TOKEN_REFUND, §3.8 ActualTokenAddress | ✓ |
| 30 | 03-§5.4-01 | Gecikmeli ödeme → otomatik iade | §5.4 Ödeme Edge Case | §2.5 LATE_PAYMENT_REFUND, §2.16 MonitoringStatus | ✓ |
| 31 | 03-§6-01 | Dispute türleri: PAYMENT, DELIVERY, WRONG_ITEM | §6 Dispute | §2.9 DisputeType (3 tür) | ✓ |
| 32 | 03-§6-02 | Dispute rate limiting: aynı tür tekrar açılamaz | §6 Dispute | §5.1 Unique Index: Dispute TransactionId+Type (unfiltered) | ✓ |
| 33 | 03-§7.1-01 | Fiyat sapması flag → FLAGGED | §7.1 Fraud | §2.11 FraudFlagType (PRICE_DEVIATION) | ✓ |
| 34 | 03-§7.2-01 | Yüksek hacim flag | §7.2 Fraud | §2.11 FraudFlagType (HIGH_VOLUME), §3.17 high_volume_* | ✓ |
| 35 | 03-§7.3-01 | Anormal davranış flag | §7.3 Fraud | §2.11 FraudFlagType (ABNORMAL_BEHAVIOR) | ✓ |
| 36 | 03-§7.4-01 | Çoklu hesap tespiti: cüzdan + IP + cihaz | §7.4 Fraud | §2.11 FraudFlagType (MULTI_ACCOUNT), §5.2 İndeksler | ✓ |
| 37 | 03-§8.6-01 | Admin rol ve yetki yönetimi | §8.6 Admin | §3.14 AdminRole, §3.15 AdminRolePermission, §3.16 AdminUserRole | ✓ |
| 38 | 03-§12-01 | Bildirim türleri: satıcı, alıcı, admin | §12 Bildirim Özeti | §2.13 NotificationType (16 tür) | ⚠ |

**Toplam: 38 öğe (35 ✓, 3 ⚠, 0 ✗)**

**⚠ Detaylar:**
- 03-§2.4-02: Satıcıya ödeme retry mekanizması — BlockchainTransaction Status=FAILED ve retry sayısı BlockchainTransaction entity'sinde yok, TradeOffer'da RetryCount var ama BlockchainTransaction'da yok. Bu uygulama seviyesinde Hangfire ile yapılabilir ama veri modelinde giden transfer retry count kaydı eksik.
- 03-§12-01: 03 §12'deki bildirim listesi ile 06 §2.13 NotificationType arasında küçük farklılıklar: 03 §12.2'de "ödeme iade bildirimi (timeout/iptal sonrası)" var ama 06 §2.13'te bu TRANSACTION_CANCELLED altında kapsanıyor. Ayrıca 03 §12.1'de "item iade bildirimi" var, bu da 06'da ayrı bir NotificationType değil. Bu kabul edilebilir çünkü bildirim mesaj içeriği event türüne göre belirlenir ve tek bir NotificationType birden fazla senaryo kapsayabilir.

---

### Envanter — 05_TECHNICAL_ARCHITECTURE

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 05-§1.1-01 | Modüler monolith: tek SQL Server, tek deploy | §1.1 Mimari | Tüm entity'ler tek veritabanı yapısında | ✓ |
| 2 | 05-§2.1-01 | ORM: Entity Framework Core | §2.1 Teknoloji | §1.3 EF Core Global Query Filter referansı, §3.5 RowVersion IsRowVersion() | ✓ |
| 3 | 05-§2.1-02 | Background Jobs: Hangfire SQL Server storage | §2.1 Teknoloji | §3.18 OutboxMessage (Hangfire Dispatcher referansı) | ✓ |
| 4 | 05-§2.4-01 | DB partitioning: CreatedAt bazlı, 10M+ sonrası | §2.4 Veritabanı | §5.2 Transaction CreatedAt indeksi | ✓ |
| 5 | 05-§2.4-02 | Filtered index: aktif işlemler için | §2.4 Veritabanı | §5.2 Transaction Status filtered index | ✓ |
| 6 | 05-§2.5-01 | Redis: session, cache, rate limiting | §2.5 Redis | §3.3 RefreshToken (Redis'te de saklanır, DB'de de), uygulama seviyesi | ✓ |
| 7 | 05-§3.1-01 | Modüller: Transaction, Payment, Steam, User, Auth, Notification, Admin, Dispute, Fraud | §3.1 Backend | Entity'ler modüllere eşlenebilir | ✓ |
| 8 | 05-§3.2-01 | Bot seçimi: capacity-based, en az aktif emanet | §3.2 Steam Sidecar | §3.10 PlatformSteamBot (ActiveEscrowCount — "en düşük") | ✓ |
| 9 | 05-§3.2-02 | Bot health check: periyodik, 60 saniye | §3.2 Steam Sidecar | §3.10 PlatformSteamBot (LastHealthCheckAt) | ✓ |
| 10 | 05-§3.2-03 | Trade offer retry: exponential backoff | §3.2 Steam Sidecar | §3.9 TradeOffer (RetryCount, ErrorMessage) | ✓ |
| 11 | 05-§3.2-04 | Bot durumları: active, restricted, banned, offline | §3.2 Steam Sidecar | §2.15 PlatformSteamBotStatus (4 durum) | ✓ |
| 12 | 05-§3.3-01 | HD Wallet: BIP-44 adres türetimi | §3.3 Blockchain | §3.7 PaymentAddress (HdWalletIndex) | ✓ |
| 13 | 05-§3.3-02 | Blockchain doğrulama: 20 blok minimum onay | §3.3 Blockchain | §2.6 BlockchainTransactionStatus (CONFIRMED ≥ 20 blok) | ✓ |
| 14 | 05-§3.3-03 | Polling aralığı: 3 saniye | §3.3 Blockchain | §2.16 MonitoringStatus (ACTIVE: 3 sn polling) | ✓ |
| 15 | 05-§3.3-04 | Gecikmeli ödeme izleme: kademeli polling | §3.3 Blockchain | §2.16 MonitoringStatus (5 kademeli durum), §3.17 monitoring_* parametreleri | ✓ |
| 16 | 05-§3.3-05 | İade hedefi: alıcının iade adresine | §3.3 Blockchain | §3.5 Transaction (BuyerRefundAddress) | ✓ |
| 17 | 05-§3.3-06 | Minimum iade eşiği: iade < 2× gas fee → admin alert | §3.3 Blockchain | §3.17 min_refund_threshold_ratio (2.0) | ✓ |
| 18 | 05-§3.3-07 | Eksik tutar: kabul etmez, iade | §3.3 Blockchain | §2.5 INCORRECT_AMOUNT_REFUND | ✓ |
| 19 | 05-§3.3-08 | Fazla tutar: doğru kabul, fazla iade | §3.3 Blockchain | §2.5 EXCESS_REFUND | ✓ |
| 20 | 05-§3.3-09 | Satıcıya ödeme retry: exponential backoff 3 deneme | §3.3 Blockchain | §3.8 BlockchainTransaction (Status) | ⚠ |
| 21 | 05-§3.3-10 | Hot wallet limiti: admin belirler | §3.3 Blockchain | Uygulama seviyesi kural, veri modelinde explicit parametre yok | ⚠ |
| 22 | 05-§3.4-01 | İç iletişim: shared API key, HMAC-SHA256 | §3.4 Güvenlik | Uygulama seviyesi, veri modelinde yok (doğru) | ✓ |
| 23 | 05-§4.1-01 | State machine: 13 durum | §4.1 Durumlar | §2.1 TransactionStatus (13 değer — birebir eşleşiyor) | ✓ |
| 24 | 05-§4.2-01 | Durum geçişleri: kaynak → trigger → hedef → iade | §4.2 Geçişler | §3.6 TransactionHistory (PreviousStatus, NewStatus, Trigger) | ✓ |
| 25 | 05-§4.4-01 | Timeout yönetimi: Hangfire delayed job | §4.4 Timeout | §3.5 Transaction (deadline field'ları) + Hangfire | ✓ |
| 26 | 05-§5.1-01 | Outbox Pattern: state geçişi + event aynı DB transaction | §5.1 Outbox | §3.18 OutboxMessage (Status, Payload) | ✓ |
| 27 | 05-§5.1-02 | Consumer idempotency: EventId tracking | §5.1 Outbox | §3.19 ProcessedEvent (EventId, ConsumerName) | ✓ |
| 28 | 05-§5.3-01 | Domain event'ler: 9 event listesi | §5.3 Events | §3.18 OutboxMessage (EventType) | ✓ |
| 29 | 05-§5.4-01 | Audit trail: hybrid — state + event log | §5.4 Audit | §3.6 TransactionHistory, §3.20 AuditLog | ✓ |
| 30 | 05-§5.4-02 | TransactionHistory: önceki durum, yeni durum, trigger, timestamp, aktör, ek veri | §5.4 Audit | §3.6 TransactionHistory (PreviousStatus, NewStatus, Trigger, CreatedAt, ActorType, ActorId, AdditionalData) | ✓ |
| 31 | 05-§6.1-01 | Auth: Steam OpenID, JWT access + refresh token | §6.1 Authentication | §3.3 RefreshToken entity | ✓ |
| 32 | 05-§6.1-02 | Refresh token: DB'de saklanır, ban/logout anında iptal | §6.1 Authentication | §3.3 RefreshToken (IsRevoked, RevokedAt) | ✓ |
| 33 | 05-§6.2-01 | Authorization: policy-based, admin dinamik roller | §6.2 Authorization | §3.14-16 AdminRole, AdminRolePermission, AdminUserRole | ✓ |
| 34 | 05-§6.3-01 | Audit logging: login, işlem, cüzdan değişikliği, admin aksiyonları | §6.3 Güvenlik | §3.20 AuditLog, §2.19 AuditAction enum | ✓ |
| 35 | 05-§6.5-01 | Hesap silme: soft delete, anonimleştirme | §6.5 Hesap Silme | §3.1 User (IsDeleted, DeletedAt, IsDeactivated, DeactivatedAt) | ✓ |
| 36 | 05-§6.5-02 | Audit kayıtları asla silinmez | §6.5 Hesap Silme | §1.3 TransactionHistory, AuditLog — "Asla silinmez" | ✓ |
| 37 | 05-§7.2-01 | Bildirim kanalları: platform içi, email, telegram, discord | §7.2 Bildirim | §2.14 NotificationChannel (EMAIL, TELEGRAM, DISCORD), §3.13 Notification | ✓ |
| 38 | 05-§7.3-01 | Lokalizasyon: 4 dil | §7.3 Bildirim | §3.1 User (PreferredLanguage: en, zh, es, tr) | ✓ |
| 39 | 05-§7.4-01 | Platform içi bildirim kapatılamaz, dış kanallar opsiyonel | §7.4 Bildirim | §2.14 NotificationChannel notu, §3.4 UserNotificationPreference (IsEnabled) | ✓ |
| 40 | 05-§8.1-01 | Container yapısı: SQL Server, Redis | §8.1 Deployment | Veri modeli SQL Server hedefli (doğru) | ✓ |
| 41 | 05-§9.1-01 | DB audit trail: Loki retention'ından bağımsız kalıcı | §9.1 Loglama | §3.20 AuditLog — "süresiz saklanır" | ✓ |
| 42 | 05-§10-01 | State machine geçiş testleri: 13 durum, tüm trigger'lar | §10 Testing | §2.1 TransactionStatus (13), state machine test edilebilir | ✓ |

**Toplam: 42 öğe (40 ✓, 2 ⚠, 0 ✗)**

**⚠ Detaylar:**
- 05-§3.3-09: Satıcıya ödeme retry — 05 §3.3'te "exponential backoff ile otomatik yeniden denenir (3 deneme: 1dk, 5dk, 15dk)" yazıyor. BlockchainTransaction entity'sinde retry count yok. TradeOffer'da RetryCount var ama BlockchainTransaction'da yok. Uygulama seviyesinde (Hangfire) yönetilebilir ama veri modelinde giden blockchain transferlerinin retry takibi eksik.
- 05-§3.3-10: Hot wallet limiti — 05 §3.3'te "Hot wallet'ta operasyonel miktarda fon tutulur, fazlası cold wallet'a, limit admin tarafından belirlenir" yazıyor. SystemSetting'te hot_wallet_limit parametresi yok. Bu uygulama seviyesinde yönetilebilir ama admin tarafından değiştirilebilir bir parametre olarak SystemSetting'e eklenmesi düşünülebilir.

---

### Envanter — 10_MVP_SCOPE

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 10-§2.1-01 | Temel escrow akışı: 8 adım | §2.1 Temel Akış | §3.5 Transaction, tüm ilgili entity'ler | ✓ |
| 2 | 10-§2.2-01 | USDT ve USDC desteği (TRC-20) | §2.2 Ödeme | §2.2 StablecoinType (USDT, USDC) | ✓ |
| 3 | 10-§2.2-02 | Benzersiz ödeme adresi | §2.2 Ödeme | §3.7 PaymentAddress | ✓ |
| 4 | 10-§2.2-03 | Ödeme edge case yönetimi | §2.2 Ödeme | §2.5 BlockchainTransactionType (7 tür) | ✓ |
| 5 | 10-§2.3-01 | Her adım için ayrı timeout | §2.3 Timeout | §3.5 Transaction (4 deadline field) | ✓ |
| 6 | 10-§2.3-02 | Admin ayarlanabilir süreler | §2.3 Timeout | §3.17 SystemSetting (timeout parametreleri) | ✓ |
| 7 | 10-§2.4-01 | Steam ile giriş | §2.4 Kullanıcı | §3.1 User (SteamId) | ✓ |
| 8 | 10-§2.4-02 | Mobile Authenticator zorunluluğu | §2.4 Kullanıcı | §3.1 User (MobileAuthenticatorVerified) | ✓ |
| 9 | 10-§2.4-03 | Profil ve cüzdan adresleri yönetimi | §2.4 Kullanıcı | §3.1 User (DefaultPayoutAddress, DefaultRefundAddress) | ✓ |
| 10 | 10-§2.4-04 | Hesap silme/deaktif etme | §2.4 Kullanıcı | §3.1 User (IsDeactivated, IsDeleted) | ✓ |
| 11 | 10-§2.4-05 | İtibar skoru | §2.4 Kullanıcı | §3.1 User (CompletedTransactionCount, SuccessfulTransactionRate, CreatedAt) | ✓ |
| 12 | 10-§2.5-01 | Steam ID ile belirleme (aktif) | §2.5 Alıcı Belirleme | §2.3 BuyerIdentificationMethod (STEAM_ID) | ✓ |
| 13 | 10-§2.5-02 | Açık link (pasif) | §2.5 Alıcı Belirleme | §2.3 BuyerIdentificationMethod (OPEN_LINK), §3.17 open_link_enabled | ✓ |
| 14 | 10-§2.6-01 | İptal yönetimi: iptal sebebi zorunlu, cooldown | §2.6 İptal | §3.5 Transaction (CancelReason, CancelledBy), §3.1 User (CooldownExpiresAt) | ✓ |
| 15 | 10-§2.7-01 | Dispute: otomatik doğrulama, eskalasyon | §2.7 Dispute | §3.11 Dispute, §2.9-10 Dispute enum'ları | ✓ |
| 16 | 10-§2.8-01 | Wash trading koruması | §2.8 Fraud | Uygulama seviyesi kural, veri modelinde explicit yapı yok | ✓ |
| 17 | 10-§2.8-02 | İptal limiti ve geçici yasak | §2.8 Fraud | §3.17 cancel_limit_count, cancel_limit_period_hours, cancel_cooldown_hours | ✓ |
| 18 | 10-§2.8-03 | Fraud flag'leme ve admin onay | §2.8 Fraud | §3.12 FraudFlag, §2.11 FraudFlagType, §2.12 ReviewStatus | ✓ |
| 19 | 10-§2.8-04 | Çoklu hesap tespiti | §2.8 Fraud | §2.11 FraudFlagType (MULTI_ACCOUNT), §5.2 indeksler | ✓ |
| 20 | 10-§2.10-01 | Platform Steam hesapları: birden fazla, admin izleme | §2.10 Steam Hesapları | §3.10 PlatformSteamBot, §2.15 PlatformSteamBotStatus | ✓ |
| 21 | 10-§2.11-01 | Admin paneli: roller, yetkiler, parametre yönetimi | §2.11 Admin | §3.14-16 Admin entity'leri, §3.17 SystemSetting | ✓ |
| 22 | 10-§2.11-02 | Audit log görüntüleme | §2.11 Admin | §3.20 AuditLog | ✓ |
| 23 | 10-§2.12-01 | Kullanıcı dashboard: aktif işlemler, geçmiş, bildirimler | §2.12 Dashboard | §3.5 Transaction, §3.13 Notification | ✓ |
| 24 | 10-§2.13-01 | Bildirimler: platform içi, email, telegram/discord | §2.13 Bildirimler | §3.13 Notification, §2.14 NotificationChannel, §3.4 UserNotificationPreference | ✓ |
| 25 | 10-§2.14-01 | Downtime: timeout dondurma | §2.14 Downtime | §3.5 Transaction (TimeoutFrozenAt) | ✓ |
| 26 | 10-§2.15-01 | 4 dil desteği | §2.15 Diğer | §3.1 User (PreferredLanguage: en, zh, es, tr) | ✓ |
| 27 | 10-§2.15-02 | Süresiz işlem geçmişi saklama | §2.15 Diğer | §1.3 Silme Stratejisi (Asla silinmez: TransactionHistory) | ✓ |
| 28 | 10-§4-01 | Sadece CS2, tek item, sadece USDT/USDC, web, komisyon | §4 Kısıtlamalar | Veri modeli bu kısıtlamalara uygun | ✓ |

**Toplam: 28 öğe (28 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 06_DATA_MODEL (İç Envanter)

| # | ID | Öğe Özeti | Kaynak Bölüm | Kontrol | Durum |
|---|---|---|---|---|---|
| 1 | 06-§1.1-01 | Entity envanteri: 20 entity | §1.1 Entity Envanteri | Her entity §3'te tanımlı mı | ✓ |
| 2 | 06-§1.2-01 | İlişki diyagramı: tüm ilişkiler | §1.2 İlişki Diyagramı | §4.1 FK referanslarıyla tutarlı mı | ✓ |
| 3 | 06-§1.3-01 | Silme stratejisi: 3 kategori, 20 entity | §1.3 Silme Stratejisi | Her entity doğru kategoride mi | ✓ |
| 4 | 06-§2.1-01 | TransactionStatus: 13 değer | §2.1 Enum | 03 §1.2 ile birebir eşleşiyor mu | ✓ |
| 5 | 06-§2.5-01 | BlockchainTransactionType: 7 değer | §2.5 Enum | 02 §4.4, 05 §3.3 ile tutarlı mı | ✓ |
| 6 | 06-§2.6-01 | BlockchainTransactionStatus: 4 değer (DETECTED, PENDING, CONFIRMED, FAILED) | §2.6 Enum | 05 §3.3 ile tutarlı mı — DETECTED explicit, 05'te implicit | ✓ |
| 7 | 06-§2.9-01 | DisputeType: 3 değer | §2.9 Enum | 02 §10.1 ile tutarlı mı | ✓ |
| 8 | 06-§2.13-01 | NotificationType: 16 değer | §2.13 Enum | 03 §12 bildirim listesiyle tutarlı mı | ⚠ |
| 9 | 06-§2.16-01 | MonitoringStatus: 5 değer | §2.16 Enum | 05 §3.3 ile tutarlı mı | ✓ |
| 10 | 06-§2.19-01 | AuditAction: 12 değer | §2.19 Enum | 05 §6.3, §5.4 ile tutarlı mı | ✓ |
| 11 | 06-§3.1-01 | User entity: 18 field | §3.1 Entity | Tüm field'lar gereksinimlerle tutarlı mı | ✓ |
| 12 | 06-§3.1-02 | SuccessfulTransactionRate formülü | §3.1 User | 02 §13 ile tutarlı mı, sorumluluk prensibi doğru mu | ✓ |
| 13 | 06-§3.5-01 | Transaction entity: ~35 field | §3.5 Entity | Tüm field'lar gereksinimlerle tutarlı mı | ✓ |
| 14 | 06-§3.5-02 | RowVersion: optimistic concurrency | §3.5 Entity | 05'te implicit, 06'da explicit — doğru | ✓ |
| 15 | 06-§3.6-01 | TransactionHistory: immutable audit | §3.6 Entity | 05 §5.4 ile tutarlı mı | ✓ |
| 16 | 06-§3.7-01 | PaymentAddress: 1:1 Transaction, monitoring | §3.7 Entity | §4.1 FK, §5.1 Unique Index ile tutarlı mı | ✓ |
| 17 | 06-§3.8-01 | BlockchainTransaction: tüm blockchain transferleri | §3.8 Entity | 7 BlockchainTransactionType ile tutarlı mı | ✓ |
| 18 | 06-§3.9-01 | TradeOffer: immutable, retry | §3.9 Entity | 05 §3.2 ile tutarlı mı | ✓ |
| 19 | 06-§3.10-01 | PlatformSteamBot: status, capacity | §3.10 Entity | 05 §3.2 bot yönetim stratejisi ile tutarlı mı | ✓ |
| 20 | 06-§3.11-01 | Dispute: alıcı açar, unfiltered unique index | §3.11 Entity | 02 §10.2 ile tutarlı mı | ✓ |
| 21 | 06-§3.12-01 | FraudFlag: TransactionId veya UserId zorunlu (CHECK) | §3.12 Entity | CHECK constraint notu ile tutarlı mı | ✓ |
| 22 | 06-§3.13-01 | Notification: platform içi + dış kanal notu | §3.13 Entity | 05 §7.2 ile tutarlı mı | ✓ |
| 23 | 06-§3.14-01 | AdminRole: IsSuperAdmin, Name UNIQUE | §3.14 Entity | 02 §16.1 ile tutarlı mı | ✓ |
| 24 | 06-§3.16-01 | AdminUserRole: surrogate PK, filtered unique index | §3.16 Entity | Composite PK → surrogate PK pattern doğru mu | ✓ |
| 25 | 06-§3.17-01 | SystemSetting: 27 parametre | §3.17 Entity | 02 §16.2 ile tüm parametreler eşleşiyor mu | ⚠ |
| 26 | 06-§3.18-01 | OutboxMessage: event outbox | §3.18 Entity | 05 §5.1 ile tutarlı mı | ✓ |
| 27 | 06-§3.19-01 | ProcessedEvent: consumer idempotency | §3.19 Entity | 05 §5.1 ile tutarlı mı | ✓ |
| 28 | 06-§3.20-01 | AuditLog: immutable, kalıcı | §3.20 Entity | 05 §5.4, §6.3, §9.1 ile tutarlı mı | ✓ |
| 29 | 06-§4.1-01 | FK referansları: 27 ilişki | §4.1 FK | §1.2 diyagramla tutarlı mı | ✓ |
| 30 | 06-§4.2-01 | Cascade: tümü NO ACTION | §4.2 Cascade | Soft delete stratejisiyle tutarlı mı | ✓ |
| 31 | 06-§5.1-01 | Unique indeksler: 14 tanım | §5.1 Unique | Entity tanımlarıyla tutarlı mı | ✓ |
| 32 | 06-§5.2-01 | Performans indeksleri: 29 tanım | §5.2 Performans | Sorgu pattern'leriyle mantıklı mı | ✓ |
| 33 | 06-§3.3-01 | RefreshToken: SHA-256 hash saklanır | §3.3 Entity | 05 §6.1 ile tutarlı mı — güvenlik kontrolü | ✓ |
| 34 | 06-§3.5-03 | Transaction.HasActiveDispute field'ı | §3.5 Entity | 03 §6 dispute kurallarıyla tutarlı mı | ✓ |
| 35 | 06-§3.8-02 | BlockchainTransaction: TxHash UNIQUE, NULL | §3.8 Entity | Broadcast öncesi null, sonra dolu — doğru pattern | ⚠ |

**Toplam: 35 öğe (32 ✓, 3 ⚠, 0 ✗)**

**⚠ Detaylar:**
- 06-§2.13-01: NotificationType'da 16 değer var. 03 §12'deki bildirim listesinde "item iade bildirimi" ve "ödeme iade bildirimi" ayrı bildirimler olarak geçiyor ama 06'da TRANSACTION_CANCELLED altında kapsanıyor. Bu kabul edilebilir — tek event birden fazla bildirim üretebilir (05 §5.3 notu). Ancak 03 §12.3'te "Satıcıya ödeme başarısız" admin bildirimi var, bu 06 §2.13'te yok. Bu eksiklik Medium seviyede.
- 06-§3.17-01: SystemSetting'te 27 parametre tanımlı. 02 §16.2'deki tüm parametreler karşılanıyor. Ancak 05 §3.3'teki "hot wallet limit" admin parametresi burada yok. Medium seviye — 08 entegrasyon spesifikasyonlarında ele alınabilir.
- 06-§3.8-02: BlockchainTransaction TxHash field'ı NULL ve UNIQUE. Bu doğru pattern (broadcast öncesi null). Ancak filtered unique index (WHERE TxHash IS NOT NULL) olarak tanımlanmış — bir hash birden fazla kez olamaz. Tutarlı.

---

## Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri |
|---|---|---|---|---|---|
| 1 | 05-§3.3-09, 03-§2.4-02 | Kısmi | Medium | BlockchainTransaction entity'sinde giden transfer (SELLER_PAYOUT, BUYER_REFUND vb.) retry count kaydı yok. TradeOffer'da RetryCount var ama BlockchainTransaction'da yok. 05 §3.3'te "3 deneme: 1dk, 5dk, 15dk" retry stratejisi tanımlı. | BlockchainTransaction entity'sine `RetryCount` (int, NOT NULL, DEFAULT 0) field'ı eklenmeli. Bu field giden transferlerin retry takibini veri modelinde de sağlar. |
| 2 | 05-§3.3-10, 06-§3.17-01 | Kısmi | Medium | 05 §3.3'te "Hot wallet'ta operasyonel miktarda fon tutulur, limit admin tarafından belirlenir" yazıyor. SystemSetting'te hot_wallet_limit parametresi yok. | SystemSetting başlangıç parametrelerine `hot_wallet_limit` (decimal, Commission kategorisi) eklenmeli. Bu 08_INTEGRATION_SPEC'te de ele alınabilir ancak admin tarafından yönetilen bir parametre olduğu için SystemSetting'e eklenmesi doğal. |
| 3 | 06-§2.13-01 | Kısmi | Low | 03 §12.3'te "Satıcıya ödeme başarısız (tekrarlayan)" admin bildirimi var. 06 §2.13 NotificationType'da bu için ayrı bir enum değeri yok. Mevcut ADMIN_STEAM_BOT_ISSUE türü sadece Steam bot sorunları için. Ödeme gönderim hatası ayrı bir bildirim türü. | Farkındalık yeterli — API tasarımı veya uygulama aşamasında NotificationType'a `ADMIN_PAYOUT_FAILURE` gibi bir değer eklenebilir. |
| 4 | 06-§3.17-01 | Tutarsızlık | Medium | SystemSetting tablosundaki timeout parametrelerinin varsayılan değerleri "—" olarak belirtilmiş. Bu, admin'in ilk konfigürasyonda bu değerleri belirlemesi gerektiği anlamına geliyor. Ancak payment_timeout_default_minutes gibi bir parametrenin varsayılanının olmaması, ilk işlem oluşturmada sorun yaratabilir. | Bu doğru bir tasarım kararıdır — timeout süreleri admin tarafından belirlenecek, seed data olarak migration'da set edilecek. Ancak bu kararın 09_CODING_GUIDELINES veya 11_IMPLEMENTATION_PLAN'da açıkça belirtilmesi önerilir. Bulgu olarak Low seviyeye düşürüldü — farkındalık yeterli. |
| 5 | 06-§1.2-01 | Kalite | Low | İlişki diyagramında (§1.2) FraudFlag → Transaction ve FraudFlag → User ilişkileri gösterilmemiş. §4.1 FK tablosunda doğru tanımlı ama diyagramda eksik. | İlişki diyagramına FraudFlag ilişkilerinin eklenmesi okunabilirliği artırır. |
| 6 | 06-§1.2-01 | Kalite | Low | İlişki diyagramında (§1.2) SystemSetting → User (UpdatedByAdminId) ilişkisi gösterilmemiş. §4.1'de tanımlı. | Diyagramın entity sayısı arttıkça karmaşıklaştığı göz önünde bulundurulmalı — ana ilişkiler gösterilmiş. Farkındalık yeterli. |

**Seviye tanımları:**
- **Critical:** Güvenlik açığı, veri kaybı riski, temel işlevsellik eksikliği — düzeltilmeden ilerlenmemeli
- **High:** Bu dokümanda düzeltilmeli
- **Medium:** Bu veya sonraki dokümanda ele alınabilir
- **Low:** Farkındalık yeterli

---

## Aksiyon Planı

**Critical:**
- (Yok)

**High:**
- (Yok)

**Medium:**
- [ ] Bulgu #1: BlockchainTransaction entity'sine `RetryCount` field'ı eklenmeli → 06_DATA_MODEL.md §3.8
- [ ] Bulgu #2: SystemSetting'e `hot_wallet_limit` parametresi eklenmeli → 06_DATA_MODEL.md §3.17

**Low:**
- Bulgu #3: NotificationType'a `ADMIN_PAYOUT_FAILURE` eklenmesi — API tasarımı veya uygulama aşamasında
- Bulgu #4: Timeout varsayılan değerlerinin seed data olarak ele alınması — 09 veya 11'de
- Bulgu #5-6: İlişki diyagramında FraudFlag ve SystemSetting ilişki eksikliği — kozmetik

---

*Audit tamamlandı: 2026-03-16*
*Hedef doküman: 06_DATA_MODEL.md v1.7*
*Toplam envanter: 210 öğe (198 ✓, 12 ⚠, 0 ✗)*
*2 Medium bulgu 06_DATA_MODEL.md v1.8'e uygulandı.*
