# T146 — Ağ başına kullanıcı ödeme/iade adresleri

**Faz:** F8 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Yeni tablo `UserWalletAddresses`** (06 §3.1a) — `UserWalletAddress : BaseEntity, IAuditableEntity`: `UserId` (FK → Users), `Network` (`BlockchainNetwork`, nvarchar(20)), `Type` (yeni enum `WalletAddressType` = `PAYOUT` / `REFUND`, nvarchar(10), 06 §2.26), `Address` (nvarchar(50), kanonik biçim — `CanonicalChainIdentifierConverter`), `ChangedAt` (NULL — yalnız takipten önceki taşınan adreste), `CreatedAt`, `UpdatedAt`, `RowVersion`. UNIQUE `UQ_UserWalletAddresses_UserId_Network_Type`, index `IX_UserWalletAddresses_Network_Address`; NOT NULL kolonlarda veritabanı DEFAULT'u yok.
- **Tek forward migration `20261018015746_T146_UserWalletAddresses`:** tablo oluşturulur → `Users`'taki boş olmayan her `DefaultPayoutAddress` / `DefaultRefundAddress` bir `TRON` satırı olur (`ChangedAt` = eski `*ChangedAt`, `CreatedAt` = `COALESCE(eski *ChangedAt, User.CreatedAt)`) → `Users`'tan dört kolon ve iki index düşer; hepsi tek migration işleminde. **Down** TRON dışı satır varsa `THROW 50146` ile reddeder, yoksa TRON satırlarını kolonlara geri yazar.
- **Doğrulayıcılar** (`Skinora.Shared.Blockchain`, `Skinora.Users.Application.Wallet`):
  - `TronAddress.IsValid` — gerçek base58check: 34 karakter, `T`, base58 çözümü 25 bayt, sürüm baytı `0x41`, `SHA-256(SHA-256(21 bayt))[0..4]` sağlaması. T146 öncesi `Trc20AddressValidator` yalnız alfabe + uzunluk + önek bakıyordu (bir harf yanlış adres ve `0x40`/`0x42` sürümlü adresler geçiyordu).
  - `IWalletAddressValidator` / `WalletAddressValidator` — ağa göre: TRON → `TronAddress`; Ethereum → T145'in `EvmAddress.Check` (karışık harfte EIP-55 zorunlu; tümü küçük / tümü büyük → `ValidWithoutChecksum`). Sonuç `Valid` / `ValidWithoutChecksum` / `InvalidFormat` / `ChecksumMismatch` + kanonik adres + `reason` (`INVALID_FORMAT` / `CHECKSUM_MISMATCH` / `CHECKSUM_CONFIRMATION_REQUIRED`). `ITrc20AddressValidator` kaldırıldı; profil, işlem oluşturma, kabul ve admin yaptırım listesi aynı doğrulayıcıyı kullanıyor.
  - `BlockchainAddress.ToDisplay` — EVM adresi EIP-55 gösterim biçimi, geri kalan dokunulmadan (06 §1.4).
