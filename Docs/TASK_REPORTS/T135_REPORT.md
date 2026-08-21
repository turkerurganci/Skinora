# T135 — StateActionPanel state×rol matrisi

**Faz:** F7 | **Durum:** ✓ **Tamamlandı — doğrulama ✓ PASS** | **Tarih:** 2026-08-21

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
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionDetailServiceTests.cs` — **+8 test** (`baselineCaptured` helper parametresi); sekizincisi mutasyon sondajının ürünü (aşağıda)

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
- `Docs/DEFERRED_BACKLOG.md` — **4** yeni satır (50 → **54 aktif**; dördüncüsü `T135-IntegrationSuiteParallelFlake`, Docker açıldıktan sonraki lokal ölçümden — bkz. §Lokal Ölçüm)

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
| Backend integration | ✓ **CI'da PASS + lokalde tekrarlandı** | (a) Dal CI'sı: **"4. Integration test"** `success` — run [`32461001539`](https://github.com/turkerurganci/Skinora/actions/runs/32461001539) ve [`32462859570`](https://github.com/turkerurganci/Skinora/actions/runs/32462859570), ikisi de yeşil. (b) **Docker açılarak lokalde de koşuldu** (proje sahibi talebi): `TransactionDetailServiceTests` **40/40**, `Skinora.Notifications.Tests` unit **111/111** (lokalde Docker yüzünden düşen 16'nın hepsi yeşil → "ortam kaynaklı" teşhisi **ölçüldü**), tam çözüm Integration filtresi **1369 testte 2 düşen** ve ikisi de **izole olarak geçiyor** (bkz. §Notlar — paralel koşum kararsızlığı) |

**Dürüstlük notu (kapandı):** yukarıdaki iki satır turun bilinen kanıt boşluğuydu — backend
değişikliğinin tüm testleri (mevcut 39 + yeni 7) Integration ayağındadır ve lokal makinede
koşturulamadı. Boşluk **dal CI'sında kapandı**: `Integration test` job'ı `success` ve aynı job
lokalde düşen 16 Notifications testini de yeşil koşturdu — yani "ortam kaynaklı" teşhisi
varsayım değil, **ölçüm**. Kanıt hâlâ lokal bir "yeşil" değil, CI çıktısıdır.

---

## Mutasyon Sondajı — D2 testlerinin ayırt ediciliği

Docker açıldıktan sonra (proje sahibi talebi) yeni testlerin gerçekten ayırt edip etmediği
ölçüldü. Her mutasyon uygulandı, derlendi, koşuldu ve **geri alındı**; çalışma ağacı her turdan
sonra temiz doğrulandı.

| # | Mutasyon | Sonuç |
|---|---|---|
| M1 | Projeksiyon ters çevrildi (`!...HasValue`) | **Tam 4 değer testi düştü** (`Is_True`, `Is_False`, `Reaches_The_Buyer_Too`, `Survives_A_Cancellation`), diğer 35 geçti ✓ |
| M2 | Kilometre taşı kapısı kaldırıldı (`!HasReachedPaymentWindow`) | **Tam 2 theory vakası düştü** (`Is_Unknown_Before_The_Seller_Confirms_Readiness` × CREATED/ACCEPTED), diğer 37 geçti ✓ |
| M3 | `role is null` kaldırıldı | **Hiçbir test düşmedi** — hayatta kalan mutant |
| M4 | `BuildResponseAsync`'in public routing'i kapatıldı | 2 **mevcut** test düştü; envanter alanı yine sızmadı |
| M3+M4 | İkisi birden | **4 düştü**, ikisi envanter erişim kontrolü ✓ |

### M3'ün açıkladığı şey — ve turun kendi düzelttiği bir iddia

M3 hayatta kalınca ilk okuma "kapı yük taşıyor, testin kapsam boşluğu var" oldu. **Bu yanlıştı.**
[`TransactionDetailService.cs:189`](../../backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs)
`if (role is null) return BuildPublicResponse(...)` diyor — yani **authenticated DTO yolu hiçbir
zaman `role == null` ile çalışmıyor** ve `BuildBuyerInventoryVisible` içindeki kapı **erişilemez
derinlemesine savunmadır**. Kapı yine de yerinde bırakıldı: iki kardeşi (`BuildPaymentAsync`,
`BuildSellerPayoutAsync`) birebir aynı kalıbı taşıyor, yani kaldırmak dosyanın konvansiyonunu
bozardı. M3+M4 bunu kesinleştirdi: **iki katman birbirini yedekliyor**, sızıntı için ikisinin
birden gitmesi gerekir — dolayısıyla M3'ü tek başına öldüren bir test **yazılamaz**, yazılsaydı
gerçek bir şeyi değil kendi kurgusunu ölçerdi.

### Sondajın bıraktığı kazanç — yeni test

`BuyerInventoryVisible_Is_Hidden_From_A_Stranger_Holding_A_Spent_Invite`. Harcanmış davet
token'ı olan yabancı, `GetByInviteTokenAsync`'in son `else`'inde `role = null` alıyor ve
prospective buyer'ın aksine — o yalnız işlem **hâlâ CREATED** iken mümkün, yani baseline'ı hiç
okunmamış olur — **hazırlık kilometre taşını çoktan geçmiş** bir işleme bakabiliyor. Sızıntının
gerçek stakes'i olan tek yol budur ve önceden **hiçbir test onu sabitlemiyordu**. Test M3+M4
altında düşüyor, yani sonucu gerçekten koruyor.

## Lokal Ölçüm (Docker açıldıktan sonra)

| Ölçüm | Sonuç |
|---|---|
| `Skinora.Notifications.Tests` unit filtresi | **111/111** — lokalde Docker kapalıyken düşen 16'nın hepsi yeşil |
| `TransactionDetailServiceTests` | **40/40** (32 taban + 8 yeni) |
| `BuyerInventoryVisible*` filtresi | **8/8**, hepsi isimleriyle doğrulandı |
| Tam çözüm Integration filtresi | **1369 testte 2 düşen**; ikisi de izole olarak geçiyor |

**Paralel koşum kararsızlığı (kayda geçti, backlog satırı açıldı).** Üç koşum yapıldı ve üçü de
**farklı** düşen küme verdi: (A) `INTEGRATION_TEST_SQL_SERVER` set edilmeden — her test sınıfı
kendi container'ını açtı — 28 düşen; **bu koşum CI'nın eşdeğeri değildir ve sayılmaz**.
(B) Paylaşılan sunucuyla 6 düşen, hepsi `Skinora.Fraud.Tests` (assembly tek başına **73/73**).
(C) Aynı kod, aynı kurulum: 2 düşen ve Fraud bu kez temiz — `NotificationDispatcherTests` +
`SettlementVerificationServiceTests`, ikisi de izole **8/8** ve **10/10**.
**Güçlü çeliştirici, over-claim edilmemeli:** ölçüm makinesinde aynı anda projenin tüm compose
stack'i (8 konteyner) çalışıyordu ve CI iki koşumda da yeşildi. Satır
`T135-IntegrationSuiteParallelFlake` (⚪) olarak açıldı; kapanışı **temiz bir makinede tekrarlı
ölçüm**.

## Doğrulama

**Tarih:** 2026-08-21 · **Validator:** bağımsız doğrulama chat'i (yapım raporu Faz 3'e kadar okunmadı)
**Dal:** `task/T135-state-action-panel-matrix` · **Commit:** `f8881a9` (HEAD == `origin/task/T135-…` aynı SHA)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Bloke edici bulgu | **0** |
| Bloke etmeyen not | 2 (N1 sayım driftı — bu turda düzeltildi · N2 `isTerminalStatus` artık tüketicisiz) |
| Düzeltme gerekli mi | Hayır |

### Giriş kapıları

| Adım | Sonuç |
|---|---|
| −1 Working tree | ✓ `git status --short` boş |
| 0 Main CI startup | ✓ son 3 run `success` — [`32425168281`](https://github.com/turkerurganci/Skinora/actions/runs/32425168281) · [`32425168333`](https://github.com/turkerurganci/Skinora/actions/runs/32425168333) · [`32411997251`](https://github.com/turkerurganci/Skinora/actions/runs/32411997251) |
| 0b Repo memory drift | ✓ `.claude/memory/MEMORY.md` T135 satırı mevcut |
| 3 Remote tazeleme | ✓ `git fetch origin` sonrası `HEAD == origin/task/T135-…` = `f8881a9` |

### Kabul kriterleri — bağımsız kanıt

Plan §F7 T135 iki satır sayıyor; ikisi de dört hücre içerir. Her hücre **koddan okunarak** ve
ayrıca **mutasyonla** doğrulandı (aşağıda).

| # | Kriter | Sonuç | Bağımsız kanıt |
|---|---|---|---|
| 1 | ACCEPTED'da satıcıya "hazırım" | ✓ | `panelRowFor(ACCEPTED,"seller") → acceptedSeller` → `StateActionPanel.tsx` `<ConfirmReadyButton>`; uç `POST /transactions/:id/confirm-ready` **var** (`TransactionsController.cs:329`) ve yanıt tipi FE `ConfirmReadyResponse` ile alan alan uyumlu (`Status`/`SellerReadyConfirmedAt`/`PaymentDeadline`/`BuyerInventoryVisible`). Sunucu bayrağı `canConfirmReady = role=="seller" && Status==ACCEPTED` (`TransactionDetailService.cs:504`) |
| 2 | SELLER_CONFIRMED'da alıcıya ödeme | ✓ | Ödeme bloğu sayfa düzeyinde: `showPaymentInfo = role==="buyer" && data.payment && status===SELLER_CONFIRMED` (`page.tsx:116`) → `PaymentInfoBlock` (`page.tsx:169`); panel `sellerConfirmedBuyer` satırıyla yönlendirme metnini ekliyor. Satıcı tarafının **main'de dalı yoktu**, eklendi (`sellerConfirmedSeller`) |
| 3 | PAYMENT_RECEIVED'da satıcıya trade deep link | ✓ | `paymentReceivedSeller` → `SellerTradeCta`: `href={tradeUrl}` + `target="_blank"` + `rel="noopener noreferrer"`, item hatırlatması, yanlış-item uyarısı, link yoksa `role="alert"` açıklaması. Backend alanı **yalnız** bu hücrede dolduruyor: `Status == PAYMENT_RECEIVED && role == "seller"` (`TransactionDetailService.cs:232`) |
| 4 | PAYMENT_RECEIVED'da alıcıya "aldım" | ✓ | `paymentReceivedBuyer` → `ConfirmReceiptButton`: buton → `<dialog>` onayı → `POST /transactions/:id/confirm-receipt` (`TransactionsController.cs:386`, idempotent 200). Sunucu bayrağı `canConfirmReceipt = role=="buyer" && Status==PAYMENT_RECEIVED` |

**04 §7.3 karşısında matris tamlığı bağımsız sayıldı:** `TransactionStatus` 12 değer +
`EMERGENCY_HOLD` overlay = 13; `panelRowFor` bunları 6 guard (FLAGGED + 4×`CANCELLED_*` +
REFUNDED) ve 6 enumerated `case` ile tüketiyor → **kalan yok**, `unclassified` bugün üretilemiyor.
13 × 3 görüntüleyici = **39 hücre**, hepsi karara bağlı.

### Doğrulama kontrol listesi

- [x] Kabul kriterleri (4 hücre) koddan ve testten doğrulandı
- [x] Referans doküman uyumu — 04 §7.3 satır satır karşılaştırıldı (ACCEPTED / SELLER_CONFIRMED / PAYMENT_RECEIVED / ITEM_DELIVERED / COMPLETED / `CANCELLED_*` / FLAGGED / EMERGENCY_HOLD / public varyant / suspended override)
- [x] İptal asimetrisi (04 §7.3, 02 §7) — satıcı modal uyarısı + alıcı gerekçeli devre dışı
- [x] ITEM_DELIVERED'da alıcıya geri sayım **gösterilmiyor**, satıcıya gün/saat gösteriliyor
- [x] 07 §7.5 sözleşme genişlemesi (D2) — kod ↔ doküman ↔ FE tipi üçü de uyumlu
- [x] Sunucu bayraklarının FE'de yeniden türetilmediği doğrulandı (`canConfirmReady` / `canConfirmReceipt` doğrudan tüketiliyor)
- [x] Yeni migration / config / env / bağımlılık yok — doğrulandı
- [x] Dal CI kanıtı **dal HEAD'ine** ait (Adım 8a)

### Test sonuçları — validator tarafından yeniden koşuldu

| Tür | Sonuç | Komut |
|---|---|---|
| FE tip kontrolü | ✓ exit 0, çıktı yok | `npx tsc --noEmit` |
| FE lint | ✓ exit 0, bulgu yok | `npm run lint` |
| FE i18n parity | ✓ **4 locale, 1344 anahtar, identical key sets** | `npm run i18n:check` |
| FE vitest | ✓ **145/145** (13 dosya) | `npm test` |
| Backend integration (ilgili) | ✓ **40/40** | `dotnet test Skinora.Transactions.Tests --filter TransactionDetailServiceTests` (Docker açık) |
| Dal CI (HEAD `f8881a9`) | ✓ run [`32470305165`](https://github.com/turkerurganci/Skinora/actions/runs/32470305165) `success` | Bloke edici **10/10** yeşil: Lint · Build · Unit · JS test · Integration · Contract · Migration dry-run · Docker build ×2 · **CI Gate** |

> **Advisory E2E 8/8 kırmızı — bloke edici değil, T135 kaynaklı değil.** Sekiz leg'in `continue-on-error`
> olduğu ve custody dönemine göre yazıldığı T137/T137a'dan beri kayıtlı; yeniden yazımın sahibi **T138**
> ve bağımlılığı T135'ti — bu turla açıldı. `CI Gate` job'ı `success`.

### Mutasyon sondajı — validator'ın kendi beş mutasyonu

Yapım turunun sondajları **tekrarlanmadı**; testlerin ayırt ediciliği bağımsız mutasyonlarla ölçüldü.
Her mutasyon uygulandı, koşuldu, geri alındı; çalışma ağacı sonda `git status --short` ile temiz doğrulandı.

| # | Mutasyon | Beklenen | Sonuç |
|---|---|---|---|
| V1 | `panelRowFor` ACCEPTED rolleri **takas edildi** | AC1 düşmeli | ✓ **7 test düştü** (63 geçti) — hem matris hem render |
| V2 | `UNWOUND` kümesinden **REFUNDED silindi** | Tamlık bekçisi yakalamalı | ✓ **5 test düştü**; yalnız `matrix.test.ts` — render testi yakalayamıyor, çünkü `unclassified` ile `unwound` **aynı** çıktıyı (null) veriyor. **Bekçinin var oluş sebebi tam olarak bu** |
| V3 | `buyerInventoryVisible === false` → `!buyerInventoryVisible` | "alan yok ≠ false" düşmeli | ✓ **tam 1 test düştü**: `stays silent while the answer is still unknown (field absent ≠ false)` |
| V4 | `ConfirmReceiptButton` onay modalı **atlandı** (`onClick={handleConfirm}`) | Geri alınamaz onay koruması düşmeli | ✓ **2 test düştü** (`asks the buyer to confirm…`, `sends nothing when the buyer backs out…`) |
| V5 | Backend `BuildBuyerInventoryVisible`'dan **kilometre taşı kapısı** kaldırıldı | "onaydan önce bilinmiyor" düşmeli | ✓ **tam 2 theory vakası düştü** (CREATED + ACCEPTED), diğer 38 geçti |

**Sonuç:** beş mutasyonun beşi de **hedeflenen** testler tarafından yakalandı; hiçbiri hayatta kalmadı,
hiçbirinde alakasız test gürültüsü olmadı. V2 ayrıca yapım turunun D3 gerekçesini **ölçümle** doğruladı.

### Güvenlik kontrolü

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | ✓ Temiz — yeni secret/anahtar/bağlantı dizesi yok |
| Auth / authorization | ✓ Temiz — `buyerInventoryVisible` taraf-özel; public + prospective + **harcanmış davetli yabancı** üç yolun üçü de test edilmiş. Yeni uç yok; iki uç da T123/T126'dan beri `AuthPolicies.Authenticated` + `RateLimit("user-write")` |
| Input validation | ✓ Temiz — iki uç da **gövdesiz**; yeni kullanıcı girdisi yok |
| Kullanıcı-kaynaklı `href` (XSS) | ✓ Temiz — **bağımsız doğrulandı:** `TradeUrlParser` şema (`http`/`https`), host (`steamcommunity.com`) ve yolu (`/tradeoffer/new/`) doğruluyor, `partner` (yalnız rakam ≤20) + `token` (alnum/`-`/`_` ≤20) alanlarını süzüp URL'i **yeniden kuruyor**; kolona yazılan `Normalized`'dir. `javascript:` şeması `Uri.UriScheme*` kontrolünde düşer |
| Yeni dış bağımlılık | ✓ Yok — `package.json` / `.csproj` değişmedi |

### Bloke etmeyen notlar

**N1 — Sayım driftı (bu turda düzeltildi).** Rapor §Etkilenen Modüller ve plan §F7 YAPIM TURU bloğu
`DEFERRED_BACKLOG` için "**3** yeni satır (50 → 53 aktif)" diyordu; dosyanın kendisi **4** yeni satır
ve **54 aktif** taşıyor. Dördüncü satır (`T135-IntegrationSuiteParallelFlake`) sonraki commit'te
(`c67937b`) eklenmiş; backlog'un kendi başlık bloğu ve raporun §Lokal Ölçüm bölümü doğru sayıyor —
yalnız iki özet satırı bayat kalmıştı. **İkisi de bu doğrulama turunda düzeltildi.** Kaynak dosya
(`DEFERRED_BACKLOG.md`) baştan doğruydu.

**N2 — `isTerminalStatus` artık tüketicisiz.** Panelin yeniden yazımı `!isTerminalStatus(status)`
kapısını (`main` `StateActionPanel.tsx:190`) `isActivePartyRow` ile değiştirdi; fonksiyon export
edilmeye devam ediyor ama FE'de **hiçbir çağıranı kalmadı** (`grep` ile doğrulandı). Bugün zararsız
ve `UNWOUND`'dan türediği için bayatlayamaz da; yine de ya bir tüketiciye bağlanmalı ya silinmelidir.
İş üretmediği için backlog satırı açılmadı — kayda geçirmek yeterli.

### Yapım raporu karşılaştırması

**Uyum: tam.** Yapım raporu Faz 3'e kadar okunmadı; okunduğunda bağımsız verdict ile **uyuşmazlık
çıkmadı**. Raporun altı kabul kriteri iddiasının altısı da yeniden üretildi. Raporun kendi kendini
düzelttiği M3 analizi (`role is null` kapısının erişilemez derinlemesine savunma olduğu) bağımsız
olarak da doğru bulundu: `BuildResponseAsync`'in `if (role is null) return BuildPublicResponse(...)`
yönlendirmesi authenticated DTO yolunu rolsüz çağrıya kapatıyor. Raporun tek yanlış satırı N1'deki
sayımdı ve düzeltildi.

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
- Commit: `ed0fdaa` — T135: StateActionPanel state×rol matrisi
- PR: [#253](https://github.com/turkerurganci/Skinora/pull/253)
- CI: **✓ PASS** — dalın **üç** commit'inin üçü de yeşil: `ed0fdaa` run [`32461001539`](https://github.com/turkerurganci/Skinora/actions/runs/32461001539) · `f0fbd8f` run [`32462859570`](https://github.com/turkerurganci/Skinora/actions/runs/32462859570) · **dal HEAD `c67937b` run [`32468527905`](https://github.com/turkerurganci/Skinora/actions/runs/32468527905)** — üçünde de `CI Gate` **success**. Mutasyon sondajının eklediği **sekizinci testi** kapsayan koşum sonuncusudur.
  Bloke edici **10 job'un 10'u** yeşil: Lint · Build · Unit test · JS test (vitest) · **Integration test** ·
  Contract test · Migration dry-run · Docker build (backend) · Docker build (frontend) · CI Gate.
  Docker build **iki kolda birden** koştu (backend + frontend) — tur her iki tarafa da dokunduğu için
  `paths-filter` doğru davrandı.
- **Lokal kanıt boşluğu CI'da kapandı:** "4. Integration test" job'ı `success` — D2'nin 7 yeni testi
  ve lokalde Docker yüzünden düşen 16 Notifications testi bu ayakta gerçek SQL Server'a karşı koştu.
- Advisory E2E **8/8 leg kırmızı, T135 kaynaklı DEĞİL**: leg bazında taban `main` (T134 run
  [`32425168333`](https://github.com/turkerurganci/Skinora/actions/runs/32425168333)) ile **birebir**
  aynı — happy-path 0/1 · cancellation 0/4 · timeout **1**/4 · payment-edge 0/6 · fraud-flags **3**/4 ·
  emergency-hold 0/3 · admin-flows **6**/7 · downtime 0/3, **toplam 10/32 → 10/32**. Spec'ler hâlâ
  custody durumlarına göre yazılı; yeniden yazımın sahibi **T138** (bağımlılığı T135'ti, artık açık).

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
