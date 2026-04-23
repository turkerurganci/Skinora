# T34 — Cüzdan adresi yönetimi

**Faz:** F2 | **Durum:** ✓ Tamamlandı | **Doğrulama:** ✓ PASS (bağımsız validator, 2026-04-23) | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **`PUT /api/v1/users/me/wallet/seller` (U3, 07 §5.3):** `[Authorize(Authenticated)]` + `[RateLimit("user-write")]`. Payload `{ walletAddress }` → merkezi doğrulama pipeline (02 §12.3: TRC-20 format + sanctions) → `User.DefaultPayoutAddress` + `PayoutAddressChangedAt` güncellenir. Response: `{ walletAddress, updatedAt, activeTransactionsUsingOldAddress }` — snapshot prensibi (02 §12.3).
- **`PUT /api/v1/users/me/wallet/refund` (U4, 07 §5.4):** U3 ile aynı yapı, `DefaultRefundAddress` + `RefundAddressChangedAt`. Tek fark rol bazlı — `WalletRole.Buyer`.
- **`ITrc20AddressValidator` + `Trc20AddressValidator`:** Format-level doğrulama — `T` prefix + 34 karakter + Base58 alfabe (`0 O I l` hariç). Tron spec'e uygun; Base58Check checksum (full decode) MVP dışı — 07 §5.3 format-only diyor.
- **`IWalletSanctionsCheck` + `NoMatchWalletSanctionsCheck`:** Adres-anahtarlı sanctions hook. Steam-ID-anahtarlı mevcut `ISanctionsCheck` (T29) login pipeline için kaldı; data contract farklı olduğu için ayrı bir interface. Gerçek liste entegrasyonu T82 → stub şu an `NoMatch` döndürür.
- **`IActiveTransactionCounter` + `ActiveTransactionCounter`:** Skinora.Users interface'i, Skinora.Transactions uygulaması. Bağımlılık yönü `Transactions → Users` olduğu için (ters çevirmek cycle yaratır) counter tanımı Users tarafında, EF sorgusu Transactions tarafında. Non-terminal statü filtresi: `COMPLETED` + 4 `CANCELLED_*` hariç hepsi.
- **`IWalletAddressService` + `WalletAddressService`:** Orchestration — format check → user load (`!IsDeactivated` filter + tracking query) → re-auth guard (mevcut adres varsa reAuthValidated=false → RE_AUTH_REQUIRED) → sanctions → active tx count (sadece değişiklik durumunda) → persist. `TimeProvider.GetUtcNow()` ile `ChangedAt` damgalanır. Hata kodları: `WalletUpdateStatus` enum (UserNotFound/InvalidAddress/SanctionsMatch/ReAuthRequired) + controller mapping.
- **Re-auth token akışı (controller):** `X-ReAuth-Token` header varsa `IReAuthTokenValidator.ValidateAsync` consume eder, payload null ise veya `payload.UserId != currentUserId` ise 403 `RE_AUTH_TOKEN_INVALID`. Header yoksa `reAuthValidated=false` service'e iletilir; service mevcut adres varsa 403 `RE_AUTH_REQUIRED`. Token single-use — 2. deneme `null` döner (T31'in Redis/InMemory store kontratı).
- **User entity + migration:** İki yeni alan (`PayoutAddressChangedAt`, `RefundAddressChangedAt`, her ikisi `DateTime?`, NULL) + `UserConfiguration` map. Migration: `20260423150726_T34_AddWalletAddressChangeTracking` — 2 kolon (nullable `datetime2`) + 2 SystemSetting insert + SYSTEM user update seed-anchor. `dotnet ef migrations add` ile üretildi.
- **SystemSettings (2 yeni, toplam 30 → 32):**
  - `wallet.payout_address_cooldown_hours` (int, Default **24**, Category `Wallet`)
  - `wallet.refund_address_cooldown_hours` (int, Default **24**, Category `Wallet`)
  - Default'lar admin override'ına açık; `IsConfigured=true` ile ship edildi → startup fail-fast (§8.9) geçer.
