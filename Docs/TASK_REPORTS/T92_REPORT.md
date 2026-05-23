# T92 — Dispute UI

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-23

---

## Yapılan İşler

`04 §5 C07` (Dispute Form) ve `04 §7.3` (S07 Aktif Dispute paneli) ürün karşıdaki son boşluğu kapatıldı: T90 PR #136 sırasında K2 olarak forward-deferred edilen "İtiraz Et" / "TX Hash Gir" / "Admin'e İlet" butonları artık canlı API çağrıları yapar. Backend tarafı zaten T58 PR'ında (DisputesController + DisputeService) production'da idi — T92 yalnız frontend wiring + form genişletmesi.

**3-step wizard (mevcut DisputeForm extend edildi):**

- **Step 1 — Type:** mevcut radio seçim (PAYMENT / DELIVERY / WRONG_ITEM) — değişiklik yok
- **Step 2 — Checking + Result:** mevcut auto-check loop. T92'de eklenen: API'nin döndürdüğü `autoCheckResult.message` artık verbatim ekrana yansır (önceden sabit i18n "auto-resolution applied" / "no automatic resolution available" gösteriliyordu). `result.resolved/unresolved/existing.title` küçük başlık + alt body = API mesajı yapısı.
- **Step 2.5 (yeni) — TX Hash retry:** `canSubmitTxHash=true` olduğunda result step'inde "Submit TX hash" butonu görünür → tek-input form'a geçer (`txhash` step) → `POST /transactions/:id/disputes/:disputeId/submit-txhash` → re-check sonucu result step'e döndürülür (resolved=true ise her iki action flag kapanır)
- **Step 3 — Escalation:** mevcut detail textarea (min 10 char). T92'de eklenen: hata code'ları `disputeForm.errors.*` namespace'iyle gösterilir; per-error message lokalize.

**Existing-dispute resume mode (DisputeBlock entry point):**

`DisputeForm` artık `existingDispute?: ExistingDisputeContext` prop'u alır. Set edilirse step 1 atlanır, doğrudan step 2 "Open dispute" başlığıyla açılır — `autoCheckMessage` snapshot'ı + `canSubmitTxHash` / `canEscalate` flag'leri parent'tan gelir, kullanıcı aynı UX akışıyla TX hash retry veya escalate aksiyonlarını çalıştırır.

**Modal pattern:**

`transactions/detail/DisputeModal.tsx` (yeni) — native `<dialog>` element + ESC handler + focus trapping (browser native), `CancelModal` paterni birebir. `useQueryClient.invalidateQueries(['transactions','detail', id])` her başarılı mutation sonrası çağrılır → DisputeBlock + availableActions flag'leri server-source-of-truth ile yenilenir.

**Wiring:**

- `StateActionPanel`: "İtiraz Et" butonu K2 disabled tooltip'i silindi, `disputeOpen` state + onClick → DisputeModal (new-dispute mode). `disputeButtonEnabled = canDispute && !isSuspended`. Modal close → onRefetch tetiklenir.
- `DisputeBlock`: K2 disabled butonları silindi; `transactionId` + `isSuspended` props eklendi; "TX Hash Gir" / "Admin'e İlet" butonları DisputeModal'ı `existingDispute` mode'da açar.
- `transactions/[id]/page.tsx`: DisputeBlock çağrısı genişletildi (transactionId + isSuspended), K2 yorumu güncellendi.

**API client (`lib/api/disputes.ts` — yeni):**

3 endpoint × DTO type + function pair:

- `openDispute(transactionId, { type })` → `OpenDisputeResponse { id, type, status, autoCheckResult: { resolved, message, canSubmitTxHash, canEscalate }, createdAt }`
- `submitDisputeTxHash(transactionId, disputeId, { txHash })` → `SubmitTxHashResponse { checkResult: { resolved, message } }`
- `escalateDispute(transactionId, disputeId, { detail })` → `EscalateDisputeResponse { status, escalatedAt, message }`

