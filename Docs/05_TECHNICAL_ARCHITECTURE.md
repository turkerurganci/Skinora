# Skinora — Technical Architecture

**Versiyon: v2.3** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-03-21

---

## 1. Genel Bakış

Bu doküman, Skinora platformunun teknik mimarisini, teknoloji seçimlerini ve altyapı kararlarını tanımlar. Tüm kararlar ürün gereksinimlerinden türetilmiştir ve hem MVP hem de uzun vadeli büyüme göz önünde bulundurularak alınmıştır.

### 1.1 Mimari Yaklaşım

**Modüler Monolith** — tüm iş mantığı tek bir deploy birimi içinde, ancak modüller arası sınırlar net olarak tanımlı.

| Özellik | Açıklama |
|---|---|
| Deploy birimi | Tek .NET uygulaması |
| Modül sınırları | Her iş alanı (Transaction, Payment, Steam, User, Notification) kendi modülü |
| Modüller arası iletişim | In-process — interface ve domain event'ler üzerinden |
| Veritabanı | Tek SQL Server instance, tek schema — modül bazlı schema ayrımı MVP'de uygulanmaz, büyüdüğünde değerlendirilir |

**Neden monolith?**
- MVP için düşük operasyonel yük
- Tek deploy, tek debug, tek monitoring noktası
- Modüler yapı sayesinde ileride microservice'e ayrıştırma mümkün

**Neden modüler?**
- İş alanları birbirine bağımlı olmadan gelişebilir
- Test izolasyonu sağlanır
- Sınırlar net olduğu için microservice geçişi doğal bir refactor olur

---

## 2. Teknoloji Stack'i

### 2.1 Özet

