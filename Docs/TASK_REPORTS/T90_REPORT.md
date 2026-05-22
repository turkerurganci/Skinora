# T90 — İşlem Detay Sayfası (S07) — Tüm State Varyantları

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-23 | **Doğrulama:** ✓ PASS (bağımsız validator)

---

## Yapılan İşler

`04 §7.3` (S07 İşlem Detay) implement edildi: platformun en karmaşık tekil ekranı — 12 transaction state × 3 caller view (seller / buyer / public unauthenticated) × suspended override matrisi tek bir client component ağacında render ediliyor. Backend kontratı T46 (`GET /transactions/:id`, 07 §7.5) zaten production'da — frontend bu DTO'nun (`TransactionDetailDto` + 12 nested type) tüm alanlarını UI'ya yansıttı. Mevcut [`transactions/[id]/page.tsx`](frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx) 9-satırlık `<div>Transaction {id}</div>` stub'ı kaldırıldı, yerine 186-satırlık veri-orchestre eden client page kondu.

**Veri akışı:**

- `lib/api/transactions.ts` — `TransactionDetailResponse` + 12 nested type (`TransactionDetailItem` / `TransactionDetailParty` / `TransactionDetailTimeout` / `TransactionDetailPayment` / `TransactionDetailSellerPayout` / `TransactionDetailRefund` / `TransactionDetailCancelInfo` / `TransactionDetailFlagInfo` / `TransactionDetailHoldInfo` / `TransactionDetailDispute` / `TransactionDetailInviteInfo` / `TransactionDetailPaymentEvent` / `TransactionDetailAvailableActions`) + `getTransactionDetail(id)` ; `AcceptTransactionRequest`/`AcceptTransactionResponse` + `acceptTransaction(id, body)` (07 §7.6, T46) ; `CancelTransactionRequest`/`CancelTransactionResponse` + `cancelTransaction(id, body)` (07 §7.7, T51). Tüm string-coded decimal alanlar (`price`/`expectedAmount`/`grossAmount`/`netAmount` vb.) string olarak korundu (scale-6/2 JSON boundary fidelity).
- `lib/api/users.ts` — `UserProfile` + `getMyProfile()` (U1, 07 §5.1) eklendi; `refundWalletAddress` alanı buyer-side CREATED'da iade adresi prefill için kullanılıyor.
- `lib/hooks/useTransactionDetail.ts` — `useQuery<TransactionDetailResponse>` (`staleTime: 5_000` — T96 SignalR ship edene kadar window-focus refetch + mutation onSuccess invalidate yolu)
- `lib/hooks/useMyProfile.ts` — `useQuery<UserProfile>` (`staleTime: 60_000`, 401 → retry 0)

**UI bileşenleri (`components/transactions/detail/`):**

12 dosya + 1 barrel + 1 helpers:

