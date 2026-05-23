# T95 — Bildirimler sayfası (S11)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-23

---

## Yapılan İşler

`04 §7.7` (S11 Bildirimler `/notifications`) sayfası baştan yazıldı. T85 frontend skeleton'unun bıraktığı `<div>Notifications</div>` stub'ı tam fonksiyonel bildirim merkeziyle değiştirildi. Backend (T26 + T80) `/api/v1/notifications` ailesindeki 4 endpoint zaten production'daydı — T95 yalnız frontend wiring + UI + i18n + header badge entegrasyonu.

**Sayfa akışı (`/notifications`):**

1. **Auth gate** — `useAuthStore.isAuthenticated` `false` ise `errors.forbidden` ErrorState gösterilir; backend `[Authorize(Policy=Authenticated)]` zaten 401 dönecek ama UI öncesi hızlı reddetme React Query çağırmayı atlatır.
2. **Skeleton (C14)** — `list.isLoading` (initial load) sırasında 5 satır + başlık çubuğu skeleton'u render edilir. `placeholderData: keepPreviousData` sayfa değişimlerinde skeleton flaşı önler — paginasyon `useTransactionList` ergonomisinin aynısı.
3. **Empty (C13)** — `totalCount === 0` durumunda `EmptyState` (🔔 ikonu, "Bildiriminiz yok" / "Yeni bildirimleriniz olduğunda burada görünür.").
4. **Data** — Header çubuğu (sayfa başlığı + "Tüm Bildirimleri Okundu İşaretle" linki) → `NotificationList` (divide-y card) → Pagination C16. Mark-all butonu okunmamış bildirim yokken devre dışı; mutation pending sırasında "İşaretleniyor…" metni.
5. **Error** — 401 `errors.forbidden`; diğer hatalar `errors.generic` + Retry.

**Bildirim satırı (`NotificationRow`):**

- Okunmamış göstergesi: 4×4 mavi nokta + `bg-blue-50/40` satır arka planı + `font-medium` metin. Okunmuş satırlar `bg-white` + `text-gray-700`.
- İkon: 04 §7.7 tablosundaki 6 kategori — `notification-icons.ts` 20 backend type'ını → 6 kategori grubuna eşler (transactionUpdate 🔄 / payment 💰 / warning ⚠ / completion ✅ / cancellation ❌ / flag 🔍). Frontend-only mapping (T95 scope kararı): API contract'a dokunmaz, 04 §7.7 spec'i tek source of truth.
- Metin: `notification.message` verbatim (backend Türkçe — K2 T97 devir).
- Zaman: `useFormatter().relativeTime(createdAt)` → next-intl lokal göreli zaman ("5 dk önce" / "5 min ago" / "5 分钟前" / "hace 5 min"). Tarih `<time dateTime>` ile semantic.
- Tıklanabilirlik: `targetType + targetId` → `/transactions/{id}` (S07) veya `/admin/flags/{id}` (S13). `null` target → row non-interactive (button role yok, gri imleç).
- Klavye: interactive satırlar `tabIndex=0` + Enter/Space ile aktive.
- ARIA: okunmamışlarda `aria-label="Okunmamış — {message}"` + ek `sr-only` "Okunmamış" prefix.

**Tıklama davranışı (Optimistic + paralel navigate — scope kararı):**

`!isRead` ise `markRead.mutate(id)` fire-and-forget olarak çağrılır; `router.push(href)` aynı render'da paralel başlar. Backend N4 endpoint'i idempotent + 404/403 swallow edilir (race koşulu güvenli). Mutation `onSuccess` `["notifications","list"]` + `["notifications","unread-count"]` queryKey'lerini invalidate ederek badge'i ve list'i tutarlı tutar.

**"Tüm Bildirimleri Okundu İşaretle" (`MarkAllReadButton`):**

- Buton: sayfada en az bir okunmamış varsa enabled; yoksa veya mutation pending'ken disabled.
- `useMarkAllRead` mutation `onSuccess` aynı iki query'yi invalidate eder → liste + header badge anında güncellenir.

**Header zil badge (T95 kapsamında canlandırıldı — scope kararı):**

`MainShell` artık `useUnreadCount(isAuthenticated && !isSuspended)` ile `GET /notifications/unread-count` çağırır, sonucu `Header` `unreadNotifications` prop'una iletir. T85 zaten kırmızı badge UI'ını tanımlamıştı (`>0` → kırmızı rozet, `>99` → "99+"), `unreadNotifications=0` default ile sessiz duruyordu. Suspended session'lar bildirimler nav entry'sini gizlediği için hook auth + suspended gate'inden geçmemiş kullanıcıyı boş bırakır. **Live push K1 T96 forward** — şu an mark-all-read / mark-read mutation'ları + 30sn `staleTime` sınırları dahilinde tutarlı. SignalR `/hubs/notifications` push handler T96'da bu queryKey'leri invalidate edecek.