| Katman | Teknoloji | Gerekçe |
|---|---|---|
| Backend | .NET 9 (C#) + ASP.NET Core | Proje sahibinin deneyimi, review edebilirlik, güçlü ekosistem |
| Frontend | Next.js (React + TypeScript) | AI kod üretim kalitesi, SSR desteği, modern ekosistem |
| Veritabanı | SQL Server 2022 | .NET ile doğal uyum, ACID compliance, proje sahibinin deneyimi |
| Cache / Session | Redis 7 | Session, cache, rate limiting — sadece çökse tolere edilebilir roller |
| ORM | Entity Framework Core | .NET standardı, migration desteği, LINQ |
| Background Jobs | Hangfire (SQL Server storage) | Persistent job scheduling, .NET native, Redis bağımsız, dashboard |
| Real-time | SignalR | WebSocket abstraction, .NET native, Next.js client desteği |

### 2.2 Backend — .NET 9 (C#)

Ana iş mantığı, API katmanı, state machine, yetkilendirme ve tüm orchestration burada yaşar.

**Temel kütüphaneler:**

| Kütüphane | Amaç |
|---|---|
| ASP.NET Core Web API | REST API |
| Entity Framework Core | ORM + migrations |
| Stateless | Transaction state machine |
| Hangfire | Background job scheduling (timeout'lar, delayed job'lar) |
| SignalR | Real-time bildirimler (WebSocket) |
| FluentValidation | Input validation |
| Serilog | Structured logging |
| MediatR | In-process domain event dispatching (modüller arası) |

**API versioning:**

| Konu | Karar |
|---|---|
| Strateji | URL prefix — `/api/v1/` |
| Breaking change politikası | Yeni versiyon açılır, eski versiyon belirli süre desteklenir |
| Deprecation süreci | Eski versiyon sunset tarihi önceden duyurulur |

### 2.3 Frontend — Next.js

| Özellik | Karar |
|---|---|
| Framework | Next.js 14+ (App Router) |
| Dil | TypeScript |
| State management | Implementasyon aşamasında belirlenir — adaylar: React Context (basit state), Zustand (karmaşık state) |
| API iletişimi | REST (fetch veya axios) |
| Real-time | SignalR client (@microsoft/signalr) |
| Styling | Implementasyon aşamasında belirlenir — Tailwind CSS öncelikli aday |
| i18n | next-intl veya next-i18next — 4 dil desteği (EN, ZH, ES, TR) |

### 2.4 Veritabanı — SQL Server 2022

| Özellik | Karar |
|---|---|
| Hosting | Docker container (başlangıç), managed'a geçiş yolu açık |
| Backup | Otomatik, günlük, offsite |
| Migration | EF Core migrations — CI/CD pipeline içinde otomatik |

**DB büyüme stratejisi:**

| Konu | Karar |
|---|---|
| Partitioning | `CreatedAt` bazlı — 10M+ satırdan sonra devreye girer |
| Arşivleme | 6 ay sonra eski kayıtlar arşiv tablolarına taşınır. Arşivleme operasyonu: her transaction archive set'i tek DB transaction içinde taşınır (copy + delete). Taşıma sırası: child'lar önce, Transaction en son. Arşiv tabloları FK constraint'siz, read-only. Idempotent retry: `INSERT ... WHERE NOT EXISTS` (06 §8.8) |
| Filtered index | Aktif işlemler için (sadece `Status NOT IN (COMPLETED, CANCELLED_*)`) |
| Connection pooling | EF Core default pool + max ayarı |
| Read replica | Gerektiğinde — MVP'de tek instance yeterli |

### 2.5 Cache / Session — Redis 7

Redis sadece **çökse de sistem durmaması gereken** rolleri üstlenir:

| Rol | Kullanım | Redis çökerse |
|---|---|---|
| Session store | Refresh token cache (source of truth: DB) | Redis çökerse refresh token DB'den okunur, performans düşer ama oturum korunur |
| Cache | Sık sorgulanan veriler (kullanıcı profili, envanter cache) | Sorgular DB'ye düşer, yavaşlar ama çalışır |
| Rate limiting | Endpoint bazlı istek sınırlandırma | Geçici olarak limitler kalkar, kısa kesinti tolere edilir |

**Kritik roller Redis'te değil, SQL Server'da:**

| Rol | Nerede | Gerekçe |
|---|---|---|
| Background jobs (Hangfire) | SQL Server | Job'lar transaction ile yazılır, uygulama restart'ında kaybolmaz |
| Event dispatching (Outbox) | SQL Server | Outbox tablosu + Hangfire polling — broker katmanına gerek yok |

**Resilience stratejisi:**

| Aşama | Strateji |
|---|---|
| MVP / Development | Standalone Redis — tek instance |
| Production | Redis Sentinel (minimum 3 node) — otomatik failover |
| Büyüdüğünde | Redis Cluster — horizontal scaling gerekirse |

**SignalR scaling notu:**

| Aşama | Karar |
|---|---|
| MVP (tek instance) | Redis backplane gerekmez — tüm WebSocket bağlantıları aynı process'te |
| Horizontal scale | SignalR Redis backplane eklenir — instance'lar arası mesaj senkronizasyonu |

---

## 3. Servis Mimarisi

Skinora dört ayrı uygulama runtime'ından oluşur. Bunlar Docker Compose içinde ayrı container'lar olarak çalışır:

```
┌─────────────────────────────────────────────────────────┐
│                    Nginx (Reverse Proxy)                 │
│                    Port 80/443 (SSL)                     │
└─────────┬──────────────────────┬────────────────────────┘
          │                      │
          ▼                      ▼
┌─────────────────┐    ┌─────────────────┐
│  .NET Backend   │    │ Next.js Frontend │
│  Port 5000      │    │ Port 3000        │
│                 │    │                  │
│ - REST API      │    │ - SSR/CSR pages  │
│ - State Machine │    │ - SignalR client │
│ - Auth          │    │ - i18n           │
│ - Hangfire      │    └─────────────────┘
│ - SignalR       │
└────┬───────┬────┘
     │       │
     │       │  HTTP/Webhook
     ▼       ▼
┌─────────┐ ┌──────────────┐
│ Steam   │ │  Blockchain  │
│ Sidecar │ │  Service     │
│ Node.js │ │  Node.js     │
│ Port    │ │  Port 5200   │
│ 5100    │ │              │
│         │ │ - TronWeb    │
│ - Trade │ │ - HD Wallet  │
│   Offer │ │ - Monitoring │
│ - Bot   │ │ - Transfer   │
│   Mgmt  │ │              │
└─────────┘ └──────────────┘
     │              │
     ▼              ▼
┌─────────┐  ┌───────────┐
│ Steam   │  │ Tron      │
│ API     │  │ Blockchain│
└─────────┘  └───────────┘

     Paylaşılan Altyapı:
┌─────────────┐  ┌─────────────┐
│ SQL Server  │  │   Redis     │
│ Port 1433   │  │   Port 6379 │
└─────────────┘  └─────────────┘
```

### 3.1 .NET Backend (Ana Uygulama)

Tüm iş mantığının merkezi. API katmanı, state machine, yetkilendirme, job scheduling ve event orchestration burada.

**Modüller:**

| Modül | Sorumluluk |
|---|---|
| Transaction | İşlem yaşam döngüsü, state machine, iş kuralları |
| Payment | Blockchain servisi ile iletişim, ödeme doğrulama, iade |
| Steam | Steam sidecar ile iletişim, trade offer orchestration |
| User | Profil, cüzdan adresi, itibar skoru |
| Auth | Steam OpenID, JWT, refresh token, policy-based authz, ToS kabul kaydı (03 §2.1) |
| Notification | Event → bildirim dönüşümü, kanal dispatching |
| Admin | Admin paneli API, rol/yetki yönetimi, flag yönetimi |
| Dispute | İtiraz yönetimi, otomatik doğrulama, admin eskalasyonu |
| Fraud | Piyasa fiyat kontrolü, anomali tespiti, flag'leme |

### 3.2 Node.js Steam Sidecar

Steam ile tüm etkileşimi yönetir. .NET backend ile HTTP üzerinden haberleşir.

| Bileşen | Kütüphane | Görev |
|---|---|---|
| Trade offer yönetimi | `steam-tradeoffer-manager` | Offer gönder, kabul et, takip et |
| Steam session | `steamcommunity` | Login, cookie yönetimi, session yenileme |
| 2FA onayı | `steam-totp` | Mobile authenticator onay kodları |
| Bot yönetimi | Custom | Birden fazla Steam hesabı, bot seçimi, sağlık kontrolü, failover |

**Bot yönetim stratejisi:**

| Konu | Karar |
|---|---|
| Bot seçimi | Capacity-based — en az aktif emanet item'a sahip bot seçilir |
| Health check | Her bot için periyodik session kontrolü (60 saniye). Yanıt vermeyen bot havuzdan çıkarılır |
| Session expire | Otomatik re-login — `steamcommunity` session yenileme. Başarısızsa admin alert |
| Mid-trade failure | Bot çökerse restart'ta pending trade offer'lar kontrol edilir ve takibe devam edilir |
| Trade offer gönderim retry | Gönderim başarısız olursa (Steam API hatası, item durumu değişmişse) exponential backoff ile yeniden denenir. Timeout süresi içinde çözülmezse işlem iptal olur (03 §2.3) |
| Tüm botlar down | Yeni trade offer gönderilmez (state machine guard), admin'e critical alert |

**İletişim modeli:**

| Yön | Protokol | Örnek |
|---|---|---|
| .NET → Sidecar | HTTP REST | "Bu item için satıcıya trade offer gönder" |
| Sidecar → .NET | Webhook callback | "Trade offer kabul edildi, item platformda" |

> **Asset lineage:** Trade receipt'ten alınan asset ID'ler: seller→bot trade sonrası `EscrowBotAssetId`, bot→buyer trade sonrası `DeliveredBuyerAssetId` Transaction'da kaydedilir. Aynı classId'den çok item olabilir — doğru item seçimi için zorunlu (06 §8.4).

### 3.3 Node.js Blockchain Servisi

Tron blockchain (TRC-20) ile tüm etkileşimi yönetir.

| Bileşen | Kütüphane/Yaklaşım | Görev |
|---|---|---|
| Adres üretimi | TronWeb + HD Wallet | Her işlem için benzersiz ödeme adresi |
| Blockchain monitoring | TronWeb + TronGrid API | Gelen transferleri izle, doğrula |
| Ödeme gönderimi | TronWeb | Satıcıya ödeme, alıcıya iade |
| Key management | HD Wallet (BIP-44) | Tek master key'den sınırsız alt adres türetimi |

> **Finansal hesaplama:** `MidpointRounding.ToZero` (truncation), scale 6, payment validation tolerance yok — gelen tutar ExpectedAmount ile tam eşleşmeli (06 §8.3, 09 §14.3). HD Wallet: `HdWalletIndex` UNIQUE — monoton artan allocator, arşivleme sonrası da reuse edilmez (06 §3.7).

**İletişim modeli:**

| Yön | Protokol | Örnek |
|---|---|---|
| .NET → Blockchain | HTTP REST | "Bu işlem için ödeme adresi üret" |
| Blockchain → .NET | Webhook callback | "Ödeme doğrulandı: 100 USDT alındı" |

**Ödeme doğrulama parametreleri:**

| Parametre | Değer | Gerekçe |
|---|---|---|
| Minimum onay sayısı | **20 blok** (~60 saniye) | Tron'da 19 SR (Super Representative) onayı sonrası blok finalize olur. 20 blok güvenli eşik |
| Ödeme durumu | `pending` → `confirmed` | 20 blok onayına kadar "pending", sonrasında "confirmed" ve state geçişi tetiklenir |
| Polling aralığı | 3 saniye | TronGrid API rate limit'lerine uygun, kullanıcı deneyimi için yeterince hızlı |

**Gecikmeli ödeme izleme (iptal sonrası):**

İşlem timeout veya iptal ile sonlandığında, ödeme adresi kademeli olarak izlenmeye devam eder:

| Süre | Polling aralığı | Gerekçe |
|---|---|---|
| İlk 24 saat | 30 saniye | Büyük çoğunluk bu sürede gelir, hızlı iade önemli |
| 1-7 gün | 5 dakika | Olasılık düşük ama hala makul |
| 7-30 gün | 1 saat | Çok nadir, ama kullanıcı parasını kaybetmemeli |
| 30 gün sonra | İzleme durdurulur | Admin alert — olağandışı durumlar manuel ele alınır |

| Konu | Karar |
|---|---|
| İade hedefi | Alıcının işlem kabul ederken belirlediği iade adresine otomatik iade (02 §12.2) |
| İade gas fee | İade tutarından düşülür (02 bölüm 4.6 ile tutarlı) |
| Minimum iade eşiği | Gas fee iadenin büyük kısmını yutuyorsa (iade < 2× gas fee) → iade yapılmaz, admin alert |
| Yanlış token | İzleme deposit adresine gelen tüm TRC-20 transferlerini takip eder. Sadece beklenen token ödeme onayını tetikler. Yanlış token (desteklenen TRC-20) gelirse → otomatik iade denemesi, başarısızsa admin alert. Desteklenmeyen token/kontrat → otomatik iade garanti edilemez, admin review (02 §4.4) |
| Admin konfigürasyonu | İzleme süreleri ve polling aralıkları admin tarafından değiştirilebilir |
| 30 gün sonra gelen ödeme | Kullanıcı destek ile iletişime geçer → admin manuel iade başlatır |

**Ödeme tutar doğrulama:**

| Senaryo | Davranış |
|---|---|
| Eksik tutar | Platform kabul etmez, gelen tutar alıcıya iade edilir (gas fee düşülerek). Timeout süresi devam eder — alıcı süre dolmadan doğru tutarı gönderebilir (02 §4.4) |
| Fazla tutar | Platform doğru tutarı kabul eder, fazla kısmı alıcıya iade eder (gas fee düşülerek). İşlem normal akışla devam eder (02 §4.4) |
| Yanlış token (desteklenen TRC-20) | Platform kabul etmez, alıcının iade adresine otomatik iade edilir (02 §4.4) |
| Desteklenmeyen token/kontrat | Platform bu varlığı işleyemez — otomatik iade garanti edilemez, manuel incelemeye (admin review) düşer (02 §4.4) |

**Satıcıya ödeme gönderimi:**

| Konu | Karar |
|---|---|
| Zamanlama | Item teslimi doğrulandıktan sonra (ITEM_DELIVERED → COMPLETED geçişi) |
| Gas fee koruma eşiği | Satıcıya gönderim gas fee'si komisyonun admin tarafından belirlenen eşiğini (%10 varsayılan) aşarsa, aşan kısım satıcının alacağından düşülür (02 §4.7) |
| Retry stratejisi | Gönderim başarısız olursa exponential backoff ile otomatik yeniden denenir (3 deneme: 1dk, 5dk, 15dk). Tüm denemeler başarısızsa admin'e critical alert gider, işlem COMPLETED'a geçmez — ödeme başarılı olana kadar bekler |

**Güvenlik katmanları:**

| Katman | Uygulama |
|---|---|
| Master key saklama | Encrypted vault veya HSM (production), environment variable (development) |
| Signing izolasyonu | Private key'ler application memory'de kalıcı tutulmaz — yalnızca signing anında Key Vault'tan okunur, imzalama tamamlandıktan sonra bellekten temizlenir |
| Hot wallet limiti | Operasyonel miktarda fon tutulur, fazlası cold wallet'a. Limit admin tarafından belirlenir. Limit aşıldığında admin'e alert gider — cold wallet transferi admin tarafından manuel başlatılır (MVP). Otomasyon ileride eklenebilir |
| Transfer limitleri | Miktar limitleri ve anomali kontrolü tüm giden transferlerde |

**Custody ve fon akışı:**

| Konu | Karar |
|---|---|
| Deposit adresleri | Her işlem için HD Wallet'tan benzersiz adres türetilir — bu adresler platformun kontrolündedir |
| Sweep mekanizması | Ödeme onaylandıktan sonra deposit adresindeki fon hot wallet'a sweep edilir (tek merkezi adres) |
| Sweep tetikleyicisi | `PaymentReceivedEvent` consumer'ı — ödeme doğrulandığında otomatik sweep job'ı başlar |
| Payout / iade kaynağı | Satıcıya ödeme ve alıcıya iade hot wallet'tan çıkar |
| Hot wallet limiti | Admin tarafından belirlenen limit aşıldığında admin'e alert — fazla fon cold wallet'a manuel transfer edilir (MVP) |
| Reconciliation | Günlük otomatik reconciliation job'ı — on-chain bakiye (hot wallet + aktif deposit adresleri) ile platform ledger (DB) karşılaştırılır. Uyuşmazlık tespit edilirse admin'e critical alert |
| Reconciliation kapsamı | Her deposit adresi: beklenen tutar vs gerçek bakiye. Hot wallet: toplam beklenen bakiye vs gerçek bakiye. Cold wallet: platform ledger'daki cold wallet kayıtları vs on-chain bakiye |
| Cold wallet ledger kaydı | Hot→cold transfer yapıldığında platform ledger'ında kayıt oluşturulur (tutar, tarih, tx hash). Reconciliation bu kaydı kullanarak hot wallet bakiye düşüşünü cold wallet artışıyla eşleştirir — false mismatch önlenir |
| Sweep hatası | Sweep başarısız olursa retry (exponential backoff, 3 deneme). Tüm denemeler başarısızsa admin'e alert — payout veya refund deposit adresinden doğrudan gönderilir (fallback) |
| Sweep öncesi refund | Sweep tamamlanmadan refund gerekirse (ör: TRADE_OFFER_SENT_TO_BUYER timeout) → refund deposit adresinden doğrudan alıcıya gönderilir. Hot wallet bakiyesine bağımlılık yoktur |

**TRON resource modeli (deposit adresleri):**

TRON'da TRC-20 transfer işlemleri energy ve bandwidth tüketir. Deposit adreslerinden sweep veya doğrudan refund/payout yapabilmek için resource stratejisi gereklidir:

| Konu | Karar |
|---|---|
| Sorun | Deposit adreslerinde yalnızca TRC-20 token bulunur — TRX veya energy yoksa giden transfer başarısız olur |
| Sweep modeli | Merkezi sweeper account yaklaşımı — sweep işleminde deposit adresinin private key'i ile imzalama yapılır, energy/bandwidth ise sweeper account'tan karşılanır (energy delegation veya TRX staking) |
| TRX prefunding | Deposit adreslerine TRX gönderilmez — merkezi delegation modeli tercih edilir (gas maliyeti düşer, adres başına TRX takibi gerekmez) |
| Energy delegation | Sweeper account, yeterli TRX stake ederek energy üretir. Sweep öncesinde deposit adresine geçici energy delegation yapılır, sweep sonrasında delegation geri alınır |
| Fallback | Energy delegation başarısız veya yetersizse → deposit adresine minimum TRX transfer edilerek gas fee karşılanır |
| Doğrudan refund/payout | Deposit adresinden doğrudan gönderim gerektiğinde aynı delegation modeli uygulanır |
| Maliyet optimizasyonu | Batch delegation — aynı blokta birden fazla deposit adresi için toplu delegation yapılabilir |

### 3.4 Servisler Arası İletişim Güvenliği

Tüm internal servisler Docker internal network üzerinde haberleşir — dış dünyadan doğrudan erişilemez. Buna ek olarak, her iletişim yönü için kimlik doğrulama ve bütünlük kontrolü uygulanır:

**Outbound (.NET → Sidecar / Blockchain):**

| Katman | Uygulama |
|---|---|
| Authentication | Shared API key — her istekte `X-Internal-Key` header'ı |
| Network | Docker internal network — port'lar dışarıya açılmaz |
| Key rotation | API key environment variable olarak tanımlı, deploy sırasında değiştirilebilir |

**Inbound (Sidecar / Blockchain → .NET webhook callback):**

| Katman | Uygulama |
|---|---|
| Payload signing | HMAC-SHA256 — her webhook payload'u shared secret ile imzalanır |
| Doğrulama | .NET backend gelen webhook'un `X-Signature` header'ını doğrular, imza eşleşmezse reddeder |
| Replay koruması | Timestamp + nonce — belirli süre penceresinin dışındaki veya tekrarlanan istekler reddedilir |
| Network | Docker internal network — dışarıdan webhook endpoint'lerine erişilemez |

**Threat model ve sınırlar:** Tüm servisler aynı Docker Compose stack'inde, aynı internal network'te çalışıyor. Shared key + HMAC imzalama, network seviyesinde spoofing ve payload bütünlük kontrolü için yeterli koruma sağlar. Ancak container compromise durumunda saldırgan shared secret'ları okuyarak hem outbound hem inbound istekleri sahteleyebilir — bu risk MVP'de kabul edilir. mTLS gibi daha ağır çözümler MVP'de gereksiz karmaşıklık ekler; gerekirse ileride servis bazlı kimlik (workload identity) veya mTLS ile güçlendirilebilir.

### 3.5 Secrets Management

Tüm hassas bilgiler (credentials, key'ler, connection string'ler) ortam bazında farklı stratejilerle yönetilir:

**Ortam bazlı saklama:**

| Ortam | Strateji |
|---|---|
| Development | `.env` dosyası — `.gitignore`'da, repo'ya dahil edilmez |
| Production | Docker Secrets veya Azure Key Vault — environment variable olarak container'a inject |

**Secret envanteri ve rotation politikası:**

| Secret türü | Örnek | Rotation politikası |
|---|---|---|
| Steam bot credentials | username, password, shared_secret, identity_secret | Manuel — değiştirildiğinde redeploy |
| Tron wallet private key | Hot wallet signing key | Yüksek güvenlik — Key Vault'ta, memory'de sadece imzalama anında |
| Internal API key | Sidecar ↔ Backend `X-Internal-Key` | Deploy sırasında otomatik rotation imkanı |
| HMAC shared secret | Webhook imzalama secret'ı | Deploy sırasında otomatik rotation imkanı |
| DB connection string | SQL Server credentials | Key Vault'ta, rotation periyodik |
| JWT signing key | Access/Refresh token imzalama | Key Vault'ta, rotation sırasında eski key geçici olarak valid kalır (grace period) |

**Güvenlik kuralları:**

| Kural | Uygulama |
|---|---|
| Log maskeleme | Secret'lar asla log'a yazılmaz — middleware seviyesinde maskeleme |
| API response | Secret'lar asla API response'a dahil edilmez |
| Tron private key | RAM'de minimum süre tutulur, signing bittikten sonra bellekten temizlenir |
| Git koruması | `.gitignore` + pre-commit hook ile secret dosyalarının repo'ya push edilmesi engellenir |

**Platform parametreleri bootstrap (06 §8.9):**

İlk kurulumda admin paneli henüz erişilemez olduğundan, yapılandırılmamış zorunlu SystemSetting parametreleri environment variable ile hydrate edilir. Startup sırası: (1) migration + seed, (2) `SKINORA_SETTING_{KEY_UPPER}` formatındaki env var override'lar uygulanır, (3) fail-fast kontrolü çalışır. Env var yalnızca `IsConfigured = false` parametreleri set eder — zaten yapılandırılmış parametreleri override etmez. Lansman sonrası parametreler admin panelinden yönetilir.

---

## 4. İşlem State Machine

### 4.1 Durumlar

| Durum | Açıklama |
|---|---|
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
| `CANCELLED_BUYER` | Alıcı tarafından iptal (ödeme öncesi) |
| `CANCELLED_ADMIN` | Admin tarafından iptal (flag reddi) |
| `FLAGGED` | Fraud tespiti nedeniyle durduruldu |

### 4.2 Durum Geçişleri

```
İşlem oluşturma isteği
        │
   Flag kontrol
        │
   ┌────┴─────────────────┐
   ▼                      ▼
FLAGGED                 CREATED ── (flag yoksa, normal akış)
   │
   ├── admin_approve ──→ CREATED
   └── admin_reject  ──→ CANCELLED_ADMIN

Emergency Hold: State değiştirmez — herhangi bir aktif state'te uygulanabilir (bkz. §4.5)

CREATED ──→ ACCEPTED ──→ TRADE_OFFER_SENT_TO_SELLER ──→ ITEM_ESCROWED ──→ PAYMENT_RECEIVED
   │            │                    │                        │
   │ timeout    │ timeout            │ timeout                │ timeout
   │ seller_cancel seller_cancel     │ seller_decline         │ seller_cancel
   │ buyer_cancel  buyer_cancel      │ buyer_cancel           │ buyer_cancel
   ↓            ↓                    ↓                        ↓
CANCELLED_*  CANCELLED_*        CANCELLED_*              CANCELLED_*
                                (item iade gerek-        (item iade)
                                 mez, trade offer
                                 iptal edilir)

PAYMENT_RECEIVED ──→ TRADE_OFFER_SENT_TO_BUYER ──→ ITEM_DELIVERED ──→ COMPLETED
                              │ timeout
                              │ buyer_decline
                              ↓
                        CANCELLED_*
                        (item iade + para iade)
```

**İptal ve red geçişleri detayı:**

| Kaynak Durum | Trigger | Hedef Durum | Iade |
|---|---|---|---|
| CREATED | timeout | CANCELLED_TIMEOUT | Yok (varlık transferi olmamış) |
| CREATED | seller_cancel | CANCELLED_SELLER | Yok |
| CREATED | buyer_cancel | CANCELLED_BUYER | Yok |
| ACCEPTED | timeout | CANCELLED_TIMEOUT | Yok |
| ACCEPTED | seller_cancel | CANCELLED_SELLER | Yok |
| ACCEPTED | buyer_cancel | CANCELLED_BUYER | Yok |
| TRADE_OFFER_SENT_TO_SELLER | timeout | CANCELLED_TIMEOUT | Trade offer iptal edilir |
| TRADE_OFFER_SENT_TO_SELLER | seller_decline | CANCELLED_SELLER | Trade offer reddedildi, iade gerekmez (03 §2.3/5) |
| TRADE_OFFER_SENT_TO_SELLER | buyer_cancel | CANCELLED_BUYER | Trade offer iptal edilir |
| ITEM_ESCROWED | timeout | CANCELLED_TIMEOUT | Item satıcıya iade |
| ITEM_ESCROWED | seller_cancel | CANCELLED_SELLER | Item satıcıya iade |
| ITEM_ESCROWED | buyer_cancel | CANCELLED_BUYER | Item satıcıya iade |
| PAYMENT_RECEIVED | timeout | — | Timeout yok (ödeme doğrulandı, teslim aşaması) |
| TRADE_OFFER_SENT_TO_BUYER | timeout | CANCELLED_TIMEOUT | Item satıcıya iade + para alıcıya iade |
| TRADE_OFFER_SENT_TO_BUYER | buyer_decline | CANCELLED_BUYER | Item satıcıya iade + para alıcıya iade (03 §3.5/5) |
| FLAGGED | admin_approve | CREATED | Flag kaldırılır, işlem normal akışa girer (03 §7.1) |
| FLAGGED | admin_reject | CANCELLED_ADMIN | İşlem iptal edilir, taraflara bildirim gider. FLAGGED yalnızca creation-time'da tetiklendiği için varlık transferi henüz olmamıştır (03 §7.1) |
| CREATED | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — sebep zorunlu (03 §8.7) |
| ACCEPTED | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — sebep zorunlu |
| TRADE_OFFER_SENT_TO_SELLER | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — aktif trade offer iptal edilir |
| ITEM_ESCROWED | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — item satıcıya iade |
| PAYMENT_RECEIVED | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — item satıcıya, ödeme alıcıya iade |
| TRADE_OFFER_SENT_TO_BUYER | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — aktif trade offer iptal, item satıcıya, ödeme alıcıya iade |
| ITEM_DELIVERED | admin_cancel | — | **Kullanılamaz.** Item alıcıya teslim edilmiş, standart iptal/iade uygulanamaz. Bu aşamadan sonra yalnızca exceptional resolution (admin manuel inceleme ve müdahale) başlatılabilir (02 §7) |
| FLAGGED | admin_cancel | CANCELLED_ADMIN | Admin doğrudan iptal — flag reject ile aynı etki ama farklı tetikleyici |
| _(Emergency hold için bkz. §4.5 — state değiştirmez, flag mekanizmasıdır)_ | | | |

> **Not:** Ödeme yapıldıktan sonra (PAYMENT_RECEIVED ve sonrası) kullanıcılar tek taraflı iptal edemez (02 §7). Bu durumlarda sadece timeout, alıcının trade offer reddi veya **admin doğrudan iptali** geçerlidir.
>
> **Not:** FLAGGED durumu yalnızca işlem oluşturma anında tetiklenir — fiyat sapması ve yüksek hacim flag'leri (03 §7). Admin onaylarsa işlem CREATED'a geçer, reddederse CANCELLED_ADMIN olur. Anormal davranış tespiti ise hesap düzeyinde çalışır — işlem FLAGGED state'ine geçmez, kullanıcının tüm fon akışı aksiyonları engellenir (03 §7.3).
>
> **Not:** Admin doğrudan iptal (admin_cancel) CREATED'dan TRADE_OFFER_SENT_TO_BUYER'a kadar olan aktif state'lerden (+ FLAGGED) tetiklenebilir. ITEM_DELIVERED sonrası admin cancel kullanılamaz — bu aşamada yalnızca exceptional resolution geçerlidir (02 §7). Flag reddi (admin_reject) ise sadece FLAGGED state'ten. İkisi de CANCELLED_ADMIN sonucunu üretir (02 §7, 03 §8.7).
>
> **Not:** Emergency hold mekanizması için bkz. §4.5. Hold bir state değişikliği değil, mevcut state üzerine uygulanan dondurma mekanizmasıdır (06 Data Model: `TimeoutFreezeReason` enum'u ile uyumlu).

### 4.3 Teknoloji

| Bileşen | Karar | Gerekçe |
|---|---|---|
| State machine | **Stateless** (.NET kütüphanesi) | Lightweight, production-proven, guard ve side effect desteği |
| Geçiş kuralları | Deklaratif — durumlar, trigger'lar, guard'lar tek yerde | Invalid geçiş otomatik engellenir |
| Side effect'ler | OnEntry/OnExit handler'ları | Duruma girerken bildirim gönder, timeout başlat vb. |

### 4.4 Timeout Yönetimi

| Yaklaşım | Karar |
|---|---|
| Scheduler | **Hangfire** — delayed job scheduling |
| Mekanizma | Her timeout gerektiren duruma girildiğinde bir delayed job schedule edilir. Süre dolduğunda job çalışır ve state machine üzerinden `TimeoutExpired` trigger'ını ateşler. |
| Persistence | Hangfire job'ları **SQL Server'da** saklanır — Redis çökse bile timeout'lar çalışmaya devam eder |
| Uyarı eşiği | Admin tarafından oran olarak ayarlanır (02 §3.4). Süre dolmadan belirtilen oran gerildiğinde Hangfire delayed job `TimeoutWarningEvent` üretir |
| Downtime | Bakım/kesinti durumunda timeout süreleri dondurulur (ürün kararı). Mekanizma için bkz. "Platform-geneli downtime politikası" |
| Blockchain degradasyonu | Blockchain doğrulama altyapısı sağlıksız olduğunda (node/indexer erişim kaybı) ödeme adımındaki aktif işlemlerin timeout süreleri dondurulur. Health check başarısız → otomatik freeze; admin manuel tetikleme de mümkün. Altyapı normale dönünce gecikmeli ödeme tespiti otomatik yapılır (02 §3.3) |
| Emergency hold | Admin emergency hold uygulanan işlemlerde timeout durur. Hold kaldırılınca timeout kaldığı yerden devam eder (bkz. §4.5, 02 §7) |

> **Aşama ayrımı:** Ödeme aşaması (ITEM_ESCROWED) per-transaction Hangfire delayed job ile yönetilir. Diğer aşamaların deadline'ları (AcceptDeadline, TradeOfferToSellerDeadline, TradeOfferToBuyerDeadline) periyodik scanner/poller job ile enforce edilir (06 §3.5).

**Timeout job yaşam döngüsü:**

| Konu | Karar |
|---|---|
| Stale job koruması | Job çalıştığında state machine guard'ı mevcut state'i ve transaction version'ını doğrular. State zaten değişmişse (kullanıcı timeout'tan önce aksiyon aldıysa) trigger reddedilir — işlem etkilenmez |
| Eski job iptali | State değişiminde önceki state'in timeout job'ı Hangfire üzerinden iptal edilir (`BackgroundJob.Delete`). Guard koruması ek güvence sağlar (belt-and-suspenders) |
| Freeze mekanizması | Timeout dondurulduğunda: aktif job iptal edilir, kalan süre transaction'da kaydedilir (`TimeoutRemainingSeconds`). Freeze kaldırıldığında kalan süre ile yeni job schedule edilir |
| Freeze/resume kaydı | Transaction'da `TimeoutFreezeReason`, `TimeoutFrozenAt`, `TimeoutRemainingSeconds` alanları (06 Data Model ile uyumlu) |

> **Otorite:** Reschedule'ın kaynağı `TimeoutRemainingSeconds`'tır. Deadline field'ları bu değerden türetilir (06 §8.1).

**Platform-geneli downtime politikası:**

Tekil transaction freeze'den farklı olarak, planlı bakım veya beklenmedik tam kesinti durumunda platform genelinde tüm timeout'lar etkilenir:

| Senaryo | Mekanizma |
|---|---|
| Planlı bakım | Admin maintenance mode flag'i aktif eder → Hangfire `BackgroundJobServer` graceful shutdown yapılır (`Dispose`), yeni timeout job'ları schedule edilmez, mevcut job'lar tetiklenmez. Kullanıcılara önceden bildirim gönderilir (02 §3.3) |
| Beklenmedik kesinti | Backend çöker → Hangfire job'ları SQL Server'da kalır. Backend restart'ta: başlatma sırasında son bilinen uptime ile şimdiki zaman arasındaki fark (outage window) hesaplanır |
| Restart sonrası recovery | 1) Outage window boyunca overdue olmuş timeout job'ları hemen ateşlenmez. 2) Tüm aktif işlemlerin timeout süreleri outage window kadar uzatılır (reschedule). 3) Uzatma işlemi tamamlandıktan sonra Hangfire processing resume edilir |
| Uptime tracking | Periyodik heartbeat job'ı (ör: 30 saniyede bir) `LastHeartbeat` timestamp'ını DB'ye yazar. Restart'ta son heartbeat ile şimdiki zaman farkı = outage window |
| Audit | Maintenance mode giriş/çıkış ve otomatik timeout uzatma işlemleri AuditLog'a kaydedilir |