- **Error code kontratı (`WalletErrorCodes`):** `INVALID_WALLET_ADDRESS`, `SANCTIONS_MATCH`, `RE_AUTH_REQUIRED`, `RE_AUTH_TOKEN_INVALID`, `VALIDATION_ERROR` — frontend i18n (T97) ile paylaşılan stable string'ler.

## Known Limitations (dokümansal devirler)

- **Cooldown enforcement scope dışı.** T34 sadece `PayoutAddressChangedAt` / `RefundAddressChangedAt` damgasını vurur; `NOW < ChangedAt + cooldown_hours` kontrolü **T45** (yeni işlem başlatma — satıcı payout) ve **T46** (işlem kabul — alıcı refund) içinde uygulanacak. SystemSetting'ler şimdiden seed + env var ile bootstrap edilebilir.
- **Sanctions hit yan etkileri — T54/T59 devir.** 02 §21.1 sanctions eşleşmesinde `hesap flag'lenir` + `aktif işlemlere otomatik EMERGENCY_HOLD` der. T34 sadece yeni adresi reddeder (403 `SANCTIONS_MATCH`); FraudFlag oluşturma (T54 Fraud flag sistemi) ve EMERGENCY_HOLD uygulaması (T59 Emergency hold) forward-devir. Pipeline interface'i (`IWalletSanctionsCheck.EvaluateAsync`) zaten `MatchedList` döndürüyor — downstream handler için yeterli veri var.
- **Gerçek sanctions list — T82 devir.** `NoMatchWalletSanctionsCheck` şu an tüm adreslere `NoMatch` döner. T82 (Sanctions screening servisi) gerçek OFAC/EU list entegrasyonunu DI swap ile devralır.
- **Onay adımı — T93 (frontend).** 02 §12.1 "Adres girişinde kullanıcıya onay adımı gösterilir" — bu UX kuralı frontend S08 kapsamı (T93). Backend sadece PUT'i kabul eder; onay frontend tarafında toplandıktan sonra gönderilir.
- **Base58Check checksum tam doğrulaması — post-MVP.** 07 §5.3 format-only diyor (T prefix + 34 char). Full Base58Check decode + 32-bit checksum kontrolü production-hardened sanctions/validator servisine T82'de girebilir.

## Etkilenen Modüller / Dosyalar

**Yeni — Skinora.Users (Application/Wallet alt paketi):**
- `backend/src/Modules/Skinora.Users/Application/Wallet/WalletRole.cs` — Seller / Buyer enum
- `backend/src/Modules/Skinora.Users/Application/Wallet/ITrc20AddressValidator.cs` + `Trc20AddressValidator` (singleton, pure)
- `backend/src/Modules/Skinora.Users/Application/Wallet/IWalletSanctionsCheck.cs` + `WalletSanctionsDecision` + `NoMatchWalletSanctionsCheck` (singleton)
- `backend/src/Modules/Skinora.Users/Application/Wallet/IActiveTransactionCounter.cs` (interface only)
- `backend/src/Modules/Skinora.Users/Application/Wallet/IWalletAddressService.cs` + `WalletAddressService` (scoped, DbContext-bound)
- `backend/src/Modules/Skinora.Users/Application/Wallet/WalletAddressDtos.cs` — `UpdateWalletRequest`, `UpdateWalletResponse`
- `backend/src/Modules/Skinora.Users/Application/Wallet/WalletUpdateResult.cs` — `WalletUpdateStatus` enum + `WalletUpdateResult`
- `backend/src/Modules/Skinora.Users/Application/Wallet/WalletErrorCodes.cs`

**Yeni — Skinora.Transactions (Application/Wallet):**
- `backend/src/Modules/Skinora.Transactions/Application/Wallet/ActiveTransactionCounter.cs` — EF Core impl

