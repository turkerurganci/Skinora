# T86 — Landing Page (S01)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-20

---

## Yapılan İşler

`04 §6.1 (S01 Landing Page)` tanımı doğrultusunda halka açık landing page implement edildi. T63a backend P1/P2 endpoint'leri (`GET /platform/stats`, `GET /platform/maintenance`) tüketen ilk frontend müşterisi; T84 ortak bileşen kütüphanesi (C08 MaintenanceBanner, C10 LanguageSelector) ve T85 chrome (Footer) yeniden kullanıldı.

- **HeroSection** — değer önerisi başlık + alt başlık + "Steam ile Giriş" CTA. CTA `<Link href="/{locale}/auth/login">` (T87 implement edecek). Bakım disable durumunda `<button disabled>` varyantı + `ctaDisabledHint` status mesajı.
- **HowItWorks** — 4 adımlı görsel akış (`<ol>` numaralı liste, semantic): satıcı başlatır → eşya emanete alınır → alıcı ödeme gönderir → otomatik teslim/ödeme. Her adımda emoji ikon + sıra numarası + başlık + açıklama. Responsive grid (`grid-cols-1 sm:grid-cols-2 lg:grid-cols-4`).
- **TrustSignals** — `usePlatformStats` ile P1 endpoint'inden `totalCompletedTransactions` + `platformUptimePercent` çeker. 3 kart: tamamlanan işlem sayısı, uptime %, otomatik doğrulama (sabit metin). `isError` → bölüm tamamen gizlenir (graceful degradation, proje sahibi onayı). Veri yüklenirken pulse skeleton.
- **MaintenanceGate** — `usePlatformMaintenance` ile P2 endpoint'i tüketir, C08 MaintenanceBanner'ı doğru varyantla (PLANNED/PLATFORM/STEAM/BLOCKCHAIN → planned/active/steamOutage/blockchainDegradation) render eder; `plannedEnd` ISO datetime'ı `toLocaleString(locale)` ile formatlayıp `scheduledAt` prop'una geçer. `ctaDisabled` render-prop kanalı ile `HeroSection`'a `type === "PLATFORM_MAINTENANCE"` durumunu bildirir (07 §10.2 semantik: PLANNED tam işlevsel, sadece aktif bakım CTA'yı kapatır — proje sahibi onayı).
- **platform.ts API wrapper** — `getPlatformStats`, `getPlatformMaintenance` typed fonksiyonlar. `PlatformStats`, `PlatformMaintenance`, `MaintenanceType` type'ları 07 §10 sözleşmesine birebir.
- **usePlatformStats hook** — `useQuery` 15 dk `staleTime` + `gcTime` (P1 cache TTL'ine hizalı, 07 §10.1).
- **usePlatformMaintenance hook** — `useQuery` 30 sn `staleTime` + `gcTime` (P2 cache TTL'ine hizalı, 07 §10.2).
- **i18n** — 4 dil (en/tr/zh/es) `landing` namespace yeniden yapılandırıldı: `hero.{title,subtitle,cta,ctaDisabledHint}`, `howItWorks.{title,steps.{sellerStarts,itemEscrowed,buyerPays,autoSettle}.{title,description}}`, `trust.{title,totalTransactions,uptime,automation.{title,body}}`. Eski `landing.title`/`landing.subtitle` (T13 placeholder) kaldırıldı; başka kullanıcı yok.
- **Page render** — `[locale]/page.tsx` `"use client"` (MaintenanceGate render-prop client→client sınırı için), Footer T85'ten reuse. `(main)` layout grubuna girmez — landing kendi shell'ini yönetir.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (8):**

- `frontend/src/components/landing/HeroSection.tsx`
- `frontend/src/components/landing/HowItWorks.tsx`
- `frontend/src/components/landing/TrustSignals.tsx`
- `frontend/src/components/landing/MaintenanceGate.tsx`
- `frontend/src/components/landing/index.ts`
- `frontend/src/lib/api/platform.ts`
- `frontend/src/lib/hooks/usePlatformStats.ts`
- `frontend/src/lib/hooks/usePlatformMaintenance.ts`

**Güncellenmiş dosyalar (5):**

- `frontend/src/app/[locale]/page.tsx` — T13 stub → tam S01 implementasyonu
- `frontend/src/i18n/messages/en.json` — `landing` namespace yeniden yapılandırıldı
- `frontend/src/i18n/messages/tr.json` — aynı şema, Türkçe
- `frontend/src/i18n/messages/zh.json` — aynı şema, 中文
- `frontend/src/i18n/messages/es.json` — aynı şema, Español

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hero section, "Nasıl Çalışır", güven göstergeleri, footer | ✓ | `page.tsx`: `<HeroSection>` + `<HowItWorks>` + `<TrustSignals>` + `<Footer>`. `npm run build` 18 route compile. |
| 2 | `GET /platform/stats` çağrısı (15dk cache) | ✓ | `usePlatformStats.ts:8-15` `staleTime: 15 * 60 * 1000` + `gcTime` aynı. `platform.ts:30-32` `apiClient<PlatformStats>("/platform/stats")`. |
| 3 | `GET /platform/maintenance` → bakım durumu gösterimi | ✓ | `usePlatformMaintenance.ts:9-15` + `MaintenanceGate.tsx:25-31` `<MaintenanceBanner variant={...} message scheduledAt>`. `VARIANT_MAP` 4 type → C08 4 varyant. |
| 4 | Bakım state: C08 banner aktif, CTA devre dışı | ✓ | `MaintenanceGate.tsx:21` `ctaDisabled = data?.active && data.type === "PLATFORM_MAINTENANCE"`. `HeroSection.tsx:27-43` `ctaDisabled` ise `<button disabled aria-disabled>` + `ctaDisabledHint`. 07 §10.2 semantik notu (PLANNED tam işlevsel) korunur — proje sahibi onayı. |

## Test Sonuçları

Plan "Test beklentisi: Yok" (F5 frontend task'ları, E2E T107+ devirli).

| Tür | Sonuç | Detay |
|---|---|---|
| `npm run build` | ✓ | 18 route, `/[locale]` Dynamic (landing), Next 16.2.3 Turbopack, TypeScript Finished 2.4s |
| `npm run lint` | ✓ | 0 error (ESLint flat config) |
| `npx prettier --check` (T86 dosyaları) | ✓ | 0Δ — yeni dosyalar + 5 güncellenmiş prettier uyumlu |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekleniyor (validate chat) |
| PR | #131 |

## Doğrulama Kontrol Listesi (plan)

- [x] 04 §6.1 tüm bölümler var mı? → Hero ✓, Nasıl Çalışır 4 adım ✓, Güven Göstergeleri 3 kart ✓, Footer ✓ (T85 reuse)

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (`NEXT_PUBLIC_API_URL` T13'ten beri mevcut)
- Docker değişikliği: Yok
- Yeni dış bağımlılık: Yok (`@tanstack/react-query` + `next-intl` + `next` mevcut)

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok (API URL env'den, anonim public endpoint'ler — 07 §10 Auth: Public)
- **Auth/authorization etkisi:** Yok (S01 public, backend `[AllowAnonymous]` + `RateLimit("public")`)
- **Input validation:** Yok (kullanıcı input'u yok, sadece read-only çağrılar)
- **Yeni dış bağımlılık:** Yok (package.json değişmedi)

## Commit & PR

- Branch: `task/T86-landing-page`
- Commit: `1088c2a` — T86: Landing page (S01)
- PR: #131
- CI: ✓ PASS — run [`26178654662`](https://github.com/turkerurganci/Skinora/actions/runs/26178654662) (HEAD `6cfdb43`, 2026-05-20). Önceki run [`26178397649`](https://github.com/turkerurganci/Skinora/actions/runs/26178397649) (`1088c2a`) rapor commit push'u tarafından concurrency-cancel edildi (task.md "Concurrency notu" beklenen davranış).

## Mimari Kararlar (Notlar)

1. **Bakım CTA disable yalnız PLATFORM_MAINTENANCE'te (proje sahibi onayı):** 07 §10.2 semantik notu açıkça PLANNED_MAINTENANCE'i "platform tam işlevsel, yalnız bilgilendirme" diye tarif eder; STEAM_OUTAGE ve BLOCKCHAIN_DEGRADATION Steam login akışını doğrudan engellemez (callback pipeline 04 §6.2 zaten geo/ToS/MA gate'lerine sahip). Plan'ın "Bakım state: CTA devre dışı" ifadesi generic — 07 §10.2 nuance'ına saygılı yorum.
2. **Auth redirect T87'ye bırakıldı (proje sahibi onayı):** Auth-store `displayName` field'ı T29'da set ediliyor ama T85 itibariyle T87 öncesi gerçek auth flow yok. T86'da client-only redirect FOUC + yanıltıcı; T87 auth ekranlarında bütüncül kapatılacak (04 §6.1 "Giriş yapmış kullanıcı `/` adresine gelirse S05'e yönlendirilir" kabulü T87 forward-devir).
3. **Stats fail → TrustSignals gizle (proje sahibi onayı):** Landing'in birincil fonksiyonu CTA; stats opsiyonel. Hata mesajı public landing için "soğuk karşılama" — sessizce kart bölümü kaldırılır.
4. **MaintenanceGate render-prop pattern + page client component:** Server component → client function-as-children serileştirme problemi (Next 16). En sade çözüm page'i client yapmak; layout zaten yok, ek maliyet sıfır.
5. **i18n eski `landing.title`/`landing.subtitle` kaldırıldı:** CLAUDE.md "no backwards-compat hacks" — T13 placeholder kullanan başka tüketici yok, doğrudan silindi.

## Known Limitations / Follow-up

- **K1 — Auth redirect T87 devirli:** Login olmuş kullanıcı `/` adresine geldiğinde otomatik dashboard redirect 04 §6.1 zorunluluğu. T87 auth ekranlarında bütüncül kapatılacak.
- **K2 — Steam Login CTA `/auth/login` route'u T87'de açılır:** Bu PR'da CTA `<Link>` yalnız placeholder, route 404'lar.
- **K3 — Pre-existing prettier drift (T84/T85 dönemi 60+ dosya):** `npm run format` global çağrısı bu drift'leri yakaladı, ancak T86 PR'ına bundled edilmedi (bundled-PR yasağı). Ayrı chore PR'a aittir (T64-T76 sidecar drift paterni).
- **K4 — TrustSignals "uptime" 99.9% sabit veri:** Backend `PlatformPublicService` T63a'da `platformUptimePercent` sabit dönüyor (gerçek SLA telemetry T-future). Frontend kontratı doğru, gösterim doğru — backend devirli.
- **K5 — Pulse skeleton 3 kart yerine grid-flex shift:** TrustSignals veri gelirken value placeholder pulse `<span>` boyutu `w-24` sabit — büyük sayılarda micro-shift olabilir. F6 visual QA tarama.
