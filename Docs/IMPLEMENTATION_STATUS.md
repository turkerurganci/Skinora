# Skinora — Implementation Status

**Son güncelleme:** 2026-04-06

---

## Durum Lejandı

| Simge | Durum | Açıklama |
|---|---|---|
| ⬚ | Bekliyor | Henüz başlanmadı |
| ⏳ | Devam ediyor | Yapım chat'inde aktif |
| ✓ | Tamamlandı | Doğrulama PASS, main'e merge edildi |
| ✗ | FAIL | Doğrulama başarısız, düzeltme bekleniyor |
| ⛔ | BLOCKED | İlerleyemiyor (alt tür: SPEC_GAP / DEPENDENCY_MISMATCH / PLAN_CORRECTION / EXTERNAL) |

**Doğrulama durumları:** ✓ PASS / ✗ FAIL / ⛔ BLOCKED

**Detaylı raporlar:** `Docs/TASK_REPORTS/TXX_REPORT.md`

---

## F0 — Proje İskeleti (T01–T16)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T01 | .NET Solution ve proje yapısı oluşturma | ✓ Tamamlandı | ✓ PASS | `b1ad141` |
| T02 | Docker Compose ve ortam konfigürasyonu | ✓ Tamamlandı | ✓ PASS | `61908d8` |
| T03 | Shared Kernel — base sınıflar, exception'lar, interface'ler | ✓ Tamamlandı | ✓ PASS | `f4e4a6f` |
| T04 | EF Core global konfigürasyon | ✓ Tamamlandı | ✓ PASS | `71ca7bf` |
| T05 | Middleware pipeline | ✓ Tamamlandı | ✓ PASS | `cb20ae0` |
| T06 | Authentication altyapısı | ⬚ Bekliyor | — | — |
| T07 | Rate limiting konfigürasyonu | ⬚ Bekliyor | — | — |
| T08 | Logging altyapısı | ⬚ Bekliyor | — | — |
| T09 | Hangfire setup ve background job altyapısı | ⬚ Bekliyor | — | — |
| T10 | Outbox pattern altyapısı | ⬚ Bekliyor | — | — |
| T11 | CI/CD pipeline | ⬚ Bekliyor | — | — |
| T12 | Test altyapısı | ⬚ Bekliyor | — | — |
| T13 | Next.js Frontend iskeleti | ⬚ Bekliyor | — | — |
| T14 | Steam Sidecar Node.js iskeleti | ⬚ Bekliyor | — | — |
| T15 | Blockchain Sidecar Node.js iskeleti | ⬚ Bekliyor | — | — |
| T16 | Monitoring altyapısı | ⬚ Bekliyor | — | — |

**F0 Gate Check:** ⬚ Bekliyor

---

## F1 — Veri Katmanı (T17–T28)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T17 | Enum tanımları (C# + EF Core migration) | ⬚ Bekliyor | — | — |
| T18 | User, UserLoginLog, RefreshToken entity'leri | ⬚ Bekliyor | — | — |
| T19 | Transaction, TransactionHistory entity'leri | ⬚ Bekliyor | — | — |
| T20 | PaymentAddress, BlockchainTransaction entity'leri | ⬚ Bekliyor | — | — |
| T21 | TradeOffer, PlatformSteamBot entity'leri | ⬚ Bekliyor | — | — |
| T22 | Dispute, FraudFlag entity'leri | ⬚ Bekliyor | — | — |
| T23 | Notification, NotificationDelivery, UserNotificationPreference entity'leri | ⬚ Bekliyor | — | — |
| T24 | Admin entity'leri (AdminRole, AdminRolePermission, AdminUserRole) | ⬚ Bekliyor | — | — |
| T25 | Altyapı entity'leri (SystemSetting, OutboxMessage, ProcessedEvent, vb.) | ⬚ Bekliyor | — | — |
| T26 | Seed data | ⬚ Bekliyor | — | — |
| T27 | Performans index'leri ve filtered index'ler | ⬚ Bekliyor | — | — |
| T28 | Initial migration ve migration testi | ⬚ Bekliyor | — | — |

**F1 Gate Check:** ⬚ Bekliyor

---

