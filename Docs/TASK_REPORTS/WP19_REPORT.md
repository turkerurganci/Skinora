# WP19 — Happy-path bildirim producer'ları

**Faz:** Pre-F6 (F6 öncesi MVP borç kapatma) | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-21

---

## Bağlam — neden bu iş

T107 (E2E happy-path) için yapılan keşif, happy-path **kullanıcı bildirimlerinin hiç üretilmediğini** ortaya çıkardı: `BUYER_ACCEPTED`, `ITEM_ESCROWED`, `PAYMENT_RECEIVED`, `TRADE_OFFER_SENT_TO_BUYER`, `TRANSACTION_COMPLETED`, `SELLER_PAYMENT_SENT`, `TRANSACTION_INVITE` tipleri yalnızca enum + `EmailCategoryMap` + testlerde geçiyor; hiçbir prod kodu bu tipler için `NotificationRequest` oluşturmuyordu. Yalnızca WP9 realtime `TransactionStatusChanged` badge push'u çalışıyordu. Şablonlar 4 dilde (en/tr/es/zh) zaten mevcuttu — yani niyet vardı, yalnızca **producer wiring** eksikti (WP1'in çağıransız payout'u ile birebir aynı desen). 03 §2–§3'ün her adımındaki "bildirim gider" ifadeleri ve T107 AC2 ("tüm bildirimler doğru tetikleniyor") bu boşluk yüzünden karşılanamıyordu.

Owner kararı (AskUserQuestion 2026-06-21): **Önce ayrı backend task'ı ile producer'ları bağla, sonra T107.** Bu WP, pre-F6 serisinin devamı (WP19) olarak ele alındı.

## Owner kararları (AskUserQuestion 2026-06-21)

1. **ITEM_DELIVERED:** Bastır + 03'ü hizala. ITEM_DELIVERED için bildirim tipi eklenmez (02 §18.2 / 06 §2.13 bildirim kataloğu yetkili); teslim anı realtime badge ile gösterilir, inbox bildirimi COMPLETED'da gelir. 03 §3.5/§12.2 hizalandı.
2. **COMPLETED:** Satıcıya 2 (SELLER_PAYMENT_SENT + TRANSACTION_COMPLETED), alıcıya 1 (TRANSACTION_COMPLETED) → 3 satır (03 §2.4 adım 5+6 + 06 §2.13 "her ikisi").

## Mimari

Mevcut domain event'leri **yeniden kullanıldı**; Notifications assembly'sine 5 yeni `NotificationConsumerBase<TEvent>` türevi consumer eklendi (MediatR scan ile otomatik keşfedilir — `OutboxModule.cs:84-89`). Event'ler **zenginleştirilmedi** (WP9 realtime consumer'larına/testlerine yayılmamak için), yeni event **eklenmedi**, **migration YOK**, enum/şablon değişikliği **YOK**. Event'te bulunmayan alıcı/parametreler için consumer içinde küçük salt-okunur `AppDbContext` re-query yapılır — `NotificationDispatcher`'ın aynı assembly'de zaten kullandığı desen.

## Yapılan İşler

