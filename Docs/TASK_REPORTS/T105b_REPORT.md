# T105b — Kullanıcı detay backend tamamlama (S20 — wallet history + reputation breakdown)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-06-13

---

## Yapılan İşler

T105 bağımsız validator'ında doğrulanan iki AD16 boşluğunu (B2 cüzdan önceki-adresler, B3 reputation breakdown) kapatan **full-stack** follow-up. Owner-onaylı tasarım: **append-only** cüzdan adresi geçmişi (AskUserQuestion 2026-06-12, "Append-only"); `.claude/settings.json` working-tree değişikliği owner kararıyla discard edildi.

- **`WalletAddressHistory`** append-only entity (`IAppendOnly`): `long Id` (IDENTITY PK), `Guid UserId` (FK→User), `string Type` ("seller"/"buyer"), `string Address`, `DateTime? SetAt`, `DateTime CreatedAt`. `TransactionHistory`/`UserLoginLog` deseni; `User`'a `ICollection<WalletAddressHistory>` navigation.
- **EF config** `WalletAddressHistoryConfiguration`: FK→User (global `NoAction`), `IX_WalletAddressHistory_UserId` + `IX_WalletAddressHistory_UserId_Type`, `Address` MaxLength(50) / `Type` MaxLength(10), **`HasColumnType` kullanılmadı** (SQLite integration testleri kırılmasın — `TransactionHistoryConfiguration` notu). `IAppendOnly` → INSERT-only (`AppDbContext.EnforceAppendOnly`).
- **Migration** `20260612212025_T105b_AddWalletAddressHistory` (+ Designer + ModelSnapshot). `dotnet ef migrations add ... --project Skinora.Shared --startup-project Skinora.API`. Up: tablo + FK + 2 index; Down: DropTable.
- **Yazım hook'u** `WalletAddressService.UpdateWalletAsync`: bir adresin **gerçek değişiminde** (`previous` non-null && `previous != candidate`) değiştirilen (önceki) adres + `SetAt = previousChangedAt` (overwrite **öncesi** yakalanır) + `CreatedAt = now`, User mutation'ı ile **aynı `SaveChangesAsync`** içinde yazılır (atomik). İlk-set (`previous` null), no-op (`previous == candidate`), sanctions-reddi ve suspended/deactivated guard'ı **yazımdan önce döner** → history satırı üretmez.
- **AD16** `AdminUserService`: `BuildCurrentWalletEntries` → async `BuildWalletEntriesAsync` — mevcut adresler User kaydından (`current=true`), öncekiler `WalletAddressHistory`'den (`current=false`, `OrderByDescending(Id)` = en yeni önce). `"current"` saklanan bir bayrak değil, okuma anında türetilir (append-only ile uyumlu). `AdminUserDetailProfileDto`'ya reputation breakdown: `CompletedTransactionCount` + `SuccessfulTransactionRate` + `CancelRate` (`= 1 − rate`; oran null ise null) — `UserProfileDto` (07 §5.1) fraction konvansiyonu birebir (M1 closure).
- **Frontend**: `UserProfileCard` itibar breakdown `<dl>`'i (tamamlanan sayı + başarı % + iptal %, `formatPercent(rate*100, locale)`, null → `rateNone`); `admin.ts` `AdminUserDetailProfile` tipi + walletHistory yorumu; `UserDetailView` wallet `getRowKey` index-suffixed (aynı adresin current + previous olarak tekrarı = A→B→A → çakışma önlenir); i18n `adminUserDetail.profile.{completedCount,successRate,cancelRate,rateNone}` + güncellenen `wallet.historyNote` (en/tr/es/zh).
- **Doc**: 07 §9.16 (breakdown alanları + walletHistory current+previous notu + örnek), 04 §8.9.1 (breakdown metrikleri) / §8.9.3 (önceki adresler kayıt notu), 06 §3.1 (`WalletAddressHistory` tablo notu).

## Etkilenen Modüller / Dosyalar

**Backend (yeni):** `Skinora.Users/Domain/Entities/WalletAddressHistory.cs`, `Skinora.Users/Infrastructure/Persistence/WalletAddressHistoryConfiguration.cs`, `Skinora.Shared/Persistence/Migrations/20260612212025_T105b_AddWalletAddressHistory(.Designer).cs`

**Backend (değişen):** `Skinora.Users/Domain/Entities/User.cs`, `Skinora.Users/Application/Wallet/WalletAddressService.cs`, `Skinora.Admin/Application/Users/AdminUserDtos.cs`, `Skinora.Admin/Application/Users/AdminUserService.cs`, `Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`

**Frontend:** `lib/api/admin.ts`, `components/admin/UserProfileCard.tsx`, `components/admin/UserDetailView.tsx`, `i18n/messages/{en,tr,es,zh}.json`

**Test:** `Skinora.API.Tests/Integration/WalletAddressEndpointTests.cs` (+5), `Skinora.API.Tests/Integration/AdminUsersEndpointTests.cs` (+3 + helper + Reset purge)

