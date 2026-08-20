# T134 — FE enum / StatusBadge / Timeline / i18n

**Faz:** F7 (P2P Geçişi) | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor | **Tarih:** 2026-08-21

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
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | `status`, `timeline.step`, `adminRoles.permissions.EMERGENCY_HOLD`, `adminTransactions.cancelRefund` |

**Bekçiler (yeni)**

| Dosya | Ne ölçer |
|---|---|
| `frontend/src/types/enums.parity.test.ts` | FE nüshası ↔ `backend/src/Skinora.Shared/Enums/*.cs` — değer **ve sıra** |
| `frontend/src/i18n/catalog-parity.test.ts` | i18n `status` / `timeline.step` ↔ `TransactionStatus` / `TIMELINE_STEPS` |

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
| i18n parity | ✓ | `npm run i18n:check` — 1307 × 4, identical key sets |
| Vitest | ✓ **71/71** (11 dosya) | T134 öncesi taban **35/35** (9 dosya) ölçüldü (`--exclude` ile iki yeni bekçi dosyası dışlanarak); +36 test iki bekçiden, +1 timeline'ın yeni SELLER_CONFIRMED vakasından |
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
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok** — hiçbir backend dosyasına dokunulmadı (`git diff --stat`: değişen 16 dosyanın 12'si `frontend/`, 4'ü `Docs/`).
- Config/env değişikliği: **Yok**.
- Docker değişikliği: **Yok**.
- **Yeni test dosyası `backend/` dizinini okur:** `enums.parity.test.ts` `../../../backend/src/Skinora.Shared/Enums` yolundaki `.cs` kaynaklarını `readFileSync` ile okur. CI tek repo checkout'u yaptığı için yol her zaman mevcuttur; üretilen artefakt yoktur, dolayısıyla "yeniden üret ve unut" riski de yoktur.

## Commit & PR

- Branch: `task/T134-fe-enum-status-timeline-i18n`
- Commit: bkz. aşağıdaki CI bölümü
- PR: (aşağıda güncellenecek)
- CI: (aşağıda güncellenecek)

## Known Limitations / Follow-up

- **StateActionPanel v3.0 matrisi yazılmadı — T135'e ait (karar D1).** T134 emekli üç dalı kaldırdı; `SELLER_CONFIRMED` için yeni bir dal eklemedi, dolayısıyla panel o statüde `null` döner. **Bu bir gerileme değildir:** backend T123'ten beri `SELLER_CONFIRMED` üretiyor ve panel T134 öncesinde de o statüyü tanımıyordu. Aynı sınırla, panelde **kalan** üç mesaj bloğunun metinleri (`accepted`, `paymentReceived`, `itemDelivered`) hâlâ custodial anlatıyı taşıyor — "platform trade offer hazırlıyor", "item alıcıya teslim ediliyor". Bunlar state×rol matrisinin hücre içerikleridir ve T135 onları 04 §7.3'e göre yeniden yazacaktır; T134'te yazmak T135'in kabul kriterini tüketmek olurdu.
- **`steamTradeOfferUrl` şu an hiçbir yerde çizilmiyor.** DTO alanı duruyor (07 §7.5, v3.0'da PAYMENT_RECEIVED + satıcı görünümünde alıcının kendi trade URL'i); onu çizen `SteamTradeOfferLink` yalnız emekli iki dalın içinde yaşadığı için T134 onunla birlikte kaldırdı. Yeni yeri 04 §7.3'ün "Steam'de Trade Offer Gönder" birincil butonudur → **T135**.
- **`T134-Doc06DeliveryDeferred`** (backlog 🟡): 06 §2.23 üç değer sayıyor, kod dört taşıyor. FE koda hizalandı; doküman düzeltmesi doküman turu işidir.
- **`T134-FeEnumUnionDup`** (backlog ⚪): `EmergencyHoldReleaseAction` FE'de iki ayrı string-union nüshası (`lib/api/admin.ts:527`, `lib/signalr/events.ts:37`) ve `enums.ts`'te hiç yok — bugün **sapma yok**, ama yeni bekçi bu iki nüshayı görmüyor. Kapsam sınırı bekçinin başlığında açıkça yazılı.
- **Permission kataloğu ekseni bekçisiz kaldı:** `permissionCatalog.ts` 14 anahtar taşıyor, backend `PermissionCatalog` 12 — bu sapmanın sahibi **T136**'dır (`T133a-FePermissionCatalogKeys`), T134 yalnız `EMERGENCY_HOLD` **etiketini** düzeltti.

## Notlar

**Working tree (Adım -1):** temiz — `git status --short` boş.

**Main CI startup (Adım 0):** son 3 tamamlanmış run'ın üçü de `success` — `32411997251` (Docker Publish) · `32411998138` (CI) · `32361465502` (Docker Publish), hepsi T139/T133b merge'lerine ait.

**Bağımlılık (Adım 2):** T134 → **T118** (`TransactionStateMachine` kapsam denetimi) ✓ Tamamlandı.

**Dış varsayımlar (Adım 4):** Tur tamamen frontend içidir — yeni paket, dış API, plan tier veya ortam değişkeni **yok**; `package.json` dependency listesine dokunulmadı. Tur bunun yerine dört **iç** varsayıma dayandı ve dördü de kaynağından okunarak doğrulandı: 04 §C01 (rozet etiketleri + tonları, 13 satır) · 04 §C05 (zaman çizelgesi 6 adım, T133a'da 8'den indirilmiş) · 04 §8.8 + 07 §9.11 (`EMERGENCY_HOLD` etiketi) · 06 §2.1/§2.7/§2.8/§2.13/§2.15/§2.23 (enum kataloğu ve emeklilik notları). Tek uyuşmazlık 06 §2.23 ↔ kod (`DeliveryStatus`) çıktı ve karar D2 ile ele alındı.

**Kapsam, kabul kriterlerinden geniş çıktı.** Plandaki iki T133a devri yalnız `NotificationType`'ı ve `EMERGENCY_HOLD` etiketini adlandırıyordu; ölçüm FE kataloğunun **üç ekseninin birden** saptığını ve 06 §2'de "kaldırılmıştır" yazan **üç enum'un** hâlâ FE'de tanımlı olduğunu gösterdi. Task adının kendisi ("FE enum/StatusBadge/Timeline/i18n") bu genişliği zaten kapsıyor.

**Kalıcı ders — bir kopya, kopya olduğu sürece eskir; bekçi hangi ekseni ölçtüğünü söylemelidir.** T134 öncesi FE'de üç ayrı kapı vardı ve üçü de yeşildi: TypeScript (`Record<NotificationType, …>` FE'nin **kendi içindeki** tamlığı zorlar), `check-i18n.mjs` (dört dilin **birbirine** uyduğunu zorlar) ve vitest. Sapan eksen — FE nüshası ↔ C# kaynağı ve i18n ↔ katalog — hiçbirinin kapsamında değildi, bu yüzden `ITEM_ESCROWED` T117'den beri, dört `BOT_*` T132'den beri sessizce yaşadı. Sondaj 2 bunu deneysel olarak gösterdi: dört dilden birden silinen bir anahtar mevcut kapıyı **yeşil** bırakıyor. T139'un dersi burada da geçerli oldu ve bir adım ileri taşındı: yeni bekçiler yalnız hedefi değil **kendi kapsamlarını** da denetliyor (sondaj 4) ve kapsam dışında bıraktıklarını (string-union nüshaları) adıyla kayda geçiriyor.
