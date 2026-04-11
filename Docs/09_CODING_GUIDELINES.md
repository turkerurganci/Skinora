# Skinora — Coding Guidelines

**Versiyon: v0.9** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-03-19

> **Amaç:** Bu doküman, projede kod üretirken ve mevcut kodu değiştirirken uyulması gereken teknik geliştirme kurallarını tanımlar.
>
> **Kapsam:** Backend, frontend, sidecar, integration ve test katmanlarında yazılan tüm kodlar.
>
> **Not:** Bu doküman mimariyi yeniden tanımlamaz. Mimari kararların kaynağı `05_TECHNICAL_ARCHITECTURE.md` ve ilgili teknik dokümanlardır. Bu dokümanın amacı, o kararların kod seviyesinde nasıl uygulanacağını standartlaştırmaktır.

---

## 1. Temel İlkeler

- Kod, önce **doğru**, sonra **okunabilir**, sonra **genişletilebilir** olmalıdır.
- Her değişiklik mevcut mimari sınırlar içinde kalmalıdır.
- Gereksiz soyutlama, gereksiz katman ve erken optimizasyon yapılmamalıdır.
- Kod, bir sonraki geliştiricinin (veya agent'ın) hızlı anlayabileceği açıklıkta yazılmalıdır.
- Geçici çözümler kalıcı çözüm gibi sunulmamalıdır.
- Sessizce kapsam genişletilmemelidir.
- Varsayım yapılan yerler açıkça belirtilmelidir.
- Bir şeyin çalışması yeterli değildir; doğru sınırlarda, doğru sorumluluklarla ve güvenle geliştirilebilir şekilde çalışması gerekir.

---

## 2. Source of Truth

Kod yazarken doküman öncelik sırası:

| Öncelik | Doküman | Kapsam |
|---------|---------|--------|
| 1 | `02_PRODUCT_REQUIREMENTS.md` | İş kuralları ve ürün kararları |
| 2 | `10_MVP_SCOPE.md` | Kapsam sınırları — ne var, ne yok |
| 3 | `05_TECHNICAL_ARCHITECTURE.md` | Mimari kararlar, teknoloji seçimleri |
| 4 | `06_DATA_MODEL.md` | Entity'ler, ilişkiler, enum'lar |
| 5 | `07_API_DESIGN.md` | Endpoint'ler, request/response, konvansiyonlar |
| 6 | `08_INTEGRATION_SPEC.md` | Üçüncü parti servis entegrasyonları |
| 7 | `09_CODING_GUIDELINES.md` | Bu doküman — kod yazım kuralları |

### Kurallar

- Kod, üst seviye dokümanlarla çelişmemelidir. Üst doküman alt dokümanı ezer.
- Çelişki fark edilirse sessizce kod yazılmaz; önce çelişki not edilir ve çözüm beklenir.
- Kod içinde iş kuralı uydurulmaz. Her iş kuralının kaynağı 02 veya 10'da olmalıdır.
- Dokümanda olmayan ama zorunlu teknik detay gerekiyorsa (ör: bir edge case'in teknik çözümü), dar kapsamlı ve gerekçeli çözüm uygulanır — bu durum açıkça belgelenir.
- Enum değerleri, status isimleri, hata kodları gibi sabitler 06 ve 07 ile birebir tutarlı olmalıdır. Kaynak doküman açıkken yazılmalıdır.

---

## 3. Genel Kod Yazım Kuralları

### 3.1 Değişiklik Disiplini

- Küçük, kontrollü ve geri alınabilir değişiklikler tercih edilir.
- Bir commit/patch tek bir amacı çözmelidir.
- İlgisiz refactor aynı değişikliğe eklenmemelidir.

### 3.2 Kod Kalitesi

- Kopyala-yapıştır yerine ortaklaştırma düşünülmelidir; ancak erken abstraction yapılmamalıdır. Üç benzer satır, erken bir soyutlamadan iyidir.
- Magic string ve magic number kullanılmamalıdır. Sabitler merkezi yönetilmelidir.
- Kod açıklığı, kısa görünmekten daha önemlidir.
- Yorum yalnızca "neden" sorusuna cevap veriyorsa yazılmalıdır. "Ne" sorusuna cevap veren yorum, kodun yeterince açık olmadığının işaretidir.

### 3.3 Sınır Koruma

- Mevcut mimariyi bozan kısa yollar eklenmemelidir.
- Bir katmanın veya modülün sorumluluğu dışında iş yapılmamalıdır.
- "Sonra düzeltiriz" diye bilinen riskli kod bırakılmamalıdır.
- Dokümanlarda tanımlı olmayan bir özellik veya davranış sessizce eklenmemelidir.

---

## 4. Solution Yapısı & Klasör Organizasyonu

Bu doküman mimariyi yeniden tanımlamaz — mimari kararlar 05'te tanımlıdır. Bu bölüm, o kararların dosya sistemi seviyesindeki uygulanmasını standartlaştırır.

### 4.1 Genel Yapı

Skinora üç ayrı runtime'dan oluşur (05 §3). Her biri kendi proje kökünde yaşar:

```
skinora/
├── backend/                         ← .NET 9 modüler monolith
│   ├── Skinora.sln
│   ├── src/
│   └── tests/
├── frontend/                        ← Next.js (App Router)
│   ├── package.json
│   └── src/
├── sidecar-steam/                   ← Node.js Steam sidecar
│   ├── package.json
│   └── src/
├── sidecar-blockchain/              ← Node.js Blockchain servisi
│   ├── package.json
│   └── src/
├── docker-compose.yml
├── docker-compose.override.yml      ← Development ortamı
├── .env.example
└── Docs/                            ← Proje dokümanları
```

### 4.2 Backend — .NET Solution

#### 4.2.1 Proje Ağacı

```
backend/
├── Skinora.sln
├── src/
│   ├── Skinora.API/                          ← Host, controller, middleware, DI
│   │   ├── Controllers/
│   │   │   ├── Auth/
│   │   │   ├── Users/
│   │   │   ├── Transactions/
│   │   │   ├── Notifications/
│   │   │   ├── Steam/
│   │   │   ├── Admin/
│   │   │   └── Platform/
│   │   ├── Middleware/
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   ├── CorrelationIdMiddleware.cs
│   │   │   └── WebhookSignatureMiddleware.cs
│   │   ├── Filters/
│   │   │   └── ApiResponseWrapperFilter.cs
│   │   ├── Configuration/                    ← Modül DI registration
│   │   │   ├── TransactionsModule.cs
│   │   │   ├── PaymentsModule.cs
│   │   │   └── ...
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── Modules/
│   │   ├── Skinora.Transactions/             ← İşlem yaşam döngüsü
│   │   │   ├── Domain/
│   │   │   │   ├── Entities/
│   │   │   │   │   ├── Transaction.cs
│   │   │   │   │   └── TransactionHistory.cs
│   │   │   │   ├── Enums/
│   │   │   │   │   └── TransactionStatus.cs
│   │   │   │   ├── Events/
│   │   │   │   │   ├── TransactionCreatedEvent.cs
│   │   │   │   │   └── ...
│   │   │   │   ├── StateMachine/
│   │   │   │   │   ├── TransactionStateMachine.cs
│   │   │   │   │   └── Guards/
│   │   │   │   ├── Interfaces/
│   │   │   │   │   └── ITransactionRepository.cs
│   │   │   │   └── ValueObjects/
│   │   │   ├── Application/
│   │   │   │   ├── Commands/
│   │   │   │   │   ├── CreateTransaction/
│   │   │   │   │   │   ├── CreateTransactionCommand.cs
│   │   │   │   │   │   ├── CreateTransactionHandler.cs
│   │   │   │   │   │   └── CreateTransactionValidator.cs
│   │   │   │   │   └── CancelTransaction/
│   │   │   │   │       └── ...
│   │   │   │   ├── Queries/
│   │   │   │   │   ├── GetTransaction/
│   │   │   │   │   │   ├── GetTransactionQuery.cs
│   │   │   │   │   │   ├── GetTransactionHandler.cs
│   │   │   │   │   │   └── TransactionDto.cs
│   │   │   │   │   └── ListTransactions/
│   │   │   │   │       └── ...
│   │   │   │   ├── EventHandlers/            ← Bu modülün kendi event'lerini dinleyen handler'lar
│   │   │   │   └── Mappings/                 ← Entity ↔ DTO mapping
│   │   │   └── Infrastructure/
│   │   │       ├── Persistence/
│   │   │       │   ├── TransactionRepository.cs
│   │   │       │   ├── TransactionConfiguration.cs  ← EF Core entity config
│   │   │       │   └── Migrations/
│   │   │       └── Services/
│   │   │
│   │   ├── Skinora.Payments/                 ← Blockchain iletişim, ödeme doğrulama
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │
│   │   ├── Skinora.Steam/                    ← Steam sidecar iletişim, trade offer
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │       └── SteamSidecarClient.cs     ← Typed HttpClient
│   │   │
│   │   ├── Skinora.Users/                    ← Profil, cüzdan, itibar
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │
│   │   ├── Skinora.Auth/                     ← Steam OpenID, JWT, refresh token
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │
│   │   ├── Skinora.Notifications/            ← Event → bildirim, kanal dispatch
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   │   └── EventHandlers/            ← Diğer modüllerin event'lerini dinler
│   │   │   └── Infrastructure/
│   │   │       ├── Channels/
│   │   │       │   ├── EmailChannel.cs
│   │   │       │   ├── TelegramChannel.cs
│   │   │       │   ├── DiscordChannel.cs
│   │   │       │   └── PlatformChannel.cs
│   │   │       └── Templates/
│   │   │
│   │   ├── Skinora.Admin/                    ← Admin paneli, rol/yetki yönetimi
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │
│   │   ├── Skinora.Disputes/                 ← İtiraz yönetimi, eskalasyon
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   └── Infrastructure/
│   │   │
│   │   └── Skinora.Fraud/                    ← Anomali tespiti, flag'leme
│   │       ├── Domain/
│   │       ├── Application/
│   │       └── Infrastructure/
│   │
│   └── Skinora.Shared/                       ← Cross-module contract'lar
│       ├── Domain/
│       │   ├── BaseEntity.cs
│       │   ├── IAuditableEntity.cs
│       │   ├── ISoftDeletable.cs
│       │   └── IDomainEvent.cs              ← EventId + OccurredAt zorunlu
│       ├── Enums/                            ← Modüller arası paylaşılan enum'lar
│       │   ├── StablecoinType.cs
│       │   └── NotificationType.cs
│       ├── Events/                           ← Modüller arası event contract'lar
│       │   ├── TransactionCreatedEvent.cs
│       │   └── PaymentReceivedEvent.cs
│       ├── Exceptions/
│       │   ├── DomainException.cs
│       │   ├── BusinessRuleException.cs
│       │   ├── NotFoundException.cs
│       │   └── IntegrationException.cs
│       ├── Models/
│       │   ├── ApiResponse.cs
│       │   └── PagedResult.cs
│       └── Interfaces/
│           ├── IUnitOfWork.cs
│           └── IOutboxService.cs
│
├── tests/
│   ├── Skinora.Transactions.Tests/
│   │   ├── Unit/
│   │   │   ├── StateMachine/
│   │   │   ├── Commands/
│   │   │   └── Validators/
│   │   └── Integration/
│   ├── Skinora.Payments.Tests/
│   ├── Skinora.Steam.Tests/
│   ├── Skinora.Users.Tests/
│   ├── Skinora.Auth.Tests/
│   ├── Skinora.Notifications.Tests/
│   ├── Skinora.Admin.Tests/
│   ├── Skinora.Disputes.Tests/
│   ├── Skinora.Fraud.Tests/
│   └── Skinora.API.Tests/                    ← Integration test (API seviyesi)
│       └── Integration/
```

#### 4.2.2 Proje Referans Kuralları

```
Skinora.API ──────→ Tüm modüller (controller'lar modül Application katmanını çağırır)
Skinora.API ──────→ Skinora.Shared

Her modül ────────→ Skinora.Shared (base class, event contract, ortak interface)

Modül ────────✗──→ Başka modül (YASAK — compile time'da proje referansı eklenmez)
```

Modüller arası iletişim yalnızca §6.3'te tanımlanan iki yolla yapılır: (1) event contract + MediatR, (2) read-only query interface. Bir modül başka bir modülün tipine, servisine veya repository'sine doğrudan erişemez.

#### 4.2.3 Modül İçi Klasör Yapısı — Kılavuz

Her modül aynı iç yapıyı takip eder:

| Klasör | İçerik | Bağımlılık kuralı |
|--------|--------|--------------------|
| `Domain/Entities/` | Entity sınıfları | Hiçbir şeye bağımlı değil (sadece Shared base class) |
| `Domain/Enums/` | Modüle özgü enum'lar | Yok |
| `Domain/Events/` | Modül-içi domain event tanımları | Shared IDomainEvent |
| `Domain/Interfaces/` | Repository interface'leri | Yok |
| `Domain/ValueObjects/` | Value object'ler | Yok |
| `Domain/StateMachine/` | State machine config (varsa) | Stateless kütüphanesi |
| `Application/Commands/` | Command + Handler + Validator (klasör per use case) | Domain, Shared |
| `Application/Queries/` | Query + Handler + DTO (klasör per use case) | Domain, Shared |
| `Application/EventHandlers/` | Domain event handler'ları | Domain, Shared |
| `Application/Mappings/` | Entity ↔ DTO mapping | Domain |
| `Infrastructure/Persistence/` | Repository impl, EF config, migration | Domain (interface), EF Core |
| `Infrastructure/Services/` | Dış servis client'ları | Domain (interface) |

**Use case başına klasör:** Her command ve query kendi klasöründe yaşar. Klasör içinde Command/Query, Handler ve Validator (varsa) + DTO bir arada bulunur.

**Domain event yerleşimi:** Sadece modül içinde tüketilen event'ler `Domain/Events/`'te kalır. Başka modül tarafından dinlenen event'ler `Skinora.Shared/Events/`'e taşınır.

### 4.3 Frontend — Next.js

```
frontend/
├── src/
│   ├── app/                                  ← App Router
│   │   ├── [locale]/                         ← i18n (en, zh, es, tr)
│   │   │   ├── (auth)/                       ← Auth layout grubu
│   │   │   │   ├── callback/
│   │   │   │   │   └── page.tsx
│   │   │   │   └── layout.tsx
│   │   │   ├── (main)/                       ← Ana layout grubu (authenticated)
│   │   │   │   ├── dashboard/
│   │   │   │   │   └── page.tsx
│   │   │   │   ├── transactions/
│   │   │   │   │   ├── new/
│   │   │   │   │   │   └── page.tsx
│   │   │   │   │   ├── [id]/
│   │   │   │   │   │   └── page.tsx
│   │   │   │   │   └── page.tsx
│   │   │   │   ├── profile/
│   │   │   │   │   └── page.tsx
│   │   │   │   ├── notifications/
│   │   │   │   │   └── page.tsx
│   │   │   │   └── layout.tsx
│   │   │   ├── (admin)/                      ← Admin layout grubu
│   │   │   │   ├── dashboard/
│   │   │   │   ├── transactions/
│   │   │   │   ├── flags/
│   │   │   │   ├── users/
│   │   │   │   ├── settings/
│   │   │   │   ├── roles/
│   │   │   │   ├── audit-logs/
│   │   │   │   └── layout.tsx
│   │   │   ├── layout.tsx                    ← Root layout
│   │   │   └── page.tsx                      ← Landing page
│   │   └── not-found.tsx
│   │
│   ├── components/
│   │   ├── ui/                               ← Paylaşılan UI bileşenleri
│   │   │   ├── Button.tsx
│   │   │   ├── Modal.tsx
│   │   │   ├── StatusBadge.tsx
│   │   │   ├── CountdownTimer.tsx
│   │   │   ├── DataTable.tsx
│   │   │   └── ...
│   │   └── features/                         ← Feature-spesifik bileşenler
│   │       ├── transactions/
│   │       │   ├── TransactionCard.tsx
│   │       │   ├── TransactionDetail.tsx
│   │       │   └── TransactionActions.tsx
│   │       ├── auth/
│   │       ├── profile/
│   │       ├── notifications/
│   │       └── admin/
│   │
│   ├── lib/
│   │   ├── api/
│   │   │   ├── client.ts                     ← API client (fetch + ApiResponse<T> unwrap)
│   │   │   ├── auth.ts                       ← Auth endpoint'leri
│   │   │   ├── transactions.ts               ← Transaction endpoint'leri
│   │   │   └── ...
│   │   ├── hooks/
│   │   │   ├── useAuth.ts
│   │   │   ├── useSignalR.ts
│   │   │   └── useTransactionUpdates.ts
│   │   ├── signalr/
│   │   │   └── connection.ts                 ← SignalR client setup
│   │   └── utils/
│   │       ├── format.ts                     ← Para, tarih formatlama
│   │       └── validation.ts
│   │
│   ├── types/
│   │   ├── api.ts                            ← ApiResponse<T>, PagedResult<T>
│   │   ├── transaction.ts
│   │   ├── user.ts
│   │   └── enums.ts                          ← 06 enum'larının TS karşılıkları
│   │
│   └── i18n/
│       ├── config.ts
│       └── messages/
│           ├── en.json
│           ├── zh.json
│           ├── es.json
│           └── tr.json
│
├── public/
├── next.config.ts
├── tsconfig.json
├── tailwind.config.ts
└── package.json
```

### 4.4 Node.js Sidecar'lar

#### 4.4.1 Steam Sidecar

```
sidecar-steam/
├── src/
│   ├── bot/                                  ← Bot yönetimi
│   │   ├── BotManager.ts
│   │   ├── BotSession.ts
│   │   └── BotHealthCheck.ts
│   ├── trade/                                ← Trade offer iş mantığı
│   │   ├── TradeOfferService.ts
│   │   └── InventoryService.ts
│   ├── api/                                  ← .NET'ten gelen HTTP endpoint'ler
│   │   ├── routes.ts
│   │   └── handlers/
│   ├── webhook/                              ← .NET'e callback gönderim
│   │   ├── WebhookClient.ts                  ← HMAC imzalama dahil
│   │   └── WebhookPayloads.ts
│   ├── health/
│   │   └── HealthController.ts
│   ├── config/
│   │   └── index.ts
│   └── index.ts                              ← Entry point
├── tsconfig.json
├── Dockerfile
└── package.json
```

#### 4.4.2 Blockchain Servisi

```
sidecar-blockchain/
├── src/
│   ├── wallet/                               ← HD Wallet, adres üretimi
│   │   ├── WalletManager.ts
│   │   └── AddressGenerator.ts
│   ├── monitor/                              ← Blockchain izleme
│   │   ├── TransactionMonitor.ts
│   │   └── PostCancelMonitor.ts              ← İptal sonrası kademeli izleme
│   ├── transfer/                             ← Giden transferler
│   │   ├── TransferService.ts
│   │   └── RefundService.ts
│   ├── api/
│   │   ├── routes.ts
│   │   └── handlers/
│   ├── webhook/
│   │   ├── WebhookClient.ts
│   │   └── WebhookPayloads.ts
│   ├── health/
│   │   └── HealthController.ts
│   ├── config/
│   │   └── index.ts
│   └── index.ts
├── tsconfig.json
├── Dockerfile
└── package.json
```

### 4.5 Dosya Yerleştirme Rehberi

"Bu dosya nereye gider?" sorusunun cevabı:

| Dosya tipi | Konum | Örnek |
|------------|-------|-------|
| Entity sınıfı | `Modules/Skinora.{Modül}/Domain/Entities/` | `Transaction.cs` |
| Enum (modüle özgü) | `Modules/Skinora.{Modül}/Domain/Enums/` | `TransactionStatus.cs` |
| Enum (modüller arası) | `Skinora.Shared/Enums/` | `StablecoinType.cs` |
| Domain event (modül-içi) | `Modules/Skinora.{Modül}/Domain/Events/` | Sadece modül içinde tüketilen event'ler |
| Domain event (modüller arası) | `Skinora.Shared/Events/` | `TransactionCreatedEvent.cs` |
| Repository interface | `Modules/Skinora.{Modül}/Domain/Interfaces/` | `ITransactionRepository.cs` |
| Repository implementasyonu | `Modules/Skinora.{Modül}/Infrastructure/Persistence/` | `TransactionRepository.cs` |
| EF Core entity config | `Modules/Skinora.{Modül}/Infrastructure/Persistence/` | `TransactionConfiguration.cs` |
| Command + Handler + Validator | `Modules/Skinora.{Modül}/Application/Commands/{UseCaseName}/` | `CreateTransaction/` |
| Query + Handler + DTO | `Modules/Skinora.{Modül}/Application/Queries/{UseCaseName}/` | `GetTransaction/` |
| Controller | `Skinora.API/Controllers/{Modül}/` | `TransactionsController.cs` |
| Middleware | `Skinora.API/Middleware/` | `ExceptionHandlingMiddleware.cs` |
| Exception sınıfı | `Skinora.Shared/Exceptions/` | `DomainException.cs` |
| Base entity / interface | `Skinora.Shared/Domain/` | `BaseEntity.cs` |
| Modül DI registration | `Skinora.API/Configuration/` | `TransactionsModule.cs` |
| Unit test | `tests/Skinora.{Modül}.Tests/Unit/` | `StateMachine/` |
| Integration test | `tests/Skinora.{Modül}.Tests/Integration/` | — |
| React component (paylaşılan) | `frontend/src/components/ui/` | `StatusBadge.tsx` |
| React component (feature) | `frontend/src/components/features/{feature}/` | `TransactionCard.tsx` |
| API client fonksiyonu | `frontend/src/lib/api/` | `transactions.ts` |
| Custom hook | `frontend/src/lib/hooks/` | `useSignalR.ts` |
| TypeScript type | `frontend/src/types/` | `transaction.ts` |
| i18n çeviri | `frontend/src/i18n/messages/` | `tr.json` |

---

## 5. Naming Conventions

### 5.1 C# (.NET Backend)

| Öğe | Convention | Örnek |
|-----|-----------|-------|
| Class, Record, Struct | PascalCase | `TransactionRepository`, `CreateTransactionCommand` |
| Interface | I + PascalCase | `ITransactionRepository`, `IOutboxService` |
| Method | PascalCase | `GetByIdAsync`, `FireTriggerAsync` |
| Property | PascalCase | `CreatedAt`, `SellerId` |
| Private field | _camelCase | `_repository`, `_logger` |
| Constant | PascalCase | `MaxRetryCount`, `DefaultPageSize` |
| Enum type | PascalCase (tekil) | `TransactionStatus`, `StablecoinType` |
| Enum value | UPPER_SNAKE_CASE | `CANCELLED_TIMEOUT`, `TRADE_OFFER_SENT_TO_SELLER` |
| Local variable, parameter | camelCase | `transactionId`, `cancellationToken` |
| Async method | Suffix: Async | `CreateAsync`, `GetByIdAsync` |
| Generic type parameter | T + PascalCase | `TResponse`, `TEntity` |

### 5.2 CQRS & MediatR Naming

| Öğe | Pattern | Örnek |
|-----|---------|-------|
| Command | {Fiil}{Entity}Command | `CreateTransactionCommand`, `CancelTransactionCommand` |
| Command handler | {Fiil}{Entity}Handler | `CreateTransactionHandler` |
| Query | Get{Entity}Query / List{Entity}Query | `GetTransactionQuery`, `ListTransactionsQuery` |
| Query handler | Get{Entity}Handler / List{Entity}Handler | `GetTransactionHandler` |
| Validator | {Fiil}{Entity}Validator | `CreateTransactionValidator` |
| DTO (response) | {Entity}Dto / {Entity}DetailDto | `TransactionDto`, `TransactionDetailDto` |
| Domain event | {Entity}{PastTense}Event | `TransactionCreatedEvent`, `PaymentReceivedEvent` |
| Event handler | {Event}Handler | `TransactionCreatedHandler`, `PaymentReceivedHandler` |

### 5.3 TypeScript (Frontend)

| Öğe | Convention | Örnek |
|-----|-----------|-------|
| React component | PascalCase (dosya ve export) | `TransactionCard.tsx` |
| Hook | camelCase, use prefix | `useAuth.ts`, `useSignalR.ts` |
| Utility fonksiyonu | camelCase | `formatCurrency`, `formatDate` |
| Type / Interface | PascalCase | `Transaction`, `ApiResponse<T>` |
| Enum | PascalCase type, UPPER_SNAKE_CASE value | C# ile birebir eşleşir |
| Dosya (component) | PascalCase.tsx | `StatusBadge.tsx` |
| Dosya (non-component) | camelCase.ts | `client.ts`, `format.ts` |
| Klasör | kebab-case | `audit-logs/`, `trade-offers/` |
| CSS class (Tailwind) | kebab-case | Tailwind default |

### 5.4 Veritabanı (SQL Server)

| Öğe | Convention | Örnek |
|-----|-----------|-------|
| Tablo | PascalCase, çoğul | `Transactions`, `TradeOffers`, `AuditLogs` |
| Kolon | PascalCase | `CreatedAt`, `SellerId`, `TransactionStatus` |
| Primary key | Id | `Id` (GUID) |
| Foreign key | {Entity}Id | `SellerId`, `TransactionId` |
| Index | IX_{Tablo}_{Kolon(lar)} | `IX_Transactions_Status_CreatedAt` |
| Unique index | UQ_{Tablo}_{Kolon(lar)} | `UQ_Users_SteamId` |
| Filtered index | IX_{Tablo}_{Kolon}_Filtered | `IX_Transactions_Status_Filtered` |
| Check constraint | CK_{Tablo}_{Kural} | `CK_Transactions_PricePositive` |
| Default constraint | DF_{Tablo}_{Kolon} | `DF_Transactions_CreatedAt` |

### 5.5 Namespace

| Proje | Namespace pattern |
|-------|-------------------|
| Modül domain | `Skinora.{Modül}.Domain.Entities` |
| Modül application | `Skinora.{Modül}.Application.Commands.{UseCaseName}` |
| Modül infrastructure | `Skinora.{Modül}.Infrastructure.Persistence` |
| Shared | `Skinora.Shared.Events`, `Skinora.Shared.Exceptions` |
| API | `Skinora.API.Controllers.{Modül}` |

### 5.6 Tutarlılık Kuralları

- Aynı kavram tüm katmanlarda aynı isimle anılmalıdır. C#'ta `TransactionStatus.CANCELLED_TIMEOUT` ise TypeScript'te de `CANCELLED_TIMEOUT`, API response'ta da `"CANCELLED_TIMEOUT"`, DB'de de `CANCELLED_TIMEOUT` olmalıdır.
- 06'daki entity ve field isimleri otoritedir. Kod bunlarla birebir eşleşmelidir.
- 07'deki API response field isimleri (camelCase) frontend type'larıyla birebir eşleşmelidir.

---

## 6. Mimari Sınırlar & Modül Kuralları

### 6.1 Katman Kuralları

| Katman | Sorumluluk | Bağımlılık |
|--------|-----------|------------|
| **Domain** | Entity, value object, enum, domain event, repository interface, state machine | Yalnızca Shared base class. Infrastructure, Application, framework bağımlılığı YASAK. **İstisna:** Stateless kütüphanesi (state machine) izinli. |
| **Application** | Command/query handler, validation, DTO, event handler, orchestration | Domain + Shared. EF Core, HttpClient gibi framework detayları YASAK (interface üzerinden). |
| **Infrastructure** | Repository impl, EF Core config, dış servis client, migration | Domain (interface'leri implemente eder) + framework kütüphaneleri. |
| **API** | Controller, middleware, filter, DI registration | Application (handler'ları MediatR ile çağırır) + Shared. |

**Bağımlılık yönü:** API → Application → Domain ← Infrastructure. Alt katman üst katmanın sorumluluğunu üstlenmez.

### 6.2 İş Kuralı Yerleşimi

| Kural tipi | Nerede | Örnek |
|-----------|--------|-------|
| Domain kuralı (her zaman geçerli) | Domain katmanı — entity method veya state machine guard | "Ödeme yapıldıktan sonra alıcı iptal edemez" |
| Use case kuralı (akışa bağlı) | Application katmanı — handler içinde | "İşlem oluşturulurken envanter kontrolü yap" |
| Input validation | Application katmanı — FluentValidation | "Fiyat 0'dan büyük olmalı" |
| Yetkilendirme | API katmanı — policy attribute | "Sadece admin erişebilir" |

**YASAK yerler:** Controller içinde iş kuralı, repository içinde iş kuralı, frontend component içinde backend'de olması gereken kural.

### 6.3 Modüller Arası İletişim

```
Modül A                          Skinora.Shared                         Modül B
   │                                   │                                   │
   │── domain event publish ─────────→ │ ←── event contract tanımı         │
   │   (outbox'a yaz)                  │                                   │
   │                                   │ ──── MediatR dispatch ──────────→ │
   │                                   │                                   │── event handler
   │                                   │                                   │   (consume)
```

| Kural | Açıklama |
|-------|----------|
| Doğrudan referans YASAK | Modül A, Modül B'nin class/interface/servisine doğrudan erişemez |
| DB erişimi | Her modül yalnızca kendi entity'lerine erişir. Başka modülün tablosuna doğrudan sorgu YASAK. |

**İki meşru iletişim yolu:**

| Yol | Ne zaman | Yön | Örnek |
|-----|----------|-----|-------|
| **Event** (asenkron, outbox) | Bir modül diğerini bilgilendiriyorsa — tepkisel akış | Tek yönlü: yayınla, dinle | `TransactionCreatedEvent` → Notification modülü dinler |
| **Read-only query interface** (senkron) | Bir modül diğerinin verisine anlık erişim gerektiriyorsa | İstek-yanıt: çağır, al | Transaction handler → `IUserQueryService.GetPayoutAddressAsync()` |

**Query interface kuralları:**
- Interface Shared'da tanımlanır, implementasyonu ilgili modülün Infrastructure'ında
- **Salt okunur** — başka modülün verisini değiştirme YASAK
- Yazma/mutasyon yalnızca event üzerinden yapılır

### 6.4 Shared Kernel Sınırları

Skinora.Shared'a **yalnızca** şunlar konur:

| İçerik | Örnek |
|--------|-------|
| Base entity ve interface'ler | `BaseEntity`, `IAuditableEntity`, `ISoftDeletable` |
| Modüller arası event contract'lar | `TransactionCreatedEvent`, `PaymentReceivedEvent` |
| Modüller arası paylaşılan enum'lar | `StablecoinType`, `NotificationType` |
| Ortak exception sınıfları | `DomainException`, `BusinessRuleException` |
| Ortak model'ler | `ApiResponse<T>`, `PagedResult<T>` |
| Ortak interface'ler | `IUnitOfWork`, `IOutboxService` |

**Shared'a konulmaz:** İş mantığı, servis implementasyonu, repository implementasyonu, modüle özgü DTO.

---

## 7. Zaman & Format Standartları

### 7.1 Zaman

| Kural | Açıklama |
|-------|----------|
| **Tüm servislerde UTC** | .NET backend, Node.js sidecar'lar, frontend API çağrıları — her yerde UTC |
| DB saklama | `datetime2(7)` + UTC. Entity'lerde `DateTime` (Kind = Utc). `DateTimeOffset` kullanılmaz — offset her zaman 0 olacağı için gereksiz overhead |
| API iletişimi | ISO 8601 UTC: `2026-03-19T14:32:00Z` (07 §2.8 K8 ile tutarlı) |
| Sidecar webhook | Timestamp field ISO 8601 UTC. Replay window hesaplaması UTC bazlı |
| Hangfire scheduling | UTC. `TimeZoneInfo.Utc` açıkça belirtilir |
| AuditLog timestamp | UTC |
| Frontend gösterimi | Backend'den gelen UTC → kullanıcının tarayıcı timezone'una çevrilir. Dönüşüm yalnızca UI render anında yapılır, API çağrılarında ve state'te her zaman UTC kalır |
| **Local time yasak** | Hiçbir katmanda `DateTime.Now` veya `DateTime.Today` kullanılmaz. `DateTime.UtcNow` veya saat sağlayıcı interface üzerinden alınır |
| **EF Core UTC enforce** | EF Core, SQL Server'dan okunan `DateTime` değerlerinde `Kind`'ı `Unspecified` olarak döndürür. Bu sessiz hatayı önlemek için global value converter zorunludur: `builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>()` (`ConfigureConventions`'da). Converter, okunan her `DateTime`'a `Kind = Utc` atar. |

### 7.2 Para Formatı

| Kural | Açıklama |
|-------|----------|
| API'de | String, 2 ondalık: `"100.00"` (07 §2.8 K8) |
| DB'de | `decimal(18,6)` — hesaplama hassasiyeti için 6 ondalık |
| Kod içinde | `decimal` tipi zorunlu. `float` / `double` YASAK (§14'te detaylandırılır) |

### 7.3 ID Formatı

| Kural | Açıklama |
|-------|----------|
| Entity ID | GUID (07 §2.1 K1) |
| Steam ID | String — `"76561198012345678"` |
| Blockchain adresi | String — tam adres, maskeleme frontend'in işi (07 §2.10 K10) |

### 7.4 Null Handling

| Kural | Açıklama |
|-------|----------|
| API response | `null` döner, field gizlenmez (07 §2.8 K8) |
| C# | Nullable reference types aktif. `?` ile açıkça belirtilir |
| TypeScript | `null` ve `undefined` ayrımı net yapılır. API'den gelen `null` değerler `null` olarak kalır |

---

## 8. Backend Kuralları

### 8.1 API

- Endpoint'ler 07'deki konvansiyonlara (K1-K10) uygun yazılır.
- Request ve response modelleri açıkça ayrılır. Internal entity doğrudan API'ye açılmaz.
- Controller iş kuralı barındırmaz — MediatR üzerinden handler'a delege eder.
- API davranışı 07 ile uyumlu olmalıdır. 07'de tanımlı olmayan endpoint eklenmez.

**Controller → Handler akışı:**

```csharp
[ApiController]
[Route("api/v1/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpPost]
    [Authorize(Policy = "MobileAuthenticatorRequired")]
    [ProducesResponseType(typeof(ApiResponse<TransactionDto>), 201)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
```

> Controller **raw result** döner. `ApiResponseWrapperFilter` tüm başarılı response'ları otomatik olarak `ApiResponse<T>` envelope'una sarar. Controller'da `ApiResponse<T>.Success(...)` çağrılmaz — çifte sarmalama riski üretir.
>
> **Sorumluluk ayrımı:** Success envelope → `ApiResponseWrapperFilter`. Error envelope → `ExceptionHandlingMiddleware`. Controller hiçbirine dokunmaz.

### 8.2 Validation

Tüm dış input FluentValidation ile validate edilir. Validation iki seviyede düşünülür:

| Seviye | Nerede | Örnek |
|--------|--------|-------|
| Shape validation | FluentValidation (pipeline behavior) | "Fiyat boş olamaz, GUID formatı doğru mu" |
| Business rule validation | Handler veya domain | "Bu stablecoin tipi destekleniyor mu, satıcı aktif mi" |

**Pipeline behavior ile otomatik validation:**

```csharp
public class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!_validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count > 0)
            throw new FluentValidation.ValidationException(failures);

        return await next();
    }
}
```

> Handler'a ulaşan her request zaten shape-valid'dir. Handler business rule'lara odaklanır.

### 8.3 Error Handling

**Exception hiyerarşisi:**

| Exception | HTTP Status | Kullanım |
|-----------|------------|----------|
| `ValidationException` (FluentValidation) | 400 | Shape validation hatası |
| `NotFoundException` | 404 | Kaynak bulunamadı |
| `BusinessRuleException` | 422 | İş kuralı ihlali (ör: "aktif işlem varken hesap silinemez") |
| `DomainException` | 409 | Domain ihlali (ör: geçersiz state geçişi, concurrency conflict) |
| `IntegrationException` | 502 | Dış servis hatası (Steam, blockchain) |
| Diğer (beklenmeyen) | 500 | Catch-all — loglama + genel hata mesajı |

> **Not:** 204 kullanılmaz — tutarlılık için 200 + boş envelope döner (07 §2.5).

**Global exception handler:**

```csharp
public class ExceptionHandlingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, errorCode, message) = ex switch
            {
                FluentValidation.ValidationException ve =>
                    (400, "VALIDATION_ERROR", ve.Errors),
                NotFoundException nf =>
                    (404, "NOT_FOUND", nf.Message),
                BusinessRuleException br =>
                    (422, br.ErrorCode, br.Message),
                DomainException de =>
                    (409, de.ErrorCode, de.Message),
                IntegrationException ie =>
                    (502, "INTEGRATION_ERROR", ie.Message),
                _ =>
                    (500, "INTERNAL_ERROR", "Beklenmeyen bir hata oluştu.")
            };

            // 500 → log as Error, diğerleri → log as Warning
            // Teknik detay son kullanıcıya verilmez
            // traceId eklenir (07 §2.4 K4)
            await WriteErrorResponse(context, statusCode, errorCode, message);
        }
    }
}
```

> Exception yutulmaz. Beklenen hatalar (business rule, not found) Warning, beklenmeyen hatalar Error olarak loglanır. Hassas bilgi (stack trace, connection string) son kullanıcıya verilmez.

### 8.4 Idempotency

- Tekrar gelebilecek çağrılar idempotent ele alınmalıdır.
- Özellikle: ödeme doğrulama webhook'ları, trade offer callback'leri, state transition tetikleyicileri.
- Outbox consumer'ları `ProcessedEvent` tablosuyla idempotency sağlar (§9'da detaylandırılır).
- Webhook callback'lerde event ID kontrolü yapılır — aynı event ID ile gelen ikinci çağrı skip edilir.

### 8.5 Transaction Yönetimi

- DB transaction sınırları bilinçli tanımlanmalıdır.
- Tek transaction içinde dış servis çağrısı (HTTP, blockchain) yapılmamalıdır.
- State geçişi + outbox event yazma → aynı DB transaction (atomik garanti).
- Dış servis çağrısı → outbox consumer tarafından, ayrı transaction'da.
- "Başarılı gibi görünen ama yarım kalan işlem" üretecek pattern YASAK.

---

## 9. Domain Kuralları

### 9.1 Genel Prensipler

- Domain modeli iş dilini yansıtmalıdır. Teknik jargon yerine iş terimleri kullanılır.
- Geçersiz state geçişleri sessizce kabul edilmemelidir — `DomainException` fırlatılır.
- Domain katmanında infrastructure detayı (EF Core, HttpClient, Hangfire) bulunmaz.
- Domain event, value object, aggregate gibi yapılar gerçekten ihtiyaç varsa kullanılır — her şeyi DDD pattern'ine zorlamak gereksiz karmaşıklık ekler.

### 9.2 State Machine Pattern'i

Transaction state machine Stateless kütüphanesiyle yönetilir (05 §4.3). Tüm durumlar ve geçişler tek yerde tanımlanır.

**Konfigürasyon yapısı:**

```csharp
public class TransactionStateMachine
{
    private readonly StateMachine<TransactionStatus, TransactionTrigger> _machine;

    public TransactionStateMachine(Transaction transaction)
    {
        _machine = new StateMachine<TransactionStatus, TransactionTrigger>(
            () => transaction.Status,
            s => transaction.Status = s);

        ConfigureTransitions(transaction);
    }

    private void ConfigureTransitions(Transaction transaction)
    {
        _machine.Configure(TransactionStatus.CREATED)
            .Permit(TransactionTrigger.BuyerAccept, TransactionStatus.ACCEPTED)
            .Permit(TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT)
            .Permit(TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER)
            .Permit(TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER)
            .Permit(TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN)
            .Permit(TransactionTrigger.FraudFlag, TransactionStatus.FLAGGED);

        // ... diğer durumlar (05 §4.2'deki geçiş tablosu ile birebir)

        _machine.Configure(TransactionStatus.ITEM_ESCROWED)
            .PermitIf(TransactionTrigger.BuyerCancel,
                TransactionStatus.CANCELLED_BUYER,
                () => !transaction.IsPaymentReceived,  // Guard: ödeme yapılmadıysa
                "Ödeme yapıldıktan sonra alıcı iptal edemez");
    }
}
```

**State machine kuralları:**

| Kural | Açıklama |
|-------|----------|
| Tek kaynak | Tüm geçişler `TransactionStateMachine`'de. Başka yerde state değiştirme YASAK. |
| Guard fonksiyonları | Geçiş koşulları `PermitIf` ile tanımlanır. İş kuralı guard'da yaşar. |
| Side effect'ler | State machine **side effect üretmez**. OnEntry/OnExit'te Hangfire, HTTP çağrısı vb. YASAK. State geçişi domain event üretir, side effect'ler (bildirim, timeout scheduling) Application katmanındaki event handler'larda yapılır. |
| Geçersiz geçiş | Stateless otomatik fırlatır. Ek olarak `DomainException` ile sarmalanır. |
| Test | Her durum × her trigger kombinasyonu (geçerli ve geçersiz) test edilmelidir (§19). |

### 9.3 Domain Event & Outbox Pattern'i

Her state geçişi bir domain event üretir. Event kaybı sıfır garantisi outbox pattern ile sağlanır (05 §5.1).

**Event tanımlama:**

```csharp
// Skinora.Shared/Domain/
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

// Skinora.Shared/Events/ — modüller arası contract
public record TransactionCreatedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    decimal Price,
    StablecoinType Stablecoin,
    DateTime OccurredAt) : IDomainEvent;
```

**Tek ID, tek otorite:** Domain event'in `EventId`'si = outbox envelope'daki `eventId` = consumer idempotency key. Ayrı ID üretilmez.

**Outbox'a yazma (aynı DB transaction içinde):**

```csharp
public class CreateTransactionHandler
    : IRequestHandler<CreateTransactionCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(
        CreateTransactionCommand request, CancellationToken ct)
    {
        // 1. Entity oluştur
        var transaction = Transaction.Create(/* ... */);

        // 2. Repository'ye kaydet
        _repository.Add(transaction);

        // 3. Outbox'a event yaz (aynı transaction'da)
        await _outboxService.PublishAsync(new TransactionCreatedEvent(
            EventId: Guid.NewGuid(),
            TransactionId: transaction.Id,
            SellerId: transaction.SellerId,
            /* ... */
            OccurredAt: DateTime.UtcNow), ct);

        // 4. SaveChanges — entity + outbox atomik commit
        await _unitOfWork.SaveChangesAsync(ct);

        return _mapper.ToDto(transaction);
    }
}
```

> **Kritik:** Adım 2, 3, 4 aynı DB transaction'da. Ya hepsi commit olur ya hiçbiri. Event kaybı sıfır.

**Consumer idempotency:**

```csharp
public class TransactionCreatedNotificationHandler
    : INotificationHandler<TransactionCreatedEvent>
{
    public async Task Handle(
        TransactionCreatedEvent @event, CancellationToken ct)
    {
        // Idempotency kontrolü
        if (await _processedEventStore.ExistsAsync(@event.EventId, ct))
            return; // Zaten işlenmiş — skip

        // İş mantığı
        await _notificationService.SendBuyerInvite(
            @event.TransactionId, @event.BuyerId, ct);

        // İşlenmiş olarak kaydet (aynı transaction'da)
        await _processedEventStore.MarkAsProcessedAsync(@event.EventId, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
```

> **Önemli sınırlama:** `ProcessedEvent` kontrolü yalnızca consumer'ın kendi DB kaydı için idempotency sağlar. Dış yan etkiler (bildirim gönderme, HTTP çağrısı vb.) için exactly-once garanti **vermez** — crash sonrası retry'da dış çağrı tekrarlanabilir. Bu nedenle:
> - Dış servislere yapılan çağrılarda `EventId` bazlı idempotency key gönderilmelidir (ör: bildirim servisinde `deduplicationId`).
> - Dış çağrı idempotent değilse, önce `MarkAsProcessed` + `SaveChanges` yapılıp ardından dış çağrı fire-and-forget/outbox ile tetiklenmelidir (at-most-once tercih ediliyorsa).
> - Hangi stratejinin uygulanacağı akışın iş gereksinimlerine bağlıdır — çift bildirim tolere edilebilir mi yoksa kesinlikle engellenmeli mi?

### 9.4 Value Object Kullanımı

Value object yalnızca gerçekten değer semantiği olan kavramlar için kullanılır:

| Aday | Value object mi? | Gerekçe |
|------|-------------------|---------|
| Para tutarı (amount + stablecoin) | Evet | İki field birlikte anlam taşır, karşılaştırma değer bazlı |
| Cüzdan adresi | Değerlendirilir | Format validasyonu varsa faydalı |
| Steam ID | Hayır | Basit string wrapper gereksiz karmaşıklık |

> Şüphe varsa value object **kullanma**. İhtiyaç ortaya çıktığında refactor daha az maliyetli.

### 9.5 Snapshot Pattern

Belirli field'lar işlem oluşturma veya kabul anında "dondurulur" ve işlem boyunca değişmez. Profil güncellemeleri aktif işlemleri etkilemez (02 §12.1-12.3, 06 §3.5).

| Snapshot field | Ne zaman alınır | Kaynak |
|---------------|----------------|--------|
| `SellerPayoutAddress` | İşlem oluşturma anında | Profildeki DefaultPayoutAddress veya override |
| `BuyerRefundAddress` | Alıcı kabul anında | Profildeki DefaultRefundAddress veya override |
| `CommissionRate`, `CommissionAmount` | İşlem oluşturma anında | SystemSetting'deki aktif oran |
| `MarketPriceAtCreation` | İşlem oluşturma anında | ItemPriceCache'den (fraud detection için) |
| Item detayları (`ItemName`, `ItemIconUrl`, `ItemExterior`, `ItemType`, `ItemInspectLink`) | İşlem oluşturma anında | Steam envanter API response'u |

**Kurallar:**
- Snapshot field'ları entity üzerinde yazıldıktan sonra **asla güncellenmez**.
- Profil değişikliği yapılsa bile aktif işlemler eski snapshot değerini kullanır.
- Agent "güncel profil adresini getir" yerine "transaction üzerindeki snapshot adresi kullan" mantığıyla kod yazmalıdır.
- Snapshot field'ların kaynağı handler'da açıkça belirtilir:

```csharp
// Doğru: snapshot al
transaction.SellerPayoutAddress = seller.DefaultPayoutAddress;
transaction.CommissionRate = await _settingsService.GetCommissionRateAsync(ct);

// YANLIŞ: sonradan profili sorgulama
var address = await _userRepo.GetPayoutAddressAsync(transaction.SellerId);
```

### 9.6 Denormalized Field Güncelleme Kuralları

06 §8.2'de tanımlanan denormalized field'lar belirli event'lerde güncellenir. Bu güncellemeler event handler'larda yapılır — handler dışında denormalized field güncelleme YASAK.

| Field | Güncelleme zamanı | Güncelleme kuralı |
|-------|-------------------|-------------------|
| `User.CompletedTransactionCount` | COMPLETED event'inde | +1 (hem satıcı hem alıcı) |
| `User.SuccessfulTransactionRate` | COMPLETED veya CANCELLED_* event'inde | `completed / (completed + iptal)` — **sorumluluk prensibi:** yalnızca iptal eden tarafın skoru etkilenir. CANCELLED_ADMIN denominatöre dahil edilmez |
| `User.CooldownExpiresAt` | İptal sayısı limiti aşıldığında | `UtcNow + cooldownDuration` (SystemSetting'den) |
| `PlatformSteamBot.ActiveEscrowCount` | Item escrow / release event'inde | +1 escrow, -1 release |
| `PlatformSteamBot.DailyTradeOfferCount` | Trade offer gönderim event'inde | +1 (gece yarısı UTC'de sıfırlanır) |

**Kurallar:**

| Senaryo | Transaction garantisi | Örnek |
|---------|----------------------|-------|
| **Same-module** (event handler ve entity aynı modülde) | Aynı DB transaction'da atomik güncelleme | `PlatformSteamBot.ActiveEscrowCount` → Steam modülü event handler'ı kendi entity'sini günceller |
| **Cross-module** (event handler farklı modülün entity'sini günceller) | Eventual consistency — Outbox dispatcher üzerinden ayrı transaction'da | `User.CompletedTransactionCount` → Transactions modülü COMPLETED event yayınlar, Users modülü event handler'ı kendi entity'sini günceller |

**Cross-module güncelleme kuralları:**
- Event handler **idempotent** olmalı — aynı event birden fazla kez işlense aynı sonucu vermeli.
- Reconciliation job ile periyodik doğrulama yapılır (ör: uygulama crash veya event işleme hatası durumunda).
- Kısa süreli tutarsızlık kabul edilir — kullanıcı-görünür kritik kararlarda (ör: cooldown kontrolü) source of truth sorgulanır.

**Same-module güncelleme kuralları:**
- Handler ve entity aynı modülde olduğunda güncelleme aynı DB transaction'da atomik yapılır.

**Genel kurallar:**
- İtibar skoru hesaplamasında `CooldownExpiresAt` kontrolü transaction oluşturma handler'ında yapılır — cooldown aktifse `BusinessRuleException` fırlatılır.

---

## 10. Veri Erişimi Kuralları

### 10.1 Genel Prensipler

- Veri erişimi repository interface'i arkasında yönetilir. Handler'lar doğrudan `DbContext` kullanmaz.
- N+1 sorgu pattern'i YASAK. Include/ThenInclude veya projection kullanılır.
- Yazma ve okuma davranışları bilinçli ayrılır — okuma sorgularında `AsNoTracking()`.
- Persistence modeli (EF entity) ile domain modeli aynı sınıf olabilir (MVP), ancak mapping gerektiren durumlarda ayrılır.

### 10.2 Repository Pattern

```csharp
// Domain/Interfaces/
public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Transaction>> ListAsync(
        TransactionFilter filter, CancellationToken ct);
    void Add(Transaction transaction);
    void Update(Transaction transaction);
}

// Infrastructure/Persistence/
public class TransactionRepository : ITransactionRepository
{
    private readonly AppDbContext _context;

    public async Task<Transaction?> GetByIdAsync(
        Guid id, CancellationToken ct)
    {
        return await _context.Transactions
            .Include(t => t.TradeOffers)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }
}
```

### 10.3 Soft Delete

06 §1.3'te tanımlanan soft delete stratejisi EF Core global query filter ile uygulanır:

```csharp
// Entity config
builder.HasQueryFilter(e => !e.IsDeleted);

// Admin: silinmişleri de görmesi gerektiğinde
var allUsers = await _context.Users
    .IgnoreQueryFilters()
    .ToListAsync(ct);
```

| Kural | Açıklama |
|-------|----------|
| Varsayılan | Tüm sorgular `IsDeleted = false` filtreli |
| Bypass | Yalnızca admin audit senaryolarında `IgnoreQueryFilters()` |
| Immutable entity'ler | TransactionHistory, BlockchainTransaction, TradeOffer, AuditLog — soft delete yok, DELETE yok |
| Mutable Catalog | SystemSetting — soft delete yok, delete yasak. Seed ile oluşturulur, yalnızca Value güncellenebilir. Key seti migration ile yönetilir (06 §1.3) |

### 10.4 Concurrency Kontrolü

```csharp
// Entity
public byte[] RowVersion { get; set; }

// EF config
builder.Property(e => e.RowVersion)
    .IsRowVersion();
```

State machine geçişlerinde optimistic concurrency zorunludur. İki concurrent webhook aynı transaction'ı güncellemeye çalışırsa `DbUpdateConcurrencyException` fırlar → retry veya hata.

### 10.5 Sorgu Kuralları

| Kural | Açıklama |
|-------|----------|
| Okuma sorguları | `AsNoTracking()` — change tracking overhead yok |
| Listeleme | Projection (Select) tercih edilir — tüm entity çekilmez |
| Pagination | `Skip/Take` + `OrderBy` zorunlu. Sınırsız liste dönmek YASAK |
| Filtreleme | IQueryable üzerinde zincirleme — DB'de çalışır, belleğe çekilmez |
| Include | Yalnızca gereken navigation property'ler. Tüm graf yükleme YASAK |

### 10.6 Migration Kuralları

| Kural | Açıklama |
|-------|----------|
| Naming | `{Timestamp}_{Açıklayıcıİsim}` — `20260319_AddDisputeEscalationFields` |
| İçerik | Bir migration tek bir değişikliği kapsar. Karışık migration YASAK |
| Geri alınabilirlik | Her migration'ın Down() metodu çalışır durumda olmalı |
| Veri migration | Schema migration ile veri migration ayrı tutulur |
| CI sıralaması | Migration → build → test. Migration başarısızsa pipeline durur |
| Review | Migration SQL output'u PR'da review edilir (`dotnet ef migrations script`) |
| FK cascade | `DeleteBehavior.NoAction` zorunlu. Cascade delete kullanılmaz — silme işlemi application katmanında yönetilir (06 §4.2) |

---

## 11. Integration Kuralları

### 11.1 Genel Prensipler

- Tüm dış servis çağrıları timeout, retry ve failure senaryoları düşünülerek yazılır.
- Retry kör şekilde uygulanmaz — hangi hataların retryable olduğu bilinçli belirlenir.
- Webhook/callback çağrıları doğrulanır (§11.3).
- Dış bağımlılık adapter/abstraction arkasında izole edilir. Üçüncü parti servis davranışı iş kuralının içine gömülmez.
- Cache kullanılan entegrasyonlar (Steam envanter, market fiyat) için TTL ve invalidation kuralları 08'de tanımlıdır. Cache implementasyonu 08'deki spesifikasyonlara uymalıdır.

### 11.2 Sidecar HTTP Client

.NET backend → sidecar iletişimi typed HttpClient ile yapılır:

```csharp
// DI registration
services.AddHttpClient<ISteamSidecarClient, SteamSidecarClient>(client =>
{
    client.BaseAddress = new Uri(config["SteamSidecar:BaseUrl"]);
    client.DefaultRequestHeaders.Add("X-Internal-Key", config["SteamSidecar:ApiKey"]);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Interface — modülün Domain katmanında (tek modül kullanıyorsa modülde, birden fazla kullanıyorsa Shared'da)
public interface ISteamSidecarClient  // Skinora.Steam/Domain/Interfaces/
{
    Task<SendTradeOfferResult> SendTradeOfferAsync(
        SendTradeOfferRequest request, CancellationToken ct);
}

// Implementation (Infrastructure)
public class SteamSidecarClient : ISteamSidecarClient
{
    private readonly HttpClient _client;

    public async Task<SendTradeOfferResult> SendTradeOfferAsync(
        SendTradeOfferRequest request, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync("/trade-offers", request, ct);

        if (!response.IsSuccessStatusCode)
            throw new IntegrationException("Steam sidecar",
                $"Trade offer gönderimi başarısız: {response.StatusCode}");

        return await response.Content.ReadFromJsonAsync<SendTradeOfferResult>(ct);
    }
}
```

### 11.3 Webhook Doğrulama

Sidecar → .NET callback'lerinde HMAC-SHA256 imza doğrulama (05 §3.4):

```csharp
public class WebhookSignatureMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/webhooks"))
        {
            await next(context);
            return;
        }

        var signature = context.Request.Headers["X-Signature"].FirstOrDefault();
        var timestamp = context.Request.Headers["X-Timestamp"].FirstOrDefault();
        var nonce = context.Request.Headers["X-Nonce"].FirstOrDefault();

        // 1. Header eksikliği kontrolü
        // 2. Timestamp replay window kontrolü (±5 dakika)
        // 3. Nonce tekrar kontrolü (ProcessedNonce tablosu)
        // 4. HMAC-SHA256 doğrulama: HMAC(timestamp + nonce + body, sharedSecret)
        // 5. Başarısızsa 401 dön

        await next(context);
    }
}
```

### 11.4 Retry Stratejisi

| Senaryo | Retryable? | Strateji |
|---------|-----------|----------|
| HTTP 5xx (sunucu hatası) | Evet | Exponential backoff: 1dk, 5dk, 15dk |
| HTTP 429 (rate limit) | Evet | `Retry-After` header'ına uyarak |
| HTTP 408 (request timeout) | Evet | Exponential backoff (geçici hata) |
| HTTP 4xx (diğer client hataları) | Hayır | Hata logla, retry yapma — istek kendisi hatalı |
| Timeout (bağlantı/okuma) | Evet | Exponential backoff |
| Network hatası | Evet | Exponential backoff |
| Tüm denemeler başarısız | — | Admin alert, işlem ilgili state'te bekler |

> **Entegrasyon-spesifik geçici 4xx'ler:** Bazı üçüncü parti API'ler belirli durumlarda 409 (Conflict) veya 423 (Locked) gibi kodları geçici hata olarak döndürebilir. Bu kodların retryable olup olmadığı entegrasyon bazında 08'de tanımlanır. Yukarıdaki tablo varsayılan davranışı belirler; entegrasyon-spesifik override'lar 08'deki tablolarda yer alır.

### 11.5 Circuit Breaker

Retry tek başına yeterli değildir — sürekli başarısız olan bir servise istek göndermeye devam etmek kaynak israfıdır ve cascade failure yaratabilir. Circuit breaker pattern uygulanır (08 §1.2-1.3):

| Parametre | Varsayılan | Açıklama |
|-----------|-----------|----------|
| Failure threshold | 5 | Ardışık başarısızlık sayısı → circuit açılır |
| Recovery timeout | 30 saniye | Circuit açıkken bekleme süresi |
| Half-open probe | 1 istek | Recovery sonrası tek deneme isteği |
| Success threshold | 2 | Half-open'dan closed'a geçiş için ardışık başarı |

**Uygulanacak entegrasyonlar:**
- Steam sidecar HTTP client
- Blockchain sidecar HTTP client
- Email servisi (Resend)
- Telegram / Discord Bot API

**Davranış:**
- Circuit **closed** (normal): istekler geçer, başarısızlık sayacı artar
- Circuit **open** (trip): istekler anında `IntegrationException` ile reddedilir, dış servise gitmez
- Circuit **half-open** (recovery): tek probe isteği gönderilir; başarılıysa closed'a döner, başarısızsa open kalır
- Circuit state değişimleri Warning seviyesinde loglanır

> Entegrasyon-spesifik parametreler (onay eşiği, polling aralığı, cache TTL, rate limit değerleri) 08'de tanımlıdır. Kod bu parametreleri 08'den alır, 09'da tekrar tanımlamaz.

---

## 12. Contract & Event Versioning

### 12.1 Neden Gerekli

Skinora üç runtime (.NET, Node.js Steam, Node.js Blockchain) içerir. Bunlar arasında HTTP ve webhook ile iletişim olur. Ayrıca outbox event'leri serialize/deserialize edilir. Herhangi birinde payload yapısı değiştiğinde, diğer tarafın bozulmaması gerekir.

### 12.2 Payload Versioning

Tüm servisler arası payload'larda version alanı bulunur:

**Sidecar HTTP request/response:**
```json
{
  "version": 1,
  "transactionId": "...",
  "tradeUrl": "..."
}
```

**Webhook callback:**
```json
{
  "version": 1,
  "eventId": "guid",
  "eventType": "TRADE_OFFER_ACCEPTED",
  "data": { ... }
}
```

> **Webhook contract kanonik alan kuralı:**
> - `eventId` (body): Zorunlu. Consumer idempotency anahtarı — aynı eventId ile gelen ikinci çağrı skip edilir (§9.3).
> - `timestamp`, `nonce` (yalnızca header): `X-Timestamp` ve `X-Nonce` olarak gönderilir (§17.5). HMAC imza hesaplamasında kullanılır (§11.3). Body'de tekrar edilmez.
> - `X-Signature` (header): HMAC-SHA256 doğrulama imzası.
> - `X-Correlation-Id` (header): Trace zinciri koruması (§18.4).

**Outbox event:**
```json
{
  "schemaVersion": 1,
  "eventType": "TransactionCreatedEvent",
  "eventId": "guid",
  "occurredAt": "2026-03-19T14:32:00Z",
  "payload": { ... }
}
```

### 12.3 Değişiklik Kuralları

| Değişiklik tipi | Breaking mi? | Kural |
|----------------|-------------|-------|
| Yeni field ekleme (opsiyonel) | Hayır | Mevcut consumer'lar etkilenmez |
| Mevcut field kaldırma | **Evet** | Yeni versiyon gerektirir |
| Field tipi değiştirme | **Evet** | Yeni versiyon gerektirir |
| Field adı değiştirme | **Evet** | Yeni versiyon gerektirir |
| Enum'a yeni değer ekleme | Hayır | Consumer bilinmeyen değeri handle edebilmeli |

### 12.4 Breaking Change Prosedürü

1. Yeni `version` numarası ile payload tanımla
2. Gönderen taraf yeni version'ı göndermeye başlar
3. Alan taraf iki version'ı da handle eder (geçiş süresi)
4. Eski version kullanımı sıfırlandığında eski handler kaldırılır

### 12.5 Tolerant Reader Prensibi

- Consumer bilinmeyen field'ları yok sayar (hata fırlatmaz)
- Consumer bilinmeyen enum değerlerini loglar ama crash etmez
- Zorunlu field eksikse hata fırlatır

### 12.6 Event Schema Evrimi

Outbox event'leri için **additive-only** stratejisi:
- Yeni field ekle, eskisini kaldırma
- Breaking değişiklik gerekiyorsa: yeni event tipi oluştur (ör: `TransactionCreatedEventV2`)
- Dispatcher hem V1 hem V2 consumer'ı destekler (geçiş süresi)

### 12.7 Contract Test

Sidecar ↔ Backend HTTP sözleşmesi ve webhook payload yapısı custom JSON schema validation ile doğrulanır (05 §10). Her payload değişikliğinde contract test güncellenir.

---

## 13. Background Job Kuralları

### 13.1 Genel Prensipler

Hangfire tüm background job'ların altyapısıdır (05 §4.4). Job'lar SQL Server'da saklanır — uygulama restart'ında kaybolmaz.

### 13.2 Job Tipleri

| Tip | Kullanım | Örnek |
|-----|----------|-------|
| **Delayed job** | Belirli süre sonra çalışacak tek seferlik iş | Timeout job: "30 dakika sonra timeout kontrolü yap" |
| **Recurring job** | Periyodik tekrarlanan iş (cron bazlı, min. dakika) | Retention cleanup: "her gece eski job kayıtlarını temizle" |
| **Fire-and-forget** | Hemen çalışacak ama asenkron iş | "Bildirim gönder" (ama API response'u beklemesin) |

### 13.3 Timeout Scheduling Pattern'i

State machine geçişi domain event üretir, timeout scheduling Application event handler'da yapılır:

```csharp
// Application/EventHandlers/ItemEscrowedTimeoutHandler.cs
public class ItemEscrowedTimeoutHandler
    : INotificationHandler<ItemEscrowedEvent>
{
    public async Task Handle(ItemEscrowedEvent @event, CancellationToken ct)
    {
        var transaction = await _repository.GetByIdAsync(@event.TransactionId, ct);

        // Ödeme timeout job'ı schedule et
        var jobId = BackgroundJob.Schedule<ITimeoutService>(
            s => s.CheckPaymentTimeoutAsync(transaction.Id),
            TimeSpan.FromMinutes(transaction.PaymentTimeoutMinutes));

        transaction.PaymentTimeoutJobId = jobId;

        // Timeout uyarı job'ı (sürenin %X'inde)
        var warningMinutes = transaction.PaymentTimeoutMinutes
            * _settings.TimeoutWarningRatio;
        var warningJobId = BackgroundJob.Schedule<ITimeoutService>(
            s => s.SendTimeoutWarningAsync(transaction.Id),
            TimeSpan.FromMinutes(warningMinutes));

        transaction.TimeoutWarningJobId = warningJobId;

        await _unitOfWork.SaveChangesAsync(ct);
    }
}
```

> **Atomiklik sınırı:** Hangfire job scheduling ile business DB commit farklı transaction'lardır — tam atomiklik garanti edilmez. `BackgroundJob.Schedule` çağrıldığında Hangfire kendi storage'ına yazar; ardından `SaveChangesAsync` başarısız olursa orphan job kalır. Aynı şekilde retry durumunda aynı job ikinci kez schedule edilebilir. Bu nedenle:
> - Timeout/warning job handler'ları çalışırken **mutlaka** transaction'ın güncel state'ini doğrulamalıdır (§13.3 devamındaki "Job handler state doğrulama" kuralına bak).
> - Reconciliation: periyodik olarak entity'deki jobId ile Hangfire'daki gerçek job durumu karşılaştırılarak orphan job'lar temizlenmelidir.

**İptal durumunda job temizliği:**
```csharp
// State geçişi iptal'e veya tamamlanma'ya olduğunda — her iki job da temizlenir
if (transaction.PaymentTimeoutJobId != null)
    BackgroundJob.Delete(transaction.PaymentTimeoutJobId);
if (transaction.TimeoutWarningJobId != null)
    BackgroundJob.Delete(transaction.TimeoutWarningJobId);
```

**Job handler state doğrulama (zorunlu):**

`BackgroundJob.Delete` yalnızca henüz çalışmamış job'ları durdurur. Zaten processing'e başlamış bir job için delete geç kalabilir. Bu nedenle her timeout/warning job handler'ı çalışırken **mutlaka** transaction'ın güncel durumunu doğrulamalıdır:

```csharp
public async Task CheckPaymentTimeoutAsync(Guid transactionId)
{
    var transaction = await _repository.GetByIdAsync(transactionId);

    // Savunma kontrolleri — koşul tutmuyorsa no-op
    if (transaction == null) return;
    if (transaction.Status != TransactionStatus.ITEM_ESCROWED) return;
    if (transaction.TimeoutFrozenAt != null) return;
    if (transaction.PaymentDeadline > DateTime.UtcNow) return;

    // Koşullar tutuyorsa timeout işlemini gerçekleştir
    // ...
}
```

Bu pattern `SendTimeoutWarningAsync` için de geçerlidir — handler çalışırken transaction zaten tamamlanmış/iptal edilmişse veya freeze aktifse no-op döner. Ek olarak warning handler `TimeoutWarningSentAt != null` ise no-op döner (çift uyarı engeli). Warning başarıyla gönderildikten sonra `transaction.TimeoutWarningSentAt = DateTime.UtcNow` set edilir.

> **Scope ve reset kuralı (06 §3.5):** `TimeoutWarningJobId` ve `TimeoutWarningSentAt` yalnızca ITEM_ESCROWED (ödeme aşaması) için geçerlidir — diğer aşamalar poller ile çalışır, ayrı warning job kullanmaz. ITEM_ESCROWED'a her girişte `TimeoutWarningSentAt = NULL` olarak başlar. ITEM_ESCROWED'dan çıkışta (PAYMENT_RECEIVED veya CANCELLED_*) job iptal edilir ve her iki alan NULL'a döner. Freeze resume sonrası yeni job schedule edilir ve SentAt NULL'a resetlenir.

### 13.4 Outbox Dispatcher

Hangfire recurring job'lar cron bazlı çalışır — minimum granülarite pratikte dakikadır. Outbox dispatcher gibi saniye bazlı polling gerektiren job'lar için **self-rescheduling delayed job** pattern'i kullanılır:

```csharp
public class OutboxDispatcher : IOutboxDispatcher
{
    private readonly int _intervalSeconds = 5; // Configurable
    private readonly IDistributedLockProvider _lockProvider;

    public async Task ProcessAndRescheduleAsync()
    {
        // Çoklu instance/restart durumunda tek dispatcher garantisi.
        // Lock süresi, ProcessPendingEventsAsync'in en kötü senaryo süresinden
        // büyük olmalıdır. Batch size ile processing süresi sınırlandırılır.
        await using var lockHandle = await _lockProvider
            .TryAcquireAsync("outbox-dispatcher", _lockLeaseDuration);

        if (lockHandle == null)
            return; // Başka instance zaten çalışıyor

        try
        {
            await ProcessPendingEventsAsync();
        }
        finally
        {
            // Exception olsa bile chain kırılmasın — rescheduling garanti
            BackgroundJob.Schedule<IOutboxDispatcher>(
                d => d.ProcessAndRescheduleAsync(),
                TimeSpan.FromSeconds(_intervalSeconds));
        }
    }
}

// Uygulama başlangıcında bir kez tetikle (Program.cs)
BackgroundJob.Enqueue<IOutboxDispatcher>(
    d => d.ProcessAndRescheduleAsync());
```

> **Neden recurring değil:** Hangfire'ın cron tabanlı recurring job mekanizması saniye hassasiyetini güvenilir şekilde desteklemez. Self-rescheduling pattern, job tamamlandıktan sonra kendini yeniden schedule ederek hem güvenilirlik hem esneklik sağlar. Polling aralığı configurable.
>
> **Tekillik garantisi:** Distributed lock, çoklu instance veya restart durumunda birden fazla dispatcher zincirinin oluşmasını önler. Lock alınamazsa job sessizce sonlanır — zaten aktif bir dispatcher vardır.
>
> **Lock lease kuralı:** Lock süresi (`_lockLeaseDuration`) sabit değildir — `ProcessPendingEventsAsync`'in en kötü senaryo süresinden büyük olmalıdır. Bunu garanti etmek için: (1) batch size ile tek döngüde işlenecek event sayısı sınırlandırılır (ör: maks 50 event/batch), (2) lock süresi = batch süre üst sınırı + güvenlik marjı. İşlem süresi lock lease'i aşarsa lock otomatik serbest kalır ve ikinci dispatcher çalışabilir — bu nedenle consumer idempotency (§9.3) son savunma hattıdır.
>
> **Hata dayanıklılığı:** `try/finally` ile rescheduling garanti altına alınır. `ProcessPendingEventsAsync()` exception fırlatsa bile bir sonraki polling döngüsü schedule edilir, chain kırılmaz.

### 13.5 Job Kuralları

| Kural | Açıklama |
|-------|----------|
| Idempotent | Her job tekrar çalıştırılabilir olmalı. Aynı job iki kez çalışsa aynı sonucu üretmeli. |
| Timeout | Her job'da `[AutomaticRetry(Attempts = 3)]` + iş mantığında idempotency kontrolü |
| Exception | Job exception fırlatırsa Hangfire otomatik retry eder. Kalıcı hata → `FailedState` → admin alert |
| Naming | Job sınıf adı açıklayıcı: `PaymentTimeoutChecker`, `OutboxDispatcher` |
| Logging | Her job başlangıç ve bitiş loglar. Correlation ID taşır. |
| UTC | Tüm schedule'lar UTC (§7.1) |

### 13.6 Timeout Freeze Pattern

Platform bakımı veya Steam/blockchain kesintisi sırasında aktif işlemlerin timeout'ları dondurulur (02 §3.3, 05 §4.4, 06 §8.1).

**Freeze başlatma (admin tetikler):**
```csharp
// 1. Tüm aktif transaction'larda TimeoutFrozenAt = UtcNow
// 2. Tüm aktif timeout VE warning Hangfire job'larını durdur:
//    BackgroundJob.Delete(transaction.PaymentTimeoutJobId)
//    BackgroundJob.Delete(transaction.TimeoutWarningJobId)
// 3. Admin'e ve aktif kullanıcılara bildirim gönder
```

**Resume (admin tetikler):**
```csharp
// 1. Her aktif transaction için frozen süreyi hesapla
var frozenDuration = DateTime.UtcNow - transaction.TimeoutFrozenAt!.Value;

// 2. Deadline'ları uzat
transaction.PaymentDeadline = transaction.PaymentDeadline!.Value + frozenDuration;
// ... diğer deadline field'ları

// 3. TimeoutFrozenAt = null
transaction.TimeoutFrozenAt = null;

// 4. SaveChanges (business DB atomik)
// 5. Timeout job'ını yeni deadline'a göre yeniden schedule et
//    transaction.PaymentTimeoutJobId = BackgroundJob.Schedule(...)
// 6. Warning job yalnızca henüz gönderilmemişse schedule et:
//    if (transaction.TimeoutWarningSentAt == null)
//        transaction.TimeoutWarningJobId = BackgroundJob.Schedule(...)
```

> **Not:** Adım 4 (DB commit) ve adım 5 (Hangfire schedule) farklı transaction'lardır. DB commit başarılı olup Hangfire schedule başarısız olursa, job'sız deadline kalır. Bu durumda reconciliation job (§13.3 notu) orphan durumu yakalar.

**Kurallar:**
- Freeze/resume business state güncellemesi atomik DB transaction'da yapılır. Hangfire job işlemleri ayrı adımdır — tam atomiklik garanti edilmez (§13.3 atomiklik sınırı notu).
- Freeze sırasında yeni timeout job schedule edilmez.
- Freeze süresi tüm aktif deadline'lara eklenir — sıfırlanmaz, duraklatılır.
- Timeout checker job çalıştığında `TimeoutFrozenAt != null` ise skip eder.

### 13.7 Retention Cleanup

OutboxMessage ve ProcessedEvent infrastructure verileri retention-based silinir (06 §1.3):

```csharp
// Recurring job — günlük
RecurringJob.AddOrUpdate<IRetentionCleanupService>(
    "retention-cleanup",
    s => s.CleanupExpiredRecordsAsync(),
    Cron.Daily,
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
```

| Entity | Retention | Koşul |
|--------|-----------|-------|
| OutboxMessage | 30 gün | `ProcessedAt != null AND ProcessedAt < UtcNow - 30 gün` |
| ProcessedEvent | 30 gün | `ProcessedAt < UtcNow - 30 gün` |

Retention süreleri admin-configurable (SystemSetting).

---

## 14. Para & Hassas Hesaplama Kuralları

### 14.1 Temel Kural

Tüm para hesaplamalarında `decimal` kullanılır. `float` ve `double` **YASAK** — kayan nokta hassasiyet kaybı escrow platformunda fon kaybı demektir.

### 14.2 Hassasiyet

| Katman | Tip | Hassasiyet |
|--------|-----|-----------|
| C# kod | `decimal` | 28-29 anlamlı basamak |
| SQL Server | `decimal(18,6)` | 6 ondalık (hesaplama hassasiyeti) |
| API response | `string` | 2 ondalık (`"100.00"`) — §7.2 |
| Blockchain | — | Token'ın native hassasiyetinde (USDT/USDC: 6 ondalık) |

### 14.3 Rounding Stratejisi

| Kural | Açıklama |
|-------|----------|
| Varsayılan | `MidpointRounding.ToZero` (truncation) — kullanıcı aleyhine yuvarlama yapılmaz |
| Uygulanma noktası | Yalnızca son adımda (API'ye dönmeden veya blockchain'e göndermeden hemen önce). Ara hesaplamalarda yuvarlama yapılmaz. |
| Gerekçe | Banker's rounding küçük farklar üretir. Escrow'da "nereye gitti bu 0.01 USDT?" sorusu güven kırar. Truncation öngörülebilir. |

### 14.4 Hesaplama Formülleri

**Komisyon:**
```
komisyon = işlem_tutarı × komisyon_oranı
alıcı_ödemesi = işlem_tutarı + komisyon
satıcı_alacağı = işlem_tutarı                  ← komisyon alıcıdan alınır, satıcının tutarına dokunmaz (02 §5)
platform_geliri = komisyon - gönderim_gas_fee
```

**İade (alıcıya):**
```
iade_tutarı = ödenen_tutar - gas_fee
minimum_iade_eşiği = 2 × gas_fee

eğer iade_tutarı < minimum_iade_eşiği:
    iade yapılmaz → admin alert
değilse:
    iade gönderilir
```

**Gas fee koruma (satıcıya ödeme):**
```
gas_fee_eşiği = komisyon × gas_fee_koruma_oranı  (varsayılan: %10)

eğer gas_fee ≤ gas_fee_eşiği:
    satıcı_alacağı = işlem_tutarı  (gas fee platform karşılar)
değilse:
    aşan_kısım = gas_fee - gas_fee_eşiği
    satıcı_alacağı = işlem_tutarı - aşan_kısım
```

**Fazla ödeme:**
```
fazla = ödenen_tutar - beklenen_tutar
iade_fazla = fazla - gas_fee

eğer iade_fazla < minimum_iade_eşiği:
    fazla iade yapılmaz → admin alert + log
değilse:
    fazla iade gönderilir, işlem beklenen tutarla devam eder
```

### 14.5 Test Zorunluluğu

Her hesaplama formülü için birim test zorunludur. Test senaryoları:

| Senaryo | Açıklama |
|---------|----------|
| Normal case | Standart tutar, standart komisyon |
| Boundary | Minimum tutar, minimum iade eşiği sınırında |
| Gas fee edge case | Gas fee > komisyon, gas fee = 0 |
| Fazla ödeme | Küçük fazla (iade eşiği altı), büyük fazla |
| Eksik ödeme | Kısmi ödeme → iade |
| Precision | 6 ondalık basamağa kadar doğruluk |

> Para hesaplaması içeren her PR'da bu testlerin güncel ve geçer durumda olması zorunludur.

---

## 15. Güvenlik Kuralları

Detaylı güvenlik mimarisi 05 §6'da tanımlıdır. Bu bölüm kod yazım anında uyulması gereken kuralları kapsar.

- Secret, token, private key gibi hassas bilgiler koda gömülmez. Environment variable veya secret manager üzerinden alınır (05 §3.5).
- Hassas veriler (private key, session token, kullanıcı şifresi) loglanmaz. Log middleware'inde maskeleme uygulanır.
- Yetkilendirme yalnızca UI'da değil, backend'de de enforce edilir. Endpoint tiplerine göre:

| Endpoint tipi | Kural |
|--------------|-------|
| Kullanıcı / admin endpoint'leri | `[Authorize(Policy = "...")]` zorunlu |
| Public endpoint'ler (07'de tanımlı: P1, A1, A2, T5 public, U5) | `[AllowAnonymous]` açıkça belirtilir |
| Webhook endpoint'ler (sidecar callback) | Auth attribute yok, `WebhookSignatureMiddleware` ile korunur (§11.3) |
| Health check | `[AllowAnonymous]` |

Varsayılan: auth zorunlu. İstisna açıkça belirtilir ve gerekçelendirilir.
- Tüm dış input'lar validate edilir (§8.2). Validation atlanması YASAK.
- Kimlik doğrulama ve imza doğrulama gereken entegrasyonlar (webhook HMAC, Steam OpenID) atlanmaz, geçici olarak bile devre dışı bırakılmaz.
- Rate limiting kritik endpoint'lerde zorunludur (07 §2.9 K9).
- Replay koruması webhook endpoint'lerinde zorunludur — timestamp + nonce (§11.3).
- `DateTime.Now` kullanımı güvenlik açığı yaratabilir (replay window hesaplaması bozulur) — §7.1'deki UTC kuralı güvenlik açısından da zorunludur.
- SQL injection: EF Core parametrized queries kullanılır. Raw SQL yazılıyorsa `FromSqlInterpolated` zorunlu, string concatenation YASAK.
- XSS: Next.js auto-escape + CSP header'ları. `dangerouslySetInnerHTML` kullanımı code review gerektirir.

---

## 16. Frontend Kuralları

### 16.1 Genel Prensipler

- UI iş kuralı merkezi olmayan şekilde dağıtılmaz. İş kuralı backend'de yaşar, frontend `availableActions` (07 §7) gibi backend yanıtlarına güvenir.
- Component'ler mümkün olduğunca tek sorumluluk taşır.
- API contract'ı bozan sessiz dönüşümler yapılmaz.
- Loading, empty, error state'leri her sayfada açıkça ele alınır (04 §5'teki tanımlara uygun).
- Kullanıcıyı yanıltan optimistic update'ler kontrollü kullanılır — özellikle state geçişi tetikleyen aksiyonlarda backend yanıtı beklenir.

### 16.2 Server vs Client Component

| Tip | Kullanım | Örnek |
|-----|----------|-------|
| Server Component | Veri çekme, SEO gereken sayfalar, statik içerik | Sayfa layout'ları, liste sayfaları |
| Client Component | Kullanıcı etkileşimi, state, event handler | Form, modal, countdown timer, SignalR listener |

`"use client"` yalnızca gerçekten gerektiğinde yazılır. Varsayılan server component'tir.

### 16.3 API Client

```typescript
// lib/api/client.ts
interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: {
    code: string;
    message: string;
    details: Record<string, string[]> | null;
  } | null;
  traceId: string;
}

async function apiClient<T>(
  url: string,
  options?: RequestInit
): Promise<T> {
  const response = await fetch(`/api/v1${url}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${getAccessToken()}`,
      ...options?.headers,
    },
  });

  const body: ApiResponse<T> = await response.json();

  if (!body.success) {
    throw new ApiError(body.error!, body.traceId, response.status);
  }

  return body.data!;
}
```

> **Not:** Yukarıdaki `apiClient` örneği **client component'ler** (`"use client"`) içindir. `getAccessToken()` tarayıcı tarafında çalışır.
>
> **Server component'larda veri çekme:** Server component'lar tarayıcı storage'a erişemez. Server-side fetch için cookie-forwarding/session-based yaklaşım kullanılır — auth akışının detayları 05 §6'da tanımlıdır. Server fetch'te `Authorization` header'ı yerine `cookies()` API'si ile HttpOnly cookie iletilir.
>
> **CSRF koruması:** Cookie tabanlı auth kullanıldığında CSRF riski oluşur. Tüm auth cookie'lerinde `SameSite=Lax` (minimum) + `Secure` + `HttpOnly` flag'leri zorunludur. CSRF koruma stratejisi akış tipine göre ayrılır:
>
> | Akış tipi | CSRF koruması |
> |-----------|--------------|
> | **Server Actions** | Next.js built-in Origin/`allowedOrigins` doğrulaması aktif tutulur. Ek önlem gerekmez. |
> | **Route Handlers / API Routes** | Framework otomatik CSRF koruması sağlamaz. Mutasyon yapan route handler'larda `Origin` veya `Referer` header doğrulaması ya da CSRF token mekanizması uygulanmalıdır. |
>
> Detaylı CSRF politikası 05 §6'da tanımlıdır.
>
> Her modül kendi wrapper fonksiyonlarını `lib/api/` altında tanımlar. Server ve client fetch yardımcıları ayrı tutulur.

### 16.4 State Management

| State tipi | Nerede | Örnek |
|-----------|--------|-------|
| Server state (API verisi) | TanStack Query (React Query) | Transaction listesi, kullanıcı profili |
| UI state (lokal) | `useState` / `useReducer` | Modal açık/kapalı, form inputları |
| Global UI state | Zustand | Auth durumu, dil seçimi, tema |
| Real-time state | SignalR + local state | Transaction status güncellemesi |

Server state ve UI state karıştırılmaz. API'den gelen veri cache'lenir ve invalidation ile güncellenir — manuel senkronizasyon yapılmaz.

### 16.5 Form Validation

- Validation hem frontend'de (UX) hem backend'de (güvenlik) yapılır.
- Frontend validation backend'in kopyası değil, kullanıcı deneyimi odaklıdır (ör: anlık feedback).
- Backend validation tek otorite — frontend bypass edilse bile backend korur.
- Validation hata mesajları 07 §2.4 K4 formatıyla uyumlu gösterilir.

### 16.6 i18n

- Tüm kullanıcıya gösterilecek metin i18n dosyalarından gelir. Hardcoded Türkçe/İngilizce metin YASAK.
- 4 dil: EN, ZH, ES, TR (05 §7.3).
- Tarih, para ve sayı formatlama `Intl` API veya next-intl formatters ile yapılır.
- Fallback: kullanıcı dili tanımlı değilse İngilizce.

---

## 17. Node.js Sidecar Standartları

Steam sidecar ve Blockchain servisi için ortak kurallar.

### 17.1 Dil ve Araçlar

| Kural | Açıklama |
|-------|----------|
| Dil | TypeScript zorunlu. JavaScript dosyası YASAK. |
| Runtime | Node.js LTS (20+) |
| Package manager | npm (lock file commit edilir) |
| Linting | ESLint + Prettier |
| Build | `tsc` → `dist/` |

### 17.2 Proje Yapısı

§4.4'teki klasör yapısı takip edilir. Her sidecar aynı pattern'i uygular:

| Klasör | Sorumluluk |
|--------|-----------|
| `api/` | .NET'ten gelen HTTP endpoint'ler (routes + handlers) |
| `webhook/` | .NET'e giden callback gönderimi (HMAC imzalama dahil) |
| `health/` | Health check endpoint |
| `config/` | Environment config, sabitler |
| İş mantığı klasörleri | Steam: `bot/`, `trade/`. Blockchain: `wallet/`, `monitor/`, `transfer/` |

### 17.3 Error Handling

```typescript
// Hata sınıflandırması
class SidecarError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly retryable: boolean
  ) {
    super(message);
  }
}

// Steam'e özgü
class SteamApiError extends SidecarError { /* ... */ }
class BotSessionExpiredError extends SidecarError { /* ... */ }

// Blockchain'e özgü
class InsufficientGasError extends SidecarError { /* ... */ }
class TransactionFailedError extends SidecarError { /* ... */ }
```

- Hata yutulmaz. Catch edilen her hata ya loglanır ya yukarı fırlatılır.
- Retryable/non-retryable ayrımı hata sınıfında belirtilir.

### 17.4 Logging

Pino kullanılır, Loki'ye push edilir (05 §9.1):

```typescript
import pino from 'pino';

const logger = pino({
  level: process.env.LOG_LEVEL || 'info',
  formatters: {
    level: (label) => ({ level: label }),
  },
  // Loki transport config
});

// Kullanım — her log'da correlationId
logger.info({ correlationId, transactionId }, 'Trade offer sent');
```

- Structured JSON formatı.
- Correlation ID .NET'ten gelen `X-Correlation-Id` header'ından alınır ve tüm loglara eklenir.
- Secret (bot password, private key) loglanmaz.

### 17.5 Webhook Callback Gönderimi

```typescript
// webhook/WebhookClient.ts
async function sendCallback(
  endpoint: string,
  payload: WebhookPayload,
  correlationId: string    // .NET'ten gelen X-Correlation-Id'yi taşı
): Promise<void> {
  const timestamp = new Date().toISOString(); // UTC
  const nonce = crypto.randomUUID();
  const body = JSON.stringify(payload);

  const signature = crypto
    .createHmac('sha256', SHARED_SECRET)
    .update(`${timestamp}${nonce}${body}`)
    .digest('hex');

  // Webhook callback'lerde kimlik doğrulama yalnızca HMAC ile yapılır (§11.3).
  // X-Internal-Key yalnızca sidecar HTTP client isteklerinde kullanılır (§11.2).
  // X-Correlation-Id: .NET → sidecar → callback → .NET trace zincirini korur (§18.4).
  await fetch(`${BACKEND_URL}${endpoint}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Signature': signature,
      'X-Timestamp': timestamp,
      'X-Nonce': nonce,
      'X-Correlation-Id': correlationId,
    },
    body,
  });
}
```

### 17.6 Health Check

Her sidecar `/health` endpoint'i expose eder:

```typescript
// Döner: { status: "healthy" | "degraded" | "unhealthy", checks: [...] }
// Steam: bot session aktif mi, Steam API erişilebilir mi
// Blockchain: Tron node erişilebilir mi, hot wallet bakiyesi yeterli mi
```

Docker Compose health check bu endpoint'i kullanır.

### 17.8 Rate Limiting

Sidecar'lar dış API'lere istek gönderirken 08'deki rate limit değerlerine uymalıdır:

| API | Limit | Kaynak |
|-----|-------|--------|
| Steam trade offer | 5 / dakika / bot | 08 §2.6 |
| Steam Web API | ~1 req/s önerilen | 08 §2.6 |
| Telegram | 1 msg/s per chat, 30 msg/s toplam | 08 §5.3 |
| Discord | 5 msg/5s per channel | 08 §6.3 |
| TronGrid | Plan bazlı | 08 §3.1 |

İstek kuyruğu (queue) mekanizması rate limit aşımını önler. Rate limit aşıldığında (`429`) → `Retry-After` header'ına uyarak bekle.

### 17.9 Graceful Shutdown

```typescript
process.on('SIGTERM', async () => {
  logger.info('Graceful shutdown başlatıldı');
  // 1. Yeni istek kabul etmeyi durdur
  // 2. Mevcut işlemleri tamamla (timeout ile)
  // 3. Kaynakları temizle (bot session, DB connection)
  // 4. Process.exit(0)
});
```

---

## 18. Logging & Observability

### 18.1 Araçlar

| Runtime | Kütüphane | Hedef |
|---------|----------|-------|
| .NET | Serilog | Loki (sink) |
| Node.js | Pino | Loki (push) |
| Frontend | — | Hata → backend error reporting endpoint (ileride) |

### 18.2 Log Level Rehberi

| Level | Ne zaman | Örnek |
|-------|----------|-------|
| `Debug` | Geliştirme sırasında detay. Production'da kapalı. | "Query executed in 12ms", "Cache hit for key X" |
| `Information` | Normal iş akışı milestone'ları | "Transaction created: {id}", "Payment confirmed: {id}" |
| `Warning` | Beklenen ama dikkat gerektiren durumlar | "Retry attempt 2/3 for Steam API", "Rate limit approaching" |
| `Error` | Başarısız işlem, yakalanmış beklenmeyen hata | "Payment send failed: {error}", "Webhook signature invalid" |
| `Fatal` | Uygulama çalışamaz durumda | "DB connection failed", "Master key not found" |

### 18.3 Structured Log Formatı

```json
{
  "timestamp": "2026-03-19T14:32:00.123Z",
  "level": "Information",
  "message": "Transaction created",
  "correlationId": "abc-123",
  "transactionId": "def-456",
  "module": "Transactions",
  "userId": "ghi-789",
  "environment": "production"
}
```

Her log entry'de zorunlu field'lar: `timestamp`, `level`, `message`, `correlationId`.

### 18.4 Correlation ID

- Her API isteğinde `CorrelationIdMiddleware` benzersiz bir ID üretir (veya header'dan alır).
- Bu ID tüm handler'lara, servis çağrılarına, sidecar isteklerine (`X-Correlation-Id` header) ve loglara taşınır.
- Sidecar → .NET webhook callback'lerinde de correlation ID geri döner.
- Amaç: Tek bir kullanıcı aksiyonunun tüm runtime'lardaki izini tek ID ile takip etmek.

### 18.5 Secret Maskeleme

- Log'a yazılmadan önce hassas field'lar maskelenir: private key, API key, refresh token, cüzdan adresi (kısmi).
- Serilog destructuring policy veya enricher ile uygulanır.
- Maskeleme kuralı merkezi tanımlanır, her log noktasında tekrar edilmez.

### 18.6 AuditLog Yazım Kuralları

AuditLog (06 §3.20) Loki'den farklıdır — DB'de kalıcı, immutable kayıttır.

| Ne zaman yazılır | Örnek |
|-----------------|-------|
| Fon hareketi | Ödeme alındı, satıcıya gönderildi, iade yapıldı |
| Admin aksiyonu | İşlem iptal, flag onay/red, rol atama, parametre değişikliği |
| Güvenlik olayı | Login, cüzdan adresi değişikliği, hesap deaktivasyonu |
| State geçişi | TransactionHistory zaten tutar, AuditLog fon/admin/güvenlik boyutunu kapsar |

- AuditLog kaydı iş mantığının parçasıdır — "sonra ekleriz" yaklaşımı YASAK.
- AuditLog entity'si immutable — UPDATE ve DELETE YASAK (06 §1.3).
- **Aktör invariantı (06 §8.6a):** `ActorType` ve `ActorId` her zaman birlikte set edilir. `SYSTEM` → sentinel GUID, `ADMIN` → aksiyon anında aktif admin rolü zorunlu, `USER` → authenticated user ID. Mismatch (ör: ActorType = SYSTEM ama ActorId normal kullanıcı) yasaktır. Audit kayıt oluşturma merkezi servis üzerinden yapılır — doğrudan INSERT yasaktır.

---

## 19. Test Kuralları

### 19.1 Genel Prensipler

- Kritik iş kuralları testsiz bırakılmaz.
- Unit test, integration test ve contract test sorumlulukları ayrılır.
- Testler davranışı doğrular, implementasyon detayına aşırı bağlanmaz.
- Flaky test kabul edilmez — tespit edildiğinde düzeltilir veya kaldırılır.
- Bug fix ile birlikte regression testi eklenir.

### 19.2 Test Tipleri

| Tip | Kapsam | Araç | Ne zaman çalışır |
|-----|--------|------|-------------------|
| Unit | Domain logic, state machine, validation, hesaplama | xUnit + Moq | Her PR |
| Integration | DB + API — gerçek SQL Server | xUnit + TestContainers | Her PR |
| Contract | Sidecar ↔ Backend HTTP/webhook sözleşmesi | Custom JSON schema validation | Her PR (payload değişikliğinde) |
| E2E | Kritik akışlar uçtan uca | Playwright | Staging deploy sonrası |

### 19.3 Naming Convention

```
{MethodName}_{Scenario}_{ExpectedResult}

Örnekler:
FireTrigger_BuyerCancelWhenPaymentReceived_ThrowsDomainException
CalculateRefund_AmountBelowMinimumThreshold_ReturnsNoRefund
CreateTransaction_ValidRequest_ReturnsCreatedTransaction
```

### 19.4 Test Yapısı

```csharp
[Fact]
public async Task FireTrigger_BuyerCancelWhenPaymentReceived_ThrowsDomainException()
{
    // Arrange
    var transaction = CreateTestTransaction(TransactionStatus.PAYMENT_RECEIVED);
    var stateMachine = new TransactionStateMachine(transaction);

    // Act
    var act = () => stateMachine.FireAsync(TransactionTrigger.BuyerCancel);

    // Assert
    await Assert.ThrowsAsync<DomainException>(act);
    Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, transaction.Status);
}
```

### 19.5 Öncelikli Test Alanları

| Alan | Strateji | Gerekçe |
|------|----------|---------|
| State machine | Her durum × her trigger (geçerli + geçersiz) | Yanlış geçiş = veri bütünlüğü kaybı |
| Para hesaplama | Boundary value analysis (§14.5) | Yanlış hesaplama = fon kaybı |
| Outbox + idempotency | Aynı event iki kez işlendiğinde duplikasyon olmaması | Event kaybı/duplikasyonu = tutarsızlık |
| Webhook doğrulama | Geçerli/geçersiz imza, expired timestamp, replay nonce | Güvenlik açığı |
| Concurrency | İki concurrent güncelleme → RowVersion kontrolü | Race condition = veri kaybı |

### 19.6 Integration Test Setup

```csharp
public class IntegrationTestBase : IAsyncLifetime
{
    protected readonly AppDbContext Context;

