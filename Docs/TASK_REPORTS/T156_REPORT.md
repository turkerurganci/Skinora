# T156 — Kullanıcı arayüzü: ağ seçimi

**Faz:** F8 | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS (PR #350) (başlık F8 Gate Check'te bayattan düzeltildi) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Ağ kavramı istemcide:** `BlockchainNetwork` enum'u (`types/enums.ts`, C# paritesi testle), `lib/utils/blockchain.ts` — ağ etiketi (`TRON (TRC-20)` / `Ethereum (ERC-20)`), katı `parseNetwork` (tahmin yok), ağa göre gezgin (`explorerTxUrl`: Tronscan / Etherscan, `NEXT_PUBLIC_ETHERSCAN_TX_BASE_URL`), ağ başına adres biçimi ön denetimi (`checkAddressShape`: TRON `T` + 33 base58; EVM `0x` + 40 hex; tek harf durumlu EVM → `NO_CHECKSUM`).
- **Ağ kuralları tek yerde:** `lib/transactions/networkChoice.ts` — `sellerNetworkOptions` (yalnız `supportedNetworks`; ödeme adresi olmayan açık ağ seçilemez), `effectiveAcceptedNetworks`, `networkGateReason` (T147 doğrulaması), `buyerNetworkOptions`, `showsNetworkFeeLine` (K8: yalnız TRON).
- **S06 ilan sihirbazı:** adım 2'de "Kabul ettiğiniz ağlar" (onay kutuları, ödeme adresi maskeli, ağ başına ücret notu, hiçbiri seçili değilse adım geçilmez); uygunluk "eligible" olsa da açık ağda ödeme adresi yoksa / açık ağ yoksa form öncesi engel (`NetworkGate`); adım 4 ağları ve ağ başına ödeme adresini gösterir; `POST /transactions` `acceptedNetworks` taşır; `NETWORK_UNAVAILABLE` + ağa bağlı retler `details.network` ile ağ adını gösterir. Taslakta `acceptedNetworks` (opsiyonel, v2 anahtarı korunur).
- **S07 kabul formu:** ilanın ağları radyo düğmesi; tutarlar `networkQuotes`'tan birebir ("Göndereceğiniz tutar", TRON'da "TRON ağ ücreti dahil"); tek ağda önden seçili, çok ağda seçim zorunlu; iade adresi seçilen ağın profil adresi (K4 — işlem başına değiştirme kapalı), yoksa elle ve ağın biçimiyle; sağlamasız Ethereum adresinde ek onay kutusu; `network` gönderilir; `NETWORK_NOT_ACCEPTED` / `NETWORK_UNAVAILABLE` / `INVALID_WALLET_ADDRESS` ağ adıyla.
- **S07 bilgi ve ödeme blokları:** işlem bilgilerinde ağ etiketi, TRON'da ayrı "TRON ağ ücreti" satırı (Ethereum'da yok), kabulden önce "Kabul edilen ağlar" ve TRON varsa "Toplam (TRON ağ ücreti hariç)"; ödeme bloğunda belirgin "Ağ: …" etiketi, `expectedAmount` birebir, fiyat/komisyon/TRON ağ ücreti kırılımı, "Yalnız <ağ> ağında USDT gönderin", Ethereum'da aynı-0x-adresi uyarısı (08 §3.0 metnine göre: sayılmaz, otomatik iade edilmez, destek talebi), borsa uyarısı kaldırıldı; ağ okunamazsa adres gösterilmez. Yanlış token bannerı ve tüm hash bağlantıları ağa göre.
- **S08 profil:** ödeme ve iade bölümlerinde ağ başına satır; C11 ağ başına (etiket, yer tutucu, biçim, onay adımında ağ etiketi, sağlamasız Ethereum'da ek onay → `confirmLowercase: true`); Steam yeniden doğrulaması o türde herhangi bir ağda adres varsa (yeni ağdaki ilk adres dahil — `walletChangeNeedsReAuth`); dönüş URL'sinde `walletNetwork`; sunucunun `CHECKSUM_MISMATCH` cevabı ayrı mesaj.
- **Backend:** `TransactionDetailService` `payment.network` artık makine değeri (`PaymentAddress.Network`); etiket sabitleri kaldırıldı.
- **i18n (4 dil):** yeni/güncellenen ~60 anahtar; kullanım şartları, gizlilik ve ToS özetinde ağ ifadeleri (TRON veya Ethereum; TRON'da ayrı ağ ücreti, Ethereum'da platform karşılar); `untranslatable.ts`'ye `ERC-20`, `Ethereum` (04 §10.4).
- **Dağıtım:** `ETHERSCAN_TX_BASE_URL` build arg (compose, Dockerfile, .env.example, DEPLOY_RUNBOOK §B); gezgin tabanında `??` → `||` (boş build arg mainnet'e düşer).
- **Belgeler:** 02 v4.13 (§4.2, §12.2), 03 v3.13 (§3.2, §3.4, §9.1), 04 v4.10 (C11, §7.2, §7.3, §7.4), 07 v4.13 (§1.3, §7.5), 11 v1.6 ("T156 kararları" + §7.6 iki satır), DEPLOY_RUNBOOK §B.

## Etkilenen Modüller / Dosyalar

- Frontend: `src/types/enums.ts`, `src/lib/utils/blockchain.ts`, `src/lib/transactions/{networkChoice,wizardDraft}.ts`, `src/lib/api/{users,transactions}.ts`, `src/lib/i18n/dynamicKey.ts`, `src/components/common/{WalletAddressInput,index}.tsx`, `src/components/profile/WalletSection.tsx`, `src/components/transactions/new/{NewTransactionForm,Step2Details,Step4Summary,EligibilityGate}.tsx`, `src/components/transactions/detail/{AcceptForm,PaymentInfoBlock,TransactionInfoPanel,TxHashLink,PaymentEventBanners,CancelInfoBlock,SellerPayoutSummary,StateActionPanel,helpers}.ts(x)`, sayfalar (`profile`, `transactions/[id]`, `invite/[token]`, `dev/components`), `src/i18n/messages/*.json`, `src/i18n/untranslatable.ts`, `Dockerfile`.
- Yeni testler: `lib/utils/blockchain.test.ts` (genişletildi), `lib/transactions/networkChoice.test.ts`, `lib/api/users.test.ts`, `components/transactions/detail/{AcceptForm,networkDisplay}.test.tsx`, `components/profile/WalletSection.test.tsx`, `components/transactions/new/NewTransactionForm.networks.test.tsx`.
- Backend: `Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs`; testler `TransactionDetailServiceTests.cs` (+1 Ethereum testi), `TransactionLifecycleEndpointTests.cs`.
- E2E: `e2e/tests/happy-path.ui.spec.ts`.
- Kök: `docker-compose.yml`, `.env.example`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | İlan ve ödeme ekranlarında ağ seçimi, açık ağ etiketi ve yanlış ağ uyarısı (aynı 0x adresi birçok ağda geçerli) | ✓ | Sihirbaz adım 2 ağ seçimi (`NewTransactionForm.networks.test.tsx`), kabul formu ağ seçimi (`AcceptForm.test.tsx`), ödeme bloğu etiketi + `payment-network-warning` + `payment-evm-warning` (`networkDisplay.test.tsx`); e2e `happy-path.ui` iddiaları |
| 2 | Ağ başına adres girişi; Tronscan/Etherscan bağlantıları ağa göre | ✓ | `WalletSection.test.tsx` (ağ başına satır, PUT `network`), `blockchain.test.ts` + `networkDisplay.test.tsx > TxHashLink` (Etherscan/Tronscan href) |
| 3 | Satıcının ağ seçimi `supportedNetworks` ile süzülür; açık ağda ödeme adresi olmayan satıcıya uygunluk ekranında neden gösterilir (T147 doğrulaması) | ✓ | `networkChoice.test.ts` (kapalı ağ adresi olsa da sunulmuyor), `NewTransactionForm.networks.test.tsx` (form düzeyi + 4 dilde engel metni) |
| 4 | 4 dilde metinler; kullanım şartları ve gizlilik metnindeki ağ ifadeleri | ✓ | `npm run i18n:check` parity OK (1387 anahtar × 4); `legal.terms.*`, `legal.privacy.*`, `auth.tos.summary.crypto` 4 dilde güncellendi |
| — | K3: payout/iade aynı ağdan; alıcı tek ağ seçer | ✓ | Kabul isteği tek `network`; iade adresi seçilen ağın adresi (`AcceptForm.test.tsx` "TRON refund address is NOT used for an Ethereum choice") |
| — | K8: TRON ağ ücreti ayrı satır, Ethereum'da satır yok; tutarlar API'den birebir | ✓ | `networkDisplay.test.tsx` (TRON satırı; Ethereum'da 9.99 gönderilse bile satır yok; 23.30 / 20.40 birebir), `AcceptForm.test.tsx` (02 §5 örneği 23.30 / 20.40) |
| — | K9: iade alıcının ağ başına iade adresine; borsa uyarısı kalkar | ✓ | Kabul formu ipucu metni K9 kapsamını söyler; `exchange` metni yok (`networkDisplay.test.tsx`) |
| — | T146: ağ başına adres, EIP-55 onay akışı, yeni ağa ilk adreste re-auth | ✓ | `WalletSection.test.tsx` (re-auth tetikleme, `confirmLowercase: true`), `users.test.ts` |
| — | T147: `NETWORK_NOT_ACCEPTED` / `NETWORK_UNAVAILABLE` | ✓ | `AcceptForm.test.tsx` (503 ağ adıyla), sihirbaz adım 4 `NETWORK_UNAVAILABLE` (POST_ERROR_CODES + 4 dil, mevcut parity testi) |
| — | T149: `networkQuotes`, `networkFeeAmount`, kabul cevabı | ✓ | Tipler + kabul formu ve bilgi paneli |
| — | 07 §1.3: `payment.network` makine değeri | ✓ (CI) | `TransactionDetailServiceTests.Payment_Network_Is_The_Machine_Value_Of_The_Deposit_Network` (Ethereum + EIP-55), mevcut iki iddia `"TRON"`'a çevrildi — Testcontainers gerektirdiği için CI'da koşar |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Frontend unit (vitest) | ✓ 352/352 | `npx vitest run --maxWorkers=3` — 36 dosya |
| Frontend tsc / lint / prettier / i18n | ✓ | `npx tsc --noEmit`, `npm run lint`, `npm run format:check`, `npm run i18n:check` (parity OK; yeni uyarı yok) |
| Frontend build | ✓ | `npm run build` |
| Backend build + format | ✓ | `dotnet build Skinora.sln` (0 hata), `dotnet format --verify-no-changes --severity error` |
| Backend entegrasyon | CI | Yerelde Docker kapalı (Testcontainers) — CI "4. Integration test" |
| E2E typecheck/format/lint | ✓ | `e2e`: `npx tsc --noEmit`, prettier, eslint |
| E2E koşu | CI (advisory) | `happy-path.ui` CI e2e matrisinde |

### Bozma denemeleri (16/16 yakalandı)

Her bozma kaynakta uygulandı, ilgili 7 test dosyası (60 test) koşuldu, dosya geri yüklendi.

| # | Bozma | Kırmızı |
|---|---|---|
| M1 | `sellerNetworkOptions` `supportedNetworks` filtresi kaldırıldı | 9 |
| M2 | Form `supportedNetworks` yerine tüm ağları veriyor | 6 |
| M3 | Ücret satırı kuralı ağdan bağımsız (her ağda satır) | 4 |
| M4 | Ödeme bloğu kuralı atlayıp `networkFeeAmount`'u gösteriyor | 1 |
| M5 | TRON'da ücret satırı gizlendi | 4 |
| M6 | Tek harfli (sağlamasız) EVM adresi `OK` sayılıyor | 3 |
| M7 | EVM uzunluk kontrolü gevşedi (`{40,}`) | 1 |
| M8 | Ethereum satırı TRON biçimini kabul ediyor | 3 |
| M9 | Re-auth yalnız aynı ağda adres varsa (yeni ağdaki ilk adres onaysız) | 2 |
| M10 | Re-auth hiç tetiklenmiyor | 4 |
| M11 | Re-auth tür ayrımı yok (iade adresi ödeme re-auth'unu tetikliyor) | 2 |
| M12 | Kabulde iade adresi ağdan bağımsız profilden | 1 |
| M13 | Çok ağda ilk ağ önden seçili | 1 |
| M14 | Gezgin ağdan bağımsız hep Tronscan | 3 |
| M15 | İlan gövdesinde `acceptedNetworks` yok | 1 |
| M16 | Kapalı ağdaki tek adres kapısı kaldırıldı | 5 |

Backend `payment.network` bozması (etikete geri dönüş) yerelde koşulamadı (Testcontainers/Docker kapalı); yeni test etiketi `"ETHEREUM"`/`"TRON"` ile karşılaştırdığı için CI'da kırmızı olur.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | PASS (bağımsız temiz bağlam doğrulayıcı, Sonnet) |
| Bulgu sayısı | 2 |
| Düzeltme gerekli mi | Evet — ikisi de bu kapanışta kapandı |

## Doğrulama sonucu

**PASS.** Bağımsız, temiz bağlamlı doğrulayıcı (Sonnet); doğrulanan HEAD `905d521`, CI `39137342419` ✓. Bozma 16/16 (her bozma en az bir testi kırdı). Dört dil dosyası 1387 anahtarla tam.

**Bulgular (ikisi de kapandı):**
1. `Docs/07_API_DESIGN.md` §1.3 satır 54: "`payment.network` yalnız gösterim metnidir (`"Tron (TRC-20)"`)" cümlesi eskiydi — makine değeri (`"TRON"` / `"ETHEREUM"`) olarak düzeltildi.
2. `confirmLowercase` sunucu boşluğu (yapım kararı iv): kabul ucu sağlamasız Ethereum iade adresini onaysız kabul ediyordu. **Yönetici kararıyla kapatıldı** (11 §F8 "T156 kararları" (xiii), GÜVENLİK ⚠):
   - `AcceptTransactionRequest` `bool? ConfirmLowercase` aldı; `TransactionAcceptanceService` sağlamasız Ethereum adresini `confirmLowercase: true` olmadan 400 `INVALID_WALLET_ADDRESS` + `details { network, reason: "CHECKSUM_CONFIRMATION_REQUIRED", requiresConfirmation: true }` ile reddeder (T146 profil düzeniyle birebir; durum değişikliğinden ve depozit tahsisinden önce). TRON'da alan yok sayılır.
   - `AcceptForm.tsx`: kutu işaretliyken gövdeye `confirmLowercase: true`; sunucu 400 `CHECKSUM_CONFIRMATION_REQUIRED` dönerse kutu gösterilir; profil varsayılanı sağlamasızsa kutu onda da gösterilir (sunucu artık onay ister).
   - Testler: backend entegrasyon (küçük/büyük harf onaysız → 400 ve hiçbir yazma yok; onaylı → kabul, kanonik saklanır; EIP-55 doğru adres onaysız kabul; TRON'da alan yok sayılır) + birim (gövde okuma); frontend 3 yeni test (gövdede alan, EIP-55/TRON'da alan yok, sunucu 400 → kutu). Frontend bozma: gövdeden `confirmLowercase` çıkarılınca 2 test kırıldı. Backend entegrasyon testleri Docker gerektirir, yerelde koşulmadı (CI).
   - 07 §7.6 kabul ucuna alan ve hata cevabı yazıldı.

**Doğrulanamayanlar:** gerçek tarayıcıda görsel kontrol; canlı gezgin (Etherscan/Tronscan) bağlantıları. **Main bütünleştirmesi:** `origin/main` (T151 `0fdc3b1` dahil) dala merge commit'iyle alındı; belge çakışmalarında iki taraf korundu (07 v4.14, 11 v1.9).

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Var — `ETHERSCAN_TX_BASE_URL` (frontend build-time, boş = mainnet `etherscan.io`); gezgin tabanı boş değerde artık mainnet'e düşer
- Docker değişikliği: Var — frontend Dockerfile'a `NEXT_PUBLIC_ETHERSCAN_TX_BASE_URL` ARG/ENV, compose `build.args`

## Commit & PR

- Branch: `task/T156-arayuz-ag-secimi`
- Commit: `cf9ecd1` — T156: Kullanıcı arayüzü — ağ seçimi
- PR: #350
- CI: kod commit'i `cf9ecd1` üzerinde run `39135303415` ✓ success (Lint, Build, Unit, Integration, Contract, Migration dry-run, Docker build, vitest, 10 E2E bacağının hepsi — `happy-path UI` dahil — yeşil, CI Gate ✓). Rapor commit'inin run'ı PR'da.

## Known Limitations / Follow-up

- ~~Kabulde EIP-55 ikinci onayı yalnız istemcide~~ — doğrulamada yönetici kararıyla kapandı: sunucu da `confirmLowercase` ister (karar xiii, bkz. "Doğrulama sonucu").
- USDC (stablecoin toggle'ı, hukuki metinlerdeki "USDT, USDC") T166'da.
- Admin işlem detayındaki Tronscan bağlantısı ve admin ekranlarının ağ başına görünümü T157'de.
- Kabul formu ağları `supportedNetworks` ile süzmüyor; kapalı ağ seçimi 503 ile ağ adıyla döner (T156 kararı v).
- Ağ başına ödeme adresi cooldown'u sihirbazda önden gösterilmiyor (süre istemcide yok); adım 4'te 422 ağ adıyla.
- C06 iptal modalındaki "(fiyat + komisyon + TRON ağ ücreti − gas fee)" metni (04 §C06 madde 3) bugünkü arayüzde hiç render edilmiyor (modal yalnız satıcı uyarısını taşıyor); T156 kapsamında eklenmedi.
- Bildirim metinleri (backend tarafında üretilen) ağ adı taşımıyor — kapsam dışı.

## Notlar

- **Working tree:** temiz (dal `origin/main` `dc902b3`'ten kesildi).
- **Main CI startup check:** son üç tamamlanmış run `39130571895` (Docker Publish, T149) ✓, `39124319255` (Docker Publish, T150) ✓, `39124319215` (CI, T150) ✓; başlangıçta `39130571903` (CI, T149) sürüyordu, sonra ✓.
- **Dış varsayımlar:**
  - Sunucu sözleşmesi (T146/T147/T149) — `GET /users/me` `wallets[]`, `GET /transactions/params` `supportedNetworks`, `GET /transactions/:id` `acceptedNetworks`/`networkQuotes`/`network`/`networkFeeAmount`, kabul `network`, PUT wallet `network`/`confirmLowercase`/`details.reason`: 07 §1.3, §5.1, §5.3, §7.4–§7.6 ve kodda (`IWalletAddressValidator`, `TransactionDetailDto`, `TransactionAcceptanceService`) okundu.
  - Kabul ucu `confirmLowercase` almıyor ve `ValidWithoutChecksum`'u kabul ediyor — `TransactionAcceptanceService.cs` Stage 4 (`refundValidation.IsUsable`) okundu; bu yüzden ikinci onay istemcide.
  - Re-auth dönüşü sorgu parametresi ekleyebilir — `AuthController.BuildReVerifyRedirect` (`?`/`&` ayırıcı) ve `ReturnUrlValidator` (yalnız `/` ile başlayan göreli yol) okundu; `walletNetwork` korunur.
  - Etherscan adresi `https://etherscan.io/tx/<hash>`, Sepolia `https://sepolia.etherscan.io/tx/<hash>` — bilinen kamu gezgin biçimi (T142 ölçüm raporu Sepolia'yı kullanıyor).
  - Yeni npm bağımlılığı yok.
- **Yönetici talimatındaki uyarı metni:** talimatta "yanlış ağa gönderilen para kurtarılmaz" diye özetlenen uyarı, "belgelerde nasıl geçiyorsa" kuralıyla 08 §3.0'a göre yazıldı: para kaybolmaz, otomatik kurtarma/iade yoktur, destek talebi gerekir. "Kurtarılmaz" denmedi.
- **Status tablosu:** paralel görevlerle çakışmayı azaltmak için yalnız T156 satırı güncellendi; üstteki "Son güncelleme" paragrafı birleştirmede yöneticiye bırakıldı.
- Yapımcı kararları 11 §F8 "T156 kararları" bloğunda (i)–(xii), hepsi `(öneriyle kaydedildi — ⚠)`.
