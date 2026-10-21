# T147 — İşlem akışında ağ seçimi

**Faz:** F8 | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS (PR #344) (başlık F8 Gate Check'te bayattan düzeltildi) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Yeni tablo `TransactionNetworkOptions`** (06 §3.5b) — `TransactionNetworkOption : IAppendOnly`: `Id`, `TransactionId` (FK → Transactions, NO ACTION), `Network` (nvarchar(20), DEFAULT yok), `SellerPayoutAddress` (nvarchar(50), kanonik — `CanonicalChainIdentifierConverter`), `CreatedAt`. UNIQUE `UQ_TransactionNetworkOptions_TransactionId_Network`. İlanın kabul ettiği her ağ için bir satır; satıcının o ağdaki ödeme adresi oluşturma anında sabitlenir (02 §12.3 snapshot).
- **`Transaction.SellerPayoutAddress` NULL'a açıldı** — kabulde seçilen ağın satırından kopyalanır. **Yeni CHECK `CK_Transactions_Network_Accepted`** = `(AcceptedAt IS NULL) OR (Network IS NOT NULL AND SellerPayoutAddress IS NOT NULL)` (T145 kararı v).
- **Tek forward migration `20261018073432_T147_TransactionNetworkOptions`** (T148'in `20261018040534_T148_NetworkSettings`'inin üstünde yeniden üretildi; ilk sürüm `20261018041208_…`): kolon NULL'a açılır → tablo → her mevcut işleme (soft-delete dahil) kendi ağında (`COALESCE(Network,'TRON')`) mevcut ödeme adresiyle bir seçenek satırı → kabul edilmiş ama ağı boş satır kalmışsa TRON (koruma) → CHECK + index. **Down:** TRON dışı seçenek ya da işlem varsa `THROW 50147`; yoksa kabul edilmemiş ilanların NULL kolonunu TRON satırından geri doldurur.
- **İlan oluşturma (07 §7.2):** gövde `acceptedNetworks` alır (verilmezse `["TRON"]` — ⚠ i); boş/tekrarlı/tanınmayan/küçük harfli değer 400 `VALIDATION_ERROR`. Her ağ için (enum sırasıyla) servis açık mı (değilse 503 `NETWORK_UNAVAILABLE` — ⚠ ii), profilde o ağın `PAYOUT` adresi var mı (422 `SELLER_WALLET_ADDRESS_MISSING`), o ağın doğrulayıcısı (400 `INVALID_WALLET_ADDRESS`), o ağın cooldown'u (422 `PAYOUT_ADDRESS_COOLDOWN_ACTIVE`), o ağda yaptırım (403 `SANCTIONS_MATCH`) — hepsi `error.details.network` taşır. Depozit adresi artık burada **türetilmez** (⚠ iii).
- **Uygunluk (07 §7.3):** "herhangi bir ağda ilan açılabilir mi" — `SELLER_WALLET_ADDRESS_MISSING` hiçbir ağda ödeme adresi yoksa, `PAYOUT_ADDRESS_COOLDOWN_ACTIVE` bütün adresler cooldown'daysa (⚠ iv). Yalnız TRON adresli satıcı için cevap T147 öncesiyle aynı.
- **Kabul (07 §7.6):** gövde `network` alır (verilmezse `TRON` — ⚠ i; tanınmayan 400). İlanın o ağa satırı yoksa 422 `NETWORK_NOT_ACCEPTED`; ağın servisi yok/donmuşsa 503 `NETWORK_UNAVAILABLE` — ikisi de durum/taraf kontrollerinden sonra, iade adresi doğrulamasından ve Steam yoklamalarından **önce**, hiçbir şey yazmadan (⚠ xv). İade adresi seçilen ağın doğrulayıcısından ve yaptırım taramasından geçer; iade cooldown'u o ağın `REFUND` satırından. Yazılanlar: `Network`, `SellerPayoutAddress` (seçilen satırdan), `BuyerRefundAddress` (kanonik). Kayıttan sonra **depozit adresi seçilen ağda** ayrılır (en iyi çaba). Yanıt `network` + `totalAmount` (⚠ x).
- **Depozit ayırıcı:** `ACCEPTED` ve `SELLER_CONFIRMED` işlemlere, yalnız ağı olan işleme adres türetir (varsayılan ağ yok — ⚠ xiii); başka ağda mevcut depoziti `NetworkMismatch` ile reddeder (hata log'u, onarım yok). `EnsurePaymentAddressJob` aynı küme + `Network != null`. FLAGGED onayındaki erken ayırma (WP4b) kaldırıldı (`FraudFlagService`).
- **K9 (`AmountValidationService`):** beş ret türü (eksik tutar, fazla tutarın fazlası, çoklu/fazladan transfer, yanlış token, iptal sonrası geç ödeme) iade satırına `Transaction.BuyerRefundAddress` yazar; ücret tahmini depozit → iade adresi transferini fiyatlar. `FromAddress` artık hiçbir iade hedefi değildir. İade adresi yoksa iade satırı yazılmaz, `REFUND_BLOCKED` uyarısı yeni sebep `RefundBlockedReason.RefundAddressMissing` ile çıkar; fazla ödemede ödeme yine kabul edilir (⚠ vii). Dört iade olayına `RefundAddress` alanı; bildirim şablonları (en/tr/es/zh) `{RefundAddress}` gösterir, eski olayda `SourceAddress`'e düşer (⚠ viii).
- **Yeniden onay (11 §F8 T146 kararı xv):** `WalletAddressService` — kullanıcının başka bir ağda **aynı türde** adresi varken yeni bir ağa ilk adres de `X-ReAuth-Token` ister; o türde hiç adresi olmayanın ilk kaydı onaysız (⚠ xi). 403 mesajı güncellendi.
- **İşlem detayı (07 §7.5):** kimliği doğrulanmış görünümlerde kabulden önce `acceptedNetworks` + `networkQuotes` (`networkFeeAmount` T149'a kadar `"0.00"`), kabulden sonra `network`; `payment.network` ağa göre gösterim metni (`"Ethereum (ERC-20)"` eklendi), `payment.address` EIP-55 (⚠ ix).
- **`activeTransactionsUsingOldAddress`:** kabul edilmemiş ilanların seçenek satırlarındaki eski adresi de sayar (⚠ xiv).
- **Ağ erişilebilirliği:** `IBlockchainNetworkAvailability` — T148 birleştirmesinden sonra T148'in `BlockchainNetworkAvailability`'si (servisi yapılandırılmış ∧ sağlık dondurmasında değil); ara `TronOnlyNetworkAvailability` silindi (⚠ xii, bkz. "Entegrasyon (T148 sonrası)"). Ethereum T150'ye kadar kapalı → ilan ve kabul fail-closed.
- **e2e:** `e2e/src/db.ts` temizliği seçenek satırlarını siler; `payment-edge-cases.spec.ts` beş iade beklentisi K9'a çevrildi (`seed.buyerRefundAddress`, `fakeBuyerWallet` değil).
- **Doküman:** 02 v4.9 (§4.2, §4.4, §12.1, §12.3) · 03 v3.12 (§2.2/9, §3.2/4, §3.4, §9.2/3) · 05 v3.16 (§3.3 iade hedefi) · 06 v6.25 (§1.1, §1.4, §3.1a, §3.5, yeni §3.5b, §3.8, §4.1, §4.2, §5.1) · 07 v4.10 (§1.3, §5.3, §7.2, §7.5, §7.6) · 08 v3.22 (§3.0, §3.4) · 09 v1.5 (§9.5) · 11 §F8 "T147 işlem akışı kararları" + §7.6 satırları. Sürüm başlıkta ve altbilgide aynı (02/05/06/07/08 T148 birleştirmesinden sonra bir üst sürüm).

## Entegrasyon (T148 sonrası) (2026-10-18)

T148 (PR #343) main'e birleşti (`7f7d47f`); `origin/main` bu dala `git merge` ile alındı (rebase yok).

**Çakışmalar ve çözüm:**

- `BlockchainNetworkAvailability.cs` (iki tarafta eklendi): T148'inki alındı (arayüz aynıydı; uygulama + `IBlockchainNetworkHealth` + `AlwaysHealthyBlockchainNetworks`).
- `AmountValidationService`: iki niyet birlikte — iade satırı `Network`'ü depozitten (T148), hedefi `BuyerRefundAddress` (T147 K9); yanlış token kontratı ağ başına registry'den (T148), iade adresi yoksa erken dönüş (T147). Ücret tahmini ağ + iade adresiyle.
- `EnsurePaymentAddressJob`: aday = `ACCEPTED`/`SELLER_CONFIRMED` ∧ adressiz ∧ **ağı seçilmiş** ∧ ağın servisi yapılandırılmış. T148'in "ağı boş işlem TRON sayılır" kolu kaldırıldı — T147 kararı xiii (ayırıcı varsayılan ağ uydurmaz) ve `CK_Transactions_Network_Accepted` ile her kabul edilmiş satırın ağı var.
- `PaymentAddressAllocator`, `TransactionsModule`, `TransactionLifecycleDtos`, test fikstürleri: otomatik birleşti; `TransactionsModule`'deki ara kayıt elle silindi.
- Dokümanlar: başlık satırlarında iki değişiklik özeti korundu (T147 önce), sürüm bir üste: 02 v4.9, 05 v3.16, 06 v6.25, 07 v4.10, 08 v3.22 (başlık + altbilgi). Gövde satırları: 06 §1.4 "Onay alanları"/"Ayarlar" T148'den, "Yaptırım listesi" T147'den; 07 §1.3 ilan/kabul/detay T147'den (kabulde "açık" = T148'in erişilebilirliği notu eklendi), `supportedNetworks` T148'den; 08 §3.0 "Ağ başına servis"/"Ödeme kabulü" T148'den, diğerleri T147'den ("Aynı ağ kuralı" ve "Depozit adresi" satırlarına T148 notu). 11 §F8 iki karar maddesi de korundu (T147 xii güncellendi). `IMPLEMENTATION_STATUS.md`: T147 satırı bu dal, T148 satırı main. `MEMORY.md`: iki girdi de korundu.

**Kod değişiklikleri:**

- Ara `TronOnlyNetworkAvailability` ve DI kaydı silindi; ilan (`NETWORK_UNAVAILABLE`) ve kabul (`NETWORK_NOT_ACCEPTED` / `NETWORK_UNAVAILABLE`) T148'in `BlockchainNetworkAvailability`'sini okur (scoped; API host'ta sağlık yoklamasının `ProbeBlockchainNetworkHealth`'i).
- Ayırıcı T148'in `IBlockchainServiceRegistry.Sidecar(network)`'ünden türetir (birleşmede geldi); kurtarma işi registry'nin `ConfiguredNetworks`'üyle süzer.
- İade (`AmountValidationService`, `PaymentRefundToBuyerConsumer`), payout (`SellerPayoutQueueJob`) ve sweep (`SweepQueueJob`) satırları `Network`'ü depozitten/işlemden yazar (T148'in kodu); T147'nin kabulde yazdığı ağ ve CHECK sayesinde bunlar artık seçilen ağı taşır. `SellerPayoutQueueJob` / `PaymentRefundToBuyerConsumer`'daki `?? TRON` yalnız savunma okuması olarak kaldı (yorumlar CHECK'e atıf yapacak şekilde güncellendi). `PayoutIssueService` zaten işlemin ağını yazıyor.
- Migration T148'in veri migration'ının üstünde yeniden üretildi: eski `20261018041208_…` dosyaları silindi, snapshot main'inkine döndürüldü, `dotnet ef migrations add T147_TransactionNetworkOptions` → `20261018073432_T147_TransactionNetworkOptions`. Üretilen şema işlemleri eskisiyle birebir aynıydı; elle eklenen kısımlar (veri kopyası, koruma UPDATE'i, Down'daki 50147 koruması ve geri doldurma) aynen taşındı. Designer artık T148'in 8 `blockchain.ethereum.*` seed satırını içeriyor; snapshot değişmedi.

**Testler:**

- `NetworkChoiceLifecycleTests` (iki ağda uçtan uca): ayırıcı `FakeBlockchainServiceRegistry` (iki ağ yapılandırılmış) ile, erişilebilirlik T148'in gerçek `BlockchainNetworkAvailability`'si ile kurulur; elle eklenen onaylı sweep yerine gerçek `SweepQueueJob` koşar. Yeni doğrulamalar: depozit seçilen ağın servisinden türer (başka ağa istek yok); iade satırı `Network` = seçilen ağ ve ücreti o ağda fiyatlanır; sweep satırı `Network` = seçilen ağ, `ToAddress` = o ağın sıcak cüzdanı; payout satırı `Network` = seçilen ağ ve ücreti o ağda fiyatlanır.
- `FakeNetworkAvailability` silindi; yerine `TestNetworkAvailability` — T148'in gerçek uygulaması sahte registry ve ayarlanabilir sağlık okumasıyla. Yeni: `TransactionCreationServiceTests.Listing_On_A_Frozen_Network_Fails_Closed_With_Network_Unavailable`, `TransactionAcceptanceServiceTests.A_Frozen_Network_Fails_Closed_And_The_Other_Network_Still_Accepts` (servisi olan ama sağlık dondurmasındaki ağ 503; diğer ağ kabul edilir).
- `EnsurePaymentAddressJobTests`: T148'in iki testi T147 kuralına uyarlandı — adaylar `CREATED` değil `ACCEPTED` (ilan artık adres almaz); `Transactions_On_An_Unconfigured_Network_Do_Not_Starve_Tron` (50/55 bekleyen Ethereum işleminin arkasındaki TRON işlemi ilk turda adres alır; ağsız kabul edilmiş satıra adres türetilmez), `Legacy_Transactions_Wait_When_Tron_Itself_Is_Unconfigured` → `Only_Transactions_On_A_Configured_Network_Are_Candidates`.

**Davranış farkı (entegrasyon öncesi T147'ye göre):**

- TRON'un sağlık dondurması sırasında (sidecar kesintisi) TRON'da yeni ilan ve kabul 503 `NETWORK_UNAVAILABLE` alır; ara uygulama TRON'u her zaman açık sayıyordu. 07 §1.3'ün tanımı buydu ("ağ o an dondurulmuşsa 503").
- İade/payout/sweep satırlarının `Network`'ü CLR varsayılanı `TRON` değil, depozitin/işlemin ağı; ücret tahmini o ağın servisinden ve ayarından.
- Kurtarma işi servisi olmayan ağdaki işlemleri aday almaz (T148'in açlık düzeltmesi); depozit o ağın kendi servisinden türer (ara durumda tek sidecar istemcisi vardı).

**Kanıt:**

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ | `dotnet build Skinora.sln` 0 hata |
| Unit (bütün `*.Tests`) | ✓ 1913/1913 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — Transactions 732, Shared 528, Platform 148, Notifications 133, Auth 92, API 84, Steam 59, Users 49, Realtime 44, Disputes 25, Fraud 19 |
| Integration + Contract (gerçek SQL Server, Testcontainers) | ✓ 1701/1701 | `dotnet test Skinora.sln -m:1 --filter "FullyQualifiedName~.Integration\|FullyQualifiedName~.Contract"` — Transactions 715, API 623, Fraud 82, Platform 74, Notifications 60, Disputes 58, Auth 40, Admin 22, Shared 21, Payments 6. Tam koşuda API'de 5 `HangfireTests` testi paylaşılan bellek içi Hangfire depolamasının başka bir test host'unca kapatılmasıyla (`ObjectDisposedException`, sıra bağımlı) düştü; `HangfireTests` tek başına 16/16, API assembly'si yeniden 623/623 — bu entegrasyonun dokunmadığı bir alan |
| Migration provası | ✓ | `~MigrationRehearsal` 20/20 (T145, T146, T148, T147): T147 provası 8/8 — temiz şema, T148'in üstüne inip mevcut satırlarla tekrar yukarı, Down geri doldurma, Ethereum varken Down 50147; T148 provası T147 Down/Up'tan geçerek yeşil |
| Yeni/uyarlanan testler | ✓ 23/23 | `NetworkChoiceLifecycleTests` (2), iki "Frozen" testi, `EnsurePaymentAddressJobTests` |
| e2e harness | ✓ | `npx tsc --noEmit` |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes` |

## Etkilenen Modüller / Dosyalar

- **Transactions:** `Domain/Entities/TransactionNetworkOption.cs` (yeni), `Transaction.cs` · `Infrastructure/Persistence/TransactionNetworkOptionConfiguration.cs` (yeni), `TransactionConfiguration.cs` · `Application/Lifecycle/TransactionNetworks.cs` (yeni), `TransactionCreationService.cs`, `TransactionAcceptanceService.cs`, `TransactionEligibilityService.cs`, `TransactionDetailService.cs`, `TransactionDetailDto.cs`, `TransactionLifecycleDtos.cs`, `TransactionErrorCodes.cs` · `Application/Networks/BlockchainNetworkAvailability.cs` (T148'inki; ara `TronOnlyNetworkAvailability.cs` entegrasyonda silindi) · `Application/PaymentAddresses/PaymentAddressAllocator.cs`, `IPaymentAddressAllocator.cs`, `EnsurePaymentAddressJob.cs` · `Application/Webhooks/AmountValidationService.cs`, `IAmountValidationService.cs` · `Application/Wallet/ActiveTransactionCounter.cs`
- **Users:** `Application/Wallet/WalletAddressService.cs`, `WalletUpdateResult.cs` · **Fraud:** `Application/Flags/FraudFlagService.cs` · **Notifications:** dört iade bildirim tüketicisi + `Resources/NotificationTemplates{,.tr,.es,.zh}.resx`
- **Shared:** `Events/BuyerPaymentInsufficientEvent.cs`, `BuyerPaymentExcessRefundedEvent.cs`, `LatePaymentRefundRequestedEvent.cs`, `WrongTokenRefundRequestedEvent.cs`, `RefundBlockedAdminAlertEvent.cs` · `Persistence/Migrations/20261018073432_T147_TransactionNetworkOptions{,.Designer}.cs` (yeni), `AppDbContextModelSnapshot.cs`
- **API:** `Controllers/TransactionsController.cs` (yeni statüler, `details`), `UsersController.cs` (mesaj), `Configuration/TransactionsModule.cs` (DI)
- **Testler (yeni):** `Transactions.Tests/Integration/Lifecycle/NetworkChoiceLifecycleTests.cs` (iki ağda uçtan uca), `Transactions.Tests/Helpers/TestNetworkAvailability.cs` (T148'in gerçek uygulaması üstünde; ilk sürümdeki `FakeNetworkAvailability` entegrasyonda silindi), `API.Tests/Integration/NetworkDimension/TransactionNetworkOptionMigrationRehearsalTests.cs`. **Güncellenen:** `TransactionCreationServiceTests` (+8), `TransactionAcceptanceServiceTests` (+12), `TransactionEligibilityServiceTests` (+1, 1 yeniden yazıldı), `PaymentAddressAllocatorTests` (+3), `EnsurePaymentAddressJobTests`, `AmountValidationServiceTests` (+6: geç ödeme + adres yok × 5 tür), `TransactionAcceptanceUnitTests`, `TransactionLifecycleEndpointTests` (+6), `WalletAddressEndpointTests` (1 test → 4), `BlockchainWebhookEndpointTests`, `NetworkDimensionMigrationRehearsalTests`, `FraudFlagServiceTests` (3 → 1), `MultiAccountDetectorTests`; 24 fikstür dosyasına kabul edilmiş satır için `Network = TRON` (yeni CHECK); `Fraud.Tests/TestSupport/StubPaymentAddressAllocator.cs` silindi
- **e2e:** `e2e/src/db.ts`, `e2e/tests/payment-edge-cases.spec.ts`
- **Doküman:** `Docs/02`, `03`, `05`, `06`, `07`, `08`, `09`, `11`, `IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`, bu rapor

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Satıcı ilanda kabul ettiği ağları seçiyor (yalnız adresi kayıtlı ağlar); alıcı bunlardan birini seçiyor | ✓ | Oluşturma: `Listing_On_Both_Networks_Snapshots_Each_Networks_Payout_Address`, `Listing_On_Ethereum_Only_Writes_Just_That_Option`, `Listing_On_A_Network_Without_A_Payout_Address_Is_Refused_Naming_It`, `Malformed_AcceptedNetworks_Is_A_Validation_Error` (5), `Payout_Cooldown_Is_Checked_Per_Accepted_Network`, `Sanctions_Are_Screened_On_Each_Accepted_Networks_Payout_Address`, `Listing_On_A_Network_Without_A_Service_Fails_Closed_With_Network_Unavailable`. Kabul: `Buyer_Chooses_Ethereum_…`, `Buyer_Chooses_Tron_On_A_Two_Network_Listing_…`, `A_Network_The_Listing_Does_Not_Accept_Is_Refused_And_Nothing_Is_Written` (2), `A_Network_Without_A_Service_Fails_Closed_…`, `An_Unknown_Network_Name_Is_A_Validation_Error` (3), `The_Refund_Address_Must_Be_Valid_On_The_Chosen_Network` (3). Telden: `Create_With_A_Network_That_Has_No_Service_Returns_503_…`, `Create_Missing_Payout_Address_Names_The_Network_In_Details`, `Accept_On_A_Network_The_Listing_Does_Not_Accept_Returns_422_…`, `Accept_On_An_Accepted_Network_Without_A_Service_Returns_503_…`, `Detail_Before_Acceptance_Lists_The_Accepted_Networks_And_Quotes_…` |
| 2 | Depozit adresi seçilen ağda ayrılıyor; HD indeksi ağ başına | ✓ | `NetworkChoiceLifecycleTests` (TRON + ETHEREUM: depozit kabul anında, `Network` = seçilen ağ, indeks 0 = o ağın sayacı); `Derives_No_Deposit_Address_At_Creation_…`; ayırıcı: `CREATED_Listing_Has_No_Network_Yet_So_No_Address_Is_Derived`, `Accepted_Row_Without_A_Network_Is_Not_Given_A_Default_One`, `An_Existing_Deposit_On_Another_Network_Is_Reported_Not_Returned`, `Allocates_For_SELLER_CONFIRMED_…`, `HdWalletIndex_Is_Counted_Per_Network` (T145); `Ignores_FLAGGED_And_CREATED_Listings_No_Network_Yet`; `Allocation_Failure_After_Commit_Does_Not_Undo_The_Acceptance` |
| 3 | Payout ve iade, ödemenin geldiği ağdan yapılıyor | ✓ (T148 ile birlikte) | T147'nin payı: işlem ağı + o ağın payout snapshot'ı + o ağdaki iade adresi + o ağdaki depozit — `NetworkChoiceLifecycleTests` iki ağda ödeme, fazladan transferin iadesi ve payout hedefini doğrular (payout `ToAddress` = seçilen ağın satıcı adresi, iade `ToAddress` = o ağın iade adresi). Satırların `Network` alanı ve ağın servisine yönlendirilmesi T148'in işi (bkz. Known Limitations) |
| 4 | Reddedilen ödeme iadeleri (eksik, fazla, yanlış token, iptal sonrası geç, fazladan transfer) alıcının o ağdaki iade adresine; `AmountValidationService` `FromAddress` kullanmıyor — her ret türü için test | ✓ | Birim (`AmountValidationServiceTests`): eksik `ConfirmedPayment_Underpayment_AboveThreshold_QueuesIncorrectAmountRefund`, fazla `ConfirmedPayment_Overpayment_AdvancesStateAndQueuesExcessRefund`, fazladan transfer `ConfirmedPayment_MultiPayment_PostEscrowState_RefundsEntireAmount`, yanlış token `WrongTokenIncoming_AboveThreshold_QueuesWrongTokenRefund`, geç ödeme `LatePayment_AfterCancel_QueuesLatePaymentRefund_ToTheBuyersRefundAddress` — hepsi `ToAddress == BuyerRefundAddress` ve `!= FromAddress`; ücret tahmini hedefi iade adresi (`…ResolvesGasFeeForDepositToRefundAddressTransfer`, `WrongTokenIncoming_EstimatesGasFee_…`); adres yoksa beş türde de iade yok + uyarı (`Rejected_Payment_Without_A_Refund_Address_Is_Not_Sent_To_The_Sender_Admin_Is_Alerted` ×5). Gerçek SQL Server, webhook ucundan: `PaymentConfirmed_Underpayment_QueuesIncorrectAmountRefundAndBuyerEvent`; iki ağda uçtan uca fazladan transfer: `NetworkChoiceLifecycleTests` |
| 5 | Başka ağda aynı türde adres varken yeni ağa İLK adres de Steam yeniden doğrulaması ister | ✓ | `UpdateSellerWallet_FirstAddressOnSecondNetwork_WithoutReAuth_Returns403_NothingWritten`, `…_WithReAuth_Persists_AndLeavesTronUntouched`, `UpdateRefundWallet_FirstRefundAddressOnSecondNetwork_WithoutReAuth_Returns403`, `UpdateRefundWallet_FirstRefundAddressAnywhere_NeedsNoReAuth_EvenWithAPayoutAddressOnAnotherNetwork` (tür başına) |
| — | `Transaction.Network` kabulden sonra NOT NULL DB CHECK'i (T145 kararı v) | ✓ | `CleanDatabase_An_Accepted_Row_Without_Network_Or_Payout_Address_Is_Refused` (2), `CleanDatabase_T147IsApplied_TableKeysAndCheckHaveTheTargetShape` |
| — | NETWORK_UNAVAILABLE servisi olmayan ağda (Ethereum, T150'ye kadar), fail-closed | ✓ | Oluşturma ve kabul testleri yukarıda (servis + telden) |
| — | TRON akışları mevcut istemciler için aynı | ✓ | Ağ alanı vermeyen bütün mevcut oluşturma/kabul testleri (servis + uç) yeşil; `Omitted_Network_Reads_As_Tron_For_The_Pre_F8_Client`; uygunluk TRON-yalnız satıcıda aynı cevap |
| — | Migration provası (temiz DB + mevcut satırlar + Down) | ✓ | `TransactionNetworkOptionMigrationRehearsalTests` (8): temiz şema; T147 öncesine inip satırlarla tekrar yukarı (3 işlem → 3 TRON seçeneği, soft-delete dahil, kolonlar dokunulmadan); Down TRON ilanını kolona geri yazar; Ethereum ilan/işlem varken Down 50147 ile reddeder ve hiçbir şey değişmez. T145/T146 provaları T147 Down/Up'tan geçerek yeşil |

## Test Sonuçları

> Bu tablo T148 entegrasyonundan **önceki** ölçümdür (1846/1653); nihai sayılar yukarıdaki Entegrasyon > Kanıt tablosundadır: unit 1913, integration + contract 1701.

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (backend, bütün `*.Tests`) | ✓ 1846/1846 (11 assembly) | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — Transactions 702, Shared 511, Platform 139, Notifications 133, Auth 92, API 77, Steam 59, Users 49, Realtime 40, Disputes 25, Fraud 19 |
| Integration + Contract (gerçek SQL Server, Testcontainers) | ✓ 1653/1653 (10 assembly) | `dotnet test Skinora.sln -m:1` tam koşu 3499/3499; birim çıkarıldığında Integration + Contract: Transactions 706, API 584, Fraud 82, Platform 74, Notifications 60, Disputes 58, Auth 40, Admin 22, Shared 21, Payments 6. Migration provaları (T147 8/8, T146 ve T145 provaları T147 Down/Up üstünden) ve iki ağda uçtan uca test bu koşuda |
| e2e harness | ✓ | `npx tsc --noEmit`, `prettier --check` (değişen dosyalar), `eslint` (değişen dosyalar) |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` |
| Build | ✓ | `dotnet build Skinora.sln` 0 hata |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (aşağıya bkz.) |
| Bulgu sayısı | 5 (F1–F5; hiçbiri engelleyici değil) |
| Düzeltme gerekli mi | Hayır — F1–F3 ve F5 bu commit'te uygulandı |

## Doğrulama sonucu (2026-10-18)

- **PASS**, doğrulanan HEAD `48e42b1`.
- K3 / K9 / yeniden doğrulama (re-auth) mutasyon probları: bir eşdeğer sağ kalan dışında hepsi yakalandı (eşdeğer olan DB CHECK yüzünden imkânsız).
- Migration elle prova edildi: 5 tohum satır, CHECK, Down koruması (50147), temiz Down.
- Depozitin kabulde ayrılmasının sonuçları kontrol edildi: takılı kalan işlem yok.
- Sayılar: unit 1913, integration 1675, contract 26. `HangfireTests` `ObjectDisposedException` uyarısı 2026-10-01'den beri var (bu görevden bağımsız).
- Bulgular: F1 eski yorumlar, F2 eski ilan iade notu (08 §3.4 + runbook), F3 T156 kriteri, F5 rapor düzeltmeleri — bu commit'te uygulandı. F4 (EF uyarısı 10622) zararsız.
- Üç açık soru: oluşturmada 503 kalır; `BuyerRefundAddress` CHECK'e ancak üretimde sayım yapıldıktan sonra eklenir (F9 öncesi takip); uygunluk müsaitlikten bağımsız kalır.

## Altyapı Değişiklikleri

- Migration: **Var** — `20261018073432_T147_TransactionNetworkOptions` (T148'in veri migration'ının üstünde) (1 tablo + UNIQUE + FK; `Transactions.SellerPayoutAddress` NULL'a açılır; 1 CHECK; 1 veri kopyası + 1 koruma UPDATE'i; Down korumalı, 50147).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni dış bağımlılık: Yok.

## Commit & PR

- Branch: `task/T147-islem-akisinda-ag-secimi`
- Commit: `8db30f3` — T147: işlem akışında ağ seçimi (kod + testler) · `bb81ad5` — uçtan uca test, e2e, doküman, rapor · `cfdca47` — PR numarası ve test sayıları · sonraki merge commit — origin/main (T148) birleştirmesi + entegrasyon
- PR: #344
- CI: PR CI run sonucu yönetici raporunda (son commit'in run'ı izlendi; ilk run `39098588462`)

## Known Limitations / Follow-up

- **T148 ile birleşme:** yapıldı — bkz. "Entegrasyon (T148 sonrası)".
- **T149:** `networkQuotes[].networkFeeAmount` ve kabul yanıtındaki ücret satırı; TRON toplamına ağ ücreti.
- **T156:** `payment.network` makine değerine geçiş, ağ seçen arayüz (`acceptedNetworks`, `network`, yeniden onay akışının yeni ağda tetiklenmesi), "Exchange'den gönderim yapmayın" uyarısının kalkması.
- `BuyerRefundAddress` DB CHECK'e alınmadı (durum makinesi guard'ı) — ⚠ v.
- Uygunluk (07 §7.3) ağ erişilebilirliğine bakmaz: yalnız Ethereum adresi olan satıcı uygun görünür, ilan Ethereum kapalıyken 503 alır — ⚠ iv.

## Notlar

- **Working tree:** temiz (başlangıçta `git status --short` boş).
- **Main CI startup check:** son tamamlanan run'lar `39087410609` ✓ (T145 CI), `39087410574` ✓ (T145 Docker Publish), `39081681867` ✓ (T144 Docker Publish); T146 merge run'ları (`39095082123` CI, `39095082208` Docker Publish) başlangıçta sürüyordu, sonra ikisi de ✓ bitti.
- **Bağımlılık:** T146 ✓ (PR #342).
- **Dış varsayımlar:** yok (dış servis, paket ya da plan katmanı varsayımı yok). Kullanılan EIP-55 vektörleri spesifikasyondan (eips.ethereum.org/EIPS/eip-55); TRON adresleri T146'nın tronweb 5.3.5 ile üretilmiş fikstürleri. Migration komutu: `dotnet ef migrations add T147_TransactionNetworkOptions --project Skinora.Shared --startup-project Skinora.API`; çıktı elle düzenlendi (veri kopyası, koruma, Down).
- **Docker:** testler sırasında Docker Desktop bir kez kapandı (paralel T148 ajanının işi bitince kapatması olası); `docker desktop start` ile yeniden açıldı. Yerel Skinora yığını `unless-stopped` ile kalkar (beklenen). İş sonunda `docker desktop stop`.
- **Paralel T148:** PR #343 bu yapım sırasında açık kaldı (birleşmedi); T147 origin/main'den kesildi. Kesişen dosyalar: `AmountValidationService`, `PaymentAddressAllocator`, `TransactionsModule`, `TransactionLifecycleDtos`, test fikstürleri, doküman başlıkları ve migration snapshot'ı.
- **Kararlar (öneriyle kaydedildi — ⚠)** — 11 §F8 "T147 işlem akışı kararları" (i)–(xv):
  - (i) `acceptedNetworks` verilmezse `["TRON"]`, kabulde `network` verilmezse `TRON`.
  - (ii) `NETWORK_UNAVAILABLE` ilan oluşturmada da.
  - (iii) Depozit kabul anında; ayırıcı/kurtarma işi `ACCEPTED` + `SELLER_CONFIRMED`; FLAGGED onayındaki erken ayırma kaldırıldı.
  - (iv) Uygunluk "herhangi bir ağ" sorusunu cevaplar.
  - (v) CHECK ölçütü `AcceptedAt`; payout adresi dahil, iade adresi hariç.
  - (vi) Seçenek satırı append-only; migration soft-delete dahil; Down 50147.
  - (vii) İade adresi yoksa iade yok + `RefundAddressMissing` uyarısı.
  - (viii) Olaylarda `RefundAddress`; bildirim metinleri iade adresini gösterir.
  - (ix) `acceptedNetworks`/`networkQuotes` yalnız kimliği doğrulanmış görünümde; `networkFeeAmount` `"0.00"`; `payment.network` gösterim metni T156'ya kadar.
  - (x) Kabul yanıtına `network` + `totalAmount`.
  - (xi) Yeniden onay kuralı tür başına.
  - (xii) Ara `TronOnlyNetworkAvailability` (T148'e kadar) — T148 birleştirmesiyle silindi.
  - (xiii) Ayırıcı varsayılan ağ uydurmaz, başka ağdaki depoziti vermez.
  - (xiv) `activeTransactionsUsingOldAddress` seçenek satırlarını sayar.
  - (xv) Kabulde ağ kontrolleri Steam yoklamalarından önce.
