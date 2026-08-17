# T137a — E2E harness custodial seed triyajı [ÖLÇÜM GÖREVİ]

**Faz:** F7 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-08-17

---

## Yapılan İşler

T117'nin P2P pivotu (`20260809162642_T117_P2P_Pivot`) üç tabloyu düşürmüştü — `PlatformSteamBots`, `TradeOffers`, `BotRecoveryItems`. E2E harness'ı bunlardan ikisine atıf yapmayı sürdürüyordu ve sekiz advisory E2E leg'in **hepsi** o günden beri kırmızıydı. Bu görev harness'ı P2P şemasına hizaladı ve **ağın ne kadarının ayakta olduğunu ölçtü**.

- `e2e/src/db.ts` cleanup batch'i P2P şemasına göre yeniden yazıldı: emekli iki tablo çıkarıldı, NO-ACTION `TransactionId` FK'sı olan yeni çocuklar (`DeliveryEvidenceCaptures` — T125, `Disputes`, `SellerPayoutIssues`) `Transactions`'tan **önce** silinecek şekilde eklendi, `ItemPriceCaches` silme satırı ikinci batch'ten birinciye taşınıp bound parametreye çevrildi (string interpolation kalktı).
- Bot seed INSERT'ü kaldırıldı; `seed.botId` / `seed.botDisplayName` ve `e2eConfig.botSteamId` alanları silindi.
- Sessiz `.catch(() => undefined)` yerine **uyarı logu** kondu (`[e2e:db] seed cleanup batch failed …`). Kırılmanın dört task boyunca görünmemesinin asıl sebebi bu sessizlikti.
- P2P karşılığı **olmayan** iki custody helper'ı ve 19 çağrı yeri silindi: `pollRefundOfferAccepted` (`TradeOffers` / `RETURN_TO_SELLER`) ve `getBotEscrowCount` (`PlatformSteamBots.ActiveEscrowCount`). Neden silindikleri ve T138'in yerlerine ne koyması gerektiği (ya da koymaması gerektiği) hem `db.ts` içine hem dört spec'in başlık bloğuna not olarak yazıldı.
- 8 leg CI'da koşuldu, sonuç leg bazında kayda geçti (§Ölçüm).

