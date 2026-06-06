# T101 — Admin İşlem listesi + detay (S15, S16)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-06-06

---

## Yapılan İşler

Frontend S15 (İşlem Listesi & Arama) + S16 (İşlem Detay — Admin), mevcut AD6/AD7 (T63) okuma yüzeyleri ve AD19/AD19b/AD19c (T59) yaşam-döngüsü endpoint'leri üzerine kuruldu. Ek olarak 04 §8.4'ün gruplu "Durum" filtresini sunucu tarafında ifade edebilmek için **AD6'ya minimal bir backend eklentisi** yapıldı (proje sahibi onayı 2026-06-06 — T100 precedent'i; migration yok).

**Backend (AD6 statusGroup):**
- Yeni `AdminTransactionStatusGroup` enum (`ACTIVE` / `COMPLETED` / `CANCELLED` / `FLAGGED`). **`ACTIVE` = terminal-olmayan** durumlar — `AdminDashboardService._terminalStates` ile birebir aynalanır, böylece S12 dashboard "Aktif İşlemler" kartı ve `?tab=active` deep-link'i aynı kümeyi gösterir (ACTIVE, FLAGGED'i içerir; ayrı `FLAGGED` grubu yalnız flag'leri daraltır).
- `AdminTransactionListQuery.StatusGroup` alanı + `BuildFilteredQuery` eşlemesi (ACTIVE → `!terminal`, COMPLETED → COMPLETED, CANCELLED → 4×CANCELLED_*, FLAGGED → FLAGGED). `status` ve `statusGroup` birlikte verilirse ikisi de uygulanır (AND); UI yalnız birini gönderir.
- `AdminTransactionsController.List` `[FromQuery] AdminTransactionStatusGroup? statusGroup` (aynı `VIEW_TRANSACTIONS` policy + `admin-read` rate limit).
- 07 §9.6 doc güncellendi (statusGroup tablo + AND notu).