    public async Task InitializeAsync()
    {
        // TestContainers ile SQL Server container başlat
        // EF Core migration uygula
        // Test verisi seed et
    }

    public async Task DisposeAsync()
    {
        // Container temizle
    }
}
```

---

## 20. Performans Kuralları

- Performans kritik alanlarda ölçmeden optimizasyon yapılmaz.
- Ancak bilinen kötü desenler en baştan engellenir:
  - N+1 sorgu (§10.5)
  - Sınırsız liste dönme (pagination zorunlu — §10.5)
  - Gereksiz eager loading (yalnızca gereken Include — §10.5)
  - Belleğe tüm tablo çekme (`ToList()` sonra filtreleme YASAK)
  - Tek transaction içinde dış servis çağrısı (§8.5)
  - Senkron blocking call async context'te (`Task.Result`, `.Wait()` YASAK)
- Cache kullanımı veri doğruluğunu bozmayacak şekilde yapılır. Cache invalidation stratejisi açıkça tanımlanır.
- Ağır sorgular, gereksiz network çağrıları ve yüksek maliyetli döngüler code review'da yakalanır.

---

## 21. Git & CI/CD Kuralları

### 21.1 Branch Stratejisi

05 §8.4'te tanımlanan strateji:

| Branch | Amaç |
|--------|------|
| `main` | Production-ready kod. Doğrudan push YASAK. |
| `develop` | Aktif geliştirme. Feature branch'ler buraya merge olur. |
| `feature/{kısa-açıklama}` | Tek bir özellik veya düzeltme. `develop`'tan ayrılır, `develop`'a merge olur. |
| `hotfix/{kısa-açıklama}` | Production acil düzeltme. `main`'den ayrılır, `main` ve `develop`'a merge olur. |

### 21.2 Commit Mesaj Formatı

```
{tip}: {kısa açıklama}

