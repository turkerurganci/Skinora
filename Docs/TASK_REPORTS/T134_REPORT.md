# T134 — FE enum / StatusBadge / Timeline / i18n

**Faz:** F7 (P2P Geçişi) | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-21

---

## Yapılan İşler

Frontend'in **katalog nüshaları** v3.0 (P2P) kod gerçeğine çekildi ve nüshaların bir daha sessizce eskimemesi için iki bekçi eklendi.

- **`types/enums.ts` üç eksende hizalandı:** `TransactionStatus` 14 → **12** (`SELLER_CONFIRMED` eklendi; `TRADE_OFFER_SENT_TO_SELLER`, `ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER` düştü) · `NotificationType` 28 → **26** (`PAYMENT_WINDOW_OPEN` + `DELIVERY_EXPECTED` eklendi; `ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER`, `ITEM_RETURNED`, `ADMIN_STEAM_BOT_ISSUE` düştü) · `AuditAction` 32 → **29** (`SETTLEMENT_CLEARED_ADMIN` eklendi; dört `BOT_*` düştü).
- **Üç emekli enum silindi:** `TradeOfferDirection`, `TradeOfferStatus`, `PlatformSteamBotStatus`. Üçü de 06 §2.7 / §2.8 / §2.15'te "**Bu enum kaldırılmıştır (v3.0, P2P geçişi)**" yazmasına rağmen FE'de tanımlıydı; ölçüldü, `enums.ts` dışında **hiçbir dosyada kullanılmıyorlardı**. Bölüm numaraları 06 §2 deseniyle bilinçli boş bırakıldı (§2.9+ referansları kaymasın).
- **`DeliveryStatus`'e `DEFERRED` eklendi** — kod dört değer taşıyor, FE üç taşıyordu (karar D2).
- **`StatusBadge` 04 §C01'e çekildi:** `SELLER_CONFIRMED` satırı eklendi, `PAYMENT_RECEIVED` yeşilden **sarıya**, `REFUNDED` mordan **kırmızıya** alındı; emekli üç satır düştü.
- **`TransactionTimeline` 04 §C05'in 6 adımına indirildi** (8'di): "Item Emanet" ve iki doğrulama adımı (`PAYMENT_VERIFIED`, `DELIVERY_VERIFIED`) kaldırıldı. Adım listesi artık **indeks kaynağının kendisi** — eski `switch` tablosu silindi, `TIMELINE_STEPS.indexOf(status)` kullanılıyor, yani drift edebilecek ikinci bir tablo kalmadı.
- **`notification-icons.ts`** 26 tipe hizalandı: `PAYMENT_WINDOW_OPEN` → 💰 `payment`, `DELIVERY_EXPECTED` → 🔄 `transactionUpdate`.
- **i18n dört dilde yenilendi:** `status` bloğu (13 anahtar — 12 statü + `EMERGENCY_HOLD` overlay) 04 §C01 etiketleriyle, `timeline.step` (6 anahtar) 04 §C05 adlarıyla, `adminRoles.permissions.EMERGENCY_HOLD` kod kataloğuyla (07 §9.11: "İşlemleri acil dondurma/kaldırma"). Kullanılmaz hâle gelen 7 anahtar silindi.
- **Admin iptal iade önizlemesi** 04 §8.5 madde 6 / §C06'ya çekildi: "emanetteki item satıcıya iade edilecek" kalemi kaldırıldı — platform item tutmadığı için iade edilecek tek şey paradır.
- **İki parity bekçisi eklendi** (kabul kriterinin "bekçi bu turda düşünülmelidir" maddesi, karar D4).

## Etkilenen Modüller / Dosyalar