### 4.5 Emergency Hold Mekanizması

Emergency hold bir state değişikliği değil, herhangi bir aktif state üzerine uygulanan dondurma mekanizmasıdır. Transaction mevcut state'inde kalır.

> **06 Data Model ile uyum:** `EMERGENCY_HOLD`, `TransactionStatus` enum'unda bir state olarak değil, `TimeoutFreezeReason` enum'unda bir değer olarak tanımlıdır.

| Konu | Karar |
|---|---|
| Uygulama | Transaction'da `IsOnHold` flag'i + `TimeoutFreezeReason = EMERGENCY_HOLD` |
| Tetikleyici | Admin tarafından manuel (sanctions eşleşmesi, hesap ele geçirme şüphesi vb.) veya sanctions eşleşmesinde otomatik (02 §14.0) |
| Etki | Timeout durur, kullanıcı aksiyonları engellenir (state machine guard), state geçişi bekletilir |
| Yetki | Ayrı `EMERGENCY_HOLD` yetkisi gerektirir (02 §7) |
| Kaldırma | Admin hold'u kaldırır → `IsOnHold = false`, timeout kaldığı yerden devam eder (§4.4 freeze mekanizması) |
| Hold + iptal | Admin hold'daki işlemi iptal edebilir → CANCELLED_ADMIN. ITEM_DELIVERED sonrası standart iptal yapılamaz, exceptional resolution başlatılır (02 §7, 03 §8.8) |
| Toplu hold | Sanctions eşleşmesinde kullanıcının tüm aktif işlemlerine otomatik hold uygulanır (03 §11a.3) |
| Audit | Hold uygulama, kaldırma ve iptal aksiyonları AuditLog'a kaydedilir, sebep zorunlu |

