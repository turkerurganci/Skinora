# T143 — HD türetme yolunun standarda çekilmesi [RİSKLİ]

**Faz:** F8 | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS (PR #338) (başlık F8 Gate Check'te bayattan düzeltildi) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Kusur ölçüldü ve kapatıldı.** `HdWalletService.getRoot()` kökü `HDNodeWallet.fromMnemonic(mnemonic)` ile kuruyordu. ethers v6 (6.17.0) yol verilmeyen bu çağrıda `defaultPath`'i uygular ve **derinliği 5** olan `m/44'/60'/0'/0/0` düğümünü döndürür (ölçüm: `node -e` ile `depth 5, path m/44'/60'/0'/0/0`). Kod göreli `44'/195'/0'/0/{i}` yolunu onun altına ekliyordu; gerçek yol `m/44'/60'/0'/0/0/44'/195'/0'/0/{i}` idi, `derivationPath` cevabı ise standart yolu bildiriyordu.
- **Düzeltme (`sidecar-blockchain/src/wallet/HdWalletService.ts`):**
  - Kök `HDNodeWallet.fromMnemonic(mnemonic, 'm')` ile kurulur (ethers `"m"` yolunda kökün kendisini döndürür).
  - `assertMasterNode`: kökün derinliği 0 ve yolu `m` değilse `HD_ROOT_NOT_MASTER` (kalıcı hata). Bu kontrol `getRoot`'ta `try` dışında — kod kusuru `INVALID_MASTER_MNEMONIC` diye raporlanmaz.
  - `derive` ve `deriveSigner` tek bir `deriveKey` fonksiyonundan geçer: türetme **mutlak** yolla (`derivationPath(index)` dizisinin kendisi) yapılır, ethers mutlak yolda kökün derinliği 0 değilse zaten reddeder; ek olarak düğümün kayıtlı `path`'i istenen yolla karşılaştırılır (`DERIVATION_FAILED`). Böylece backend'e dönen `derivationPath` fiilen kullanılan yoldur ve kayıtlı adres ile onu imzalayan anahtar iki farklı yoldan gelemez.
  - İndeks üst sınırı `MAX_DERIVATION_INDEX = 2^31 − 1` (sertleştirilmemiş aralık). Üstü `INVALID_DERIVATION_INDEX` → `/api/wallet/derive` 400. Eskiden ethers'ın jenerik hatası 500 `INTERNAL_ERROR` olurdu.
  - `masterNodeFromPhrase` dışa açıldı (Ethereum yolu vektör testi ve T150'nin kullanması için).
- **Bilinen-cevap vektörleri bağımsız uygulamadan üretildi** (aşağıda "Bağımsız üretici").
- **Backend test literali:** `HttpBlockchainSidecarClientTests` stub cevabında eski yolun indeks-0 adresi (`TV54Fr…`) standart yol dizisiyle eşleştirilmişti; yeni indeks-0 adresine (`TUEZSd…`) çekildi.
- **Doküman:** 08 §3.2 ("bugünkü sapma" notu kaldırıldı, düzeltmenin kaydı + koruma + vektör yeri yazıldı) ve §3.7.2 vektör satırı (v3.18) · 05 §3.3 key management satırı (v3.11) · `secrets/README.md` türetme yolu satırı · `DEPLOY_RUNBOOK` yeni **§G.0b** (Nile ortamı sıfırlama) + §G.0 satırına yönlendirme · sidecar `config/index.ts` yorumu · `.claude/memory/reference_local_payment_leg_nile.md` (indeks-0 doğrulama adresi değişti).

### Bağımsız üretici

- **Kütüphaneler:** `@scure/bip39` 2.4.0, `@scure/bip32` 2.4.0, `@noble/curves` 2.4.0, `@noble/hashes` 2.4.0, `@scure/base` 2.4.0 (npm, 2026-08-27/28 yayınları), Node 24.12.0. Repo dışında bir geçici klasörde kuruldu; **ethers ve TronWeb kullanılmadı**.
- **Yöntem:** (1) `seed = mnemonicToSeedSync(mnemonic, '')` → `5eb00bbddcf06908…ce9e38e4`; (2) `master = HDKey.fromMasterSeed(seed)`, `master.depth === 0`; (3) `child = master.derive(path)`; (4) `pub = secp256k1.getPublicKey(child.privateKey, false)` (65 bayt); (5) `a20 = keccak_256(pub.slice(1)).slice(-20)`; (6) TRON: `createBase58check(sha256).encode(0x41 ‖ a20)`, Ethereum: `0x` + hex + EIP-55.
- **Çapraz doğrulama:** aynı üretici iç içe eski yolda (`m/44'/60'/0'/0/0/44'/195'/0'/0/{i}`) eski test dosyasının beş adresini **birebir** üretti — eski vektörlerin standart yola değil iç içe yola ait olduğunun kanıtı. Ethereum indeks 0/1 (`0x9858EfFD…`, `0x6Fac4D18…`) bu mnemonic için yaygın yayımlanmış değerlerdir; düzeltilmiş ethers yolu da aynı değerleri verdi.

| İndeks | TRON `m/44'/195'/0'/0/i` | Ethereum `m/44'/60'/0'/0/i` |
|---|---|---|
| 0 | `TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH` | `0x9858EfFD232B4033E47d90003D41EC34EcaEda94` |
| 1 | `TSeJkUh4Qv67VNFwY8LaAxERygNdy6NQZK` | `0x6Fac4D18c912343BF86fa7049364Dd4E424Ab9C0` |
| 2 | `TYJPRrdB5APNeRs4R7fYZSwW3TcrTKw2gx` | `0xb6716976A3ebe8D39aCEB04372f22Ff8e6802D7A` |
| 3 | `TRhVWK5XEDkQBDevcdCWW7RW51aRncty4W` | `0xF3f50213C1d2e255e4B2bAD430F8A38EEF8D718E` |
| 4 | `TT2X2yyubp7qpAWYYNE5JQWBtoZ7ikQFsY` | `0x51cA8ff9f1C0a99f88E86B8112eA3237F55374cA` |
| 1.000.000 | `TLyD19fUG6pKu6yRQNgH3TKnnoBJb1xVH4` | `0x99228504058ca9c46Cdc0cA00E7BC323454fEd2b` |
| 2^31 − 1 | `TVapZk5c7S7ARtSBi9jD13rhkbZke4F4sa` | `0x8848bfC75a28756B521b09afDC120BdDddC7d7c9` |

Mnemonic: BIP-39 referans "abandon ×11 about", parola boş. İndeks-0 TRON özel anahtarının ilk 16 hanesi (`b5a4cea271ff424d`) da testte; tam 64 haneli anahtar pre-commit sır kapısına takıldığı için yazılmadı (adres kontrolleri anahtarı zaten sabitliyor).

## Etkilenen Modüller / Dosyalar

- `sidecar-blockchain/src/wallet/HdWalletService.ts` — kök, koruma, tek türetme yolu, indeks sınırı
- `sidecar-blockchain/src/wallet/HdWalletService.test.ts` — bağımsız vektörler (iki ağ), regresyon testleri
- `sidecar-blockchain/src/config/index.ts` — yorum
- `backend/tests/Skinora.Transactions.Tests/Unit/PaymentAddresses/HttpBlockchainSidecarClientTests.cs` — stub adres literali (4 yer)
- `Docs/08_INTEGRATION_SPEC.md` (v3.18), `Docs/05_TECHNICAL_ARCHITECTURE.md` (v3.11), `Docs/DEPLOY_RUNBOOK.md`, `secrets/README.md`
- `.claude/memory/reference_local_payment_leg_nile.md`, `.claude/memory/MEMORY.md`, `Docs/IMPLEMENTATION_STATUS.md`, bu rapor

**Dokunulmayan fikstürler ve nedeni:** `sidecar-fake/src/routes/blockchain.ts` sahte adres üretir (`fakeTronAddress`), yol dizisi zaten standart; `sidecar-blockchain/contracts/*.json` adresleri bu mnemonic'ten türemiş değil; backend `StubBlockchainSidecarClient` / `PaymentAddressAllocatorTests` sahte adres taşır. Depozitten imzalayan yollar (`TransferService.sweep`, `RefundService.refund`) `deriveSigner`'ı ve `DEPOSIT_ADDRESS_MISMATCH` korumasını değişmeden kullanır; testleri (`TransferService.test.ts` uyuşmazlık testi dahil) tam süitte geçti.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Depozit anahtarları belgelenen standart yoldan türüyor (TRON `m/44'/195'/0'/0/{i}`) | ✓ | Kök `fromMnemonic(m, 'm')` + mutlak yol; `derive`/`deriveSigner` testleri bağımsız vektörlerle 7 indekste geçiyor; `derivationPath` = düğümün kayıtlı yolu (`deriveKey` karşılaştırması). Eski uygulamaya karşı 17 test kırmızı |
| 2 | Test vektörleri koddan değil bağımsız bir uygulamadan üretildi, kaynak testte yazılı | ✓ | `@scure/*` + `@noble/*` 2.4.0; sürüm ve yöntem `HdWalletService.test.ts` başındaki yorumda |
| 3 | Ethereum yolu `m/44'/60'/0'/0/{i}` (ayrı tohum, K7); bilinen-cevap vektörü bağımsız uygulamadan | ✓ | `masterNodeFromPhrase(...).derivePath("m/44'/60'/0'/0/i")` 7 indekste bağımsız EIP-55 adresleriyle eşleşiyor. Ayrı tohum ve servis T150'nin işi (K7); burada paylaşılan kök primitifinin Ethereum yolunda doğruluğu sabitlendi |
| 4 | Nile verisi sıfırlama adımı runbook'ta | ✓ | `DEPLOY_RUNBOOK` §G.0b: envanter → isteğe bağlı boşaltma (eski yol tarifi) → imaj → veritabanı sıfırlama (24 sa ödeme adresi beklemesi uyarısıyla) → yeni yol doğrulaması → sıcak cüzdan/kilit hesabı ölçümü |
| 5 | `secrets/README.md` türetme yolu satırı düzeltilen gerçek yolla aynı | ✓ | Satır: BIP-32 kökünden `m/44'/195'/0'/0/{index}`, parola boş; T143 öncesi yol ve §G.0b yönlendirmesi |
| — | Kök derinliği 0 korumasının testi (görev girdisi) | ✓ | `master node (T143 regression guard)` describe bloğu: yolsuz `fromMnemonic` → derinlik 5 ve `assertMasterNode` reddediyor; `masterNodeFromPhrase` derinlik 0 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (sidecar-blockchain) | ✓ 506/506 | `npm test` → 26 dosya; `HdWalletService.test.ts` 16 → 37 test |
| Fark ölçümü | ✓ 17 kırmızı | Yeni test dosyası eski `HdWalletService.ts`'e (yalnız yeni dışa açılan sembollerin şimleriyle) karşı koşuldu: 17 başarısız / 20 geçti — TRON vektörleri (14), iç içe yol regresyonu, indeks-0 anahtar öneki, 2^31 sınırı |
| Typecheck / build / lint / format | ✓ | `npx tsc --noEmit`, `npm run build`, `npm run lint`, `prettier --check --end-of-line auto "src/**/*.ts"` temiz (düz `--check`'in 61 dosya uyarısı yerel CRLF artifaktı; CI LF'de koşar) |
| Unit (backend, dokunulan sınıf) | ✓ 25/25 | `dotnet test tests/Skinora.Transactions.Tests --filter FullyQualifiedName~HttpBlockchainSidecarClientTests` |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (F8 yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Doğrulama sonucu (2026-10-18)

- **✓ PASS** (head `cf99cdb`, temiz bağlamlı doğrulama alt ajanı). Üçüncü, bağımsız bir uygulamayla (yalnız `node:crypto` + elle Keccak/base58check/EIP-55) 14 vektörün 14'ü eşleşti; eski iç içe yolda main'in 5 eski vektörü de üretildi (kusur doğrulandı). Mutasyon probu: 8 bozma öldürüldü, 4 eşdeğer yaşadı. Sidecar 506/506, backend 25/25. Sıcak cüzdan ve kilit hesabı bu tohumdan türemiyor (600 adres tarandı).
- **Bloke etmeyen bulgular T150'ye devredildi:** (1) `masterNodeFromPhrase` yalnız testlerde kullanılıyor, `getRoot` aynı mantığı ayrıca kuruyor — Ethereum vektörleri üretim yolunu sınamıyor; (2) `getRoot` içindeki `assertMasterNode` çağrısı servis üzerinden test edilmiyor. T150 kabul kriterine eklendi.

## Altyapı Değişiklikleri

- Migration: Yok (`PaymentAddress` türetme yolunu saklamıyor; yalnız adres + indeks)
- Config/env değişikliği: Yok (aynı `HD_WALLET_MNEMONIC`; ancak aynı tohum artık başka adresler verir — §G.0b)
- Docker değişikliği: Yok
- Yeni bağımlılık: Yok (bağımsız üretici repo dışında koştu)

## Commit & PR

- Branch: `task/T143-hd-turetme-yolu`
- Commit: `5557437` — T143: HD türetme yolu standarda çekildi — TRON depozitleri BIP-32 kökünden m/44'/195'/0'/0/{i}
- PR: #338
- CI: rapor commit'inden sonra izleniyor (sonuç PR'da)

## Known Limitations / Follow-up

- **Nile'daki eski depozitler koddan erişilemez.** Bu makinedeki `.env` tohumuyla eski indeks 0 = `TF6nQDf6…` (2026-09-02 provasından bakiye taşıyor: hafıza 2 USDT + 15 TRX diyor, güncel bakiye ölçülmedi), test depozitleri 9001/9003/9004/9005 (9005'te ~1,6 TRX). Yeni indeks 0 = `TQSxAMivyMEof6GEGW8nvXCbrTzsKhZYhJ`. Para Nile test değeri; boşaltmak ya da bırakmak operatörün kararı (§G.0b adım 2). **Yerel ortamda §G.0b henüz koşulmadı** — merge sonrası T143 imajına geçerken koşulmalı.
- **Sıfırlama bedeli:** yerel veritabanı sıfırlanınca kullanıcılar/ayarlar gider ve satıcı ödeme adresi 24 saatlik beklemeyi yeniden başlatır; bir sonraki canlı prova planına girdi.
- **Sıcak cüzdan ve kilit hesabı değişmez:** `HOT_WALLET_ADDRESS` (`TP6e9Yqa…`) ve `STAKE_ACCOUNT_ADDRESS` (`TVqvXQ…`) tohumun eski/yeni yolunda indeks 0–50, 9001–9005 ve iki kök düğümün hiçbirine eşit değil (salt okunur betik; sır ekrana basılmadı).
- **Ethereum tarafı:** bu görev yalnız paylaşılan kök primitifini Ethereum yolunda sabitledi. Ethereum servisi (T150) ayrı konteyner ve ayrı tohumla kendi türetmesini yazacak; o servisin testi aynı vektörleri (08 §3.7.2) kullanmalı.
- **11 §7.6 izlenebilirlik satırı** ("TRON HD yolu … Ethereum ayrı tohum", T143 + T150) "Implemented" sütunu boş bırakıldı — satırın Ethereum yarısı T150'de.

## Notlar

- **Working tree:** temiz (worktree `agent-a9776211d61e71f2c`, başlangıç `6922b03`).
- **Main CI startup check (2026-10-18):** son 3 tamamlanmış main run'ı `39070845422` CI ✓ success (`6922b03`), `39070845359` Docker Publish ✓ success (`6922b03`), `39063106414` CI ✓ success (`2e70a03`).
- **Bağımlılık:** T141 ✓ Tamamlandı (IMPLEMENTATION_STATUS).
- **Dış varsayımlar:**
  - *ethers v6 `fromMnemonic` yolsuz çağrıda varsayılan yolu uygular* — `node_modules/ethers/lib.esm/wallet/hdwallet.js:311-315` (`if (!path) path = defaultPath`) ve çalışan ölçüm: derinlik 5, yol `m/44'/60'/0'/0/0`. `fromMnemonic(m, 'm')` → derinlik 0, yol `m`; mutlak yol derinlik-5 düğümde `cannot derive root path … non-zero depth 5` hatası verir (ölçüldü).
  - *Bağımsız kütüphane sürümleri var ve yeterince eski* — `npm view <pkg> time`: beşi de 2.4.0, 2026-08-27/28 yayını.
  - *Standart TRON indeks-0 adresi* — görevde verilen `TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH` bağımsız üreticiyle yeniden türetildi ve eşleşti.
  - *Mainnet'te eski yoldan açılmış adres yok* — canlıya çıkış F9'da (11 §F9); yalnız Nile etkileniyor.
  - *Sıcak cüzdan HD tohumundan türemiyor* — yukarıdaki salt okunur karşılaştırma.
- **Paralel görev:** T142 (ölçüm) başka bir yapımcıda; T142 satırlarına ve 08 §3.7 ölçüm yer tutucularına dokunulmadı. 08'in başlık satırı (v3.18) ve IMPLEMENTATION_STATUS/MEMORY "Current Status" satırları T142 dalıyla metin çakışması verebilir — içerik çakışması yok. IMPLEMENTATION_STATUS'un en üstteki "Son güncelleme" satırı (zaten T141'den bayat) yöneticiye bırakıldı; yalnız T143 satırı güncellendi.
- **Pre-commit sır kapısı:** ilk commit denemesi `deriveSigner`'ın dönüş nesnesinde (özel anahtar alanına `…Hex` adlı değişkenin atandığı satır) yanlış pozitif verdi; kapı atlatılmadı, değişken adı `keyHex` yapıldı.
