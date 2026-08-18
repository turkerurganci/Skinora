# T131 — AdminDisputeService: item-refund bacağı + override

**Faz:** P5 | **Durum:** ✓ Tamamlandı (tur 1 ✗ FAIL → düzeltme turu → tur 2 ✓ PASS) | **Tarih:** 2026-08-17 (yapım) · 2026-08-18 (doğrulama tur 1) · 2026-08-18 (düzeltme turu) · 2026-08-18 (doğrulama tur 2)

---

## Yapılan İşler

### AC1 — Item iade bacağı kaldırıldı (sözleşme kalıntıları)
Bacak **kodda zaten yoktu**: T117 `ItemRefundToSellerRequestedEvent` tipini sildi ve
`AdminDisputeService` yerine açık bir negatif yorum taşıyordu. Kapatılan şey, bacağın hâlâ
**var olduğunu söyleyen** dört kalıntıdır:

| Yer | Ne diyordu |
|---|---|
| `07 §9.30` AD29 | "item platformdaysa `ItemRefundToSellerRequestedEvent` yayınlanır" |
| `06 §3.11` WP5 notu | "alıcı iade + item platformdaysa satıcıya iade" |
| `DisputeResolutionOutcome.cs` XML doc | "REFUNDED + buyer refund / **seller item-return**" |
| `DisputeResolvedEvent.cs` + `IAdminDisputeService.cs` XML doc | "refund/**return** events" |

07 §9.20/§9.22 iade tablolarına **dokunulmadı** — onlar plan gereği T133a'nın
(`11_IMPLEMENTATION_PLAN.md:3220`). T131 yalnız kendi ucunun (AD29) sözleşmesini kapattı.

Ek hijyen (aynı dosyada, aynı sınıf hata): `DisputeResolvedEvent.BuyerId` param doc'u hâlâ
"the dispute opener" diyordu — T127 bulgusu B3 alanı işleme taşımıştı.

### AC2 — Kanıtlanmış teslimatta BUYER_FAVOR gerekçe istiyor
`AdminDisputeService.ResolveAsync`'e **Stage 5b override kapısı** eklendi. AD29 body'si
`overrideReason` (≥20 karakter) alıyor; kanıtlanmış teslimatta eksikse istek
**400 `OVERRIDE_REASON_REQUIRED`** ile reddediliyor.

Kalıcı kayıt: yeni kolon `Disputes.ResolutionOverrideReason` (nvarchar(2000), NULL) +
`DISPUTE_RESOLVED` audit satırının `NewValue` JSON'ı. İkisi birden, çünkü kolon **okunduğu**
yer, audit satırı (06 §3.20, append-only) **değiştirilemediği** yer.

AD28 `buyerFavorRequiresOverride` (sunucuda hesaplanmış bool) döndürüyor; kural tek bir
private helper'da (`RequiresOverrideReason`) yaşıyor ve hem AD28 hem AD29 onu okuyor.

### AC3 — `deliveredItemName` admin ekranında
FE `AdminDisputeDetail` tipine `deliveredItemName` + `resolutionOverrideReason` +
`buyerFavorRequiresOverride` eklendi; `DisputeResolveModal` gelen item adını **beklenen item
adının hemen altında** çiziyor (alan yoksa satır hiç çizilmiyor). Karşılaştırmanın iki tarafı
da artık **aynı fetch'ten** geliyor (`detail.transaction.itemName`), liste satırı yalnız
detay inene kadar placeholder.

### AC4 — SELLER_FAVOR sonrası terminal disposition (T127 gözlem G3)
Re-entry kapısı (`DeliveryTimeoutRound`) misdelivery imzası taşıyan bir satırı **admin karar
verse bile** sonsuza kadar `Held` döndürüyordu → satır `PAYMENT_RECEIVED`'da ve süresi dolmuş
kalıyor, alıcının parası emanette asılı kalıyordu.

Port enum'una `MisdeliveryEscalationOutcome.AlreadyRuledByAdmin` eklendi (adapter dispute
statüsünü görür, tur göremez — modül yönü Disputes → Transactions). Adapter `RESOLVED_FOR_*`
için bu değeri döndürüyor, `CLOSED` için eski `AlreadyResolved`'da kalıyor. Tur, yalnız
birincisinde `Cancel` döndürüyor → timeout olağan seyrini izliyor → alıcıya iade.

Dayanak: 02 §9.2'nin yasakladığı şey **sessiz** iptaldir; bir admin o satırı okuyup karar
verdiği an iptal sessiz olmaktan çıkar. `CLOSED` kapıyı açmaz çünkü o, sistemin kendi
otomatik çözümüdür (06 §2.10) — kimse bakmamıştır.

## Etkilenen Modüller / Dosyalar

**Backend — kaynak (11):**
- `Skinora.Shared/Enums/DisputeResolutionOutcome.cs` — XML doc (AC1)
- `Skinora.Shared/Events/DisputeResolvedEvent.cs` — XML doc (AC1) + `BuyerId` param doc
- `Skinora.Disputes/Application/Admin/IAdminDisputeService.cs` — XML doc (AC1)
- `Skinora.Disputes/Application/Admin/AdminDisputeDtos.cs` — AD28 +2 alan, AD29 +1 alan
- `Skinora.Disputes/Application/Disputes/DisputeErrorCodes.cs` — `OVERRIDE_REASON_REQUIRED`
- `Skinora.Disputes/Domain/Entities/Dispute.cs` — `ResolutionOverrideReason`
- `Skinora.Disputes/Infrastructure/Persistence/DisputeConfiguration.cs` — kolon config
- `Skinora.Disputes/Application/Disputes/MisdeliveryDisputeEscalator.cs` — AC4 adapter kolu
- `Skinora.Transactions/Application/Delivery/IDeliveryMisdeliveryEscalator.cs` — AC4 enum
- `Skinora.Transactions/Application/Delivery/DeliveryTimeoutRound.cs` — AC4 serbest bırakma
- `Skinora.API/Services/AdminDisputeService.cs` — AC1 doc + AC2 kapı/kayıt/audit + AD28

**Backend — migration (2):** `20260817184054_T131_DisputeResolutionOverrideReason.{cs,Designer.cs}`

**Frontend (8):** `lib/api/admin.ts` · `lib/hooks/useAdminDisputeResolve.ts` ·
`components/admin/DisputeResolveModal.tsx` · `i18n/messages/{en,tr,es,zh}.json` (+6 anahtar ×4)

**Test (3):** `AdminDisputeServiceTests.cs` (+11) · `DeliveryTimeoutRoundTests.cs` (+3) ·
`MisdeliveryDisputeEscalatorTests.cs` (Theory genişletildi)