**Pagination (URL `?page=N` — scope kararı):**

- `useSearchParams` ile URL query okunur, `parsePage` ile pozitif int'e sanitize edilir (NaN/negatif → 1).
- Sayfa değişimi `router.push(pathname?page=N)`; `page=1` URL'de query'siz tutulur (canonical URL).
- Deep-link friendly: refresh ve browser back/forward sayfayı korur.
- `keepPreviousData` sayfa flips'i skeleton flaşı olmadan gerçekleştirir.

## Etkilenen Modüller / Dosyalar

**Yeni:**

- [frontend/src/lib/api/notifications.ts](../../frontend/src/lib/api/notifications.ts) — 4 client function (`listNotifications`, `getUnreadCount`, `markAllNotificationsRead`, `markNotificationRead`) + `NotificationListItem` + `NotificationListQuery` + response DTO'ları.
- [frontend/src/lib/hooks/useNotificationList.ts](../../frontend/src/lib/hooks/useNotificationList.ts) — `useQuery` ile N1 list + `keepPreviousData` + 401 retry guard.
- [frontend/src/lib/hooks/useUnreadCount.ts](../../frontend/src/lib/hooks/useUnreadCount.ts) — `useQuery` ile N2 unread-count + 30sn staleTime + 401 retry guard.
- [frontend/src/lib/hooks/useNotificationMutations.ts](../../frontend/src/lib/hooks/useNotificationMutations.ts) — `useMarkAllRead` + `useMarkRead` + shared invalidator.
- [frontend/src/lib/utils/notification-icons.ts](../../frontend/src/lib/utils/notification-icons.ts) — 20 `NotificationType` → 6 ikon kategorisi mapping + emoji table.
- [frontend/src/components/notifications/NotificationRow.tsx](../../frontend/src/components/notifications/NotificationRow.tsx) — satır component (ikon, mavi nokta, mesaj, göreli zaman, tıklama).
- [frontend/src/components/notifications/NotificationList.tsx](../../frontend/src/components/notifications/NotificationList.tsx) — `<ul>` divide-y wrapper.
- [frontend/src/components/notifications/MarkAllReadButton.tsx](../../frontend/src/components/notifications/MarkAllReadButton.tsx) — header çubuğu linki + mutation.
- [frontend/src/components/notifications/index.ts](../../frontend/src/components/notifications/index.ts) — barrel export.

**Değişen:**

