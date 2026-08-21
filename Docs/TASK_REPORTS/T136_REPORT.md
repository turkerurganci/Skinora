# T136 — Admin bot sayfaları silme + create-flow metinleri

**Faz:** F7 (P6 — Emeklilik / P7 — FE) | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS | **Tarih:** 2026-08-21

---

## Yapılan İşler

F7'nin custody emekliliğinin **frontend yarısı**. Backend yüzeyi T132'de, sidecar tarafı T133'te, doküman T133a'da düşmüştü; FE geride kalmıştı ve iki yerde **kullanıcıya yansıyan hata** üretiyordu.

Kapsam kod yazılmadan **9 paralel ajanla ölçüldü** (8 envanter şeridi — rota / hook-api / nav-yetki / i18n / create-flow / testler / doküman / backend-artık — artı 1 bağımsız tamlık eleştirmeni; 160 bulgu, 380 tool çağrısı). Eleştirmen şeritlerin üç hatasını yakaladı; en önemlisi `lib/api/admin.ts` için üç farklı silme aralığı verilmiş olması ve ikisinin, harfiyen uygulansaydı **canlı AD1 dashboard kodunu** silecek ya da yetim süslü parantez bırakacak olmasıydı. Uygulanan küme eleştirmenin `cat -n` ile yeniden ölçtüğü kümedir.

### Silinen (7 dosya, 805 satır)

| Dosya | Satır | Neden |
|---|---|---|
| `app/[locale]/admin/steam-accounts/page.tsx` | 39 | S18 rotası — AD10 backend'de yok (07 §9.10 kaldırma notu), sayfa 404 alıyordu. Dizin komple kalktı |
| `components/admin/SteamAccountsView.tsx` | 59 | bot filosu kompozitörü |
| `components/admin/SteamAccountCard.tsx` | 172 | bot kartı (emanet item sayısı, günlük trade kotası) |
| `components/admin/SteamAccountsStatus.tsx` | 160 | S12 dashboard bot sağlık bloğu — **canlı hata**, aşağıda |
| `components/admin/RecoveryQueuePanel.tsx` | 268 | AD26 triage tablosu (07 §9.29) |
| `components/admin/BotRecoveryQueue.tsx` | 51 | AD25 veri sarmalayıcısı (07 §9.28) |
| `lib/hooks/useAdminSteamAccounts.ts` | 56 | AD10/AD25/AD26 react-query hook'ları |

### Düzeltilen

- **`lib/api/admin.ts`** — AD10/AD25/AD26 tip+fonksiyon yüzeyi (105 satır: `AdminSteamAccountStatus`, `AdminSteamAccount`, `AdminSteamAccountsResponse`, `getAdminSteamAccounts`, `BotRecoveryStatus`, `BotRecoveryQueueItem/Response`, `getBotRecoveryQueue`, `UpdateBotRecoveryRequest`, `updateBotRecoveryItem`) ve `AdminDashboardResponse.steamAccounts` alanı
- **`app/[locale]/admin/dashboard/page.tsx`** — bot bloğu kaldırıldı, iki sütunlu grid tek çocuğa düştüğü için grid sarmalayıcısı da kalktı, bayat JSDoc ("three children … the bot block") tazelendi
- **`components/admin/index.ts`** — 5 bileşenin barrel ihracı (10 satır)
- **`components/layout/AdminSidebar.tsx`** — `AdminMenuItem.key` union üyesi + MENU girişi (04 §8.7 bunu adıyla emrediyor)
- **SignalR `AdminBotStatusChanged` zinciri** — `events.ts` payload arayüzü + event sabiti, `NotificationsHubClient.ts` import/handler tipi/`conn.on` kaydı, `RealtimeProvider.tsx` handler'ı + JSDoc satırı. Backend bu olayı **hiç yayımlamıyor** (`grep -rn "BotStatusChanged" backend/src` → 0)
- **`lib/admin/permissionCatalog.ts`** — `KNOWN_PERMISSION_KEYS` 14 → 12
- **i18n dört dil** — 59 anahtar/dil düştü
- **`i18n/catalog-parity.test.ts`** — SCOPE docstring'i tazelendi ("owned by T136" artık kapanmış iş; `adminSteamAccounts.*` blokları artık yok)

### Eklenen

- **`lib/admin/permissionCatalog.parity.test.ts`** (113 satır) — FE↔C#↔i18n üçlü katalog bekçisi

---

## Etkilenen Modüller / Dosyalar

**Kod (commit `59a0bd7`):** 24 dosya — 7 silme, 1 ekleme, 16 düzeltme. **188 ekleme / 1360 silme.** Tamamı `frontend/src` altında.

**Doküman (commit `fd3010f`):** `Docs/TASK_REPORTS/T136_REPORT.md` (yeni), `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`, `Docs/DEFERRED_BACKLOG.md` — artı `permissionCatalog.parity.test.ts`’te CI turu 1’in prettier düzeltmesi.

**Dokunulmayanlar:** `backend/`, `sidecar-steam/`, `sidecar-blockchain/`, `sidecar-fake/`, `e2e/`, `.github/`, docker-compose dosyaları ve 02–10 numaralı spec dokümanları. Doküman dayanağı T133a’da zaten hizalanmıştı; bu tur **kodu dokümana** getirdi, tersini değil.

---

## Kabul Kriterleri Kontrolü

### Yazılı kriterler (11_IMPLEMENTATION_PLAN.md §F7, Task T136)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `/admin/steam-accounts` rotası ve `RecoveryQueuePanel` / `useAdminSteamAccounts` yüzeyi silindi | ✓ | 7 dosya `git rm`; `next build` rota listesinde `/[locale]/admin/steam-accounts` **yok**; `grep -rn "steam-accounts" frontend/src` → 0 |
| 2 | FE nüshası `PermissionCatalog` ile birebir 12 anahtar, ölü i18n etiketleri düştü (`T133a-FePermissionCatalogKeys`) | ✓ | `grep -c '^  "' permissionCatalog.ts` → **12**; yeni bekçi testi `KNOWN_PERMISSION_KEYS` ↔ C# `PermissionCatalog.All` **sıra dahil** eşitliğini iddia ediyor ve geçiyor; `VIEW_STEAM_ACCOUNTS`/`MANAGE_STEAM_RECOVERY` 4 dilden düştü |