## F2 — Çekirdek Servisler (T29–T43)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T29 | Steam OpenID authentication (login + callback + token üretimi) | ⬚ Bekliyor | — | — |
| T30 | ToS kabul, yaş gate, geo-block | ⬚ Bekliyor | — | — |
| T31 | Steam re-verify ve authenticator kontrolü | ⬚ Bekliyor | — | — |
| T32 | Refresh token yönetimi | ⬚ Bekliyor | — | — |
| T33 | User profil servisi | ⬚ Bekliyor | — | — |
| T34 | Cüzdan adresi yönetimi | ⬚ Bekliyor | — | — |
| T35 | Hesap ayarları (dil, bildirim tercihleri, Telegram/Discord bağlama) | ⬚ Bekliyor | — | — |
| T36 | Hesap deaktif ve silme | ⬚ Bekliyor | — | — |
| T37 | Bildirim altyapı servisi | ⬚ Bekliyor | — | — |
| T38 | Platform içi bildirim kanalı | ⬚ Bekliyor | — | — |
| T39 | Admin rol ve yetki yönetimi | ⬚ Bekliyor | — | — |
| T40 | Admin RBAC (policy-based authorization) | ⬚ Bekliyor | — | — |
| T41 | Admin parametre yönetimi | ⬚ Bekliyor | — | — |
| T42 | AuditLog servisi | ⬚ Bekliyor | — | — |
| T43 | User itibar skoru hesaplama | ⬚ Bekliyor | — | — |

**F2 Gate Check:** ⬚ Bekliyor

---

## F3 — İş Mantığı (T44–T63b)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T44 | Transaction State Machine | ⬚ Bekliyor | — | — |
| T45 | İşlem oluşturma akışı | ⬚ Bekliyor | — | — |
| T46 | Alıcı kabul akışı | ⬚ Bekliyor | — | — |
| T47 | Timeout scheduling | ⬚ Bekliyor | — | — |
| T48 | Timeout warning | ⬚ Bekliyor | — | — |
| T49 | Timeout execution | ⬚ Bekliyor | — | — |
| T50 | Timeout freeze/resume | ⬚ Bekliyor | — | — |
| T51 | İptal akışı | ⬚ Bekliyor | — | — |
| T52 | Komisyon ve finansal hesaplamalar | ⬚ Bekliyor | — | — |
| T53 | Gas fee yönetimi | ⬚ Bekliyor | — | — |
| T54 | Fraud flag sistemi | ⬚ Bekliyor | — | — |
| T55 | AML kontrolü (fiyat sapması, yüksek hacim) | ⬚ Bekliyor | — | — |
| T56 | Çoklu hesap tespiti | ⬚ Bekliyor | — | — |
| T57 | Wash trading kontrolü | ⬚ Bekliyor | — | — |
| T58 | Dispute sistemi | ⬚ Bekliyor | — | — |
| T59 | Emergency hold | ⬚ Bekliyor | — | — |
| T60 | Satıcı payout issue | ⬚ Bekliyor | — | — |
| T61 | SignalR hub — işlem real-time güncellemeler | ⬚ Bekliyor | — | — |
| T62 | SignalR hub — bildirim push | ⬚ Bekliyor | — | — |
| T63 | Admin dashboard ve işlem yönetimi API | ⬚ Bekliyor | — | — |
| T63a | Platform public endpoint'leri (backend) | ⬚ Bekliyor | — | — |
| T63b | Retention job'ları (toplu temizlik) | ⬚ Bekliyor | — | — |

**F3 Gate Check:** ⬚ Bekliyor

---