**Sıfır production kaynak değişikliği** — `git diff origin/main...HEAD` yalnız `e2e/` + docs + memory.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `e2e/src/db.ts` | Cleanup batch yeniden yazıldı, bot seed + 2 helper silindi, uyarı logu eklendi |
| `e2e/src/config.ts` | Kullanılmayan `botSteamId` kaldırıldı |
| `e2e/tests/cancellation.spec.ts` | 6 custody assertion satırı + 2 import silindi, başlık notu |
| `e2e/tests/timeout.spec.ts` | 7 custody assertion satırı + 2 import silindi, başlık notu |
| `e2e/tests/emergency-hold.spec.ts` | 4 custody assertion satırı + 2 import silindi, başlık notu |
| `e2e/tests/payment-edge-cases.spec.ts` | 2 custody assertion satırı + 1 import silindi, başlık notu |
| `Docs/11_IMPLEMENTATION_PLAN.md` | T138 kabul kriteri "9 spec" → ölçülen gerçek sayı |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `db.ts`in emekli tabloya yaptığı dört atıf kaldırıldı/karşılığıyla değiştirildi; harness setup'ı 8 leg'in hepsinde spec'lere ULAŞIYOR | ✓ | Gerçek atıf sayısı **8** çıktı (§Bulgular); 8 leg'in hiçbirinde `Invalid object name` / `PK_Users` izi kalmadı, hepsi testleri koşturuyor — CI run [`32044914807`](https://github.com/turkerurganci/Skinora/actions/runs/32044914807) |
| 2 | 8 leg koşuldu, sonuç leg bazında kayda geçti (hangisi geçiyor, hangisi hangi ADIMDA düşüyor) | ✓ | §Ölçüm tablosu (8/8 leg, test bazında pass/fail + düşme adımı) |
| 3 | Ölçüm T138'in kabul kriterlerine işlendi ("9 spec" → gerçek sayı) | ✓ | `Docs/11_IMPLEMENTATION_PLAN.md` §F7 T138: 7 spec yeniden yazım (20 test) + 2 spec noktasal düzeltme (2 test) |

## Bulgular — plandan iki sapma

**B1 — Atıf sayısı dört değil sekiz.** Plan `db.ts` 97 · 102 · 131 · 284'ü sayıyordu. Eksik kalanlar: (a) satır 92 `TradeOffers` cleanup ve satır 268 `pollRefundOfferAccepted` — `TradeOffers` da aynı T117 migration'ında düşmüş; (b) `DeadlineColumn` allow-list'indeki iki emekli kolon adı — T117 `TradeOfferToSellerDeadline → SellerConfirmDeadline` ve `TradeOfferToBuyerDeadline → DeliveryDeadline` yeniden adlandırmasını yapmış, harness eski adları taşıyordu. (b) tabloların **arkasındaki ikinci duvardı**: akış custody durumlarında daha önce öldüğü için hiç görünmemişti, görünse `Invalid column name` verecekti.

**B2 — "Legler spec'lere hiç ulaşmadan ölüyor" tanısı yanlıştı.** Spec'ler koşuyor. SQL Server ad-hoc batch'in nesne adlarını **compile anında** çözümlüyor, dolayısıyla tek bilinmeyen tablo cleanup batch'inin **tamamını** no-op yapıyordu: ilk test bot INSERT'ünde `Invalid object name` ile, sonraki testler cleanup hiç çalışmadığı için `PK_Users` duplicate'iyle düşüyordu. Planın "leg başına tam 1 iz = hiç başlamadı imzası" okuması bu kaskadın sonucunu sebep sanmış. Çürütücü kanıt planın yazıldığı gün de elde edilebilirdi: T113 leg'i o hâlde bile **3 test geçiriyordu** (seed kullanmayan testler).

Her iki sapma da `11_IMPLEMENTATION_PLAN.md` §F7 notuna işlendi.

## Ölçüm — leg bazında sonuç

**Öncesi (main, T129 merge run [`32033733318`](https://github.com/turkerurganci/Skinora/actions/runs/32033733318)):** 32 testten **3'ü pass / 29'u fail**. İz deseni her leg'de aynı: **1** × `Invalid object name 'PlatformSteamBots'` + (n−1) × `PK_Users` duplicate.

**Sonrası (final HEAD `7d6583b`, CI run [`32046880752`](https://github.com/turkerurganci/Skinora/actions/runs/32046880752)):** 32 testten **10'u pass / 22'si fail**. `Invalid object name` ve `PK_Users` izi **sıfır** — harness artık 8 leg'in hepsinde spec'lere ulaşıyor ve testler akış seviyesinde düşüyor.

**Tekrarlanabilirlik:** aynı ölçüm iki bağımsız run'da **birebir** aynı çıktı — [`32044914807`](https://github.com/turkerurganci/Skinora/actions/runs/32044914807) (HEAD `5138cf4`) ve [`32046880752`](https://github.com/turkerurganci/Skinora/actions/runs/32046880752) (HEAD `7d6583b`): leg başına aynı pass/fail sayıları, aynı düşme noktaları. Sayılar tek koşumluk flake değil.

| Leg | Öncesi | Sonrası | Düştüğü ADIM (sonrası) |
|---|---|---|---|
| happy-path | 0/1 | 0/1 | Akış: `timeout awaiting ITEM_ESCROWED (last=ACCEPTED)` |
| T108 cancellation | 0/4 | 0/4 | Akış: 4/4 `ITEM_ESCROWED` (last=ACCEPTED) |
| T109 timeout | 0/4 | **1/4** | test 1 (accept timeout) ✓ geçiyor — P2P'de de geçerli tek faz. test 2 `TRADE_OFFER_SENT_TO_SELLER`, test 3–4 `ITEM_ESCROWED` |
| T110 payment | 0/6 | 0/6 | Akış: 6/6 `ITEM_ESCROWED` |
| T111 fraud-flags | 0/4 | **3/4** | test 3 (high volume) **custody değil, YENİ İŞ KURALI çakışması**: ikinci create `ITEM_ALREADY_LISTED` — T128'in (SellerId, ItemAssetId) tekillik kapısı |
| T112 emergency-hold | 0/3 | 0/3 | Akış: 3/3 `ITEM_ESCROWED` |
| T113 admin-flows | 3/7 | **6/7** | AC1: `steamAccounts.length ≥ 1` — alan `AdminDashboardResponse`'ta yok (kaynak teyidi: `AdminDashboardDtos.cs` yalnız `summaryCards` + `recentFlags`) |
| T114 downtime | 0/3 | 0/3 | Akış: 2 × `ITEM_ESCROWED`, 1 × `TRADE_OFFER_SENT_TO_SELLER` |

**Ölçümün T138'e çevirisi (kriter 3):** "9 spec" tahmini yerine **7 spec yeniden yazım (20 test) + 2 spec noktasal düzeltme (2 test)**. Ayrıntı ve gerekçeler `11_IMPLEMENTATION_PLAN.md` §F7 T138 kabul kriterlerinde.

**Ölçümün T137'ye çevirisi (görevin ikinci amacı — "T137'nin aciliyetini ölçer"):** Fake sidecar'ın `/api/inventory/:steamId` ucu `steamId` parametresini **yok sayıyor** (`sidecar-fake/src/routes/steam.ts:40` — tek sabit `INVENTORY_ITEMS` listesi, satıcı ve alıcı aynı envanteri görüyor). P2P'nin çekirdek kanıtı "item satıcıdan çıktı, alıcıda göründü" (T125 baseline diff'i) bu yüzden simüle **edilemiyor**. Sonuç: 9 spec'ten 8'i (yalnız `admin-flows` hariç) T137 olmadan yeşile dönemez — **T137 kritik yolun üstünde**, proje sahibinin paralel başlatma kararı ölçümle doğrulandı. Yan bulgu: fake'te ikinci bir item var (`11111111002` AWP), yani T111'in high-volume testi T137'yi beklemeden düzelebilir — ama o item'ın `ItemPriceCaches` satırı olmadığı için harness'a ikinci cache satırı gerekir (T138 detayı).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| tsc | ✓ 0 hata | `npx tsc --noEmit` (e2e) |
| eslint | ✓ 0/0 | `npx eslint .` (e2e) |
| prettier | ✓ temiz | `npx prettier --end-of-line auto --check "src/**/*.ts" "tests/**/*.ts"` — lokal CRLF artefaktı ayıklandı, CI "1. Lint" LF üzerinde yetkili |
| E2E | ölçüm (yeşil beklenmiyor) | §Ölçüm |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: `E2E_BOT_STEAM_ID` artık harness tarafından okunmuyor (compose'da tanımlı değildi; fake sidecar'ın `FAKE_BOT_STEAM_ID`'si duruyor — T137 kapsamı)
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T137a-e2e-harness-triage`
- Commit: `9e8df29` — emekli tablo atıfları + custody helper'ları · `5138cf4` — deadline allow-list'i T117 rename'ine hizalama
- PR: [#243](https://github.com/turkerurganci/Skinora/pull/243)
- CI: ✓ **run [`32048912041`](https://github.com/turkerurganci/Skinora/actions/runs/32048912041) `conclusion=success`** (HEAD `9835824`) — CI Gate ✓ + "1. Lint" ✓; 8 advisory E2E leg beklendiği gibi kırmızı, run conclusion'ını düşürmüyor (§Notlar). **E2E yüzeyi `5138cf4`'ten beri donmuş** (sonraki commit'ler yalnız `Docs/` + `.claude/`), dolayısıyla o commit'ten sonraki üç run — `32044914807`, `32046880752`, `32048912041` — aynı e2e kodunu ölçüyor ve aynı sonucu veriyor. Bu raporu sonlandıran commit'in kendi run'ı, doğası gereği rapora yazılamaz (doküman-only, e2e'ye dokunmaz); doğrulama chat'i dal HEAD'inin run'ına bakmalıdır.

## Known Limitations / Follow-up

- **Bu görev hiçbir leg'i yeşile çevirmiyor** ve çevirmemesi gerekiyor: 22 test P2P akışı yeniden yazılana kadar (T138) kırmızı kalır. Yeşil beklentisi T137 + T135 + T138 zincirinin sonunda doğar.
- `happy-path.ui.spec.ts` CI matrisinde **yok** (8 leg, `test:ui` dışarıda) — bu spec hiç ölçülmedi, T138'in kabul kriterine not olarak eklendi.
- T111 high-volume testinin `ITEM_ALREADY_LISTED` çakışması T128'in getirdiği **yeni iş kuralı**yla ilgili; harness kusuru değil, T138 kapsamı (ikinci assetId + ikinci `ItemPriceCaches` satırı).
- `admin-flows` AC1'in `steamAccounts` assertion'ı 03 §8.1'in custody-era "Platform Steam hesaplarının durumu" bloğuna dayanıyor; doküman tarafının emekliye ayrılması T133a/T136 kapsamı.

## Notlar

**Working tree (task.md Adım -1):** Kirli — T130 yapımı (23 değişik + 5 yeni dosya, migration dahil) başka bir chat'te commit'lenmemiş hâlde duruyordu. Proje sahibi kararı (2026-08-17): **izole worktree** — `git worktree add c:/projects/Escrow-T137a -b task/T137a-e2e-harness-triage origin/main`. T130 çalışma alanına dokunulmadı.

**Adım 0 (main CI startup):** Son 3 tamamlanmış run success — `32039187802`, `32039187921`, `32033733318`.

**Dış varsayımlar:** Yok. Görev yalnız repo içi harness kodunu ve mevcut CI matrisini kullanıyor; yeni paket, plan tier'ı veya dış API varsayımı yok.

**CI run conclusion'ı hakkında:** Bu görevde 8 advisory leg'in kırmızı kalması **beklenen** sonuçtur (plan: "bu görevin ÇIKTISI yeşil leg değil, ÖLÇÜMDÜR"). Buna rağmen final run [`32046880752`](https://github.com/turkerurganci/Skinora/actions/runs/32046880752) `conclusion=success` verdi (`gh run watch --exit-status` → exit 0): `continue-on-error: true` bir job'ın **adım** başarısızlığını run seviyesine taşımıyor. Ara run'ların `failure` görünmesinin sebebi advisory legler değildi — o legler GitHub `codeload` **429/503 flake'i** yüzünden `Set up job` aşamasında ölmüştü ve job-setup çökmesi `continue-on-error` ile maskelenmiyor. Bitiş Kapısı bu yüzden literal olarak karşılanıyor: final HEAD'in run'ı `success`, CI Gate ✓, "1. Lint" ✓; diğer blocking job'lar path filtresi gereği skipped (`e2e/**` değişikliği `code` filtresini tetiklemez).