`apiClient<T>` zaten `ApiResponse<T>` envelope'ını unwrap eder + 4xx'i `ApiError` olarak fırlatır; per-endpoint try/catch DisputeForm'da `extractErrorKey()` ile `error.code` → `disputeForm.errors.{code}` i18n key'ine map'lenir.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (2):**

- `frontend/src/lib/api/disputes.ts` (T8/T9/T10 client + 7 type)
- `frontend/src/components/transactions/detail/DisputeModal.tsx` (`<dialog>` wrapper, DisputeForm orchestrator)

**Değişen dosyalar (9):**

- `frontend/src/components/common/DisputeForm.tsx` (API extend: prop signature → `onOpen/onSubmitTxHash/onEscalate/existingDispute`, +2 step `txhash/txhashChecking`, API verbatim message, error code i18n map)
- `frontend/src/components/common/index.ts` (export type rename + 2 new types)
- `frontend/src/components/transactions/detail/DisputeBlock.tsx` (wire active buttons, props +`transactionId`+`isSuspended`)
- `frontend/src/components/transactions/detail/StateActionPanel.tsx` (wire dispute button, K2 yorumu kaldırıldı, DisputeModal render)
- `frontend/src/components/transactions/detail/index.ts` (+DisputeModal export)
- `frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx` (DisputeBlock prop'ları + K2 yorumu güncelle)
- `frontend/src/app/[locale]/dev/components/page.tsx` (C07 demo'yu yeni API'ye uyarla)
- `frontend/src/i18n/messages/{en,tr,es,zh}.json` (4 locale: +14 net key — `stepLabel.txhash/txhashChecking`, `result.existing.title`, `submitTxHash/submitTxHashConfirm`, `txHashLabel/Placeholder/Hint`, `detailHint`, `errors.*` (10 alt-key), −5 key: `result.{resolved,unresolved}.description`, `transactionDetail.dispute.comingInT92`, `transactionDetail.actions.{disputeComingInT92,comingSoon}`)

**Yeni dosyalar (doc, 1):**

- `Docs/TASK_REPORTS/T92_REPORT.md` (bu rapor)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | C07 Dispute Form: 3 adımlı (tür seçimi → otomatik kontrol → eskalasyon) | ✓ | `DisputeForm.tsx` step machine: `type` → `checking/result` → `escalation/done` (+ `txhash/txhashChecking` PAYMENT sub-step); existingDispute mode aynı state machine'in step 2'sinden başlar |
| 2 | Dispute tür seçimi: ödeme, teslim, yanlış item | ✓ | `DisputeForm.tsx` L210-238: 3 radio (PAYMENT/DELIVERY/WRONG_ITEM) + `t("type.${option}.title/description")` |
| 3 | Otomatik kontrol sonucu gösterimi | ✓ | `DisputeForm.tsx` L255-301 result step: API `autoCheckResult.message` verbatim render edilir (resolved=green / unresolved=yellow box); existingDispute mode `autoCheckMessage` snapshot'ı kullanır |
| 4 | TX hash girme imkanı (ödeme dispute) | ✓ | `DisputeForm.tsx` L266-274 "Submit TX hash" butonu (`canSubmitTxHash=true` ve resolved=false ise görünür) → L303-340 `txhash` step (input + Verify) → `submitDisputeTxHash` API call |
| 5 | Admin'e iletme butonu + detay textarea | ✓ | `DisputeForm.tsx` L275-284 "Escalate to Admin" butonu (`canEscalate=true` ise) → L342-380 escalation step (textarea min 10 char + Submit) → `escalateDispute` API call |
| 6 | Dispute durumu gösterimi | ✓ | `DisputeBlock.tsx` zaten T90'da render eder (type/status/autoCheckResult/createdAt); T92 ek olarak action butonları wire eder + onClose → React Query invalidate ile status fresh data fetch eder |

## Doğrulama Kontrol Listesi

- [x] Tüm dispute UI öğeleri görünür ve aktif mi? — Evet: 3-step wizard çalışıyor (StateActionPanel "İtiraz Et" → DisputeModal new-dispute mode, DisputeBlock action butonları → existingDispute mode); auto-check / TX hash retry / escalate üç ayrı API çağrısı bağlı; her başarılı mutation queryClient.invalidateQueries tetikler.

## Test Sonuçları

**Test beklentisi:** Yok (11_IMPLEMENTATION_PLAN.md T92: "Test beklentisi: Yok"). Frontend henüz test runner içermez (paket.json'da `jest`/`vitest`/`playwright` yok); UI doğrulaması validator chat'inde manuel smoke testle yapılır.

**Build:** `npx next build` → ✓ Compiled successfully in 6.9s, 24 dynamic route + TypeScript ✓.

**TypeScript:** `npx tsc --noEmit` → exit 0.

**Lint:** `npx eslint src --max-warnings=0` → exit 0.

**Format:** `npx prettier --write` task'ta değişen 13 dosyada idempotent (5 dosya unchanged, 8 dosyada minor whitespace düzeltme).

**i18n parity:** 4 locale × 490 leaf parity ✓ (T90 sonrası 476 → T92 +14 net = 490).

```bash
$ node parity-check.mjs
en 490 keys
tr 490 keys
es 490 keys
zh 490 keys
tr missing 0 extra 0
es missing 0 extra 0
zh missing 0 extra 0
```

## Altyapı Değişiklikleri

- **Migration:** Yok (sırf frontend).
- **Bağımlılık:** Yok (mevcut TanStack Query + next-intl + tailwind + react kullanıldı).
- **DI / config:** Yok.
- **Environment variable:** Yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok. Tüm API request'ler `apiClient` üzerinden gider, Bearer token localStorage'dan alınır.
- **Auth/authorization:** Backend dispute endpoint'leri `[Authorize(Authenticated)]` + service-level "is buyer" check (T58); frontend buton görünürlüğü `availableActions.canDispute` / `dispute.canSubmitTxHash` / `dispute.canEscalate` server-source-of-truth flag'lerine dayanır, client re-derive etmez.
- **Input validation:** `txHash` boşsa submit disabled; `detail` min 10 char client-side + backend `EscalateDisputeRequest` validator; backend sanctions / 4xx hata kodları (DUPLICATE_DISPUTE, INVALID_STATE_TRANSITION, vb.) frontend `disputeForm.errors.*` namespace'ine map'lenir.
- **Yeni dış bağımlılık:** Yok.
- **XSS:** API mesajları (`autoCheckResult.message`, `checkResult.message`, `escalation.message`, `autoCheckMessage` snapshot) JSX child olarak render edildi — React varsayılan escape yapar. `whitespace-pre-line` CSS class'ı yalnız newline koruma için; HTML interpret etmiyor. Hiçbir yerde `dangerouslySetInnerHTML` yok.
- **CSRF:** SameSite Bearer + JSON POST + same-origin fetch; backend route'ları zaten `[ValidateAntiForgeryToken]` kapsamı dışında (REST API).
- **Clipboard:** Yok — T92'de panoya kopyalama yok.

## Dış Varsayımlar (Ön-uçuş)

| Varsayım | Doğrulama | Sonuç |
|----------|-----------|-------|
| Backend `POST /transactions/:id/disputes` (T8) mevcut | `DisputesController.cs` L33 `[HttpPost]` + DisputeService.OpenAsync | ✓ |
| Backend `POST /transactions/:id/disputes/:disputeId/submit-txhash` (T9) mevcut | `DisputesController.cs` L70 + DisputeService.SubmitTxHashAsync | ✓ |
| Backend `POST /transactions/:id/disputes/:disputeId/escalate` (T10) mevcut | `DisputesController.cs` L111 + DisputeService.EscalateAsync | ✓ |
| Backend response shape T8 `autoCheckResult: { resolved, message, canSubmitTxHash, canEscalate }` | `DisputeDtos.cs` L12 `AutoCheckResultDto` 4 bool/string alan | ✓ |
| `TransactionDetailDispute.canSubmitTxHash` / `canEscalate` field'ları surface ediliyor | `TransactionDetailDto.cs` L119 `DisputeSummaryDto` 2 bool flag | ✓ |
| `availableActions.canDispute` server-derived | `TransactionDetailService.cs` L375 `canDispute = role == "buyer" && status in {ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED} && !HasActiveDispute` | ✓ |
| API route prefix `/api/v1` `apiClient` tarafından otomatik eklenir | `client.ts` L3 `API_BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "/api/v1"` | ✓ |
| `DisputeType` ve `DisputeStatus` frontend enums backend ile birebir | `types/enums.ts` L82-93 3+3 değer (PAYMENT/DELIVERY/WRONG_ITEM, OPEN/ESCALATED/CLOSED) | ✓ |
| `<dialog>` element next 16.2 + React 19 ile uyumlu | T90 `CancelModal.tsx` zaten kullanıyor ve production'da çalışıyor | ✓ |
| `useQueryClient` + invalidateQueries pattern T90 page'inde kuruluydu | `transactions/[id]/page.tsx` L55 + L64 mevcut | ✓ |

Dış varsayım kırığı yok.

## Commit & PR

- Branch: `task/T92-dispute-ui`
- Commit: `34d58e5` — `T92: Dispute UI (C07 + S07 action wiring)`
- PR: [#139](https://github.com/turkerurganci/Skinora/pull/139)
- CI: ✓ PASS — run [`26331452400`](https://github.com/turkerurganci/Skinora/actions/runs/26331452400) 9/9 job ✓ (Lint + Build + Unit + Integration + Contract + Migration dry-run + Docker (frontend) + CI Gate; Guard skipped)

## Known Limitations / Follow-up

- **K1 — SignalR DisputeUpdate event (T96):** 07 §10.x DisputeUpdate `{transactionId, disputeId, status, autoCheckResult}` SignalR event tasarımı 07'de tanımlı ama T92 React Query polling/invalidate paterni kullanır (T96 ile aynı pattern; T90 K1 dürüst devir). Dispute status değişimleri (escalate sonrası ESCALATED, admin tarafından CLOSED) sayfa refetch'iyle güncellenir; admin kapatma anında push T96 SignalR `/hubs/notifications` channel'da consume edilecek.
- **K2 — Lokal mesaj çevirisi T97'ye devredildi:** Backend `PaymentDisputeAutoChecker` (T58) / `DeliveryDisputeAutoChecker` / `WrongItemDisputeAutoChecker` Türkçe-only inline mesajları döndürür ("Ödemeniz doğrulandı, işlem devam ediyor", "Blockchain üzerinde ödeme bulunamadı", vb.). T92 frontend bu mesajları verbatim render eder — T90 DisputeBlock'taki pattern ile tutarlı, proje sahibi onayı 2026-05-23 "Recommended A: API verbatim göster, T97 backend devri". Tam multi-locale support T97 backend i18n geçişi ile gelir.
- **K3 — Dispute create surface yalnız buyer:** 02 §10.2 + service guard "Yalnızca alıcı dispute açabilir" — T92 buton görünürlüğü server `availableActions.canDispute` flag'i ile yönetildiği için satıcı zaten bu yola giremez (server `canDispute=false` döner). Seller payout sorunu için `/transactions/:id/report-payout-issue` (T11 endpoint) ayrı UI gerekecek — T-future (Satıcı dashboard / S05 follow-up).
- **K4 — DELIVERY ve WRONG_ITEM type'larında TX hash sub-step görünmez:** Backend bu type'ların auto-checker'ları `canSubmitTxHash=false` döner; frontend conditional render bu flag'i takip eder. Spec gereği TX hash yalnız PAYMENT dispute'una ait (07 §7.9 `NotPaymentDispute` 422 hata kodu garantisi).
- **K5 — Concurrent submit-txhash race:** Kullanıcı submit-txhash butonuna iki kez hızlı bastığında ikinci request submitting state'iyle disable edilir ama transition sırasında ilk request henüz `setStep("txhashChecking")`'e geçmediyse double-call mümkün — backend idempotent (T58 service `Set<>(BlockchainTransactions)` AsNoTracking sorgusu) + `txhash` step disable+min-input check yeterli koruma. Defansif submit throttle T-future.
- **K6 — autoCheckResult snapshot stale read:** DisputeBlock'tan açılan modal `dispute.autoCheckResult` (TransactionDetailDispute) snapshot'ı kullanır — admin tarafından dispute kapatılmışsa veya başka tarafın aksiyonuyla flag'ler değişmişse user request submit ettiğinde backend `DISPUTE_CLOSED` / `ALREADY_ESCALATED` 409 dönecek; UI hata kodunu `disputeForm.errors.DISPUTE_CLOSED` / `ALREADY_ESCALATED` ile gösterir + onClose → refetch ile fresh state alır. Daha proaktif "modal açılırken canRefetch" T-future.
- **K7 — Dispute history (closed disputes) gösterimi:** 04 §7.3 spec'te dispute panel "Aktif itiraz" başlığını taşıyor — closed disputes (CLOSED status) backend `TransactionDetailDispute` field'ında null döner mü kontrolü? `TransactionDetailService.cs` L375 `canDispute` flag'i CLOSED dispute yokken true olabilir, ama dispute field'ı CLOSED ise yine null mu döner? T-future audit. Şimdilik backend `dispute` field'ı varsa render edilir; CLOSED status için statuses.CLOSED i18n key zaten mevcut.
- **K8 — Test coverage:** Plan "Test beklentisi: Yok" diyor. Manuel smoke test validator chat'inde yapılır. Vitest + RTL frontend test runner T-future (öncelik: Vitest project setup, sonra DisputeForm state machine unit + integration mock fetch).

## Notlar

- **Working tree:** temiz (Adım -1 ✓).
- **Main CI startup check:** 3/3 success — runs [`26330594495`](https://github.com/turkerurganci/Skinora/actions/runs/26330594495) (T91 chore PR #137), [`26330594485`](https://github.com/turkerurganci/Skinora/actions/runs/26330594485) (T91), [`26329500104`](https://github.com/turkerurganci/Skinora/actions/runs/26329500104) (chore frontend tsc) (Adım 0 ✓).
- **Bağımlılık kontrolü:** T90 ✓ Tamamlandı (PR #136, squash `168d04a`). T58 (DisputeService) zaten F3'te merge'li.
- **Scope kararları (2026-05-23, proje sahibi onayı):**
  - UI yerleşim: **Modal** (CancelModal pattern) — Recommended seçildi
  - Form yapısı: **Tek unified DisputeForm** (`existingDispute` prop ile) — Recommended seçildi
  - Mesaj lokalize: **API verbatim göster, T97 backend devri** — Recommended seçildi
- **T90 K2 closure:** T90 raporu Known Limitations K2 "Dispute butonları disabled → T92" satırı bu task ile kapatılıyor; `comingInT92` / `disputeComingInT92` / `comingSoon` i18n key'leri 4 locale'den silindi, JSX'teki disabled tooltip'ler kaldırıldı.
- **API client modülerleştirmesi:** `lib/api/disputes.ts` ayrı dosya açıldı (T58 backend pattern: ayrı DisputesController + DisputeService + DisputeDtos) — `lib/api/transactions.ts`'e gömmek discoverability + bağımlılık tipini bulanıklaştırırdı. T93+ profil/notification/admin task'ları aynı paterni izlemeli.
- **Detail page DisputeBlock prop genişlemesi breaking change değil:** Tek caller `transactions/[id]/page.tsx` aynı PR'da güncellendi; component dış API'ya yayılmadı (`index.ts` re-export aynı isim).
- **Memory yansıtma:** Bu rapor commit'lendikten sonra `.claude/memory/MEMORY.md` Current Status bölümüne 1-2 satır T92 özet eklenir (Bitiş Kapısı item 8).
