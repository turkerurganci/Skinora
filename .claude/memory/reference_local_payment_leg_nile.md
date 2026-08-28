---
name: reference_local_payment_leg_nile
description: Lokal ödeme bacağı Nile testnet kurulumu — üretilen cüzdanlar, seçilen USDT kontratı ve kalan dış-değer borcu
type: reference
---

**2026-08-28'de lokal ödeme bacağının yapılandırılabilir yarısı kuruldu** (`DEPLOY_RUNBOOK §G` adım 5 + 9). Sırlar `.env`'de (gitignored); burada yalnız **kamuya açık** değerler var.

- **HD master mnemonic** — 24 kelime, `skinora-blockchain-sidecar` container'ının kendi `ethers`'ıyla üretildi (256-bit entropy). Deposit adresleri `m/44'/195'/0'/0/{index}` yolundan türüyor. **index 0 = `TF6nQDf6Pv7Ao8vfzSbCWm2Uh9H5iBhgyM`** — bu adres kurulum doğrulaması için kullanılır: `POST /api/wallet/derive {"index":0}` aynı adresi döndürmelidir (`.env` → container → `HdWalletService` yolunun uçtan uca kanıtı).
- **Hot wallet** — HD master'dan bağımsız ayrı anahtar (05 §3.3 gereği), adres **`TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD`**. Aynı adres `reconciliation.hot_wallet_address` SystemSetting'ine **admin API üzerinden** yazıldı (`PUT /api/v1/admin/settings/:key`, süper admin JWT ile; audit satırı oluştu) — SQL'le değil, çünkü ayar env'den set edilemez ve gerçek doğrulama yolundan geçmesi istendi.
- **`TRON_USDT_CONTRACT` = `TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`** — Nile'da zincire sorularak seçildi: `name()` = *Tether USD*, `symbol()` = *USDT*, `decimals()` = **6** (sidecar `tokenDecimals: 6` bekliyor), totalSupply ≈ 17.6M. Rakip aday `TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj` de aynı üç değeri veriyor ama totalSupply'ı ~1e27 (serbest mint test token'ı). **Kesin karar faucet'ten gelen token'la verilir** — runbook §G.5 bunu şart koşuyor; bakiye geldiğinde hangi kontratta durduğu `POST /api/wallet/balances` ile teyit edilmeli.
- **`TRON_USDC_CONTRACT` bilerek BOŞ bırakıldı.** Kod boşu güvenle işliyor (`walletHandlers.ts:96` ve `PaymentMonitorRules.ts:46` boş girişi atlıyor), allowlist yalnız USDT taşıyor. Doğrulanamamış bir adres yazmak, yanlış kontratı allowlist'e almaktan daha kötü olurdu.
- **Bağlantı kanıtı** (`/health` değil): `POST /api/wallet/balances` **`blockNumber: 70476111`** ile döndü ve `tokens` yalnız `TRX` + `USDT` taşıdı — sidecar Nile'ı gerçekten okuyor ve allowlist beklendiği gibi. **TronGrid API key olmadan çalıştı**; anahtar hız limiti içindir, açılış şartı değil.

**Kalan dış-değer borcu (yalnız insan yapabilir):** TronGrid API key (`TRON_API_KEY`; 3 sn'lik izleme turu anahtarsız 429 yer) · hot wallet'a faucet TRX (sweep 200 TRX energy delege eder) · alıcı rolündeki cüzdana Nile test USDT. Ayrıca happy path'in Steam yarısı için ikinci hesabın (`76561198652999063`) MA + trade URL'i gerekiyor — alıcı kapıları fail-closed.

İlgili: [[reference_local_stack_runbook_g]]


## Prova durumu — 2026-08-29 (nerede kaldık)

**Tron tarafı BİTTİ, Steam tarafı 31 Ağustos'a kadar bloke.**

Hazır olanlar (ölçüldü):

- Hot wallet `TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD` → **798,9 TRX**. Faucet 1000 TRX verdi; 200'ü alıcı cüzdanına gönderildi, 1,1 TRX ücret (1 TRX yeni hesap açma + 0,1 bant genişliği — `08 §3.3` ile uyumlu).
- Alıcı cüzdanı (TronLink, Nile) `TWrbG7F38xPMty4jRhgnBxrfAPtS84KmHK` → **200 TRX + 1000 USDT**.
- USDT'nin durduğu kontrat **`TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`** — yani `.env`'e yazılan kontrat **faucet tarafından doğrulandı** (rakip aday `TXLAQ63…`'te bakiye **0**). Runbook §G.5'in "faucet'in verdiğini kullan" kuralı karşılandı.
- `TRON_API_KEY` dolu. **Geçerliliği kanıtlanamadı:** Nile uydurma bir anahtara da 200 dönüyor, yani "çalıştı" cevabı anahtarı doğrulamıyor. Gerçek kanıt trongrid.io panelindeki istek sayacı ya da provada 429 görmemek.
- `auth.min_steam_account_age_days` **30 → 1** yapıldı (admin API, audit kaydıyla). İkinci hesabın Steam hesabı 4 günlüktü ve giriş kapıda duruyordu. **Prova sonrası 30'a geri alınmalı.**

Bloke eden tek şey (Steam):

- `turkerurganci_2` (`76561198652999063`) Steam hesabı **2026-08-24**'te açıldı ve MA **aynı gün** etkinleştirildi. Steam 7 günden genç MA'ya 15 günlük bekletme uyguluyor, alıcı kapıları da bekletme = 0 şartını arıyor. **7 gün 2026-08-31'de doluyor**; o gün trade URL kaydı hiçbir kısayol olmadan geçer.
- **Denenip geri alınan kısayol:** `MobileAuthenticatorVerified = 1`'i veritabanına elle yazmak **kapıyı açmıyor**. Alıcı kabul ve hazırlık adımları bayrağı hiç okumuyor, canlı probe kullanıyor (`TransactionAcceptanceService.cs:209` yorumunda yazılı). Bayrak yalnız satıcı kapısını besliyor. Tekrar denenmesin.
- Alıcının CS2 envanteri yok ve **gerek de yok** — teslimat referansı bilerek non-blocking. CS2 indirmeye gerek yok, ayrıntı `DEPLOY_RUNBOOK §G.5`'te.

31 Ağustos'ta koşulacak: trade URL kaydı → satıcı ilan açar (`turkerurganci`, envanterinde Tec-9 | Groundwater var) → alıcı kabul → `confirm-ready` → deposit adresi → TronLink'ten USDT → `payment-detected` → 20 blok → `PAYMENT_RECEIVED`.