**Değişiklik — Skinora.Users:**
- `backend/src/Modules/Skinora.Users/Domain/Entities/User.cs` — `PayoutAddressChangedAt`, `RefundAddressChangedAt` alanları
- `backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserConfiguration.cs` — 2 yeni property map

**Değişiklik — Skinora.Platform:**
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — 31, 32 yeni row'lar

**Değişiklik — API katmanı:**
- `backend/src/Skinora.API/Controllers/UsersController.cs` — 2 yeni endpoint + `UpdateWalletAsync` private orchestrator
- `backend/src/Skinora.API/Configuration/UsersModule.cs` — 4 yeni DI kaydı (TRC-20 validator, sanctions, counter, wallet service)

**Yeni — Migration:**
- `backend/src/Skinora.Shared/Persistence/Migrations/20260423150726_T34_AddWalletAddressChangeTracking.cs` + `.Designer.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — güncellendi (2 kolon + 2 SystemSetting satırı)

**Yeni — Testler:**
- `backend/tests/Skinora.API.Tests/Integration/WalletAddressEndpointTests.cs` — 10 integration test (SQLite in-memory factory, AuthReVerifyEndpointTests mirror'ı + yeni `Transaction` helper)

**Değişiklik — SeedDataTests:**
- `backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` — 30→32 expected count, `wallet.payout_address_cooldown_hours` + `wallet.refund_address_cooldown_hours` configured-key listesine eklendi

**Değişiklik — Doküman:**
- `Docs/06_DATA_MODEL.md` v4.9 → v5.0 — User §3.1'e 2 yeni alan satırı + SystemSetting §3.17 parametre tablosuna 2 yeni row

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T34) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `PUT /users/me/wallet/seller` → satıcı ödeme adresi kaydet/güncelle | ✓ | `UsersController.UpdateSellerWallet` + `WalletAddressService.UpdateWalletAsync(role=Seller)`. Testler: `UpdateSellerWallet_NoExistingAddress_ReturnsSuccessAndPersists` (200 + DB persist + `PayoutAddressChangedAt` damga), `UpdateSellerWallet_ExistingAddress_WithValidReAuthToken_Persists` (update flow ✓). |
| 2 | `PUT /users/me/wallet/refund` → alıcı iade adresi kaydet/güncelle | ✓ | `UsersController.UpdateRefundWallet` + aynı service `role=Buyer`. Test: `UpdateRefundWallet_NoExistingAddress_PersistsAndSetsTimestamp` — `DefaultRefundAddress` + `RefundAddressChangedAt` set, seller alanları NULL kalır (cross-role sızıntı yok). |
| 3 | Merkezi doğrulama pipeline: TRC-20 format + sanctions screening | ✓ | `WalletAddressService` sıra: (1) `ITrc20AddressValidator.IsValid` → InvalidAddress, (2) user load, (3) re-auth, (4) `IWalletSanctionsCheck.EvaluateAsync` → SanctionsMatch, (5) persist. Testler: `UpdateSellerWallet_InvalidFormat_Returns400InvalidWalletAddress` (X prefix), `UpdateSellerWallet_WrongLength_Returns400InvalidWalletAddress`, `UpdateSellerWallet_SanctionsMatch_Returns403AndLeavesAddressUnchanged` (stub flag flip → 403 + persist yok). |
| 4 | Mevcut adres varsa `X-ReAuth-Token` zorunlu (Steam re-verify) | ✓ | Controller re-auth guard — mevcut adres + header yok → service ReAuthRequired döner → 403. Mevcut adres + geçersiz header → controller 403 `RE_AUTH_TOKEN_INVALID`. Testler: `UpdateSellerWallet_ExistingAddress_WithoutReAuthToken_Returns403ReAuthRequired`, `UpdateSellerWallet_ExistingAddress_WithInvalidReAuthToken_Returns403TokenInvalid`, `UpdateSellerWallet_ExistingAddress_WithValidReAuthToken_Persists` (+ 2. çağrı 403 — single-use kanıtı). |
| 5 | Cooldown: satıcı → yeni işlem başlatma engeli; alıcı → yeni işlem başlatma + kabul engeli | ⚠️ **kısmi — devir** | T34 `PayoutAddressChangedAt` / `RefundAddressChangedAt` alanlarını damgalar + iki yeni SystemSetting ship eder. Enforcement kuralı (NOW < ChangedAt + cooldown_hours bloğu) **T45/T46** işlem başlatma/kabul akışlarında uygulanır (plan/11 §T45 ve §T46). Bu scope bölümü 07 §5.3 response'u veya T34 bitirme için zorunlu değil; forward-devir 02 §12.3 doğrultusunda belgelidir. |
| 6 | Aktif işlemler eski adresle tamamlanır (snapshot prensibi) | ✓ | 06 §3.5 Transaction entity'si `SellerPayoutAddress` + `BuyerRefundAddress` snapshot alanlarına sahip (T19). T34 `User.DefaultPayoutAddress`'i değiştirirken Transaction'ların snapshot'larına dokunmaz. `IActiveTransactionCounter` sayar, değiştirmez. Test: `UpdateSellerWallet_CountsNonTerminalTransactionsOnly` — 3 non-terminal CREATED/PAYMENT_RECEIVED + 1 COMPLETED + 1 unrelated → count = 2. Snapshot dokunulmadan kaldığı doğrulanır. |
| 7 | Adres onay adımı | ⚠️ **UI scope — T93** | 02 §12.1 / §12.2 "Adres girişinde kullanıcıya onay adımı" frontend UX kuralı (S08 Profil sayfası). Backend sadece PUT'i kabul eder; onay UI katmanında toplanır → T93 (Profil sayfaları) scope'unda. T34 dışı, doküman-destekli devir. |

## Doğrulama Kontrol Listesi (11 §T34)

- [x] **02 §12 tüm kurallar uygulanmış mı?** — Merkezi pipeline (§12.3): format + sanctions. Adres değişikliği doğrulaması (§12.3): `X-ReAuth-Token` zorunlu. Snapshot prensibi (§12.3): Transaction entity'si kendi adres field'larını saklıyor. Zorunluluk kontrolü (§12.1 "cüzdan olmadan işlem başlatılamaz"): T45'te enforce edilecek — T34 sadece kayıt yolu. Onay adımı (§12.1): T93 UI scope. Aktif işlem varken değişiklik (§12.1): snapshot prensibi + `activeTransactionsUsingOldAddress` counter ile frontend bilgilendiriliyor.
- [x] **Sanctions eşleşmesinde hesap flag'leniyor mu?** — **Kısmi.** T34 yeni adresi reddediyor (403 `SANCTIONS_MATCH`). FraudFlag entity oluşturma (T54) ve aktif işlemlere EMERGENCY_HOLD (T59) forward-devir. 02 §21.1'in 3 yan-etki maddesinden 1'i T34'te karşılanıyor (red); kalan 2 scope dışı. `IWalletSanctionsCheck.EvaluateAsync` sözleşmesi `MatchedList` döndürdüğü için downstream handler için yeterli veri var.
- [x] **Cooldown mekanizması çalışıyor mu?** — **Kısmi.** T34 timestamp'i damgalıyor + cooldown süresini SystemSetting'e koyuyor; `NOW < ChangedAt + cooldown` enforcement T45/T46'da. Bu scope ayrımı 11 plan + scope onayında kabul edildi.

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build Skinora.sln -c Release
```
→ Build succeeded. **0 Warning(s). 0 Error(s).** 00:00:07.