{opsiyonel detaylı açıklama}
```

| Tip | Kullanım |
|-----|----------|
| `feat` | Yeni özellik |
| `fix` | Bug düzeltme |
| `refactor` | Davranış değiştirmeyen kod değişikliği |
| `test` | Test ekleme veya düzeltme |
| `docs` | Doküman değişikliği |
| `chore` | Build, config, dependency değişikliği |
| `migration` | DB migration |

Örnekler:
```
feat: add buyer cancel endpoint for ITEM_ESCROWED state
fix: correct gas fee calculation when fee exceeds commission threshold
migration: add RetryCount column to BlockchainTransactions
```

### 21.3 PR Kuralları

- PR tek bir amaca hizmet eder. Karışık değişiklik YASAK.
- PR açıklamasında ne yapıldığı ve neden yapıldığı belirtilir.
- CI pipeline (lint + test + build) geçmeden merge YASAK.
- Migration içeren PR'da SQL output review edilir.
- Para hesaplaması içeren PR'da §14.5 testleri geçer durumda olmalıdır.

### 21.4 CI Pipeline Sıralaması

```
1. Lint (C# + TypeScript + ESLint)
2. Build (.NET + Next.js + Sidecar'lar)
3. Unit test
4. Integration test (TestContainers)
5. Contract test
6. Migration dry-run (staging DB'ye karşı)
```

### 21.5 Notification & Guard Katmanları (T11.2)

T11.1 retrospektifi T13–T20 döneminde main CI'nin 5 task üst üste sessizce kırık kaldığını, task chat'lerinin PR açmadan bittiğini ve validator'ın kırmızı CI'yi "lokal temiz" gerekçesiyle geçtiğini tespit etti. Tek savunma katmanı yetmiyor — mekanik + şablon + süreç kurallarıyla birden fazla ağ gerekli.

**Katman A — Startup check (skill düzeyi):**
- `/task TXX` ve `/validate TXX` skill'leri Adım 0'da `gh run list --branch main --limit 3` çağırır.
- Son 3 tamamlanmış run'dan biri bile `failure/cancelled/timed_out/action_required` ise **HARD STOP** — root cause çözülmeden task'a başlanmaz.
- Rasyonelizasyonlar ("lokal temiz", "ilgisiz kırılma", "sadece docker-publish") yasak.
- Kaynak: `.claude/skills/task.md` Adım 0, `.claude/skills/validate.md` Adım 0.

**Katman B — Pre-push CI guard (hook düzeyi):**
- `scripts/git-hooks/pre-push` push öncesinde push edilen branch'in son CI run'ını kontrol eder.
- `failure/cancelled/timed_out/action_required/startup_failure` sonuçlarında push bloklanır.
- `gh` CLI yok veya auth yoksa WARN + geçer (fail-open).
- Bypass: `SKINORA_ALLOW_DIRECT_PUSH=1 SKINORA_BYPASS_REASON="..." git push` — `Docs/BYPASS_LOG.md`'ye `[ci-failure]` kayıt düşer.

**Katman C — Validator finding kuralı (süreç düzeyi):**
- `.claude/skills/validate.md` Faz 1 Adım 7a: task branch CI run'ı FAIL ise bu bir S2 Kırılma finding'idir, sessizce geçilemez.
- CI kırılması önceki task borcundan ise → BLOCKED (DEPENDENCY_MISMATCH).
- `INSTRUCTIONS.md §3.3` validator CI rasyonelizasyon yasağı paralel madde olarak kayıtlı.

**Katman D — Task chat bitiş kapısı:**
- `.claude/skills/task.md` "Bitiş Kapısı" bölümü: branch push + PR create + PR numarası TXX_REPORT + CI run başladı — dört maddenin hepsi ✓ olmadan task "yapım bitti" sayılmaz.
- Bundled PR yasağı: başka bir task'ın PR'ına gömmek yasak (tek istisna: aynı TXX'in düzeltmeleri aynı branch'e).
- "PR: Henüz oluşturulmadı" veya boş PR alanı → otomatik BLOCKED.

**Tek bir katman yeterli değildir.** Savunma katmanları birbirini tamamlar: startup check chat'i başlamadan önce yakalar; pre-push hook push öncesinde yakalar; validator task sonunda yakalar; bitiş kapısı bundled PR'ı yakalar.

---

## 22. Refactor Kuralları

- Refactor, davranış değiştirmeden yapılır.
- Davranış değişikliği gerekiyorsa bu açıkça belirtilir ve ayrı commit/PR olur.
- Büyük refactor küçük parçalara bölünür.
- Çalışan ama kritik akışları etkileyen kod, test veya koruma olmadan taşınmaz.
- Refactor PR'ı feature PR'ıyla karıştırılmaz.

---

## 23. Yasaklar

Aşağıdaki davranışlar koşulsuz olarak yasaktır:

**İş kuralı ve kapsam:**
- Dokümanda tanımlı olmayan iş kuralı uydurmak
- Sessizce kapsam genişletmek (istenmemiş özellik eklemek)
- Geçici hack'i kalıcı çözüm gibi bırakmak
- "Sonra düzeltiriz" diye bilinen riskli kod bırakmak

**Mimari:**
- Katman ihlali yapmak (§6.1)
- Modüller arası doğrudan referans eklemek (§6.3)
- Başka modülün tablosuna doğrudan sorgu yazmak (§6.3)
- Controller veya repository içine iş kuralı gömmek (§6.2)

**Güvenlik:**
- Secret'ı (API key, private key, password) koda gömmek
- Hassas veriyi loglamak
- Webhook HMAC doğrulamayı atlamak veya geçici olarak devre dışı bırakmak
- Raw SQL'de string concatenation kullanmak
- Yetkilendirme kontrolünü yalnızca frontend'e bırakmak

**Veri ve hesaplama:**
- Para hesaplamasında `float` veya `double` kullanmak (§14.1)
- `DateTime.Now` veya `DateTime.Today` kullanmak (§7.1)
- Ara hesaplamalarda yuvarlama yapmak (§14.3)
- State'i state machine dışında değiştirmek (§9.2)

**Hata yönetimi:**
- Exception yutmak (catch bloğunda sessizce devam etmek)
- Teknik hata detayını (stack trace, connection string) son kullanıcıya vermek

**Veri erişimi:**
- Sınırsız liste dönmek — pagination zorunlu (§10.5)
- Belleğe tüm tablo çekip filtrelemek
- `Task.Result` veya `.Wait()` ile senkron blocking yapmak

**Süreç:**
- Testsiz kritik davranış değiştirmek (§19.1)
- İlgisiz refactor'ı feature commit'ine eklemek (§3.1)
- Contract'ı versionsuz kırmak (§12.3)
- CI pipeline geçmeden merge yapmak (§21.3)

---

## 24. AI Çalışma Kuralları

Kod üreten model (agent) için ek kurallar:

**Kod yazmadan önce:**
- İlgili source-of-truth dokümanlarını kontrol et (§2 hiyerarşisine göre).
- Eksik veya çelişkili kural varsa not et; sessizce varsayım yapma.
- Mevcut kodu anlamadan değişiklik önerme — önce oku, sonra yaz.

**Kod yazarken:**
- Küçük diff üret. Tek seferde büyük değişiklik yerine adım adım ilerle.
- Mevcut mimariyi bozma. Katman ve modül sınırlarına sadık kal (§6).
- Gerekmedikçe yeni bağımlılık (NuGet/npm paketi) ekleme.
- Kodla birlikte kısa gerekçe ver — ne yaptığını ve neden yaptığını açıkla.
- Gereksiz stil/refactor önerisi yapma. Sadece istenen değişikliği yap.

**Özel kontroller:**
- State machine geçişi eklerken: guard + application-layer side effect (event handler) + outbox event + TransactionHistory + AuditLog (gerekiyorsa) — tümünün tam olduğunu kontrol et.
- Para hesaplaması yazarken: `decimal` kullanıldığını, ara adımda yuvarlama olmadığını, birim testinin eklendiğini doğrula.
- Timestamp kullanırken: `DateTime.UtcNow` olduğunu doğrula (§7.1).
- Sidecar contract değişikliğinde: backward compatibility kontrolü yap, version numarasını güncelle (§12).
- Webhook endpoint yazarken: HMAC doğrulama middleware'inin aktif olduğunu kontrol et (§11.3).

**Yapma:**
- Dokümanda olmayan iş kuralı uydurmak.
- Tek commit'te birden fazla amacı çözmek.
- Gerekli yerde TODO bırakılacaksa belirsiz bırakmak — açık ve izlenebilir yaz.
- Sadece kritik risk, çelişki veya blokaj varsa belirt — gereksiz uyarı üretme.

---

## 25. Review Checklist

Her kod tesliminde sorulacak sorular:

### Doküman Uyumu
- [ ] Bu değişiklik ilgili dokümanlarla (02, 05, 06, 07, 08) uyumlu mu?
- [ ] Enum değerleri, status isimleri, hata kodları 06/07 ile birebir eşleşiyor mu?

### Mimari
- [ ] Katman sınırları ihlal edilmiş mi? (§6.1)
- [ ] Modül sınırını aşan bağımlılık eklenmiş mi? (§6.3)
- [ ] İş kuralı doğru katmanda mı? (§6.2)

### State & Event
- [ ] State geçişi varsa: guard + application-layer side effect (event handler) + outbox event + TransactionHistory + AuditLog (gerekiyorsa) tam mı?
- [ ] Outbox event aynı DB transaction'da yazılıyor mu? (§9.3)
- [ ] Consumer idempotent mi? (§9.3)

### Güvenlik
- [ ] Secret koda gömülmüş mü?
- [ ] Hassas veri loglanıyor mu?
- [ ] Yetkilendirme backend'de enforce ediliyor mu?
- [ ] Webhook HMAC doğrulaması aktif mi?

### Veri & Hesaplama
- [ ] Para hesaplamasında `decimal` kullanılmış mı? (§14.1)
- [ ] Timestamp'lerde `DateTime.UtcNow` kullanılmış mı? (§7.1)
- [ ] Concurrency kontrolü (RowVersion) gerekli yerlerde var mı? (§10.4)

### Hata & Retry
- [ ] Kritik akışlarda hata yönetimi düşünülmüş mü? (§8.3)
- [ ] Retry ve idempotency gerekli yerlerde uygulanmış mı? (§8.4, §11.4)

### Contract
- [ ] Sidecar/webhook payload'u değişmişse version güncellenmiş mi? (§12)
- [ ] Breaking change varsa prosedür uygulanmış mı? (§12.4)
- [ ] Contract test güncellenmiş mi? (§12.7)

### Kalite
- [ ] Log ve observability yeterli mi? (§18)
- [ ] Test ihtiyacı karşılanmış mı? (§19)
- [ ] Kapsam sessizce genişletilmiş mi?
- [ ] İlgisiz refactor eklenmiş mi?

---

*Skinora — Coding Guidelines v0.9*