> **Karşılıklı constraint:** `IsOnHold = 1` ise `TimeoutFrozenAt NOT NULL` ve `TimeoutFreezeReason = EMERGENCY_HOLD` zorunlu; `TimeoutFreezeReason = EMERGENCY_HOLD` ise `IsOnHold = 1` zorunlu (06 §3.5).

---

## 5. Event Sistemi

### 5.1 Outbox Pattern

State geçişlerinde event kaybı kabul edilemez. Outbox Pattern bu garantiyi sağlar:

```
1. State geçişi + OutboxMessages tablosuna event yazma → AYNI DB TRANSACTION
2. Outbox Dispatcher (Hangfire recurring job) → tabloyu poll eder
3. İşlenmemiş event'leri okur → ilgili consumer'ları in-process çağırır
4. Başarılı → event'i "processed" işaretler
```

**Garanti:** DB transaction commit olduysa event kesinlikle işlenecek. Uygulama çökse bile Hangfire restart'ta kaldığı yerden devam eder (job'lar SQL Server'da).

> **Dispatcher davranışı:** Dispatcher PENDING ve FAILED kayıtları birlikte çeker (`WHERE Status IN (PENDING, FAILED)`). FAILED kayıtlar PENDING'e geri dönmez — doğrudan işlenir. Maksimum retry aşılınca admin alert (06 §3.18).

