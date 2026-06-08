# T105 — Admin Kullanıcı Detay (S20)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız validator bekliyor) | **Tarih:** 2026-06-08

---

## Yapılan İşler

S20 Admin Kullanıcı Detay ekranı **full-stack** olarak tamamlandı. Plan T105'i "salt frontend, test yok" varsayıyordu; ancak inceleme sırasında **AD16 `GetDetailAsync`'in baştan sona T39'dan kalma bir contract-only placeholder olduğu** ortaya çıktı (İstatistikler kısmen sıfır, Flag/Dispute/Counterparty listeleri hardcoded `[]`, ReputationScore `null`). T54/T58/T63 backing service'leri F3/F4'te tamamlanmış ama hiçbiri geri dönüp `AdminUserService`'i bağlamamıştı. Proje sahibi iki AskUserQuestion ile (1) badge boşluğu → "C full-stack", (2) genel sınır → "1) Tam wire-up" kararını verdi.

### Backend (AD16 wire-up)
- **Yeni `IAdminUserActivityProvider`** (`Skinora.Admin`) + impl **`AdminUserActivityProvider`** (`Skinora.API.Services`, composition root). Cross-module agregasyon (Transactions/Fraud/Disputes) — `AdminTransactionQueryService` (AD7) ile aynı "composition-root, çünkü Skinora.Admin bu modülleri referans edemez" gerekçesi.
  - **İstatistikler:** `Transaction` üzerinden gerçek toplam/tamamlanan/iptal(4 CANCELLED_*)/flag(FLAGGED) sayıları + tamamlanan işlem hacmi (invariant 2-ondalık string, yoksa null) + son işlem tarihi (max CreatedAt).
  - **Badge sinyalleri:** `ActiveTransactionCount` = terminal-olmayan işlem sayısı (5 terminal hariç; FLAGGED aktif; `IsOnHold` dahil — AD1 dashboard / AD19d "aktif" tanımıyla birebir) + `HasTransactionOnHold` = herhangi bir işlemde EMERGENCY_HOLD.
  - **FrequentCounterparties:** karşı taraf = diğer taraf (alıcısı olmayan satırlar atlanır), paylaşılan işlem sayısına göre top-10, isimler tek dictionary lookup ile çözülür (wash-trading sinyali, §8.9.7).
  - **FlagHistory:** `FraudFlag.UserId` ile (account+transaction level; `transactionId` ACCOUNT_LEVEL'de null).
  - **DisputeHistory:** Dispute→Transaction join ile (kullanıcı alıcı **veya** satıcı taraf; sadece OpenedByUserId değil — satıcı tarafı da kapsanır).
- **`AdminUserService.GetDetailAsync`** provider'ı + `IReputationScoreCalculator`'ı inject edip AD16 DTO'sunu birleştirir; reputation on-demand hesaplanır (06 §3.1).
- **DTO değişikliği:** `AdminUserDetailProfileDto` → `ActiveTransactionCount` + `HasTransactionOnHold` eklendi; `AdminUserFlagEntryDto.TransactionId` → `Guid?` (account-level).
- **DI:** `IAdminUserActivityProvider` → `TransactionsModule.cs`'de kaydedildi.
- **Doc:** 07 §9.16 — profile badge alanları + `isSuspended` grubu + `accountStatus` SUSPENDED + transactionId-null/totalVolume/counterparty notları.

### Frontend (S20)
- **Yeni route** `app/[locale]/admin/users/[steamId]/page.tsx` (T99/T101 forward-link hedefi; T101 K2'de 404'tü, artık karşılanıyor).
- **`lib/api/admin.ts`:** AD16 (`getAdminUserDetail` + tipler) + AD16b (`getAdminUserTransactions`, AD6 list shape reuse) katmanı.
- **Hook'lar:** `useAdminUserDetail` (30s staleTime), `useAdminUserTransactions` (keepPreviousData, sayfalı).
- **Bileşenler:** `UserDetailView` (orchestrator) + `UserProfileCard` (avatar/kimlik/§8.9.1 koşullu badge'ler/reputation) + `UserStatsCard` + cüzdan/flag/dispute/counterparty `ResponsiveTable`'ları + **`TransactionListTable` reuse** (T101) §8.9.4 işlem tablosu için.
- **i18n:** `adminUserDetail` namespace, **67 leaf × 4 locale** (en/tr/es/zh, parity doğrulandı).

## Etkilenen Modüller / Dosyalar

**Backend (yeni):** `IAdminUserActivityProvider.cs`, `AdminUserActivityProvider.cs`
**Backend (değişen):** `AdminUserDtos.cs`, `AdminUserService.cs`, `TransactionsModule.cs`, `AdminUsersEndpointTests.cs`
**Doc:** `07_API_DESIGN.md` §9.16
**Frontend (yeni):** `admin/users/[steamId]/page.tsx`, `UserDetailView.tsx`, `UserProfileCard.tsx`, `UserStatsCard.tsx`, `useAdminUserDetail.ts`, `useAdminUserTransactions.ts`
**Frontend (değişen):** `lib/api/admin.ts`, `components/admin/index.ts`, `i18n/messages/{en,tr,es,zh}.json`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Profil bilgileri: avatar, ad, Steam ID, hesap yaşı, durum badge'leri | ✓ | `UserProfileCard` — avatar/displayName/steamId/accountAge + base 4-durum badge + §8.9.1 koşullu badge'ler (Aktif İşlem Var / Hold Altında); profile DTO gerçek veri |
| 2 | İstatistikler kartı: toplam/başarı/iptal/flag, hacim, son işlem | ✓ | `UserStatsCard` + `AdminUserActivityProvider` gerçek agregasyon; test `GetUserDetail_WithActivity` (6/2/1/1, volume 204.00, lastTransaction) |
| 3 | Cüzdan adresi geçmişi (mevcut + önceki, tarihlerle) | ~ Kısmi | Mevcut adresler render + tarih; **önceki adresler defer** (history entity yok — açık K-not + UI disclosure `wallet.historyNote`) |
| 4 | Alıcı-satıcı ilişkileri tablosu | ✓ | `FrequentCounterparties` provider + counterparty tablosu; test `GetUserDetail_FrequentCounterparties_RankedBySharedCount` (3 vs 1 sıralama) |
| 5 | `GET /admin/users/:steamId` çağrısı | ✓ | `getAdminUserDetail` (AD16) + AD16b işlem tablosu; route canlı (`next build` ƒ Dynamic) |

**Doğrulama kontrol listesi — 04 §8.9 tüm bileşenler:** Profil ✓ / İstatistikler ✓ / Cüzdan geçmişi ~ (mevcut) / İşlem tablosu ✓ (AD16b reuse) / Flag geçmişi ✓ / Dispute geçmişi ✓ / Alıcı-satıcı ilişkileri ✓.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Release build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Integration (AdminUsers) | ✓ 19/19 | SQLite; 5 yeni AD16 testi (stats/badge, flag account+txn level, dispute seller-side join, counterparty ranking, reputation 4.0) |
| Integration (tüm Admin) | ✓ 127/127 | `--filter ~Admin` (regresyon yok) |
| dotnet format | ✓ Δ=0 | `--verify-no-changes` exit 0 |
| Frontend tsc | ✓ 0 | `tsc --noEmit` |
| Frontend eslint | ✓ 0 | T105 dosyaları |
| Frontend prettier | ✓ clean | `--check --end-of-line auto` (Windows CRLF working-copy artefaktı; git LF saklar) |
| Frontend next build | ✓ | `/[locale]/admin/users/[steamId]` ƒ Dynamic |
| i18n parity | ✓ 67×4 | en/tr/es/zh 0 missing/extra |

> Not: SQL Server'a özel Fraud/Transactions Testcontainers testleri lokalde (Docker yok, T11.3) çalışmaz → CI'da koşar.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: **Yok** (computed agregasyon; yeni alan/tablo yok).
- Config/env: Yok.
- Docker: Yok.
- Yeni dependency: **Yok** (backend + frontend).

## Commit & PR
- Branch: `task/T105-admin-user-detail`
- Commit: `89871e5` — kod + i18n + doc (rapor/status/memory ayrı commit)
- PR: #160
- CI: ⏳ izleniyor

## Known Limitations / Follow-up
- **K1 — Cüzdan önceki-adres geçmişi:** Veri modelinde `WalletAddressHistory` entity'si yok; yalnız mevcut adresler + değişiklik tarihi `User`'da. UI disclosure + forward-not (gelecekte schema'ya history tablosu eklenirse dolar).
- **K2 — Reputation "detaylı breakdown":** AD16 yalnız tek skor (`reputationScore`) verir; §8.9.1 "detaylı breakdown" tek skor olarak gösterilir (breakdown DTO'da yok).
- **K3 — FE permission guard yok:** Backend `VIEW_USERS` policy ile korunur (T99 K5 / T103 K5 / T104 K1 emsali).
- **K4 — DELETED hesap durumu:** Soft-delete query filter anonimleştirilmiş kullanıcıları zaten dışlar; `DELETED` badge forward-compat placeholder (AD16 normalde ACTIVE/SUSPENDED/DEACTIVATED döndürür).
- **K5 — `/admin/users` (index) stub:** T105 yalnız detay ekranı; users-liste ekranı plan kapsamında değil.

## Notlar
- **Adım -1 (working tree):** Session başı temiz.
- **Adım 0 (main CI startup):** Son 3 main run `success` (`27152338399`/`27152338402` T104 #159 + `27101235527` T103 #158).
- **Dış varsayım (Adım 4 — KIRIK):** Plan AD16'nın downstream task'larca (T54/T58/T63) doldurulduğunu varsayıyordu; gerçekte `GetDetailAsync` T39 placeholder olarak kaldı (Stats kısmi, Flag/Dispute/Counterparty `[]`, Reputation null). Proje sahibine sunuldu → "Tam wire-up" onaylandı (PLAN_CORRECTION niteliğinde, ayrı task'a bölünmedi).
- **Tasarım kararları (AskUserQuestion, 2026-06-08):** (1) §8.9.1 badge boşluğu → C full-stack; (2) T105 sınırı → 1) Tam wire-up (cüzdan geçmişi hariç hepsi bağlandı).
- **Mimari:** `AdminUserActivityProvider` composition-root'ta (Skinora.Admin → Transactions/Fraud/Disputes referans edemez); `IReputationScoreCalculator` Skinora.Users'tan doğrudan inject (Skinora.Admin referans ediyor).