Kriter 2'nin doğrulama detayı: iki ölü anahtar 5. ve 6. sıradaydı; çıkarıldığında kalan 12 backend sırasıyla **birebir** hizalandı (`JSON.stringify(FE−2) === JSON.stringify(BE)` → `true`, envanter turunda programatik olarak ölçüldü). Başka hiçbir değişiklik gerekmedi. Dosyanın kendi JSDoc'u zaten "the 12 admin permissions" diyordu — yanlış olan yorum değil **dizi**ydi, dolayısıyla yorum düzeltilmedi.

### Proje sahibi onaylı kapsam eklentileri (ölçüm buldu, yazılı kriterde yoktu)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| B1 | "create-flow metinleri" — custodial anlatı 4 dilde P2P'ye çekildi | ✓ | 5 metin ailesi, aşağıda |
| B2 | Ölü DTO alanları + `CancelInfoBlock` ürün hatası | ✓ | 7 nokta; `grep -rn "itemReturned\|itemEscrowedAt\|escrowBotAssetId" frontend/src` → 0 |
| B3 | FE↔BE permission parity bekçisi | ✓ | 5 mutasyon, 5'i de yakalandı |
| B5 | `TimeoutPhase` drifti | ✓ | `events.ts:30` `SellerConfirm` ile hizalandı |

---

## Ölçümün en değerli iki bulgusu — ikisi de kullanıcıya yansıyan hata

**1. S12 dashboard'da kalıcı hata paneli.** `AdminDashboardResponse.steamAccounts` FE'de **zorunlu** alan olarak bildiriliyordu ama backend `AdminDashboardDtos.cs:5-7` T132'den beri yalnız `(SummaryCards, RecentFlags)` döndürüyor. Alan sessizce `undefined` geliyor, `dashboard/page.tsx` bunu `SteamAccountsStatus`'a besliyor, bileşen `if (isError || !accounts)` koluna düşüyordu → **her admin**, her dashboard ziyaretinde *"Could not load Steam accounts."* paneli ve altında 404'e gidecek bir "Yönet" linki görüyordu. 404 sınıfı bir kırıklık değil, **tip yalanı** kaynaklı bir drift — bu yüzden hiçbir test düşmüyordu.

**2. Her iptal edilen işlemde uydurulan sorun.** `CancelInfoBlock.tsx:37-41` `itemReturned` alanını **koşulsuz** (satırın `reason` gibi bir koşulu yoktu) render ediyordu. Backend `CancelInfoDto` v3.0'da alanı sildi (`TransactionDetailDto.cs:130-134` → `(CancelledBy, Reason, CancelledAt, PaymentRefunded)`), alan hiç gelmiyor → `undefined` → falsy → iptal edilen **her** işlemin detayında kullanıcı *"Item iade edildi: **Hayır**"* görüyordu. 02 §3.2 normatif olarak *"Item iadesi diye bir işlem yoktur"* diyor — yani ekran, P2P'de var olmayan bir sorunun **olumsuz** cevabını veriyordu. FE tipi (`transactions.ts:250`) alanı hâlâ vaat ettiği için TypeScript sessizdi.

Bunların hiçbiri T134 (enum/badge/timeline) veya T135 (StateActionPanel matrisi) kriterlerinde değildi — **sahipsizdiler**.

---

## Create-flow metinleri — kriterin gerçek yeri ölçüldü

Plan `(b)` yarısını tek satırda geçiyordu. Ölçüm, ifadenin **işlem oluşturma formunu işaret etmediğini** gösterdi:

S06 sihirbazının 7 dosyası (`NewTransactionForm`, `Step1ItemSelection`, `Step2Details`, `Step3BuyerWallet`, `Step4Summary`, `StepIndicator`, `EligibilityGate`) ve `newTransaction.*` i18n alt ağacının tamamı 4 dilde okundu. Custodial imza taraması (`bot|escrow|emanet|custod|platform hesab|trade offer|托管|机器人|custodia`) `newTransaction.*` altında **sıfır** isabet verdi. Metinler 04 §7.2 (satır 788-880) ve 03 §2.2 ile birebir; `createTransaction` payload'ının 7 alanının 7'si de canlı; bot hesabı seçimi, emanet onayı checkbox'ı gibi ölü bir adım/alan yok.

Custodial anlatı, satıcının forma **giderken** okuduğu yüzeylerdeydi. Proje sahibi kararıyla beş metin ailesi 04 §S01'in dört P2P adımına (satır 547-552) ve 02 §2.1/§2.2'ye hizalandı:

| Yüzey | Öncesi | Sonrası |
|---|---|---|
| `landing.howItWorks.steps` | 4 adım; 2. adım *"Eşya emanete alınır / Steam üzerinden eşya **platform botuna** gönderilir"* — FE'de kalan tek açık custody cümlesi | 04 §S01'in dört adımı: satıcı başlatır+davet eder → satıcı hazır onayı + alıcı ödemeyi emanete gönderir → satıcı **doğrudan** alıcıya gönderir → platform doğrular + ödemeyi aktarır |
| adım anahtarları | `itemEscrowed`, `buyerPays` | `paymentEscrowed`, `sellerDelivers` — anahtar adının kendisi yanlıştı: emanete alınan **para** |
| `HowItWorks.tsx:10,13` | `STEP_KEYS` + `STEP_ICONS` custodial adları sabitliyordu; 🛡️ "platform item'ı koruyor" imgesiydi | anahtarlar eşlendi; 🛡️ artık `paymentEscrowed`'da (korunan **para**), yeni slot 📦 |
| `landing.hero.subtitle` | *"**Eşyanız** ve ödemeniz … emanet **üzerinden** el değiştirir"* — giriş yapmamış her ziyaretçinin gördüğü ilk cümle | ödeme emanette, eşya doğrudan satıcıdan alıcıya |
| `auth.tos.summary.escrow` | *"**Eşyanız** ve ödemeniz … emanette tutulur"* — kullanıcının işlem açabilmek için **onayladığı** beş cümleden biri | *"Ödemeniz … emanette tutulur; platform eşyanın kendisini hiçbir zaman tutmaz"* |
| `legal.privacy.sections.howWeUse.body` | *"Steam takas tekliflerini **göndermek** ve doğrulamak"* | *"envanterleri **okuyarak** Steam teslimatlarını doğrulamak"* |