**Consumer Idempotency:** Dispatcher retry yaptığında aynı event birden fazla kez işlenebilir. Kritik consumer'larda (ödeme gönderimi, trade offer) bu kabul edilemez.

| Bileşen | Uygulama |
|---|---|
| Idempotency key | Her Outbox event'inin benzersiz `EventId`'si (GUID) |
| Tracking | `ProcessedEvents` tablosu — consumer event'i işledikten sonra `EventId`'yi kaydeder |
| Kontrol | Consumer event'i işlemeden önce `ProcessedEvents`'te var mı kontrol eder, varsa skip eder |
| Garanti | Event işleme + `ProcessedEvents` kaydı aynı DB transaction'da — ya ikisi birden olur ya hiçbiri |

**Dış sistem idempotency (Sidecar / Blockchain):**

`ProcessedEvents` mekanizması in-process consumer dedup'u sağlar. Ancak dış servislere (Steam Sidecar, Blockchain) yapılan HTTP çağrılarında ek koruma gereklidir:

| Sorun | Consumer event'i işler → sidecar'a HTTP call yapar → sidecar side effect'i gerçekleştirir (trade offer gönderir) → consumer `ProcessedEvents` commit'ten önce çöker → event replay → aynı side effect tekrar tetiklenir |
|---|---|
| **Çözüm** | **Uygulama** |
| Command-level idempotency key | Her HTTP isteğinde `X-Idempotency-Key` header'ı gönderilir (`EventId` veya `TransactionId + action türü`) |
| Receiver-side dedup | Sidecar ve blockchain servisleri gelen key'i paylaşılan SQL Server'da (idempotency tablosu) kaydeder. Aynı key ile gelen istek → önceki sonucu döndürür, side effect tekrarlanmaz. Container recreate/redeploy durumunda dedup geçmişi korunur |
| Receiver-side akış | 1) Key gelir → store'da kontrol. 2) Yoksa → key `in_progress` olarak kaydedilir. 3) Side effect çalıştırılır. 4) Sonuç key kaydına yazılır. Replay'de: key `in_progress` bulunursa → dış sistem durumu kontrol edilir (ör: trade offer var mı?), tamamlanmışsa sonuç döndürülür, yoksa retry edilir |
| Kapsam | Tüm kritik dış çağrılar: trade offer gönderimi, ödeme gönderimi, iade, sweep |

