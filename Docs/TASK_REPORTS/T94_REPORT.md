# T94 — Hesap ayarları (S10)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-23

---

## Yapılan İşler

`04 §7.6` (S10 hesap ayarları `/settings`) sayfası baştan yazıldı. T85 frontend skeleton'unun bıraktığı `<div>Settings</div>` stub'ı tam fonksiyonel ayarlar ekranıyla değiştirildi. Backend (T34/T35/T36) `users/me/settings` ailesindeki 12 endpoint zaten production'daydı — T94 yalnız frontend wiring + UI + i18n.

Sayfa dört bölüm halinde sıralı render edilir:

1. **NotificationPreferencesSection** — Platform içi (her zaman açık, devre dışı bırakılamaz), Email (toggle + adres input + doğrulama kodu akışı), Telegram (toggle, sadece bağlıysa aktif), Discord (toggle, sadece bağlıysa aktif).
2. **LinkedAccountsSection** — Telegram (modal: kod + bot link + "Kontrol Et"), Discord (OAuth redirect → callback banner). Bağlıyken her ikisi de "Bağlantıyı Kaldır" butonu.
3. **LanguagePreferenceSection** — 4 dil dropdown (EN/中文/ES/TR). Değişiklik U8'e persist edilir + `router.replace` ile yeni locale prefix'ine yönlendirir.
4. **AccountManagementSection** — "Hesabı Deaktif Et" (gri) + "Hesabı Sil" (kırmızı). Her iki akış için `<dialog>` onay modal'ı; sil için "SİL" verbatim input zorunlu.

**Email doğrulama akışı (`04 §7.6` 4-adım):**

1. Kullanıcı email adresini input'a yazar → "Kaydet" → `PUT /users/me/settings/notifications` `{email: {address}}`.
2. Adres backend'de saklanır, `verified=false` ile döner → "Email henüz doğrulanmadı" amber rozeti gösterilir.
3. "Doğrulama Kodu Gönder" butonu → U15 `/email/send-verification` → backend masked address ile dönen `sentTo` UI'da gösterilir + verify form açılır.
4. Kullanıcı kodu girer → "Doğrula" → U16 `/email/verify` → başarı → `queryClient.invalidate` → `verified=true` yeşil rozet.

**Telegram bağlama akışı (`04 §7.6` 5-adım):**

1. LinkedAccounts "Bağla" butonu → U9 `/telegram/connect` → response `{verificationCode, botUrl, expiresIn}` → `TelegramConnectModal` açılır.
2. Modal'da kod + bot link + 4-adım talimat gösterilir. "Telegram Bot'u Aç" linki `target="_blank"` ile bot'a yönlendirir.
3. Kullanıcı bot'a `/start <kod>` komutunu gönderir (backend `W1` webhook handler'da kod doğrulanır + bağlantı kurulur).
4. Kullanıcı modal'da "Kontrol Et" butonuna basar → `getAccountSettings()` çağrısı + `queryClient.setQueryData` ile cache güncellenir.
5. `connected=true` ise modal kapanır + UI'da bağlı durumu görsel; aksi durumda "Henüz bağlantı görünmüyor" amber notice. **K1 (SignalR `TelegramConnected` push)** T96 forward-deferred.

**Discord OAuth akışı (`04 §7.6` + `07 §5.13`):**