- **API (07 §5.3/§5.4, §1.3):** `PUT /users/me/wallet/{seller,refund}` gövdesi `network` (`TRON` / `ETHEREUM`; verilmezse `TRON` — ⚠ i) ve `confirmLowercase` alır. Tek harfli Ethereum adresi onaysız → 400 `INVALID_WALLET_ADDRESS`, `details: { network, reason: "CHECKSUM_CONFIRMATION_REQUIRED", requiresConfirmation: true }`; diğer geçersizlerde `reason` = `INVALID_FORMAT` / `CHECKSUM_MISMATCH`, `requiresConfirmation: false`. Tanınmayan `network` → 400 `VALIDATION_ERROR`. 200 yanıtı `walletAddress` (gösterim biçimi), `network`, `updatedAt`, `activeTransactionsUsingOldAddress`, `checksumVerified`. Format/onay kontrolü tek kullanımlık `X-ReAuth-Token` tüketilmeden önce yapılır.
- **Ağ başına kurallar (`WalletAddressService`):** satır `(UserId, Network, Type)`; yeniden onay yalnız o ağda adres varsa; cooldown zamanı (`ChangedAt`) yalnız o satırda; yaptırım taraması adresin ağında; geçmiş satırı (`WalletAddressHistory.Network`) o ağla; aynı Ethereum adresinin başka harf yazımı değişiklik sayılmaz.
- **Okuyanlar:**
  - `GET /users/me` (07 §5.1): yeni `wallets[]` (`network`, `type`, `address` gösterim biçiminde, `changedAt`); `sellerWalletAddress` / `refundWalletAddress` TRON adresleri olarak korunuyor.
  - `GET /auth/me` `hasSellerWallet` / `hasRefundWallet`: herhangi bir ağda (⚠ vii).
  - İşlem oluşturma + uygunluk: satıcının **TRON** `PAYOUT` satırı ve onun cooldown'u (T147'ye kadar); profil adresi oluşturmada yeniden doğrulanıyor (artık sağlamayla).
  - Kabul: iade adresi TRON doğrulayıcısından geçer, kanonik biçimde `Transaction.Network` ile birlikte sabitlenir (K9 için hazır); iade cooldown'u alıcının **o ağdaki** `REFUND` satırından.
  - Giriş yaptırım kontrolü (`DbLoginSanctionsCheck`): kullanıcının her adresi kendi ağında.
  - Admin yaptırım listesi (AD23): retroaktif tarama eklenen adresin ağındaki bütün kullanıcı adreslerine bakar (ETHEREUM listesi artık profil adresleriyle eşleşebilir); son karar bellekte birebir.
  - Çoklu hesap (`MultiAccountDetector`): eşleşme `(Network, Type, kanonik adres)`, TRON → ETHEREUM sırasıyla; son karar bellekte birebir; flag ayrıntısına `matchNetwork`. `MultiAccountRetroScanJob`: herhangi bir ağda adresi olan aktif kullanıcılar.
  - Admin kullanıcı detayı (AD16 `walletHistory[]`): mevcut adresler satırlardan, her girişte `network`, Ethereum EIP-55.
  - Hesap silme (06 §6.2): kullanıcının bütün adres satırları silinir (tek DB işlemi içinde).
- **e2e:** `e2e/src/db.ts` satıcı ödeme adresini `UserWalletAddresses`'a yazar, temizlik önce bu tabloyu siler; alıcı iade adresi `TJRy…` sağlaması geçersizdi (tronweb `isAddress` = false) → geçerli `TVjCCQghBmKJK4ZGa26fpJYxTxvZzzAK1W`.
- **Doküman:** 06 v6.23 (§1.3 yeni kategori, §1.4 uygulandı notları, §2.26, §3.1, yeni §3.1a, §5.1, §5.2, §6.2, §7.1, envanter) · 07 v4.8 (§1.3, §2.10 örnek, §4.5, §5.1, §5.3, §5.4, §7.2 notu, §9.16, §9.24) · 02 v4.7 (§12.1 notu, §12.3 format satırı) · 03 v3.11 (§9) · 09 v1.4 (§9.5) · DEPLOY_RUNBOOK prova notu · 11 §F8 "T146 kullanıcı adresi kararları" + §7.6 satırları. Sürüm başlıkta ve altbilgide aynı.

## Etkilenen Modüller / Dosyalar

