# UI Tur Bulguları — Düzeltme Planı (2026-08-23)

**Kaynak:** [UI_TOUR_2026-08-23.md](UI_TOUR_2026-08-23.md) — 9 bulgu (3 🔴 · 3 🟡 · 3 ⚪)
**Durum:** öneri — görev numaraları ve sıralama **proje sahibi onayı** bekliyor

Her kalemin kabul kriteri, **bugün başarısız olan ve düzeltmeden sonra geçmesi gereken** bir ölçümle yazıldı. Tur sırasında kullanılan probe'lar tekrar koşulabilir durumda (`scratchpad/tour.cjs`, üç mod).

---

## Öncelik sırası ve gerekçesi

| Sıra | Kalem | Neden bu sırada |
|---|---|---|
| 1 | **F1 · B8** Trade URL alanı | Ürünün ana akışını açan **en küçük** iş: backend hazır, eksik olan tek bir form. Tek başına "işlem oluşturulamıyor"u kaldırmaz (F2 gerekir) ama F2 tek başına da yetmez |
| 2 | **F2 · B9** Envanter istemcisi | F1 ile birlikte ana akışı fiilen açar. Sırası ikinci çünkü F1'den büyük ve dış davranışa bağımlı |
| 3 | **F3 · B1** Rate limit + forwarded headers | Güvenlik **ve** işlevsellik; ayrıca kayıtlı 🔴'yı kapatır. Üç bağımsız parçası var, ayrı ayrı sevk edilebilir |
| 4 | **F4 · B7** Bildirim dili | Sessiz ve para yolunu etkiliyor (kaçırılan teslimat süresi → kusur ataması) |
| 5 | **F5 · B2** `/admin/users` | Admin menüsünde ölü link; operasyonel |
| 6 | **F6 · B3/B4/B5/B6** Küçük kalemler | Tek chore PR'ında toplanabilir |

> **Not:** F1 ve F2 **birlikte** teslim edilmezse ana akış hâlâ kapalı kalır. Ayrı PR'lar olabilir ama aynı sürümde çıkmalılar.

---

## F1 · B8 — Trade URL kaydı için UI (🔴)

**Kök neden:** `User.MobileAuthenticatorVerified` yalnız U17 (`PUT /users/me/settings/steam/trade-url`) tarafından yazılıyor; U17'yi çağıran hiçbir frontend kodu yok. Kullanıcı `/transactions/new` → *"MA aktif değil"* → `/auth/mobile-authenticator` → "yeniden kontrol et" (Steam'e sormuyor, sadece `/auth/me` okuyor) döngüsünde kilitleniyor.

**Önerilen düzeltme — iki yerleşim, tek bileşen:**

1. Yeni `SteamTradeUrlSection` bileşeni (`frontend/src/components/settings/`), mevcut bölüm desenini izler (`LanguagePreferenceSection`, `NotificationPreferencesSection` kardeşleri).
2. **Ayarlar** sayfasına eklenir — U17'nin yol adı (`/users/me/settings/…`) ve 07 §5.16a orayı işaret ediyor.
3. **Aynı bileşen `/auth/mobile-authenticator` sayfasına da** gömülür. Gerekçe: engellenen kullanıcı tam olarak oraya yönlendiriliyor; çözümü başka bir sayfada aratmak döngüyü kısmen sürdürür.