**Doc:** `Docs/07_API_DESIGN.md`, `Docs/04_UI_SPECS.md`, `Docs/06_DATA_MODEL.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | WalletAddressHistory entity + migration; payout/refund adres değişimi tarihçeye yazılır | ✓ | Entity + config + migration `T105b_AddWalletAddressHistory`; `WalletAddressService.cs` hook; test `UpdateSellerWallet_Replacement_WritesPreviousAddressToHistory` (Type/Address/SetAt) + `..._TwoReplacements_AppendsTwoRowsOldestFirst` + refund independence |
| 2 | AD16 walletHistory[] önceki adresleri döndürür (current=false + setAt) → §8.9.3 | ✓ | `BuildWalletEntriesAsync`; test `GetUserDetail_WalletHistory_IncludesCurrentAndPreviousAddresses` (current=true önce, current=false + setAt) |
| 3 | Reputation breakdown DTO: tamamlanan sayı + başarı % + iptal % (04 §7.4.2 deseni) | ✓ | `AdminUserDetailProfileDto` 3 alan; `CancelRate = 1 − rate`; test `GetUserDetail_ReputationBreakdown_MirrorsDenormalizedCounters` (8 / 0.75 / 0.25) + `..._NullRate_YieldsNullCancelRate` |
| 4 | AD16 profile breakdown'u expose eder; FE UserProfileCard render eder | ✓ | `GetDetailAsync` profile alanları; `UserProfileCard.tsx` breakdown `<dl>`; `next build` ✓; i18n 4 dil |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Integration (API.Tests) | ✓ 475/475 | `dotnet test Skinora.API.Tests` (SQLite) — yeni: WalletAddress +5, AdminUsers +3; regresyon yok |
| Unit (Users) | ✓ 16/16 | `dotnet test Skinora.Users.Tests` |
| Unit (Admin) | ✓ 20/20 | `dotnet test Skinora.Admin.Tests` |
| Backend build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| dotnet format | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes` exit 0 |
| Frontend tsc | ✓ 0 | `npx tsc --noEmit` |
| Frontend eslint | ✓ 0 | T105b dosyaları |
| Frontend prettier | ✓ clean | `--check --end-of-line auto` (changed files + 4 i18n) |
| Frontend next build | ✓ | `/admin/users/[steamId]` ƒ Dynamic |
| i18n parity | ✓ 1063×4 | 0 missing / 0 extra; breakdown anahtarları 4 dilde |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız doğrulama chat'i bekliyor (yapan ≠ denetleyen) |
| Yapım-içi adversarial review | 6-boyut / refute-default (AC-conformance, append-only integrity, wallet hook, reputation breakdown, contract/migration drift, security/regression) → **0 bulgu** |
| Bulgu sayısı | 0 |

## Altyapı Değişiklikleri

- **Migration:** Var — `T105b_AddWalletAddressHistory` (yeni `WalletAddressHistory` tablosu + FK + 2 index). Fresh DB + mevcut DB'ye uygulanabilir; veri seed/backfill yok.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni bağımlılık:** Yok.

## Commit & PR

- Branch: `task/T105b-user-detail-backend`
- Commit: `d00b79f` — T105b implementation (kod + migration + test + doc + i18n)
- Rapor + status + memory: ayrı commit
- PR: #161
- CI: ⏳ Claude izliyor (concluded + success beklenir)

## Known Limitations / Follow-up

- **Backfill yok:** Önceki adresler yalnızca özellik etkinleştikten sonraki değişimler için kaydedilir; T105b öncesi değişiklikler kurtarılamaz (geçmişte hiç saklanmamıştı). Mevcut adres User kaydından her zaman gösterilir.
- Reputation breakdown denormalize `User.SuccessfulTransactionRate` / `CompletedTransactionCount` üzerinden okunur (skorun girdileri); AD16 yeniden hesaplama yapmaz — `ReputationAggregator`'ın hesapladığı (CANCELLED_ADMIN hariç + wash-trading filtresi, 02 §13/§14.1) değeri devralır. Stats bloğundaki `completedTransactions` (canlı agregasyon) profil breakdown'ındaki `completedTransactionCount` (denormalize sayaç) ile çoğu kullanıcıda eşit; biri aktiviteyi, diğeri itibar skorunu açıklar.
- FE permission guard yok (backend `VIEW_USERS` enforce — T99 K5 / T104 K1 emsali).

## Notlar

- **Working tree (Adım -1):** `.claude/settings.json` (T105 frontend-lint izin-allowlist eklentisi, harness kaynaklı) → owner kararı **discard** (`git restore`). Branch temiz main `d89cd84` üzerinden açıldı.
- **Main CI startup (Adım 0):** son 3 run success (T105 #160 CI + Docker Publish + T104 #159).
- **Dış varsayımlar (Adım 4):** (DOĞRULANDI) tek cüzdan-değişim yolu = `WalletAddressService.UpdateWalletAsync` (grep ile teyit, admin reset yolu yok); T34 değişim akışı mevcut, T105b yalnız history INSERT ekler; çift adres modeli (payout/Seller + refund/Buyer) gerçek → discriminator gerekli; `cancelRate` = 0..1 kesir (M1 closure); `IAppendOnly` UPDATE'i reddeder → "current flip" deseni yasak → append-only seçildi; migration assembly = Skinora.Shared; entity PK = `long` IDENTITY. (KONTROL-uygulamada) Address `HasColumnType` yok (SQLite); breakdown denormalize rate'ten gelir (recompute yok); i18n 4-dil parity.
- **Append-only & test temizliği:** `AdminUsersEndpointTests.Reset()` `WalletAddressHistory`'yi **raw SQL** (`DELETE FROM WalletAddressHistory`) ile temizler — tracked `RemoveRange` `EnforceAppendOnly`'yi tetikler (Deleted state). `WalletAddressEndpointTests` zaten `EnsureDeleted/EnsureCreated` kullandığı için etkilenmez.
- **Bağımsız doğrulama** ayrı chat'te yapılacak (yapım raporunu görmeden) — yapım chat'inde başlatılmaz.