- **Shared:** `Blockchain/TronAddress.cs` (yeni), `Blockchain/BlockchainAddress.cs` (`ToDisplay`), `Enums/WalletAddressType.cs` (yeni), `Sanctions/ISanctionsViolationHandler.cs` (yorum), `Persistence/Migrations/20261018015746_T146_UserWalletAddresses{,.Designer}.cs` (yeni), `AppDbContextModelSnapshot.cs`
- **Users:** `Domain/Entities/UserWalletAddress.cs` (yeni), `User.cs`, `WalletAddressHistory.cs` (yorum) · `Infrastructure/Persistence/UserWalletAddressConfiguration.cs` (yeni), `UserConfiguration.cs`, `WalletAddressHistoryConfiguration.cs` · `Application/Wallet/IWalletAddressValidator.cs` (yeni; `ITrc20AddressValidator.cs` silindi), `WalletAddressService.cs`, `IWalletAddressService.cs`, `WalletAddressDtos.cs`, `WalletUpdateResult.cs`, `WalletRole.cs`, `IWalletSanctionsCheck.cs` · `Application/Profiles/UserProfileService.cs`, `UserProfileDtos.cs` · `Application/Account/AccountLifecycleService.cs`
- **Auth:** `CurrentUserService.cs`, `DbLoginSanctionsCheck.cs` · **Admin:** `AdminUserService.cs`, `AdminUserDtos.cs` · **Fraud:** `MultiAccountDetector.cs` · **Transactions:** `TransactionCreationService.cs`, `TransactionAcceptanceService.cs`, `TransactionEligibilityService.cs`, `TransactionLifecycleDtos.cs` (yorum)
- **API:** `UsersController.cs`, `AdminSanctionsService.cs`, `MultiAccountRetroScanJob.cs`, `UsersModule.cs` (DI), `SteamAuthenticationModule.cs` (yorum)
- **Testler (yeni):** `Shared.Tests/Unit/Blockchain/TronAddressTests.cs`, `Users.Tests/Unit/Wallet/WalletAddressValidatorTests.cs`, `API.Tests/Integration/NetworkDimension/UserWalletAddressMigrationRehearsalTests.cs`. **Güncellenen:** `WalletAddressEndpointTests` (+10 test metodu), `AdminSanctionsEndpointTests` (+2), `AdminUsersEndpointTests`, `UserProfileEndpointTests`, `AuthSessionEndpointTests`, `AccountLifecycleEndpointTests`, `TransactionLifecycleEndpointTests`, `NetworkDimensionSchemaTests`, `MultiAccountRetroScanJobTests`, `MultiAccountDetectorTests` (+3), `TransactionAcceptanceServiceTests` (+1 ve 3 yeni geçersiz adres vakası), `TransactionCreationServiceTests` (sağlama vakası), `TransactionEligibilityServiceTests` (+2), `AccountLifecycleServiceTests`, `BlockchainAddressTests` (`ToDisplay`), `EnumTests` (27 enum) ve `User` fikstürü kuran 20 test dosyası (mekanik: `DefaultPayoutAddress = X` → `WalletAddresses = { UserWalletAddress.For(TRON, PAYOUT, X) }`)
- **e2e:** `e2e/src/db.ts`
- **Doküman:** `Docs/02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `09_CODING_GUIDELINES.md`, `11_IMPLEMENTATION_PLAN.md`, `DEPLOY_RUNBOOK.md`, `IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`, bu rapor

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kullanıcı her ağ için ayrı ödeme ve iade adresi kaydedebiliyor | ✓ | `UpdateSellerWallet_EthereumChecksummed_PersistsLowercase_EchoesEip55`, `UpdateSellerWallet_FirstAddressOnSecondNetwork_NeedsNoReAuth_AndLeavesTronUntouched`, `UpdateRefundWallet_EthereumReplacement_WritesEthereumHistory_TronUntouched`, `UpdateSellerWallet_NoExistingAddress_ReturnsSuccessAndPersists` (gövdede ağ yok → TRON); şema `UQ_UserWalletAddresses_UserId_Network_Type` (`CleanDatabase_OneAddressPerUserNetworkAndType`, gerçek SQL Server); profil `GetMe_Authenticated_ReturnsOwnProfile` (`wallets[]`) |
| 2 | Bekleme süresi (cooldown) ağ başına işliyor | ✓ | `Payout_Cooldown_Is_Per_Network_Ethereum_Change_Does_Not_Block_Tron` (uygunluk), `Wallet_Cooldown_Is_Per_Network_Ethereum_Change_Does_Not_Block_Tron_Acceptance` (kabul), `Returns_Payout_Address_Cooldown_Active_When_Recent_Change` / `Wallet_Cooldown_Active_Rejects_With_403` (TRON satırı), `...FirstAddressOnSecondNetwork...` (TRON `ChangedAt` değişmiyor) |
| 3 | Geçmiş ağ başına işliyor | ✓ | `UpdateRefundWallet_EthereumReplacement_WritesEthereumHistory_TronUntouched` (`Network = ETHEREUM`, `SetAt` eski `ChangedAt`), `UpdateSellerWallet_Replacement_WritesPreviousAddressToHistory` (TRON), `UpdateSellerWallet_SameAddressInOtherLetterCase_IsNotAReplacement`; AD16 `GetUserDetail_WalletHistory_IncludesCurrentAndPreviousAddresses` (`network` alanı) |
| 4 | TRON base58check doğrulayıcı (gerçek sağlama) | ✓ | `TronAddressTests` — tronweb 5.3.5 `isAddress` ile üretilen bilinen cevaplar: geçerli 6 adres; son karakter, harf durumu, yer değiştirme, tümü büyük, `0x40`/`0x42` sürüm baytı, uzunluk, alfabe dışı ve T146 öncesi fikstürler reddediliyor. Uç uca: `UpdateSellerWallet_InvalidForNetwork_Returns400WithReason` (TRON `CHECKSUM_MISMATCH`), `Refund_Address_Format_Invalid_Returns_400` (kabul), `Rejects_Invalid_Wallet_Format_On_Seller_Profile` (oluşturma), `AddAddress_TronShapeWithBrokenChecksum_Returns400InvalidWalletAddress` (admin) |
| 5 | EVM EIP-55: karışık harfte sağlama zorunlu, tümü küçük harfte ikinci onay (bayrak API'de) | ✓ | `WalletAddressValidatorTests` (EIP-55 spesifikasyon vektörleri + ethers v6.17.0 `getAddress`); `UpdateSellerWallet_EthereumSingleCase_WithoutConfirmation_Returns400ConfirmationRequired` (4 spesifikasyon örneği, `details.reason` + `requiresConfirmation`), `UpdateSellerWallet_EthereumLowercase_WithConfirmation_Persists_ChecksumNotVerified` (`checksumVerified: false`), karışık harf yanlış sağlama → `CHECKSUM_MISMATCH`; `UpdateSellerWallet_ConfirmationRequired_DoesNotConsumeTheReAuthToken` |
| — | Mevcut kullanıcı adresleri tek forward migration'la yeni modele (TRON) | ✓ | `UserWalletAddressMigrationRehearsalTests`: temiz DB şeması; `ExistingAddresses_PreT146SchemaRolledForward_BecomeTronRows` (T146 öncesi şemaya inilir, kolonlar eski kodun yazdığı gibi doldurulur, T146 yeniden uygulanır: 5 TRON satırı, zamanlar korunur, NULL zaman NULL kalır, boş değer taşınmaz, kolonlar düşmüş); `Down_WithTronRowsOnly_PutsThemBackIntoTheUserColumns`; `Down_WithAnEthereumAddress_IsRefused_AndChangesNothing` (50146). T145 provası (`NetworkDimensionMigrationRehearsalTests`) T146 Down/Up'tan geçerek yeşil |
| — | Mevcut uçlar TRON için geriye uyumlu, `network` parametresi 07 §1.3'e göre | ✓ | Ağsız gövdeli bütün eski `WalletAddressEndpointTests` yeşil; `sellerWalletAddress`/`refundWalletAddress` korunuyor; `UpdateSellerWallet_UnknownNetwork_Returns400ValidationError` |
| — | Kayıtta yaptırım kontrolü ağ başına | ✓ | `UpdateSellerWallet_EthereumChecksummed_...` (stub ETHEREUM ağını ve kanonik adresi aldı), `UpdateSellerWallet_SanctionsMatch_...` (TRON); giriş ve retroaktif tarama: `AddAddress_EthereumRetroactiveScan_FlagsTheHolderOfThatEthereumAddress` |
| — | Çoklu hesap eşleşmesi çalışıyor | ✓ | `MultiAccountDetectorTests` (mevcut testler + `Flags_When_Other_Account_Shares_Ethereum_Payout_In_Other_Letter_Case`, `Tron_Match_Is_Reported_With_Its_Network`, `Same_Ethereum_Address_As_Payout_And_Refund_Is_No_Match`), `MultiAccountRetroScanJobTests` (yalnız Ethereum adresli kullanıcı da taranıyor) |
| — | K9 iade adresi T147'de kullanılabilir | ✓ | Kabul iade adresini işlemin ağının doğrulayıcısından geçirip kanonik biçimde `Transaction.Network` ile sabitliyor (`Happy_Path_...` `persisted.Network == TRON`); ret iadesi yönlendirmesi T147 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (backend, bütün `*.Tests` projeleri) | ✓ 1840/1840 (11 assembly) | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — Shared 511, Transactions 696, Platform 139, Notifications 133, Auth 92, API 77, Steam 59, Users 49, Realtime 40, Disputes 25, Fraud 19 |
| Integration + Contract (gerçek SQL Server, Testcontainers) | INTEG_RESULT | `dotnet test Skinora.sln -m:1 --filter "FullyQualifiedName~.Integration\|FullyQualifiedName~.Contract"` |
| Format | FORMAT_RESULT | `dotnet format Skinora.sln --verify-no-changes --severity error` |
| Build | ✓ | `dotnet build Skinora.sln` 0 hata |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Var** — `20261018015746_T146_UserWalletAddresses` (1 tablo, 1 unique + 1 performans index'i, FK; `Users`'tan 4 kolon + 2 index düşer; 2 veri kopyalama; Down korumalı, 50146).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni dış bağımlılık: Yok (base58 çözümü `BigInteger` ile, SHA-256 framework'ten).

## Commit & PR

- Branch: `task/T146-ag-basina-kullanici-adresleri`
- Commit: COMMIT_SHA — T146: ağ başına kullanıcı ödeme/iade adresleri
- PR: #PR_NO
- CI: CI_RESULT

## Known Limitations / Follow-up

- **T147:** ağ seçimi gelene kadar işlem oluşturma/uygunluk/kabul TRON satırını okur; Ethereum-yalnız satıcı ilan açamaz (`SELLER_WALLET_ADDRESS_MISSING`). Reddedilen ödeme iadelerinin alıcının iade adresine yönlendirilmesi (K9, `AmountValidationService` `FromAddress`) T147'de.
- **T156:** arayüz hâlâ yalnız TRON adresi girer ve `network` göndermez (sunucu `TRON` varsayar); ağ seçimi, `wallets[]` gösterimi ve `requiresConfirmation` onay penceresi T156'da. Frontend'in kendi TRC-20 biçim kontrolü sağlama yapmaz — sunucu reddeder.
- EIP-55'in "all caps" örnekleri gibi sağlaması tesadüfen tek harfe denk gelen adresler de onay ister (tek harfli girdide sağlama kanıtı yok — ⚠ ii).
- `ISanctionsViolationHandler.RecordWalletAttemptAsync` flag ayrıntısına ağ yazmaz (adresin biçimi ağı belli eder); gerekirse T157 admin ekranlarıyla.
- `IActiveTransactionCounter` eski adresi yalnız adresle sayar (TRON ve Ethereum adresleri çakışamaz).

## Notlar

- **Working tree:** temiz (başlangıçta `git status --short` boş).
- **Main CI startup check:** son tamamlanan run'lar `39081681867` ✓, `39081681865` ✓ (T144 merge), `39087410574` ✓ (T145 merge CI); T145 Docker Publish `39087410609` başlangıçta sürüyordu.
- **Bağımlılık:** T145 ✓ (PR #341).
- **Dış varsayımlar:**
  - TRON adresi = base58check(`0x41` + 20 bayt), sağlama `SHA-256²` ilk 4 bayt → tronweb 5.3.5 (`npm install tronweb@5.3.5` izole scratch klasörü, 2026-10-18) `isAddress` / `address.fromHex` / `address.toHex` ile doğrulandı; `0x40`/`0x42` sürümlü base58check dizgilerinin 34 karakter `T…` olduğu ve tronweb'in reddettiği ölçüldü.
  - EIP-55 spesifikasyon vektörleri (eips.ethereum.org/EIPS/eip-55) ve ethers v6.17.0 `getAddress` (aynı scratch kurulum) — karışık harfte yanlış sağlama `INVALID_ARGUMENT`.
  - EF Core 9 `HasConversion` sorgu parametresini de kanonik biçime çevirir (T145'te ölçüldü); SQL Server varsayılan collation'ı büyük/küçük harfe duyarsız → çoklu hesap ve retroaktif taramada son karar bellekte birebir.
  - Migration komutu: `dotnet ef migrations add T146_UserWalletAddresses --project Skinora.Shared --startup-project Skinora.API` (dotnet-ef 9.0.3); çıktı elle düzenlendi (önce tablo + veri kopyası, sonra kolon düşürme; Down koruması ve geri kopyalama).
  - Docker Desktop testler için açıldı; yerel Skinora yığını `unless-stopped` ile kendiliğinden kalktı (beklenen), iş sonunda `docker desktop stop`.
- **Kararlar (öneriyle kaydedildi — ⚠)** — 11 §F8 "T146 kullanıcı adresi kararları":
  - (i) `network` verilmezse `TRON` (07 §1.3 "zorunlu" diyordu; T156'ya kadar geriye uyum), tanınmayan değer 400 `VALIDATION_ERROR`.
  - (ii) İkinci onay tümü büyük harfli Ethereum adresine de uygulanır; alan adı `confirmLowercase` korundu.
  - (iii) Onay bayrağı: 400 `details.{network, reason, requiresConfirmation}`, 200 `network` + `checksumVerified`.
  - (iv) Format/onay kontrolü re-auth token'ı tüketilmeden önce.
  - (v) Satır `BaseEntity` (`UpdatedAt`, `RowVersion`); `ChangedAt` NULL olabilir (yalnız taşınan eski adres, cooldown yok); taşınan `CreatedAt` = `COALESCE(*ChangedAt, User.CreatedAt)`.
  - (vi) Hesap silmede satırlar silinir (06 §1.3 yeni "Kullanıcı verisi" kategorisi).
  - (vii) `hasSellerWallet` / `hasRefundWallet` = herhangi bir ağda.
  - (viii) T147'ye kadar oluşturma/uygunluk/kabul TRON satırı; kabulde Ethereum iade adresi reddedilir.
  - (ix) Çoklu hesap `(Network, Type, adres)`, bellekte birebir son karar, `matchNetwork` ayrıntısı.
  - (x) AD16 `network` alanı + EIP-55 gösterim; AD23 retroaktif tarama her ağda.
  - (xi) TRON doğrulaması her yerde gerçek base58check; `ITrc20AddressValidator` kaldırıldı.
  - (xii) Aynı adresin yeniden kaydı cooldown'u yeniden başlatır (önceki davranış).
  - (xiii) Down koruması `THROW 50146`.
  - (xiv) e2e alıcı iade adresi geçerli bir TRON adresiyle değiştirildi.
- **Paralel T148:** sidecar istemci yönlendirmesine ve ağ ayarlarına dokunulmadı. Status/memory/sürüm satırlarında ya da migration'da çakışma olursa origin/main birleştirilir, iki taraf korunur, migration gerekirse üstüne yeniden üretilir.