**Doküman (6):** `02` v3.5→v3.6 · `03` v3.4→v3.5 · `05` v3.3→v3.4 · `06` v6.8→v6.9 ·
`07` v3.5→v3.6 · `11_IMPLEMENTATION_PLAN.md` (T131 bloğu — NİHAİ ŞEKİL + AC4)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Item iade bacağı kaldırıldı | ✓ | `grep -rn "item-return\|ItemRefundToSeller\|item platformdaysa" backend/src Docs/07_API_DESIGN.md Docs/06_DATA_MODEL.md` → kalan **6 isabetin altısı da olumsuz/belgeleyen** cümle ("No item-return branch…", "There is no item-return effect…") + bu turun kendi 07 changelog satırı; bacağın **var olduğunu** söyleyen tek bir satır kalmadı. AD29 + 06 §3.11 + 3 XML doc temizlendi |
| 2 | Kanıtlanmış teslimatta BUYER_FAVOR gerekçe istiyor | ✓ | `Resolve_BuyerFavor_AtItemDelivered_WithoutOverrideReason_IsRejected` · `…WithATokenOverrideReason_IsRejected` · `…PersistsTheOverrideReason_AndAuditsIt` · `Resolve_SellerFavor_AtItemDelivered_NeedsNoOverrideReason` · `Resolve_OverrideReason_OnARulingThatOverrodeNothing_IsNotStored` · `Resolve_OnAHeldTransaction_ReportsTheHold_NotTheMissingOverride` |
| 3 | AD28 `deliveredItemName` admin ekranında | ✓ | Backend: `Get_ReturnsTheDeliveredItemName_ForTheAdminToCompare` (DTO'da ilk assertion — T130'da hiç yoktu). FE: `DisputeResolveModal.tsx` koşullu satır + `fields.deliveredItemName` ×4 dil; `npm run i18n:check` → parity OK 4×1319 |
| 3b | i18n 4 dil parity | ✓ | `i18n parity OK — 4 locales, 1319 keys each, identical key sets.` |
| 4 | SELLER_FAVOR sonrası terminal disposition (G3) | ✓ | `An_Admin_Ruling_Releases_The_Misdelivery_Hold_To_Cancellation` · `A_System_Closed_Dispute_Does_Not_Release_The_Hold` · `Resolved_Dispute_Is_Left_Alone` (Theory: CLOSED→`AlreadyResolved`, RESOLVED_FOR_*→`AlreadyRuledByAdmin`) |
| 4a | B1 — serbest bırakılan satır satıcının kaydına yazılmıyor (düzeltme turu) | ✓ | `An_Admin_Ruling_Releases_The_Misdelivery_Hold_To_Cancellation` (damga yazılıyor) · `An_Ordinary_Delivery_Timeout_Is_Not_Marked_As_Admin_Released` · `Recompute_Cancelled_Timeout_Released_By_Admin_Ruling_Counts_For_Neither_Party` · `Recompute_Admin_Release_Does_Not_Clear_An_Unrelated_Timeout` · `Admin_Released_Delivery_Timeouts_Never_Reach_The_Cooldown` · `Admin_Release_Removes_Only_Its_Own_Row_From_The_Count` |
| 4b | N2 — kapıyı yalnız imzayı görmüş karar açıyor (düzeltme turu) | ✓ | `A_Ruling_That_Predates_The_Signature_Re_Escalates` (Theory ×2) · `A_Ruling_In_The_Same_Instant_As_The_Signature_Re_Escalates` · `A_Closed_Dispute_Is_Not_Re_Escalated_By_A_Later_Signature` · `A_First_Round_Signature_Does_Not_Release_On_A_Ruling_That_Predates_It` · `A_Ruling_Made_Without_The_Signature_Does_Not_Release_The_Hold` · `The_Escalator_Is_Told_When_The_Signature_Was_First_Observed` |
| 4c | N1 — port doc'u iki üreticiyi de anlatıyor (düzeltme turu) | ✓ | `IDeliveryTimeoutRound.cs` `Cancel` doc'u: "timeout recorded against the seller" cümlesi kaldırıldı, iki üretici adıyla yazıldı |