## F4 — Entegrasyonlar (T64–T83)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T64 | Steam Sidecar — bot session yönetimi | ⬚ Bekliyor | — | — |
| T65 | Steam Sidecar — trade offer gönderme | ⬚ Bekliyor | — | — |
| T66 | Steam Sidecar — trade offer durum izleme | ⬚ Bekliyor | — | — |
| T67 | Steam Sidecar — envanter okuma | ⬚ Bekliyor | — | — |
| T68 | Steam Sidecar — webhook callback ve backend entegrasyonu | ⬚ Bekliyor | — | — |
| T69 | Steam Sidecar — bot failover ve capacity-based seçim | ⬚ Bekliyor | — | — |
| T70 | Blockchain Sidecar — HD wallet adres üretimi | ⬚ Bekliyor | — | — |
| T71 | Blockchain Sidecar — ödeme izleme | ⬚ Bekliyor | — | — |
| T72 | Blockchain Sidecar — tutar doğrulama ve edge case'ler | ⬚ Bekliyor | — | — |
| T73 | Blockchain Sidecar — TRC-20 transfer (payout, refund, sweep) | ⬚ Bekliyor | — | — |
| T74 | Blockchain Sidecar — energy delegation | ⬚ Bekliyor | — | — |
| T75 | Blockchain Sidecar — gecikmeli ödeme izleme | ⬚ Bekliyor | — | — |
| T76 | Blockchain Sidecar — reconciliation job | ⬚ Bekliyor | — | — |
| T77 | Blockchain Sidecar — hot wallet yönetimi | ⬚ Bekliyor | — | — |
| T78 | Email entegrasyonu (Resend) | ⬚ Bekliyor | — | — |
| T79 | Telegram entegrasyonu | ⬚ Bekliyor | — | — |
| T80 | Discord entegrasyonu | ⬚ Bekliyor | — | — |
| T81 | Steam Market fiyat API | ⬚ Bekliyor | — | — |
| T82 | Sanctions screening servisi | ⬚ Bekliyor | — | — |
| T83 | Geo-block servisi | ⬚ Bekliyor | — | — |

**F4 Gate Check:** ⬚ Bekliyor

---

## F5 — Kullanıcı Arayüzü (T84–T106)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T84 | Ortak UI bileşenleri (C01–C17) | ⬚ Bekliyor | — | — |
| T85 | Global layout (header, navigation, footer) | ⬚ Bekliyor | — | — |
| T86 | Landing page (S01) | ⬚ Bekliyor | — | — |
| T87 | Auth akışı ekranları | ⬚ Bekliyor | — | — |
| T88 | Dashboard (S05) | ⬚ Bekliyor | — | — |
| T89 | İşlem oluşturma (S06) | ⬚ Bekliyor | — | — |
| T90 | İşlem detay sayfası (S07) — tüm state varyantları | ⬚ Bekliyor | — | — |
| T91 | Ödeme bilgileri ve edge case UI | ⬚ Bekliyor | — | — |
| T92 | Dispute UI | ⬚ Bekliyor | — | — |
| T93 | Profil sayfaları (S08, S09) | ⬚ Bekliyor | — | — |
| T94 | Hesap ayarları (S10) | ⬚ Bekliyor | — | — |
| T95 | Bildirimler sayfası (S11) | ⬚ Bekliyor | — | — |
| T96 | SignalR client entegrasyonu | ⬚ Bekliyor | — | — |
| T97 | i18n (4 dil desteği) | ⬚ Bekliyor | — | — |
| T98 | Responsive tasarım | ⬚ Bekliyor | — | — |
| T99 | Admin Dashboard (S12) | ⬚ Bekliyor | — | — |
| T100 | Admin Flag kuyruğu + detay (S13, S14) | ⬚ Bekliyor | — | — |
| T101 | Admin İşlem listesi + detay (S15, S16) | ⬚ Bekliyor | — | — |
| T102 | Admin Parametre yönetimi (S17) | ⬚ Bekliyor | — | — |
| T103 | Admin Steam hesapları (S18) | ⬚ Bekliyor | — | — |
| T104 | Admin Rol & yetki yönetimi (S19) | ⬚ Bekliyor | — | — |
| T105 | Admin Kullanıcı detay (S20) | ⬚ Bekliyor | — | — |
| T106 | Admin Audit log (S21) | ⬚ Bekliyor | — | — |

**F5 Gate Check:** ⬚ Bekliyor

---

## F6 — Uçtan Uca Doğrulama (T107–T114)

| Task | Ad | Durum | Doğrulama | Commit |
|---|---|---|---|---|
| T107 | E2E — Happy path (tam escrow akışı) | ⬚ Bekliyor | — | — |
| T108 | E2E — İptal senaryoları | ⬚ Bekliyor | — | — |
| T109 | E2E — Timeout senaryoları | ⬚ Bekliyor | — | — |
| T110 | E2E — Ödeme edge case'ler | ⬚ Bekliyor | — | — |
| T111 | E2E — Fraud/flag senaryoları | ⬚ Bekliyor | — | — |
| T112 | E2E — Emergency hold | ⬚ Bekliyor | — | — |
| T113 | E2E — Admin akışları | ⬚ Bekliyor | — | — |
| T114 | E2E — Downtime ve bakım senaryoları | ⬚ Bekliyor | — | — |

**F6 Gate Check:** ⬚ Bekliyor
