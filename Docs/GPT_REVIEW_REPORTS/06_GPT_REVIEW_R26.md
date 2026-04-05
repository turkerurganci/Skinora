# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 26
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: SystemSetting fail-fast kurgusu ilk kurulumda bootstrap kilidi üretiyor
- **Seviye:** KRİTİK
- **Kategori:** Operasyonel semantik / Lifecycle
- **Konum:** §3.17 SystemSetting, §8.9 Seed Data
- **Sorun:** Varsayılanı olmayan ayarlar `Value = NULL, IsConfigured = false` seed ediliyor ve startup'ta fail-fast uygulanıyor. Admin paneli uygulama içinde olduğundan ilk kurulumda bootstrap deadlock: app açılmaz → admin panel erişilemez → ayar yapılandırılamaz → app açılmaz.
- **Öneri:** Açık bir bootstrap yolu tanımlanmalı (bootstrap mode, env var hydration, daraltılmış admin shell vb.).

### BULGU-2: TimeoutWarningSentAt tek alanı, çok aşamalı timeout modelini tek anlamlı taşımıyor
- **Seviye:** ORTA
- **Kategori:** State-dependent kural / Yapısal tutarlılık
- **Konum:** §3.5 Transaction, timeout alanları ve state→deadline/job matrisi
- **Sorun:** Dört ayrı deadline evresi var ama warning takibi için tek TimeoutWarningSentAt alanı var. Alanın hangi aşamada geçerli olduğu ve state geçişinde reset kuralı explicit yazılmamış.
- **Öneri:** Warning takibini stage-bazlı hale getir veya en azından "hangi state geçişinde warning tracking resetlenir" kuralı açık yazılmalı.

### BULGU-3: NotificationDelivery için bağımsız bildirim yaşam döngüsü eksik
- **Seviye:** ORTA
- **Kategori:** Lifecycle / Retention / Constraint bütünlüğü
- **Konum:** §1.3 silme stratejisi, §3.13 Notification, §3.13a NotificationDelivery, §6.1, §8.8
- **Sorun:** Transaction-linked notification delivery lifecycle net. Ama Notification(TransactionId = NULL) purge edildiğinde child NotificationDelivery kayıtlarının ne olacağı tanımlı değil — orphan row riski.
- **Öneri:** NotificationDelivery için bağımsız bildirim lifecycle zinciri tanımla.

### BULGU-4: TransactionHistory / AuditLog aktör semantiği için explicit invariant eksik
- **Seviye:** ORTA
- **Kategori:** Audit bütünlüğü / Operasyonel semantik
- **Konum:** §3.6 TransactionHistory, §3.20 AuditLog, §8.5 admin aksiyonu invariantı, §8.9 SYSTEM sentinel
- **Sorun:** Admin FK invariantı ve transaction aktör invariantları güzel tanımlı. Ama audit kayıtlarının kalbi olan ActorType + ActorId çifti için aynı kesinlikte normatif invariant yok. SYSTEM sentinel, admin tarihsel güvencesi dağınık.
- **Öneri:** §8.5/§8.6 yanına "Audit Aktör İnvariantı" bölümü eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | SystemSetting bootstrap deadlock | ✅ KABUL | §8.9 fail-fast + §3.17 admin yönetimi = ilk kurulumda deadlock somut. R25'te delete yasağını çözdük ama bootstrap yolunu tanımlamadık. | §8.9'a env var override bootstrap mekanizması eklendi |
| 2 | TimeoutWarningSentAt scope belirsizliği | ⚠️ KISMİ | Çok aşamalı warning riski yok — alan yalnızca ödeme aşamasında kullanılıyor. Ama bu sınırlama + state geçişi reset kuralı explicit yazılmamıştı. | Field açıklamalarına "yalnızca ITEM_ESCROWED" scope notu + reset kuralı eklendi. State→deadline matrisine warning field reset açıklaması eklendi |
| 3 | Bağımsız notification delivery lifecycle | ✅ KABUL | Transaction-linked delivery net ama TransactionId = NULL notification purge edildiğinde child delivery kayıtları için lifecycle tanımsız. Orphan riski gerçek. | §3.13a silme politikasına bağımsız bildirim delivery purge kuralı, §6.1 retention tablosuna NotificationDelivery referansı eklendi |
| 4 | Audit aktör invariantı eksik | ✅ KABUL | §8.5 admin FK, §8.6 transaction aktör invariantları mevcut ama ActorType + ActorId çifti için normatif kural dağınık. Audit trail güvenilirliği için merkezi invariant gerekli. | §8.6a "Audit Aktör İnvariantı" bölümü eklendi — SYSTEM/ADMIN/USER kuralları, enforcement mekanizması |

---

## Uygulanan Düzeltmeler

- [x] §8.9: Bootstrap konfigürasyon yolu — env var override mekanizması (SKINORA_SETTING_{KEY_UPPER}), startup sırası, güvenlik kuralı
- [x] §3.5: TimeoutWarningJobId ve TimeoutWarningSentAt field açıklamalarına "yalnızca ITEM_ESCROWED" scope notu eklendi
- [x] §3.5: TimeoutWarningSentAt reset kuralı — ITEM_ESCROWED girişte NULL, çıkışta NULL, freeze resume sonrası NULL
- [x] §3.5: State→deadline matrisine warning field reset açıklaması eklendi
- [x] §3.13a: Bağımsız bildirim delivery lifecycle — parent notification purge edildiğinde birlikte purge, sıra: delivery → notification
- [x] §6.1: Notification + NotificationDelivery birlikte retention/purge kuralı
- [x] §8.6a: Audit Aktör İnvariantı bölümü — SYSTEM/ADMIN/USER kuralları, genel kural, enforcement mekanizması
- [x] Versiyon v4.8 → v4.9

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 27 tetiklendi → **SONUÇ: TEMİZ** — döngü tamamlandı (R1-R26, 26 round)
