# T141 — Çok ağlı ödeme — doküman ve karar tabanı

**Faz:** F8 | **Durum:** ⏳ Devam ediyor — yapım bitti, bağımsız doğrulama bekliyor | **Tarih:** 2026-10-17

---

## Yapılan İşler

- **Kararlar kayda geçti (11 §F8).** Proje sahibi kararları **K7** (Ethereum ayrı kök tohum, kendi konteyneri, yol `m/44'/60'/0'/0/{i}`), **K8** (TRON sweep ücreti alıcıya, kabul anında ayrı "TRON ağ ücreti" satırı; payout satıcıya mevcut %10 kuralıyla; sweep öncesi iadede satır tam iade; Ethereum'da platform) ve **K9** (reddedilen ödeme iadeleri alıcının o ağdaki iade adresine — **T110-K1 tersine döndü**) işaretsiz yazıldı. Yönetici girdisi sekiz karar ve iki yapımcı önerisi `(öneriyle kaydedildi — ⚠)` ile yazıldı (aşağıda ⚠ listesi).
- **Plan hizalandı.** T143 (Ethereum yolu ayrı tohumdan — eski metin "aynı servis" diyordu), T149 (dağılım ve kilit kaderi artık T141'de), T150 (ayrı tohum + ayrı yakıt anahtarı), T167 (depozit işlem başına, MASAK notları), T169 (TRON en düşük tutarı açık kalemi) kabul kriterleri kararlara çekildi.
- **§7.6 F8 kaynak öğeleri** tablosu (21 satır) 11'e eklendi; her hedef kural bir F8 (ya da F9 T166/T167/T169) görevine bağlı.
- **Ağ kavramı üç katmanda tanımlandı:** veri modeli (06 §1.4 hedef model + §2.25 `BlockchainNetwork`), API (07 §1.3 hedef sözleşme), servis sözleşmesi (05 §3.3a — ağ başına servis, webhook yükünde `network`, ağ başına ayar ve dondurma).
- **Ethereum akışı TRON'un yanında yazıldı (08 §3.7):** ağ/token/sağlayıcı, servis ve anahtarlar, depozit, `finalized` kabulü (K5), yakıt cüzdanı, gönderim (nonce, imzalı işlemin yayından önce saklanması, imzacı izin listesi), ücret politikası, yanlış token ve yerel ETH, 30 gün geç ödeme, hata senaryoları. Ortak kurallar yeni 08 §3.0'da.
- **Bugünkü TRON davranışını değiştiren her hedef kural için uygulayan görev yazıldı:** iade hedefi `FromAddress` → iade adresi (T146/T147), 6 haneli beklenen / 2 haneli gösterim (T144), HD yolu sapması (T143), USDC seçeneği (T166), sweep ücretinin platformda kalması (T149), kilit kapama anahtarının kodda olmaması (T149).
- **"Yalnız USDT" (K2)** 10_MVP_SCOPE, 02 (ve 01, 03, 04, 06, 07, 08) içine işlendi; USDC yalnız yanlış token izin listesinde kalır.
- **Kilit (hibrit enerji) uykuda:** 08 §3.3, 05 §3.3, 02 §4.6 ve DEPLOY_RUNBOOK §C.2 "uykuda" notu; backlog `StakeAccountUnmonitored` satırına not.
- **#334 merge teyidi** status kaydına yazıldı (squash `2e70a03`, main CI `39063106414` ✓ + Docker Publish `39063106482` ✓).

## Etkilenen Modüller / Dosyalar

Yalnız doküman. Kod, migration, config, Docker değişikliği yok.

| Dosya | Sürüm | Değişiklik |
|---|---|---|
| `Docs/11_IMPLEMENTATION_PLAN.md` | v1.0 → **v1.1** | §F8 K7–K9 + ⚠ listesi; T143/T149/T150/T167/T169; §7.6 |
| `Docs/08_INTEGRATION_SPEC.md` | v3.16 → **v3.17** | §3 başlığı + §3.0; §3.1a sweep ücreti alıcıya; §3.2 HD sapması; §3.3 yalnız USDT + kilit uykuda; §3.4 iade hedefi K9 + eşik ağ başına; §3.6; yeni §3.7 (3.7.1–3.7.8); §1.1, §8, §9 |
| `Docs/02_PRODUCT_REQUIREMENTS.md` | v4.3 → **v4.4** | §2.2, §2.3, §4.1, §4.2 (yeniden yazıldı), §4.4 + not (K9), §4.6, §4.7, §5 + örnek tablo, §12.1–§12.3, §16.2 |
| `Docs/03_USER_FLOWS.md` | v3.9 → **v3.10** | §2.2 adım 9/15, §2.4 adım 4–5, §3.2 adım 1/4, §3.4 adım 3, §4.3, §5.3, §5.4, §5.5, §9 |
| `Docs/05_TECHNICAL_ARCHITECTURE.md` | v3.9 → **v3.10** | §3 şema notu, §3.3 tabloları, yeni §3.3a, §3.5 sırlar, §8.1, §8.2 |
| `Docs/06_DATA_MODEL.md` | v6.19 → **v6.20** | yeni §1.4 + §2.25; §1.1, §2.2, §3.1, §3.5, §3.7, §3.8, §3.17, §3.22, §3.25, §5.1, §8.3 notları |
| `Docs/07_API_DESIGN.md` | v4.4 → **v4.5** | yeni §1.3; §5.3, §7.2, §7.4, §7.5, §7.6, §9.23, §9.24 notları |
| `Docs/10_MVP_SCOPE.md` | v2.1 → **v2.2** | §2.2, §3.2, §4, §6 (altbilgi v2.0 → v2.2, önceden kaymıştı) |
| `Docs/01_PROJECT_VISION.md` | v1.1 → **v1.2** | §6.1 |
| `Docs/04_UI_SPECS.md` | v4.7 → **v4.8** | S06 adım 2, S07 ödeme bilgileri, admin filtre, C11, §10.4 (yalnız F8 hedef notları; ekran T156) |
| `Docs/09_CODING_GUIDELINES.md` | v1.1 → **v1.2** | §7.2 T144 hedef satırı |
| `Docs/DEPLOY_RUNBOOK.md` | (sürümsüz → "Son güncelleme" eklendi) | §C.2 uykuda; `STAKE_ACCOUNT_*` satırı; payout erteleme notu |
| `Docs/DEFERRED_BACKLOG.md` | — | Durum satırı; `StakeAccountUnmonitored`, `T110-RefundAddressDocConflict`, `sanctions-expansion` notları; sayı değişmedi |
| `Docs/IMPLEMENTATION_STATUS.md` | — | T141 ⏳; başlık; #334 merge teyidi; önceki başlık STATUS_CHANGELOG'a taşındı |
| `Docs/STATUS_CHANGELOG.md` | — | önceki "Son güncelleme" girişi birebir taşındı |
| `.claude/memory/MEMORY.md` | — | T141 satırı |
| `Docs/TASK_REPORTS/T141_REPORT.md` | yeni | bu rapor |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1a | K1–K6 dokümanlara işlendi: **ağ kavramı veri modelinde** | ✓ | `grep -c "^### 1.4\|^### 2.25" Docs/06_DATA_MODEL.md` → 2. §1.4: `Network` alanları (PaymentAddress, BlockchainTransaction, ColdWalletTransfer, SellerPayoutIssue, Transaction), ağlı tekillik anahtarları, kanonik adres, `UserWalletAddress`, `TransactionNetworkOption`, `NetworkFeeAmount` |
| 1b | Ağ kavramı **API'de** | ✓ | `grep -c "^### 1.3 Ağ Boyutu" Docs/07_API_DESIGN.md` → 1; §5.3, §7.2, §7.4, §7.5, §7.6, §9.23–§9.24 "F8 hedefi" notları |
| 1c | Ağ kavramı **sidecar sözleşmesinde** | ✓ | `grep -c "^### 3.3a" Docs/05_TECHNICAL_ARCHITECTURE.md` → 1 (uç kümesi `grep -rhoE "\"api/[a-z/-]+\"" backend/src` çıktısından alındı: 11 uç); 08 §3.0 "Ağ başına servis" |
| 1d | Ethereum akışı (depozit, yakıt, finalized, sweep, payout, iade, 30 gün geç ödeme) 08'de TRON'un yanında | ✓ | `grep -n "^#### 3.7" Docs/08_INTEGRATION_SPEC.md` → 3.7.1–3.7.8 (8 alt başlık): depozit §3.7.3, yakıt §3.7.5, `finalized` §3.7.4, sweep/payout/iade §3.7.6, ücret §3.7.7, 30 gün §3.7.4 "Geç ödeme" |
| 2a | (a) ayrı kök tohum / aynı tohum kararı kapandı | ✓ | K7 — 11 §F8; 05 §3.3, §3.5; 08 §3.0, §3.2, §3.7.2, §9.2 |
| 2b | (b) depozit işlem başına / kullanıcı başına + MASAK "karşı tarafı beyan" tartısı | ✓ ⚠ | 11 §F8 "(b) Depozit adresi işlem başına kalır" (gerekçede beyan kuralının alıcının ödediği tarafla — Skinora — ilgili olduğu yazılı); 05 §3.3; 08 §3.0, §3.7.3 |
| 2c | (c) TRON ağ ücretinin alıcı/satıcı dağılımı | ✓ | K8 — 02 §4.6, §4.7, §5 (örnek tablo); 06 §1.4, §8.3; 07 §1.3; 08 §3.1a |
| 2d | (d) Ethereum yanlış token iadesi + imzacı izin listesi | ✓ ⚠ | 11 §F8 "(d)"; 08 §3.7.4 (sınıflandırma, yerel ETH), §3.7.6 (imzacı: token `transfer`, `chainId`, tip 4 yok) |
| 3 | §7'ye "F8 kaynak öğeleri" izlenebilirlik tablosu | ✓ | `grep -c "^### 7.6 F8" Docs/11_IMPLEMENTATION_PLAN.md` → 1; 21 satır, "F8 boşluk: 0". Tablodaki her bölüm referansının var olduğu ölçüldü (aşağıdaki "Doğrulama komutları" 2) |
| 4 | "Yalnız USDT" 10_MVP_SCOPE ve 02'ye yansıdı | ✓ | `grep -n "Yalnız USDT\|Sadece USDT" Docs/10_MVP_SCOPE.md Docs/02_PRODUCT_REQUIREMENTS.md` → 10 §2.2, §4; 02 §4.1, §4.2 başlığı "Token ve Ağ Seçimi" |
| — | Görev talimatı: K7–K9 işaretsiz, yönetici kararları `(öneriyle kaydedildi — ⚠)` | ✓ | `grep -o "öneriyle kaydedildi — ⚠." Docs/*.md \| sort \| uniq -c` → hepsi tam biçim `(öneriyle kaydedildi — ⚠)`: 02 ×3 · 03 ×1 · 05 ×5 · 06 ×5 · 07 ×1 · 08 ×9 · 11 ×11 · DEFERRED_BACKLOG ×1 · DEPLOY_RUNBOOK ×1. K7–K9 satırlarında işaret yok (`grep -n "K7 —\|K8 —\|K9 —" Docs/11_IMPLEMENTATION_PLAN.md`) |
| — | Değişen her dokümanın sürümü + "Son güncelleme" | ✓ | `git diff -U0 origin/main -- Docs \| grep -o "^[-+]\*\*Versiyon: v[0-9.]*\*\*"` → 10 doküman, her biri bir artış (tablo yukarıda) |
| — | Normatif metinde belirsiz kelime yok | ✓ | `git diff -U0 -- Docs \| grep "^+" \| grep -i "olabilir\|gerekirse\|muhtemelen\|belki"` → yalnız 11'in başlık satırı eşleşti; eşleşen kelime o satırın **eski** (2026-08-21) girişindedir, bu turda yazılan metinde yok |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit / Integration | Yok | Görev doküman görevidir (11 §F8 T141 "Test beklentisi: Yok") |
| Doküman tutarlılık ölçümleri | ✓ | Aşağıdaki "Doğrulama komutları" |
| CI | bkz. Commit & PR | Docs-only PR; CI'nin bloke edici adımları değişmeyen kodu koşar |

**Doğrulama komutları (yapım sırasında koşuldu, worktree `task/T141-cok-agli-odeme-dokuman`):**

1. Eski iade hedefi kalıntısı: `grep -n "08 §562" Docs/*.md` → yalnız `DEFERRED_BACKLOG.md:169` (tarihsel T110 notu), `IMPLEMENTATION_STATUS.md:271` (tarihsel T110 satırı) ve `DEFERRED_BACKLOG.md` T110 satırının yeni notu. `grep -n "geldiği adres\|kaynak adrese" Docs/0*.md Docs/10*.md Docs/DEPLOY_RUNBOOK.md` → yalnız "eski kural" anlatan satırlar (02 §4.4 notu, 08 §3.4 "Neden tersine döndü") ve sürüm başlıkları.
2. Yeni bölümlerin varlığı (11 §7.6'nın gösterdiği hedefler): `06 ### 1.4` 1 · `06 ### 2.25` 1 · `07 ### 1.3` 1 · `05 ### 3.3a` 1 · `08 ### 3.0` 1 · `08 ### 3.7 ` 1 · `08 #### 3.7.1`…`3.7.8` 1'er · `11 ### 7.6` 1 · `DEPLOY_RUNBOOK ### C.2` 1.
3. Backlog sayacı (dosyanın kendi komutu): aktif `awk 'BEGIN{s=0}/^## /{s=($0 ~ /Öne Çıkanlar/)?0:1}/^\| (🔴|🟡|⚪) /{if(s)n++}END{print n+0}' Docs/DEFERRED_BACKLOG.md` → **38**; çözülmüş (aynı komut `✅`) → **155**. Önce ve sonra aynı.
4. HD yolu iddiası (08 §3.2 notu) — ethers 6.17.0 (sidecar'ın kilitli sürümü, `sidecar-blockchain/package.json:35`) scratchpad'e kurulup ölçüldü: `HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase("abandon … about"))` → `path = m/44'/60'/0'/0/0, depth = 5`; `.derivePath("44'/195'/0'/0/0")` → `m/44'/60'/0'/0/0/44'/195'/0'/0/0`. Kodun bu çağrıyı kullandığı: `sidecar-blockchain/src/wallet/HdWalletService.ts:138`.
5. K9'un "bugünkü kod" iddiası: `grep -n "FromAddress" backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs` → satır 194/211 (yanlış token), 272/284 (gecikmeli ödeme), 328/341, 389/403, 449/463 (`SourceAddress: …FromAddress`).
6. Kilit anahtarının bugün olmadığı (08 §3.3, DEPLOY_RUNBOOK §C.2): `grep -rn "DELEGATION_ENABLED\|ENERGY_DELEGATION" sidecar-blockchain/src` → sonuç yok; tek ayar `STAKE_ACCOUNT_ADDRESS` (`config/index.ts:119`).

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor — bağımsız doğrulama alt ajanı (F8 yönetici düzeni, INSTRUCTIONS §3.3) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok (06 §1.4 hedef modeli T145'te tek forward migration ile uygulanır)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T141-cok-agli-odeme-dokuman` (origin/main `2e70a03`'tan)
- Commit: `960cb11` — T141: çok ağlı ödemenin karar tabanı dokümanlara işlendi (K7–K9, ağ boyutu, Ethereum akışı)
- Commit: `24a3bd2` — T141: rapor, status (⏳ + #334 merge teyidi), repo hafızası; ⚠ işaretleri tam biçime çekildi
- Commit: CI run zincirinin rapora yazılması (bu commit — yalnız bu rapor)
- PR: #337
- CI (PR, `pull_request`): `960cb11` → [`39067628786`](https://github.com/turkerurganci/Skinora/actions/runs/39067628786) ✓ success · `24a3bd2` → [`39067906983`](https://github.com/turkerurganci/Skinora/actions/runs/39067906983) ✓ success (docs-only: "Detect changed paths" + "1. Lint" + "CI Gate" koştu, kod adımları yol filtresiyle atlandı). Bu commit'in run'ı PR kontrollerinde

## Known Limitations / Follow-up

- **K8 örnek tutarlarında yönetici girdisiyle fark (hesap, karar değil).** Girdi 20 $ TRON için satıcının alacağını "~15,76–17,75", 200 $ için "196,13–198,11" veriyordu. Aynı girdinin payout aralığıyla (2,29–4,53 $) ve 02 §4.7'nin kuralıyla (satıcı payout − komisyonun %10'u kadar öder) üst uç 20 − (4,53 − 0,04) = **15,51** ve 200 − (4,53 − 0,40) = **195,87** çıkar; girdinin üst ucu ~4,27–4,28 $'lık bir payout'a karşılık gelir. Dokümanlara (11 §F8 K8, 02 §5) hesapla tutarlı değerler yazıldı ve formül yanına konuldu. Alt uç ve alıcı tutarları birebir aynı. Yöneticinin onayı istenir.
- **Sweep'ten sonra yapılan iadede TRON ağ ücreti satırının işlenişi** kararda yok (karar yalnız "sweep öncesi iade → tam iade" diyor). 02 §4.6 bunu T149'a bağladı.
- **T72'nin tarihsel kabul kriteri** (11 satır ~1560, "İade kaynak adrese gönderilir") değiştirilmedi — kapanmış bir MVP görevinin tanımıdır; geçerli kural 02 §4.4 ve 08 §3.4'tedir.
- Kodda bugün: USDC seçilebiliyor (T166), iade hedefi `FromAddress` (T146/T147), ödeme ekranı ve i18n metinleri "Exchange'den gönderim yapmayın" diyor (T156), beklenen tutar 6 haneli (T144), HD yolu standart dışı (T143), kilit kapama anahtarı yok (T149), sweep ücreti platformda (T149). Her biri dokümanda hedef kural + görev olarak yazıldı.
- 08 §3.7'deki sayılar (RPC sağlayıcısı, `getLogs` sınırları, ücret tavanı, "bekle" eşiği, Sepolia test token) T142'nin ölçümüne bırakıldı; bölüm kuralı yazar, sayının hangi görevden geleceğini söyler.

## Notlar

- **Working tree:** temiz (`git status --short` boş, worktree `agent-a7989cc627dfd2bfa`).
- **Main CI startup check (Adım 0), son 3 tamamlanmış main run'ı:** Docker Publish `39063106482` success · CI `39063106414` success · CI `39061292212` success (hepsi `gh run list --branch main`). Bir önceki Docker Publish `39059183809` failure idi; #336 ile kapandı ve sonraki iki Docker Publish yeşil.
- **Bağımlılık:** T141'in bağımlılığı yok.
- **Dış varsayımlar:**
  - ethers 6.17.0 `HDNodeWallet.fromMnemonic` varsayılan yolu `m/44'/60'/0'/0/0` döndürür → ölçüldü (komut 4).
  - Reddedilen ödeme iadelerinin bugün `FromAddress`'e gittiği → kodda görüldü (komut 5).
  - Kilit kapama anahtarının kodda olmadığı → kodda görüldü (komut 6).
  - Sidecar uç kümesi (05 §3.3a) → `grep -rhoE "\"api/[a-z/-]+\"" backend/src --include=*.cs` ve `grep -rhoE "(post|get)\(['\"][^'\"]+" sidecar-blockchain/src` çıktısından.
  - Ethereum değerleri (Tether `0xdAC17F958D2ee523a2206206994597C13D831ec7` 6 ondalık, Circle USDC `0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48` 6 ondalık, `chainId` 1 / 11155111) proje sahibi / yönetici girdisinden (11 §F8 K1, T152, karar dosyası) alındı; bu görevde zincirden **ölçülmedi** — zincir okuması T142'nin kabul kriteridir.
- **Yapı değişikliği onayı (GUARDRAILS §3):** yeni alt bölümler (06 §1.4, §2.25; 07 §1.3; 05 §3.3a; 08 §3.0, §3.7; 11 §7.6) görev tanımının kendisinin istediği eklemelerdir (11 §F8 T141 kabul kriterleri 1 ve 3). 08 §3 başlığı iki ağı kapsayacak şekilde yeniden adlandırıldı; içindekiler bağlantısı güncellendi (`grep -rn "#3-tron" Docs` → başka referans yoktu).
- **⚠ listesi** (`(öneriyle kaydedildi — ⚠)`; F8 Gate Check'ten önce proje sahibine gösterilecek):
  1. (b) Depozit adresi işlem başına kalır — 11 §F8; 05 §3.3; 08 §3.0, §3.7.3
  2. Kilit (hibrit enerji) kodu uykuda, varsayılan kapalı anahtar (anahtarı T149 ekler); 2026-09-16 hibrit kararının yerini K4/K8 aldı — 02 §4.6; 05 §3.3; 08 §3.3; DEPLOY_RUNBOOK §C.2
  3. (d) Ethereum yanlış token izin listesi (USDT + Circle USDC), yerel ETH alarmı, imzacı kısıtları (token `transfer`, `chainId`, tip 4 yok) — 08 §3.7.4, §3.7.6
  4. Ethereum ücret politikası: sweep ve payout tavana kadar platformda, üstünde bekle + alarm, iade ölçülen ücreti düşer; sayılar T142 — 02 §4.7; 08 §3.7.7
  5. Yakıt cüzdanının sıcak cüzdandan ayrı anahtarı — 05 §3.3a, §3.5; 08 §3.7.2, §9.2
  6. İade eşiği (`net < 2 × gas`) ağ başına ayar — 02 §4.6; 06 §3.17; 08 §3.4
  7. TRON için en düşük satış tutarı / ~50 $ altında TRON'u gizleme — karar verilmedi, T169'a açık kalem
  8. MASAK notları T167'ye (alıcı USDT'yi önceden tutmalı; Resmî Gazete metnine karşı doğrulama)
  9. **Yapımcı önerisi —** F8 veri modeli ve API biçimi: `UserWalletAddress`, `TransactionNetworkOption`, `Transaction.NetworkFeeAmount`, EVM küçük harf saklama + EIP-55 gösterim, `network` alanı, `acceptedNetworks`, `networkQuotes`, `confirmLowercase`, hata kodları `NETWORK_NOT_ACCEPTED` (422) / `NETWORK_UNAVAILABLE` (503) — 06 §1.4; 07 §1.3
  10. **Yapımcı önerisi —** Ethereum akış ayrıntıları: yakıtın bir bloğa girmesi yeterli, imza tipleri 0 ve 2, giden işlem `finalized`'da `CONFIRMED`, açılışta USDT ücret anahtarı kontrolü, kara liste için yayın öncesi `eth_call`, ağı uyuşmayan webhook'un reddi — 08 §3.7; 05 §3.3a
