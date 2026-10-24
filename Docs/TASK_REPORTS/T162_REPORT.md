# T162 — HTTPS + alan adı (getskinora.com)

**Faz:** F9 | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-24

Kaynak: 11 T162 (`Docs/plan/11_F9.md`), DEPLOY_RUNBOOK §K.5 "T162'ye kalanlar". Sahip kararları (2026-10-24): alan adı **`getskinora.com`** (`skinora.com` alınamadı, marka Skinora kalır); tünel `skinora-trial` ve token hazır; route `getskinora.com` → `http://skinora-reverse-proxy:80`; Cloudflare Access (`ev-ip` bypass + `sahip` allow, One-time PIN) yönetici tarafından API ile kuruldu.

---

## Yapılan İşler

- **nginx (`nginx/nginx.conf`):**
  - `set_real_ip_from 172.30.161.250` + `real_ip_header CF-Connecting-IP` (+ `real_ip_recursive off`): `$remote_addr` yalnız tünel bağlayıcısından gelen istekte ziyaretçi IP'si olur. `X-Real-IP` / `X-Forwarded-For` bunu taşır.
  - T166'nın `geo $skinora_trusted_edge`'i `172.20.0.0/16 − ağ geçidi` yerine yalnız `172.30.161.250/32` ve **`$realip_remote_addr`** ile (realip sonrası `$remote_addr` ziyaretçidir; güven kararını veremez).
  - `X-Forwarded-Proto`: bağlayıcıdan gelen `https` aynen, diğer her eşte nginx'in `$scheme`'i (`$skinora_forwarded_proto`, dört location).
  - Cloudflare'den düz `http` gelen istek → `301 https://$host$request_uri` (`$skinora_edge_plain_http`).