**Frontend (S15 + S16):**
- `lib/api/admin.ts` işlem katmanı: `AdminTransactionListItem`/`AdminTransactionListResponse`/`AdminTransactionListQuery` (+ `statusGroup`), `AdminTransactionDetail` + 8 alt-record (AD7), `cancel`/`emergency-hold`/`release-hold` sonuç tipleri ve `listAdminTransactions`/`getAdminTransaction`/`cancelAdminTransaction`/`applyEmergencyHold`/`releaseEmergencyHold`.
- 3 React Query hook: `useAdminTransactionList` (keepPreviousData), `useAdminTransactionDetail`, `useAdminTransactionMutations` (cancel/hold/release → `["admin","transactions"]` + `["admin","dashboard"]` invalidation).
- `TransactionListTable` (04 §8.4 — 8 kolon: ID→S16, Item görsel+ad, Fiyat, Satıcı→S20, Alıcı→S20, Durum StatusBadge, Oluşturulma, Tamamlanma/İptal; ResponsiveTable mobil kart).
- `TransactionDetailView` (04 §8.5 — 9 AD7 bölümü: İşlem Bilgileri / Durum Geçmişi timeline / Taraflar / Ödeme / Satıcıya Ödeme / İade / Bildirim Geçmişi / Dispute Geçmişi / Flag Geçmişi) + state-aware aksiyon rayı: FLAGGED→İşleme Devam Et/İptal Et (AD4/AD5 flag-resolution, mevcut hook'lar reuse); aktif→İşlemi İptal Et (AD19) + Emergency Hold Uygula (AD19b); EMERGENCY_HOLD→Hold Kaldır Devam/İptal (AD19c) + hold bilgisi + Auto-Hold sanctions etiketi; ITEM_DELIVERED→Exceptional Resolution (deferred); terminal→salt okunur. Aksiyon modal'ları `FlagActionModal` reuse.
- Sayfalar: `admin/transactions/page.tsx` (stub→tam, URL-senkron filtre + S12 dashboard `?tab=active`/`?range=` deep-link uyumu) + yeni `admin/transactions/[id]/page.tsx`.
- `adminTransactions` 4-locale namespace (100 leaf × 4; toplam parity **859×4**).

## Etkilenen Modüller / Dosyalar

**Backend:**
- `backend/src/Modules/Skinora.Transactions/Application/Admin/IAdminTransactionQueryService.cs` (enum + query alanı)
- `backend/src/Skinora.API/Services/AdminTransactionQueryService.cs` (terminal/cancelled set + group filtresi)
- `backend/src/Skinora.API/Controllers/AdminTransactionsController.cs` (query param)
- `backend/tests/Skinora.API.Tests/Integration/AdminT63EndpointTests.cs` (+3 test)
- `Docs/07_API_DESIGN.md` (§9.6 statusGroup)

**Frontend:**
- `frontend/src/lib/api/admin.ts` (işlem katmanı — append)
- `frontend/src/lib/hooks/useAdminTransaction{List,Detail,Mutations}.ts` (yeni)
- `frontend/src/components/admin/{TransactionListTable,TransactionDetailView}.tsx` (yeni) + `index.ts`
- `frontend/src/app/[locale]/admin/transactions/page.tsx` (stub→tam) + `[id]/page.tsx` (yeni)
- `frontend/src/i18n/messages/{en,tr,es,zh}.json` (`adminTransactions` namespace)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | S15 İşlem listesi: filtre (durum, tarih, kullanıcı, tutar, stablecoin), sayfalama | ✓ | `page.tsx` FilterBar (statusGroup/stablecoin/search/min-max/date) URL-senkron + `Pagination`; AD6 query; `next build` ƒ /admin/transactions |
| 2 | S16 İşlem detay: durum geçmişi timeline, ödeme/payout/refund detayları, admin aksiyonlar (iptal, hold) | ✓ | `TransactionDetailView` 9 AD7 bölümü + aksiyon rayı (cancel/hold/release); AD7 + AD19/b/c |
| 3 | Admin iptal modal'ı, emergency hold modal'ı | ✓ | `FlagActionModal` reuse — cancel (reason≥10) + hold (reason≥10) + release (note≥1) |
| 4 | GET /admin/transactions, GET /admin/transactions/:id çağrıları | ✓ | `listAdminTransactions`/`getAdminTransaction` (admin.ts); hook'lar |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend integration (AD6/AD7) | ✓ 24/24 | `dotnet test Skinora.API.Tests --filter AdminT63EndpointTests` (SQLite in-memory); 21 mevcut + 3 yeni statusGroup (ACTIVE/CANCELLED/FLAGGED) |
| Backend build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Backend format | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes` |
| Frontend tsc | ✓ 0 | `tsc --noEmit` |
| Frontend eslint | ✓ 0/0 | `eslint <T101 files>` |
| Frontend prettier | ✓ clean | `prettier --check <T101 files + 4 locale>` |
| Frontend build | ✓ PASS | `next build` — `/admin/transactions` + `/admin/transactions/[id]` ƒ Dynamic |
| Locale parity | ✓ 859×4 | node leaf-count: en/tr/es/zh = 859, adminTransactions = 100 her dilde |
| Frontend unit | — | Frontend test runner yok (F5 plan-onaylı) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validate chat'i bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: Yok (yeni domain enum/kolon yok; `AdminTransactionStatusGroup` API query-param enum'u, DB'de saklanmaz).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni bağımlılık: Yok.

## Commit & PR
- Branch: `task/T101-admin-transactions`
- Commit: `4a9c4e0` — T101: Admin İşlem listesi + detay (S15, S16)
- PR: #152
- CI: ⏳ izleniyor

## Known Limitations / Follow-up
- **K1 — Exceptional Resolution:** ITEM_DELIVERED'da standart iptal + EMERGENCY_HOLD release→CANCEL@ITEM_DELIVERED için backend endpoint yok (AD19 422 `CANNOT_CANCEL_AT_DELIVERY_STAGE` / AD19c 422 `CANNOT_CANCEL_DELIVERED_HOLD`). Buton disabled "Yakında" — ayrı task'a forward (04 §8.5 line 1566).
- **K2 — S20 taraf linkleri:** Satıcı/Alıcı `→ /admin/users/{steamId}` (S20) linkleri T105'e kadar 404 (T99/T100 forward-deep-link paterni).
- **K3 — S16 taraf skoru:** AD7 `AdminTransactionPartyDto` yalnız steamId/displayName/avatar taşır (reputationScore yok) → basit taraf satırı; skor gösterimi forward (07 §9.7 party DTO genişletme).
- **K4 — Liste cancel tarihi:** AD6 list DTO'sunda yalnız `completedAt` var (cancelledAt yok) → "Tamamlanma/İptal" kolonu completedAt veya "—"; iptal tarihi S16 detayında.
- **K5 — `?range=daily|weekly`:** COMPLETED grubuna eşlenir; tam gün/hafta penceresi deferred (AD6 `dateFrom`/`dateTo` `CreatedAt` filtreler, kartlar `CompletedAt` sayar).
- **K6 — Geçmiş bölümlerinde ham enum:** Dispute/Flag tür+durum ve Notification tür değerleri ham backend string'leri (admin audit verisi) — locale map'lenmedi.
- **K7 — Auto-Hold sanctions etiketi:** `emergencyHoldReason` üzerinde `/sanction/i` heuristic; AD7'de `isAutomatic`/sanctions bayrağı yok → forward.
- **K8 — TX hash explorer:** Tronscan (`tronscan.org/#/transaction/`) hard-coded; env-config forward.
- **K9 — Frontend test runner yok:** F5 plan-onaylı (T97–T100 ile aynı).

## Notlar
- **Working tree:** Session başında temiz. Yapım sırasında `.claude/settings.json` working-tree'de değişti (T101 dışı, harness/permission kaynaklı) — commit'e dahil EDİLMEDİ (yalnız T101 dosyaları stage'lendi), proje sahibine ayrıca bildirildi.
- **Adım -1 (Working tree hygiene):** temiz.
- **Adım 0 (Main CI startup):** son 3 main run `success` (`27070067469`/`27070067472`/`27068534849` — #151/#149).
- **Adım 4 (Dış varsayım doğrulama):** (1) AD6/AD7 endpoint'leri mevcut (T63) — kod okundu, **doğrulandı**; (2) AD6 `status` tek `TransactionStatus?` → §8.4 gruplu filtre için yetersiz — **kırık varsayım**, proje sahibine sunuldu (2026-06-06), Seçenek A (gruplu + statusGroup backend eklentisi) onaylandı; (3) AD19/b/c (T59) mevcut — doğrulandı; (4) yeni dış bağımlılık yok.
- **Scope kararı:** T100 precedent'i — F5 frontend task'ı kendi backend ihtiyacını (statusGroup) ekleyebilir. Migration/yeni enum-in-DB yok.
- **Tasarım:** S16 bespoke (AD7 DTO doğrudan tüketilir, S07 party-perspektif component'leri uymadığı için); shared `StatusBadge`/format helper'lar + `FlagActionModal` reuse edildi.