ToS maddesi özellikle önemliydi: platformun kullanıcıya verdiği **yazılı taahhüt** 02 §2.1'in ("Platform item'a hiçbir zaman dokunmaz") ve 02 §580'in ("Platform item custody'si garanti etmez, edemez") tam tersini söylüyordu — üstelik aynı dosyadaki `legal.terms.serviceDescription.body` T133a sonrası **zaten doğru** yazılmıştı, yani modal kendi tam sözleşmesiyle de çelişiyordu. Hedef metin repoda mevcuttu.

`landing.hero.title` (es/zh yerelleştirmeleri "ítem custody" olarak da okunabiliyor) **dokunulmadı** — envanter turu bunu `BELIRSIZ` işaretledi ve 04 §541 dokümanın kendisinin de aynı belirsizliği taşıdığını gösterdi; bu bir kopya/marka kararı, ölçümle kapatılamaz.

---

## Eklenen bekçi ve mutasyon sondajları

Sapmayı üreten şey bekçisizlikti ve bu **ölçüldü**: `permissionCatalog.ts` son commit'i `0a36031` (WP5, #174), backend `PermissionCatalog.cs` son commit'i `eb0e49d` (T132, #247) — FE nüshası silme turundan hiç haberdar olmadı ve hiçbir test düşmedi.

Mevcut hiçbir kapı bu ekseni tutamıyordu:
- `enums.parity.test.ts` kataloğu **göremez** — parser regex'i `public enum (\w+)` arıyor, `PermissionCatalog` bir `static class` + `IReadOnlyList<PermissionEntry>`
- `check-i18n.mjs` yalnız **diller arası** parity ölçüyor (`baseKeys` vs `keys`), kod↔mesaj yönünü hiç ölçmüyor
- `catalog-parity.test.ts` bloğu **kapsam dışı** ilan etmiş ve sahibini adıyla T136 diye yazmıştı
- Bekçi **backend'de vardı** (`AdminRolesEndpointTests.cs:88-96` — `Assert.Equal(12, …)` + iki `DoesNotContain`), FE'de yoktu

Yeni test FE dizisini C# kaynağıyla (**sıra dahil** — 07 §9.11 sıra-normatif) ve dört dilin `adminRoles.permissions` bloğuyla karşılaştırır; C# tarafını iki adımda parse eder (`Keys` sabitleri + `All` listesi), çünkü `All` anahtarlara sembolik atıf yapar.

**Beş mutasyon koşuldu, beşi de hedeflenen testlerce yakalandı, hayatta kalan mutant yok:**

| # | Mutasyon | Düşen test | Yorum |
|---|---|---|---|
| M1 | Ölü `VIEW_STEAM_ACCOUNTS` FE kataloğuna geri (T136 öncesi hâl) | 2 | sıra testi + etiket halkası testi |
| M2 | Tek dilde (`tr`) ölü etiket bırakıldı | **tam 1** | yalnız o dilin üyelik testi |
| M3 | **Dört dilde birden** ölü etiket bırakıldı | 4 | **`check-i18n.mjs` YEŞİL kaldı** — *"parity OK — 1286 keys each, identical key sets"*. T134'ün kalıcı dersi ("dördü birden yanlışken parity yeşil kalır") burada **varsayım değil ölçüm** |
| M4 | Sıra takası (`VIEW_USERS` ↔ `MANAGE_ROLES`) | **tam 1** | yalnız sıra testi; üyelik testleri doğru şekilde etkilenmedi — küme değişmedi |
| M5 | C# parser'ı kör et (`All` → `AllEntries`) | 6 | bekçi **kendini** kontrol ediyor: sessizce boş liste karşılaştırmak yerine gürültüyle düşüyor |

M4'ün ilk denemesi CRLF nedeniyle **hiç inmedi** (`replace` tutmadı) ve test 7/7 geçti; bu bir hayatta kalan mutant değil, uygulanmamış bir mutasyondu — doğru satır sonuyla tekrarlandı ve mutasyonun indiği dosya içeriğiyle doğrulandıktan sonra tam 1 test düştü.

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Tip kontrolü | ✓ 0 hata | `npx tsc --noEmit` |
| Lint | ✓ 0 hata | `npm run lint` (eslint) |
| i18n parity | ✓ | `npm run i18n:check` → **"parity OK — 4 locales, 1285 keys each, identical key sets"**. 1344 → 1285 (−59/dil). 15 advisory untranslatable uyarısı **öncesiyle aynı** (Gas fee / Mobile Authenticator); yeni metinler **hiç** eklemedi |
| Unit | ✓ **152/152** | `npm test` (vitest), 14 dosya. T135'te 145/13 idi — yeni bekçi **tam 7** test ekledi (1 öz-kontrol + 1 sıra + 4 dil + 1 etiket halkası) |
| Build | ✓ | `npm run build` (`next build`). Rota listesinde `/[locale]/admin/steam-accounts` **yok** |

**i18n silme dökümü (dosya başına):** `adminSteamAccounts` 45 + `adminDashboard.steamAccounts` 10 + `adminNav.steamAccounts` 1 + `adminRoles.permissions` ölü 2 + `transactionDetail.cancelInfo.itemReturned` 1 = **59**. Dört dil × 59 = 236 anahtar. Anahtar **yeniden adlandırmaları** (`itemEscrowed`→`paymentEscrowed`, `buyerPays`→`sellerDelivers`) sayıyı değiştirmez.

**Bayat `.next` uyarısı:** ilk `tsc --noEmit` koşumu `.next/types/validator.ts(197)` üzerinden silinen rotayı arayan **tek** bir hata verdi. `.next` git'e izlenmiyor (`frontend/.gitignore:17`) ve üretilmiş manifest bir önceki build'e aitti; `rm -rf .next` sonrası tsc temiz. CI her koşumda sıfırdan build ettiği için bu bir bulgu değil.

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✓ PASS** (2026-08-21, bağımsız chat — INSTRUCTIONS.md §3.3 izolasyon kuralı) |
| Bloke edici bulgu sayısı | **0** |
| Bloke etmeyen bulgu | **1** (N1 — aşağıda, backlog satırı açıldı) |
| Düzeltme gerekli mi | Hayır |
| Dal / commit | `task/T136-admin-bot-surface-removal` · `e4e9ee1` (`HEAD == origin/task/...`, doğrulama sırasında ilerlemedi) |

**Giriş kapıları temiz.** Adım -1 working tree boş; Adım 0 main son 3 run'ın üçü de `success` (`32478492189`, `32478492044` — T135 #253; `32425168281` — T134 #252); Adım 0b repo memory T136 satırı mevcut (`.claude/memory/MEMORY.md:58`).

### Kabul kriterleri — ikisi de bağımsız olarak yeniden üretildi

| # | Kriter | Sonuç | Validator kanıtı (yapım turunun sondajları tekrarlanmadı, kendi ölçümü koşuldu) |
|---|---|---|---|
| 1 | FE nüshası `PermissionCatalog` ile birebir 12 anahtar, ölü i18n etiketleri düştü | ✓ | İki kaynak **yan yana** okundu: `PermissionCatalog.cs` `All` listesi 12 giriş, `permissionCatalog.ts` 12 anahtar, **sıra dahil birebir**. Dört dilin `adminRoles.permissions` bloğu programatik sayıldı → **12 / 12 / 12 / 12**, dördü de aynı küme. `VIEW_STEAM_ACCOUNTS` / `MANAGE_STEAM_RECOVERY` için dört dilde ve repo genelinde grep → **0** |
| 2 | `/admin/steam-accounts` rotası ve `RecoveryQueuePanel` / `useAdminSteamAccounts` yüzeyi silindi | ✓ | Rota dizini yok (`app/[locale]/admin/` altında `steam-accounts` **yok**); `next build` rota listesi 11 admin rotası basıyor, silinen rota **yok**; yedi ada karşı repo-genişliğinde grep (`steam-accounts`, `RecoveryQueuePanel`, `useAdminSteamAccounts`, `SteamAccountCard`, `SteamAccountsStatus`, `SteamAccountsView`, `BotRecoveryQueue`) `frontend/src` + `frontend/e2e`'de **0 isabet**; tek kalan atıf `catalog-parity.test.ts`'in kapsam docstring'i ve o **bilerek** tazelenmiş |

### Kapsam eklentileri — ölü olduğu backend'den teyit edildi

Yazılı kriterlerin dışındaki her silme, **backend tarafında karşılığının gerçekten olmadığı** doğrulanarak kabul edildi (S2 Kırılma taraması):

| Silinen FE yüzeyi | Backend teyidi |
|---|---|
| `AdminDashboardResponse.steamAccounts` | `AdminDashboardDtos.cs:5-7` → `(SummaryCards, RecentFlags)`, alan yok |
| AD10 / AD25 / AD26 istemcisi (`admin.ts`, 105 satır) | `grep -rn "steam-accounts" backend/src --include=*.cs` → **0** uç |
| `AdminBotStatusChanged` SignalR zinciri | `grep -rn "AdminBotStatusChanged" backend/src backend/tests` → **0** |
| `CancelInfo.itemReturned` + `CancelTransactionResponse.itemReturned` | `TransactionDetailDto.cs:128` / `TransactionLifecycleDtos.cs:249` → alan v3.0'da düşürülmüş, yalnız kaldırma yorumu duruyor |
| `escrowBotAssetId` | Canlı modelde yok; yalnız dondurulmuş migration dosyalarında (beklenen) |
| `TimeoutPhase` `TradeOfferToSeller` → `SellerConfirm` | `Skinora.Shared/Enums/TimeoutPhase.cs` → `Accept / SellerConfirm / Payment / Delivery`; FE mirror'ı **yalan söylüyordu**, düzeltildi |

Metin turunun doküman dayanağı da ayrıca okundu: **04 §6.1 S01** dört adımı (satıcı başlatır+davet → hazır onayı + ödeme emanete → satıcı doğrudan gönderir → platform doğrular + ödeme aktarılır) yeni i18n metinleriyle **birebir**; **04 §8.1** S12 layout'unda sol menüde Steam Hesapları girişi ve dashboard'da bot sağlık paneli **yok**; **04 §4.3** admin navigasyonunda S18 **yok**; **03 §8.1** dashboard maddesi yalnız `recentFlags` sayıyor. Kod dokümanı takip ediyor, tersi değil.

### Validator kendi mutasyonlarını koştu — altısı da yakalandı, hayatta kalan mutant yok

Yapım turunun beş mutasyonu **tekrarlanmadı**; bekçinin iddia ettiği üç ekseni (FE dizisi / C# kaynağı / dört dil) ve öz-kontrolünü ayrı ayrı zorlayan altı mutasyon bağımsız olarak uygulandı:

| # | Mutasyon | Düşen test | Yorum |
|---|---|---|---|
| V1 | FE kataloğuna 13. anahtar (`VIEW_STEAM_ACCOUNTS`) eklendi | 2 | sıra testi + etiket halkası testi |
| V2 | FE'de `VIEW_FLAGS` ↔ `MANAGE_FLAGS` sırası takas edildi | **tam 1** | yalnız sıra testi — küme değişmediği için üyelik testleri doğru şekilde sessiz kaldı (07 §9.11 sıra-normatifliği gerçekten ölçülüyor) |
| V3 | `en.json`'dan `MANAGE_SANCTIONS` etiketi silindi | 2 | o dilin üyelik testi + etiket halkası |
| V4 | `zh.json`'a ölü etiket eklendi | **tam 1** | yalnız o dilin üyelik testi |
| V5 | **Backend** C# kataloğuna 13. giriş eklendi | 5 | **en değerlisi:** bekçi gerçekten canlı C# kaynağını okuyor, dondurulmuş bir nüshayı değil — sıra testi + dört dilin hepsi düştü |
| V6 | C# parser'ı körleştirildi (`All` → `AllEntries`) | 6 | öz-kontrol (`expected 0 to be greater than or equal to 10`) sessiz boş-liste karşılaştırması yerine gürültüyle düşüyor |

Her mutasyondan sonra dosya geri alındı; doğrulama sonunda `git status --short` **boş**.

### Test ve CI kanıtı

| Tür | Sonuç | Komut / run |
|---|---|---|
| Lint (eslint) | ✓ 0 bulgu | `npx eslint` → çıktı yok, exit 0 |
| i18n parity | ✓ | `npm run i18n:check` → *"parity OK — 4 locales, **1285** keys each, identical key sets"*; 15 advisory untranslatable uyarısı **T136 öncesiyle aynı** (Gas fee / Mobile Authenticator, kayıtlı backlog kalemi) |
| Anahtar parity (bağımsız) | ✓ | Dört dil düzleştirilip karşılaştırıldı: **1285 / 1285 / 1285 / 1285**, `missing: 0 extra: 0` |
| Unit (vitest) | ✓ **152/152** | `npx vitest run`, 14 dosya |
| Build / tip | ✓ | `npm run build` (`next build`) exit 0; rota listesinde silinen rota yok |
| Dal HEAD CI | ✓ `success` | run [`32491360190`](https://github.com/turkerurganci/Skinora/actions/runs/32491360190) (`e4e9ee1`) — bloke edici **10/10 yeşil**, `CI Gate` success. (Rapor gövdesi bir önceki commit'in run'ını — `32489325975` / `fd3010f` — anıyor; HEAD'in kendi run'ı da yeşil.) |
| Advisory E2E | 8/8 kırmızı — **T136 kaynaklı DEĞİL** | Taban ölçüldü: T136 **öncesi** T135 dal HEAD run'ı [`32478473148`](https://github.com/turkerurganci/Skinora/actions/runs/32478473148) **aynı sekiz leg'i aynı şekilde** kırmızı bırakıyor. Sahibi T138 |

### Güvenlik kontrolü

- **Secret sızıntısı:** Temiz — eklenen satırlarda sır kalıbı yok; `package.json` / lockfile değişmedi.
- **Auth/authorization:** Temiz ve **bağımsız olarak teyit edildi**. Yetkilendirme sunucu tarafında (`[Authorize(Policy = "Permission:...")]` + `PermissionCatalog.IsKnown`); FE kataloğu yalnız S19 etiketlemesi için okunuyor (`RoleFormModal.tsx:40`, `t.has(key) ? t(key) : p.label`). İki ölü anahtarın düşmesi hiçbir kapıyı gevşetmiyor. Backend'in kendi bekçisi de yerinde: `AdminRolesEndpointTests.cs:88-96` AD11 cevabında `Assert.Equal(12, …)` + iki `DoesNotContain`.
- **Input validation:** Etkilenmedi — yeni kullanıcı girdisi yok.
- **Yeni bağımlılık:** Yok. Eklenen test yalnız `node:fs` / `node:url` / `node:path` kullanıyor.

### N1 — bloke etmeyen bulgu (backlog satırı açıldı)

**FE↔C# parity bekçileri, drift'in YARATILDIĞI PR'da koşmuyor.** T136'nın eklediği `permissionCatalog.parity.test.ts` ve kardeşi `enums.parity.test.ts` (T134) backend C# kaynağını okur ama `frontend/` altında yaşar; ikisini de yalnız `3b. JS test (vitest)` job'ı koşturur, o job `frontend` / `sidecar-*` filtrelerine bağlıdır (`ci.yml:304`) ve `frontend` filtresi yalnız `frontend/**` + `.github/workflows/**`'dir (`ci.yml:69-71`). Backend-only bir PR job'ı `skipped` yapar; `ci-gate` yalnız `failure`/`cancelled` arar (`ci.yml:740-745`), yani **`skipped` geçer** ve bekçi hiç koşmadan CI Gate yeşil kalır.

**Bu bir varsayım değil, tarihin kendisi:** T136'nın kapattığı drift'i yaratan T132'nin squash commit'i (`eb0e49d`) `.claude/`, `Docs/`, `backend/` ve `docker-compose*.yml` dosyalarına dokunuyor — `frontend/**`'e **hiç** dokunmuyor. Yani yeni bekçi o gün var olsaydı da **koşmayacaktı**. Backend'deki ikame sinyal kısmî: `AdminRolesEndpointTests.cs` sayıyı tutar ve backend-only PR'da koşar, ama FE nüshasının **varlığından söz etmez** — nitekim T132 o assertion'ı güncelledi (satır 88-91'deki yorum bunu yazıyor) ve FE nüshası yine de bayatladı.

**Neden bloke edici değil:** T136'nın iki yazılı kabul kriteri de bir bekçi **talep etmiyor**; ikisi de bugünkü kod durumu üzerinden kanıtla karşılandı. Bekçi turun gönüllü eklentisi ve bugün doğru çalışıyor (altı mutasyonun altısını yakaladı). Açık **yapısaldır ve T136'dan eskidir** (aynı kablolama T134'te merge edildi), bu yüzden düzeltme T136'ya yüklenmedi — sahipli bir backlog satırına yazıldı: `T136-ParityGuardsSkippedOnBackendPRs` (🟡). Önerilen ucuz kapatma `frontend` paths-filter'ına bekçilerin okuduğu C# yollarını eklemektir; job'ın `if`'i değişmez, yalnız tetikleyici genişler.

**Kalıcı ders:** bir kopya bekçisi, **izlediği kaynağın değiştiği PR'da koşmuyorsa** yalnız kendi tarafındaki bozulmayı yakalar. T134/T136'nın ortak dersi "kopya nüsha drift eder" idi; N1 bunun bir katman altını gösteriyor — **bekçinin tetikleyicisi de kapsamın bir parçasıdır**, ve tetikleyici sessizce dar kaldığında bekçi "yeşil" değil **görüşsüz**dür.

### Yapım raporu karşılaştırması

**Uyum: tam.** Raporun ölçtüğü her iddia bağımsız olarak yeniden üretildi ve doğru bulundu — 12 anahtarlık katalog, dört dilin 1285 anahtarı, silinen yedi dosya, 04 §S01 dört adımı, `AdminDashboardResponse` alan yokluğu, `TimeoutPhase` hizalaması, advisory leglerin T136 kaynaklı olmayışı ve mutasyonların yakalandığı. Rapor kendi öz-hatalarını da (mutasyon sondajı sırasında çalışmanın geri alınması, prettier CRLF yanlış-pozitifi, pre-commit BIP-39 yanlış-pozitifi ve **bypass edilmemiş** oluşu) kayda geçirmiş; validator bunlarda uyuşmazlık bulmadı. Uyuşmazlık **yok**; rapora eklenen tek yeni şey N1'dir — raporun kapsamadığı, bekçinin *tetiklenme koşuluna* dair yapısal bir ölçüm.

---

## Altyapı Değişiklikleri

- **Migration:** Yok
- **Enum/şema:** Yok — `types/enums.ts`'e dokunulmadı, `enums.parity.test.ts` bekçisi etkilenmedi
- **Config/env:** Yok
- **Docker:** Yok
- **Yeni bağımlılık:** Yok (saf silme + metin düzeltmesi + bir test dosyası)

---

## Commit & PR

- Branch: `task/T136-admin-bot-surface-removal`
- Commit: `59a0bd7` (kod) · `fd3010f` (rapor + status + memory + backlog, prettier düzeltmesi)
- PR: [#254](https://github.com/turkerurganci/Skinora/pull/254)
- CI turu 1: run [`32488164707`](https://github.com/turkerurganci/Skinora/actions/runs/32488164707) → **`1. Lint` ✗** (kök neden aşağıda). Düzeltme `fd3010f` push edilince bu run **cancelled** oldu — concurrency, başarısızlık değil (task.md Bitiş Kapısı concurrency notu).
- CI turu 2: run [`32489325975`](https://github.com/turkerurganci/Skinora/actions/runs/32489325975) (`fd3010f`) → **✓ `success`**. Bloke edici **10/10 yeşil**: `1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (frontend)` · `Detect changed paths` · **`CI Gate` success**.
- Advisory E2E: 8/8 leg kırmızı — **T136 kaynaklı değil**. Legler custody dönemine göre yazılı ve T117'den beri kırmızı; sahibi **T138**, bağımlılığı T135'ti ve o tur açıldı. T136'nın bu leglere katkısı `admin-flows.spec.ts:124-128`'in `steamAccounts` iddiasının artık **kesin** ölü olması; envanter turu ayrıca beş ölü assertion daha buldu (Known Limitations §2, backlog `T136-E2EDeadItemReturnedAssertions`).

### CI turu 1 — `1. Lint` kırıldı, kök neden bulundu ve kapatıldı

İlk run'ın **bloke edici** `1. Lint` job'ı düştü. Bloke edici diğer joblar sırasını beklerken `3b. JS test (vitest)` yeşildi; advisory E2E legleri ayrı konu — custody dönemi spec'leri, sahibi **T138** (bkz. Known Limitations §2).

Kök neden **lokal olarak doğrudan yeniden üretilemedi**: `npm run format:check` bu makinede **115 dosyada** uyarıyor — turun hiç dokunmadığı dosyalar dahil (`auth-store.ts`, `blockchain.ts`, `enums.ts`). Bu, proje hafızasındaki `e2e-prettier-crlf-local-artifact` kaydının birebir tekrarı: `core.autocrlf=true`, diskte CRLF, prettier LF bekliyor; CI'nin LF checkout'u yetkili. Yani lokal çıktı bu soruda **kullanılamaz** ve "hangi dosya gerçekten bozuk" sorusunu gizler.

Ayrım şöyle yapıldı: turun değiştirdiği 17 dosyanın **git'te saklanan (LF) içeriği** `git show HEAD:<dosya>` ile çıkarıldı ve `frontend/` **altındaki** geçici bir dizine yazıldı — prettier config'i dosyadan yukarı doğru aradığı için konum önemli. (İlk deneme kopyaları scratchpad'e koymuştu; `.prettierrc` bulunamadığı için **12 dosya birden** uyardı ve ölçüm geçersizdi. Fark edilip tekrarlandı.)

Doğru ölçümde **tam bir dosya** uyardı: `src/lib/admin/permissionCatalog.parity.test.ts` — tek bir satır `printWidth: 100`'ü aşıyordu (`expect(value, ...).toBeDefined();`, 103 karakter). Prettier'in istediği üç satırlık sarma uygulandı; LF içerik üzerinde `--check` artık *"All matched files use Prettier code style!"* diyor ve testin kendisi 7/7 geçmeye devam ediyor.

Diğer **16 dosyanın LF hâli temiz** çıktı — yani lokal 115 uyarının tamamı CRLF gürültüsüydü ve turun ürettiği tek gerçek sapma bu satırdı.

**KALICI DERS:** bilinen bir lokal yanlış-pozitif gerçek bir bulguyu **maskeler**. "Lokal prettier zaten hep uyarıyor, CI yetkili" bu turda doğru bir cümleydi ama **yetersizdi** — doğru olan cümle "lokal ölçüm kullanılamaz, o hâlde kullanılabilir bir lokal ölçüm kur" olmalıydı. Ayırt edici ölçüm CI beklenmeden yapılabilirdi: git'in sakladığı LF içeriği, config'in **bulunabildiği** bir yola koymak. Hafıza kaydı `e2e-prettier-crlf-local-artifact` "bulgu sayma" diyor; eksik olan yarısı **"ama LF üzerinden ayrıca ölç"**.

---

## Known Limitations / Follow-up

1. **`formatRelativeTime` üretimde yetim kaldı.** Tek üretim çağıranı `SteamAccountCard.tsx:137` idi. Fonksiyon `lib/utils/format.ts:71`'de `formatStablecoin`/`formatPercent` ile aynı genel amaçlı util ailesinde ve kendi testi var (`format.test.ts:35-39`). Silinmedi — genel amaçlı bir formatlayıcıyı tek çağıranı düştü diye kaldırmak T136'nın kapsamı değil. **DEFERRED_BACKLOG adayı** (`T136-FormatRelativeTimeUnused`, ⚪).

2. **E2E tarafı T138'e devredildi** — bu turda kasten dokunulmadı. Envanter altı ölü custody assertion'ı ölçtü, **beşi hiçbir şeridin bulmadığı** yerlerdeydi ve tamlık eleştirmeni tarafından çıkarıldı:
   - `e2e/tests/cancellation.spec.ts:80 · :96 · :118` — üç `expect(body.itemReturned).toBe(true)` **hard-fail** (alan `CancelTransactionResponse`'ta yok). Test **başlıkları** da custody dilinde (`:73`, `:89` — "item returned")
   - `e2e/tests/emergency-hold.spec.ts:206` — hard-fail; **`:142` ise boşuna geçiyor** (`resumeBody.itemReturned ?? null` → `toBeNull()`; alan hiç olmasa da geçer, yani iddia artık hiçbir şey doğrulamıyor)
   - `e2e/tests/admin-flows.spec.ts:124-128` — `steamAccounts` bloğunun ≥1 seed'li bot içerdiğini iddia ediyor; alan AD1'de hiç yok, seed de yok (`e2e/src/db.ts:67`, T137a)
   - `e2e/src/api.ts:165` — `getAdminDashboard` docstring'i `steamAccounts` döndüğünü yazıyor (yalnız yorum)
   - `docker-compose.e2e.yml:14` — *"Schema + happy-path seed (users, bot) are applied by the Playwright harness"*; T137a bot seed'ini koddan kaldırmış, compose yorumu güncellenmemiş

3. **Ölü rol satırları — migration borcu (backlog adayı).** `AdminRolePermissions` tablosunda `VIEW_STEAM_ACCOUNTS` / `MANAGE_STEAM_RECOVERY` string'lerini taşıyan satırlar dev/staging DB'lerde kalmış olabilir. Envanter turu bunun **tuzağa dönüşmediğini** ölçtü: `PermissionAuthorizationHandler.cs:24` bu anahtarları isteyen bir policy bulamıyor, ve `UpdateAsync` (satır 137-144) ilk rol düzenlemesinde `desired` dışındaki satırları soft-delete ediyor; FE gönderilen kümeyi katalogdan türettiği için (`RoleFormModal.tsx:61`) ölü anahtar geri yollanamaz → `INVALID_PERMISSION` 400 tuzağı **doğmuyor**. Kalan etki kozmetik (AD14 dizisinde fazladan eleman, `RolesTable.tsx:48` sayaç rozetinde +1). Ucuz kapatma yolu tek satırlık idempotent bir `DELETE`; T136'nın ön koşulu **değil**.

4. **İki derleme bekçisi bu turda yoktu ve hâlâ yok** (tamlık eleştirmeninin ölçümü): `next.config.ts` `typedRoutes: false`, dolayısıyla geride kalmış bir `/admin/steam-accounts` href'i **derleme hatası üretmezdi** — üstelik iki canlı çağrı da düz template string olduğu için typedRoutes açık olsa bile kaçardı. Ve next-intl varsayılanları (`defaultGetMessageFallback`, `node_modules/use-intl/…/initializeConfig-B5qJiBCm.js:14-16`) eksik anahtarı **fırlatmadan** düz metin olarak basar; proje bu varsayılanları ezmiyor (`i18n/request.ts` yalnız `{locale, messages}` döndürüyor) ve `IntlMessages` tip augmentation'ı yok. Yani yarım kalmış bir silme turu üç ayrı sessiz üretim bozukluğu üretebilirdi (menüde düz anahtar metni, 404'e giden link, dashboard'da ölü blok) ve **CI'nin tamamı yeşil kalırdı**. Bu turda karşı önlem el disiplini + tam süpürme oldu; kalıcı bir kapı isteniyorsa `IntlMessages` augmentation'ı veya kod↔mesaj lint'i ayrı bir kalem.

5. **`lib/signalr/events.ts` başka union nüshaları taşıyor.** `TimeoutPhase` bu turda hizalandı ama aynı dosyadaki `EmergencyHoldReleaseAction` gibi string-union kopyalarını hiçbir bekçi C#'a karşı karşılaştırmıyor — bu zaten kayıtlı: `DEFERRED_BACKLOG` `T134-FeEnumUnionDup`.

---

## Notlar

**Working tree hygiene (Adım -1):** temiz — `git status --short` boş.

**Main CI startup check (Adım 0):** son 3 run'ın üçü de `success` — `32478492189`, `32478492044` (T135 #253), `32425168281` (T134 #252).

**Bağımlılık:** T132 ✓ Tamamlandı (doğrulama PASS, 2026-08-19, `T132_REPORT.md:191`).

**Dış varsayımlar (Adım 4) — dördü de ölçüldü, kırık yok:**
- *Yeni paket gerekmiyor:* tur saf silme + metin düzeltmesi + bir test dosyası. `package.json` değişmedi.
- *Backend uçları gerçekten yok:* `grep -rn "steam-accounts" backend/src --include=*.cs` → **0**; `grep -rn "recovery-queue" …` → **0**; `grep -rn "PlatformSteamBot|BotRecoveryItem|BOT_RECOVERY_UPDATED" …` (Migrations hariç) → **0**. `grep -rn "BotStatusChanged" backend/src` → **0**.
- *Doküman dayanağı hazır:* 04 §8.7 (satır 1685-1693) ve 07 §9.10 / §9.28 / §9.29 gövdesiz, yalnız kaldırma notu + numara-koruma çapası taşıyor; 04 §8.8 ve 07 §9.11 yetki matrisi **12** giriş listeliyor. T136 dokümanı beklemedi.
- *`check-i18n.mjs` davranışı:* sabit sayım taşımıyor, `en.json` bazlı locale-vs-locale küme karşılaştırması yapıyor → dört dilden simetrik silme yeşil kalır, asimetrik silme doğru şekilde kırar (M2'de ölçüldü).

**Git hook'ları kurulu:** `core.hooksPath = scripts/git-hooks`.

**Pre-commit sır tarayıcısı yanlış pozitif verdi — bypass EDİLMEDİ.** İlk commit denemesi `en.json` üzerinden bloke oldu: yazdığım İngilizce cümle (noktalama içermeyen 13 kelimelik bir dizi: *"only then does…"* → *"…pays into escrow."*) **13 ardışık küçük harfli 3-8 karakterlik kelime** içeriyordu ve `pre-commit:145`'in BIP-39 mnemonic dedektörüne (`\b([a-z]{3,8} ){11}[a-z]{3,8}\b`) takıldı. `SKINORA_ALLOW_SECRET=1` kullanılmadı; cümle iki cümleye bölünerek kalıp kırıldı (nokta + virgül eklenerek: *"…are ready to send. Only then does the payment address open, and the buyer…"*). Anlam korundu, `BYPASS_LOG`'a kayıt geçmedi. Eklenen diğer İngilizce metinler aynı regex'e karşı tarandı, isabet yok.

**Mutasyon sondajları sırasında bir öz-hata yapıldı ve onarıldı.** İlk sondaj turunda mutasyonlar `git checkout -- <dosya>` ile geri alındı; commit henüz atılmadığı için bu, **çalışmanın kendisini** HEAD'e (main'e) döndürdü — `permissionCatalog.ts` 14 anahtara, `tr.json` 1344 anahtara geri düştü. Durum `grep`/leaf sayımıyla ölçüldü, iki dosya yeniden uygulandı ve tam doğrulama (tsc + eslint + i18n + vitest) onarım sonrası **yeniden** koşuldu. Sonraki tüm sondajlar commit atıldıktan sonra `git checkout HEAD -- <dosya>` ile yapıldı. Kayıt tutuluyor çünkü aynı hata sessizce yapılsaydı tur eksik merge edilirdi.

**Kalan "custody" isabetleri kalıntı değil, kaldırma kaydı.** Süpürme sonrası 5 dosyada terim geçiyor: `dashboard/page.tsx`, `HowItWorks.tsx`, `catalog-parity.test.ts`, `permissionCatalog.parity.test.ts`, `events.ts` — beşi de bu turda **bilerek yazılmış** yorum/docstring'ler (neyin neden kaldırıldığını anlatıyorlar). Deseni repo zaten kullanıyor (`e2e/src/db.ts:67`, T137a).

**Yanlış silme riski ve KORU listesi.** Ölçüm, "steam"/"trade" kelimesi taşıyan ama P2P'de **canlı** olan yüzeyleri açıkça ayırdı ve hiçbirine dokunulmadı: `useSteamInventory` + `getSteamInventory` (kullanıcının kendi envanteri, 02 §479 "salt okunur envanter"), `steamTradeOfferUrl` (`TransactionDetailService.cs:232` — **alıcının kendi** trade URL'i, satıcıya gösterilen teslimat CTA'sı; silinmesi para akışını kırardı), `AcceptForm` trade URL alanı (T119a'da P2P **için** eklendi), Steam login/OAuth, `STEAM_OUTAGE` dondurma yüzeyi, Telegram **bot**u (`settings.linkedAccounts.telegram.*` — `bot` grep'inin tek yanlış-pozitif ailesi). `lib/api/steam.ts` bir yemdi: tek export'u canlı; ölü bot API katmanı adında ne "steam" ne "bot" geçen `lib/api/admin.ts`'in ilk 151 satırında gömülüydü — ad tabanlı tarama bunu tamamen kaçırırdı.

**Mini güvenlik kontrolü:** Secret sızıntısı yok (tarayıcı da doğruladı). Auth/authorization etkisi: `AdminGuard` **salt rol** tabanlı, hiçbir permission kontrolü yapmıyor (`AdminGuard.tsx:40`, kendi JSDoc'u "not a security boundary" diyor) — dolayısıyla kaldırılan bir FE yetki kapısı yok; iki ölü yetki anahtarı FE'de yalnız ölü katalog satırı + ölü i18n etiketi olarak yaşıyordu ve backend zaten `IsKnown` ile yetkili. Input validation etkisi yok (yeni kullanıcı girdisi eklenmedi). Yeni dış bağımlılık yok.