**Katalog ve gösterim (T134'ün asıl teslimi)**

| Dosya | Değişiklik |
|---|---|
| `frontend/src/types/enums.ts` | Üç eksen hizalandı, üç emekli enum silindi, `DeliveryStatus.DEFERRED` eklendi |
| `frontend/src/components/common/StatusBadge.tsx` | 04 §C01 renk/kapsam |
| `frontend/src/components/common/TransactionTimeline.tsx` | 6 adım; `STEPS` → export `TIMELINE_STEPS`; `switch` tablosu → `indexOf` |
| `frontend/src/lib/utils/notification-icons.ts` | 26 tip; iki yeni tipin kategorisi |
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | `status`, `timeline.step`, `adminRoles.permissions.EMERGENCY_HOLD`, `adminTransactions.cancelRefund` · **doğrulama turu (N1):** `adminAuditLog.action` 26 → **29** (4 etiket eklendi, öksüz `BOT_STATUS_CHANGED` silindi) |

**Bekçiler (yeni)**

| Dosya | Ne ölçer |
|---|---|
| `frontend/src/types/enums.parity.test.ts` | FE nüshası ↔ `backend/src/Skinora.Shared/Enums/*.cs` — değer **ve sıra** |
| `frontend/src/i18n/catalog-parity.test.ts` | i18n `status` / `timeline.step` / **`adminAuditLog.action`** ↔ `TransactionStatus` / `TIMELINE_STEPS` / **`AuditAction`** (üçüncü eksen doğrulama turunda eklendi — N1) |

**Zorunlu yan etki (davranış T135/T136'ya ait)**

| Dosya | Değişiklik |
|---|---|
| `frontend/src/components/transactions/detail/StateActionPanel.tsx` | Emekli üç state dalı + yalnız onlarda çizilen `SteamTradeOfferLink` kaldırıldı (karar D1) |
| `frontend/src/components/admin/TransactionDetailView.tsx` | `ITEM_ESCROWED_STATES` silindi; iade önizlemesi para-only |
| `frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx` | `showPaymentInfo` ve EMERGENCY_HOLD timeline fallback'i `SELLER_CONFIRMED`'a |
| `frontend/src/app/[locale]/dev/components/page.tsx` | `ALL_STATUSES` → `Object.values(TransactionStatus)` (REFUNDED eksikti) |
| `frontend/src/components/transactions/detail/PaymentInfoBlock.tsx` | Yorumdaki state adı |

**Doküman**

| Dosya | Değişiklik |
|---|---|
| `Docs/11_IMPLEMENTATION_PLAN.md` | §F7 P7 T134 YAPIM TURU bloğu (D1–D4) + başlık girişi |
| `Docs/DEFERRED_BACKLOG.md` | 2 satır ✅ kapandı, 2 satır 🆕 açıldı |
| `Docs/IMPLEMENTATION_STATUS.md` | T134 ⏳ + T139 post-merge teyidi |
| `Docs/TASK_REPORTS/T139_REPORT.md` | T139 merge + post-merge CI kanıtı (bu dalda kayda geçti) |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `npm run i18n:check` yeşil, 4 dil parity | ✓ | `i18n parity OK — 4 locales, **1307 keys each**, identical key sets`. Anahtar deltası main'e karşı ölçüldü: **−14 silinen / +2 eklenen** (`status.SELLER_CONFIRMED`, `timeline.step.SELLER_CONFIRMED`), beklenenle birebir. 15 advisory uyarı **T134 öncesinden** gelir (`Gas fee` / `Mobile Authenticator`, WP18 kararı gereği bloke etmez) ve hiçbiri bu turda dokunulan anahtarlara ait değildir |
| 2 | FE `NotificationType` kod enum'u ile birebir (26 değer) | ✓ | `enums.parity.test.ts > NotificationType matches the C# member list, in order` geçiyor. Ölçüm: FE 26 / C# 26, fark yok |
| 3 | İkonlar/etiketler yeni iki tipi kapsıyor | ✓ | `notification-icons.ts` `CATEGORY_BY_TYPE` `Record<NotificationType, …>` olduğu için eksik tip **derleme hatasıdır**; `tsc --noEmit` exit 0. `PAYMENT_WINDOW_OPEN` → `payment` (💰: açılan şey ödeme penceresidir), `DELIVERY_EXPECTED` → `transactionUpdate` (🔄: emekli `TRADE_OFFER_SENT_TO_BUYER`'ın ikonu korundu) |
| 4 | `adminRoles.permissions.EMERGENCY_HOLD` dört dilde kod kataloğuyla hizalı | ✓ | TR artık 07 §9.11 / 04 §8.8 ile **birebir**: "İşlemleri acil dondurma/kaldırma". EN/ES/ZH aynı anlama (nesne = işlemler) çekildi: `Apply/lift emergency hold on transactions` · `Aplicar/retirar retención de emergencia en transacciones` · `对交易应用/解除紧急冻结` |
| 5 | (Kriterin notu) "parity'yi zorlayan bir bekçi yok — bekçi bu turda düşünülmelidir" | ✓ | İki bekçi eklendi, **dördü de sondajla ayırt edici bulundu** (aşağıdaki tablo). Kapsam sınırı bekçinin kendi başlığında yazılı |
| 6 | Task adının diğer üç ekseni (enum / StatusBadge / Timeline) | ✓ | `TransactionStatus` 12/12 + `AuditAction` 29/29 parity testinde; StatusBadge 04 §C01'in 13 satırını `Record<ExtendedStatus, …>` ile kapsıyor; Timeline 6 adım — `renders exactly the six v3.0 steps (04 §C05)` testi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| TypeScript | ✓ exit 0 | `npx tsc --noEmit` |
| Lint | ✓ exit 0 | `npm run lint` (eslint) |
| i18n parity | ✓ | `npm run i18n:check` — **1310 × 4**, identical key sets (yapım turu 1307; doğrulama turunun N1 düzeltmesi +4 anahtar ekledi, 1 öksüz anahtarı sildi) |
| Vitest | ✓ **75/75** (11 dosya) — yapım turu 71/71, doğrulama turunun N1 bekçi ekseni +4 | T134 öncesi taban **35/35** (9 dosya) ölçüldü (`--exclude` ile iki yeni bekçi dosyası dışlanarak); +36 test iki bekçiden, +1 timeline'ın yeni SELLER_CONFIRMED vakasından |
| Build | ✓ | `npm run build` — `Compiled successfully`, TypeScript ✓, 3/3 static page. Tek uyarı `middleware → proxy` deprecation'ı, T134 öncesinden gelir |
| Prettier | ✓ (içerik) | `npx prettier <dosya> \| diff` → CRLF normalize edildiğinde **birebir aynı**. Lokal `--check` uyarısı yalnız satır sonu artifaktıdır (repo `core.autocrlf=true`); yetkili ölçüm CI'ın LF checkout'udur |

### Bekçilerin ayırt ediciliği — dört sondaj

Her sondajda kod/katalog **kasten bozuldu**, bekçinin düşüp düşmediği ölçüldü, sonra geri alındı.

| # | Sondaj | Beklenen | Ölçülen |
|---|---|---|---|
| 1 | `enums.ts`'e emekli `ITEM_ESCROWED` geri eklendi | enum parity düşer | ✓ **1 failed / 22 passed** — yalnız `TransactionStatus matches the C# member list, in order`. Hedefli |
| 2 | `status.SELLER_CONFIRMED` **dört dilden birden** silindi | `check-i18n.mjs` yeşil kalır, yeni bekçi düşer | ✓ `i18n parity OK — 1306 keys each, **identical key sets**` **ve** catalog-parity **4 failed / 9 passed** (en/tr/es/zh). İki bekçinin dik eksenler olduğunun doğrudan kanıtı |
| 3 | `TIMELINE_STEPS`'e 7. adım eklendi | timeline eksenleri düşer | ✓ **9 failed** — dört dilin timeline testi + `guard checks itself` + timeline bileşen testleri |
| 4 | Bekçinin **C# parser regex'i** bozuldu | bekçi sessizce geçmez | ✓ **2 failed**: `parsed both sides — the guard is actually comparing something` → `expected 0 to be greater than or equal to 20`. `it.each` boş listeyle 21 karşılaştırmayı hiç üretmedi; self-check onu yakaladı |

Sondaj 2 aynı zamanda **T134'ün var oluş sebebini** ölçüyor: dört dil birden yanlışken mevcut CI kapısı yeşil kalıyordu — `check-i18n.mjs` dillerin **birbirine** uyduğunu kanıtlar, kataloğa uyduğunu değil.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (2026-08-21, ayrı chat, yapım raporu görülmeden) |
| Bulgu sayısı | **1 bloke etmeyen (N1)** + 2 gözlem · bloke edici **0** |
| Düzeltme gerekli mi | N1 proje sahibi kararıyla **aynı dalda kapatıldı** |

**Giriş kapıları:** working tree boş · main son 3 run `success` (`32411998138` CI · `32411997251` + `32361465502` Docker Publish) · repo memory T134 satırı mevcut.

**Altı kabul kriterinin altısı da bağımsız olarak yeniden üretildi.** Validator, yapım turunun ölçümlerine güvenmek yerine kendi araçlarını yazdı: `enums.parity.test.ts`'i hiç kullanmadan **bağımsız bir parser** ile 21 aynalı enum'un tamamını C# kaynağına karşı değer+sıra olarak karşılaştırdı → **fark yok**; taban `origin/main`'de `TransactionStatus` 14 / `NotificationType` 28 / `AuditAction` 32 / `DeliveryStatus` 3 + üç emekli enum ölçülerek sapmanın gerçekliği ayrıca doğrulandı. `EMERGENCY_HOLD` etiketi `PermissionCatalog.cs:43` ↔ `tr.json` **birebir**. StatusBadge'in 13 satırı 04 §C01 tonlarıyla, Timeline'ın 6 adımı 04 §C05 sırasıyla tek tek karşılaştırıldı.

**Validator'ın kendi ölçümleri:** `tsc --noEmit` 0 · `eslint` 0 · `i18n:check` 1310×4 identical · `vitest` **75/75** · `next build` exit 0 · prettier (CRLF normalize edilince) birebir. Dal HEAD `6506463` CI run [`32420549055`](https://github.com/turkerurganci/Skinora/actions/runs/32420549055) **`success`**, `CI Gate` yeşil — rapor yalnız `cbad04d` run'ını gösteriyordu, HEAD'in kendi run'ı doğrulama sırasında tamamlandı. Sekiz advisory E2E leg'i **main tabanıyla birebir** 8/8 kırmızı; ayrıca `e2e/` FE enum kataloğunu hiç import etmiyor, yani T134 bu sinyale yapısal olarak dokunamaz.

**Bekçi ayırt ediciliği — validator'ın KENDİ sondajları.** Yapım turunun dört sondajı tekrarlanmadı; beş bağımsız mutasyon koşuldu (guard'ın kanıtı başkasının provası değil kendi provandır):

| # | Sondaj | Ölçülen |
|---|---|---|
| A | **C# kaynağına** değer eklendi (FE'ye değil) | ✓ düştü — bekçi üretilmiş bir artefaktı değil gerçek kaynağı okuyor |
| B | Yalnız **sıra** değişimi (küme aynı): `FLAGGED`↔`REFUNDED` | ✓ düştü; catalog-parity doğru şekilde yeşil kaldı (sıralı karşılaştırma yapmaz) |
| C | **Dört dile birden ÖKSÜZ anahtar** (`status.ITEM_ESCROWED`) | ✓ `check-i18n.mjs` **yeşil** (1308×4 identical), catalog-parity **dört dilde de** düştü. Yapım turu sondaj 2 *silme* yönünü ölçmüştü; bu *ekleme* yönü — bekçi **çift yönlü** |
| D | C# parser'ı bozuldu (girinti 4→6) | ✓ vakumda sessizce geçmedi: `TransactionStatus parsed to no members` |
| E | Backend'de olmayan FE enum'u geri kondu | ✓ düştü (`declares no enum the backend does not have`) |

### N1 (bloke etmez, proje sahibi kararıyla aynı dalda kapatıldı) — `adminAuditLog.action` kataloğu T134'ün kendi ayak izinde saptı

Tur `AuditAction`'ı 32 → 29 çekti ama **aynı enum'la anahtarlanan i18n etiket kataloğuna hiç dokunmadı** — turun i18n diff'i `status`, `timeline.step`, `adminRoles.permissions` ve `cancelRefund` içeriyor, `adminAuditLog.action` yok. Ölçüm dört dilde de aynı çıktı: **enum 29 / katalog 26**; eksik `SETTLEMENT_CLEARED_ADMIN` · `MAINTENANCE_MODE_CHANGED` · `TIMEOUT_AUTO_EXTENDED` · `PLATFORM_OUTAGE_DETECTED`, öksüz `BOT_STATUS_CHANGED`.

**İkisi doğrudan T134'ün ürünü:** `SETTLEMENT_CLEARED_ADMIN`'i enum'a bu tur ekledi (etiketsiz), `BOT_STATUS_CHANGED`'i bu tur sildi (etiketini bırakarak). Diğer üçü WP7/WP16 borcu. **Dördünün de canlı üretim yazıcısı var** — `AdminTransactionService.cs:808` (AD32), `RestartRecoveryService.cs:155`, `PlatformHealthProbeJob`, `AdminMaintenanceService.cs:235` — yani S21 audit log'unda admin bugün ham `SETTLEMENT_CLEARED_ADMIN` görüyor.

**Neden hiçbir kapı görmedi:** katman kırılmıyor, **derece kaybediyor** — `AuditLogTable.tsx:107` `tAction.has(row.action) ? tAction(row.action) : row.action` ile ham enum adına düşüyor. Ve `check-i18n.mjs` yeşil kalıyor çünkü dört dil **aynı şekilde** yanlış. Bu, T134'ün var oluş sebebi olan mekanizmanın bir katalog ötedeki tekrarıdır ve T118'in `ResxNotificationTemplateResolver` dersinin ikizidir. D4'ün "kapsam sınırı bekçinin kendi başlığında yazılıdır" iddiası bu eksen için geçerli değildi: `catalog-parity.test.ts` başlığı yalnız string-union istisnasını adlandırıyor, bu kataloğu hiç anmıyordu.

**Kapanış (aynı dal):** dört etiket dört dile de yazıldı ve öksüz silindi — katalog artık dört dilde de **29/29 ve enum SIRASINDA**. `catalog-parity.test.ts`'e **üçüncü eksen** eklendi (`adminAuditLog.action` ↔ `AuditAction`, self-check `toHaveLength(29)`, boş-etiket kontrolü dahil) ve **kapsam sınırı başlığa yazıldı**: enum-anahtarlı olmayan bloklar (`adminAuditLog.category`, `adminFlags.signalType`, `adminTransactions.statusGroup`, `adminSteamAccounts.*` — API projeksiyonları/sözlükleri) ve **T136'ya ait** `adminRoles.permissions` adıyla kapsam dışı sayıldı. Yeni eksenin ayırt ediciliği **iki yönde de** kanıtlandı: `SETTLEMENT_CLEARED_ADMIN` dört dilden birden silinince `check-i18n.mjs` yeşil (1309×4) / bekçi dört dilde düştü; öksüz `BOT_STATUS_CHANGED` dört dile geri konunca yine yeşil (1311×4) / bekçi yine dört dilde düştü.

**KALICI DERS:** bir turun enum'a **eklediği ve enum'dan sildiği** değerler, o enum'la anahtarlanan **her nüshada** aynı turda kapatılmalıdır — ve bir bekçinin kapsam beyanı, kapsadıklarını değil **kapsamadıklarını** saymalıdır. T134 bir kopya ekseninin drift ettiğini kanıtlarken ikinci bir kopya eksenini kendi eliyle drift ettirdi.

### Bloke etmeyen gözlemler (kapsam dışı, bilgi olarak kayda geçti)

- **G1 — timeline sınıflandırması geleceğe karşı zorlanmıyor.** Sondajla ölçüldü: C# + FE + dört dile yeni bir `TransactionStatus` eklendiğinde yalnız `toHaveLength(13)` self-check'i düşer ve teşhis timeline'ı **işaret etmez**; `TIMELINE_STEPS`/`OFF_TIMELINE` bölümlemesine girmeyen statü `indexOf → -1 → 0` ile sessizce "1. adım, sürüyor" olarak çizilir — `REFUNDED`'ın kod yorumunda anlatılan hatanın aynısı. **Bugün kırık değil:** bölümleme tam (6 + 6 = 12) ve validator bunu doğruladı. Kapanışı bir bölümleme-tamlığı assertion'ıdır.
- **G2 — EMERGENCY_HOLD timeline'ı sabit statü basıyor.** `page.tsx:137` hold'da `holdInfo.previousStatus`u (FE tipinde **mevcut**: `lib/api/transactions.ts:260`) okumak yerine sabit `SELLER_CONFIRMED` veriyor, yani `PAYMENT_RECEIVED`'da dondurulmuş bir işlem timeline'da bir adım geride görünüyor. **T134 kaynaklı değil** — main'deki sabit `ITEM_ESCROWED`'in birebir taşınmışı, göreli konum değişmedi.

## Altyapı Değişiklikleri

- Migration: **Yok** — hiçbir backend dosyasına dokunulmadı (`git diff --stat`: değişen 16 dosyanın 12'si `frontend/`, 4'ü `Docs/`).
- Config/env değişikliği: **Yok**.
- Docker değişikliği: **Yok**.
- **Yeni test dosyası `backend/` dizinini okur:** `enums.parity.test.ts` `../../../backend/src/Skinora.Shared/Enums` yolundaki `.cs` kaynaklarını `readFileSync` ile okur. CI tek repo checkout'u yaptığı için yol her zaman mevcuttur; üretilen artefakt yoktur, dolayısıyla "yeniden üret ve unut" riski de yoktur.

## Commit & PR

- Branch: `task/T134-fe-enum-status-timeline-i18n` (origin/main `28a910c` üzerinden kesildi)
- Commit: `cbad04d` — T134: FE enum/StatusBadge/Timeline/i18n v3.0 hizalaması + iki parity bekçisi
- Ek commit: `20e7bb2` — T139 merge teyidi raporlara işlendi (bkz. Notlar)
- PR: [#252](https://github.com/turkerurganci/Skinora/pull/252)
- CI: ✓ **PASS** — dal HEAD `cbad04d`, run [`32418535947`](https://github.com/turkerurganci/Skinora/actions/runs/32418535947) `success`, **`CI Gate` yeşil**

**Bloke edici jobların tamamı yeşil:** `1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (frontend)` · `CI Gate` (+ `Detect changed paths`). `0. Guard (direct push)` skipped (beklenen). Docker build'in yalnız **frontend** kolunun koşması `paths-filter`ın doğru davranışıdır — tur `backend/` ve `sidecar-*/` altında tek satır değiştirmedi.

**Sekiz advisory E2E leg'i kırmızı — T134 kaynaklı DEĞİL, ve sayı ölçülerek doğrulandı.** Legler T117'nin custody emekliliğinden beri kırmızıdır (`e2e/src/db.ts` harness'ı emekli tablolara bakıyor), `continue-on-error` + `ci-gate.needs` dışındadır ve yeniden yazımlarının sahibi **T138**'dir. T137'nin kalıcı dersi gereği leg statüsü değil **geçen test kümesi** sayıldı:

| Leg | Geçen | Düşen |
|---|---|---|
| E2E happy-path | 0 | 1 |
| T108 cancellation | 0 | 4 |
| T109 timeout | 1 | 3 |
| T110 payment edge cases | 0 | 6 |
| T111 fraud-flags | 3 | 1 |
| T112 emergency-hold | 0 | 3 |
| T113 admin-flows | 6 | 1 |
| T114 downtime | 0 | 3 |
| **Toplam** | **10** | **22** |

**10/32** — T139 tur 3'ün ölçümüyle (run `32407918580`: geçen 1+3+6 = 10, düşen 22) **birebir aynı**. T134 advisory ağına dokunmadı, regresyon yok.

## Known Limitations / Follow-up

- **StateActionPanel v3.0 matrisi yazılmadı — T135'e ait (karar D1).** T134 emekli üç dalı kaldırdı; `SELLER_CONFIRMED` için yeni bir dal eklemedi, dolayısıyla panel o statüde `null` döner. **Bu bir gerileme değildir:** backend T123'ten beri `SELLER_CONFIRMED` üretiyor ve panel T134 öncesinde de o statüyü tanımıyordu. Aynı sınırla, panelde **kalan** üç mesaj bloğunun metinleri (`accepted`, `paymentReceived`, `itemDelivered`) hâlâ custodial anlatıyı taşıyor — "platform trade offer hazırlıyor", "item alıcıya teslim ediliyor". Bunlar state×rol matrisinin hücre içerikleridir ve T135 onları 04 §7.3'e göre yeniden yazacaktır; T134'te yazmak T135'in kabul kriterini tüketmek olurdu.
- **`steamTradeOfferUrl` şu an hiçbir yerde çizilmiyor.** DTO alanı duruyor (07 §7.5, v3.0'da PAYMENT_RECEIVED + satıcı görünümünde alıcının kendi trade URL'i); onu çizen `SteamTradeOfferLink` yalnız emekli iki dalın içinde yaşadığı için T134 onunla birlikte kaldırdı. Yeni yeri 04 §7.3'ün "Steam'de Trade Offer Gönder" birincil butonudur → **T135**.
- **`T134-Doc06DeliveryDeferred`** (backlog 🟡): 06 §2.23 üç değer sayıyor, kod dört taşıyor. FE koda hizalandı; doküman düzeltmesi doküman turu işidir.
- **`T134-FeEnumUnionDup`** (backlog ⚪): `EmergencyHoldReleaseAction` FE'de iki ayrı string-union nüshası (`lib/api/admin.ts:527`, `lib/signalr/events.ts:37`) ve `enums.ts`'te hiç yok — bugün **sapma yok**, ama yeni bekçi bu iki nüshayı görmüyor. Kapsam sınırı bekçinin başlığında açıkça yazılı.
- **`adminAuditLog.action` ekseni doğrulama turunda kapatıldı (N1).** Yapım turu `AuditAction`ı 32 → 29 çekerken bu kataloğu 26'da bıraktı; dört etiket dört dile yazıldı, öksüz silindi ve bekçiye üçüncü eksen olarak eklendi. Ayrıntı: §Doğrulama.
- **Permission kataloğu ekseni bekçisiz kaldı:** `permissionCatalog.ts` 14 anahtar taşıyor, backend `PermissionCatalog` 12 — bu sapmanın sahibi **T136**'dır (`T133a-FePermissionCatalogKeys`), T134 yalnız `EMERGENCY_HOLD` **etiketini** düzeltti.

## Notlar

**Working tree (Adım -1):** temiz — `git status --short` boş.

**Main CI startup (Adım 0):** son 3 tamamlanmış run'ın üçü de `success` — `32411997251` (Docker Publish) · `32411998138` (CI) · `32361465502` (Docker Publish), hepsi T139/T133b merge'lerine ait.

**Bağımlılık (Adım 2):** T134 → **T118** (`TransactionStateMachine` kapsam denetimi) ✓ Tamamlandı.

**Dış varsayımlar (Adım 4):** Tur tamamen frontend içidir — yeni paket, dış API, plan tier veya ortam değişkeni **yok**; `package.json` dependency listesine dokunulmadı. Tur bunun yerine dört **iç** varsayıma dayandı ve dördü de kaynağından okunarak doğrulandı: 04 §C01 (rozet etiketleri + tonları, 13 satır) · 04 §C05 (zaman çizelgesi 6 adım, T133a'da 8'den indirilmiş) · 04 §8.8 + 07 §9.11 (`EMERGENCY_HOLD` etiketi) · 06 §2.1/§2.7/§2.8/§2.13/§2.15/§2.23 (enum kataloğu ve emeklilik notları). Tek uyuşmazlık 06 §2.23 ↔ kod (`DeliveryStatus`) çıktı ve karar D2 ile ele alındı.

**Kapsam, kabul kriterlerinden geniş çıktı.** Plandaki iki T133a devri yalnız `NotificationType`'ı ve `EMERGENCY_HOLD` etiketini adlandırıyordu; ölçüm FE kataloğunun **üç ekseninin birden** saptığını ve 06 §2'de "kaldırılmıştır" yazan **üç enum'un** hâlâ FE'de tanımlı olduğunu gösterdi. Task adının kendisi ("FE enum/StatusBadge/Timeline/i18n") bu genişliği zaten kapsıyor.

**Kalıcı ders — bir kopya, kopya olduğu sürece eskir; bekçi hangi ekseni ölçtüğünü söylemelidir.** T134 öncesi FE'de üç ayrı kapı vardı ve üçü de yeşildi: TypeScript (`Record<NotificationType, …>` FE'nin **kendi içindeki** tamlığı zorlar), `check-i18n.mjs` (dört dilin **birbirine** uyduğunu zorlar) ve vitest. Sapan eksen — FE nüshası ↔ C# kaynağı ve i18n ↔ katalog — hiçbirinin kapsamında değildi, bu yüzden `ITEM_ESCROWED` T117'den beri, dört `BOT_*` T132'den beri sessizce yaşadı. Sondaj 2 bunu deneysel olarak gösterdi: dört dilden birden silinen bir anahtar mevcut kapıyı **yeşil** bırakıyor. T139'un dersi burada da geçerli oldu ve bir adım ileri taşındı: yeni bekçiler yalnız hedefi değil **kendi kapsamlarını** da denetliyor (sondaj 4) ve kapsam dışında bıraktıklarını (string-union nüshaları) adıyla kayda geçiriyor.