> **1–4 satırları yapım chat'inin kendi değerlendirmesidir.** Bağımsız validator (2026-08-18)
> 1–3'ü onayladı, **4'ü `~ Kısmi`'ye indirdi**: D4'ün "satıcının kaydına kusur yazılmaz"
> koşulu karşılanmıyor (bulgu **B1**). Validator'ın kendi tablosu ve kanıtları
> [§Doğrulama](#doğrulama) bölümünde. **4a–4c düzeltme turunun (2026-08-18) kriterleridir**
> — detay [§Düzeltme Turu](#düzeltme-turu-2026-08-18) bölümünde; bunlar da yapım chat'inin
> kendi değerlendirmesidir ve bağımsız doğrulama bekliyor.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0E/0W | `dotnet build Skinora.sln --configuration Release` |
| Unit | ✓ 1433/1433 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration | ✓ 1326/1326 | Proje bazında **seri** koşuldu (T127 dersi). Disputes 54 · API 486 · Transactions 507 · Auth 37 · Notifications 60 · Platform 65 · Shared 16 · Fraud 73 · Admin 22 · Payments 6 |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` |
| **Backend toplam** | **✓ 2768/2768** | T130 tabanı 2754 → **+14** yeni test |
| FE tsc | ✓ 0 | `npx tsc --noEmit` |
| FE eslint | ✓ 0 | `npm run lint` |
| FE prettier | ✓ 0 | `npx prettier --check --end-of-line crlf` (lokal CRLF artifaktı izole edildi) |
| FE i18n | ✓ 4×1319 | `npm run i18n:check` — parity OK, advisory 15 (değişmedi) |
| FE vitest | ✓ 33/33 | `npm test` |
| FE build | ✓ | `npm run build` — `/[locale]/admin/disputes` ƒ |
| **CI** | **✓ PASS** | HEAD `bdbe46e`, run [`32062899657`](https://github.com/turkerurganci/Skinora/actions/runs/32062899657) — CI Gate `success`, bloke edici 11/11 job yeşil |

## Doğrulama — Tur 1 (2026-08-18, ✗ FAIL)

> Tur 2 (✓ PASS) bu dosyanın sonundaki [§Yeniden Doğrulama — Tur 2](#yeniden-doğrulama--tur-2-2026-08-18--pass) bölümündedir. Aşağısı tarihsel kayıttır.

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✗ FAIL** (bağımsız validator chat'i, 2026-08-18) |
| Bulgu sayısı | **3** — 1 bloke edici (B1) + 2 bloke etmeyen (N1, N2) |
| Düzeltme gerekli mi | **Evet** — düzeltme turu kriterleri `11_IMPLEMENTATION_PLAN.md` §P5 T131'e yazıldı (D6, D7 + N1, N2) |

### Validator kapıları

| Adım | Sonuç |
|---|---|
| -1 Working tree | ✓ temiz |
| 0 Main CI son 3 | ✓ [`32057012508`](https://github.com/turkerurganci/Skinora/actions/runs/32057012508) · [`32057012471`](https://github.com/turkerurganci/Skinora/actions/runs/32057012471) · [`32053321109`](https://github.com/turkerurganci/Skinora/actions/runs/32053321109) — üçü de `success` |
| 0b Repo memory | ✓ T131 satırları mevcut |
| 8a Task branch CI | ✓ [`32067472674`](https://github.com/turkerurganci/Skinora/actions/runs/32067472674), headSha `fed6689` = dal HEAD, CI Gate `success`, bloke edici **11/11** yeşil |

### Validator'ın kendi ürettiği kanıt (dal HEAD `fed6689`)

| Tür | Sonuç | Komut |
|---|---|---|
| Build | ✓ 0 Error / 0 Warning | `dotnet build Skinora.sln -c Release` |
| Unit | ✓ **1433/1433** | `dotnet test Skinora.sln -c Release --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration | ✓ **1326/1326** | proje bazında seri, `--filter "FullyQualifiedName~.Integration"` |
| Contract | ✓ **9/9** | `--filter "FullyQualifiedName~.Contract"` |
| **Backend toplam** | ✓ **2768/2768** | — |
| FE tsc / eslint / i18n / vitest | ✓ 0 / 0 / **1319×4 identical key sets** / 33/33 | `npx tsc --noEmit` · `npm run lint` · `npm run i18n:check` · `npm test` |

Lokal `prettier --check` yedi dosyada uyarı verdi; `tr -d '\r'` sonrası çıktı **birebir aynı** → `core.autocrlf=true` artifaktı, CI `1. Lint` (LF checkout) yeşil. Bulgu sayılmadı.

**E2E advisory (8 leg kırmızı) T131 kaynaklı DEĞİL — bağımsız olarak yeniden üretildi:** dal run'ı ile main tabanı [`32057012471`](https://github.com/turkerurganci/Skinora/actions/runs/32057012471) **birebir aynı** (10 passed / 22 failed), `PlatformSteamBots` izi **0**, T131 yüzeylerinden (`overrideReason` · `OVERRIDE_REASON_REQUIRED` · `buyerFavorRequiresOverride` · `ResolutionOverrideReason` · `deliveredItemName`) `--log-failed` üzerinde **0 iz**.

### Kabul kriterleri — validator verdict'i

| # | Kriter | Yapım | Validator | Not |
|---|---|---|---|---|
| 1 | Item iade bacağı — dört kalıntı | ✓ | ✓ | Dördü de kapalı; kalan tüm `ItemRefundToSeller` isabetleri olumsuz/tarihsel. 07 §9.20/§9.22 doğru şekilde dokunulmadı (T133a) |
| 2 | Kanıtlanmış teslimatta BUYER_FAVOR gerekçe istiyor (D1·D2·D3) | ✓ | ✓ | Kapı `Status == ITEM_DELIVERED` tek helper'da; ≥20 trim'li; override yoksa saklanmıyor; audit `NewValue`'da; AD28 bool'u her zaman dönüyor |
| 3 | AD28 `deliveredItemName` admin ekranında | ✓ | ✓ | Beklenen item adının hemen altında, alan yoksa satır çizilmiyor; 4 dil parity |
| 4 | SELLER_FAVOR sonrası terminal disposition | ✓ | **~ Kısmi** | D5 ✓ (`AlreadyRuledByAdmin`, `CLOSED` kapıyı açmıyor). **D4'ün "satıcının kaydına kusur yazılmaz" koşulu ✗ — B1** |

### Bulgular

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| B1 | S1 Sapma (**bloke edici**) | Admin kararıyla serbest bırakılan satır, admin'in akladığı satıcıya kusur yazıyor | `ReputationAggregator.cs:218` · `CancelCooldownEvaluator.cs:111` |
| N1 | S1 Sapma | `DeliveryTimeoutDecision.Cancel` XML doc'u ikinci üreticisi için yanlış | `IDeliveryTimeoutRound.cs:47-51` |
| N2 | Gözlem | İlk-tur serbest bırakma, admin'in hiç görmediği bir imzayı iptale çevirebiliyor | `DeliveryTimeoutRound.cs:316` |

**B1 — doğrulanmış üretim zinciri:** `DeliveryTimeoutRound.cs:316` `Cancel` → `DeadlineScannerJob.cs:267→126→138` `Fire(Timeout)` → `TransactionStateMachine.cs:266` `PAYMENT_RECEIVED → CANCELLED_TIMEOUT` → `DeadlineScannerJob.cs:161-164` history satırı (`PreviousStatus = PAYMENT_RECEIVED`) + `affected.Add(sellerId, …)` → `:182` `RefreshAsync(evaluateCooldown: true)` → `ReputationAggregator.cs:218` (`PAYMENT_RECEIVED → Seller`; paydaya girer, başarı sayılmaz) **ve** `CancelCooldownEvaluator.cs:111` (aynı satır cooldown penceresine girer → `User.CooldownExpiresAt` damgalanabilir). Buna karşılık `11_IMPLEMENTATION_PLAN.md` D4 ve — bu görevde yazılan — `03_USER_FLOWS.md:490` "(kayda kusur yazılmaz)" diyor. İtibarın admin düzeltme yüzeyi yok; satır kalıcı olduğu için ceza da kalıcı. T131 öncesi bu popülasyon erişilemezdi (satır sonsuza kadar `Held` dönüyordu) → yeni davranış. `IDeliveryTimeoutRound.cs:50` zaten "timeout recorded against the seller" diyor — 03 §6.4'ün inkâr ettiği sonucu.

**Proje sahibi kararı (2026-08-18): D6 — kod düzeltilir (seçenek (a)).** N1 ve N2 aynı düzeltme turuna alındı. Kriterlerin nihai metni `11_IMPLEMENTATION_PLAN.md` §P5 T131 "DÜZELTME TURU KABUL KRİTERLERİ" bloğunda.

### Yapım raporu karşılaştırması

- **AC1–AC3: tam uyum.** Validator kanıtları yapım raporunun kanıtlarını bağımsız olarak yeniden üretti.
- **AC4: uyuşmazlık.** Rapor ✓ işaretlemiş; itibar/cooldown sonucu raporda hiç geçmiyor. Validator `~ Kısmi` verdi (mekanizma doğru, D4'ün karar metninin ikinci yarısı karşılanmıyor).
- Raporun kendi *Known Limitations* üç kalemi (çözülmüş dispute'ların ekrandan açılamaması · modal component testi yokluğu · `SELLER_PAYOUT` yarış penceresi) doğru sınıflandırılmış; validator ilk ikisini bağımsız olarak da gördü, üçüncüsü T131 kapsamı dışında ve doğrulanmadı.

### Merge durumu

**Merge edilmedi.** FAIL kuralı: branch merge edilmez, düzeltme ayrı yapım chat'inde yapılır, sonrasında yeni doğrulama chat'i açılır.

---

## Düzeltme Turu (2026-08-18)

Doğrulamanın üç maddesi (**B1 bloke edici** · **N1** · **N2**) kapatıldı. Turun iki kapsam
kararı proje sahibine **seçenekli** sunuldu ve **önerilen seçenekler onaylandı**; ikisi de
`11_IMPLEMENTATION_PLAN.md` §P5 T131 bloğuna **NİHAİ ŞEKİL** olarak yazıldı.

### B1 (bloke edici) — admin kararıyla serbest bırakılan satır artık kimseye yazılmıyor

**Ayırt edici alan:** yeni nullable kolon `Transactions.TimeoutReleasedByAdminRulingAt`.
Damgayı **yalnız** teslimat turunun admin-serbest-bırakma kolu yazar
(`DeliveryTimeoutRound.ReleasedByAdminRuling`) — kararın **yanında**, çünkü scanner yalnız
`Cancel` görür ve o değerin iki üreticisi oradan birbirinin aynısıdır.

`ReputationAggregator` ve `CancelCooldownEvaluator` damgalı satırı **hem sorgu katmanında
filtreler hem switch içinde açık guard'la reddeder**. Çift yazım bilinçlidir ve T129'un
`REFUNDED` bölmesindeki desenin aynısıdır: `CANCELLED_TIMEOUT`'un artık iki üreticisi var,
sorgunun ileride genişletilmesi sessizce yeniden ceza yazmaya başlamamalı.

**Statü `CANCELLED_TIMEOUT` kaldı** — B1'in metni bunu şart koşuyor; ayrıca iade akışı,
bildirim metni ve state machine geçişi o statüye bağlı.

**Reddedilen iki alternatif** (proje sahibine sunuldu): (i) kolonsuz çıkarım
(`MisdeliverySignature` capture'ı olan bir `CANCELLED_TIMEOUT` ancak admin serbest
bırakmasıyla oluşabilir, çünkü re-entry kapısı diğer tüm yolları kısa devre ediyor) — doğru,
ama doğruluğu **başka bir dosyadaki** kapının değişmemesine bağlı; (ii) statüyü
`CANCELLED_ADMIN`'e çevirmek — B1'in metnine aykırı ve dalga boyu geniş.

**D7'nin gereği — yan kayıtların tamamı tarandı.** Serbest bırakılan iptalin ürettiği yan
kayıtlar tek tek çıkarıldı: `TransactionTimedOutEvent` (tüketicileri yalnız Notifications +
Realtime), `PaymentRefundToBuyerRequestedEvent` (alıcıya iade — doğru), `TransactionHistory`
(audit — doğru) ve itibar + cooldown. **Satıcının kaydına yazan başka yol yok** (fraud flag
yolu dahil), yani B1'in adlandırdığı iki tüketici bu akışın tamamıdır.

### N2 — kapıyı yalnız imzayı GÖRMÜŞ bir karar açar

**Karşılaştırma:** `Disputes.ResolvedAt` ↔ imzanın **ilk** kayda geçtiği an
(`DeliveryEvidenceCaptures` içindeki **en eski** `MisdeliverySignature` satırının
`ObservedAt`'i). Serbest bırakma yalnız ruling imzadan **kesin olarak sonra** ise uygulanır.
İki belirsiz girdi — eşit an ve (şema regresyonu olan) NULL `ResolvedAt` — "görmemiş" sayılır:
iki hata simetrik değildir, gereksiz inceleme admin'in zamanına, yanlış serbest bırakma
satıcının malına mal olur. **En eski** capture okunur, en yeni değil — sonraki bir turun aynı
bulguyu yeniden kaydetmesi, kanıtı görmüş bir kararı görmemişe çeviremez.

**Karar port'un iki ucu arasında bölündü.** Tur imzanın ilk gözlem anını `EscalateAsync`'e
**parametre** olarak verir (ilk turda kendi saati — imza o an oluşuyorsa mevcut her karar
zorunlu olarak ondan öncedir); dispute'u görebilen taraf adapter olduğu için karşılaştırmayı
o yapar ve yeni `MisdeliveryEscalationOutcome.ReEscalatedAfterRuling` döner. Tur bu değeri
diğer serbest-bırakmayan cevaplar gibi `Held`'e çevirir ve damgayı **yazmaz**.

**Yeniden eskalasyon:** aynı satır `ESCALATED`'a döner, `SystemCheckResult` yeni bulguyla
(alıcının dilinde) yazılır, `ResolvedAt` **temizlenir**
(`CK_Disputes_Resolved_ResolvedAt` ikisini eşler), `AdminNote` + `AdminId` **korunur**
(sonraki admin kimin ne gerekçeyle karar verdiğini görmeli) ve iki tarafa inceleme bildirimi
gider. Önceki karar kaybolmaz: değiştirilemez `DISPUTE_RESOLVED` audit kaydındadır
(06 §3.20). Kural `02 §10.2`'ye yazıldı — oradaki yasak ikinci bir dispute **satırıdır**
(`UQ_Disputes_TransactionId_Type`), aynı satırın **yeni bir olguyla** yeniden ele alınması
değil. Kapıyı yalnız platformun yeni gözlemi açar; kullanıcı itirazı veya eski kararın
yeniden tartışılması açmaz. **`CLOSED` bu kolun dışındadır** (06 §2.10 — insan kararı yok,
korunacak bir hüküm de yok) ve orada mevcut davranış zaten parayı serbest bırakmıyor.

**Reddedilen iki alternatif:** (i) sessizce `Held` kalmak — D4'ün kapatmak için var olduğu
fail-frozen sınıfını bu popülasyon için geri getirir (para emanette asılı, tek çıkış
belgesiz AD19); (ii) dispute'a dokunmadan `Transaction`'a triyaj damgası — 02 §10.2 sorusunu
doğurmaz ama kimsenin okumadığı bir yüzey yaratır (T130 N2 / T137'nin "sahipsiz sinyal ölür"
dersi).

### N1 — port doc'u iki üreticiyi de anlatıyor

`DeliveryTimeoutDecision.Cancel` doc'undan tek üreticiye ait "timeout recorded against the
seller" cümlesi **çıkarıldı**; yerine iki üretici de adıyla yazıldı: (a) kanıtlı
teslimatsızlık — kusur satıcınındır ve 06 §3.1 ona yazar; (b) admin kararıyla serbest bırakma
— imza item'ın satıcıdan **ayrıldığını** söyler, karar satıcıyı aklamıştır ve satır
damgalanıp iki haritadan da düşer. Çağıranın işi iki kolda da aynıdır; farklı olan satırın
satıcının kaydına ne ifade ettiğidir, ve o bilgi enum'da değil işlem satırında taşınır.

### Düzeltme turu — etkilenen dosyalar

**Kaynak (7):**
- `Skinora.Transactions/Domain/Entities/Transaction.cs` — `TimeoutReleasedByAdminRulingAt`
- `Skinora.Transactions/Application/Delivery/DeliveryTimeoutRound.cs` — damga + imza anının
  kaynağı (`MisdeliveryFirstObservedAtAsync`, en eski capture)
- `Skinora.Transactions/Application/Delivery/IDeliveryTimeoutRound.cs` — **N1** doc
- `Skinora.Transactions/Application/Delivery/IDeliveryMisdeliveryEscalator.cs` — yeni
  parametre + `ReEscalatedAfterRuling`
- `Skinora.Transactions/Application/Reputation/ReputationAggregator.cs` — **B1** dışlama
- `Skinora.Transactions/Application/Reputation/CancelCooldownEvaluator.cs` — **B1** dışlama
- `Skinora.Disputes/Application/Disputes/MisdeliveryDisputeEscalator.cs` — **N2** karşılaştırma
  + yeniden eskalasyon

**Migration (1):** `20260818085958_T131_TimeoutReleasedByAdminRulingAt` — 1 additive nullable
`datetime2` kolon, seed/CHECK/index yok.

**Test (5 dosya, +11 test):**
- `DeliveryTimeoutRoundTests` — damga yazılıyor / olağan timeout'ta yazılmıyor /
  `ReEscalatedAfterRuling` → `Held` + damgasız / escalator'a **ilk** gözlem anı veriliyor;
  ilk-tur testi N2'nin gerektirdiği şekilde **yeniden yazıldı**
  (`A_First_Round_Signature_Does_Not_Release_On_A_Ruling_That_Predates_It`)
- `MisdeliveryDisputeEscalatorTests` — karardan sonraki imza iki terminalde de yeniden eskale
  ediyor / eşit an da yeniden eskale ediyor / `CLOSED` sonraki imzayla yeniden eskale
  **edilmiyor**; mevcut "left alone" theory'si imzayı karardan **önceye** alacak şekilde
  güncellendi
- `ReputationAggregatorTests` — damgalı satır iki tarafta da sayılmıyor / damga **başka** bir
  timeout'u temizlemiyor
- `CancelCooldownEvaluatorTests` — damgalı satırlar cooldown'a hiç ulaşmıyor / dışlama yalnız
  kendi satırını düşürüyor
- `TimeoutTestSupport` — port test double'ı yeni imzaya uyarlandı

### Düzeltme turu — test sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ **0 error / 0 warning** | `dotnet build Skinora.sln -c Debug` |
| Backend toplam | ✓ **2779/2779** | 13 test projesi, **proje bazında seri** koşum (Testcontainers kuralı, T127 dersi). Taban 2768 → **+11** |

Proje kırılımı: API 551 · Admin 22 · Auth 120 · Disputes 83 · Fraud 91 · Notifications 171 ·
Payments 6 · Platform 189 · Realtime 40 · Shared 413 · Steam 39 · Transactions 1032 · Users 22.

Enum değeri eklendiği için (`MisdeliveryEscalationOutcome.ReEscalatedAfterRuling`) tüm
Unit-filter suite ayrıca koşuldu — kardeş projelerdeki parity testleri dahil temiz
(Shared 391 · Platform 124 · Transactions 518).

**Frontend:** bu turda **hiçbir FE dosyası değişmedi** (düzeltme tamamen backend); FE kapıları
CI'nin 1. Lint / 3b. JS test legleriyle doğrulandı (ikisi de ✓).

### Düzeltme turu — CI

**✓ PASS** — HEAD `ea56b78`, run [`32122430201`](https://github.com/turkerurganci/Skinora/actions/runs/32122430201),
`headSha` = dal HEAD, **CI Gate `success`**. Bloke edici **11 job'un 11'i yeşil**: Detect
changed paths · 1. Lint · 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration
test · 5. Contract test · **6. Migration dry-run** (yeni kolon şemaya temiz uygulanıyor) ·
7. Docker build (backend) · 7. Docker build (frontend) · CI Gate. `0. Guard` skipped.

Dal izolasyon kontrolü temiz: `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+…'` →
yalnız `T131`. Working tree session başında temizdi.

**8 advisory E2E leg kırmızı — T131 kaynaklı DEĞİL, dört kanıtla:**

1. **Düzeltme turunun yüzeylerinden 0 iz** — `grep -ciE
   "TimeoutReleasedByAdminRulingAt|ReEscalatedAfterRuling|signatureFirstObserved|
   RuledWithoutTheSignature"` → **0** (948 satırlık `--log-failed` çıktısı üzerinde).
2. **`PlatformSteamBots` izi 0** — T137a'nın onardığı harness katmanı geçiliyor.
3. **Spec çalıştıran yedi leg tabanı leg-leg birebir üretiyor:** happy-path 0/1 ·
   cancellation 0/4 · timeout 1/4 · payment 0/6 · fraud-flags 3/4 · emergency-hold 0/3 ·
   downtime 0/3 — T137a'nın main run'ında (`32050987594`) ölçtüğü değerlerin aynısı.
4. **Sekizinci leg (T113 admin-flows) tek bir test bile koşmadı:** "Start database" adımı
   `mcr.microsoft.com/v2/mssql/server/manifests/2022-latest` imajını çekerken ağ hatası
   aldı (`read tcp …` → `Error response from daemon`) ve job orada düştü. **Geçici altyapı
   arızası**, test başarısızlığı değil. Tabandaki 6/7'si eklenince toplam **10 passed /
   22 failed = 32** — yani T137a tabanıyla birebir.

Kalan 22 test custody durumlarında takılıyor; sahiplik **T137** → **T138**.

## Altyapı Değişiklikleri
- **Migration (yapım):** `T131_DisputeResolutionOverrideReason` — 1 additive nullable kolon
  (`Disputes.ResolutionOverrideReason` nvarchar(2000)). Seed yok, CHECK yok, index yok.
- **Migration (düzeltme turu):** `T131_TimeoutReleasedByAdminRulingAt` — 1 additive nullable
  kolon (`Transactions.TimeoutReleasedByAdminRulingAt` datetime2). Seed yok, CHECK yok,
  index yok. Mevcut satırlarda NULL = "admin serbest bırakması değil", yani geriye dönük
  veri hiçbir sayımdan düşmez; dışlama yalnız bu turdan sonra oluşacak satırlara uygulanır.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni paket:** Yok.

## Mini Güvenlik Kontrolü
- **Secret sızıntısı:** Yok — yeni secret/anahtar/bağlantı dizesi yok.
- **Auth/authorization:** Değişmedi. AD28 `VIEW_DISPUTES`, AD29 `MANAGE_DISPUTES` aynı kaldı;
  yeni endpoint yok. Değişiklik yetkili admin'in **yapabileceklerini daraltıyor** (gerekçesiz
  override artık reddediliyor), genişletmiyor.
- **Input validation:** Yeni kullanıcı girdisi `overrideReason` — trim + min/max uzunluk
  sunucuda zorlanıyor, kolon genişliği 2000 ile sınırlı, EF parametrize sorgu. Ekranda
  `{detail.resolutionOverrideReason}` React text node olarak render ediliyor (XSS yok).
- **Yeni dış bağımlılık:** Yok.

## Notlar

### Kapılar
- **Adım -1 (working tree):** temiz — `git status --short` boş çıktı.
- **Adım 0 (main CI):** son 5 run `success` — `32053321109`, `32053321130`, `32049649996`,
  `32049649962`, `32039187802`.
- **Bağımlılık:** T130 ✓ Tamamlandı (merge `523dc97`, PR #242).

### Dış Varsayımlar (task.md Adım 4)
**Yok.** Yeni paket, dış API, plan tier veya ortam varsayımı bulunmuyor; değişiklik tümüyle
mevcut yığın içinde (EF Core additive kolon, mevcut endpoint sözleşmesi, mevcut i18n kapısı).
Doğrulanan tek repo-durumu varsayımı: `MisdeliveryEscalationOutcome` **Shared EnumTests parity
kapısında değil** (Transactions modülü port enum'u) — `grep -rn "MisdeliveryEscalationOutcome"
backend/tests` yalnız kullanım yerlerini gösteriyor, sayım assertion'ı yok; dolayısıyla değer
eklemek parity testi kırmıyor. Doğrulandı: unit leg 1433/1433 yeşil.

### Proje sahibi kararları (yapım öncesi, 2026-08-17)
| # | Karar | Seçilen |
|---|---|---|
| K1 | Gerekçenin şekli | Ayrı alan + kalıcı kolon (+migration, AD28'de döner) |
| K2 | Kapının koşulu | `Status == ITEM_DELIVERED` — planın literal metninin **üst kümesi** |
| K3 | G3 kapsamı | T131'e alındı, dördüncü kabul kriteri olarak plana yazıldı |
| K4 | 02 §10 doküman borcu | Kural 02 §10.4'e yazıldı; iki atıf artık hedefini buluyor |
| K5 | G3 disposition | Normal timeout akışına dön → iptal + alıcıya iade |

**K2 neden üst küme:** kriterin ilk metni "INVENTORY_DELTA kanıtlı ITEM_DELIVERED" idi.
`INVENTORY_DELTA` yalnız alıcı envanteri **Public** iken yazılıyor
(`DeliveryVerificationService:175`), dolayısıyla bayrağa bağlı bir kapı admin'in gerekçe yazma
yükümlülüğünü vakanın gücüne değil **Steam'in o an okunabilir olmasına** bağlardı; ayrıca
launch'ta kanıt kapısı kapalı olduğu için en kalabalık popülasyonu (alıcı onayıyla gelen
teslimatlar) korumasız bırakırdı. `ITEM_DELIVERED` durumuna tek bir giriş kenarı var
(`DeliverItem`) ve guard'ı zaten 02 §9.2 kanıtını şart koşuyor — yani durum **kanıtın
kendisidir**. Kapsam geniş olduğu için kriterin harfi de karşılanıyor. Sapma
`11_IMPLEMENTATION_PLAN.md`'ye **NİHAİ ŞEKİL** olarak yazıldı (T122'nin kalıcı dersi).

### Ölçüm hatası (kayda geçti)
`Resolve_BuyerFavor_AtItemDelivered_PersistsTheOverrideReason_AndAuditsIt` ilk koşumda FAIL
verdi: assertion audit JSON'ında Türkçe gerekçeyi **ham substring** olarak arıyordu, oysa
`System.Text.Json` varsayılan encoder'ı ASCII dışını `\uXXXX` olarak kaçırıyor. Değer
kaydedilmişti; hatalı olan testti. Assertion `JsonDocument` ile ayrıştırılmış değere bağlandı —
artık gerekçenin **alfabesine** değil, kaydedilip kaydedilmediğine bakıyor.

## Known Limitations / Follow-up

1. **Çözülmüş dispute'lar ekrandan açılamıyor (pre-existing).** `DisputeQueueTable` yalnız
   `ESCALATED` satırlara "Çöz" butonu veriyor; diğerlerinde "—" var ve modal hiç açılmıyor.
   Bu yüzden AD28'in çözüm-sonrası yarısı (`adminNote`, `adminId`, `resolvedAt` ve şimdi
   `resolutionOverrideReason`) sözleşmede ve modalın render yolunda var ama **bugün
   erişilemiyor**. Dördü de aynı sınıfta; ilk üçü T131'den önce de böyleydi. Kapatma yolu:
   çözülmüş satırlar için salt-okunur bir "Detay" aksiyonu (≈20 satır + 1 i18n anahtarı ×4).
   T131'in kabul kriterlerinde olmadığı için **yapılmadı** — proje sahibi kararına sunuluyor.
2. **`DisputeResolveModal` için component testi yok.** FE'de admin dispute yüzeyinde hiç
   vitest yok (9 test dosyasının hiçbiri admin/dispute kapsamıyor); AC3'ün FE yarısının kanıtı
   tsc + i18n parity + build. Yeni bir test altyapısı kurmak T131'in kapsamı dışındaydı.
3. **`SELLER_PAYOUT` yarış penceresi (pre-existing, T131 kaynaklı değil).** Keşif sırasında
   görüldü: `OutgoingTransferDispatchJob` aday sorgusu yalnız `BlockchainTransaction.Status ==
   PENDING`'e bakıyor, işlemin güncel durumunu okumuyor — kuyruğa alınmış ama broadcast
   edilmemiş bir payout satırı varken `BUYER_FAVOR` ile `REFUNDED`'a geçilirse iki transfer de
   yayınlanabilir. Pencere dar (payout ancak `!HasActiveDispute` iken kuyruğa girer) ama kapalı
   değil. T131'in kapsamında değil, doğrulanması gerekiyor.

## Commit & PR
- Branch: `task/T131-admin-dispute-override`
- Commit: `abb8daf` — T131: AdminDisputeService item-refund bacağı + override
- Commit: `bdbe46e` — Merge `origin/main` (T137a #243) — aşağıdaki nota bakınız
- PR: [#245](https://github.com/turkerurganci/Skinora/pull/245)
- CI: **✓ PASS** — HEAD `bdbe46e`, run [`32062899657`](https://github.com/turkerurganci/Skinora/actions/runs/32062899657),
  **CI Gate `success`**. Bloke edici **11 job'un 11'i yeşil**: 1. Lint · 2. Build ·
  3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test ·
  **6. Migration dry-run** (yeni kolon şemaya temiz uygulanıyor) · 7. Docker build
  (backend + frontend) · Detect changed paths · CI Gate.

### Session ortasında main ilerledi (kayda geçti)
Yapım sürerken **başka bir session T137a'yı (#243) main'e merge etti ve bu worktree'nin
HEAD'ini `main`'e aldı.** Task dalı push edilmiş olduğu için iş kaybolmadı. İki sonucu oldu:

1. PR #245 `CONFLICTING` doğdu — T137a bu görevin de dokunduğu **üç ortak dokümana**
   dokunmuştu (`IMPLEMENTATION_STATUS.md`, `11_IMPLEMENTATION_PLAN.md`,
   `.claude/memory/MEMORY.md`). **GitHub çakışmalı PR'da CI run'ı hiç yaratmadı** —
   `feedback_branch_from_main_after_squash` ile aynı imza, farklı sebep; "Actions bozuk"
   teşhisine gidilmedi.
2. Çözüm `bdbe46e` merge commit'i: iki doküman çakışması **her iki taraf da korunarak**
   çözüldü (T131 girişi "Son güncelleme", T137a "Önceki güncelleme"ye indi; memory'de ikisi
   de görev numarası sırasında). `11_IMPLEMENTATION_PLAN.md` otomatik birleşti ve T137a'nın
   T138 kriterine yazdığı ölçüm ("7 spec yeniden yazım, 21 test") bozulmadan geldi — kontrol
   edildi.

Merge'ün getirdiği tek kod değişikliği `e2e/` altındadır; **backend ve frontend kaynağına
sıfır dokunuş** (`git diff abb8daf..bdbe46e --stat` → 11 dosya: 3 doküman + T137a raporu +
7 `e2e/` dosyası), dolayısıyla yukarıdaki lokal test sonuçları bu HEAD için geçerlidir ve
CI bunu bağımsız olarak teyit etti. Dal izolasyon kontrolü temiz: `git log main..HEAD` yalnız
`T131` içeriyor.

### Advisory E2E legleri (T131 kaynaklı DEĞİL — kanıtlı)
8 advisory leg kırmızı (`continue-on-error`, `ci-gate.needs` dışında — `ci.yml:612-625`).
Üç bağımsız kanıt T131'in bunlara katkısının sıfır olduğunu gösteriyor:

- **`Invalid object name 'PlatformSteamBots'` izi: 0** — T137a'nın onardığı harness katmanı
  gerçekten geçiliyor (T117'den T137a'ya kadar bu imza 8/8 leg'de tam 1 kez görülüyordu).
- **T131 yüzeylerinden 0 iz** — `grep -ciE "overrideReason|OVERRIDE_REASON_REQUIRED|
  deliveredItemName|ResolutionOverrideReason|AlreadyRuledByAdmin|buyerFavorRequiresOverride"`
  → **0** (1012 satırlık `--log-failed` çıktısı üzerinde).
- **Sayım T137a'nın ölçtüğü tabanı birebir yeniden üretiyor:** 10 passed / 22 failed = 32,
  yani T137a'nın main run'ında (`32050987594`) ölçtüğü 10/32 ile aynı. Kalan 22 test custody
  durumlarında takılıyor; sahiplik **T137** (sidecar-fake envanter ucu steamId'yi yok sayıyor,
  teslimat kanıtı simüle edilemiyor) → **T138** (spec yeniden yazımı).

---

## Yeniden Doğrulama — Tur 2 (2026-08-18) ✓ PASS

Bağımsız validator chat'i, **yapım raporu görülmeden**, kendi verdict'ini oluşturduktan sonra
raporla karşılaştırdı. Dal HEAD **`3df4d91`** (= `origin/task/T131-admin-dispute-override`,
merge-base = `origin/main`).

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✓ PASS** |
| Bulgu sayısı | **2** — bloke edici **0**, bloke etmeyen 2 (N1, N2 — ikisi de bu turda kapatıldı) |
| Düzeltme gerekli mi | Hayır |

### Validator kapıları

| Adım | Sonuç |
|---|---|
| -1 Working tree | ✓ temiz |
| 0 Main CI son 3 | ✓ [`32057012508`](https://github.com/turkerurganci/Skinora/actions/runs/32057012508) · [`32057012471`](https://github.com/turkerurganci/Skinora/actions/runs/32057012471) · [`32053321109`](https://github.com/turkerurganci/Skinora/actions/runs/32053321109) — üçü de `success` |
| 0b Repo memory | ✓ T131 satırları mevcut |
| 8a Task branch CI | ✓ [`32124494037`](https://github.com/turkerurganci/Skinora/actions/runs/32124494037), headSha `3df4d91` = dal HEAD, CI Gate `success`, bloke edici **11/11** yeşil (0. Guard skipped) |

### Validator'ın kendi ürettiği kanıt (dal HEAD `3df4d91`)

| Tür | Sonuç | Komut / detay |
|---|---|---|
| Build | ✓ 0 Error / 0 Warning | `dotnet build Skinora.sln -c Debug` |
| **Backend toplam** | ✓ **2779/2779**, 0 fail | 13 proje **seri** koşuldu (Testcontainers kuralı), 13/13 exit 0: API 551 · Admin 22 · Auth 120 · Disputes 83 · Fraud 91 · Notifications 171 · Payments 6 · Platform 189 · Realtime 40 · Shared 413 · Steam 39 · Transactions 1032 · Users 22 |
| FE tsc / eslint | ✓ 0 / 0 | `npx tsc --noEmit` · `npm run lint` |
| FE i18n | ✓ **4 locale × 1319 anahtar, identical key sets** | `npm run i18n:check` — 15 advisory `untranslatable` uyarısı pre-existing, hiçbiri T131 anahtarı değil |
| FE vitest | ✓ 33/33 | `npm test` |

**Advisory E2E (8 leg kırmızı) T131 kaynaklı DEĞİL — bu turda en güçlü kanıtla:** dal run'ı
[`32124494037`](https://github.com/turkerurganci/Skinora/actions/runs/32124494037) ile main
tabanı [`32057012471`](https://github.com/turkerurganci/Skinora/actions/runs/32057012471)
**leg sayımları birebir aynı** (3× "1 failed" · 1× "1 passed" · 3× "3 failed" · 1× "3 passed" ·
1× "4 failed" · 1× "6 failed" · 1× "6 passed" = **10 passed / 22 failed = 32**) ve **hata
imzaları da birebir**: 18× `timeout awaiting ITEM_ESCROWED` · 2× `timeout awaiting
TRADE_OFFER_SENT_TO_SELLER` · 1× `second create failed` · 1× `toBeGreaterThanOrEqual`. Yani
kalan 22 test **T117'de emekli edilen custody statülerini bekleyen spec'lerdir** — sahiplik
**T138**. T131 yüzeylerinden `--log-failed` üzerinde **0 iz**; `PlatformSteamBots` izi **0**
(T137a'nın onardığı harness katmanı geçiliyor).

### Kabul kriterleri — validator verdict'i (hepsi bağımsız olarak yeniden üretildi)

| # | Kriter | Yapım | Validator | Kanıt |
|---|---|---|---|---|
| 1 | Item iade bacağı — dört kalıntı (AC1) | ✓ | ✓ | Dördü kapalı; `ItemRefundToSeller` **kaynakta 0 isabet** (kalan tek atıf 05 §4.2'nin "v3.0 kaldırılan event'ler" listesi — belgeleyici). 07 §9.20/§9.22 doğru şekilde dokunulmadı: `git diff -U0` hunk'ları yalnız satır 3 + 2470–2489, iki tablo da her hunk'ın dışında (T133a sınırı korundu) |
| 2 | Kanıtlanmış teslimatta BUYER_FAVOR gerekçe (D1·D2·D3) | ✓ | ✓ | **D1:** kapı `AdminDisputeService.cs:461-463` tek helper'da `Status == ITEM_DELIVERED`, hiçbir kanıt bayrağı okumuyor. D1'in gerekçesi bağımsız doğrulandı: `ITEM_DELIVERED`'a giden **tek** state-machine kenarı `TransactionStateMachine.cs:265` ve guard zinciri (`HasDeliveryEntryInvariant` → `HasDeliveryEvidence` → `IsSufficientForDelivery` + `DeliveryVerifiedAt`) 02 §9.2 kanıtını şart koşuyor → durum gerçekten kanıtın kendisi. **D2:** `overrideReason` trim'li ≥20 / ≤2000; kolon `nvarchar(2000)` — DTO sınırıyla **eşit**, taşma SaveChanges'te patlamaz; override yoksa `ResolutionOverrideReason = null`; audit `NewValue`'ya da yazılıyor; kapı state guard'larından **sonra** (hold testi bunu sabitliyor). **D3:** AD28 bool'u **aynı** helper'dan geliyor ve `JsonIgnore` taşımıyor (her zaman dönüyor) |
| 3 | AD28 `deliveredItemName` admin ekranında (AC3) | ✓ | ✓ | Modal alanı beklenen adın **hemen altında** çiziyor; koşul `detail?.deliveredItemName &&` → `undefined` **ve** boş string ikisini de eliyor; karşılaştırmanın iki tarafı da aynı `detail` fetch'inden (`expectedItemName = detail?.transaction.itemName ?? dispute.itemName`); i18n 4×1319 identical |
| 4 | SELLER_FAVOR sonrası terminal disposition (D4·D5) | ✓ | ✓ | `Cancel` → `DeadlineScannerJob.cs:267` → `Fire(Timeout)` → `CANCELLED_TIMEOUT` + alıcıya iade zinciri uçtan uca izlendi. `CLOSED` kapıyı açmıyor (`AlreadyResolved`). `RESOLVED_FOR_BUYER` kolu savunma amaçlı: alıcı lehine karar işlemi `REFUNDED`'a taşıyor, tarayıcı ise yalnız `PAYMENT_RECEIVED` sorguluyor → pratikte erişilemez. Modül yönü doğru (Transactions → Disputes referansı yok), adapter `DisputesModule.cs:33-34`'te kayıtlı |
| 4a | **B1** — serbest bırakılan satır satıcının kaydına yazılmıyor (D6·D6a) | ✓ | ✓ | Damgayı **tek yazan** `DeliveryTimeoutRound.cs:377` (repo genelinde tek atama). İki tüketici de **hem sorguda hem switch'te** dışlıyor (`ReputationAggregator.cs:82-84` + `:215`; `CancelCooldownEvaluator.cs:63-64` + `:128`). Statü `CANCELLED_TIMEOUT` kaldı. Migration = 1 additive `datetime2` nullable, seed/CHECK/index yok. **D6a bağımsız olarak yeniden tarandı:** `CANCELLED_TIMEOUT`'un repo'daki diğer tüm okuyucuları (`FraudFlagService` · `FraudFlagAdminQueryService` · `UserActiveTransactionChecker` · `ActiveTransactionCounter` · `TransactionCreationService` · `AdminTransactionService.IsTerminalState` · liste/detay servisleri) yalnız **terminal-dışlama filtresi**; satıcının kaydına yazan başka yol **yok** → D6a'nın "iki tüketici akışın tamamıdır" iddiası doğru |
| 4b | **N2** — kapıyı yalnız imzayı görmüş karar açıyor (N2a·N2b·N2c) | ✓ | ✓ | Karşılaştırma `dispute.ResolvedAt is not { } ruledAt \|\| ruledAt <= signatureFirstObservedAtUtc` → eşit an **ve** NULL `ResolvedAt` ikisi de "görmemiş" (N2a). **En eski** capture okunuyor (`OrderBy(ObservedAt).First`). Tur imza anını parametre veriyor, adapter karşılaştırıyor, `ReEscalatedAfterRuling` → `Held` ve damga **yazılmıyor** (N2b). Yeniden eskalasyon `ESCALATED` + `ResolvedAt = null` → `CK_Disputes_Resolved_ResolvedAt` sağlanıyor; iki tarafa bildirim `DisputeEscalatedEvent(AutoEscalated: true)` ile gidiyor (N2c). **Para kilitlenmiyor:** `AdminDisputeService` Stage 3 guard'ı `ESCALATED` istiyor ve yeniden eskale edilen satır tam olarak orada → ikinci kez çözülebiliyor |
| 4c | **N1** — port doc'u iki üreticiyi de anlatıyor | ✓ | ✓ | "timeout recorded against the seller" cümlesi çıkarılmış, iki üretici adıyla yazılmış ve **doc'un her iddiası kodda doğrulandı** (yorum metnine güvenilmedi) |

### Doküman uyumu

Beş kaynak dokümanın hepsi sürüm + `Son güncelleme` bump'ı almış (T130 N1 konvansiyonu):
02 v3.5→v3.7 · 03 v3.4→v3.6 · 05 v3.3→v3.4 · 06 v6.8→v6.10 · 07 v3.5→v3.6. 06 §3.5 / §3.11
kolon satırları migration'larla **tip ve nullability olarak birebir**; 06 §3.1 formül bölmesi
ve sorumluluk haritası satırı aggregator predicate'iyle birebir; 07 §9.30 AD28/AD29 sözleşmesi
DTO'larla **alan alan** eşleşiyor (opsiyonellik `JsonIgnore` ile, `buyerFavorRequiresOverride`
bilinçli olarak zorunlu).

### Güvenlik kontrolü

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | Temiz |
| Auth etkisi | Temiz — `AdminDisputesController` diff'te yok; `VIEW_DISPUTES` (AD27/AD28) + `MANAGE_DISPUTES` (AD29) policy'leri değişmedi |
| Input validation | Temiz — `overrideReason` trim + min/max, kolon genişliği DTO sınırıyla eşit, JSX'te React escape'i, log'lanmıyor |
| Yeni bağımlılık | Yok — diff'te `.csproj` / `package.json` değişikliği yok |

### Bulgular — bloke edici 0

| # | Seviye | Açıklama | Etkilenen dosya | Durum |
|---|---|---|---|---|
| N1 | S1 Sapma (bloke etmeyen) | 06 §2.10 `DisputeStatus` tablosu `RESOLVED_FOR_*`'a hâlâ koşulsuz **"Terminal."** diyordu; aynı turda yazılan N2c yeniden eskalasyonu o statüyü geri döndürüyor ve bu **varsayılan yol** — ilk-tur imzasında karşılaştırma anı `now` olduğu için mevcut **her** karar imzadan önce sayılır. "Terminal." aynı dosyada normatif anlamda kullanılıyor (satır 132 `REFUNDED`). 06 bu görevde v6.10'a çekilmişti ama §2.10 açılmamıştı — AC1'in kapatmak için var olduğu "kod değişti, sözleşme eski sözü veriyor" sınıfının aynısı | `Docs/06_DATA_MODEL.md:212-213` | **Bu turda kapatıldı** (proje sahibi onayı): tabloya istisna işaretlendi + §2.10'a yeniden eskalasyon notu yazıldı, 06 v6.10→**v6.11**. Şema değişikliği yok |
| N2 | S3 Eksik (bloke etmeyen) | N2c'nin "`AdminNote` + `AdminId` **KORUNUR**" maddesi hiçbir testte iddia edilmiyordu; üstelik fixture `AddDisputeAsync` ikisini de hiç set etmediği için assertion eklense **bile** regresyonu yakalayamazdı. Kod doğruydu (`ReEscalateAsync` alanlara dokunmuyor), korumasızdı | `backend/tests/.../MisdeliveryDisputeEscalatorTests.cs` | **Bu turda kapatıldı** (proje sahibi onayı): fixture'a `_admin` kullanıcısı + `AdminId`/`AdminNote` (yalnız `RESOLVED_FOR_*`'ta — `CLOSED` sistemin kendi çözümü, admin'i yok), `A_Ruling_That_Predates_The_Signature_Re_Escalates`'e iki assertion. **Mutasyon testiyle kanıtlandı:** `ReEscalateAsync`'e `AdminNote = null; AdminId = null` enjekte edildiğinde test **2/2 FAIL** veriyor → assertion boş değil. Mutasyon geri alındı, üretim kodu değişmedi |

**Elenen aday bulgular:** validator 23 aday bulgunun her birini üç bağımsız çürütücüden
geçirdi; 22'si elendi. En yakın ikisi 05 §4.2 ve 03 §4.4 adım 7'nin niteliksiz "sorumlu:
satıcı" ifadeleriydi, ikisi de geçerli çıkmadı: (a) 02 §13 (satır 416/419/420) **aynı şekilde**
niteliksiz özet tutup sorumluluğu 06 §3.1'e açıkça delege ediyor ve T129'un `DeliveryReversedAt`
ikizinde de aynı konvansiyon uygulanmış; (b) 03 §4.4 **adım 1** yanlış-teslimat imzası vakasını
zaten §4.4'ün iptal dalının **dışına** çıkarıyor ("işlem iptal edilmez, dispute'a yükseltilir"),
dolayısıyla adım 7 o vakayı hiç kapsamıyor.

### Yapım raporu karşılaştırması

**Tam uyumlu — uyuşmazlık yok.** Raporun kabul kriterleri tablosundaki sekiz satırın hepsi
(1 · 2 · 3 · 3b · 4 · 4a · 4b · 4c) bağımsız olarak yeniden üretildi. Raporun bildirdiği
**2779/2779** backend sayısı validator'ın kendi koşumuyla **birebir** örtüştü (proje bazında
dağılım dahil). Raporun *Known Limitations* kalemleri doğru sınıflandırılmış.

### Kalıcı ders (tur 2)

**Bir kuralın KORUNDUĞUNU söyleyen madde, o korumayı bozan bir değişikliğin testi kırıp
kırmayacağıyla ölçülür.** N2c'nin `AdminNote`/`AdminId` koruması koddaydı ve doğruydu, ama
fixture ikisini de NULL bıraktığı için testin o maddeye dair söyleyebileceği hiçbir şey yoktu
— "assertion yok" ile "assertion var ama boş" aynı korumasızlıktır. T129 B4'ün ("kural yazıldı"
≠ "kural korunuyor") test tarafındaki karşılığı; ayırt etmenin yolu **mutasyon**: korumayı
kaldır, süit hâlâ yeşilse koruma test edilmiyordur.