- [frontend/src/app/[locale]/(main)/notifications/page.tsx](../../frontend/src/app/%5Blocale%5D/(main)/notifications/page.tsx) — `<div>Notifications</div>` stub'ı → tam fonksiyonel S11 sayfası.
- [frontend/src/components/layout/MainShell.tsx](../../frontend/src/components/layout/MainShell.tsx) — `useUnreadCount` ile Header'a unread sayı bağlandı.
- [frontend/src/i18n/messages/{tr,en,zh,es}.json](../../frontend/src/i18n/messages/) — yeni `notificationsInbox` namespace (10 key × 4 locale = 40 yeni satır). `notifications` namespace (S10 bildirim tercihleri) collision'sız korundu.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bildirim listesi: okunmamış vurgusu, ikon, metin, zaman, tıklanabilir | ✓ Karşılandı | `NotificationRow.tsx` — `!isRead` → mavi nokta + `bg-blue-50/40` + `font-medium`; `iconForType` → 6 kategori emoji; `notification.message` text; `useFormatter().relativeTime`; `targetType+targetId` → `/transactions/:id` veya `/admin/flags/:id` + role=button + keyboard handler. |
| 2 | "Tüm Bildirimleri Okundu İşaretle" linki | ✓ Karşılandı | `MarkAllReadButton.tsx` + `useMarkAllRead` mutation; okunmamış yokken disabled; pending sırasında "İşaretleniyor…". |
| 3 | State'ler: yok (empty), yeni bildirimler, yükleniyor | ✓ Karşılandı | `page.tsx` — `list.isLoading` → 5×Skeleton; `totalCount===0` → EmptyState (C13) "Bildiriminiz yok"; data + okunmamış vurgulu satırlar. |
| 4 | Pagination | ✓ Karşılandı | `Pagination` C16 + `useSearchParams` URL `?page=N`; `parsePage` sanitize; `page=1` URL'de query'siz; `pageSize=20` (spec'le birebir). |

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 04 §7.7 tüm state'ler var mı? | ✓ Karşılandı | empty (`EmptyState` 🔔), data (`NotificationList`), loading (`Skeleton` ×5); okunmamış vurgusu + ikon + mesaj + zaman + tıklanabilirlik tablosu birebir. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | T95 test beklentisi: **Yok** (11_IMPLEMENTATION_PLAN T95) |
| Integration | — | T95 test beklentisi: **Yok** |
| TypeScript | ✓ PASS | `npx tsc --noEmit` çıktısız (frontend kök) |
| ESLint | ✓ PASS | `npm run lint` çıktısız warning/error |
| Build | ✓ PASS | `npm run build` — `Compiled successfully in 3.0s` + TypeScript 3.4s + `/[locale]/notifications` route generated |
| 4-locale parity | ✓ PASS | 632/632/632/632 key, drift yok (Node script ile karşılaştırıldı) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Validate chat bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok (backend N1–N4 endpoint'leri T26 + T80'de hazır, T95 yalnız frontend)
- **Config/env:** Yok
- **Docker:** Yok
- **Yeni dış bağımlılık:** Yok (next-intl 4.12 + @tanstack/react-query 5.x mevcut, `useFormatter` next-intl 3.x'ten beri var)

## Commit & PR

- Branch: `task/T95-notifications-page`
- Commit: `464bbe2` — `T95: Bildirimler sayfası (S11) — list + mark-all-read + header badge`
- PR: [#142](https://github.com/turkerurganci/Skinora/pull/142)
- CI: ✓ PASS — run [`26339316183`](https://github.com/turkerurganci/Skinora/actions/runs/26339316183) 10/10 success (1. Lint / 2. Build / 3. Unit / 4. Integration / 5. Contract / 6. Migration / 7. Docker frontend / CI Gate + Detect changed paths + Guard skipped)

## Known Limitations / Follow-up

- **K1 — SignalR realtime push yok** (T96 forward devir) — `/hubs/notifications` `Notification` event handler bağlanmadığı için yeni bildirim sayfa açıkken otomatik gözükmez; sayfa navigation veya 30sn `staleTime` sınırından sonra refetch ile gelir. Header badge de aynı şekilde — mutation invalidate'leri + staleTime ile tutarlı, live push T96.
- **K2 — Backend `message` Türkçe verbatim** (T97 i18n devir) — Notification mesajları backend tarafında oluşturulurken `tr-TR` formatında — locale-aware backend messaging T97 kapsamında yapılır. Frontend yalnız sayfa şablonu + meta (göreli zaman, ikon kategorisi, başlıklar) için 4-locale parity sağlar.
- **K3 — `targetType=flag` admin route'u** mevcut `/admin/flags/:id` route'una bağlanır; admin route ailesi T100'de tanımlı, henüz parametre route'u yok — bu URL navigate ettiğinde 404 ihtimali var (admin S13 link wiring T100 forward).
- **K4 — `nav.notifications`/`nav.unread`** zaten T85'te tanımlıydı, T95 yeniden kullandı; çakışma yok.
- **K5 — Unread count polling cadence yok** — yalnız mutation invalidate + 30sn staleTime + page navigation refetch. Production'da SignalR T96 ile live olacağı için ek polling yan etki (rate-limit, prod load) doğurmadan deferred edildi.

## Notlar

- **Working tree (Adım -1):** temiz
- **Main CI startup check (Adım 0):** 3/3 success — run IDs `26337997661`, `26337997668` (T94 #141 × 2), `26333984865` (T93 #140)
- **Dış varsayım kontrolü (Adım 4):**
  - Backend 4 endpoint canlı — kanıt: `backend/src/Skinora.API/Controllers/NotificationsController.cs` okundu, N1–N4 implement.
  - `PagedResult<T>` shape `{ items, totalCount, page, pageSize, totalPages }` — kanıt: `frontend/src/types/api.ts:20-26`.
  - `next-intl` v4.12.0 `useFormatter().relativeTime` mevcut — kanıt: `package.json` "next-intl": "^4.9.0", next-intl 3.x'ten beri var olan API.
  - `@tanstack/react-query` ^5.97.0 `keepPreviousData` + `useMutation` mevcut — kanıt: `useTransactionList.ts` aynı pattern'i kullanıyor.
  - Header `unreadNotifications` prop T85'te zaten kuruldu (`Header.tsx:11-14,45-52`), MainShell sadece kabloyu çekiyor.
- **Scope kararları (proje sahibi onayı 2026-05-23, AskUserQuestion):**
  - K1: Tıklama → **Optimistic + paralel navigate** (Recommended)
  - K2: Header badge → **T95'te canlandır** (Recommended)
  - K3: İkon mapping → **Frontend mapping** (Recommended)
  - K4: Pagination state → **URL query `?page=N`** (Recommended)
