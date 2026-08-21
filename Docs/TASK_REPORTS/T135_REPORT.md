# T135 — StateActionPanel state×rol matrisi

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-21

---

## Yapılan İşler

S07 işlem detay ekranının **04 §7.3 state × rol matrisi** v3.0'a çekildi. T134 bu paneli
derlenebilir bırakmış ama matrisin kendisini (D1 kararı) T135'e devretmişti; tur o devri
kapattı ve devrin görünür kıldığı iki yapısal açığı da giderdi.

**Ölçülen başlangıç durumu** (`origin/main` `20c9a91` üzerinde): panelin **beş hücresi** eksik
veya custodial dönemin metnini taşıyordu, ve `POST /confirm-ready` (T123) ile
`POST /confirm-receipt` (T126) uçlarının **FE'de hiçbir çağıranı yoktu** — yani satıcının
hazırlık onayı ve alıcının teslim onayı arayüzden erişilemiyordu.

### Matris satırları (04 §7.3)

| Durum × Rol | Öncesi | Sonrası |
|---|---|---|
| ACCEPTED × satıcı | "The platform is preparing your trade offer" (custodial) | **[Göndermeye Hazırım]** → `POST /confirm-ready`, 07 §7.6a'nın altı hata kodu ayrı ayrı karşılanıyor |
| ACCEPTED × alıcı | "Satıcının item'ı göndermesi bekleniyor" (yanlış faz) | "Satıcının hazırlık onayı bekleniyor" |
| SELLER_CONFIRMED × satıcı | **dal yok → `null`** | "Hazır olduğunuzu onayladınız. Alıcının ödemesi bekleniyor." |
| SELLER_CONFIRMED × alıcı | (yalnız `PaymentInfoBlock`) | + panelde ödeme yönlendirmesi |
| PAYMENT_RECEIVED × satıcı | "Item alıcıya teslim ediliyor" (platform gönderiyormuş gibi) | **[Steam'de Trade Offer Gönder]** (`steamTradeOfferUrl`, yeni sekme) + item hatırlatması + yanlış-item uyarısı |
| PAYMENT_RECEIVED × alıcı | "Item'ınız gönderiliyor" | **[Teslim Aldım]** → onay modalı → `POST /confirm-receipt` |
| ITEM_DELIVERED × satıcı | "Ödemeniz işleniyor" | Net ödeme tarihi + 7 günlük Steam geri alma açıklaması + gün/saat geri sayımı |
| ITEM_DELIVERED × alıcı | "Item'ınız teslim edildi." | + **güvence** metni; geri sayım **gösterilmiyor** (04 §7.3) |

### İptal asimetrisi (04 §7.3 · 02 §7)

PAYMENT_RECEIVED'da satıcının iptal modal'ı artık sonucu söylüyor ("ödeme alıcıya iade edilir
ve itibar puanınız etkilenir"); alıcının devre dışı iptal butonunun **altında gerekçesi**
yazıyor ("Ödeme gönderildiği için iptal edemezsiniz") — önceden sebepsiz gri bir butondu.

### D2 — `buyerInventoryVisible` kalıcı yüzeye çıkarıldı (proje sahibi kararı)

04 §7.3'ün ACCEPTED notu "alıcı envanteri gizli" uyarısını şart koşuyor. Bu olgu §7.6a
confirm-ready yanıtında **bir kez** dönüyordu; oysa:

- Olgu **kalıcıdır** — 02 §9.2 envanter kanıtı yolu işlemin sonuna kadar kapalı kalır.
- Yükümlülüğü taşıyan taraf **alıcıdır** ("Teslim Aldım"a basmazsa işlem timeout'a düşer) ve
  alıcı confirm-ready yanıtını **hiç görmez**.

Bu yüzden alan `GET /transactions/:id` sözleşmesine eklendi (07 §7.5, doküman v3.8 → **v3.9**).
Yeni kolon **yok**: projeksiyon `Transaction.BuyerBaselineCapturedAt`'in NULL olup olmamasıdır
(06 §3.5 zaten bu NULL'u sinyal olarak tanımlıyor). Kapı `payment` bloğuyla **aynı** kilometre
taşı damgasıdır (`SellerReadyConfirmedAt`), statü adı değil — böylece onaydan **önce** alan hiç
dönmüyor ("bilinmiyor" ≠ "görünür") ve pencere açıldıktan sonra iptal edilen işlemlerde
korunuyor.

### D4 — REFUNDED açığı kapatıldı (proje sahibi kararı)

Matris tamlık taraması ölçtü: `helpers.ts`'in `isCancelledStatus` ve `isTerminalStatus`
fonksiyonları REFUNDED'ı saymıyordu. Sonucu iki katmanlıydı — sayfa `CancelInfoBlock` + iade
özetini hiç çizmiyordu (07 §7.5 ikisini de bu statü için vaat ediyor) ve panel hiçbir dala
düşmediği hâlde `!isTerminalStatus` doğru olduğu için altında **iki ölü disabled buton**
çiziyordu. Yani T129'un `DeliveryReversed → REFUNDED` iadesini alan alıcı **aldığı parayı
ekranda göremiyordu**. İki küme tek bir `UNWOUND` kümesinden türetildi; bir daha ayrı ayrı
bayatlayamazlar.

### D3 — Matris tamlık bekçisi

Sınıflandırma (`panelRowFor`) render'dan ayrıldı: her (statü × rol) hücresi tam bir `PanelRow`
değerine çözülüyor, panel her değer için tek bir dal çiziyor.
`StateActionPanel.matrix.test.ts` 39 hücrenin hepsini yürüyor ve sınıflandırılmayanı **adıyla**
raporluyor.

---

## Etkilenen Modüller / Dosyalar

**Backend (D2 — sözleşme genişlemesi, yeni kolon yok):**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailDto.cs` — `bool? BuyerInventoryVisible`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs` — `BuildBuyerInventoryVisible` + iki kurulum noktası
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionDetailServiceTests.cs` — +7 test (`baselineCaptured` helper parametresi)

**Frontend:**
- `frontend/src/lib/api/transactions.ts` — `confirmReady()` / `confirmReceipt()` + tipleri, `canConfirmReady` / `canConfirmReceipt` / `buyerInventoryVisible` alanları, bayat `steamTradeOfferUrl` yorumu düzeltildi
- `frontend/src/components/transactions/detail/helpers.ts` — `UNWOUND` kümesi (REFUNDED dahil), `PanelRow` / `panelRowFor` / `isActivePartyRow` / `PANEL_ROLES`
- `frontend/src/components/transactions/detail/StateActionPanel.tsx` — matris render'ı yeniden yazıldı
- **Yeni:** `ConfirmReadyButton.tsx`, `ConfirmReceiptButton.tsx`, `SellerTradeCta.tsx`, `SettlementNotice.tsx`, `InventoryHiddenNotice.tsx`
- **Yeni test:** `StateActionPanel.matrix.test.ts` (44 test), `StateActionPanel.test.tsx` (26 test)
- `frontend/src/components/transactions/detail/index.ts` — beş yeni export
- `frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx` — K2 (deep link devri) kapandı, not silindi
- `frontend/src/i18n/messages/{en,tr,es,zh}.json` — `transactionDetail.actions` altında **+34 anahtar ×4 dil**
- `frontend/vitest.config.ts`, `frontend/vitest.setup.ts` — test altyapısı (aşağıda)

**Doküman:**
- `Docs/07_API_DESIGN.md` — v3.8 → **v3.9** (§7.5 `buyerInventoryVisible` satırı + örnek + normatif not; başlık ve altbilgi hizalı)
- `Docs/DEFERRED_BACKLOG.md` — 3 yeni satır (50 → **53 aktif**)

### Test altyapısı — iki engel kaldırıldı (yan ürün)

Bu, S07 panelinin **ilk** RTL testiydi ve iki altyapı boşluğunu ortaya çıkardı; ikisi de yalnız
test tarafını etkiler, ürün derlemesi değişmedi:

1. **`vitest.config.ts`** — `next-intl`'in `createNavigation`'ı uzantısız `next/navigation`
   import ediyor; Next 16 bunu paket `exports` üzerinden değil kendi bundler eklentisiyle
   veriyor, dolayısıyla Vite çözemiyordu. `@/components/common` barrel'ına dokunan **her** test
   (LanguageSelector oradan geliyor) ilk assertion'dan önce yükleme hatası veriyordu. Alias +
   `server.deps.inline: ["next-intl"]` eklendi.
2. **`vitest.setup.ts`** — jsdom `<dialog>` etiketini ayrıştırıyor ama `showModal`/`close`
   metotlarını implement etmiyor; modal açan her bileşen (CancelModal, DisputeModal, yeni
   teslim onayı) mount'ta patlıyordu. Testlerin gözlediği davranışı (open bayrağı + `close`
   olayı) veren bir shim eklendi.

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | ACCEPTED'da satıcıya "hazırım" | ✓ | `ConfirmReadyButton` → `POST /confirm-ready`. Testler: "gives the seller the readiness button…", "posts confirm-ready and refetches on success", "honours the server's canConfirmReady flag…", "disables the button for a suspended session". Matris: `ACCEPTED × seller → acceptedSeller` |
| 2 | SELLER_CONFIRMED'da alıcıya ödeme | ✓ | `PaymentInfoBlock` zaten adresi çiziyor (`page.tsx` `showPaymentInfo`, T123'ten beri); T135 panelin **eksik olan** yarısını ekledi — satıcıya "ödeme bekleniyor", alıcıya ödeme yönlendirmesi. Testler: "tells the seller the payment is awaited…", "points the buyer at the payment details…" |
| 3 | PAYMENT_RECEIVED'da satıcıya trade deep link | ✓ | `SellerTradeCta`; `href` = `steamTradeOfferUrl`, `target=_blank`, `rel=noopener noreferrer` (üçü de assert ediliyor). Ayrıca item hatırlatması, yanlış-item uyarısı ve link yoksa ölü bağlantı yerine açıklama |
| 4 | PAYMENT_RECEIVED'da alıcıya "aldım" | ✓ | `ConfirmReceiptButton` → onay modalı → `POST /confirm-receipt`. Testler: "asks the buyer to confirm before sending an irreversible receipt" (modal AÇILDIĞINDA henüz istek gitmediği ayrıca assert ediliyor), "sends nothing when the buyer backs out" |
| 5 | (T134 D1 devri) `accepted` / `paymentReceived` / `itemDelivered` metinleri | ✓ | Üçü de yeniden yazıldı, dört dilde; custodial ifade kalmadı ("platform is preparing" için negatif assertion var) |
| 6 | (D3) Matris tamlık bekçisi | ✓ | `StateActionPanel.matrix.test.ts` — 39/39 hücre sınıflandırılmış, ölü `PanelRow` yok, çerçeve paylaşan satır kümesi ayrıca sabitlenmiş. 44 test |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| FE `tsc --noEmit` | ✓ exit 0 | — |
| FE `eslint` | ✓ exit 0 | — |
| FE `i18n:check` | ✓ **1344 × 4 identical key sets** | Taban `main`'de 1310×4 (ölçüldü) → **+34**. Advisory untranslatable uyarısı **15** — hepsi pre-existing, yeni anahtarlardan **0** |
| FE `vitest run` | ✓ **145/145** (13 dosya) | Taban 75/75 → **+70** (44 matris + 26 satır davranışı) |
| FE `next build` | ✓ `Compiled successfully` | exit 0 |
| FE `prettier --check` | ✓ | Lokal çalışma ağacı CRLF olduğu için repo geneli `format:check` 107 dosya uyarıyor — bilinen lokal artefakt. **Turun dokunduğu 18 dosya** LF'e normalize edilip ayrıca kontrol edildi: "All matched files use Prettier code style!" |
| Backend `dotnet build` (Release) | ✓ 0 Error / 0 Warning | tüm çözüm |
| Backend unit (CI filtresi) | **1450 / 1466**, 16 düşen | Düşen 16'nın **hepsi** `Skinora.Notifications.Tests.Unit.Channels` altında ve hepsinin hatası `DockerUnavailableException` — Testcontainers lokal Docker daemon'ına bağlanamıyor. T135 Notifications modülüne **hiç dokunmuyor**. |
| Backend integration | **lokalde koşulamadı** | Aynı sebep: Testcontainers + Docker Desktop daemon kapalı, makinede SQL Server servisi de yok (`Get-Service MSSQL*` → yalnız `SQLWriter`). D2'nin 7 yeni testi bu ayaktadır → **kanıt dal CI'sının "4. Integration test" job'ından gelecek** |

**Dürüstlük notu:** yukarıdaki iki satır bu turun bilinen kanıt boşluğudur. Backend
değişikliğinin tüm testleri (mevcut 39 + yeni 7) Integration ayağındadır; lokal makinede
koşturulamadıkları için **doğrulanmış hâlleri CI çıktısıdır**, lokal bir "yeşil" değil.

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

---

## Altyapı Değişiklikleri

- **Migration:** Yok. D2 yeni kolon eklemedi — mevcut `BuyerBaselineCapturedAt`'in projeksiyonu.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **API sözleşmesi:** `GET /transactions/:id` yanıtına `buyerInventoryVisible?` eklendi (yalnız
  ekleme; mevcut hiçbir alan değişmedi/kalkmadı, dolayısıyla geriye dönük uyumlu).

---

## Commit & PR

- Branch: `task/T135-state-action-panel-matrix`
- Commit: `<hash>` — T135: StateActionPanel state×rol matrisi
- PR: #<no>
- CI: ⏳

---

## Güvenlik Kontrolü (Katman 1)

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | Yok — yeni secret/anahtar/bağlantı dizesi eklenmedi |
| Auth / authorization | `buyerInventoryVisible` **taraf-özel**: `role is null` iken (public + prospective buyer) `null` döner ve bu ayrıca test edilir. Yeni bilgi ifşası değerlendirildi: satıcı aynı olguyu §7.6a yanıtında zaten alıyor, alıcı kendi envanteri hakkında bilgileniyor, üçüncü şahsa hiçbir şey açılmıyor. Yeni endpoint yok |
| Input validation | Yeni kullanıcı girdisi yok (iki uç da gövdesiz) |
| **Kullanıcı-kaynaklı `href`** | `steamTradeOfferUrl` alıcının verdiği URL'dir ve satıcıya `<a href>` olarak basılır — XSS yüzeyi olarak incelendi. **Güvenli:** `TransactionAcceptanceService:244` kolona `parsedTradeUrl.Normalized` yazıyor; `TradeUrlParser` şemayı (`http`/`https`), hostu (`steamcommunity.com`), yolu (`/tradeoffer/new/`) doğruluyor ve URL'i `partner` (yalnız rakam, ≤20) + `token` (alnum/-/_, ≤20) alanlarından **yeniden kuruyor** — `javascript:` veya yabancı host taşıyamaz. `rel="noopener noreferrer"` ayrıca test ediliyor |
| Yeni dış bağımlılık | **Yok.** Testler için `@testing-library/user-event` gerekiyordu; yeni paket eklemek yerine mevcut `@testing-library/react`'in `fireEvent`'i kullanıldı — `package.json` değişmedi |

---

## Known Limitations / Follow-up

Turda **ölçülen ama kapsanmayan** üç açık `DEFERRED_BACKLOG`'a adıyla yazıldı:

1. **`T135-FeDisputeTypeChoices` (🟡 — tek gerçek kullanıcı etkisi).** `DisputeForm.tsx:208` üç
   dispute türünü koşulsuz çiziyor; sunucunun WP5'te tam bu iş için eklediği
   `availableActions.disputableTypes` alanı FE tipinde bile tanımlı değil. SELLER_CONFIRMED'da
   yalnız `PAYMENT` açılabilirken alıcı `DELIVERY` seçebiliyor ve uç reddediyor. Matris satırı
   değil, dispute UI'ı (T92/WP5 bölgesi) — bu yüzden kapsama alınmadı.
2. **`T135-FeTimeoutLabelKeyStale` (⚪).** `helpers.timeoutLabelKey` hem ölü (çağıranı yok) hem
   bayat (custodial timeout tip adları; backend beş yeni ad yayınlıyor).
3. **`T135-TimelineHoldPreviousStatus` (⚪).** T134 doğrulamasının **G2** gözlemi: EMERGENCY_HOLD'da
   timeline `holdInfo.previousStatus` yerine sabit `SELLER_CONFIRMED` çiziyor. T134 "benim
   kaynaklı değil" diye doğru şekilde kaydetmişti ama sahipsiz kalmıştı; artık satırı var.

Ayrıca **davranış değişikliği olarak kayda geçen bir düzeltme:** public görünümde COMPLETED bir
işlem eskiden alıcının "İşlem başarıyla tamamlandı" mesajını görüyordu (rol kontrolü COMPLETED
dalından **sonra** geliyordu). Matris artık rolsüz görüntüleyiciye CREATED dışında hiçbir taraf
mesajı vermiyor — 04 §7.3'ün public varyantı zaten CREATED ile sınırlı.

---

## Notlar

**Working tree:** temiz (Adım -1, `git status --short` boş).

**Adım 0 — main CI startup check:** son 3 tamamlanmış run `success` —
[`32425168333`](https://github.com/turkerurganci/Skinora/actions/runs/32425168333) (CI, T134 #252) ·
[`32425168281`](https://github.com/turkerurganci/Skinora/actions/runs/32425168281) (Docker Publish, T134 #252) ·
[`32411998138`](https://github.com/turkerurganci/Skinora/actions/runs/32411998138) (CI, T139 #251).

**Bağımlılıklar:** T123 ✓ Tamamlandı (confirm-ready) · T126 ✓ Tamamlandı (confirm-receipt).

### Dış Varsayımlar (Adım 4 — hepsi kanıtlandı, kırık yok)

| Varsayım | Kanıt |
|---|---|
| `POST /transactions/:id/confirm-ready` var ve authenticated | `TransactionsController.cs:329` (`[HttpPost("{id:guid}/confirm-ready")]`, `AuthPolicies.Authenticated`, `RateLimit("user-write")`) |
| `POST /transactions/:id/confirm-receipt` var, idempotent | `TransactionsController.cs:386`; `ConfirmReceiptStatus.AlreadyDelivered` → 200 |
| `availableActions.canConfirmReady` / `canConfirmReceipt` sunucudan geliyor | `TransactionDetailService.cs:500-522` — **FE nüshasında ikisi de yoktu**, tur ekledi |
| `steamTradeOfferUrl` PAYMENT_RECEIVED × satıcıda dolu | `TransactionDetailService.cs:232` — FE yorumu emekli `TRADE_OFFER_SENT_TO_*` diyordu, düzeltildi |
| ITEM_DELIVERED'da `timeout.type = "settlement"`, `expiresAt = PayoutEligibleAt` | `TransactionDetailService.cs:432` |
| `CountdownTimer` gün/saat gösterebiliyor (04 §7.3 "dakika hassasiyeti gereksiz") | `CountdownTimer.tsx:134` `verboseDays` "{days}d {hours}h" — **yeni format gerekmedi** |
| Trade URL'i güvenli biçimde `href` yapılabilir | `TradeUrlParser` normalize ediyor (yukarıda §Güvenlik) |
| Yeni npm/NuGet bağımlılığı | Gerekmedi |

### Proje sahibi kararları (yapım öncesi soruldu, dördü de onaylandı)

- **D1 — ITEM_DELIVERED satırı DAHİL.** Gerekçe: T134 D1 `itemDelivered` metinlerini açıkça
  T135'e devretti ve 04 §7.3'ün ITEM_DELIVERED satırı karşılanmıyordu (net ödeme tarihi, gün/saat
  geri sayımı, alıcıda geri sayım **olmaması**).
- **D2 — `buyerInventoryVisible` KALICI**, yani detay DTO'suna alan (geçici inline uyarı +
  backlog satırı seçeneği reddedildi). Kapsamı backend + 07'ye genişletti.
- **D3 — Matris tamlık bekçisi EKLENSİN** (yalnız satır davranış testleriyle yetinilmedi).
- **D4 — REFUNDED açığı BU TURDA KAPATILSIN** (backlog satırı açılması reddedildi).

### Kalıcı ders

T134 bir katalog **kopyasının** sessizce eskidiğini ölçtü ve karşı önlemi bir parity testiydi.
T135 aynı mekanizmanın **dallanma** biçimini ölçtü: bir enum üzerinde `switch` yazan her katman,
enum büyüdüğünde sessizce eksik kalır — TypeScript bir `default` gördüğü an tatmin olur, eslint
ve `check-i18n.mjs` hiç bakmaz. REFUNDED tam bu şekilde iki helper kümesinden birden düşmüştü ve
WP5'ten (buyer-favor dispute) beri, T129'dan (settlement reversal) beri de erişilebilirdi.
**Karşı önlem tip sistemi değil, bir tamlık testidir** — her hücrenin karara bağlandığını iddia
eden ve bağlanmayanı **sayarak değil adıyla** söyleyen bir assertion.
