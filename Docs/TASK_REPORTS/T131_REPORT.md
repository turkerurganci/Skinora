# T131 — AdminDisputeService: item-refund bacağı + override

**Faz:** P5 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-17

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
| 4 | SELLER_FAVOR sonrası terminal disposition (G3) | ✓ | `An_Admin_Ruling_Releases_The_Misdelivery_Hold_To_Cancellation` · `A_System_Closed_Dispute_Does_Not_Release_The_Hold` · `A_First_Round_Signature_Also_Releases_When_An_Admin_Has_Ruled` · `Resolved_Dispute_Is_Left_Alone` (Theory: CLOSED→`AlreadyResolved`, RESOLVED_FOR_*→`AlreadyRuledByAdmin`) |

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

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration:** `T131_DisputeResolutionOverrideReason` — 1 additive nullable kolon
  (`Disputes.ResolutionOverrideReason` nvarchar(2000)). Seed yok, CHECK yok, index yok.
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
- Commit: (aşağıda)
- PR: (aşağıda)
- CI: (aşağıda)
