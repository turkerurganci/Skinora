# T145 — Veri modeline ağ boyutu + adres biçimi [RİSKLİ]

**Faz:** F8 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Enum:** `Skinora.Shared.Enums.BlockchainNetwork` (`TRON`, `ETHEREUM`) — 06 §2.25. `TRON` ilk değer = CLR varsayılanı; ağ seçmeyen kod TRON'a düşer (sıralamayı sabitleyen test var).
- **`Network` kolonu** (`nvarchar(20)`, bütün enum'lar gibi adı saklar):
  - NOT NULL: `PaymentAddresses`, `BlockchainTransactions`, `ColdWalletTransfers`, `SellerPayoutIssues`, `WalletAddressHistory`.
  - NULL: `Transactions` (kabul anında dolar; T147'ye kadar kabul daima `TRON` yazar).
  - `SanctionedAddresses.Network` string → enum; değerler `'TRC-20'` → `TRON` / `ETHEREUM`, CHECK `IN ('TRON','ETHEREUM')`.
- **Tekillik anahtarları ağı içeriyor:** `UQ_PaymentAddresses_Network_Address`, `UQ_PaymentAddresses_Network_HdWalletIndex`, `UQ_BlockchainTransactions_Network_TxHash_EventIndex` (`WHERE TxHash IS NOT NULL`), `UQ_ColdWalletTransfers_Network_TxHash`, `UQ_SanctionedAddresses_Network_Address_Active` (`WHERE IsActive = 1`); `IX_WalletAddressHistory_UserId_Network_Type`.
- **Onay CHECK'leri ağa duyarlı** (06 §1.4 "Onay alanları"): `CONFIRMED` → `ConfirmedAt NOT NULL AND (Network <> 'TRON' OR ConfirmationCount >= 20)`; `PENDING` → `Network <> 'TRON' OR ConfirmationCount < 20`. TRON davranışı birebir aynı.
- **Tek forward migration `20261017230514_T145_NetworkDimension`:** NOT NULL kolonlar önce NULL eklenir → `TRON` ile doldurulur → NOT NULL yapılır; **veritabanı DEFAULT'u kalmaz** (ağsız INSERT başarısız olur, sessizce TRON olmaz). `Transactions.Network` yalnız kabul edilmiş (`AcceptedAt` dolu; `BuyerId` kabul sayılmaz — SteamID hedefli ilanda oluşturulurken dolar) ya da depozit adresi ayrılmış satırlarda `TRON`; kabul edilmemiş satır NULL. `SanctionedAddresses` `'TRC-20'` → `TRON` (eski CHECK düşürüldükten sonra, yeni CHECK eklenmeden önce). **Down** önce korur: herhangi bir tabloda TRON dışı satır varsa `THROW 50145` (eski şema onu taşıyamaz), yoksa eski şemaya ve `'TRC-20'`'ye döner. Para kolonlarına dokunulmadı (`decimal(18,6)`).
- **Kanonik biçim (06 §1.4):**
  - `Skinora.Shared.Blockchain.BlockchainAddress` — kural değerin biçiminden okunur: `0x` + hex → küçük harf; geri kalan her şey (TRON base58 adresi, TRON işlem kimliği) **dokunulmadan** döner (base58'de `0` yok, TRON adresi `0x` ile başlayamaz). `AreEqual` / `Comparer` (null hiçbir şeyle eşleşmez).
  - `CanonicalChainIdentifierConverter` — EF kolon dönüştürücüsü; adres kolonları (`User.DefaultPayout/RefundAddress`, `WalletAddressHistory.Address`, `Transaction.SellerPayoutAddress/BuyerRefundAddress`, `PaymentAddress.Address`, `BlockchainTransaction.FromAddress/ToAddress/ActualTokenAddress`, `ColdWalletTransfer.FromAddress/ToAddress`, `SanctionedAddress.Address`) ve hash kolonları (`BlockchainTransaction.TxHash`, `ColdWalletTransfer.TxHash`, `SellerPayoutIssue.PayoutTxHash`). Yazarken uygulanır; EF o kolonla karşılaştırılan sorgu parametresini de aynı biçime çevirir. Okurken de uygulanır (bugün EVM satırı yok; TRON için kimlik).
  - `EvmAddress` + `Keccak256` — EIP-55 sağlaması (bağımlılıksız; Ethereum'un Keccak-256'sı, FIPS SHA3 değil). Sonuç: `Invalid` / `Checksummed` / `NotChecksummed`. Test vektörleri koddan değil **ethers v6.17.0** (`keccak256`, `getAddress`) ve EIP-55 spesifikasyonundan; 135/136/137/300 baytlık girdiler blok sınırını kapsar.
- **Kapatılan büyük/küçük harf duyarlı karşılaştırmalar** (her biri yeniden okundu):
  - Backend: `StablecoinContractOptions.ResolveByContract` (Ordinal → `AreEqual`) · `MultiAccountSignalEvaluator.ParseExchangeAddresses` (Ordinal set → kanonik comparer) · `WalletAddressService` (`previous != candidate` → `AreEqual`, aday kanonik) · `SanctionedAddressLookup` (ağ + kanonik; son karar bellekte birebir — SQL Server'ın varsayılan CI collation'ı TRON'u duyarsız eşlerdi) · `DbWalletSanctionsCheck` / `DbLoginSanctionsCheck` / `TransactionCreationService` / `TransactionAcceptanceService` (ağ parametresi, bugün `TRON`) · `HotWalletMonitorService` (Ordinal → `AreEqual`) · **listede olmayan iki yer daha:** `ReconciliationService` (bakiye anlık görüntüsü `GroupBy/ToDictionary` Ordinal → kanonik comparer; sıcak/soğuk eşitliği) ve `BlockchainPayoutVerifier` (payout hash Ordinal → `AreEqual`). `AdminSanctionsService` ağ başına (aşağıda).
  - Sidecar (`src/address/canonicalAddress.ts`: `canonicalAddress`, `sameAddress`): `PaymentMonitorRules` (`classifyToken`, `isIncomingFor`) · `MonitorRegistry` ve `PostCancelMonitor` (izleyici anahtarı kanonik adres, kontrat kontrolü) · `TransferGuard` (sıcak/soğuk hedef, izinli kontrat kümesi, çıkış geçmişi) · `TransferService` / `RefundService` (imzacı adresi = depozit adresi) · `walletHandlers` (kontrat → sembol eşlemesi kanonik anahtarlı). TRON için hepsi kimlik/birebir; davranış değişmedi.
- **Ağın gerektiği kod yolları:**
  - `TransactionAcceptanceService`: kabulde `Network = TRON`, iade adresi kanonik.
  - `PaymentAddressAllocator`: ağ = `transaction.Network ?? TRON`; `MAX(HdWalletIndex)` **ağ başına**; satır ağı taşır, adres kanonik.
  - `BlockchainWebhookHandler`: tekrar kontrolü `(Network, TxHash, EventIndex)`; ağ webhook'un depozit adresinden (`PaymentAddress.Network`, soft-delete dahil) okunur; yazılan her satır o ağı taşır.
  - `PayoutIssueService` (işlemin ağı), `HotWalletService` (TRON). Diğer satır üreticileri (sweep, payout, iade kuyrukları, `AmountValidationService`) CLR varsayılanı `TRON` ile yazar — T147/T148 ağ yönlendirmesini getirene kadar bilinçli; `AmountValidationService`'e T144 paralel çalıştığı için dokunulmadı.
- **Admin yaptırım API'si (07 §9.23–§9.24):** `network` = `TRON` | `ETHEREUM` (verilmezse `TRON`; `TRC-20` → 400 `VALIDATION_ERROR`); doğrulayıcı ağa göre (TRON base58 / EVM EIP-55); adres kanonik saklanır ve döner; listede tanınmayan ağ filtresi boş sayfa; retroaktif tarama yalnız `TRON` (profil adresleri T146'ya kadar TRON) ve birebir eşleşme.
- **Doküman:** 06 v6.22 (§1.4 uygulandı işaretleri ve ayrıntı, §2.25, §3.1 geçmiş, §3.5/§3.7/§3.8/§3.8a/§3.22/§3.25 `Network` satırları ve anahtarlar, §5.1) · 07 v4.7 (§1.3, §9.23–§9.24) · 08 v3.20 (§3.0 adres biçimi, §3.2 atomiklik, §3.4 tekrar anahtarı) · 11 §F8 T145 karar maddesi + §7.6 izlenebilirlik satırları. Sürüm başlık ve altbilgide aynı.

## Etkilenen Modüller / Dosyalar

- **Shared:** `Enums/BlockchainNetwork.cs` (yeni) · `Blockchain/BlockchainAddress.cs`, `EvmAddress.cs`, `Keccak256.cs` (yeni) · `Persistence/Converters/CanonicalChainIdentifierConverter.cs` (yeni) · `Sanctions/ISanctionedAddressLookup.cs`, `SanctionedAddressMatch.cs` · `Persistence/Migrations/20261017230514_T145_NetworkDimension{,.Designer}.cs` (yeni), `AppDbContextModelSnapshot.cs`
- **Transactions:** entity'ler `Transaction`, `PaymentAddress`, `BlockchainTransaction`, `SellerPayoutIssue` + konfigürasyonları · `TransactionAcceptanceService`, `TransactionCreationService`, `PaymentAddressAllocator`, `StablecoinContractOptions`, `BlockchainWebhookHandler`, `PayoutIssueService`, `BlockchainPayoutVerifier`
- **Payments:** `ColdWalletTransfer` + konfigürasyon · **Platform:** `SanctionedAddress`, `SanctionedAddressNetworks`, `SanctionedAddressConfiguration`, `SanctionedAddressLookup` · **Users:** `WalletAddressHistory` + konfigürasyon, `UserConfiguration`, `IWalletSanctionsCheck`, `DbWalletSanctionsCheck`, `WalletAddressService` · **Auth:** `DbLoginSanctionsCheck` · **Fraud:** `MultiAccountSignalEvaluator`
- **API:** `AdminSanctionsService`, `AdminSanctionsDtos`, `HotWalletMonitorService`, `HotWalletService`, `ReconciliationService`
- **Sidecar:** `src/address/canonicalAddress.ts` (+test, yeni), `monitor/PaymentMonitorRules.ts`, `MonitorRegistry.ts`, `PostCancelMonitor.ts`, `transfer/TransferGuard.ts`, `TransferService.ts`, `RefundService.ts`, `api/walletHandlers.ts` (+ `PaymentMonitorRules.test.ts`, `TransferGuard.test.ts`)
- **Testler (yeni):** `Shared.Tests/Unit/Blockchain/BlockchainAddressTests.cs`, `EvmAddressTests.cs` · `API.Tests/Integration/NetworkDimension/NetworkDimensionSchemaTests.cs`, `NetworkDimensionMigrationRehearsalTests.cs`, `NetworkDimensionTestSupport.cs`. **Güncellenen:** `EnumTests`, `StablecoinContractOptionsTests`, `MultiAccountSignalEvaluatorTests`, `HotWalletMonitorServiceTests`, `ReconciliationServiceTests`, `PaymentAddressAllocatorTests`, `AdminSanctionsEndpointTests`, `WalletAddressEndpointTests`, `TransactionAcceptanceServiceTests` (stub imzası)
- **Doküman:** `Docs/06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `11_IMPLEMENTATION_PLAN.md`, `IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`, bu rapor

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | PaymentAddress, Transaction, BlockchainTransaction, ColdWalletTransfer ve ödeme sorunu kayıtlarında Network alanı | ✓ | Entity + konfigürasyon + migration; `CleanDatabase_T145IsApplied_AndNetworkColumnsHaveTheTargetShape` (6 tablo NOT NULL, `Transactions` NULL, nvarchar(20)) |
| 2 | Tekillik anahtarları ağı içeriyor (TxHash+EventIndex, Address, HdWalletIndex) | ✓ | `CleanDatabase_UniqueIndexes_LeadWithTheNetwork` (sys.index_columns sırası) + `PaymentAddress_SameAddress_IsUniquePerNetwork`, `..._HdWalletIndex_IsUniquePerNetwork`, `BlockchainTransaction_TxHashEventIndex_IsUniquePerNetwork`, `ColdWalletTransfer_TxHash_IsUniquePerNetwork`, `SanctionedAddress_ActiveAddress_IsUniquePerNetwork` (gerçek SQL Server); kodda `HdWalletIndex_Is_Counted_Per_Network`, `WebhookDedup_SameTxHashAndEventIndex_OnAnotherNetwork_IsANewEvent` |
| 3 | Kullanıcı adres geçmişi ve yaptırım listesi ağ başına | ✓ (⚠ kapsam) | `WalletAddressHistory.Network` + index `(UserId, Network, Type)`; `WalletAddressEndpointTests` geçmiş satırı `TRON`. Yaptırım: `(Network, Address)` UQ, CHECK, lookup ağ parametresi (`SanctionsLookup_MatchesEvmInAnyCase_TronExactly_AndOnlyOnItsNetwork`), admin API (`AdminSanctionsEndpointTests` T145 bloğu). `UserWalletAddress` tablosu T146'ya bırakıldı — aşağıda ⚠ (i) |
| 4 | Adresler tek kanonik biçimde saklanıyor ve karşılaştırılıyor (EVM duyarsız); bugünkü duyarlı karşılaştırmalar kapatıldı | ✓ | `EvmIdentifiers_AreStoredLowercase_TronValuesUnchanged` (ham SQL ile okuma), `EvmQueryParameter_IsCanonicalised_ByTheColumnConverter`, `PaymentAddress_EvmAddressInOtherLetterCase_IsTheSameKey`; birim: `BlockchainAddressTests`, `StablecoinContractOptionsTests.ResolveByContract_EvmContract_Matches_In_Any_Letter_Case`, `MultiAccountSignalEvaluatorTests`, `HotWalletMonitorServiceTests` (EVM eşleşir, TRON küçük harf eşleşmez), `ReconciliationServiceTests.RunAsync_EvmDepositAddress_SnapshotInChecksumCase_IsMatched`; sidecar `canonicalAddress.test.ts` + `PaymentMonitorRules`/`TransferGuard` testleri. **TRON duyarlılığı korunuyor:** `Canonicalize_NonEvmValue_IsUnchanged`, `AreEqual_TronDifferentCase_IsNotEqual`, lookup'ta küçük harfli TRON eşleşmiyor (CI collation'a rağmen), `ResolveByContract_Is_Case_Sensitive` (mevcut), sidecar TRON hedef/kontrat testleri |
| 5 | Para kolonları 6 ondalık kalır | ✓ | `CleanDatabase_MoneyColumns_StaySixDecimals` (7 kolon `18,6`); migration para kolonuna dokunmuyor |
| 6 | Tek forward migration; mevcut TRON satırları Network=TRON | ✓ | `T145_NetworkDimension` tek migration; `ExistingRows_PreF8SchemaRolledForward_AreAllTron` (satırlar yazılır → T145 öncesi şemaya inilir: `Network` kolonları yok, yaptırım `TRC-20` → T145 yeniden uygulanır: 6 tablo `TRON`, kabul/tamamlanmış/depozitli işlem `TRON`, kabul edilmemiş NULL); `Down_WithANonTronRow_IsRefused_AndChangesNothing` (`SqlException 50145`, şema değişmedi); `NetworkColumns_HaveNoDatabaseDefault` |
| — | SanctionedAddress Network CHECK genişletildi | ✓ | `SanctionedAddress_NetworkCheck_AcceptsEnumNames_RejectsPreF8Value` |
| — | Ağa duyarlı onay CHECK'leri | ✓ | `ConfirmedCheck_TwentyBlocksIsTronOnly`, `PendingCheck_TwentyBlockCeilingIsTronOnly` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (backend, bütün `*.Tests` projeleri) | ✓ 1780/1780 (11 assembly) | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — origin/main (T144) birleştirildikten sonra. Birleştirme öncesi 1739/1739 |
| Integration (gerçek SQL Server, Testcontainers) | ✓ 1553/1553 | `dotnet test Skinora.sln -m:1 --filter "FullyQualifiedName~.Integration"` birleştirme sonrası: Transactions 664, API 535, Fraud 81, Platform 74, Notifications 60, Disputes 58, Auth 37, Admin 22, Shared 16, Payments 6. Birleştirme öncesi ilk tam koşuda (11 assembly paralel, her biri kendi SQL konteyneri) Transactions'ta 6 test zaman/yük kaynaklı düştü (hiçbiri T145 koduna dokunmuyor); tek başına 6/6 ve Transactions 656/656 ✓ |
| Contract | ✓ 13/13 | `--filter "FullyQualifiedName~.Contract"` |
| Sidecar (vitest) | ✓ 518/518 (27 dosya) | `npx vitest run`; `tsc --noEmit` ✓, `eslint` ✓ |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` temiz; sidecar dosyaları `prettier` ile |
| Build | ✓ | `dotnet build Skinora.sln` 0 hata |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Doğrulama sonucu (2026-10-18)

- **PASS** — `8cd1472` üzerinde (doğrulama alt ajanı).
- Migration doğrulayıcı tarafından temiz bir veritabanında ve gerçek Nile verisinin T145 öncesine geri alınmış bir kopyasında prova edildi: Up, Down ve koruma (50145) belirtildiği gibi çalıştı; model snapshot'ında bekleyen değişiklik yok.
- Keccak yayımlanmış vektörlerle ve `@noble/hashes` ile doğrulandı.
- Unit 1780/1780; integration bütün assembly'ler yeşil (paralel yükte zaman aşımına uğrayanlar tek başına geçti); sidecar 518/518.
- Mutasyon kontrolleri: 7 yakalandı, 3 sidecar çağrı yeri mutantı hayatta kaldı (T150 kabul kriterine eklendi).
- Webhook'ta Unknown-vs-Idempotent değişikliği risksiz bulundu (PaymentConfirmed için de geçerli).
- Bulgular F1 (migration backfill: `BuyerId` kabul sayılmaz), F2 (`FeeEstimationService` adres karşılaştırması `sameAddress`), F4 (04 / 02 / 05 belge notları) bu dalda uygulandı; F3 T150 kabul kriterine yazıldı.

## Altyapı Değişiklikleri

- Migration: **Var** — `20261017230514_T145_NetworkDimension` (6 kolon, 5 tekil index + 1 performans index değişti, 3 CHECK değişti, 3 veri güncellemesi; Down korumalı).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni dış bağımlılık: Yok (Keccak-256 kod içinde).

## Commit & PR

- Branch: `task/T145-ag-boyutu-veri-modeli`
- Commit: `2fc9755` — T145: veri modeline ağ boyutu + kanonik adres biçimi
- PR: #341
- CI: PR CI run `39081964940` (head `c396961`, birleştirme sonrası) ✓ success; son rapor commit'inin run'ı da izlendi (yönetici raporunda)

## Known Limitations / Follow-up

- `UserWalletAddress` tablosu ve `User.DefaultPayout/RefundAddress` + `*ChangedAt` kolonlarının taşınması **T146** (⚠ i). O zamana kadar profil adresleri TRON; yaptırım eşleşmeleri `TRON` ağında.
- `Transaction.Network` için "kabulden sonra NOT NULL" CHECK'i yok; T147 ağ seçimiyle zorunlu kılmalı (⚠ v). Allocator ağ yoksa `TRON` varsayar.
- Sweep/payout/iade kuyrukları ve `AmountValidationService` satırları CLR varsayılanı `TRON` ile yazıyor; ağın satıra açıkça taşınması ve servis yönlendirmesi T147/T148'in işi.
- `BlockchainWebhookHandler`'da `ConfirmationCount = Math.Max(count, 20)` ve spam satırı için 20 tabanı TRON'a özgü; ağ politikasına taşınması T148.
- Webhook'un kendi `network` alanı ve depozit adresinin ağıyla karşılaştırılması T148 (05 §3.3a); bugün ağ yalnız depozit adresinden okunuyor.
- `TransactionDetailService` `payment.network` hâlâ gösterim metni `"Tron (TRC-20)"` — makine değerine geçiş T147/T156.
- Davranış farkı (kenar): webhook bilinmeyen bir `PaymentAddressId` ile ve daha önce kaydedilmiş bir `(TxHash, EventIndex)` ile gelirse eskiden `Idempotent`, şimdi `Unknown` döner (ağ depozit adresinden çözülemez). Sidecar her zaman satırın kendi depozit adresini gönderir.

## Notlar

- **Working tree:** temiz (başlangıçta `git status --short` boş).
- **Main CI startup check:** son tamamlanan run'lar `39076077822` CI ✓, `39076077806` Docker Publish ✓, `39075036563` CI ✓ (T142/T143 merge'leri).
- **Bağımlılık:** T141 ✓ (PR #337).
- **Dış varsayımlar:**
  - EF Core 9 bir değer dönüştürücüsünü o kolonla karşılaştırılan sorgu parametresine de uygular → `EvmQueryParameter_IsCanonicalised_ByTheColumnConverter` gerçek SQL Server'da geçti.
  - SQL Server'ın varsayılan collation'ı (`SQL_Latin1_General_CP1_CI_AS`, Testcontainers `mssql/server:2022-latest`) büyük/küçük harfe duyarsız → TRON lookup'ı bellek içi birebir kararla kapatıldı; `SanctionsLookup_..._TronExactly...` küçük harfli TRON'un eşleşmediğini gösteriyor.
  - `System.Security.Cryptography.SHA3_256` Ethereum'un Keccak-256'sı değildir (padding farkı) → `Keccak256_IsNotSha3` testi; vektörler ethers v6.17.0 çıktısı (sidecar `node_modules`, 2026-10-18).
  - Ayrı EF migration komutu: `dotnet ef migrations add T145_NetworkDimension --project Skinora.Shared --startup-project Skinora.API` (dotnet-ef 9.0.3), çıktı elle düzenlendi (nullable → doldur → NOT NULL, veri güncellemeleri, Down koruması); `Model_HasNoPendingChanges` ✓.
- **Kararlar (öneriyle kaydedildi — ⚠), 11 §F8 "T145 veri modeli kararları":**
  - (i) `UserWalletAddress` + `User` kolon taşıması T146'ya — 06 §1.4 satırı "T145, T146" diyordu, plan kriteri yalnız "adres geçmişi" diyor; tablo okuyan/yazan servislerle aynı görevde gelmezse kullanıcı adresinin iki kaynağı olur.
  - (ii) Kanonik biçim hash kolonlarına da uygulandı (aynı `0x`-hex kuralı; TRON işlem kimliği dokunulmaz).
  - (iii) 20 blok CHECK'i yalnız TRON'a (Ethereum `finalized` DB'de ifade edilemez).
  - (iv) Yaptırım listesinde Ethereum adresi: karışık harfte EIP-55 zorunlu, tek harf durumunda sağlamasız kabul; `TRC-20` değeri API'de artık 400.
  - (v) `Transaction.Network` CHECK'i T147'ye.
  - (vi) Keccak-256 bağımlılıksız yazıldı (yeni NuGet paketi yerine); yalnız sağlama kontrolünde kullanılıyor, anahtar materyali görmüyor.
- **Paralel T144:** `FinancialCalculator` / `AmountValidationService` para mantığına dokunulmadı. `TransactionCreationService`'te yalnız yaptırım çağrısının imzası değişti. Migration ya da status satırı çakışırsa origin/main birleştirilip EF snapshot yeniden üretilir.
