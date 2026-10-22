# T163 — İmza sırları Docker secret + çevrimdışı yedek

**Faz:** F9 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-10-21 (kapanış 2026-10-22)

---

## Yapılan İşler

- **TRON servisinin imza sırları Docker secret oldu** (DEFERRED_BACKLOG `SigningSecretsInPlainEnv`). Tohum ve sıcak cüzdan anahtarı `/run/secrets/tron_hd_mnemonic` ve `/run/secrets/tron_hot_wallet_private_key` dosyalarından okunur (`sidecar-blockchain/src/config/signingSecrets.ts`; Ethereum servisinin T150 deseni, `*_FILE` yol geçersiz kılma dahil). Compose iki dosyayı yalnız `skinora-blockchain-sidecar`'a bağlar; deneme yayını overlay'i `secrets/trial/` altından bağlar.
- **Env yolu üretimde kapalı.** `HD_WALLET_MNEMONIC` / `HOT_WALLET_PRIVATE_KEY` yalnız `NODE_ENV` **tam** `development` ya da `test` iken okunur. Başka her değerde (üretim, boş, `dev`, `Development`, `staging`) dolu değişken servisi durdurur: `SIGNING_SECRET_IN_ENV`. Dosya + değişken birlikte: `SIGNING_SECRET_AMBIGUOUS`. Compose iki değişkeni artık hiç geçmez; `.env.example`'dan kalktı.
- **Yeni açılış kuralları (fail-closed):** sıcak anahtar `HOT_WALLET_ADDRESS`'i kontrol etmiyorsa `HOT_WALLET_ADDRESS_MISMATCH` (öneriyle kaydedildi — ⚠ PARA; Ethereum servisinin kuralı), 64 hex değilse `INVALID_SIGNING_KEY`, okunamayan secret `SIGNING_SECRET_UNREADABLE`. Ethereum servisinde aynı durum artık `SECRET_UNREADABLE` (önceden adsız `STARTUP_FAILED`).
- **Sırlar `config` nesnesinden çıkarıldı** (`config.hdWalletMnemonic` / `config.hotWalletPrivateKey` yok); giriş noktası sırları bir kez okuyup imzalayan servislere verir. `WalletManager` artık ortam okumaz, tohumu enjekte alır. Açılış satırı yalnız **kaynak** ve **açık adres** yazar: "Signing secrets" satırı (`hdMnemonicSource`, `hotWalletKeySource`, `hotWalletAddressFromKey`) ve "HD wallet initialized" satırı (`depositIndex0`).
- **Çevrimdışı yedek betiği** `scripts/wallet-backup/`: `wallet-backup.sh backup|verify|restore` + `wallet-manifest.mjs`. GnuPG simetrik AES-256 (S2K SHA-512, 65.011.712 yineleme, MDC bütünlük), geçici `GNUPGHOME`, parola ekrana yazılmadan okunur ve gpg'ye fd ile verilir, açık metin yalnız geçici özel dizinde ve `shred` ile silinir. Arşivde `MANIFEST.json`: dosya SHA-256'ları + türetilen açık adresler (tohum başına 5 depozit, sıcak, yakıt). Yedek alındıktan sonra parola ikinci kez istenip arşiv geri açılır ve kaynakla karşılaştırılır. `--label testnet|mainnet` zorunlu; restore etiketi tutmayan arşivi ve farklı dolu dosyanın üstüne yazmayı reddeder (`--force` hariç), Docker'ın açtığı boş dizinleri kaldırır, geri yüklenen dizinden adresleri yeniden türetip karşılaştırır.
- **Doküman:** DEPLOY_RUNBOOK yeni **§L** (sırlar, araç, parola, kim/ne zaman, ortam ve saklama, testnet/mainnet ayrımı, geri yükleme, prova, sınırlar) + §0/§B/§G.1/§K.3/§K.4; 05 §3.3 "Master key saklama" / "Signing izolasyonu" ve §3.5 bugünkü gerçeğe çekildi, **KMS/HSM tetiği** (eşik T169'da sahibin); DOC_CHANGELOG; `secrets/README.md` (TRON dosyaları, eksik dosya tuzağı, `.env`'den geçiş); DEFERRED_BACKLOG `SigningSecretsInPlainEnv` ✅ + yeni 🟡 `SharedSecretsInPlainEnv`.
- **Ortak sırlar değerlendirildi** (JWT, iki webhook sırrı, üç iç anahtar): bu turda taşınmadı, gerekçeyle backlog'da (aşağıda "Kararlar").
- **CI yol filtreleri:** sidecar-blockchain ← `docker-compose.yml` (compose wiring testi okuyor), sidecar-ethereum ← `scripts/wallet-backup/**` (yedek testi orada koşuyor).