**Dokunulacak dosyalar:**
- `frontend/src/lib/api/users.ts` — `updateSteamTradeUrl()` (U17 çağrısı; `TradeUrlResponse` tipi zaten backend'de tanımlı)
- `frontend/src/components/settings/SteamTradeUrlSection.tsx` (yeni) + `index.ts`
- `frontend/src/app/[locale]/(main)/settings/page.tsx` — bölümü ekle
- `frontend/src/app/[locale]/auth/mobile-authenticator/page.tsx` — bölümü ekle, "recheck" metnini gerçeğe uydur
- `frontend/messages/{en,tr,es,zh}.json` — 4 dilde metinler

**Üç durum ele alınmalı** (backend zaten üçünü de döndürüyor):

| Backend cevabı | UI davranışı |
|---|---|
| `200` + `mobileAuthenticatorActive: true` | Başarı; `["auth","me"]` query invalidate → engel kalkar |
| `200` + `mobileAuthenticatorActive: false` + `setupGuideUrl` | "MA kapalı görünüyor" + Steam kurulum rehberi linki |
| **Pending** (`ApiAvailable=false`, 07 §5.16a) | "Steam şu an yanıt vermiyor, URL kaydedildi — tekrar dene". **Sessiz başarı gösterilmemeli** |

**Kabul kriterleri:**
- AC1 — Yeni bir kullanıcı, **yalnız arayüzü kullanarak** `mobileAuthenticatorActive: true` durumuna gelebilir
- AC2 — `/transactions/new` engeli kalkar ve sihirbazın 1. adımı çizilir
- AC3 — Geçersiz URL `INVALID_TRADE_URL` ile alanın altında gösterilir, toast'a düşmez
- AC4 — Steam erişilemezken "pending" durumu ayrı ve dürüst bir mesaj gösterir
- AC5 — Dört dilde metin var, i18n parity testi geçer

**Doğrulama (bugün başarısız olan ölçüm):**
```
/tr/transactions/new → form alanı sayısı 0, "MA aktif değil"
düzeltmeden sonra   → Ayarlar'dan URL kaydedilebilmeli, engel kalkmalı
```

**Boyut:** S/M · **Risk:** düşük (backend değişmiyor)

---

## F2 · B9 — Envanter istemcisinin değiştirilmesi (🔴)

**Kök neden:** `steamcommunity` paketinin `getUserInventoryContents` çağrısı Steam'den **429** alıyor; aynı konteynerden birebir aynı URL + `Referer` + `count=1000` ile düz `fetch` **200** dönüyor. Elenen hipotezler: User-Agent, URL biçimi, profil gizliliği, `STEAM_API_KEY`.

**Önerilen düzeltme:** `SteamCommunityInventoryAdapter`'ı doğrudan `fetch` ile sayfalayan bir uygulamayla değiştir. Kod bunu **kolaylaştıracak şekilde zaten ayrılmış**: `InventoryService` bir port arkasında çalışıyor (*"tests inject a stub without monkey-patching steamcommunity"*), yani değişim tek adaptörle sınırlı.

Korunması gerekenler: sayfalama (`start_assetid`), `403` + gövde `null` → **private** ayrımı, `429` → retry/backoff, Redis cache (120 sn), `RateLimitedQueue` bütçesi.

**Dokunulacak dosyalar:** `sidecar-steam/src/trade/InventoryService.ts` (adaptör) · testleri · `package.json` (paket bağımlılığı düşebilir)

**Kabul kriterleri:**
- AC1 — `GET /api/inventory/{steamId}` public bir envanter için **200** + item listesi
- AC2 — Private envanter **ayırt edilebilir** kod döndürür (`STEAM_UNAVAILABLE` değil)
- AC3 — Gerçek `429`'da retry/backoff çalışır ve hata **rate limit** olarak raporlanır
- AC4 — Sayfalama 1000'den fazla item'da doğru; cache davranışı korunur
- AC5 — Mevcut sidecar testleri geçer, yeni adaptör için birim testi eklenir

**Doğrulama:**
```
bugün: sidecar /api/inventory → 503 "HTTP error 429" (her denemede)
       aynı konteynerde düz fetch → 200 (her denemede)
sonra: sidecar /api/inventory → 200 + item listesi
```

**Boyut:** M · **Risk:** orta — dış servis davranışına bağlı, **F1'den sonra ölçülmeli**

---

## F3 · B1 — Rate limit ve istemci IP'si (🔴)

Üç bağımsız parça; ayrı sevk edilebilir ama **üçü birden** gerekli.

### F3a — `UseForwardedHeaders` (kayıtlı 🔴 `ForwardedHeadersNotRegistered`)

Backend'de kayıtlı değil → `RemoteIpAddress` her istekte nginx IP'si. **Tuzak:** `X-Forwarded-For`'a körü körüne güvenmek spoofing açar — `KnownProxies`/`KnownNetworks` docker ağıyla **sınırlanmalı**, yoksa istemci kendi IP'sini uydurup rate limit ve geo-block'u birlikte atlar.

- AC — Gerçek bir girişten sonra `UserLoginLogs.IpAddress` **istemci IP'si**, nginx IP'si değil
- AC — Sahte `X-Forwarded-For` taşıyan istek **kabul edilmez** (bilinmeyen proxy'den gelen header yok sayılır)

### F3b — `/auth/me` · `/refresh` · `/logout` kovadan çıkarılmalı

`AuthController` sınıf düzeyinde `[RateLimit("auth")]` taşıyor (10/60 sn) ve `me` için istisna yok. Bu üç uç **giriş denemesi değil**; oturum okuma/yenileme.

Öneri: `me` → `user-read` (kullanıcı bazlı, 60/dk) · `refresh` ve `logout` → kendi politikaları (kullanıcı/oturum bazlı). `steam` ve `steam/callback` `auth` kovasında **kalmalı** — brute-force yüzeyi orası.

- AC — Tek kullanıcı 20 tam sayfa yüklemesini üst üste yapabilir, 429 almaz
- AC — Giriş uçlarının brute-force limiti **korunur** (10/60 sn)
- AC — Kimliksiz istekler, giriş yapmış kullanıcıların oturum çağrılarını düşüremez

### F3c — 429 sessizce "yetkisiz" gibi görünmemeli

`AdminGuard` `/auth/me` 429 dönünce rolü çözemiyor ve kullanıcıyı dashboard'a atıyor.

- AC — 429/ağ hatası ile "admin değil" **ayrılır**; hata durumunda geri atma yerine yeniden dene/hata gösterilir

**Boyut:** F3a S · F3b S · F3c S · **Risk:** F3a orta (güvenlik ayarı — yanlış yapılandırma spoofing açar), diğerleri düşük

---

## F4 · B7 — Kayıt anında dil tercihi (🟡)

**Kök neden:** [UserProvisioningService.cs:32](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/UserProvisioningService.cs#L32) `PreferredLanguage = "en"` sabit; arayüz dili aktarılmıyor. Bu alan tüm bildirimlerin, itiraz yazışmalarının ve teslimat uyarılarının dilini belirliyor.

**Önerilen düzeltme:** giriş başlatılırken arayüz dili taşınır (OpenID `return_to`/state üzerinden) ve provisioning'e parametre olarak geçer; desteklenmeyen değer `en`'e düşer.

- AC1 — `/tr/auth/login`'den kayıt olan kullanıcının `PreferredLanguage` = `tr`
- AC2 — Desteklenmeyen/eksik dilde varsayılan `en`
- AC3 — Mevcut kullanıcıların tercihi **değişmez**
- AC4 — Ayarlar'daki dil kutusu ile arayüz dili ilk girişte tutarlı

**Boyut:** S/M · **Risk:** düşük · **Not:** state parametresine dokunulacağı için CSRF/state doğrulaması bozulmamalı

---

## F5 · B2 — `/admin/users` (🟡)

**Ölçüldü: backend hazır, iş tamamen frontend.** [AdminController.cs:158](../../backend/src/Skinora.API/Controllers/AdminController.cs#L158) — **AD15** `GET /admin/users`, imzası eksiksiz:

```csharp
ListUsers(string? search, Guid? roleId, int page = 1, int pageSize = 20)
  → PagedResult<AdminUserListItemDto>
```

Yani uç **planlanmış, yazılmış ve yetkilendirilmiş** (`PolicyManageRoles`, `admin-read` kovası); atlanan tek şey UI. Bu, "menüden kaldıralım mı?" seçeneğini büyük ölçüde geçersiz kılıyor — kaldırmak, çalışan bir backend yeteneğini gömmek olur.

**Öneri: uygula.** Gerekli ortak bileşenler galeride hazır ve çalışıyor (`FilterBar` C17 · `Pagination` C16 · `UserCard` C04 · `EmptyState` C13 · `Skeleton` C14).

- AC1 — Liste render olur, arama ve rol filtresi çalışır, sayfalama `PagedResult` ile uyumlu
- AC2 — Satırdan detay sayfasına (`/admin/users/{steamId}`) geçilir
- AC3 — Boş sonuç `EmptyState`, yükleme `Skeleton`, hata `ErrorState` ile
- AC4 — Dört dilde metin

**Boyut:** S/M (yalnız frontend) · **Risk:** düşük

---

## F6 · Küçük kalemler — tek chore PR'ı

| Bulgu | Düzeltme | Boyut |
|---|---|---|
| **B3** `/transactions` iskelet | Dashboard'daki `TransactionList`'i bu rotaya bağla **veya** rotayı kaldır. Ana menüde link olmadığı için kaldırmak da savunulabilir | XS/S |
| **B4** favicon yok | `app/icon.png` + `apple-icon.png` (Next.js App Router kuralı) | XS |
| **B5** starter artıkları | `frontend/public/` içindeki 5 şablon SVG'sini sil | XS |
| **B6** durum ekranları girişsiz açılıyor | Karar kalemi: bilgilendirme olarak bırak (mevcut) veya duruma bağla. **Düzeltme önermiyorum**, kayıt yeterli | — |

---

## Sevk planı

| PR | İçerik | Bağımlılık |
|---|---|---|
| PR-1 | **Rapor + 9 backlog satırı** (`DEFERRED_BACKLOG.md`) | yok — önce bu |
| PR-2 | F1 (Trade URL UI) | PR-1 |
| PR-3 | F2 (envanter istemcisi) | — (F1 ile paralel yürüyebilir, **aynı sürümde çıkmalı**) |
| PR-4 | F3a + F3b + F3c | — |
| PR-5 | F4 (dil tercihi) | — |
| PR-6 | F6 (küçük kalemler) | — |
| PR-7 | F5 (`/admin/users` liste sayfası) | — (backend hazır) |

**Backlog etkisi:** liste şu an **56 aktif / 58 çözülmüş, 🔴 1**. Dokuz satır eklenirse **65 aktif**, **🔴 4** olur.

---

## Düzeltmelerin doğrulaması

Turun probe'ları düzeltme sonrası **aynen** koşulabilir; her biri bugün başarısız olan bir ölçümü hedefliyor:

| Kalem | Bugün | Düzeltmeden sonra beklenen |
|---|---|---|
| F1 | `/transactions/new` form alanı **0**, "MA aktif değil" | Ayarlar'dan MA açılabilir, sihirbaz çizilir |
| F2 | sidecar envanter **503** `429` | **200** + item listesi |
| F3a | `UserLoginLogs.IpAddress` = `::ffff:172.20.0.5` | istemci IP'si |
| F3b | 11. istekte **429** (kimliksiz) | 20 sayfa yüklemesi temiz; giriş uçları hâlâ 10/60 |
| F3c | 429'da admin sayfası sessizce dashboard'a atıyor | hata gösterilir |
| F4 | `/auth/me` → `"language":"en"` (tr arayüzden kayıt) | `"tr"` |
| F5 | `/admin/users` metin uzunluğu **120** | liste render (veya menüden kalkmış) |
| F6 | `/favicon.ico` **404** | **200** |

**Ek olarak yapılmalı — bu turun asıl dersi:** yukarıdaki kontrollerin hiçbirini mevcut E2E ağı yakalamıyordu. F1/F2 kapandıktan sonra **gerçek oturumla en az bir uçtan uca akış** (kayıt → MA → item seç → işlem oluştur) otomatik ağa girmeli; aksi hâlde aynı sınıf açık yeniden birikir.
