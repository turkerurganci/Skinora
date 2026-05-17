# T74 — Blockchain Sidecar — energy delegation

**Faz:** F4 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-17

---

## Yapılan İşler

**Sidecar (`sidecar-blockchain`):**

- `TronDelegationClient` (yeni) — TronWeb 5.x `transactionBuilder.delegateResource(balance, receiver, 'ENERGY', owner, lock=false)` + `undelegateResource(balance, receiver, 'ENERGY', owner)` + `sendTrx(to, amount, from)` thin wrapper. Her çağrı için `TronWebFactory` injection (test'lerde stub geçilir, prod path yeni `new TronWeb({fullHost, headers: {TRON-PRO-API-KEY}, privateKey})` binding üretir). Mevcut `TronTransferClient` pattern'ini birebir aynalır (signing isolation 05 §3.3).
- `EnergyDelegationService` (yeni) — orchestration: `withDelegation<T>(depositAddress, action, context)` generic envelope. Akış 08 §3.3 birebir: (1) `delegateEnergy` sweeper→deposit, (2) `action()` (sweep/refund broadcast), (3) `undelegateEnergy` reclaim. Sonuç `DelegationOutcome<T>` — `mode: 'delegated' | 'fallback'`, `delegationAmountSun`, `fallbackAmountSun`, `action: T`.
- **Failure semantics** (`EnergyDelegationService`):
  - `delegateEnergy` başarısız → `sendTrx` ile fallback TRX prefund (`SWEEP_TRX_FALLBACK_SUN`, default 15 TRX); fallback başarılıysa `mode='fallback'`, undelegate yok (delegate gerçekleşmediği için).
  - `delegateEnergy` + `sendTrx` ikisi de fail → `DELEGATION_AND_FALLBACK_FAILED` retryable=true, dispatcher SystemSetting retry policy ile tekrar dener (T73 `blockchain.transfer_retry_intervals_minutes`).
  - `action()` başarısız + `mode='delegated'` → undelegate yine de denenir (try/finally pattern), sonra action hatası propagated (broadcast başarısızsa bir sonraki retry'da budget kalmaması için).
  - `undelegateEnergy` başarısız (action başarılıyken) → `logger.warn` + action sonucu döndürülür. Sweep zaten zincirde, sweep'i yeniden broadcast etmek double-spend riski (admin investigation, K3 forward).
- `TransferService.sweep()` — `EnergyDelegationService.withDelegation` ile sarıldı. `SweepResult` artık `txHash` + `delegationMode` + `delegationAmountSun` + `fallbackAmountSun` döner. `energyDelegation` injection unwired ise `DELEGATION_NOT_WIRED` non-retryable hata (defense-in-depth — sweep delegation olmadan deposit'ten transfer denemesi `OUT_OF_ENERGY` üretirdi).
- `RefundService.refund()` — aynı pattern, `RefundResult` ile delegation mode audit alanları. Backend dispatcher refund family (5 type) bu surface'i kullanıyor.
- `api/transferHandlers.ts` — `sweep`/`refund` handler response'ları artık `{ txHash, delegationMode, delegationAmountSun, fallbackAmountSun }` döner. Payout etkilenmedi (hot wallet kendi gas'ını öder, delegation gereksiz).
- `config/index.ts` — 2 yeni env değişkeni:
  - `SWEEP_ENERGY_DELEGATION_SUN` (default 200_000_000 SUN = 200 TRX — Stake 2.0 oranıyla ~16k Energy headroom; TRC-20 transfer ~65k Energy 08 §3.3, payı içerir)
  - `SWEEP_TRX_FALLBACK_SUN` (default 15_000_000 SUN = 15 TRX — 08 §3.3 "TRC-20 transfer ~13-15 TRX gas")
- `index.ts` DI wiring: `TronDelegationClient` + `EnergyDelegationService` instance, hot wallet credentials sweeper olarak kullanılır (scope kararı 2026-05-17).
- Vitest unit testleri (104/104 ✓):
  - `TronDelegationClient.test.ts` (10) — delegate ENERGY+lock=false sözleşmesi, undelegate, sendTrx, build_failed (no txID), broadcast_rejected (result=false), network exception → DELEGATE_BROADCAST_FAILED retryable, no_private_key non-retryable, SidecarError preservation.
  - `EnergyDelegationService.test.ts` (10) — delegated happy path (delegate→action→undelegate), fallback path (delegate fail → sendTrx success), DELEGATION_AND_FALLBACK_FAILED (her ikisi fail), undelegate fault tolerance (action result preserved), action-fail with delegated → undelegate still attempted, action-fail with fallback → no undelegate, generic action return type preservation, configuration guards (SWEEPER_NOT_CONFIGURED, INVALID_DELEGATION_AMOUNT, INVALID_FALLBACK_AMOUNT).
  - `TransferService.test.ts` (14 — T73'te 9, T74'te 5 yeni) — sweep delegation path success + reports delegated mode, sweep reports fallback mode, broadcast error propagation through envelope, DELEGATION_NOT_WIRED guard, DEPOSIT_ADDRESS_MISMATCH (delegation çağrılmamalı); refund happy path + delegation mode reporting, refund fallback mode, refund DELEGATION_NOT_WIRED guard.

**Backend (`Skinora.Platform` + `Skinora.Shared`):**

- 2 yeni SystemSetting (06 §3.17, 53. ve 54. anahtarlar — wait, total artık 53):
  - `blockchain.sweep_energy_delegation_sun` (string, default `"200000000"`, kategori `Monitoring`, API kategori `blockchain_health`, birim `SUN`)
  - `blockchain.sweep_trx_fallback_sun` (string, default `"15000000"`, kategori `Monitoring`, API kategori `blockchain_health`, birim `SUN`)
- `SystemSettingsCatalog.cs` — 2 yeni metadata entry (admin UI'da `blockchain_health` kategorisinde görünür, label "Sweep/refund Energy delegation tutarı (T74)" / "Energy delegation fallback TRX tutarı (T74)").
- Migration `20260517111404_T74_AddSweepDelegationSettings` — Up: 2 row `InsertData`; Down: 2 row `DeleteData`. Şema değişikliği yok, sadece seed delta. 11 → 12 migration zinciri.
- `SeedDataTests.cs` — count 51 → 53; configured key listesi 30 → 32 (yeni 2 anahtar alfabetik sırada `blockchain.sweep_*` arasına eklendi); class XML doc T74 referansı.

**Mimari notu — Sidecar config ↔ Backend SystemSetting drift:** Sidecar `SWEEP_ENERGY_DELEGATION_SUN` / `SWEEP_TRX_FALLBACK_SUN` env'i okur. Backend SystemSetting tablosu aynı değerleri admin görünürlüğü/audit için tutar ama sidecar'a runtime'da propagate etmez (env restart gerekir). Bu MVP scope kararı (2026-05-17 onaylı) — Backend→Sidecar live setting fetch endpoint'i T-future. **K1 olarak Known Limitations'ta dokümante.**

## Etkilenen Modüller / Dosyalar

**Yeni (sidecar):**

- [`sidecar-blockchain/src/tron/TronDelegationClient.ts`](../../sidecar-blockchain/src/tron/TronDelegationClient.ts)
- [`sidecar-blockchain/src/tron/TronDelegationClient.test.ts`](../../sidecar-blockchain/src/tron/TronDelegationClient.test.ts)
- [`sidecar-blockchain/src/wallet/EnergyDelegationService.ts`](../../sidecar-blockchain/src/wallet/EnergyDelegationService.ts)
- [`sidecar-blockchain/src/wallet/EnergyDelegationService.test.ts`](../../sidecar-blockchain/src/wallet/EnergyDelegationService.test.ts)

**Güncellenen (sidecar):**

- [`sidecar-blockchain/src/transfer/TransferService.ts`](../../sidecar-blockchain/src/transfer/TransferService.ts) — sweep delegation wrap + SweepResult shape
- [`sidecar-blockchain/src/transfer/RefundService.ts`](../../sidecar-blockchain/src/transfer/RefundService.ts) — refund delegation wrap + RefundResult shape
- [`sidecar-blockchain/src/transfer/TransferService.test.ts`](../../sidecar-blockchain/src/transfer/TransferService.test.ts) — 5 yeni delegation path testi + mevcut sweep/refund testlerinde stub delegation injection
- [`sidecar-blockchain/src/api/transferHandlers.ts`](../../sidecar-blockchain/src/api/transferHandlers.ts) — sweep + refund response shape (delegationMode audit alanları)
- [`sidecar-blockchain/src/config/index.ts`](../../sidecar-blockchain/src/config/index.ts) — sweepEnergyDelegationSun + sweepTrxFallbackSun
- [`sidecar-blockchain/src/index.ts`](../../sidecar-blockchain/src/index.ts) — TronDelegationClient + EnergyDelegationService DI

**Yeni (backend):**

- [`backend/src/Skinora.Shared/Persistence/Migrations/20260517111404_T74_AddSweepDelegationSettings.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260517111404_T74_AddSweepDelegationSettings.cs) (+ Designer)

**Güncellenen (backend):**

- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs) — 51 → 53 row (index 52 + 53 T74 anahtarları)
- [`backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`](../../backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs) — 2 yeni `blockchain_health` entry
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — 2 yeni seed row otomatik
- [`backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs`](../../backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs) — count 51→53, configured liste 30→32 + alfabetik düzene 2 yeni anahtar

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sweep öncesi deposit adresine geçici Energy delegation | ✓ | `EnergyDelegationService.withDelegation` ilk adım `client.delegateEnergy({owner=sweeper, receiver=deposit, amountSun, lock=false})` çağırır → sweep broadcast'i bunun ardından çalışır. Test: `EnergyDelegationService.test.ts` "delegates, runs the action, then undelegates" — call order assertion + `delegateEnergy` argümanları (ENERGY resource, sweeper credentials). `TransferService.sweep()` wrap test: "derives signer, delegates Energy, broadcasts deposit -> hot wallet and reports delegated mode" + `delegationMode='delegated'` `delegationAmountSun=200_000_000`. |
| 2 | `delegateresource` çağrısı | ✓ | `TronDelegationClient.delegateEnergy` `tronWeb.transactionBuilder.delegateResource(balance, receiver, 'ENERGY', owner, lock=false)` invocation. Test: `TronDelegationClient.test.ts` "builds, signs and broadcasts a delegateResource call with ENERGY + lock=false" → 5 argüman kontrol edilir (200_000_000, RECEIVER_ADDR, 'ENERGY', OWNER_ADDR, false). Error path testleri: DELEGATE_BUILD_FAILED (no txID), DELEGATE_BROADCAST_REJECTED (result=false), DELEGATE_BROADCAST_FAILED (network exception). |
| 3 | Sweep sonrası `undelegateresource` ile geri alım | ✓ | `EnergyDelegationService.withDelegation` `tryUndelegate()` action başarılı (`action-succeeded`) ve `mode='delegated'` ise `client.undelegateEnergy({owner=sweeper, receiver=deposit, amountSun})` çağrılır. Test: `EnergyDelegationService.test.ts` happy-path call sırası kontrolü + `TronDelegationClient.test.ts` "builds, signs and broadcasts an undelegateResource call (no lock parameter)" → 4 argüman (no `lock` çünkü undelegateResource Stake 2.0 imzasında lock yok). Undelegate fail toleransı: action result preserved, log warn (test "preserves the action result when undelegate broadcast fails"). |
| 4 | Fallback: delegation başarısızsa deposit adresine minimum TRX transfer | ✓ | `EnergyDelegationService.acquireBudget` `delegateEnergy` throw eder → catch içinde `sendTrx({from=sweeper, to=deposit, amountSun=fallback})` denenir; fallback başarılı ise `mode='fallback'`. Test: `EnergyDelegationService.test.ts` "falls back to TRX prefund when delegation broadcast fails" → fallback path call sırası + `sendTrx` argüman kontrolü (sweeper signer, 15_000_000 SUN). Her ikisi de başarısız ise `DELEGATION_AND_FALLBACK_FAILED` retryable=true (test "raises DELEGATION_AND_FALLBACK_FAILED when both delegate and sendTrx fail"). `TronDelegationClient.sendTrx` testi: "builds, signs and broadcasts a TRX transfer used as 08 §3.3 fallback" → `transactionBuilder.sendTrx(RECEIVER_ADDR, 15_000_000, OWNER_ADDR)` invocation. |

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 08 §3.3 energy delegation akışı doğru mu? | ✓ | 08 §3.3 birebir: (a) sweeper account hot wallet (scope kararı 2026-05-17 — MVP'de tek-account modeli); (b) `delegateresource` sweep öncesi `lock=false` ile (`EnergyDelegationService` flow ilk adım); (c) `undelegateresource` sweep sonrası (try/finally ile reclaim); (d) fallback minimum TRX transfer (sendTrx ile, default 15 TRX/15_000_000 SUN); (e) `mode` ve `amountSun` outcome audit alanları sweep/refund response'unda döner. SystemSetting `blockchain.sweep_energy_delegation_sun` (default 200 TRX) + `blockchain.sweep_trx_fallback_sun` (default 15 TRX) admin tunable. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar Vitest | ✓ **104/104 PASS** | `npm test` — toplam 8 suite, 104 test, 1.26s. T73'te 79 idi, T74 yeni 25 (10 TronDelegationClient + 10 EnergyDelegationService + 5 TransferService delegation path). |
| Backend Unit (non-integration) | ✓ **~866 PASS, 0 FAIL** | `dotnet test Skinora.sln -c Release --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~InitialMigration"` — Shared 189 + Users 16 + Auth 57 + Platform 102 + Fraud 14 + Transactions 386 + Notifications 49 + Steam 13 + Realtime 25 + API 15. Disputes.Tests modülü tamamen Integration filter ile gizlendi (lokalde "No test matches" warning). |
| Backend Build Release | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` — 25.16s build, 0 Warning + 0 Error tüm 25+ projede. |
| Backend `dotnet format --verify-no-changes` | ✓ PASS (Δ=0) | Tek run'da sessiz çıktı (no changes required). |
| Sidecar `npm run format:check` | ⚠ pre-existing drift (T73 K6 ile aynı havuz) | T74 yeni dosyaları (`TronDelegationClient.test.ts`, `EnergyDelegationService.test.ts`) formatlandı; pre-existing 23 dosya T64-T72 boyunca biriken drift; ayrı chore PR önerilir. |
| Lokal Testcontainers integration | ⚠ env-skip | Lokalde Docker Desktop kapalı — F4 envelope. CI Linux runner'da Testcontainers ile çalışır (SeedDataTests 53 row, Integration test'ler 11. migration zincirini fresh DB üzerinde rehearse eder). |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Yapım bitti, validate chat'ine geçilebilir (validator izolasyon — INSTRUCTIONS.md §3.3) |
| Bulgu sayısı | — (validator değerlendirmesinden önce) |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** **Var** — `20260517111404_T74_AddSweepDelegationSettings` — 2 row `InsertData` (Id `0aa51010-0000-0000-0000-000000000034` + `...00035`); Down: 2 row `DeleteData`. Şema değişikliği yok, sadece seed delta. 11 → 12 migration zinciri.
- **Config/env değişikliği:**
  - **Sidecar** — 2 yeni env: `SWEEP_ENERGY_DELEGATION_SUN` (opsiyonel, default 200_000_000 SUN), `SWEEP_TRX_FALLBACK_SUN` (opsiyonel, default 15_000_000 SUN). Eksikse default değerlerle başlar. Hot wallet credentials (T73'te eklendi) sweeper olarak kullanılır — yeni credential gerekmez.
  - **Backend** — 2 yeni SystemSetting key (`blockchain.sweep_energy_delegation_sun`, `blockchain.sweep_trx_fallback_sun`). Admin tarafından `PATCH /admin/settings` ile değiştirilebilir; **runtime'da sidecar'a propagate olmaz** (env restart gerekir — K1 forward).
- **Docker değişikliği:** **Yok** — yeni env değişkenleri opsiyonel default'larla geliyor; docker-compose.yml güncellemesi gereksiz (admin override istiyorsa env'e ekler).

## Commit & PR

- Branch: `task/T74-energy-delegation`
- Commit: (yapım commit hash TBD — push öncesi)
- PR: (TBD — push sonrası `gh pr create`)
- CI: (TBD — PR açılınca `gh run watch` ile izlenir)

## Known Limitations / Follow-up

- **K1 — Backend SystemSetting → Sidecar live propagation eksik.** Admin `PATCH /admin/settings` ile `blockchain.sweep_energy_delegation_sun` veya `blockchain.sweep_trx_fallback_sun` değiştirirse sidecar bunu okumaz; sidecar restart (yeni env değeriyle) gerekir. MVP scope kararı 2026-05-17 (Backend→Sidecar fetch endpoint T-future). Audit ve admin görünürlüğü için backend tarafında değerler kayıtlı; runtime drift admin'in farkında olduğu yönetilen bir durum.
- **K2 — Sweep dispatcher otomatik tetikleyici T-future (T73 K2 ile aynı).** Backend `OutgoingTransferDispatchJob` sadece SELLER_PAYOUT + refund family row'larını picker. SWEEP-tipi BlockchainTransaction row üreten konsumer (PaymentReceivedEvent → SWEEP row) henüz yok. T74 sweep primitive + delegation primitive ready, backend orkestre etmiyor. Plan tanımında T74 scope'unda backend dispatcher değişikliği yok (sidecar primitive odaklı, 2026-05-17 onaylı).
- **K3 — Undelegate fail "stranded delegation" admin notification.** `EnergyDelegationService.tryUndelegate` warn log emitir ama admin'e push/email göndermez (stranded delegation TRX'i hot wallet'tan geçici olarak kilitlemiş kalır — Stake 2.0 modelinde 3 gün sonra otomatik geri alınabilir veya manuel `undelegateresource`). Admin notification consumer T96 forward devir.
- **K4 — Sidecar pre-existing prettier drift (T73 K6 ile aynı havuz).** T64-T73 boyunca biriken 23 dosya format drift; T74 yeni dosyaları formatlandı, geri kalan dosyalar ayrı chore PR.
- **K5 — Stake 2.0 oranı volatil — `SWEEP_ENERGY_DELEGATION_SUN` default 200 TRX konservatif.** Tron mainnet'te 1 TRX delegate karşılığı Energy oranı ~70-85/TRX arası dalgalanır (network upgrade + congestion). 200 TRX × 80 = 16,000 Energy headroom. TRC-20 transfer ~65k Energy gerektirir → 200 TRX bir sweep için **yetersiz olabilir** (oran düşerse). Default değer admin tarafından artırılabilir (örn. 500 TRX = 40k Energy). Production'da admin runtime monitoring + `getAccountResources` query ile düzenleme gerekebilir. Default değer plan tanımındaki 08 §3.3 "~65.000 Energy" ile uyumlu hedeflenmiş ama Stake 2.0 dönüşüm oranı dış değişken — admin sorumluluğunda.
- **K6 — Energy delegation `lock=false` mode = 3 gün otomatik unlock.** Stake 2.0 spec: `lock=false` delegasyonlar anında reclaim edilebilir; admin/sidecar reclaim yapmasa bile 3 gün sonra TRON ağı otomatik unlock eder. T74 her sweep sonrası manuel reclaim yapar; 3-gün fallback yalnız `undelegate` fail durumunda devreye girer (K3).
- **K7 — `delegateresource`/`undelegateresource` gas tüketir (~14 TRX/işlem hot wallet'tan).** Her sweep = 1 delegate + 1 undelegate = 2 ekstra Tron işlemi. Hot wallet TRX bakiyesi yeterli olmalı; bakiye monitoring T77 scope.
- **K8 — Capacity-based delegation routing T-future.** Hot wallet'ın Stake 2.0 Energy budget'ı sabit (admin'in stake ettiği TRX × oran). Aynı anda 100+ deposit sweep istendiğinde delegation tek sweeper'dan dağıtılır → cumulative budget aşılırsa delegateResource fail olur → fallback path aktif olur. Multi-sweeper account routing T-future optimization.

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş çıktı, T73 merge sonrası temiz başlangıç.
- **Main CI startup (Adım 0):** Son 3 main run 3/3 SUCCESS — `25987227889` (T73 #114), `25987227898` (T73 #114 paralel), `25972872805` (T72 #113). ✓ Hard stop tetiklenmedi.
- **Repo memory drift (Adım 0b):** `.claude/memory/MEMORY.md` "Current Status" bloğu T73 ile güncel (push #114 sonrası yansıtıldı). T74 satırı bu yapım chat'inde eklenecek.
- **Dış Varsayımlar (Adım 4):**
  - **TronWeb 5.3.5 `delegateResource` + `undelegateResource` API mevcut** — `node -e "const TronWeb = require('tronweb'); const tw = new TronWeb({fullHost:'https://api.trongrid.io'}); console.log(typeof tw.transactionBuilder.delegateResource);"` → `function`. Aynı şekilde `undelegateResource: function`, `getAccountResources: function`. TronWeb version: `5.3.5`. ✓ Doğrulandı.
  - **TronGrid `/wallet/delegateresource` + `/wallet/undelegateresource` endpoint'leri** — 08 §3.1 endpoint tablosunda mevcut (satır 385-386). TronWeb 5.x bunları `transactionBuilder.delegateResource/undelegateResource` üzerinden çağırır. ✓.
  - **Stake 2.0 model** — Tron 2022'de Stake 2.0'a geçti (`DelegateResourceContract`/`UnDelegateResourceContract` proto). TronWeb 5.x bu modelin imzasını kullanır (`balance` parametresi = TRX freeze amount SUN cinsinden, Energy chain tarafında hesaplanır). ✓.
  - **`sendTrx` builder method** — TronWeb 5.3.5'te `tw.transactionBuilder.sendTrx(to, amount, from)` mevcut (TRX transfer için). `getAccountResources(address)` da var ama T74 scope'unda kullanılmadı (proactive health check T-future).
  - **Hot wallet sweeper modeli onayı** — 2026-05-17 AskUserQuestion 4 soru, tümü "Recommended" seçeneklerle yanıtlandı: (a) hot wallet = sweeper, (b) sweep/refund flow içinde gömülü delegation, (c) SystemSetting + default, (d) sadece SystemSetting seed (backend dispatcher değişikliği yok).
  - **Default value plausibility** — 200 TRX delegation Stake 2.0 ~80 Energy/TRX oranıyla 16,000 Energy karşılığı; TRC-20 transfer ~65k Energy gerektiriyor (08 §3.3). **Default değer TRC-20 transfer için yetersiz** olabilir — K5 ile dokümante edildi, admin runtime ayar yapmalı. 15 TRX fallback 08 §3.3 "13-15 TRX gas" tablosuyla uyumlu.
- **Scope onayı (2026-05-17):** Proje sahibi `AskUserQuestion` 4 soru-yanıt akışında onay verdi (yukarıda doğrulanan). Plan tanımına uygun sidecar-only scope.
- **Squash-merge bundled-PR guard:** T74 commit'leri yalnızca `T74:` prefix taşıyacak (commit-msg hook real-time enforce eder + Bitiş Kapısı git log mekanik check).