| Consumer | Tüketilen event | Üretilen bildirim → alıcı | Re-query |
|---|---|---|---|
| `TransactionInviteNotificationConsumer` | `TransactionCreatedEvent` | TRANSACTION_INVITE → alıcı (yalnız kayıtlı; `BuyerId` null → no-op) | Yok (event-only) |
| `BuyerAcceptedNotificationConsumer` | `BuyerAcceptedEvent` | BUYER_ACCEPTED → satıcı | `User` (BuyerName) |
| `PaymentReceivedNotificationConsumer` | `PaymentReceivedEvent` | PAYMENT_RECEIVED → satıcı | `Transaction.SellerId` |
| `EscrowedAndTradeOfferNotificationConsumer` | `TransactionStatusChangedEvent` | ITEM_ESCROWED + TRADE_OFFER_SENT_TO_BUYER → alıcı (ToStatus'a göre dallanır; diğer ToStatus → no-op) | `Transaction` (+ `PaymentAddress`) |
| `PayoutCompletedNotificationConsumer` | `PayoutCompletedEvent` | SELLER_PAYMENT_SENT → satıcı + TRANSACTION_COMPLETED → satıcı + TRANSACTION_COMPLETED → alıcı (kayıtlıysa) | `Transaction` (SellerId/BuyerId/ItemName) |

Doc hizalama: 03 §3.5 adım 10 + §12.2 satır "Item teslim edildi" → ITEM_DELIVERED inbox bildirimi yoktur (realtime durum güncellemesi; inbox COMPLETED'da), 02 §18.2 / 06 §2.13 atıflı.

## Etkilenen Modüller / Dosyalar

**Yeni (production):**
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/TransactionInviteNotificationConsumer.cs`
- `.../BuyerAcceptedNotificationConsumer.cs`
- `.../PaymentReceivedNotificationConsumer.cs`
- `.../EscrowedAndTradeOfferNotificationConsumer.cs`
- `.../PayoutCompletedNotificationConsumer.cs`

**Yeni (test):**
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionInviteNotificationConsumerTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Integration/HappyPathNotificationConsumerTests.cs`
- `backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationDispatcher.cs`
- `backend/tests/Skinora.Notifications.Tests/TestSupport/InMemoryProcessedEventStore.cs`

**Değişen (doc):**
- `Docs/03_USER_FLOWS.md` (§3.5 adım 10, §12.2 — ITEM_DELIVERED hizalama)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Happy-path geçişlerinde doğru kullanıcıya doğru bildirim üretilir | ✓ | 5 consumer; matris yukarıda; 03 §12.1/§12.2 ile eşleşir |
| 2 | Mevcut producer pattern + i18n şablonları kullanılır (yeni şablon/migration yok) | ✓ | `NotificationConsumerBase` türevleri; 4-dil resx mevcut; migration yok |
| 3 | Realtime push'tan ayrı, gerçek inbox/email bildirimi oluşur | ✓ | `INotificationDispatcher.DispatchAsync` → `Notification` satırı + delivery |
| 4 | OPEN_LINK/kayıtsız alıcıda invite no-op; idempotent | ✓ | `TransactionInvite` null-buyer testi; base idempotency testleri |
| 5 | COMPLETED: satıcı 2 + alıcı 1 | ✓ | `PayoutCompleted_NotifiesSellerTwice_AndBuyerOnce` testi |
| 6 | Regresyon yok (mevcut event tüketicileri/akışları bozulmadı) | ✓ | Yeni consumer'lar yalnız handler ekler; endpoint testleri outbox'ı pump etmez; `OutboxTests` izole test event'leri kullanır |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Notifications) | ✓ 106/106 | `dotnet test Skinora.Notifications.Tests --filter "Category!=Integration"` (+3 TransactionInvite) |
| Unit (solution) | ✓ 0 fail | `dotnet test Skinora.sln --filter "Category!=Integration"` — Transactions 782 / API 528 / Platform 133 / Notifications 106 / Auth 83 / Steam 82 / Fraud 79 / Disputes 37 / Users 22 (regresyon yok) |
| Integration (yeni) | ✓ 8/8 | `HappyPathNotificationConsumerTests` — validator lokal gerçek SQL Server (TestContainers) + CI Integration job (run 27907445321) success |
| Format gate | ✓ | `dotnet format Skinora.sln --verify-no-changes` exit 0 |
| Build | ✓ 0E | `dotnet build Skinora.sln -c Debug` exit 0 |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-21, rapor görülmeden kendi verdict'i) |
| Yapım-içi self-check | ✓ AC 1–6 |
| Düzeltme gerekli mi | Hayır — 0 bloke-edici bulgu |

### Bağımsız Validator Sonucu (2026-06-21)

**Verdict: ✓ PASS — AC 1–6 hepsi ✓, 0 bloke-edici bulgu.**

**Kapılar:**
- Adım -1 working tree temiz · Adım 0 main son-3 run success (`27904430722`/`27904430715`/`27903815964`) · Adım 0b repo memory WP19 satırı mevcut.
- Adım 8a task CI HEAD `0adef87` run [27907445321](https://github.com/turkerurganci/Skinora/actions/runs/27907445321) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate; `0. Guard` + `3b. JS test` skipped).

**Validator lokal yeniden çalıştırma:**
- `dotnet build Skinora.sln -c Release` → **0 Warning / 0 Error**.
- Notifications unit (`Category!=Integration&Category!=Contract`) → **106/106**.
- `HappyPathNotificationConsumerTests` gerçek SQL Server (TestContainers, Docker UP) → **8/8**.
- `dotnet format Skinora.sln --verify-no-changes` → exit 0.

**Bağımsız spec/kod teyidi:**
- **Alıcı eşlemesi 06 §2.13 kataloğuyla birebir:** TRANSACTION_INVITE→alıcı, BUYER_ACCEPTED→satıcı, ITEM_ESCROWED→alıcı, PAYMENT_RECEIVED→satıcı, TRADE_OFFER_SENT_TO_BUYER→alıcı, TRANSACTION_COMPLETED→her ikisi, SELLER_PAYMENT_SENT→satıcı.
- **5 event'in 5'i de gerçek prod publisher'a sahip** (ölü consumer yok): `TransactionCreationService:281` · `TransactionAcceptanceService:191` · `AmountValidationService:511` · `TradeOfferDispatchJob:300`+`SteamWebhookHandler:609` · `OutgoingTransferConfirmationJob:115`.
- **Şablon placeholder'ları consumer parametreleriyle birebir** (NotificationTemplates.resx: `{ItemName}/{Amount}`, `{BuyerName}`, `{Amount}/{PaymentAddress}`, `{Amount}`, parametre-yok, `{ItemName}`, `{Amount}`).
- **COMPLETED 2+1** (owner kararı #3): SELLER_PAYMENT_SENT + TRANSACTION_COMPLETED satıcı + TRANSACTION_COMPLETED alıcı — `PayoutCompleted_NotifiesSellerTwice_AndBuyerOnce` testi doğrular.
- **ITEM_DELIVERED bastırma** (owner kararı #2): notification tipi yok, consumer fire etmez, 03 §3.5/§12.2 hizalandı.
- **Sıfır mevcut prod kaynak değişikliği** (yalnız 5 yeni consumer + testler; mevcut event/handler/şema dokunulmadı) → regresyon yüzeyi minimal. MediatR auto-scan (`OutboxModule.cs` Notifications assembly) consumer'ları kayıt eder; idempotency `IProcessedEventStore` ile consumer-başına.
- **Güvenlik:** secret yok · auth değişmedi (internal event tüketimi) · yeni kullanıcı girdisi yok (parametreler iç veriden) · yeni dış bağımlılık yok · re-query'ler salt-okunur.

**Non-blocking gözlemler:** (1) `EscrowedAndTradeOfferNotificationConsumer` ilgisiz `ToStatus` geçişlerinde de event'i processed işaretler (zararsız, EventId benzersiz). (2) ITEM_ESCROWED'da PaymentAddress yoksa Amount→TotalAmount + adres→boş fallback (savunmacı; pratikte ITEM_ESCROWED'a varıldığında adres her zaman tahsisli — WP4b). (3) Uçtan-uca MediatR/DI dispatch wiring'i WP19 testlerinde doğrudan assert edilmiyor (consumer'lar doğrudan örnekleniyor); ~15 canlı consumer ile birebir aynı desen + assembly scan + T107 E2E bunu gerçek bildirimlerle kapatacak. (4) Rapor "Değişen (doc)" listesi yalnız 03'ü anar; diff ayrıca standart status/plan/memory dosyalarını günceller (task bitiş kapısı gereği — içerik dokümanı değil).

**Yapım raporu karşılaştırması:** Tam uyumlu — bağımsız verdict yapım self-check'i (AC 1–6 ✓) ile örtüşür; uyuşmazlık yok.

## Altyapı Değişiklikleri

- Migration: **Yok** (enum/şablon/şema değişmedi; tüm tipler + resx + EmailCategoryMap zaten mevcuttu)
- Config/env: Yok
- Docker: Yok
- Yeni paket: Yok
- DI/registration: Yok (consumer'lar MediatR scan ile otomatik keşfedilir)

## Mini Güvenlik Kontrolü

- Secret sızıntısı: Yok
- Auth/authorization: Etkilenmez (consumer'lar internal event tüketir)
- Input validation: Yeni kullanıcı girdisi yok (parametreler iç verilerden türetilir)
- Yeni dış bağımlılık: Yok

## Known Limitations / Follow-up

- ITEM_DELIVERED için ayrı inbox/email bildirimi yok (owner kararı — realtime durum güncellemesi + COMPLETED inbox bildirimi kapsar).
- Bildirim şablonları "USDT" literalini sabit içerir (gerçek stablecoin USDT/USDC ayrımı yapmaz) — mevcut şablon basitleştirmesi, WP19 kapsamı dışı (07/WP17 doc katmanı).
- `{BuyerName}` boş display-name'de "Buyer" fallback'ine düşer (kayıtlı kullanıcıda neredeyse imkânsız edge).

## Commit & PR

- Branch: `task/WP19-happy-path-notifications`
- Commit: `1cbf21e` — WP19: Happy-path bildirim producer'ları (T107 keşfi)
- PR: [#195](https://github.com/turkerurganci/Skinora/pull/195)
- CI: ✓ PASS — run [27907174750](https://github.com/turkerurganci/Skinora/actions/runs/27907174750) (`bf8e2a5`) tüm job success (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate; `3b. JS test` skipped — FE'ye dokunulmadı)

## Notlar

- **Working tree (task.md Adım -1):** temiz (T107 keşfi sonrası boş T107 branch'i silindi).
- **Main CI startup (Adım 0):** son 3 run success (`27904430722`/`27904430715`/`27903815964`).
- **Dış varsayımlar (Adım 4):** Şablonların 4 dilde mevcudiyeti — `NotificationTemplates.{resx,tr,es,zh}` grep ile doğrulandı (tüm 7 tip + tokenlar mevcut). Producer pattern + MediatR auto-scan — `OutboxModule.cs:84-89` + mevcut consumer'lar ile doğrulandı. Migration gereksizliği — enum/şablon/şema değişmediği için doğrulandı.
- T107 bu WP merge + doğrulama sonrası devam edecek (E2E artık AC2'yi gerçek bildirimlerle doğrulayabilir).
