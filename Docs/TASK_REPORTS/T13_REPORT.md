# T13 — Next.js Frontend İskeleti

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-09

---

## Yapılan İşler
- Next.js 16 App Router projesi oluşturuldu (TypeScript, Tailwind CSS)
- `[locale]` route grupları kuruldu: `(auth)`, `(main)`, `admin/`
- 09 §4.3 klasör yapısı oluşturuldu: `components/ui/`, `components/features/`, `lib/api/`, `lib/hooks/`, `lib/signalr/`, `lib/stores/`, `lib/utils/`, `types/`, `i18n/`
- API client yazıldı: `lib/api/client.ts` — fetch wrapper, `ApiResponse<T>` unwrap, `ApiError` class, Bearer token (07 §2.4 envelope)
- State management kuruldu: TanStack Query (QueryClientProvider) + Zustand (auth-store)
- i18n kuruldu: next-intl, 4 dil (EN, ZH, ES, TR), fallback EN, middleware routing
- 23 TypeScript enum tanımı yazıldı: `types/enums.ts` — 06 §2 birebir eşleşme
- SignalR client setup: `lib/signalr/connection.ts` — HubConnectionBuilder, auto-reconnect
- ESLint + Prettier konfigüre edildi (eslint-config-prettier entegrasyonu)
- Dockerfile güncellendi: multi-stage build (deps → builder → runner), standalone output
- Health check API route eklendi: `/api/health`
- CI workflow güncellendi: frontend lint + frontend build adımları

## Etkilenen Modüller / Dosyalar

### Oluşturulan dosyalar (frontend/)
- `src/app/globals.css` — Tailwind import
- `src/app/not-found.tsx` — 404 sayfası
- `src/app/api/health/route.ts` — Health check endpoint
- `src/app/[locale]/layout.tsx` — Root layout (i18n, providers)
- `src/app/[locale]/page.tsx` — Landing page
- `src/app/[locale]/(auth)/layout.tsx` — Auth layout
- `src/app/[locale]/(auth)/callback/page.tsx` — Steam callback
- `src/app/[locale]/(main)/layout.tsx` — Main layout
- `src/app/[locale]/(main)/dashboard/page.tsx`
- `src/app/[locale]/(main)/transactions/page.tsx`
- `src/app/[locale]/(main)/transactions/new/page.tsx`
- `src/app/[locale]/(main)/transactions/[id]/page.tsx`
- `src/app/[locale]/(main)/profile/page.tsx`
- `src/app/[locale]/(main)/notifications/page.tsx`
- `src/app/[locale]/admin/layout.tsx` — Admin layout
- `src/app/[locale]/admin/dashboard/page.tsx`
- `src/app/[locale]/admin/transactions/page.tsx`
- `src/app/[locale]/admin/flags/page.tsx`
- `src/app/[locale]/admin/users/page.tsx`
- `src/app/[locale]/admin/settings/page.tsx`
- `src/app/[locale]/admin/roles/page.tsx`
- `src/app/[locale]/admin/audit-logs/page.tsx`
- `src/lib/api/client.ts` — API client
- `src/lib/signalr/connection.ts` — SignalR client
- `src/lib/hooks/useAuth.ts` — Auth hook
- `src/lib/stores/auth-store.ts` — Zustand auth store
- `src/lib/providers.tsx` — TanStack Query provider
- `src/lib/utils/format.ts` — Formatlama yardımcıları
- `src/types/api.ts` — ApiResponse, PagedResult types
- `src/types/enums.ts` — 23 enum (06 §2 birebir)
- `src/i18n/routing.ts` — next-intl routing config
- `src/i18n/request.ts` — next-intl server request config
- `src/i18n/messages/en.json` — İngilizce
- `src/i18n/messages/zh.json` — Çince
- `src/i18n/messages/es.json` — İspanyolca
- `src/i18n/messages/tr.json` — Türkçe
- `src/middleware.ts` — i18n middleware
- `.prettierrc` — Prettier config
- `Dockerfile` — Multi-stage Next.js standalone build