## Etkilenen Modüller / Dosyalar

- `sidecar-blockchain/src/config/signingSecrets.ts` (yeni), `secretFile.ts` (yeni, Ethereum'unkinin kopyası), `config/index.ts`, `src/index.ts`, `wallet/WalletManager.ts`
- `sidecar-blockchain/src/config/signingSecrets.test.ts` (yeni, 20), `signingSecretsStartup.test.ts` (yeni, 9), `composeWiring.test.ts` (+7)
- `sidecar-ethereum/src/config/index.ts`, `index.test.ts` (+3), `composeWiring.test.ts`, `walletBackupScript.test.ts` (yeni, 6)
- `scripts/wallet-backup/wallet-backup.sh`, `scripts/wallet-backup/wallet-manifest.mjs` (yeni)
- `docker-compose.yml`, `docker-compose.trial.yml`, `.env.example`, `.github/workflows/ci.yml` (yol filtreleri)
- `secrets/README.md`, `Docs/DEPLOY_RUNBOOK.md`, `Docs/05_TECHNICAL_ARCHITECTURE.md` (v3.32 — merge'de T160c v3.31 ile çakıştı), `Docs/plan/11_F9.md` (v1.22, kapanış), `Docs/DOC_CHANGELOG.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1a | TRON `HD_WALLET_MNEMONIC` ve `HOT_WALLET_PRIVATE_KEY` düz env yerine Docker secret (dosya) ile okunuyor | ✓ | `signingSecrets.ts`; compose secret listesi (`tron_hd_mnemonic`, `tron_hot_wallet_private_key`) yalnız blockchain sidecar'da (`composeWiring.test.ts` TRON: 4 test + diğer 4 servis almıyor; Ethereum testi blockchain sidecar'ın yalnız TRON secret'ı bağladığını sabitler). Prova: konteyner env'inde `MNEMONIC`/`PRIVATE_KEY` adlı değişken **0**, mount'lar `/run/secrets/tron_*` |
| 1b | Env yolu üretim/deneme ortamında kapalı ya da yalnız geliştirme için açıkça işaretli | ✓ | Kapalı: `NODE_ENV` ≠ `development`/`test` iken dolu değişken → `SIGNING_SECRET_IN_ENV` (unit 6 mod × 2 değişken; süreç testi üç durum). Deneme yayını overlay'i `NODE_ENV=production` zorlar. Compose değişkeni geçmez (`composeWiring.test.ts` "carries no signing material as a plain environment variable") |
| 1c | Sır log'a / hata mesajına / health ucuna sızmıyor (testle) | ✓ | `signingSecretsStartup.test.ts`: üretim modunda süreç başlatılır — açılış çıktısı, altı ret durumunun çıktısı, BIP-39 sağlaması bozuk tohum, `/health`, `/api/wallet/derive` 200/400/401 cevapları tohum ve anahtarı içermez. Unit: her ret mesajı değeri içermez. Prova: 5 sır dosyasının içeriği servis log'larında, `docker inspect` çıktısında ve iki `/health` cevabında **0** |
| 1d | JWT / webhook / iç anahtar için değerlendirme: ucuzsa taşı, değilse gerekçeyle backlog (⚠ GÜVENLİK) | ✓ (değerlendirildi, backlog) | DEFERRED_BACKLOG `SharedSecretsInPlainEnv` 🟡 (hedef T171) — gerekçe "Kararlar" K3 |
| 2 | İki ağın tohum ve anahtarları için çevrimdışı yedek prosedürü yazılı (DEPLOY_RUNBOOK yeni bölüm) + betik (ekrana sır yazmadan şifreli arşiv + doğrulama özeti) | ✓ | DEPLOY_RUNBOOK §L.1–§L.8 (araç, parola, kim/ne zaman/nerede, testnet/mainnet ayrımı); `scripts/wallet-backup/` (SHA-256 manifestte, adres türetme kontrolü her komutta); `walletBackupScript.test.ts` her komutun çıktısında tohum/anahtar/parola olmadığını doğrular |
| 3 | Geri yükleme denendi: yedek → sil → geri yükle → servis açılıyor ve türetilen adresler aynı (TRON + Ethereum; sıcak/yakıt + birkaç depozit) | ✓ | §"Geri yükleme provası" aşağıda + DEPLOY_RUNBOOK §L.7; CI'da küçük hâli `walletBackupScript.test.ts` |
| — | Sahibin seçmesi gereken noktalar açık soru olarak listelendi | ✓ | "Proje sahibine açık sorular" aşağıda |

### Geri yükleme provası (2026-10-21, bu PC)

Ortam: ayrı compose projesi `-p skinora-t163` (geliştirme stack'ine ve repo kökündeki `secrets/`'a dokunulmadı), `docker-compose.yml` + prova overlay'i (yalnız secret dosya yolları ve `container_name` sıfırlama), `--profile ethereum`, `up --no-deps` ile yalnız iki imza servisi, `NODE_ENV=production`, TRON Nile, Ethereum Sepolia (anahtarsız genel RPC `ethereum-sepolia-rpc.publicnode.com`). Sırlar scratch dizininde **rastgele** üretildi (5 dosya, bakiyesiz, hiçbir yere yazdırılmadı), prova sonunda `shred` ile silindi; arşiv parolası rastgele, dosyadan okutuldu (`--passphrase-file`).

| Adım | Sonuç |
|---|---|
| 1. Servisler sırlarla açıldı | `skinora-blockchain-sidecar` ve `skinora-ethereum-sidecar` **healthy**; TRON `hdMnemonicSource: file`, `hotWalletKeySource: file` |
| 2. Servislerin bildirdiği adresler (TRON derive 0–4 + açılış satırı; Ethereum derive 0–4 + açılış satırı) = `wallet-manifest.mjs`'in dosyalardan türettikleri | TRON depozit ✓, TRON sıcak ✓, Ethereum depozit ✓, Ethereum sıcak ✓ yakıt ✓ — **hepsi eşit** |
| 3. `wallet-backup.sh backup … --label testnet` | `Backup OK [testnet]`; 5 dosya; arşiv geri açılıp manifest + kaynakla eşleşti; arşiv SHA-256 `603467cc6990e3c2…` |
| 4. Sırlar silindi, servisler yeniden oluşturuldu | Docker host'ta 5 **boş dizin** açtı; TRON `refused to start: SIGNING_SECRET_UNREADABLE`, Ethereum `STARTUP_FAILED` (bu düzeltmeden önceki imaj); ikisi `Restarting (1)` — para yolu açılmadı |
| 5. `wallet-backup.sh restore … --label testnet` | 5 boş dizin kaldırıldı, 5 dosya geri yazıldı, adresler arşivle eşleşti (`Restore OK`) |
| 6. Servisler yeniden açıldı | İkisi **healthy**; bildirilen adres JSON'u 1. adımınkiyle **byte byte aynı** (`before==after: true`) |
| 7. Sızıntı taraması (`grep -F -f <sır dosyası>`) | servis log'ları **0**, `docker inspect` **0**, `/health` ×2 **0** |
| 8. Ethereum `SECRET_UNREADABLE` (düzeltmeden sonra) | Yeniden kurulan Ethereum imajı eksik secret'larla `run --rm --no-deps` → `"code":"SECRET_UNREADABLE"`, çıkış 1 |
| 9. Temizlik | `-p skinora-t163 down -v --rmi local --remove-orphans` → konteyner/volume/ağ/imaj **0**; `escrow_*` volume'leri 8 (dokunulmadı) |

Prova adresleri (açık adres, sır değil): TRON depozit #0–#4 `TSQeyjaVwWP7qnZRMtj5Lf1364qYGGypwb`, `TTdE4esWS34u9aBSCThkCbSty9e8agSusf`, `TP47xVF4hVNBcQCW64jMStBLq2qFn18uog`, `TLK5B6vH5p7RuRztSHTZzLPmpDjSZGniZa`, `TJoReQi6VYopwLV5ZRWTk2zZWQye657YD6`; TRON sıcak `TChQy2yUsZgzcsyCBc4iidG5cSHf7tvytt`; Ethereum depozit #0–#4 `0x119FDBcC121851e6F8bE3e63B3a8d0FAaf7c64B5`, `0x41ed71BfB186796CBD11EC386e0867F56676b0E7`, `0x791E4ED0484D8d7245703306975E75c039F36f70`, `0xe9d9de38AA70AB6B07428984d2fB2c88186E4A8a`, `0xb118DFA72a576272329CE46eD97D99b44ba94B04`; Ethereum sıcak `0xcc61C48c0119edf378128BeE64C0eaD1005E1411`, yakıt `0x323C053007B96B9E471DF279D61565B2FCcCdA40`.

Ek: betiğin türettiği adresler T143 bilinen-cevap vektörleriyle (TRON `TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH` …, Ethereum `0x9858EfFD232B4033E47d90003D41EC34EcaEda94` …) ve Ethereum servisinin kendi `EthereumHdWallet` sınıfıyla eşleşir (`walletBackupScript.test.ts`).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-blockchain vitest | ✓ 637/637 (34 dosya) | `npx vitest run`; yeni: `signingSecrets.test.ts` 20, `signingSecretsStartup.test.ts` 9, `composeWiring.test.ts` +7 |
| sidecar-ethereum vitest | ✓ 878/878 (37 dosya) | yeni: `walletBackupScript.test.ts` 6, `index.test.ts` +3 (`SECRET_UNREADABLE`) |
| Kapanış (merge sonrası) | ✓ | sidecar-blockchain 654/654 (35 dosya; ilk tam koşuda 1 süreç testi zaman aşımı → B6 düzeltmesi sonrası temiz), sidecar-ethereum 894/894 (37 dosya; `walletBackupScript.test.ts` 9 — yeni 3: AES-256 + SIGTERM + SIGHUP), scripts 47/47, `tsc` + `lint` + `prettier` iki sidecar. Backend'e T163 dokunmadı (merge ile gelen T160c/T167 kodu main'de yeşil) |
| scripts (`node --test "scripts/**/*.test.mjs"`) | ✓ 47/47 | `compose-exposure.test.mjs` dahil (deneme yayını secret'ları `secrets/trial/` dışından gelemez — yeni TRON secret'ları da kapsandı) |
| Build | ✓ | `npm run build` iki sidecar; Docker imajları (prova) |
| Lint / format | ✓ | `npm run lint` iki sidecar; `prettier --check` değişen dosyalar |
| Secret guard (pre-commit) | ✓ | İlk commit denemesi yanlış pozitiflerle bloklandı (kod tanımlayıcıları + test fikstürleri); bypass kullanılmadı — tanımlayıcılar yeniden adlandırıldı ve fikstürler özellik erişimine alındı; `bash scripts/git-hooks/pre-commit` → 0 |
| CI | ✓ run `39615737136` | aşağıda "Commit & PR" |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (8/8 kriter) — bağımsız doğrulama alt ajanı, Opus high, HEAD `1173613d`, 2026-10-22 |
| Bulgu sayısı | 7 — 1 × S1 ⚠ GÜVENLİK (B1), 6 × S4 (B2–B7) |
| Düzeltme gerekli mi | Engelleyici değil; kapanışta kapatıldı (aşağıda) |
| Geri yükleme provası | Doğrulamada bağımsız tekrarlandı (`-p skinora-t163val`, yeni rastgele sırlar): önce = sonra = arşiv manifesti, sızıntı taraması 0 |

**Kapanışta kapananlar:**

| # | Bulgu | Kapanış |
|---|---|---|
| B1 (S1 ⚠ GÜVENLİK) | SIGTERM/SIGHUP'ta açık metin sırlar geçici dizinde kalıyordu | `wallet-backup.sh`: `trap 'exit 129' HUP`, `trap 'exit 130' INT`, `trap 'exit 143' TERM` (çıkış → `cleanup`); ayrıca `cleanup` kendini kesilemez yapar (`trap '' HUP INT TERM` — ölçümde normal çıkıştaki temizlik süreç grubuna gelen sinyalle yarıda kalıp dosya bırakıyordu). Ölçüm (`timeout -s SIG t`, süreç grubuna sinyal — kapanan terminal gibi): eski betik `restore` 12/14 kesintide dizin bıraktı, yeni betik 0/14; `verify` TERM/HUP/INT × 11 zaman noktası (0,3–1,3 sn) → 0 kalıntı. Yeni test `walletBackupScript.test.ts` "shreds the plaintext temp directory when interrupted with SIGTERM/SIGHUP": PATH'e konan `node` ara betiği açık metin çıkarıldıktan sonra manifest kontrolünü 6 sn tutar, `timeout` 3. sn'de sinyal gönderir → çıkış 124, geçici dizin **0**; eski betikle iki test de kırılır (`left=1`). Sır içeriği yazdırılmadı (test çıktısı taranıyor). Runbook §L.2 cümlesi + §L.8 "yakalanamayan kesinti" (`kill -9`, elektrik) maddesi |
| B2 (S4) | PR main ile çakışıyordu | `origin/main` (`403fe257`, T167 + T160c) merge commit ile alındı — aşağıda "Commit & PR" |
| B3 (S4) | Bayat atıflar, eski değişkeni öneren hata metinleri | `wallet-backup.sh` §L.5→§L.6, §L.6→§L.8; `secrets/README.md` §L.5→§L.6; `src/index.ts` `signingMaterial.ts`→`signingSecrets.ts`; `TransferService` (2) ve `EnergyDelegationService` hata metinleri artık `tron_hot_wallet_private_key` secret dosyasını söylüyor |
| B4 (S4 ⚠ GÜVENLİK) | `NODE_ENV` trim'leniyordu | `envSecretsAllowed` trim etmez: `' development'`, `'development\n'`, `' test '`, `'\ttest'` → kapalı; iki yeni test (birim + `loadSigningMaterial` → `SIGNING_SECRET_IN_ENV`). Önce kontrol: compose / override / trial / e2e / CI'da NODE_ENV hep boşluksuz değer. Trim'i geri koyma bozması → 2 test kırıldı |
| B5 (S4) | Şifreleme parametreleri testle sabit değildi | Yeni test `gpg --list-packets`: cipher 9 (AES-256), s2k 3, hash 10 (SHA-512), count 65011712, MDC (ya da yeni GnuPG'de AEAD), sıkıştırma yok. `AES256` → `CAST5` bozması → test kırıldı |
| B6 (S4) | Süreç testleri lokal yük altında aralıklı kırılıyor | Neden: her test servisi `tsx` ile soğuk başlatıyor (tüm modül grafiği o an derleniyor); tam koşuda diğer süreç testleriyle yan yana 25 sn'yi aşabiliyor (kapanışta bir kez daha görüldü: 1/654). `signingSecretsStartup.test.ts` beklemesi 60 sn'ye çıkarıldı (yalnız satır hiç gelmezse beklenir; ret durumları süreç çıkınca erken biter) → sonraki tam koşu 654/654. T163 dışındaki `startupWiring.test.ts` (30 sn) dokunulmadı — aynı neden; derlenmiş `dist/` ile başlatmak kalıcı çözüm olur (not) |
| B7 (S4) | `SharedSecretsInPlainEnv` plan T171'de yoktu | `plan/11_F9.md` v1.22 T171 kabul kriterine eklendi (ortak sırlar secret dosyasından + satır kapanır; mainnet cüzdanları aynı gün çevrimdışı yedeklenir); DOC_CHANGELOG |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: **Var.** `.env`'den `HD_WALLET_MNEMONIC` ve `HOT_WALLET_PRIVATE_KEY` kalktı; yerine `secrets/tron_hd_mnemonic.txt` ve `secrets/tron_hot_wallet_private_key.txt` (deneme yayını `secrets/trial/`). **Merge sonrası geliştirme stack'i bu iki dosya olmadan TRON servisini açamaz** (dosyalar boş olabilir; eksikse Docker dizin açar → `SIGNING_SECRET_UNREADABLE`). Geçiş komutları `secrets/README.md`.
- Docker değişikliği: Var — `docker-compose.yml` iki secret + servis bağlaması; `docker-compose.trial.yml` deneme yolları. İmaj Dockerfile'ları değişmedi.

## Commit & PR

- Branch: `task/T163-imza-sirlari-secret`
- Commit: `d0bb3c31` — T163: TRON imza sırları Docker secret, env yolu üretimde kapalı, çevrimdışı yedek betiği (+ rapor/status commit'i)
- Kapanış commit'leri: `5f3110a7` — B1/B3/B4/B5/B7 düzeltmeleri; `eb492850` — `origin/main` `403fe257` (T167 #368 + T160c #365) merge commit'i, belge çakışmaları iki taraf korunarak (05 v3.32, plan v1.22, backlog 53/160 awk ile), kod çakışması yok (T160c'nin `TransferService.ts` / `EnergyDelegationService.ts` değişiklikleri otomatik birleşti; B3 hata metinleri üstünde)
- PR: #369
- CI (kapanış): ✓ PASS — run `39818585773` (HEAD `5bb0dde2`, merge + kapanış düzeltmeleri): CI Gate ✓, 26 job success; Sidecar-ethereum vitest `walletBackupScript.test.ts` 9/9 Linux runner'da atlanmadan koştu (AES-256 `--list-packets`, SIGTERM, SIGHUP dahil)
- CI: ✓ PASS — run `39615737136` (HEAD `b55498b2`, kod + rapor): CI Gate ✓; Sidecar-blockchain vitest (`signingSecrets` 20, `signingSecretsStartup` 9) ✓, Sidecar-ethereum vitest (`walletBackupScript` 6 — CI runner'da atlanmadan koştu) ✓, 3c Script test ✓, 7 Docker build ✓, 11 E2E (advisory) ✓. Önceki run `39615073534` (`d0bb3c31`) yeni push yüzünden `cancelled` (concurrency). Not: API, Ethereum Docker build job'ını run bittikten sonra da `in_progress` gösterdi; adımlarının hepsi ("Complete job" dahil) `success`. Bu satırı ekleyen commit yalnız rapordur

## Kararlar (öneriyle kaydedildi — ⚠)

- **K1 (⚠ GÜVENLİK) — env yolu yalnız `NODE_ENV` tam `development`/`test`.** Boş/bilinmeyen `NODE_ENV` kapalı sayılır (yanlış yazım açık bırakmasın). Değişken üretimde **yok sayılmaz, servisi durdurur**: birisi tohumu `docker inspect`'in gördüğü yere koymuş demektir.
- **K2 (⚠ PARA) — sıcak anahtar `HOT_WALLET_ADDRESS`'i kontrol etmeli (`HOT_WALLET_ADDRESS_MISMATCH`).** Yanlış yedekten geri yüklenen anahtar ilk payout'ta değil açılışta yakalanır. Mevcut bir geliştirme `.env`'inde adres ile anahtar uyuşmuyorsa servis artık açılmaz (bilinçli).
- **K3 (⚠ GÜVENLİK) — ortak sırlar bu turda taşınmadı → `SharedSecretsInPlainEnv` (T171).** Bedel: beş servis, backend'e dosya yapılandırma kaynağı (`AddKeyPerFile` — paylaşılan çerçevede var, ölçüldü), üç sidecar'a `_FILE`, her sırrın iki uca aynı dosyadan bağlanması, testlerin yeniden yazımı ve **geliştirme `.env` sözleşmesinin değişmesi** (`${BLOCKCHAIN_WEBHOOK_SECRET:-${WEBHOOK_SECRET}}` gibi geri düşüş zincirleri compose secret kaynağında ifade edilemiyor). Risk farkı küçük: yeniden üretilebilir sırlar, kayıpları para kaybı değil; `docker inspect` okuyabilen host'taki dosyayı da okur; deneme yayını yalnız test ağında. T171'de bütün sırlar zaten yeniden üretildiği için taşıma orada ek geçiş bedeli doğurmaz.
- **K4 (⚠ GÜVENLİK) — araç GnuPG.** Bu PC'de kurulum gerektiren tek seçenek değil (Git for Windows'la geliyor), açık standart, bütünlük denetimli, betikle uçtan uca test edilebiliyor. 7-Zip / age karşılaştırması §L.2.
- **K5 — boş tohum dosyasıyla TRON servisi yine açılır** (T163 öncesi sözleşme: derive 503). Eksik dosya (Docker dizini) ise açılmaz. Üretimde boş tohumu reddetmek ayrı bir karar; bu görevde davranış değiştirilmedi.
- **K6 — yedek kapsamı beş imza dosyası.** Soğuk cüzdan / kilit hesabı anahtarları bu makineye girmez (kendi donanım yedekleri, T171); Cloudflare token'ı panelden yeniden alınır; DB yedeği T164.
- **K7 — parola alt sınırı 16 karakter** (betik reddeder; öneri 6 diceware kelimesi).

## Proje sahibine açık sorular (geri dönüşü zor — TAHMİN EDİLMEDİ, öneri işaretli; T171 kurulum gününde seçilecek, DEPLOY_RUNBOOK §L'de de "öneri" olarak duruyor)

1. **Yedek aracı:** GnuPG (öneri — kurulu, betikli, test edildi) mı, 7-Zip AES-256 mı, age mi?
2. **Arşiv parolasının yeri:** öneri parola yöneticisi + USB'lerden ayrı bir yerde kapalı zarfta kağıt. Başka bir düzen mi?
3. **Tohum kelimelerinin kağıt yedeği ve paylaştırma:** öneri paylaştırma **yok**, iki tam kopya iki ayrı güvenli yerde. Shamir/SLIP-39 (ör. 2-of-3) ister misiniz? Kağıt yedek hiç olmasın mı?
4. **Saklama yerleri:** öneri iki USB (farklı marka) + iki kağıt, iki ayrı fiziksel yer (ev + ikinci adres / banka kiralık kasası). Mainnet arşivi buluta konmasın (öneri) — onaylıyor musunuz?
5. **Sıcak/yakıt anahtarlarının kağıda yazılması:** öneri hayır (yalnız şifreli arşivde). Onay?
6. **Prova sıklığı ve sorumlu:** öneri 6 ayda bir geri yükleme provası, yalnız proje sahibi. Onay?
7. **Disk şifrelemesi:** deneme yayını PC'sinde BitLocker açık mı? (`secrets/*.txt` diskte düz metin; öneri: açık olmalı.)
8. **Ortak sırların zamanı (K3):** T171'e bırakılması uygun mu, yoksa deneme yayınından (T172) önce mi?

## Known Limitations / Follow-up

- **Tohum kökü ve sıcak anahtar süreç boyunca bellekte** (05 §3.3 "Signing izolasyonu" bugünkü gerçek). Değişikliği KMS/HSM tetiğine bağlı; eşik T169.
- **Host'taki secret dosyaları düz metin**; compose secret'ı swarm dışında bind mount'tur. Koruma host erişimi + disk şifrelemesi (soru 7). `shred` NTFS/SSD'de silmeyi garanti etmez.
- **Docker eksik secret dosyası yerine host'ta dizin açar** (ölçüldü). Servisler artık bunu adıyla reddeder; geri yükleme betiği boş dizini kaldırır; `secrets/README.md` uyarır. Compose tarafında önleme yok.
- **Geliştirme stack'inin geçişi gerekiyor** (Altyapı Değişiklikleri). Sahip PC'sindeki mevcut `.env` değerleri bu görevde taşınmadı (repo kökündeki gerçek `secrets/`'a dokunma yasağı).
- **Ortak sırlar env'de** → `SharedSecretsInPlainEnv` (T171).
- **Etkileşimli parola istemi** (`read -s` / `/dev/tty`) bu ortamda elle denenemedi; otomatik testler ve prova `--passphrase-file` ile koştu. Sahip ilk gerçek yedekte etkileşimli yolu kullanacak (§L.4).
- **Yedek testinin CI'da koşması** ubuntu runner'da `gpg` ve `shred` bulunmasına bağlı; yoksa test **kırmızı** olur (atlamaz).

## Notlar

- **Working tree:** temiz (başlangıçta `git status --short` boş).
- **Main CI startup check:** son tamamlanmış 3 run `success` — CI `39600854571` (e7e8acf1), Docker Publish `39600854541` (e7e8acf1), Docker Publish `39607353298` (d81d91f1); d81d91f1 CI `39607353309` başlangıçta kuyruktaydı, sonra `success`.
- **Devir kaydı (merge teyidi):** T161 (#367) main `d81d91f1` → CI `39607353309` ✓, Docker Publish `39607353298` ✓ (STATUS_CHANGELOG'a işlendi).
- **Dış varsayımlar ve kanıtları:**
  - Compose'un dosya kaynaklı secret'ı swarm dışında çalışır ve `/run/secrets/<ad>`'a bağlar — prova: konteyner mount'ları `/run/secrets/tron_*`. Docker Compose v5.1.0.
  - **Kaynak dosya yoksa compose hata vermez, Docker host'ta ve konteynerde dizin açar** — ölçüldü (scratch compose: `config` çıkış 0, `up` çıkış 0, konteynerde `cat: read error: Is a directory`, host'ta `missing/a.txt/` dizini).
  - `docker compose config` eksik secret dosyasıyla render eder (CI `compose-exposure` testi dosyasız koşar) — aynı ölçüm, `config` çıkış 0.
  - GnuPG 2.2.41 (Git for Windows) `--pinentry-mode loopback --passphrase-fd`, `--no-symkey-cache`, AES256 + S2K mode 3 SHA512 + MDC destekliyor — `gpg --list-packets`: `cipher 9 … s2k 3, hash 10`, `mdc_method: 2`; yanlış parola çıkış 2.
  - `Microsoft.Extensions.Configuration.KeyPerFile` ASP.NET Core paylaşılan çerçevesinde (K3 değerlendirmesi) — `C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\9.0.9\` altında dll mevcut.
  - Sepolia genel RPC (`ethereum-sepolia-rpc.publicnode.com`) anahtarsız `finalized` blok veriyor — prova: Ethereum servisi `healthy`, derive 0–4 200.
- **Bypass değişkeni kullanılmadı.** İlk commit denemesi pre-commit secret guard'ına takıldı (dal o an `d81d91f1` ile aynıydı ve öyle push edildi); bulgular yanlış pozitifti (ör. adında "secret" geçen bir değişkene fonksiyon sonucu atanması, tohum değişkenine sabit atayan bir test fikstürü, 12 küçük harfli kelimelik bir yorum satırı). Kod tanımlayıcıları (`loadSigningMaterial`, `SigningMaterial`, `MountedFileReader`) ve fikstür erişimi buna göre düzenlendi.
- **Kullanıcının ev dizininde** ilk gpg denemesi `~/.gnupg` oluşturdu (önceden yoktu, yalnız boş anahtarlık); deneme sonunda silindi. Betik kendisi geçici `GNUPGHOME` kullanır.