- `DetailHeader` — "İşlem #<short-id>" + `StatusBadge` (`ExtendedStatus` desteği, EMERGENCY_HOLD overlay dahil)
- `TransactionInfoPanel` — fiyat / komisyon / toplam / token / oluşturulma satırları; null-aware (`commissionAmount`/`totalAmount` public view'da yok)
- `PartiesPanel` — seller + buyer iki `UserCard variant="detailed"`; buyer null ise dashed placeholder ("buyerPending" mesajı, layout collapse olmasın diye)
- `StateActionPanel` — **scope'un kalbi**: 12 state × seller/buyer + public surface'i tek explicit switch tree'de eşleyen orchestrator. Branch'ler 04 §7.3 matrix'inin satırlarıyla 1:1; her state için seller ve buyer panel mesajı i18n key olarak çıkarıldı. Üst seviyede countdown (frozen vs running) + alt seviyede "İşlemi İptal Et" + "İtiraz Et" (K2 disabled) ikincil aksiyon barı.
- `AcceptForm` — CREATED + buyer için "Kabul Ediyorum" formu: refund address input (profile'daki adres varsa prefilled + "Değiştir" disabled K4 / yoksa boş + required) + 9 hata kodu i18n mapping (`REFUND_ADDRESS_REQUIRED`/`INVALID_WALLET_ADDRESS`/`SANCTIONS_MATCH`/`WALLET_COOLDOWN_ACTIVE`/`STEAM_ID_MISMATCH`/`ALREADY_ACCEPTED`/`INVALID_STATE_TRANSITION`/`BUYER_NOT_FOUND`/`VALIDATION_ERROR` + `generic`) + `availableActions.canAccept=false` ise disable + sebep banner
- `PaymentInfoBlock` — ITEM_ESCROWED + buyer view ödeme paneli: full address + `CopyButton` + amount/token/network/countdown (frozen state asFreezeReason ile typed) + 4 madde uyarı listesi (04 §7.3 verbatim)
- `PaymentEventBanners` — 4 type × banner variant (INCORRECT_AMOUNT/WRONG_TOKEN red warning, EXCESS_AMOUNT/LATE_PAYMENT blue info) + refund tx hash; LATE_PAYMENT yalnız CANCELLED state'te render (spec gereği)
- `SellerPayoutSummary` — COMPLETED + seller view 2 varyant: gas fee tamamen komisyondan ise 1-satır net ödeme, kısmi seller payı varsa 4-satır gas detayı (toplam/komisyondan/sizden) + 3-row wallet/txHash/sentAt; address mask'lı + CopyButton
- `CancelInfoBlock` — CANCELLED_* için iptal başlığı (cancelledBy 4 enum) + reason + itemReturned/paymentRefunded yes/no + (refund varsa) original/gas/net refund + refund address/txHash + refundedAt
- `FlagHoldBanner` — EMERGENCY_HOLD (turuncu-kırmızı, holdInfo.message verbatim — `holdInfo.reason` admin-only, 04 §7.3 "hold sebebi gösterilmez" güvenlik kuralı) veya FLAGGED (turuncu, flagInfo.message verbatim); hold flag'i precede ediyor
- `DisputeBlock` — active dispute paneli: type (3 enum × i18n) + status (3 enum × i18n) + autoCheckResult (varsa) + "TX Hash Gir" / "Admin'e İlet" butonları (K2 disabled + "comingInT92" tooltip)
- `InviteLinkBlock` — CREATED + seller view; buyer kayıtlı + bildirildi → bilgi rozet, kayıt değil → kopyalanabilir invite URL paneli
- `helpers.ts` — `isTerminalStatus` / `isCancelledStatus` / `isEmergencyHold` / `isFlagged` predicate'leri; `asFreezeReason` (TimeoutFreezeReason coerce), `maskAddress` (address/txHash kısaltma), `computeWarningSeconds` (warningThresholdPercent → seconds-until-red), `deriveCallerView`/`CallerView` type

**Page (`app/[locale]/(main)/transactions/[id]/page.tsx`):**

- 4 yükleme state: loading (Skeleton x4) → ApiError 404 (`notFound` ErrorState) → ApiError 403 (`forbidden` ErrorState) → diğer hata (`generic` ErrorState + retry)
- Suspended override → `SuspendedBanner` üstte; tüm aksiyonlar `StateActionPanel` içinde `isSuspended` ile disabled (form-level zaten backend Auth policy ile düşer ama 04 §7.3 read-only UI'yı explicit ister)
- Conditional render kuralları: `showPaymentInfo = role==='buyer' && payment && status===ITEM_ESCROWED`, `showSellerPayout = role==='seller' && sellerPayout && status===COMPLETED`, `showInviteInfo = role==='seller' && inviteInfo && status===CREATED`
- `TransactionTimeline` EMERGENCY_HOLD'ı ITEM_ESCROWED'a indirgiyor (timeline TransactionStatus enum'ı bekliyor; emergency-hold visual overlay zaten FlagHoldBanner'da var)
- `onRefetch` callback → `queryClient.invalidateQueries(['transactions','detail',id])` — accept/cancel mutation sonrası page'in fresh detail çekmesini sağlar

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (16):**

- `frontend/src/lib/hooks/useTransactionDetail.ts`
- `frontend/src/lib/hooks/useMyProfile.ts`
- `frontend/src/components/transactions/detail/helpers.ts`
- `frontend/src/components/transactions/detail/DetailHeader.tsx`
- `frontend/src/components/transactions/detail/TransactionInfoPanel.tsx`
- `frontend/src/components/transactions/detail/PartiesPanel.tsx`
- `frontend/src/components/transactions/detail/StateActionPanel.tsx`
- `frontend/src/components/transactions/detail/AcceptForm.tsx`
- `frontend/src/components/transactions/detail/PaymentInfoBlock.tsx`
- `frontend/src/components/transactions/detail/PaymentEventBanners.tsx`
- `frontend/src/components/transactions/detail/SellerPayoutSummary.tsx`
- `frontend/src/components/transactions/detail/CancelInfoBlock.tsx`
- `frontend/src/components/transactions/detail/FlagHoldBanner.tsx`
- `frontend/src/components/transactions/detail/DisputeBlock.tsx`
- `frontend/src/components/transactions/detail/InviteLinkBlock.tsx`
- `frontend/src/components/transactions/detail/index.ts`
- `Docs/TASK_REPORTS/T90_REPORT.md` (bu rapor)

**Değişen dosyalar (6):**

- `frontend/src/lib/api/transactions.ts` (+13 type + 3 function: `getTransactionDetail`, `acceptTransaction`, `cancelTransaction`)
- `frontend/src/lib/api/users.ts` (+`UserProfile` + `getMyProfile`)
- `frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx` (9 satır stub → 186 satır page)
- `frontend/src/i18n/messages/en.json` (+`transactionDetail.*` namespace, 37 yeni leaf)
- `frontend/src/i18n/messages/tr.json` (+`transactionDetail.*` namespace, 37 yeni leaf)
- `frontend/src/i18n/messages/es.json` (+`transactionDetail.*` namespace, 37 yeni leaf)
- `frontend/src/i18n/messages/zh.json` (+`transactionDetail.*` namespace, 37 yeni leaf)
- `Docs/IMPLEMENTATION_STATUS.md` (T90 satırı `⏳ Devam ediyor` + commit hash)

## Kabul Kriterleri Kontrolü

| Kriter | Durum | Kanıt |
|--------|-------|-------|
| State × role varyantları: 12 state (CREATED → COMPLETED, CANCELLED_*, FLAGGED, EMERGENCY_HOLD) | ✓ | `StateActionPanel.tsx` ve `PrimaryActionPanel` içinde her state için explicit branch + i18n key (seller + buyer iki ayrı kopya) |
| Her state'te satıcı ve alıcı görünümü farklı | ✓ | i18n: `actions.created.seller` vs `actions.created.buyer.cannotAcceptReason`, `accepted.seller` vs `accepted.buyer`, ... × 8 active state |
| Suspended session override | ✓ | `page.tsx` üstte `SuspendedBanner` + `StateActionPanel`/`AcceptForm` `isSuspended` ile aksiyon disabled |
| Ödeme edge case banner'ları: eksik / fazla / yanlış token / gecikmeli | ✓ | `PaymentEventBanners.tsx` 4 type ayrı banner; INCORRECT/WRONG_TOKEN red warning, EXCESS/LATE_PAYMENT blue info |
| Dispute aktif gösterimi | ✓ | `DisputeBlock.tsx` — type/status/autoCheckResult + 2 disabled buton (K2 T92 devir) |
| İptal bilgileri (sebep, tür, iade özeti) | ✓ | `CancelInfoBlock.tsx` — cancelledBy 4 enum başlık + reason + itemReturned/paymentRefunded + refund alt-blok (originalAmount/gasFee/netRefund/refundAddress/txHash/refundedAt) |
| GET /transactions/:id çağrısı | ✓ | `useTransactionDetail` → `getTransactionDetail` → `apiClient<TransactionDetailResponse>("/transactions/" + id)` |
| SignalR real-time güncellemeler | ⚠ K1 forward-deferred → T96 | `useTransactionDetail` `staleTime: 5_000` + window-focus refetch + accept/cancel onSuccess `invalidateQueries` ile yarı-realtime; SignalR client T96'da wire'lanır |

## Doğrulama Kontrol Listesi (04 §7.3)

- [x] 04 §7.3 tüm state × role varyantları var mı? — Evet (CREATED public/seller/buyer + 7 active state × seller/buyer + 4 CANCELLED variant + FLAGGED + EMERGENCY_HOLD + COMPLETED seller/buyer = 25 branch). Public ITEM_ESCROWED ve sonrası backend'de yok (07 §7.5 public surface CREATED-only) — bu nedenle StateActionPanel public branch'i sadece CREATED'da CTA gösterir, diğer status'larda `null` döner.
- [x] 07 §7.5 TransactionDetailResponse tüm alanları ekrana yansıtılmış mı? — 13 alt-bloğun tamamı bir component'e map edildi: item → ItemCard, seller/buyer → PartiesPanel, price/commission/total/token/createdAt → TransactionInfoPanel, timeout → CountdownTimer (StateActionPanel + PaymentInfoBlock), payment → PaymentInfoBlock, sellerPayout → SellerPayoutSummary, refund → CancelInfoBlock alt-bloğu, cancelInfo → CancelInfoBlock, flagInfo/holdInfo → FlagHoldBanner, dispute → DisputeBlock, inviteInfo → InviteLinkBlock, paymentEvents → PaymentEventBanners, availableActions → StateActionPanel conditional buttons. `escrowBotAssetId`/`deliveredBuyerAssetId` audit alanları — UI'da yer almaz (06 §8.4 audit/dispute için döndürülüyor, kullanıcıya gösterilmez), DTO type'larda field tanımlı.

## Test Sonuçları

**Test beklentisi:** Yok (11_IMPLEMENTATION_PLAN.md T90: "Test beklentisi: Yok"). Frontend henüz test runner içermez (paket.json'da `jest` / `vitest` / `playwright` yok); UI doğrulaması validator chat'inde manuel smoke testle yapılır.

**Build:** `npx next build` → ✓ Compiled successfully in 3.4s + TypeScript ✓ + 24 dynamic route prerendered + `/[locale]/transactions/[id]` route mevcut.

**TypeScript:** `npx tsc --noEmit` → 0 hata.

**Lint:** `npx eslint src --max-warnings=0` → 0 warning.

**Format:** `npx prettier --check "<glob>"` → tüm yeni dosyalar formatted (prettier write tetiklendi, sadece whitespace değişiklikleri).

**i18n parity:** 4 locale × 476 leaf parity ✓ (T89 sonrası 439 + T90 yeni 37 transactionDetail leaf, drift yok).

```bash
$ node parity-check.mjs
en 476 keys
tr 476 keys
es 476 keys
zh 476 keys
tr missing 0 extra 0
es missing 0 extra 0
zh missing 0 extra 0
```

## Altyapı Değişiklikleri

- **Migration:** Yok (sırf frontend).
- **Bağımlılık:** Yok (mevcut TanStack Query + next-intl + tailwind kullanıldı).
- **DI / config:** Yok.
- **Environment variable:** Yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok. Backend zaten authenticated/public ayrımını yapıyor (07 §7.5 public surface trimmed); frontend tüm field'ları DTO type-safety ile alıp render eder.
- **Auth/authorization:** Backend `[AllowAnonymous]` + service-level caller check (callerSteamId match). Frontend bu kararı tekrar etmez — `userRole` field'ına güvenir. Suspended hesap: backend Auth policy zaten 403 döner, frontend additional UI guard'ı sadece UX rahatlığı için.
- **Input validation:** AcceptForm `refundWalletAddress` boş ise client-side hata; backend sanctions + wallet format + cooldown check zaten yapıyor (T34/T82). CancelModal `minReasonLength=10` (default — common/CancelModal.tsx); backend reason field validation yapıyor (CancelTransactionRequest validator).
- **Yeni dış bağımlılık:** Yok.
- **XSS:** Kullanıcı-girdisi text içerikleri (`cancelInfo.reason`, `flagInfo.message`, `holdInfo.message`, `dispute.autoCheckResult`) JSX child olarak render edildi — React varsayılan escape yapar. Hiçbir yerde `dangerouslySetInnerHTML` kullanılmadı.
- **Clipboard:** CopyButton silent fail-on-permission-denied — insecure-context (HTTP) tooltip göstermez ama hata da fırlatmaz; mevcut common component davranışı (T84 review'da onaylanmıştı).

## Dış Varsayımlar (Ön-uçuş)

| Varsayım | Doğrulama | Sonuç |
|----------|-----------|-------|
| Backend `GET /transactions/:id` endpoint'i mevcut + 07 §7.5 ile uyumlu | `TransactionsController.cs` L170 `[HttpGet("{id:guid}")]` + `TransactionDetailDto.cs` 13 nested type | ✓ |
| Backend `POST /transactions/:id/accept` mevcut | `TransactionsController.cs` L201 + `AcceptTransactionRequest(string RefundWalletAddress)` | ✓ |
| Backend `POST /transactions/:id/cancel` mevcut | `TransactionLifecycleDtos.cs` L128 `CancelTransactionRequest(string Reason)` | ✓ |
| `GET /users/me` mevcut + `RefundWalletAddress` field'ı surface ediliyor | `UsersController.cs` L80 `[HttpGet("me")]` + `UserProfileService.cs` L60 `RefundWalletAddress: user.DefaultRefundAddress` | ✓ |
| `CountdownTimer` `frozen` + `frozenReason` props destekliyor | `common/CountdownTimer.tsx` L8-15 props + L77-99 frozen branch | ✓ |
| `StatusBadge` EMERGENCY_HOLD `ExtendedStatus` destekliyor | `common/StatusBadge.tsx` L5 `type ExtendedStatus = TransactionStatus | "EMERGENCY_HOLD"` + L21 STATUS_COLOR_MAP'te entry | ✓ |
| `TransactionTimeline` enum TransactionStatus alıyor (EMERGENCY_HOLD enum üyesi değil) | `common/TransactionTimeline.tsx` L7 props `status: TransactionStatus` | ✓ — page.tsx EMERGENCY_HOLD → ITEM_ESCROWED override yapıyor |
| 4 locale (tr/en/es/zh) mevcut + T89 sonrası 439 key parity | `wc -l messages/*.json` 536/536/536/536 + parity script 439 leaf | ✓ |

Dış varsayım kırığı yok.

## Commit & PR

- Branch: `task/T90-transaction-detail-page`
- Commits: `25b41e4` (yapım), `2a32bb6` (memory yansıtma), validator finalize commit (bu commit)
- PR: [#136](https://github.com/turkerurganci/Skinora/pull/136) `MERGEABLE`
- Task branch CI: run [`26313228495`](https://github.com/turkerurganci/Skinora/actions/runs/26313228495) 10/10 job ✓ (Lint+Build+Unit+Integration+Contract+Migration+Docker+CI Gate)

## Doğrulama (validator, 2026-05-23)

**Verdict:** ✓ PASS — bağımsız re-doğrulama; bulgu yok.

**Hard stop kontrolleri:**

- Adım -1 (working tree hygiene): ✓ `git status --short` boş
- Adım 0 (main CI startup): ✓ 3/3 success — run [`26309843043`](https://github.com/turkerurganci/Skinora/actions/runs/26309843043) (T89), [`26309843044`](https://github.com/turkerurganci/Skinora/actions/runs/26309843044) (T89 push), [`26253085726`](https://github.com/turkerurganci/Skinora/actions/runs/26253085726) (T88)
- Adım 0b (repo memory drift): ✓ MEMORY.md T90 satırları mevcut (`2a32bb6` commit)

**Kabul kriterleri:** 7/7 ✓ + 1 (SignalR) plan tarafından T96 forward-deferral olarak işaretli (kabul kriteri "T96 ile bağlantılı" wording'i).

**Doğrulama kontrol listesi:** 2/2 ✓ — 04 §7.3 state × role matrisi tam + 07 §7.5 TransactionDetailResponse 13 alt-blok ekrana yansıdı.

**Test/build:**

- `npx tsc --noEmit` → exit 0 (lokal Windows)
- `npx eslint src --max-warnings=0` → exit 0
- i18n parity tr/en/es/zh = 476/476/476/476, missing 0 extra 0
- Task branch CI 10/10 ✓

**Güvenlik:** Secret sızıntısı / auth / input validation / XSS — hepsi temiz; yeni dış bağımlılık yok.

**Known Limitations (devir kabul):**

- K1 SignalR → T96 (plan bağımlılığı)
- K2 Dispute butonları disabled → T92 (plan bağımlısı)
- K3 Steam trade offer URL (DTO'da yok) → T-future
- K4 İade adresi "Değiştir" linki (per-tx override field'ı yok) → T-future

## Known Limitations / Follow-up

- **K1 — SignalR real-time güncellemeler:** Detail page state geçişlerini canlı görmek için `/hubs/transactions` SignalR client'ına abone olmalıdır (CountdownSync 30sn broadcast, StatusChanged, PaymentReceived, StateExpiring event'leri). T90 şu an React Query `staleTime: 5_000` + window-focus refetch + accept/cancel mutation onSuccess `invalidateQueries` ile yarı-realtime davranır. **T96 SignalR Client Entegrasyonu** bu task'a wiring katacak (T96 zaten bu sayfanın bağımlılığı listelendi, plan'da kabul kriteri "T96 ile bağlantılı" olarak işaretli).
- **K2 — "İtiraz Et" butonu disabled:** Backend `canDispute` flag'i true gelse de buton "T92'de eklenecek" tooltip'i ile disabled. DisputeBlock içindeki "TX Hash Gir" / "Admin'e İlet" butonları aynı şekilde. **T92 Dispute UI** 3-adımlı C07 form'unu wire eder + endpoint'leri tetikler. T92 task'ı zaten T90 bağımlısı.
- **K3 — Steam trade offer URL link'i:** 04 §7.3 TRADE_OFFER_SENT_TO_SELLER + TRADE_OFFER_SENT_TO_BUYER state'lerinde "Steam'e git linki" istiyor ama backend DTO'da bu URL yok (`escrowBotAssetId` var ama trade offer ID değil, asset ID). T64-T69 Steam sidecar zaten trade offer ID'sini takip ediyor (`SteamTradeOffer` entity) ama T90 sırasında DTO genişletilmedi. **T-future** (sidecar bot session + backend DTO field eklenmesi → frontend href'i set eder). Şimdilik ilgili state'lerde sadece bilgi mesajı render edilir, link yok.
- **K4 — İade adresi "Değiştir" linki disabled:** 04 §7.3 + 02 §12.2 "yalnızca bu işlem için geçerli adres değişikliği, profil adresi etkilenmez" ister, ama backend `AcceptTransactionRequest(string RefundWalletAddress)` tek alan tutar ve buyer'ın `DefaultRefundAddress`'ini eşzamanlı güncelliyor (TransactionAcceptanceService.cs L150-153). Per-transaction override `BuyerRefundAddressOverride` field'ı için backend kontrat genişletilmeli. **T-future** — şimdilik AcceptForm input prefill + "Değiştir" disabled + `changeUnavailable` tooltip.
- **K5 — `escrowBotAssetId` / `deliveredBuyerAssetId` UI gösterimi:** DTO type'larda tanımlı ama 04 §7.3 audit/dispute amaçlı tutar — kullanıcıya gösterilmez (06 §8.4 not). Şu an page'de render edilmedi (geri çekilir, K6 olarak admin view'da T101'de surface edilebilir). **Devam — T101 Admin İşlem Detay** veya **T-future**.
- **K6 — Suspended override yalnızca buton-disabled seviyesinde:** 04 §7.3 "salt okunur görüntülenir" diyor. Frontend buton-disabled + readonly mesajı gösteriyor ama mevcut button click attempt'leri yine de mount edilmiş state'i çağırıyor; gerçek koruma backend Auth policy + JWT claim'inde (T29-T32). UI seviyesinde explicit `pointer-events-none` overlay yok (gerek de yok — buton disabled zaten click'i engelliyor; düzenlenebilir input yok).
- **K7 — Public surface (unauthenticated) yalnızca CREATED'da:** Backend zaten 07 §7.5 public shape'i CREATED dışı state'lerde trim ediyor (`userRole=null` döner ama action surface'i `RequiresLogin` flag'i ile sınırlanır). Frontend public branch sadece CREATED için CTA gösterir. Diğer durumlar için 403 path zaten devrede; bu **scope-as-designed**.
- **K8 — i18n RTL desteği:** TR/EN/ES/ZH hepsi LTR. Arapça / Farsça gibi RTL desteği T97 i18n task'ında genişletilebilir; T90 LTR-only Tailwind class kullanıyor.

## Notlar

- **Working tree:** temiz (Adım -1 ✓).
- **Main CI startup check:** 3/3 success — runs [`26309843043`](https://github.com/turkerurganci/Skinora/actions/runs/26309843043) (T89), [`26309843044`](https://github.com/turkerurganci/Skinora/actions/runs/26309843044) (T89 push), [`26253085726`](https://github.com/turkerurganci/Skinora/actions/runs/26253085726) (T88) (Adım 0 ✓).
- **Bağımlılık kontrolü:** T84 ✓ Tamamlandı, T96 ⬚ Bekliyor → **proje sahibi onayı (2026-05-22):** "T90 — SignalR forward-deferred K1 olarak işaretle (Recommended)". K1 known limitation olarak doküman edildi, T96 devir.
- **Scope kararları (2026-05-22):** Dispute butonu K2 disabled + "comingInT92" tooltip (Recommended seçildi); refund-address override K4 disabled + T-future (Recommended seçildi). Bundled-PR yasağı uyarınca T92 ve T96 ayrı task PR'larında ship edilecek.
- **04 §7.3 spec uyum notu:** Spec'teki "Conditional Buton Kuralları" tablosu 4 buton listeliyor: "Kabul Ediyorum" / "İşlemi İptal Et" / "İtiraz Et" / "Admin'e İlet". Implementation karşılıkları sırasıyla AcceptForm submit (CREATED+buyer), StateActionPanel cancel butonu (active states), StateActionPanel dispute butonu (K2 disabled), DisputeBlock escalate butonu (K2 disabled). Conditional görünürlük backend `availableActions` flag'lerine bağlanmış — server source-of-truth, client mobile authenticator / cooldown / Steam ID match / refund cooldown logic'ini re-derive etmez. Bu doğru ayrım: tek bir kaynak (`availableActions`) UI conditional'lerini güder, drift edemez.
- **Memory yansıtma:** Bu rapor commit'lendikten sonra `.claude/memory/MEMORY.md` Current Status bölümüne 1-2 satır T90 özet eklenir (Bitiş Kapısı item 8).