**Birim + integration (lokal, Release, non-integration kategorisi — SQL Server'a ihtiyaç duymayanlar):**

| Test projesi | Geçen | Başarısız | Süre |
|---|---|---|---|
| `Skinora.Shared.Tests` (unit) | 156 | 0 | 22 s |
| `Skinora.Auth.Tests` (unit) | 54 | 0 | 0.2 s |
| `Skinora.Transactions.Tests` (unit) | 58 | 0 | 52 s |
| `Skinora.API.Tests` (unit + SQLite integration) | **148** | **0** | 3 dk 19 s |

`WalletAddressEndpointTests` 10/10 PASS:
1. `UpdateSellerWallet_InvalidFormat_Returns400InvalidWalletAddress`
2. `UpdateSellerWallet_WrongLength_Returns400InvalidWalletAddress`
3. `UpdateSellerWallet_NoExistingAddress_ReturnsSuccessAndPersists`
4. `UpdateSellerWallet_ExistingAddress_WithoutReAuthToken_Returns403ReAuthRequired`
5. `UpdateSellerWallet_ExistingAddress_WithInvalidReAuthToken_Returns403TokenInvalid`
6. `UpdateSellerWallet_ExistingAddress_WithValidReAuthToken_Persists` (token single-use bonus assert)
7. `UpdateSellerWallet_SanctionsMatch_Returns403AndLeavesAddressUnchanged`
8. `UpdateSellerWallet_CountsNonTerminalTransactionsOnly` (3 non-terminal + 1 terminal + 1 unrelated → count=2)
9. `UpdateRefundWallet_NoExistingAddress_PersistsAndSetsTimestamp` (refund role + cross-role no-leak)
10. `UpdateSellerWallet_Unauthenticated_Returns401`

**SQL Server-bağımlı `Category=Integration` testleri CI'de koşacak:** `SeedDataTests` 30→32 count değişikliği + `wallet.*` key'leri configured listesinde doğrulanıyor.

**Format:**
```bash
dotnet format Skinora.sln --verify-no-changes --no-restore
```
→ 0 değişiklik (temiz).

## Altyapı Değişiklikleri

- **Test factory pattern:** `WalletAddressEndpointTests.Factory` — `UserProfileEndpointTests.Factory`'yi temel alır, ek olarak: (1) `IReAuthTokenStore` swap (`InMemoryReAuthTokenStore`), (2) `IWalletSanctionsCheck` swap (`ConfigurableSanctionsStub` — per-test `ShouldMatch` flag), (3) `CreateTransactionAsync` helper (STEAM_ID method + `TargetBuyerSteamId` pair → `CK_Transactions_BuyerMethod_SteamId` CHECK constraint geçer). T35+ wallet/profile yazma akışları için yeniden kullanılabilir pattern.
- **Migration:** `20260423150726_T34_AddWalletAddressChangeTracking`. `dotnet ef migrations add` ile üretildi. 2 kolon ekleme (Users tablosu, nullable `datetime2`) + 2 SystemSettings insert + SYSTEM user update (seed anchor). Down migration tam reverse. Dbup idempotent test T28 CI job'unda bu migration'ı yeni container'a 2× uygulayacak.
- **Yeni NuGet bağımlılığı:** **yok.**

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş.
- **Startup CI check (Adım 0):** İlk okumada 1 `failure` (`24841594435` Docker Publish / sidecar-steam) — log "Hello future GitHubber!" HTML hata sayfası = ghcr.io transient 503. Aynı commit'in CI workflow run'ı (`24841594207`) success. Kullanıcı onayıyla `gh run rerun 24841594435 --failed` çağrıldı; re-run success. Son 3 main run: `24841594435` ✓, `24841594207` ✓, `24839671945` ✓. T34'e başlamadan önce yeşil teyit edildi.
- **Dış varsayımlar (Adım 4):**
  - TRC-20 format (T prefix + 34 char + Base58 alfabe) — 07 §5.3 explicit + Tron docs ✓
  - Transaction entity snapshot alanları mevcut (`SellerPayoutAddress`, `BuyerRefundAddress` — T19 `Transaction.cs:46-47`) ✓
  - Terminal status tanımı (`TransactionStatus.COMPLETED` + 4 `CANCELLED_*`) ✓
  - **`ISanctionsCheck` Steam-ID-anahtarlı, adres-anahtarlı değil** — yeni interface `IWalletSanctionsCheck` eklendi (scope proposal'da açıkça belirtildi, kabul edildi) ✓
  - **User entity'de wallet-change timestamp yok** → SPEC_GAP; scope proposal'da additive olarak T30 precedent'ı ile T34'e dahil edildi, kullanıcı onayladı ✓
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+[a-z]?'` sadece `T34` döner. Yabancı task commit'i yok.
- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izlemeli (validate.md Adım 18).

## Commit & PR

- **Branch:** `task/T34-wallet-address`
- **Commit (kod):** `2a11ffc`
- **Commit (rapor+status+memory):** `569d92c`
- **PR:** [#58](https://github.com/turkerurganci/Skinora/pull/58)
- **CI run:** [`24843938408`](https://github.com/turkerurganci/Skinora/actions/runs/24843938408) — 10 job, **9 success + 1 skipped** (`0. Guard (direct push)` PR'da beklenen skip). Lint ✓ Build ✓ Unit ✓ Integration ✓ Contract ✓ Migration dry-run ✓ Docker build ✓ CI Gate ✓.

---

## Doğrulama (bağımsız validator — 2026-04-23)

**Verdict:** ✓ **PASS** — 0 S-bulgu, 2 minor advisory (her ikisi de rapor Known Limitations bölümünde doğru belgelenmiş forward-devir).

### HARD STOP kapıları
- **Adım -1 Working tree:** `git status --short` boş ✓.
- **Adım 0 Main CI:** Son 3 run `24841594435` ✓, `24841594207` ✓, `24839671945` ✓ — hepsi success.
- **Adım 0b Memory drift:** `MEMORY.md` T34 satırı mevcut (Line 11, 29, 30) ✓.

### Bağımsız kabul kriteri verdict'i (yapım raporu okunmadan üretildi, Faz 3'te karşılaştırıldı)

| # | Kriter | Bağımsız verdict | Rapor verdict | Uyum |
|---|---|---|---|---|
| 1 | `PUT /users/me/wallet/seller` | ✓ | ✓ | Uyumlu |
| 2 | `PUT /users/me/wallet/refund` | ✓ | ✓ | Uyumlu |
| 3 | Merkezi doğrulama pipeline (TRC-20 format + sanctions) | ✓ | ✓ | Uyumlu |
| 4 | Mevcut adres varsa `X-ReAuth-Token` zorunlu | ✓ | ✓ | Uyumlu |
| 5 | Cooldown (satıcı/alıcı işlem başlatma engeli) | ⚠ kısmi — T45/T46 devir | ⚠ kısmi — T45/T46 devir | Uyumlu |
| 6 | Aktif işlemler eski adresle tamamlanır (snapshot) | ✓ | ✓ | Uyumlu |
| 7 | Adres onay adımı | ⚠ UI scope — T93 devir | ⚠ UI scope — T93 devir | Uyumlu |

### Doğrulama kontrol listesi
- [x] **02 §12 tüm kurallar uygulanmış mı?** — Merkezi pipeline ✓, re-auth ✓, snapshot ✓. Zorunluluk/cooldown enforcement + onay adımı forward-devir (T45/T46/T93), belgelenmiş.
- [x] **Sanctions eşleşmesinde hesap flag'leniyor mu?** — **Kısmi.** Yeni adres red (403 `SANCTIONS_MATCH`) ✓; FraudFlag oluşturma (T54) + EMERGENCY_HOLD (T59) + gerçek liste (T82) forward-devir. `FraudFlagType` enum'da `SANCTIONS_MATCH` değeri henüz yok — T54/T82 devri mimari olarak tutarlı. `NoMatchWalletSanctionsCheck` stub kodda açıkça "T82 devir" olarak yorumlanmış.
- [x] **Cooldown mekanizması çalışıyor mu?** — **Kısmi.** `PayoutAddressChangedAt` / `RefundAddressChangedAt` damgalanıyor + 2 yeni SystemSetting (default 24 saat) seed; `NOW < ChangedAt + cooldown` enforcement T45/T46 devri.

### Test sonuçları (bağımsız koşum)

| Tür | Sonuç | Kanıt |
|---|---|---|
| Build (lokal Debug, `dotnet build -warnaserror -v quiet`) | **0W / 0E** | 14.78 s |
| `WalletAddressEndpointTests` (filter) | **10/10 PASS** | 2 s, `Skinora.API.Tests.dll` |
| Tüm backend solution testleri (11 proje) | **590 PASS / 0 FAIL / 0 SKIP** | Shared 166, API 148, Auth 85, Transactions 68, Platform 28, Notifications 25, Steam 21, Admin 20, Fraud 12, Disputes 11, Payments 6 |
| `Skinora.Users.Tests` | **boş** (scaffolding) | Kabul edilebilir — wallet testleri API.Tests içinde |
| Task branch CI run `24844230495` | **10/10 ✓** | 1 Lint, 2 Build, 3 Unit, 4 Integration, 5 Contract, 6 Migration dry-run, 7 Docker build, CI Gate + Detect changed paths ✓; 0. Guard skipped (PR'da beklenen) |
| PR #58 status check rollup | **mergeable** | `gh pr view 58` |

### Güvenlik kontrolü
- **Secret sızıntısı:** ✗ bulgu — kod, test ve migration temiz; token/key hardcoded yok (test stub'ındaki `TestSecret` beklenen symmetric key pattern'ı).
- **Auth etkisi:** 2 yeni endpoint `[Authorize(Policy=Authenticated)]` + `[RateLimit("user-write")]`; public endpoint açılmamış.
- **Re-auth token:** single-use (T31 Redis GETDEL / InMemoryReAuthTokenStore kontratı), `payload.UserId != userId` cross-user guard ✓ (controller 137-138).
- **Input validation:** TRC-20 format validator Base58 alphabet whitelist + uzunluk + prefix; null/whitespace reddediliyor. `UpdateWalletRequest` null body → 400 `VALIDATION_ERROR`.
- **Yeni dış bağımlılık:** yok (`.csproj` diff boş).
- **Migration güvenliği:** 2 kolon NULL eklemesi — mevcut satırları etkilemez. Seed user update sadece `Id = SYSTEM_USER_ID`'yi hedefler (deterministik). Down tam reverse.

### Doküman uyumu
- **07 §5.3 / §5.4:** Response alanları (`walletAddress`, `updatedAt`, `activeTransactionsUsingOldAddress`) tam eşleşiyor. Error code'lar (`VALIDATION_ERROR`, `INVALID_WALLET_ADDRESS`, `SANCTIONS_MATCH`, `RE_AUTH_REQUIRED`, `RE_AUTH_TOKEN_INVALID`) ve HTTP status kodları eksiksiz.
- **02 §12.3 / 03 §9.1 §9.2:** Merkezi pipeline ve snapshot prensibi doğru; enforcement + flag yan etkileri T45/T46/T54/T59/T82'ye kodda yorum + raporda Known Limitations ile belgelenmiş.
- **06 §3.1 (User tablosu):** `PayoutAddressChangedAt` + `RefundAddressChangedAt` kolonları v5.0 güncellemesinde tablolandı.
- **06 §3.17 (SystemSettings):** `wallet.payout_address_cooldown_hours` + `wallet.refund_address_cooldown_hours` eklendi (int, default 24, Category `Wallet`).

### Rapor karşılaştırması
- **Tam uyumlu** — 7 kabul kriteri, 3 kontrol listesi maddesi, test sonuçları (10/10 wallet + API.Tests 148/148), güvenlik kontrolleri ve doküman deltası rapor ile birebir örtüşüyor. Known Limitations bölümü T45/T46/T54/T59/T82/T93 forward-devirlerini doğru target-task'lara bağlıyor.
- **Bulgu:** yok.

### Bundled-PR check (Bitiş Kapısı 9)
`git log main..HEAD --format='%s'` → 3 commit, hepsi `T34:` prefix'li. Yabancı task commit'i yok ✓.