> **ExternalIdempotencyRecord detay:** DB schema: `UNIQUE(ServiceName, IdempotencyKey)` — servis bazlı scope. Concurrency: atomik insert-or-read + conditional update. Stale recovery: `LeaseExpiresAt` field — lease dolduktan sonra in_progress → failed reclaim. Exactly-once sınırı: receiver-side idempotency tek başına yeterli değil, downstream provider'da da idempotent işlem zorunlu (06 §3.21).

> **Garanti:** In-process dedup (ProcessedEvents) + dış servis dedup (X-Idempotency-Key + kalıcı store) birlikte çalışarak HTTP sınırı boyunca effectively-once (deduplicated command processing) semantik sağlar. Dağıtık sistemlerde teorik exactly-once garanti edilemez; bu model pratik olarak duplicate side effect'leri önler.

### 5.2 Event Dispatching

| Karar | Gerekçe |
|---|---|
| **Outbox + Hangfire polling** (MVP) | Broker katmanı yok — monolith'te tüm consumer'lar aynı process'te, in-process çağrı yeterli. Redis bağımlılığı ortadan kalkar, kritik event'ler SQL Server'da güvende |
| Büyüdüğünde | RabbitMQ'ya geçiş — event contract'lar aynı kalır, sadece transport değişir (dispatcher broker'a publish eder, consumer'lar broker'dan okur) |

**Neden broker yok (MVP)?** Skinora modüler monolith — tüm event consumer'ları aynı .NET process'in modülleridir. Steam ve Blockchain consumer'ları event'i in-process alır, ardından ilgili dış servise (Node.js sidecar / blockchain servisi) HTTP çağrısı yapar. Consumer'ın kendisi .NET'te çalışır; dış servisler yalnızca komut alıcısıdır. Event'i bir broker'a koyup aynı process'in başka thread'inin okuması yerine doğrudan çağırmak daha basit ve güvenilir.

### 5.3 Domain Event'ler

Her state geçişi bir domain event publish eder:

| Event | Consumer | Aksiyon |
|---|---|---|
| `TransactionCreatedEvent` | Notification | Alıcıya davet bildirimi |
| `TransactionAcceptedEvent` | Steam, Notification | Satıcıya trade offer gönder, bildirim |
| `ItemEscrowedEvent` | Blockchain, Notification | Ödeme adresi üret, alıcıya bildirim |
| `PaymentReceivedEvent` | Steam, Blockchain, Notification | Alıcıya trade offer gönder, sweep job başlat (§3.3 custody), bildirim |
| `ItemDeliveredEvent` | Blockchain, Notification | Satıcıya ödeme gönder, bildirim |
| `TransactionCompletedEvent` | Notification | Her iki tarafa bildirim |
| `TransactionCancelledEvent` | Steam, Blockchain, Notification | İade işlemleri, bildirimler |
| `TransactionFlaggedEvent` | Notification | Admin'e alert |
| `TimeoutWarningEvent` | Notification | Yaklaşan timeout bildirimi |

> **Not:** Notification consumer bu event'leri aldığında, olayın türüne ve mevcut state'e göre uygun bildirimleri üretir. Örneğin `TransactionCancelledEvent` alındığında iptal sebebi, iade durumu ve ilgili taraflara (satıcı/alıcı) göre farklı bildirim mesajları oluşturulur. Aynı event birden fazla bildirim tetikleyebilir (örn: item iade bildirimi + ödeme iade bildirimi). Bildirim tetikleyicilerinin tam listesi için bkz. 03 §12.

### 5.4 Audit Trail

| Bileşen | Karar |
|---|---|
| Yaklaşım | **Hybrid** — state + event log |
| Transaction tablosu | Güncel durum (hızlı sorgulama) |
| TransactionHistory tablosu | Her durum geçişinin tam kaydı |
| AuditLog tablosu | Fon hareketleri, admin aksiyonları, güvenlik olayları — kalıcı, immutable |
| Kayıt içeriği | Önceki durum, yeni durum, trigger, timestamp, tetikleyen aktör, ek veri |

Full event sourcing'in karmaşıklığı olmadan tam tarihçe sağlanır. TransactionHistory işlem state geçişlerini, AuditLog ise fon/admin/güvenlik olaylarını kapsar. Dispute'larda, yasal taleplerde ve denetimlerde "tam olarak ne oldu?" sorusuna kesin cevap verilir.

---

## 6. Kimlik Doğrulama & Güvenlik

### 6.1 Authentication

| Bileşen | Karar |
|---|---|
| Giriş yöntemi | Steam OpenID |
| Token stratejisi | **JWT (access token)** + **Refresh Token** |
| Access token ömrü | Kısa (ör: 15 dakika) |
| Refresh token | **DB source of truth** — Redis cache olarak kullanılır. Ban/logout anında her ikisinden de iptal edilir. Redis çökerse DB'den okunur |
| Session yönetimi | Refresh token üzerinden — çalınan access token kısa ömürlü |

**İstemci tarafı token modeli:**

| Token | Saklama | Gerekçe |
|---|---|---|
| Access token (JWT) | JavaScript memory (değişken/state) — localStorage veya cookie'de saklanmaz | XSS ile çalınma riski minimize edilir. Sayfa yenilendiğinde refresh token ile yenilenir |
| Refresh token | **HttpOnly + Secure + SameSite=Strict cookie** | JavaScript erişemez (XSS koruması), CSRF koruması SameSite ile sağlanır |

> **CSRF modeli:** Refresh token HttpOnly cookie'de olduğu için refresh endpoint'ine CSRF koruması (SameSite + anti-forgery token) uygulanır. Access token header'da (Authorization: Bearer) gönderildiği için API endpoint'lerinde cookie tabanlı CSRF riski yoktur.

**Akış:**
1. Steam OpenID ile kimlik doğrulama
2. Platform JWT (access token) + refresh token üretir
3. Access token her istekte header'da gönderilir
4. Süresi dolunca refresh token ile yenilenir
5. Ban veya logout → refresh token iptal → access token süresi dolunca erişim kesilir

### 6.2 Authorization

| Bileşen | Karar |
|---|---|
| Yaklaşım | **Policy-based authorization** (.NET built-in) |
| Kullanıcı tarafı | Authenticated vs Anonymous, Mobile Authenticator kontrolü, resource-based erişim (kendi işlemi) |
| Admin tarafı | Süper admin + dinamik rol grupları, policy bazlı yetki atama |

### 6.3 Güvenlik Katmanları

| Katman | Uygulama |
|---|---|
| Rate limiting | Redis-based, endpoint bazlı ayrı limitler |
| CORS | Sadece kendi domain'inden |
| CSRF koruması | SameSite cookie + anti-forgery token |
| Input validation | FluentValidation — her endpoint'te |
| SQL injection | EF Core parametrized queries |
| XSS | Next.js auto-escape + CSP header'ları |
| HTTPS | Zorunlu, HTTP → HTTPS redirect |
| Sensitive data | Private key'ler, API secret'lar → environment variable veya secret manager |
| Audit logging | Login, işlem oluşturma, cüzdan değişikliği, admin aksiyonları loglanır — kritik olaylar Loki'ye ek olarak AuditLog tablosuna kalıcı yazılır (06 §3.20) |
| Abuse throttling | OpenID başlatma, callback ve token refresh endpoint'lerine progressive delay + geçici kilitleme (Steam OpenID kullanıldığı için klasik login brute-force geçerli değil) |

### 6.4 Cüzdan Adresi Güvenliği

| Kural | Uygulama |
|---|---|
| Adres değişikliği | Steam re-authentication zorunlu |
| Aktif işlem varken | Profildeki adres değişse bile aktif işlemler eski adresle tamamlanır |
| Onay adımı | Adres girişinde kullanıcıya görsel onay gösterilir |

### 6.5 Hesap Silme ve Veri Anonimleştirme

| Konu | Karar |
|---|---|
| Silme yöntemi | Soft delete — hesap deaktif edilir, kişisel veriler anonimleştirilir |
| Aktif işlem guard'ı | Aktif işlem varken hesap silinemez (02 bölüm 5.24 ile tutarlı) |
| Anonimleştirme | Steam ID, cüzdan adresi, profil bilgileri temizlenir — işlem geçmişi anonim olarak saklanır (audit trail) |
| Bildirim kanalı temizliği | UserNotificationPreference.ExternalId temizlenir + NotificationDelivery.TargetExternalId masked formata dönüştürülür (06 §6.2) — delivery kaydı korunur, kişisel hedef anonimleşir |
| KVKK/GDPR uyumu | Kullanıcı veri silme talebi → kişisel veriler anonimleştirilir, audit log korunur |
| Audit kayıtları | TransactionHistory ve AuditLog kayıtları asla silinmez — immutable audit trail (06 §3.20) |

---

## 7. Bildirim Altyapısı

### 7.1 Mimari

```
State geçişi → Outbox (DB) → Hangfire Dispatcher → Notification Consumer → Kanal Dispatching
                                                                                │
                                                              ┌─────────────────┼─────────────────┐
                                                              ▼                 ▼                 ▼
                                                        Platform içi        Email          Telegram/Discord
                                                        (DB + SignalR)    (Resend/SG)       (Bot API)
```

**Tek Notification Consumer + kanal dispatching:** Event alınır → kullanıcı tercihleri kontrol edilir → şablon çözümlenir (dil + içerik) → ilgili kanallara dağıtılır.

### 7.2 Kanallar

| Kanal | Teknoloji | Detay |
|---|---|---|
| Platform içi | DB (`Notifications` tablosu) + SignalR | Real-time push, okundu/okunmadı durumu |
| Email | Resend veya SendGrid | Transactional email, HTML şablonlar |
| Telegram/Discord | Bot API | Kullanıcı profilinde chat ID / user ID bağlar |

### 7.3 Lokalizasyon

| Bileşen | Karar |
|---|---|
| Desteklenen diller | İngilizce, Çince, İspanyolca, Türkçe |
| Şablon sistemi | .NET resource dosyaları (.resx) + placeholder'lar (`{ItemName}`, `{Amount}`) |
| Kanal adaptasyonu | Tek şablon → kanal bazlı format (Email: HTML, Telegram: Markdown, Platform: kısa metin) |
| Fallback | Kullanıcının dili tanımlı değilse → İngilizce |

### 7.4 Kullanıcı Tercihleri

- Platform içi bildirim her zaman aktif (kapatılamaz)
- Email ve Telegram/Discord ayrı ayrı açılıp kapatılabilir
- Bildirim tipi bazlı granüler kontrol MVP'de yok, ileride eklenebilir

### 7.5 Retry Stratejisi

| Kanal | Retry | Backoff |
|---|---|---|
| Email | 3 deneme | Exponential (1dk, 5dk, 15dk) |
| Telegram/Discord | 3 deneme | Exponential (1dk, 5dk, 15dk) |
| Platform içi | Outbox garantisi | Event tekrar işlenir |
| Tüm kanallar başarısız | Log + admin alert | Kritik bildirimler için |

---

## 8. Deployment & Altyapı

### 8.1 Container Yapısı — Docker Compose

| Container | Base Image | Port | Açıklama |
|---|---|---|---|
| `skinora-backend` | .NET 9 runtime | 5000 | Ana uygulama |
| `skinora-frontend` | Node.js | 3000 | Next.js SSR |
| `skinora-steam-sidecar` | Node.js | 5100 | Steam bot yönetimi |
| `skinora-blockchain` | Node.js | 5200 | Tron blockchain servisi |
| `skinora-db` | SQL Server 2022 | 1433 | Veritabanı |
| `skinora-redis` | Redis 7 | 6379 | Cache, session, rate limiting |
| `skinora-reverse-proxy` | Nginx | 80/443 | SSL termination, routing |
| `skinora-loki` | Grafana Loki | 3100 | Log toplama |
| `skinora-prometheus` | Prometheus | 9090 | Metrik toplama |
| `skinora-grafana` | Grafana | 3001 | Dashboard, alerting |
| `skinora-uptime-kuma` | Uptime Kuma | 3002 | Uptime monitoring |

### 8.2 Ortamlar

| Ortam | Amaç | Blockchain | Steam |
|---|---|---|---|
| Local (Development) | Geliştirme | Tron Testnet (Nile/Shasta) | Steam test hesapları + düşük değerli test envanteri |
| Staging | Test ve QA | Tron Testnet | Steam test hesapları + düşük değerli test envanteri |
| Production | Canlı | Tron Mainnet | Gerçek Steam hesapları |

### 8.3 Hosting

| Aşama | Hosting | Gerekçe |
|---|---|---|
| MVP / Başlangıç | VPS (Hetzner, DigitalOcean, Contabo) | Düşük maliyet, tam kontrol, Docker Compose ile deploy |
| Büyüme | VPS + managed DB | Veritabanı güvenilirliği artırılır |
| Ölçek | Kubernetes (gerekirse) | Container image'lar aynı kalır, orchestration değişir |

### 8.4 CI/CD Pipeline

| Adım | Araç |
|---|---|
| Kod repo | GitHub |
| CI | GitHub Actions — lint, test, build |
| Container build | Docker (GitHub Actions içinde) |
| Container registry | GitHub Container Registry (ghcr.io) |
| CD | GitHub Actions + SSH → `docker compose pull && docker compose up -d` |
| Migration | EF Core migrations — pipeline içinde otomatik |

**Branch stratejisi:**
- `main` — production-ready kod
- `develop` — aktif geliştirme
- Feature branch'ler → `develop`'a merge → test → `main`'e merge → production deploy

### 8.5 SSL/TLS

| Bileşen | Karar |
|---|---|
| Sertifika | Let's Encrypt (ücretsiz, otomatik yenileme) |
| Reverse proxy | Nginx — SSL termination |

### 8.6 Veri Güvenliği

| Bileşen | Karar |
|---|---|
| DB backup | Otomatik, günlük, offsite (farklı lokasyona) |
| Disaster recovery | Production öncesi tanımlanacak — RTO/RPO hedefleri |
| Encryption at rest | SQL Server TDE (Transparent Data Encryption) |

---

## 9. Monitoring & Hata Yönetimi

### 9.1 Loglama

| Bileşen | Karar |
|---|---|
| Araç | **Loki + Grafana** (self-hosted) |
| .NET logging | Serilog → Loki sink |
| Node.js logging | Pino → Loki push |
| Format | Structured JSON |
| Correlation ID | Her işlemle ilgili tüm loglar (backend, sidecar, blockchain) aynı `correlationId` ile etiketlenir |
| DB audit trail | Fon hareketleri, admin aksiyonları, güvenlik olayları → AuditLog tablosuna kalıcı yazılır (Loki retention'ından bağımsız) |

### 9.2 Metrikler

| Bileşen | Karar |
|---|---|
| Araç | **Prometheus + Grafana** (self-hosted) |
| .NET metrikleri | Prometheus .NET client — metrics endpoint expose |
| Node.js metrikleri | prom-client — metrics endpoint expose |

**İzlenen metrik kategorileri:**

| Kategori | Örnekler |
|---|---|
| Sistem | CPU, RAM, disk, network (her container) |
| Uygulama | Request/response süreleri, hata oranı, aktif bağlantı sayısı |
| İş metrikleri | Aktif işlem sayısı, tamamlanma oranı, ortalama işlem süresi, günlük hacim |
| Entegrasyon | Steam API yanıt süresi/hata oranı, Tron node yanıt süresi, email delivery oranı |
| Güvenlik | OpenID/callback/refresh başarısızlıkları, abuse throttling tetik sayısı, rate limit hit sayısı, flag'lenen işlem sayısı |

### 9.3 Alerting

| Bileşen | Karar |
|---|---|
| Araç | **Grafana Alerting** |
| Kanal | Telegram bot (admin'e) |

| Seviye | Koşul | Aksiyon |
|---|---|---|
| Critical | Servis down, DB bağlantısı koptu, blockchain/Steam erişilemiyor | Anında Telegram + Email |
| Warning | Hata oranı yükseldi, yanıt süresi arttı, disk %80+, job queue birikiyor | Telegram |
| Info | Günlük özet, deployment tamamlandı | Sadece log |

### 9.4 Hata Takibi

| Bileşen | Karar |
|---|---|
| Yaklaşım | Structured logging + Loki + Grafana dashboard |
| Hata dashboard | `level=error` filtresine dayalı — servis bazlı hata sayısı, trend, en sık hatalar |
| Alert | Hata oranı eşiği aşıldığında Grafana Alerting tetiklenir |
| Büyüdüğünde | GlitchTip (self-hosted, Sentry SDK uyumlu) eklenebilir |

### 9.5 Uptime Monitoring

| Bileşen | Karar |
|---|---|
| Dış monitoring | **Uptime Kuma** (self-hosted) — HTTP/TCP check, bildirim entegrasyonları |
| İç monitoring | `/health` endpoint — DB, Redis, Steam API, Tron node bağlantılarını kontrol eder |

### 9.6 Maliyet

Tüm monitoring stack'i self-hosted ve ücretsizdir:
- Loki + Grafana + Prometheus: Açık kaynak
- Uptime Kuma: Açık kaynak
- Grafana Alerting: Grafana içinde dahili
- Sentry/GlitchTip: Gerekirse ileride eklenir (açık kaynak)

---

## 10. Testing Stratejisi

| Katman | Kapsam | Araç |
|---|---|---|
| Unit test | Domain logic, state machine kuralları, validasyonlar | xUnit + Moq |
| Integration test | DB + API — gerçek SQL Server'a karşı | xUnit + TestContainers |
| E2E test | Kritik akışlar (işlem oluşturma → tamamlanma) | Playwright veya benzeri |
| Contract test | Sidecar ↔ Backend HTTP sözleşmesi | Pact veya custom schema validation |

**Öncelikli test alanları:**

| Alan | Gerekçe |
|---|---|
| State machine geçişleri | Yanlış geçiş = veri bütünlüğü kaybı. Tüm 13 durum, geçerli/geçersiz trigger kombinasyonları ve emergency hold mekanizması (§4.5) test edilmeli |
| Ödeme doğrulama ve iade hesaplama | Yanlış hesaplama = fon kaybı. Eksik/fazla/yanlış token, gas fee koruma eşiği, iade tutarı senaryoları |
| Sidecar ↔ Backend iletişimi | Webhook imza doğrulama, idempotency (in-process + X-Idempotency-Key), replay koruması |
| Timeout yönetimi | Delayed job scheduling, stale job guard koruması, timeout freeze/resume, uyarı eşiği |
| Custody ve fon akışı | Sweep mekanizması, reconciliation, hot wallet limit kontrolü |

**CI'da test çalıştırma:** Her PR'da unit + integration testleri çalışır. E2E testleri staging deploy sonrası çalışır. Detaylar 09_CODING_GUIDELINES.md'de tanımlanacak.

---

## 11. Teknoloji Kararları Özet Tablosu

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| 1 | Genel mimari | Modüler Monolith | MVP basitliği, düşük operasyonel yük, microservice geçiş yolu açık |
| 2 | Backend | .NET 9 (C#) + ASP.NET Core | Proje sahibinin deneyimi, review edebilirlik |
| 3 | Veritabanı | SQL Server 2022 | .NET uyumu, ACID, proje sahibinin deneyimi |
| 4 | Cache/Session | Redis 7 | Session, cache, rate limiting — sadece çökse tolere edilebilir roller |
| 5 | Frontend | Next.js (React + TypeScript) | AI kod üretim kalitesi, SSR, modern ekosistem |
| 6 | Steam entegrasyonu | Node.js sidecar (steam-tradeoffer-manager) | En olgun kütüphane ekosistemi, fiili standart |
| 7 | Blockchain entegrasyonu | Kendi altyapı — TronWeb (Node.js), HD Wallet | Tam kontrol, komisyon tasarrufu, edge case yönetimi |
| 8 | State machine | Stateless + Outbox Pattern + Hangfire (SQL Server) | Event kaybı sıfır, persistent timeout'lar, Redis bağımsız, audit trail |
| 9 | Auth & güvenlik | Steam OpenID + JWT/Refresh + Policy-based authz | Anlık iptal, dinamik yetkiler, endüstri standardı |
| 10 | Bildirimler | Tek consumer + kanal dispatching, SignalR | Merkezi tercih yönetimi, real-time push |
| 11 | Deployment | Docker Compose, VPS, GitHub Actions CI/CD, Nginx | Düşük maliyet, tekrarlanabilir, taşınabilir |
| 12 | Monitoring | Loki + Prometheus + Grafana, Uptime Kuma | Tamamen self-hosted, ücretsiz, merkezi dashboard |

---

*Skinora — Technical Architecture v2.3*
