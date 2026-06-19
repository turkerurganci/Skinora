# WP13 — FE Tamlık (yasal sayfalar + polish + enum sync)

**Faz:** F6 öncesi (PRE_F6_PLAN) | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız validator bekliyor) | **Tarih:** 2026-06-19

---

## Yapılan İşler

WP13, frontend tamlık paketidir — MVP yasal gereklilik (yasal sayfalar), enum drift kapatma ve birikmiş FE-polish/follow-up kalemleri. **Owner kararları (AskUserQuestion 2026-06-19):** yasal sayfalar = **iskelet + i18n placeholder** (gerçek hukuki metin WP17); permission guard = **minimal FE** (route guard + sidebar gizleme, `isAdmin`); next/image migration = **ERTELE** (WP18/post-MVP); kalan polish = **TAM WP13** (admin-sort + url-state-sync + NEXT_LOCALE cookie + dispute-polish + countdown + email cooldown).

**Salt frontend — MIGRATION YOK, yeni runtime dependency YOK.**

1. **FE-enums-ts-lag** — `types/enums.ts` backend ile senkronlandı: `NotificationType` 20→27 (EMERGENCY_HOLD_APPLIED/RELEASED, INSUFFICIENT_PAYMENT, OVERPAYMENT_REFUNDED, WRONG_TOKEN_REFUND, ACCOUNT_SUSPENDED/UNSUSPENDED), `AuditAction` 12→30 (REFUND_BLOCKED + fraud/bot/recovery/reconciliation/sanctions/MAINTENANCE_MODE_CHANGED), `FraudFlagType` 4→5 (SANCTIONS_MATCH), `BlockchainTransactionType` 9→10 (SWEEP). `notification-icons.ts` `Record<NotificationType,…>` 7 yeni tip için tamamlandı (yoksa tsc kırılırdı).
2. **Yasal sayfalar** — yeni `/privacy`, `/terms`, `/support` route'ları + paylaşılan `LegalPage` iskeleti + `legal` i18n namespace (42 anahtar ×4 dil, placeholder; gerçek metin WP17). Footer düzeltildi (privacy aktif, support eklendi, `privacyComingSoon` kaldırıldı). **Kırık `/terms` 404'ü kapandı** (callback/TosModal/TosRepromptGate/Footer hepsi artık çözülüyor).
3. **login→dashboard redirect** — landing page (`[locale]/page.tsx`) authenticated ziyaretçiyi `/dashboard`'a yönlendirir (AuthInitializer hidrasyonundan sonra; unauth no-op).
4. **Minimal FE permission guard** — `AdminGuard` (`/admin` layout sarmalayıcı): token localStorage'tan okunur (store hidrasyon yarışını atlar), paylaşılan `["auth","me"]` query çözülene kadar bekler, admin değilse `/dashboard`'a / oturum yoksa `/`'a yönlendirir. `AuthInitializer` artık `isAdmin`'i `/auth/me` `role` claim'inden doldurur (`admin`/`super_admin`). **Backend yetki authoritative kalır** (her endpoint 403 enforce eder); guard yalnız kırık-shell deneyimini engeller.
5. **admin-table-sort** — `ResponsiveTable`'a opsiyonel tıkla-sırala başlık desteği (`sortKey` kolon alanı + `sort` prop, `aria-sort`); transactions (createdAt/price/status, AD6) + flags (type/reviewStatus/createdAt, AD2) URL-senkron (`?sortBy=&sortOrder=`). Paylaşılan `tableSort.ts` (parse + toggle). Backend yalnız bu iki listede sort destekler → kapsam onlarla sınırlı.
6. **url-state-sync** — dashboard tab/page `?tab=&page=` senkron + deep-link tüketimi (`?tab=completed`); create-transaction wizard adımı `?step=` (push → tarayıcı geri tuşu adımlar arası gezinir; veri-farkında clamp → hard-refresh temiz step 1'e iner, hassas form verisi URL'ye yazılmaz).
7. **NEXT_LOCALE cookie migration** — yeni `i18n/navigation.ts` (`createNavigation` + `setLocaleCookie`); `LanguageSelector` + settings `LanguagePreferenceSection` artık localStorage `preferredLocale` + manuel path-splice + full reload yerine `NEXT_LOCALE` cookie + next-intl soft navigation kullanır (path + query korunur). `preferredLocale` okuyan yoktu → temiz kaldırma.
8. **dispute-detail-polish** — merkezi Tronscan URL sabiti (`blockchain.ts`, env-override) + `TxHashLink` (masked hash → Tronscan explorer linki + kopya) `SellerPayoutSummary`/`CancelInfoBlock`/`PaymentEventBanners`'a bağlandı; `ItemCard` detailed variant'a asset-id satırı. *(Kapatılan-dispute admin notu + seller payout-issue UI ertelendi — backend DTO alanı yok, aşağı bkz.)*
9. **verification countdown + email cooldown Retry-After** — `ApiError.retryAfterSeconds` (`Retry-After` header capture, apiClient) + yeni `InlineCountdown` (mm:ss/Ns); `NotificationPreferencesSection`: kod-geçerlilik geri sayımı (`expiresIn`) + resend-cooldown geri sayımı (429 + Retry-After'da resend butonunu kilitler).
10. **steamTradeOfferUrl href** — `TransactionDetailResponse.steamTradeOfferUrl` (WP12 backend DTO) FE tipine eklendi; `StateActionPanel` TRADE_OFFER_SENT_TO_* state'lerinde "Steam takas teklifine git" CTA (URL doluysa).
11. **Trivia** — `transactionDetail.accept.errors.ACCOUNT_FLAGGED` i18n (4 dil, WP4a fallback yerine özel mesaj); `format.ts` deprecated `formatAmount` alias kaldırıldı (0 call-site); `AccountManagementSection` logout'taki mükerrer `localStorage.removeItem` kaldırıldı (WP11 — `logout()` zaten yapıyor).

## Etkilenen Modüller / Dosyalar

**Yeni:** `i18n/navigation.ts` · `lib/auth/roles.ts` · `lib/auth/AdminGuard.tsx` · `lib/admin/tableSort.ts` · `lib/utils/blockchain.ts` · `components/legal/LegalPage.tsx` + `index.ts` · `components/common/InlineCountdown.tsx` · `components/transactions/detail/TxHashLink.tsx` · `app/[locale]/{privacy,terms,support}/page.tsx`

**Değişen (öne çıkanlar):** `types/enums.ts` · `lib/utils/notification-icons.ts` · `app/[locale]/page.tsx` · `app/[locale]/admin/layout.tsx` · `lib/auth/AuthInitializer.tsx` · `components/common/ResponsiveTable.tsx` + `index.ts` · `components/admin/{TransactionListTable,FlagQueueTable}.tsx` · `app/[locale]/admin/{transactions,flags}/page.tsx` · `lib/api/admin.ts` · `lib/hooks/useAdminFlagList.ts` · `app/[locale]/(main)/dashboard/page.tsx` · `components/transactions/new/NewTransactionForm.tsx` · `components/common/LanguageSelector.tsx` · `components/settings/{LanguagePreferenceSection,NotificationPreferencesSection,AccountManagementSection}.tsx` · `components/transactions/detail/{SellerPayoutSummary,CancelInfoBlock,PaymentEventBanners,StateActionPanel}.tsx` · `components/common/ItemCard.tsx` · `components/layout/Footer.tsx` · `lib/api/{client,transactions}.ts` · `lib/utils/format.ts` · `app/[locale]/(main)/transactions/[id]/page.tsx` · `i18n/messages/{en,tr,es,zh}.json`

## Kabul Kriterleri Kontrolü

| # | Kriter (owner-onaylı kapsam) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Yasal sayfalar (/privacy /terms /support) mevcut + kırık link yok | ✓ | `next build` 3 yeni route (`/[locale]/privacy|terms|support`); Footer + callback + TosModal `/terms`'e çözülür |
| 2 | login→dashboard redirect (authenticated landing) | ✓ | `page.tsx` `isAuthenticated` → `router.replace(.../dashboard)` |
| 3 | Admin client-side guard (minimal, route + redirect) | ✓ | `AdminGuard` non-admin → `/dashboard`; `isAdmin` `/auth/me` role'den; backend authoritative |
| 4 | enum sync (NotificationType/AuditAction/FraudFlagType/SWEEP) | ✓ | `enums.ts` 27/30/5/10; `notification-icons.ts` record tam; tsc 0 |
| 5 | admin tablo tıkla-sırala (API hazır, transactions + flags) | ✓ | `ResponsiveTable.sort` + URL `?sortBy=&sortOrder=`; allow-list `SORT_KEYS` |
| 6 | url-state-sync (tx-list tab/page + deep-link + wizard step) | ✓ | dashboard `?tab=&page=`; wizard `?step=` push + clamp |
| 7 | NEXT_LOCALE cookie migration | ✓ | `navigation.ts setLocaleCookie` + soft nav; `preferredLocale` kaldırıldı |
| 8 | dispute-detail polish (Tronscan URL sabiti + asset-id) | ✓ | `blockchain.ts` + `TxHashLink` 3 blokta; ItemCard asset-id satırı |
| 9 | steamTradeOfferUrl href + ACCOUNT_FLAGGED i18n + cleanup | ✓ | StateActionPanel CTA; accept.errors.ACCOUNT_FLAGGED ×4; formatAmount/logout temizliği |

## Test Sonuçları

> **Not:** FE'de henüz otomatik test runner yok (Vitest kurulumu WP18 kapsamında). FE doğrulaması statik kapılar + build ile yapılır.

| Tür | Sonuç | Detay |
|---|---|---|
| Type check | ✓ 0 hata | `npx tsc --noEmit` exit 0 |
| Lint | ✓ 0 hata | `npx eslint` exit 0 (InlineCountdown effect `tick()` indirection ile `set-state-in-effect` uyarısı giderildi) |
| Format (dokunulan dosyalar) | ✓ temiz | `prettier --check` (44 değişen/yeni dosya) → "All matched files use Prettier code style" |
| Build | ✓ başarılı | `npm run build` exit 0; 36 route (3 yeni yasal route dahil); TypeScript build içinde geçti |
| i18n parity | ✓ 1230×4 | en/tr/es/zh anahtar setleri birebir (legal namespace 42×4 dahil) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Yapım self-check | 9/9 kabul kriteri ✓ |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok (salt frontend).
- **Config/env değişikliği:** Opsiyonel `NEXT_PUBLIC_TRONSCAN_TX_BASE_URL` (default `https://tronscan.org/#/transaction/`) — tanımsızsa default kullanılır, davranış değişmez.
- **Docker değişikliği:** Yok.
- **Yeni dependency:** Yok (`next-intl/navigation createNavigation` zaten next-intl ^4.9 içinde).

## Commit & PR

- Branch: `task/WP13-fe-completeness`
- Commit: `37e770c` — WP13: FE tamlık — yasal sayfalar + polish + enum sync
- PR: #186
- CI: ✓ PASS — HEAD `37e770c` run [`27831329203`](https://github.com/turkerurganci/Skinora/actions/runs/27831329203) tüm job success (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate)

## Known Limitations / Follow-up

- **next/image migration ERTELENDİ** (owner kararı) — ~11 bileşen ham `<img>` (eslint-disable ile bilinçli) + Steam-CDN whitelist → WP18/post-MVP.
- **closed-dispute admin notu + seller payout-issue UI** — `TransactionDetailDispute` DTO'sunda `adminNote`/`resolvedAt` yok, detail DTO'da payout-issue alanı yok → backend DTO eklemesi gerekir (FE-dışı). Follow-up (WP8/WP17 yakını veya ayrı).
- **VERIFICATION_COOLDOWN saniyesi** — backend bu özel 429'da `RetryAfterSeconds`'i yalnız mesaj metnine gömer (header/details değil); `ApiError.retryAfterSeconds` platform rate-limit 429'larını (Retry-After header) kapsar. Cooldown saniyesini yapısal vermek küçük bir backend değişikliği ister → follow-up.
- **Gerçek ToS/Privacy/Support hukuki metni** → WP17 (content-authoring); WP13 yalnız iskelet + placeholder.
- **Repo-geneli prettier drift** (51 dosya, dokunulmayanlar) → WP18; bu PR yalnız dokunduğu dosyaları prettier-clean bıraktı.
- **`middleware` deprecation uyarısı** (Next 16 "proxy") — pre-existing, WP13-dışı.

## Notlar

- **Dış varsayımlar:** Salt FE, paid-feature/dış-API varsayımı yok. `next-intl/navigation createNavigation` v4.9'da mevcut (build + tsc doğruladı). Backend sort param adları `sortBy`/`sortOrder` controller'lardan teyit edildi (AdminFlagsController/AdminTransactionsController). `/auth/me` `role` alanı admin türevi için kullanıldı (CurrentUserService raw JWT role claim).
- **Mini güvenlik kontrolü:** Secret sızıntısı yok (mailto + Tronscan public; `NEXT_PUBLIC_*` by-design public). Auth: AdminGuard salt client-convenience, backend authoritative korunur; yasal sayfalar bilinçli public; yeni korunmasız endpoint yok. Input validation: sort/URL paramları allow-list ile doğrulanır; locale sabit listeden; yeni persist-edilen kullanıcı girdisi yok. Yeni dış bağımlılık yok.
- **Working tree:** Adım -1 temiz. Adım 0 main son-3 success (`27825433288`/`27825433282`/`27824841355`).