- **Neden tüm ağ değil tek adres:** görev metni "yalnız cloudflared'in ağı" diyordu; ağa güvenmek ağ geçidini (Docker Desktop'ta yerel `127.0.0.1:8090` istekleri `172.30.161.1`'den gelir — T166'da ölçüldü) ve aynı ağdaki her container'ı "Cloudflare" saydırırdı. Bu yüzden bağlayıcı sabit adrese alındı ve yalnız o güvenilir (öneriyle kaydedildi).
- **Trial overlay (`docker-compose.trial.yml`):** cloudflared `ipv4_address: 172.30.161.250`; ağ `subnet 172.30.161.0/24` + `ip_range 172.30.161.0/25` (dinamik adresler alt yarıdan, sabit adres başkasına verilemez); `ReverseProxy__KnownNetworks__0` ve alt ağ değişkensiz — `TRIAL_NETWORK_SUBNET` kalktı (`.env.example`'dan da): nginx değişken okuyamaz, alt ağ tek başına kayarsa istemci IP'si sessizce kaybolurdu.
- **Backend:** kod değişikliği yok — `UseForwardedHeaders` zaten `XForwardedFor | XForwardedProto`, güven `172.30.161.0/24`; çerezler zaten `Secure = true`. Yeni entegrasyon testi şemanın yalnız güvenilir proxy'den kabul edildiğini sabitler.
- **Alan adı:** dağıtım adresi `skinora.com` → `getskinora.com`: `appsettings.json` (Steam OpenID dört URL, Resend gönderen, Discord redirect), `ResendSettings` varsayılanı, `SteamOpenIdSettings` belgeleri, `docker-compose.yml` gönderen varsayılanları, `.env.example` (uyarı e-postası, Grafana/Resend gönderen), frontend `SUPPORT_EMAIL` (`support@getskinora.com`) + testleri, Shared testlerindeki örnek adresler. Belgeler: 07 (v4.28), 08 (v3.50), `plan/11_F9.md` (v1.32), INTEGRATION_RUNBOOKS (Resend, Telegram, Discord, GeoIP), DEPLOY_RUNBOOK, `secrets/README.md`. Geçmiş kayıtlar (TASK_REPORTS, changelog'lar, memory) bilerek değişmedi.
- **Testler:** yeni `scripts/nginx/edge-realip.test.mjs`; `scripts/compose-exposure.test.mjs`'e T162 testi + `ip_range`; `AuthSteamEndpointTests.ForwardedProto_IsBelievedOnlyFromTheTrustedProxy` (3 durum). CI `changes.scripts` filtresine `nginx/**` (nginx-only PR'da nginx testleri hiç koşmuyordu — T165 `edge-health` dahil).
- **Runbook:** DEPLOY_RUNBOOK §K.5 yeniden yazıldı — tünel adımları, §K.5.1 IP/şema mekanizması, §K.5.2 Cloudflare Access (uygulama/politika tablosu, panelden yeniden kurulum, **ev IP'si değişince `ev-ip` güncelleme: panel + API `PUT …/access/policies/<id>`**, ileride Telegram/Resend webhook yolları için yol bazlı bypass — yalnız belge), §K.5.3 ölçüm. §B geo + OpenID satırları, §K giriş, §K.1, §K.4, §K.6, §P güncellendi.
- **Deneme yayını kurulumu (bu PC, ilk kez):** §K.4 ile — `secrets/trial/` sırları **yeni** (JWT, iki webhook, iki iç anahtar, sa parolası `printf '%sAa1!'` kuralıyla, Redis, Grafana), testnet cüzdan dosyaları **boş**, `steam_api_key` / `tron_api_key(_secondary)` / `ethereum_rpc_*` geliştirmenin `secrets/`'ından kopya; `.env.trial` `.env.example`'dan (Production, Nile/Sepolia değerleri, `PUBLIC_ORIGIN` + dört `STEAM_OPENID_*` `https://getskinora.com`, `SKINORA_TRIAL_STACK=1`). Migration 65/65.

## Etkilenen Modüller / Dosyalar

- `nginx/nginx.conf`, `docker-compose.trial.yml`, `docker-compose.yml`, `.env.example`, `.github/workflows/ci.yml`
- `backend/src/Skinora.API/appsettings.json`, `backend/src/Skinora.Shared/Email/ResendSettings.cs`, `backend/src/Modules/Skinora.Auth/Configuration/SteamOpenIdSettings.cs`
- `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs`, `backend/tests/Skinora.Shared.Tests/Unit/{Email,Notifications,Telegram}/…`
- `frontend/src/lib/legal/operator.ts`, `frontend/src/components/legal/legal.test.tsx`
- `scripts/nginx/edge-realip.test.mjs` (yeni), `scripts/compose-exposure.test.mjs`
- Docs: `DEPLOY_RUNBOOK.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `plan/11_F9.md`, `DOC_CHANGELOG.md`, `INTEGRATION_RUNBOOKS/{RESEND,TELEGRAM,DISCORD,GEOIP}_SETUP.md`, `secrets/README.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`
- Repo dışı (gitignored, ana checkout): `secrets/trial/*` (yeni sırlar), `.env.trial`

## Kabul Kriterleri Kontrolü

| # | Kriter | Durum | Kanıt |
|---|---|---|---|
| 1 | Alan adı alındı, tünel bağlandı (token `secrets/trial/`, hostname → `skinora-reverse-proxy:80`) | ✓ | `$TRIAL --profile tunnel up -d` → 4 × `Registered tunnel connection` (fra03, ist03, ist07, fra10); `https://getskinora.com/nginx-health` → `200` (bu PC); tünel durunca `530` |
| 2 | Geçerli sertifika, yenileme otomatik (Cloudflare kenarı) | ✓ | `openssl s_client -connect getskinora.com:443 -servername getskinora.com` → `CN=getskinora.com`, SAN `getskinora.com, *.getskinora.com`, Let's Encrypt YE2, 2026-10-24 → 2027-01-22, `Verify return code: 0 (ok)`. Yenileme Cloudflare'in (Universal SSL) — bizde sertifika dosyası yok |
| 3 | Gerçek istemci IP'si korunuyor (`CF-Connecting-IP` → nginx real_ip, yalnız cloudflared'den) | ✓ | nginx access log: tünelden gelen her istek `81.214.103.181`; Redis `skinora:ratelimit:auth:ip:81.214.103.181` (backend'in kovası). Sahte: tünelden `CF-Connecting-IP: 1.2.3.4` → Cloudflare **403**; `X-Forwarded-For: 1.2.3.4` → kova yine `81.214.103.181`; yerel `127.0.0.1:8090`'a `CF-Connecting-IP` + `X-Forwarded-For` → log `172.30.161.1`, kova `ip:172.30.161.1`. Test: `edge-realip.test.mjs` (başka container + yerel port başlıkları yok sayılır) |
| 4 | Backend `https` şemasını görüyor | ✓ (dolaylı canlı + test) | Canlı: Steam'e giden `openid.return_to=https://getskinora.com/api/v1/auth/steam/callback`, `realm=https://getskinora.com`; düz http → nginx `301 https://getskinora.com/…`. nginx testi: bağlayıcıdan `X-Forwarded-Proto: https` backend'e `https` gider, başka eşten `http`. Backend testi: güvenilir eş + `https` → controller (302 Steam), başlıksız veya güvenilmeyen eş → `307 https` (HttpsRedirection şemayı http gördü). Bozma: `XForwardedProto` kaldırılınca test kırmızı. Backend istek log'u şemayı yazmadığı için canlıda doğrudan okunmadı |
| 5 | Oturum çerezi gerçek alan adında çalışıyor | ✓ kısmi — tarayıcı adımı sahipte | `curl -D - 'https://getskinora.com/api/v1/auth/steam?returnUrl=%2Fen%2Fdashboard'` → `Set-Cookie: skinora_oid_rt=…; path=/api/v1/auth; secure; samesite=lax; httponly` (Domain yok → yalnız `getskinora.com`). `refreshToken` (Steam girişi sonrası) tarayıcı gerektirir — sahip adımı aşağıda |
| — | Dış erişim Access'e düşüyor (görev talimatı) | ✓ | Dış getirici `r.jina.ai` → `https://getskinora.com/?p=2888014579` ve `/api/health?q=220910240` → **"Log in to skinora-trial" `shiny-bird-c918.cloudflareaccess.com`**; nginx log'unda bu iki istek **yok**. Access yapılandırması API'den okundu: uygulama `getskinora.com`, `ev-ip` bypass `81.214.103.181/32` (öncelik 1), `sahip` allow (2) |
| — | Runbook: Access, ev IP'si güncelleme, webhook bypass | ✓ | DEPLOY_RUNBOOK §K.5.2 |
| — | CI testleri yeni duruma göre | ✓ | aşağıda |

**Dış ölçüm notu (önemli):** ilk dış ölçüm görev talimatındaki gibi WebFetch ile yapıldı ve **uygulama sayfasını** döndürdü; tünel ~20 sn içinde durduruldu (16:37:28 UTC). nginx log'u isteğin `81.214.103.181`'den (`User-Agent: Claude-User (claude-code/…)`) geldiğini gösterdi: Claude Code'un WebFetch'i isteği **bu PC'den** atar, yani istek `ev-ip` bypass'ından geçti — dış erişim değil. Ölçüm gerçekten dışarıdan çıkan bir getiriciyle (`r.jina.ai`) tekrarlandı ve Access ekranı görüldü. Bu PC'den yapılan "dış" ölçüm bypass yüzünden hiçbir şey kanıtlamaz.

## Test Sonuçları

| Komut | Sonuç |
|---|---|
| `node --test "scripts/**/*.test.mjs"` | 243/243 ✓ (yeni `edge-realip` 8, `compose-exposure` 15) |
| `node --test scripts/nginx/edge-realip.test.mjs` — bozma: `geo` `$remote_addr`'e döndü | 3 pass / 5 fail (yakalandı) |
| aynı — bozma: `real_ip_header X-Real-IP` | 4 pass / 4 fail (yakalandı) |
| `node --test scripts/compose-exposure.test.mjs` — bozma: nginx adresi `.251` | T162 testi kırmızı (yakalandı) |
| `dotnet test Skinora.sln --filter "FullyQualifiedName!~Integration"` | 12 proje, hepsi ✓ (Shared 607, Transactions 1198, API 313 …) |
| `dotnet test … --filter AuthSteamEndpointTests` | 18/18 ✓ (yeni 3 dahil) |
| aynı, bozma: `ForwardedHeaders.XForwardedProto` kaldırıldı | 1/3 fail (yakalandı), geri alındı |
| `npx vitest run src/components/legal` | 30/30 ✓ |
| `nginx -t` (nginx:alpine, repo conf) | syntax ok |

## Altyapı Değişiklikleri

- Deneme yayını ağı değişkensiz: `172.30.161.0/24`, dinamik `.0/25`, bağlayıcı `.250`. `TRIAL_NETWORK_SUBNET` artık okunmuyor (`.env.trial`'dan da silindi).
- Geliştirme stack'inde (`172.20.0.0/16`) nginx artık hiçbir eşin `CF-IPCountry`'sine güvenmez (önceden ağ içi container'lara güveniyordu; geliştirmede Cloudflare yok — işlevsel fark yok).
- Bu PC'de deneme yayını volume'leri oluştu (`skinora-trial_*`, 8 adet) — ölçüm sonunda `down` (volume'ler silinmedi); tünel durduruldu.

## Commit & PR

- Commit: `113fbcdc` (kod + belgeler) + rapor/status commit'i
- PR: #384 — https://github.com/turkerurganci/Skinora/pull/384
- CI: yönetici izler (yönetici düzeni)

## Kararlar (öneriyle kaydedildi — ⚠)

- ⚠ GÜVENLİK: "yalnız cloudflared'in ağı" yerine **yalnız bağlayıcının sabit adresi** (gerekçe yukarıda — ağ geçidi ve diğer container'lar).
- Cloudflare'den düz `http` isteği nginx'te `301 https`'e çevrilir (Cloudflare "Always Use HTTPS" ayarına bağımlı kalmamak için). HSTS eklenmedi (alan adını bir yıl https'e kilitler; pilot öncesi karar).
- Destek ve gönderen e-posta adresleri de `getskinora.com`'a alındı (alan adına bağlı dağıtım adresi sayıldı; e-posta alan adı doğrulaması o alan adına yapılacak).

## Sahibe kalan adımlar

1. **Tarayıcıdan Steam girişi:** `$TRIAL --profile tunnel up -d --wait` (Grafana için önce `telegram_bot_token.txt` dolu olmalı, yoksa `--wait` Grafana'da hata verir — tünel yine açılır) → `https://getskinora.com` → *Sign in with Steam* → dönüşte giriş yapılmış olmalı; geliştirici araçları → Application → Cookies → `getskinora.com` altında `refreshToken` (`Secure`, `HttpOnly`, yol `/api/v1/auth`). Ardından §K.4 adım 7 (süper admin).
2. Ev IP'si değişirse §K.5.2 ile `ev-ip`'yi güncelle.
3. Telegram/Resend webhook'ları açılırken §K.5.2'deki yol bazlı bypass'ları oluştur (§P ile birlikte).

## Known Limitations / Follow-up

- **Grafana deneme yayınında açılmıyor:** `secrets/trial/telegram_bot_token.txt` boş; contact point provisioning token'sız abort eder (§L.9'da "boşsa Grafana açılmaz" olarak zaten yazılı). Ölçümde Grafana durduruldu; heartbeat de Grafana sağlığına bakar. Bot token §P sahip adımıdır (T165/T171).
- Backend'in `https` şemayı gördüğü canlıda doğrudan okunmadı (istek log'unda şema yok) — dolaylı canlı kanıt + iki katmanlı test.
- `www.getskinora.com` tanımlı değil (ne tünel hostname'i ne Access).
- Bağlayıcı adresi ve alt ağ değişirse nginx.conf aynı commit'te değişmeli — CI testi ayrışmayı yakalar.
- Cloudflare kenarı istemcinin `CF-Connecting-IP`'sini 403 ile kesiyor — bu bizim değil Cloudflare'in davranışı; nginx tarafı buna dayanmaz.

## Notlar

- **Working tree:** temiz (başlangıç `9690b63b`).
- **Main CI startup check:** `40060245445` success, `40060245528` success, `40046709448` success.
- **Dış varsayımlar:**
  - nginx:alpine'da realip modülü var → `nginx -t` ve `edge-realip` testi geçti.
  - Docker statik IP + `ip_range` dışı adres → compose render + canlı `docker network inspect` (`IPRange 172.30.161.0/25`, bağlayıcı `.250` ile tünel bağlandı).
  - Cloudflare `CF-Connecting-IP` / `X-Forwarded-Proto` gönderiyor → canlı: nginx log'unda gerçek IP, Steam `return_to` https, düz http'de nginx 301.
  - Access yapılandırması → API okuması (uygulama, iki politika, öncelikler).
  - Claude Code WebFetch isteği bu PC'den atar → nginx log'u (`Claude-User` UA, `81.214.103.181`) — ölçüm yöntemi buna göre değişti.
- Cloudflare API anahtarı ve tünel token'ı hiçbir çıktıya yazdırılmadı; yalnız dosyadan okundu.