1. LinkedAccounts "Bağla" → U10 `/discord/connect` → `{discordAuthUrl}` döner → `window.location.assign(discordAuthUrl)`.
2. Discord OAuth ekranında kullanıcı yetkilendirir → backend callback (U10b `/discord/callback`) → `/settings?discord=connected` veya `/settings?discord=error&reason=...` redirect.
3. Sayfa mount'unda `captureDiscordCallback()` lazy `useState` initializer (T93 re-auth pattern'i ile birebir) query param'ı parse eder + banner gösterir.
4. `useEffect` ile `router.replace()` query param'ı URL'den siler (history'de leak önleme, T93 paterni).
5. 6 status kod (`connected`, `denied`, `already_linked`, `expired`, `exchange_failed`, `invalid_state`) + `error` fallback 4-locale lokalize banner ile gösterilir.

**Hesap Deaktif Et / Sil akışı (`04 §7.6` + `07 §5.17`):**

- Backend aktif işlem kontrolünü zorunlu kılıyor — UI ön-kontrol yapmaz (race-free, tek kaynak). 422 `HAS_ACTIVE_TRANSACTIONS` döndüğünde modal içinde Türkçe lokalize hata mesajı gösterilir.
- Deaktif: tek-buton onay modal'ı + "Deaktif Et" → U13 → cookie backend tarafından silinir, `useAuthStore.logout()` + `localStorage.removeItem("access_token")` + `router.replace("/{locale}")`.
- Sil: ciddi uyarı modal + **"SİL" verbatim text input** (backend `UsersController.cs:496` `Confirmation == "SİL"` bekliyor; lokalize edilmez — tüm 4 dilde sabit; `DELETE_ACCOUNT_CONFIRMATION` const ile API client'ta belgelendi) → submit disabled until match → "Hesabı Sil" → U14 → aynı redirect davranışı.

**Dil tercihi akışı (`04 §7.6` + `07 §5.10`):**

1. `<select>` dropdown 4 dil seçeneği — current = `settings.language` (sunucudan gelir; bozuk olursa "tr" fallback).
2. Seçim değişikliği → U8 `/settings/language` → backend persist.
3. `localStorage.preferredLocale` güncellenir (header `LanguageSelector` ile aynı kalıp).
4. `router.replace` ile URL path'i mevcut locale prefix'i yeni dile çevirir (örn. `/en/settings` → `/tr/settings`).

**API client (`lib/api/settings.ts` yeni):**

12 fonksiyon + 10 DTO interface — U6/U7/U8/U9/U10/U11/U12/U13/U14/U15/U16 birebir, `07 §5.6–§5.17` ile 1:1 eşleşme. `DELETE_ACCOUNT_CONFIRMATION = "SİL"` const ile lokalize edilmemesi gerektiği kodda belgelenmiş.

**Hook (`lib/hooks/useAccountSettings.ts` yeni):**

`useQuery` wrapper, `queryKey: ["users","me","settings"]`, staleTime 60s, 401 retry-storm guard (`useMyProfile` paterni mirror). Mutation'lar bu key'e `invalidateQueries` ile uğrar.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (9):**

- `frontend/src/app/[locale]/(main)/settings/page.tsx` (S10 sayfa + Discord callback handler)
- `frontend/src/lib/api/settings.ts` (12 endpoint client + 10 DTO)
- `frontend/src/lib/hooks/useAccountSettings.ts` (U6 hook)
- `frontend/src/components/settings/NotificationPreferencesSection.tsx` (bildirim tercihleri + email verify)
- `frontend/src/components/settings/LinkedAccountsSection.tsx` (Telegram + Discord bağla/kaldır)
- `frontend/src/components/settings/TelegramConnectModal.tsx` (kod + bot link + "Kontrol Et")
- `frontend/src/components/settings/LanguagePreferenceSection.tsx` (dil dropdown + U8 persist + locale redirect)
- `frontend/src/components/settings/AccountManagementSection.tsx` (deaktif + sil modal'ları + SİL input)
- `frontend/src/components/settings/index.ts` (barrel export)
- `Docs/TASK_REPORTS/T94_REPORT.md` (bu rapor)

**Değişen dosyalar (5):**

- `frontend/src/i18n/messages/en.json` (+95 leaf-key, `settings` namespace)
- `frontend/src/i18n/messages/tr.json` (+95 leaf-key)
- `frontend/src/i18n/messages/es.json` (+95 leaf-key)
- `frontend/src/i18n/messages/zh.json` (+95 leaf-key)
- `Docs/IMPLEMENTATION_STATUS.md` (T94 satırı ⬚ Bekliyor → ⏳ Devam ediyor)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bildirim tercihleri: platform içi, email (toggle+input), Telegram (toggle + bağlama akışı), Discord (toggle + OAuth) | ✓ | `NotificationPreferencesSection.tsx` 4 satır: platform (locked, `canDisable=false` server enforce) + email (toggle + adres input + verify flow) + telegram (toggle disabled iff `!connected`) + discord (toggle disabled iff `!connected`). Backend U6 4-kanal şeması (`SettingsDtos.cs:5-22`) ile 1:1 eşleşme. |
| 2 | Dil tercihi (dropdown) | ✓ | `LanguagePreferenceSection.tsx` 4 dil `<select>` dropdown → U8 persist + localStorage update + `router.replace` ile locale prefix yenileme. `LanguageSelector` (C10 header) ile aynı pattern. |
| 3 | Telegram bağlama: doğrulama kodu + bot link | ✓ | `LinkedAccountsSection.tsx` "Bağla" → U9 `/telegram/connect` → `TelegramConnectModal.tsx` (kod + bot link + 4-adım talimat + "Kontrol Et" buton). Backend response `{verificationCode, botUrl, expiresIn}` 07 §5.11 birebir. |
| 4 | Discord bağlama: Discord OAuth | ✓ | `LinkedAccountsSection.handleDiscordConnect` → U10 `/discord/connect` → `window.location.assign(discordAuthUrl)`. Callback `/settings?discord=connected|error&reason=...` page-level `captureDiscordCallback()` lazy useState init + 6 reason code 4-locale banner + `router.replace` URL temizleme. |
| 5 | Hesabı deaktif et / sil modal'ları | ✓ | `AccountManagementSection.tsx` iki ayrı `<dialog>` modal — deaktif (tek-buton onay) + sil (ciddi uyarı + "SİL" input). Her ikisinde başlık + açıklama + submit + vazgeç. |
| 6 | Hesap sil: "SİL" yazarak onay | ✓ | `ConfirmModalBody.handleSubmit` `phrase !== DELETE_ACCOUNT_CONFIRMATION` → early return (submit bloke). `DELETE_ACCOUNT_CONFIRMATION = "SİL"` const `settings.ts:182`, tüm 4 dilde sabit (`accountManagement.delete.confirmLabel` `"{phrase}"` placeholder ile). Backend `UsersController.cs:496` `"SİL"` verbatim doğrular. |
| 7 | Aktif işlem kontrolü (deaktif/sil engeli) | ✓ | Backend U13/U14 422 `HAS_ACTIVE_TRANSACTIONS` döndürür (`AccountLifecycleErrorCodes.HasActiveTransactions`). Client `AccountManagementSection.handleConfirm` `ApiError.code === "HAS_ACTIVE_TRANSACTIONS"` → `t("errors.hasActiveTransactions")` lokalize banner (modal içinde, submit FAIL'inde gösterilir, kullanıcı modal'ı kapatabilir). UI ön-kontrol yapmaz — race-free single source of truth. |

**Doğrulama kontrol listesi (`11_IMPLEMENTATION_PLAN.md` T94):**

- [x] **04 §7.6 tüm ayarlar ve modal'lar var mı?** — Evet. 04 §7.6 bölümleri: 1) Bildirim Tercihleri 4-kanal tablosu ✓ 2) Bağlı Hesaplar tablosu (Telegram, Discord) ✓ 3) Telegram bağlama 5-adım ✓ 4) Dil Tercihi dropdown ✓ 5) Hesabı Deaktif Et 4-adım ✓ 6) Hesabı Sil 5-adım + "SİL" input ✓.

## Test Sonuçları

**Test beklentisi:** Yok (`11_IMPLEMENTATION_PLAN.md` T94: "Test beklentisi: Yok"). Frontend henüz test runner içermez; UI doğrulaması validator chat'inde manuel smoke testle yapılır.

| Tür | Sonuç | Komut |
|---|---|---|
| TypeScript | ✓ | `npx tsc --noEmit` → exit 0 |
| ESLint | ✓ | `npx eslint src/app/[locale]/(main)/settings src/components/settings src/lib/api/settings.ts src/lib/hooks/useAccountSettings.ts` → exit 0 |
| Prettier | ✓ | `npx prettier --check` → "All matched files use Prettier code style!" |
| Build | ✓ | `npx next build` → ✓ Compiled successfully + `/[locale]/settings` route üretildi |
| i18n parity | ✓ | leaf-key sayımı en:622 / tr:622 / es:622 / zh:622 (settings = 95 leaf × 4 locale = 380 yeni) |

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only task).
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni prod dep:** Yok (mevcut next, react, next-intl, @tanstack/react-query, zustand yeterli).

## Commit & PR

- Branch: `task/T94-account-settings`
- Commit: `67e8372` "T94: Hesap ayarları (S10) — bildirim tercihleri + bağlı hesaplar + dil + hesap yönetimi"
- PR: [#141](https://github.com/turkerurganci/Skinora/pull/141)

## Known Limitations / Follow-up

- **K1 — SignalR `TelegramConnected` push yok:** Telegram bağlama onayı için kullanıcı manuel "Kontrol Et" butonuna basmak zorunda. Backend webhook (W1) bağlantıyı kurduğunda SignalR push gönderilmesi T96'da planlanmış; o zaman modal otomatik kapanır. Şimdiki UX kabul edilebilir: 4-adım talimatın son adımı zaten "Kontrol Et" tıklama.
- **K2 — SignalR `DiscordConnected` push yok:** Discord callback redirect query param'ı ile durum bildirildiği için bağlama doğal akışı SignalR'a ihtiyaç duymaz; ancak başka bir tab/cihazda yapılan bağlantı bu sayfada görünmez. T96 devir.
- **K3 — Verification kodu countdown UI yok:** Backend `expiresIn` saniye olarak döner (email 600s, telegram 300s) ama UI sadece "Kod 300 saniye geçerli" gibi static text gösterir. T-future iyileştirme: `CountdownTimer` shared util (C16) ile gerçek-zamanlı geri sayım.
- **K4 — Email cooldown saniye fallback yok:** `VERIFICATION_COOLDOWN` 429 backend response'unda muhtemelen `Retry-After` header'ı var; mevcut `apiClient` header'ları yutar. Şimdilik UI sadece "Bir süre bekleyip tekrar deneyin" diyor. T-future: header'ı UI'a expose edip dynamic süre göster.
- **K5 — Language change full reload yapmaz:** `router.replace(targetPath)` next.js client-side navigation kullanır; next-intl provider yeniden mount edilir ama React Query cache eski locale anahtarlarını korur (settings/transactions/notifications). 60s staleTime sonrası refresh edilir; manuel `window.location.assign` kullanmak alternatif (header `LanguageSelector` `window.location.assign` kullanıyor — drift'i sonradan tek noktaya getirmek opsiyonel). T-future.
- **K6 — Telegram modal `expiresIn` countdown yok:** Modal açıkken kodun gerçek süresi 5dk sonra dolar; UI sadece başlangıç saniyesini gösterir. Kullanıcı 5dk içinde tamamlamazsa modal'ı yeniden açıp yeni kod almak zorunda. T-future iyileştirme: countdown + "Kod süresi doldu, yenilemek için modal'ı kapatıp tekrar açın" prompt.
- **K7 — `next/image` yerine `<img>` yok:** T94 görsel asset eklemiyor — Discord ve Telegram logoları SVG/text-only kullanılmadı; sade label butonlar tercih edildi. Görsel iyileştirme T-future.

## Notlar

- **Working tree pre-check (Adım -1):** Başlangıçta `git status --short` boş ✓.
- **Adım 0 main CI:** Son 3 main run hepsi `success` (T93 PR #140 [26333984865](https://github.com/turkerurganci/Skinora/actions/runs/26333984865), T93 docker [26333984859](https://github.com/turkerurganci/Skinora/actions/runs/26333984859), T92 PR #139 [26331943296](https://github.com/turkerurganci/Skinora/actions/runs/26331943296)) ✓.
- **Dış varsayım kontrolü:** Tüm 12 backend endpoint'i (U6/U7/U8/U9/U10/U10b/U11/U12/U13/U14/U15/U16) production'da (`UsersController.cs:148-510` doğrulandı). C10 `LanguageSelector` ve `CancelModal` paterni mevcut, kalıp birebir uygulandı. Backend U14 `Confirmation == "SİL"` verbatim (`UsersController.cs:496`); UI tüm 4 dilde sabit metin gösterir.
- **Proje sahibi onayı (2026-05-23):** 3 karar noktasında onay alındı: (1) **Telegram polling = Manuel refresh + "Kontrol Et" buton** (Recommended) — T96 SignalR forward-deferred; (2) **Discord callback = query'den oku + banner + URL temizle** (Recommended) — T93 re-auth token pattern'i ile birebir; (3) **"SİL" tüm dillerde sabit** (Recommended) — backend verbatim kontrol ediyor, lokalize edilirse payload mismatch.
- **DELETE_ACCOUNT_CONFIRMATION const:** Backend bekleneni `settings.ts:182` `export const DELETE_ACCOUNT_CONFIRMATION = "SİL"` ile dokumante edildi + comment ile `UsersController.cs:496` referansı verildi. Locale dosyalarında `accountManagement.delete.confirmLabel` `{phrase}` placeholder ile gelir; component `t("delete.confirmLabel", { phrase: DELETE_ACCOUNT_CONFIRMATION })` çağrısı ile UI'a "SİL" inject edilir → çevirmen "SİL" verbatim'ı görmez, lokalize edilmesi yanlışlıkla mümkün değil.
- **`<dialog>` modal pattern:** `CancelModal.tsx` (T19 pattern) + `DisputeModal.tsx` (T92 pattern) ile birebir — `useEffect` ile `dialog.showModal()/close()` + `cancel` event handler + body component'i separate edip `open && payload` guard ile yalnız aktifken render.
- **Email address change soft re-verify reset:** Email adresini değiştirip kaydederken backend `verified=false` set eder (T35 davranışı). UI bunu otomatik fark eder (`emailAddressChanged` derived state) ve "Doğrulama Kodu Gönder" butonunu gizler (kullanıcı önce kaydetmeli) — yeniden gösterir kayıt sonrası fetch'te.
- **Pre-existing C10 LanguageSelector vs LanguagePreferenceSection drift:** Header'daki `LanguageSelector` `window.location.assign` ile full reload yaparken T94 `LanguagePreferenceSection` `router.replace` ile soft navigation kullanır. Drift T-future K5; şimdilik kabul edilebilir çünkü settings sayfasında zaten React Query state cache az ve 60s staleTime sonrası yenilenir.

## Doğrulama

**Validator chat (bağımsız, 2026-05-23):** ✓ PASS — 0 bulgu, 1 minor advisory.

| Kontrol | Sonuç | Kanıt |
|---|---|---|
| Adım -1 — Working tree hygiene | ✓ | `git status --short` boş |
| Adım 0 — Main CI son 3 run | ✓ | 26333984865 success / 26333984859 success / 26331943296 success |
| Adım 0b — Repo memory T94 satırı | ✓ | `.claude/memory/MEMORY.md` line 223 mevcut |
| Kabul kriterleri 1–7 | ✓ | 7/7 karşılandı, kanıtlar §"Kabul Kriterleri Kontrolü" tablosunda |
| 04 §7.6 doğrulama maddesi | ✓ | Tüm 6 bölüm (bildirim 4-kanal / bağlı hesaplar / Telegram 5-adım / dil / deaktif / sil "SİL") implement |
| Backend "SİL" verbatim | ✓ | `AccountLifecycleService.cs:21` `DeleteConfirmationPhrase = "SİL"` Ordinal compare; client const `settings.ts:182` aynı string |
| TypeScript | ✓ | `npx tsc --noEmit` exit 0 |
| ESLint | ✓ | `npx eslint src/**/*.{ts,tsx}` exit 0 (0 warning, 0 error) |
| Prettier (T94 dosyaları) | ✓ | `npx prettier --check` T94 dosyaları üzerinde "All matched files use Prettier code style!"; tüm src 125 pre-existing drift (main baseline 129 → T94 sonrası 125, regresyon yok) |
| Next build | ✓ | `npx next build` PASS, `/[locale]/settings` route üretildi |
| i18n parity | ✓ | en 622 / tr 622 / es 622 / zh 622, settings.* 95 leaf-key ×4 locale = 380 yeni |
| Task branch CI | ✓ | run [26336582873](https://github.com/turkerurganci/Skinora/actions/runs/26336582873) success (commit `37d9c11`) |
| Güvenlik — secret sızıntısı | ✓ | Yok |
| Güvenlik — auth | ✓ | Sayfa `isAuthenticated` guard + backend `[Authorize(Policy = AuthPolicies.Authenticated)]` defense-in-depth |
| Güvenlik — input validation | ✓ | Confirmation phrase backend Ordinal compare; email backend tarafında validate; client trim'den fazlasını yapmaz |
| Yeni dış bağımlılık | ✓ | Yok |

**Minor advisory (S1 değil, kayıt amaçlı):**

- **A1 — Endpoint count cosmetic drift:** Rapor + memory "12 endpoint client" diyor; `settings.ts` aslında 11 HTTP wrapper barındırıyor (U6, U7, U8, U9, U10, U11, U12, U13, U14, U15, U16). U10b `/discord/callback` backend redirect endpoint'i client HTTP çağrısı değil — `captureDiscordCallback()` URL query handler ile karşılanır; 12 sayısı bu URL handler'ı sayarsa doğru. İmplementasyon eksiği yok; kabul kriterlerini etkilemez.

**Yapım raporu karşılaştırması:** Tam uyumlu, uyuşmazlık yok. Kabul kriterleri tablosu validator bağımsız okumasıyla 1:1 örtüşür.