### Güncellenen dosyalar
- `frontend/package.json` — name, dependencies, scripts
- `frontend/eslint.config.mjs` — prettier entegrasyonu
- `frontend/next.config.ts` — next-intl plugin, standalone output
- `.github/workflows/ci.yml` — frontend lint + build adımları

### Silinen dosyalar
- `frontend/server.js` — placeholder

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Next.js App Router projesi oluşturuldu | ✓ | `npm run build` başarılı, 16 route |
| 2 | [locale] route grupları: auth, main, admin | ✓ | `(auth)`, `(main)`, `admin/` dizinleri mevcut |
| 3 | Klasör yapısı: components/ui/, components/features/, lib/api/, lib/hooks/, lib/signalr/, types/, i18n/ | ✓ | Tüm dizinler oluşturuldu |
| 4 | API client: fetch wrapper, ApiResponse<T>, ApiError, Bearer token | ✓ | `lib/api/client.ts` — 07 §2.4 envelope unwrap |
| 5 | State management: TanStack Query + Zustand | ✓ | `lib/providers.tsx` + `lib/stores/auth-store.ts` |
| 6 | i18n: next-intl, 4 dil, fallback EN | ✓ | 4 message dosyası, routing.ts defaultLocale: "en" |
| 7 | TypeScript enum'ları (C# karşılıkları): types/enums.ts | ✓ | 23 enum, 06 §2 birebir |
| 8 | SignalR client setup: lib/signalr/connection.ts | ✓ | HubConnectionBuilder, auto-reconnect |
| 9 | ESLint + Prettier konfigüre | ✓ | `npm run lint` temiz, `.prettierrc` mevcut |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ PASS | `npm run build` — 16 route, 0 error |
| Lint | ✓ PASS | `npm run lint` — 0 warning, 0 error |
| Unit | N/A | Task tanımında test beklentisi yok |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Doğrulama Kontrol Listesi
- [x] 09 §4.3 klasör yapısı eşleşiyor mu? — Evet (minor: admin/ explicit prefix vs (admin)/ route group — documented & justified)
- [x] API client 07 §2.4 envelope formatını unwrap ediyor mu? — Evet (success/data/error/traceId)
- [x] i18n 4 dil dosyası mevcut mu? — Evet (en.json, zh.json, es.json, tr.json)

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz — yalnızca NEXT_PUBLIC_ env vars
- [x] Auth etkisi: Temiz — placeholder token storage, gerçek auth T29'da
- [x] Input validation: N/A (iskelet)
- [x] Yeni bağımlılık: next-intl, @tanstack/react-query, zustand, @microsoft/signalr, eslint-config-prettier, prettier — tümü beklenen

### Yapım Raporu Karşılaştırması
- Uyum: Tam uyumlu — yapım raporundaki 9 kabul kriteri validator tarafından bağımsız doğrulandı

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: `NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_SIGNALR_URL` env değişkenleri eklendi
- Docker değişikliği: Dockerfile multi-stage build'e güncellendi

## Commit & PR
- Branch: `task/T13-nextjs-frontend-skeleton`
- Commit: `9ba24a3`
- PR: (merge sırasında)
- CI: (merge sırasında)

## Known Limitations / Follow-up
- Admin route'ları `(admin)` route group yerine `admin/` explicit prefix kullanıyor — Next.js App Router'da aynı path'e çözümlenen iki paralel route grubu desteklenmiyor. Bu 09 §4.3'teki `(admin)` notasyonundan küçük bir sapma ama fonksiyonel olarak doğru: admin sayfaları `/admin/*` altında erişilebilir.
- `middleware.ts` deprecation uyarısı: Next.js 16 "proxy" convention'ı önermiyor, ancak next-intl hâlâ middleware kullanıyor. next-intl güncellemesi ile çözülecek.
- Placeholder page içerikleri minimal — tam UI T84+ task'larında implement edilecek.

## Notlar
- next-intl v4 kullanıldı (App Router native desteği)
- `output: "standalone"` Docker optimizasyonu için aktif
- CI workflow'una `npm ci && npm run lint` (lint step) ve `npm ci && npm run build` (build step) eklendi
