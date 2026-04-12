# T21 — TradeOffer, PlatformSteamBot Entity'leri

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-12

---

## Yapilan Isler
- TradeOffer entity (06 §3.9): 11 field, BaseEntity + IAuditableEntity
- PlatformSteamBot entity (06 §3.10): 10 field, BaseEntity + ISoftDeletable + IAuditableEntity
- TradeOfferConfiguration: 7 state-dependent CHECK constraint, filtered unique SteamTradeOfferId, 2 perf index, 2 FK
- PlatformSteamBotConfiguration: unique SteamId, soft delete query filter, Transaction.EscrowBotId FK (cross-module)
- SteamModuleDbRegistration + Program.cs registration
- 21 integration test (TestContainers SQL Server)

## Etkilenen Moduller / Dosyalar
- `backend/src/Modules/Skinora.Steam/Domain/Entities/TradeOffer.cs` (yeni)
- `backend/src/Modules/Skinora.Steam/Domain/Entities/PlatformSteamBot.cs` (yeni)
- `backend/src/Modules/Skinora.Steam/Infrastructure/Persistence/TradeOfferConfiguration.cs` (yeni)
- `backend/src/Modules/Skinora.Steam/Infrastructure/Persistence/PlatformSteamBotConfiguration.cs` (yeni)
- `backend/src/Modules/Skinora.Steam/Infrastructure/Persistence/SteamModuleDbRegistration.cs` (yeni)
- `backend/src/Modules/Skinora.Steam/Skinora.Steam.csproj` (Transactions referansi eklendi)
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionConfiguration.cs` (EscrowBotId FK notu guncellendi)
- `backend/src/Skinora.API/Program.cs` (SteamModuleDbRegistration eklendi)
- `backend/tests/Skinora.Steam.Tests/Integration/TradeOfferSteamBotEntityTests.cs` (yeni)

## Kabul Kriterleri Kontrolu
| # | Kriter | Sonuc | Kanit |
|---|---|---|---|
| 1 | TradeOffer entity: 06 §3.9 (SteamTradeOfferId, Direction, Status, SentAt, RespondedAt, vb.) | ✓ | TradeOffer.cs — 11 field birebir |
| 2 | PlatformSteamBot entity: 06 §3.10 (SteamId, BotName→DisplayName, Status, ActiveEscrowCount, DailyTradeOfferCount, vb.) | ✓ | PlatformSteamBot.cs — 10 field birebir |
| 3 | Unique: TradeOffer.SteamTradeOfferId (filtered), PlatformSteamBot.SteamId | ✓ | Config'de HasIndex + IsUnique; test: SteamTradeOfferId_Unique_PreventsDuplicates, SteamId_Unique |
| 4 | Check constraint'ler: SENT→SentAt NOT NULL, ACCEPTED→SentAt+RespondedAt NOT NULL, vb. | ✓ | 7 CK_ constraint; 8 violation + 5 satisfied test |
| 5 | FK'ler: TradeOffer→Transaction, TradeOffer→PlatformSteamBot; Transaction→PlatformSteamBot | ✓ | Config FK; 3 FK enforcement test |
| 6 | Index'ler: TradeOffer.TransactionId, PlatformSteamBotId | ✓ | Config IX_ index tanimlari |
| 7 | Soft delete: PlatformSteamBot | ✓ | HasQueryFilter + test: SoftDelete_FilteredByDefault |
| 8 | Denormalized: ActiveEscrowCount, DailyTradeOfferCount | ✓ | Default 0, Update test PASS |

## Test Sonuclari
| Tur | Sonuc | Detay |
|---|---|---|
| Integration | ✓ 21/21 passed | `dotnet test --filter "Category=Integration"` — CRUD, CHECK, unique, FK, soft delete |
| Regresyon | ✓ 99/99 passed | Mevcut API.Tests regresyon yok |

## Dogrulama
| Alan | Sonuc |
|---|---|
| Dogrulama durumu | Ayri chat'te yapilacak |
| Bulgu sayisi | — |
| Duzeltme gerekli mi | — |

## Altyapi Degisiklikleri
- Migration: Yok (T28'de toplu)
- Config/env degisikligi: Yok
- Docker degisikligi: Yok

## Commit & PR
- Branch: `task/T21-trade-offer-steam-bot-entities`
- Commit: `0b8428e` — T21: TradeOffer, PlatformSteamBot entity'leri
- PR: #21
- CI: Bekleniyor

## Known Limitations / Follow-up
- Transaction.EscrowBotId FK cross-module: PlatformSteamBotConfiguration'dan configure edildi (circular reference onlemi). TransactionConfiguration'da aciklayici NOT comment.
- FAILED status icin SentAt constraint yok (06 §3.9 spesifikasyonu: pre-send failure valid) — test ile kanitlandi.

## Notlar
- Working tree: 1 dosya (.claude/settings.local.json) → commit (PR #19)
- Startup check: main CI run 24312090370 ✓ success (Hangfire fix sonrasi)
- Dis varsayim: yok (tum bagimliliklar internal)
- Hangfire exit code 1 fix'i ayri PR #20 olarak merge edildi (T21 oncesi blocker)
