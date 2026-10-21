# T161 — Sunucu sıkılaştırma

**Faz:** F9 | **Durum:** ✓ Tamamlandı (bağımsız doğrulama PASS) | **Tarih:** 2026-10-21

---

## Bağlam — ortam kararı

Proje sahibi kararı (2026-10-21, modal): **kiralık sunucu yok.** Deneme yayını sahibin bu PC'sinde, mevcut Docker Desktop + **Cloudflare Tunnel** ile, yalnız test ağlarında (Nile + Sepolia). Modemde port açılmaz; alan adı `skinora.com` henüz alınmadı. Bu yüzden 11 T161'in "dışarıya yalnız 80/443" kriteri bu ortamda **"dışarıya — LAN arayüzü dahil — yayımlanan port yok ya da yalnız 127.0.0.1; dış trafik yalnız tünelden"** olarak uygulandı ve `Docs/plan/11_F9.md` (v1.20) T161/T162/T172 metinleri buna göre güncellendi (sahip kararı; onay alınmış sayıldı).

## Yapılan İşler

**1. Port kapatma (`docker-compose.yml`, `docker-compose.override.yml`)**
- Taban dosyada **her host portu `127.0.0.1`'e bağlandı**: backend 5000, frontend 3000, steam 5100, blockchain 5200, ethereum 5300, db 1433, redis 6379, loki 3100, prometheus 9090, grafana 3001, uptime-kuma 3002. İç portlar için ayar yok (literal `127.0.0.1`). Edge'in tek ayarı `NGINX_BIND_ADDRESS` (varsayılan `127.0.0.1`; yalnız 80/443'ü doğrudan sunan bir sunucuda `0.0.0.0`).
- Dosya başına port politikası yorumu (05 §3.4, Linux'ta Docker'ın ufw'yu atlaması, CI kapısı).
- Dev override'ındaki bütün `ports:` kaldırıldı — compose override port listesini **ekler**; oradaki çıplak `"5000:5000"` aynı porta ikinci bir `0.0.0.0` bağı açardı. Boş kalan servis girdileri (`skinora-db:` vb.) silindi.

**2. Deneme yayını overlay'i (`docker-compose.trial.yml`, yeni)**
- `name: skinora-trial` (ayrı proje; `-p` verilirse o geçer) — ayrı container, ağ, volume, imaj.
- Her serviste `container_name: !reset null` (adlar projeden türer, geliştirme stack'inin sabit adlarıyla çakışmaz) ve iç servislerde `ports: !reset []`. Yalnız operatör portları kalır: edge `127.0.0.1:${TRIAL_HTTP_PORT:-8090}`, Grafana `127.0.0.1:3011`, Uptime Kuma `127.0.0.1:3012` (`!override`).
- **Ölçülen tuzak:** `!reset` etiketleri YAML anchor'ı (`<<: *x`) üzerinden verildiğinde Compose v5.1.0 onları **sessizce düşürdü** ve taban portlar kaldı (render çıktısında `127.0.0.1:5000` vb. görüldü). Etiketler bu yüzden her serviste tek tek yazılı; dosya yorumu ve runbook bunu söylüyor.
- Ayrı ağ alt ağı `${TRIAL_NETWORK_SUBNET:-172.30.161.0/24}`; backend'in `X-Forwarded-For` güveni (`ReverseProxy__KnownNetworks__0`) aynı değişkenden — `.env.trial`'a kopyalanmış geliştirme değeri (`172.20.0.0/16`) devreye girmez.
- Ayrı secret'lar: üç Ethereum secret'ı ve tünel token'ı `./secrets/trial/` altından.
- `ASPNETCORE_ENVIRONMENT=Production`, dört Node servisinde `NODE_ENV=production` zorlanır; Grafana `GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_ADMIN_PASSWORD:?…}` — parola yoksa render bile edilmez.
- **Tünel (profil `tunnel`):** `skinora-cloudflared` (`cloudflare/cloudflared:2026.9.3`, `tunnel --no-autoupdate run`, token `TUNNEL_TOKEN_FILE=/run/secrets/cloudflare_tunnel_token` — Docker secret, env değil; port yok; `restart: unless-stopped`) + `skinora-tunnel-preflight` (alpine; token dosyası boş ya da yalnız boşluksa `TUNNEL_TOKEN_MISSING` yazıp `exit 1`). cloudflared `depends_on: preflight: service_completed_successfully` + edge `service_healthy` → token yoksa **hiç başlatılmaz** (boş token'la yeniden başlama döngüsü yerine).

**3. Sidecar'lar iç anahtarsız açılmıyor (fail-closed)**
- `sidecar-steam` ve `sidecar-blockchain`: yeni `src/config/internalKey.ts` — `requireInternalKey()` boş/boşluk → `INTERNAL_KEY_MISSING`, < 32 karakter → `INTERNAL_KEY_TOO_SHORT` (Ethereum servisiyle aynı kodlar ve taban). Hata mesajı anahtarı içermez.
- `src/index.ts`: kontrol importlardan hemen sonra, hiçbir şey kurulmadan / dinlenmeden; başarısızsa `logger.fatal({code})` + hata fırlatılır → süreç 1 ile çıkar.
- `src/api/middleware.ts`: `internalKeyAuth` artık `internalKeyAuth(expectedKey)` fabrikası — boş anahtarla **kurulamaz** (eski "anahtar yoksa doğrulamayı atla — dev modu" yolu silindi), karşılaştırma `timingSafeEqual` (Ethereum servisinin aynısı). `RouterDeps.internalKey` zorunlu.
- Ethereum servisi zaten fail-closed'dı (T150) — değişmedi.

**4. CI port kapısı (`scripts/compose-exposure.test.mjs`, yeni)**
- `node --test` (job "3c. Script test", `ci-gate.needs` içinde). `.env.example` kopyasından bir stub env ile `docker compose config --format json` render eder: (a) taban + `ethereum` profili → her port `127.0.0.1`; (b) `NGINX_BIND_ADDRESS=0.0.0.0` yalnız edge'i açar; (c) taban + dev override → `0.0.0.0` yok; (d) taban + trial (+ `ethereum`, `tunnel`) → yayın yalnız edge/Grafana/Uptime Kuma ve loopback; her taban servisi trial'da var; sabit `container_name` yok; alt ağ `172.30.161.0/24`; secret'lar `secrets/trial/`'dan; Production + güven aralığı; cloudflared portsuz, `TUNNEL_TOKEN_FILE`, düz `TUNNEL_TOKEN` yok, sabit sürüm, preflight bağımlılığı; Grafana parolasız render reddedilir.
- docker yoksa lokalde atlanır, **CI'da başarısız olur** (`process.env.CI`).
- `ci.yml`: `scripts` yol süzgecine `docker-compose.yml`, `docker-compose.override.yml`, `docker-compose.trial.yml`, `.env.example` eklendi (yalnız compose değiştiren PR da kapıyı koştursun); job yorumuna not.

**5. Belgeler**
- `Docs/DEPLOY_RUNBOOK.md`: yeni **§K Deneme yayını** (K.1 farklar, K.2 port politikası, K.3 ön koşullar — deneme yayını geliştirme cüzdanlarını paylaşmaz, K.4 adım adım kurulum — host'tan DB bağlantısı olmadan migration, K.5 tünel + T162'ye kalanlar, K.6 gelecekteki Linux sunucu: tünel ya da doğrudan 80/443 + "Docker ufw'yu atlar" uyarısı + dışarıdan nmap, K.7 bu görevin ölçümü, K.8 durdurma/silme); §B `INTERNAL_KEY` satırı fail-closed; §0 özetine §K satırı; başlık.
- `Docs/plan/11_F9.md` v1.20: F9 başlığına ortam kararı notu; T161, T162 (tünel bağlantısı, tünel arkasında gerçek istemci IP'si, `https` şeması), T172 (deneme yayınında, tünel üzerinden) kriterleri.
- `Docs/05_TECHNICAL_ARCHITECTURE.md` v3.28: §3.4 port kuralı ve iç anahtar "uygulandı (T161)"; §8.1 `skinora-cloudflared` satırı; §8.3 deneme yayını satırı.
- `Docs/DOC_CHANGELOG.md` (05, 11, DEPLOY_RUNBOOK bölümlerinin başına), `.env.example` (`NGINX_BIND_ADDRESS`, `TRIAL_*`, iç anahtar zorunluluğu, `GRAFANA_ADMIN_PASSWORD` boş), `secrets/README.md` (`secrets/trial/`), `.claude/CONTEXT.md` (dosya haritası).

## Etkilenen Modüller / Dosyalar
- `docker-compose.yml`, `docker-compose.override.yml`, `docker-compose.trial.yml` (yeni), `.env.example`, `.github/workflows/ci.yml`
- `sidecar-steam/src/config/internalKey.ts` (yeni), `…/internalKey.test.ts` (yeni), `…/config/startupWiring.test.ts` (yeni), `…/config/index.ts`, `…/api/middleware.ts`, `…/api/middleware.test.ts` (yeni), `…/api/routes.ts`, `…/api/routes.test.ts`, `…/index.ts`
- `sidecar-blockchain/src/config/internalKey.ts` (yeni), `…/internalKey.test.ts` (yeni), `…/config/startupWiring.test.ts`, `…/config/index.ts`, `…/api/middleware.ts`, `…/api/middleware.test.ts` (yeni), `…/api/routes.ts`, `…/index.ts`
- `scripts/compose-exposure.test.mjs` (yeni; kapanışta `.env.trial` işareti testi)
- `Docs/DEPLOY_RUNBOOK.md`, `Docs/plan/11_F9.md`, `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/DOC_CHANGELOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `Docs/TASK_REPORTS/T161_REPORT.md`, `secrets/README.md`, `.claude/CONTEXT.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Dışarıya (LAN dahil) yayımlanan port yok ya da yalnız 127.0.0.1; dış trafik yalnız tünelden | ✓ | Canlı: `-p skinora-t161` yığını (11 servis `healthy`). `docker ps --format '{{.Names}} {{.Ports}}'` → yalnız `reverse-proxy 127.0.0.1:8090->80`, `grafana 127.0.0.1:3011->3000`, `uptime-kuma 127.0.0.1:3012->3001`. `Get-NetTCPConnection -State Listen` (sahibi `com.docker.backend`) → projeye ait yalnız `127.0.0.1:{3011,3012,8090}` (`[::]:64110/64120` başka ajanın testcontainers'ı). `netstat -ano` aynı. Edge'e giden tek dış yol cloudflared (portsuz, iç ağdan) |
| 2 | DB, Redis, backend, sidecar'lar, Grafana, Prometheus, Loki portları dışarıya kapalı | ✓ | Canlı: Wi-Fi adresi `192.168.1.104`'e 8090 3011 3012 1433 6379 5000 5100 5200 3100 9090 → **hepsi kapalı**; `127.0.0.1`'de yalnız 8090/3011/3012 açık, DB/Redis/backend/sidecar/Loki/Prometheus **loopback'te de kapalı** (deneme yayını onları hiç yayımlamaz). Render: `compose-exposure.test.mjs` 9/9; bozma: taban DB portu çıplak `"${DB_PORT}:1433"` yapılınca 3 test, trial'da redis `!reset` kaldırılınca 1 test **kırmızı** |
| 3 | Sidecar'lar iç anahtar boşsa açılmıyor (testle) | ✓ | Birim: `internalKey.test.ts` (iki sidecar, 6'şar), `middleware.test.ts` (8'er — boş anahtarla kurulamaz; eksik/boş/yanlış/kısa/uzun/tekrarlı başlık 401; doğru anahtar geçer), steam `routes.test.ts` T161 bloğu (anahtarsız/yanlış/önek 401, doğru anahtarla rota, `/health` anahtarsız 200, router anahtarsız kurulamaz). Süreç: steam `config/startupWiring.test.ts` (unset/blank/kısa → çıkış > 0 + kod; anahtarla `Steam sidecar listening`), blockchain `startupWiring.test.ts` (unset/kısa → çıkış > 0 + kod, delegasyon satırı hiç basılmadı; diğer 8 açılış testi anahtarla açılıyor). Container: `run -e INTERNAL_KEY=` steam → `refused to start: INTERNAL_KEY_MISSING`, exit 1; blockchain `-e INTERNAL_KEY=<13 karakterlik test değeri>` → `INTERNAL_KEY_TOO_SHORT`, exit 1. İç ağdan anahtarsız istek → 401 (iki servis) |
| 4 | Deneme yayını için ayrı compose projesi (proje adı, volume, `.env`/secrets) ve runbook'ta adım adım | ✓ | `docker-compose.trial.yml` (`name: skinora-trial`, `--env-file .env.trial`, `secrets/trial/`, `container_name: !reset`, ayrı alt ağ). Canlı: volume'ler `skinora-t161_*`, ağ `172.30.161.0/24`, backend env `ReverseProxy__KnownNetworks__0=172.30.161.0/24`, `ASPNETCORE_ENVIRONMENT=Production`. Runbook §K.4 adımları canlı koşuldu (migration 60/60 = repo'daki 60 migration dosyası) |
| 5 | cloudflared servisi compose'da hazır, token env/secret'tan, token yoksa açılmıyor | ✓ | Boş token → `skinora-tunnel-preflight` `TUNNEL_TOKEN_MISSING`, exit 1, `up` exit 1, cloudflared `Created` (başlatılmadı); yalnız boşluklu token → aynı. Sahte token → preflight geçti, cloudflared başladı ve `Provided Tunnel token is not valid.` (dosyadan okundu); container env'inde yalnız `TUNNEL_TOKEN_FILE`, port yok. Gerçek tünel T162 |
| 6 | Gelecekteki Linux sunucu için 80/443 seçeneği + host güvenlik duvarı notu runbook'ta | ✓ | DEPLOY_RUNBOOK §K.6 (tünel önerilen / doğrudan 80/443 `NGINX_BIND_ADDRESS=0.0.0.0`; Docker'ın `DOCKER`/`DOCKER-USER` zincirlerinin ufw'dan önce işlenmesi; `ss -ltnp` + dışarıdan `nmap -Pn -p-`) |
| 7 | CI'ı kırmadan (geliştirme/CI portları açık) | ✓ | CI'daki E2E akışları yalnız `docker-compose.e2e.yml` kullanır (`ci.yml` 852–922) — dosyaya dokunulmadı. Geliştirme stack'i `127.0.0.1`'de aynı portları yayımlamaya devam eder (localhost akışları — §G.2 migration, `curl localhost:5200/metrics` vb. — değişmedi). CI sonucu aşağıda |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-steam vitest | ✓ 161/161 (12 dosya) | `npx vitest run` |
| sidecar-blockchain vitest | ✓ 601/601 (32 dosya) | `npx vitest run` |
| sidecar-ethereum compose wiring | ✓ 34/34 | `npx vitest run src/config/composeWiring.test.ts` (taban dosyayı okur) |
| scripts (node --test) | ✓ 46/46 | `node --test "scripts/**/*.test.mjs"` (yeni compose-exposure 9/9 dahil) |
| tsc / lint / build | ✓ | iki sidecar: `npx tsc --noEmit`, `npm run lint`, `npm run build`; değişen dosyalar `prettier --check` temiz (diğer dosyalardaki uyarılar yerel CRLF artefaktı — CI LF'de yetkili) |
| Canlı port taraması | ✓ | §Kabul 1–5; ayrıntı DEPLOY_RUNBOOK §K.7 |

**Not (yük altında zaman aşımı):** ilk tam blockchain koşusunda yeni süreç testlerinden ikisi 20 sn iç süre sınırına takıldı (makinede paralel ajanlar + 32 test dosyası paralel); tek başına 3–6 sn'de geçtiler. Gereksiz iki süreç testi (boşluklu anahtar — birim testte var; "anahtarla açılır" — dosyadaki diğer 8 test zaten anahtarla açılıyor) çıkarıldı, ikisi kaldı (iki ayrı kod).

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS — bağımsız doğrulama (Opus high, yönetici düzeni), HEAD `d3e97a50`, 7/7 kriter |
| Bulgu sayısı | 6 × S4 (S1–S3 yok). Rapor: `f9-handoff/T161_VALIDATION.md` |
| Düzeltme gerekli mi | Hayır (PASS). S4-1 (⚠ GÜVENLİK, `--env-file` unutulması) yönetici kararıyla kapanışta düzeltildi; diğerleri aşağıda |

**S4 bulguları ve karşılıkları:**

| # | Bulgu | Karşılık |
|---|---|---|
| S4-1 ⚠ | `--env-file .env.trial` unutulursa deneme yayını sessizce geliştirme `.env`'iyle kalkıyor (TRON tohumu, anahtarlar, DB parolası paylaşılır); runbook §K.4'teki "unutulursa geliştirme projesine gider" cümlesi bu bayrak için yanlıştı | **Kapanışta düzeltildi.** Overlay'e `x-skinora-trial-guard: ${SKINORA_TRIAL_STACK:?...}`; işaret yalnız `.env.trial`'da (runbook §K.4 `echo 'SKINORA_TRIAL_STACK=1' >> .env.trial` der), `.env.example`'a konmadı. İşaretsiz `config` → `rc=1 required variable SKINORA_TRIAL_STACK is missing a value`; işaretli → `rc=0`; geliştirme `.env`'li (işaretsiz) dizinde `--env-file` yokken de `rc=1`. CI kapısına yeni test (`compose-exposure.test.mjs` 10/10) ve `.env.example`'da işaret olmadığını sabitleyen assert. Runbook §K.1/§K.2/§K.4 düzeltildi |
| S4-2 | `INTERNAL_KEY=your_internal_api_key_here` yalnız uzunluk (26 < 32) yüzünden reddediliyor; örnek değeri açıkça reddeden kural/test yok | Kabul edildi, kodda değişiklik yok (şu an doğru reddediliyor). Şablon ≥ 32 karakterlik yer tutucuya çevrilirse servis o değerle açılır: şablonu değiştiren görev örnek değeri reddeden kural/test eklemeli |
| S4-3 | `docker-compose.e2e.yml` portları `0.0.0.0` (14333 DB, 5000, 5100–5300, 8080) | Bilinçli kapsam dışı (⚠, aşağıda); runbook §K.7'ye uyarı eklendi |
| S4-4 | 3 GB sınırında yığın açıkken soğuk frontend derlemesi swap'ı doldurdu (available 431 MB); soğuk backend+frontend birlikte denenmedi | **T172 risk notu.** Runbook §K.3'e RAM ölçümü ve "yığın durmuşken derle, sonra aç" kuralı eklendi |
| S4-5 | Geçersiz (boş olmayan) token'la cloudflared `restart: unless-stopped` yüzünden döngüye giriyor (~2 dk'da 9 yeniden başlama) | Gözlem, değişiklik yok (port açmaz, sızıntı yok); runbook §K.7'ye not |
| S4-6 | Bu PC'de IIS/HTTP.sys `0.0.0.0:80` dinliyor; `192.168.1.104:80` LAN'dan 200 (Skinora dışı, modemde yönlendirme yok) | T161 dışı host gözlemi; sahibe bildirilecek, runbook §K.7'ye not |

**RAM ölçümü (Docker/WSL2 sınırı 3 GB, doğrulayıcı):** tam deneme yığını (12 servis, ethereum dahil) boştayken ≈ 1,56 GiB (db 829 MiB, backend 183, uptime-kuma 125, grafana 83, frontend 79, blockchain 60, loki 57, steam 47, prometheus 42, ethereum 38, nginx 14, redis 7); `OOMKilled=false`, `RestartCount=0`. Yığın açıkken frontend `build --no-cache`: en düşük available 431 MB, swap 1024/1024 MB (dolu), build başarılı, OOM yok. T172 riski: açık yığınla soğuk derleme yapma.

**Ek doğrulama kanıtı:** ethereum sidecar'ı container'da boş anahtarla denendi (`MISSING`/`MISSING`/`TOO_SHORT`, rc=1); 13 bozma testinin (M1–M4, X1–X6) tamamı kırmızı verdi. CI `39579911699` (`d3e97a50`) 27 iş yeşil.

## Altyapı Değişiklikleri
- Migration: Yok.
- Config/env değişikliği: **Var.** `SKINORA_TRIAL_STACK` (yalnız `.env.trial`, zorunlu işaret — kapanış), `NGINX_BIND_ADDRESS` (yeni, varsayılan `127.0.0.1`), `TRIAL_HTTP_PORT` / `TRIAL_GRAFANA_PORT` / `TRIAL_UPTIME_KUMA_PORT` / `TRIAL_NETWORK_SUBNET` (yalnız trial), `.env.example`'da `GRAFANA_ADMIN_PASSWORD` boş (geliştirme `:-admin` ile aynı davranır). **İç anahtar artık zorunlu:** `INTERNAL_KEY` (ya da servis başına `*_SIDECAR_INTERNAL_KEY`) boş ya da < 32 karakterse Steam ve TRON servisi açılmaz. Bu makinedeki geliştirme `.env`'i ölçüldü: `INTERNAL_KEY`, `STEAM_SIDECAR_INTERNAL_KEY`, `BLOCKCHAIN_SIDECAR_INTERNAL_KEY` 64 karakter → etkilenmez. `.env.example`'daki yer tutucu (26 karakter) bilerek geçmez.
- Docker değişikliği: **Var.** Taban host portları loopback; dev override portsuz; yeni trial overlay + cloudflared/preflight servisleri. Geliştirme stack'inin davranış farkı: servis portları artık LAN'dan erişilemez (localhost'tan aynen).

## Commit & PR
- Branch: `task/T161-sunucu-sikilastirma`
- Commit: `0eac6569` — T161: portlar 127.0.0.1, deneme yayını overlay'i + Cloudflare tüneli, sidecar iç anahtar zorunlu · `af6883f7` — runbook §K, plan, rapor + status + memory · CRLF düzeltmesi (11_F9.md) + bu satır
- PR: #367 (https://github.com/turkerurganci/Skinora/pull/367)
- CI: izleniyor (son durum yönetici bildiriminde)

## Known Limitations / Follow-up
- **Tünel arkasında gerçek istemci IP'si (T162).** cloudflared → nginx → backend zincirinde `X-Forwarded-For`'un son halkası cloudflared olur; `ForwardLimit=1` ile backend herkesi tek IP görür (rate limit tek kova, geo-block/VPN sinyali etkisiz). T162 kriterine yazıldı: nginx `real_ip_header CF-Connecting-IP` + `set_real_ip_from 172.30.161.0/24`. Tünel bu görevde bağlanmadığı için bugün etkisi yok.
- **`https` şeması (T162).** TLS Cloudflare'da biter; nginx `X-Forwarded-Proto http` gönderir — çerez `Secure` / OpenID `return_to` T162'de ele alınır (kritere yazıldı).
- **Dış tarama yapılmadı.** LAN kontrolü bu makinenin kendi Wi-Fi adresine bağlanma denemesi; internetten/LAN'daki başka cihazdan tarama yok (modemde yönlendirme yok — sahip kararı). Gerçek dış tarama T162'de alan adı üzerinden.
- **`docker-compose.e2e.yml` portları `0.0.0.0`'da kaldı.** Sabit test değerleri taşır; CI runner'ında harness `localhost` üzerinden bağlanır. Bu PC'de yerel E2E koşulursa test DB'si (bilinen `sa` parolası) koşu süresince LAN'dan erişilebilir — kapsam dışı bırakıldı, CI'ı riske atmamak için (⚠).
- **Webhook sırrı boşken TRON servisi yine açılır** (her gönderimde reddeder — `WebhookClient`). Kriter iç anahtar içindi; webhook sırrı açılış kapısı yapılmadı.
- **`sidecar-fake`** iç anahtarsız kabul etmeye devam eder (E2E test ikizi, bilinçli).
- `.env.trial` için tam şablon yok (`.gitignore`'da `.env.*`); runbook §K.4 `.env.example`'dan kopyalayıp değiştirilecekleri listeler. Zorunlu işaret (`SKINORA_TRIAL_STACK=1`) runbook adımıyla eklenir; ayrı `.env.trial.example` dosyası denendi ama pre-commit kancası `.env.example` dışındaki `.env*` dosyalarını reddediyor (bypass kullanılmadı).

## Notlar
- **Working tree:** temiz (`git status --short` boş, dal `origin/main` 50868b13'ten açık).
- **Main CI startup check:** son 3 tamamlanmış run — `39546534805` (Docker Publish, #362) success, `39546534823` (CI, #362) success, `39545306202` (CI, #361) success. #363 run'ları (`39572533142`, `39572533083`) o anda `in_progress` (sayılmaz).
- **Dış varsayımlar:**
  - Compose `!reset` / `!override` → yerel Docker Compose v5.1.0'da ölçüldü (render); CI runner ubuntu-latest'teki Compose v2 ≥ 2.24 bu etiketleri destekler — kapı CI'da koşuyor (sonuç aşağıda).
  - `cloudflare/cloudflared:2026.9.3` Docker Hub'da var (2026-09-24 yayımı, ~4 hafta); `tunnel run --help` → `--token-file` / `$TUNNEL_TOKEN_FILE` destekli; imaj kullanıcısı `65532:65532`, giriş `cloudflared --no-autoupdate` (ölçüldü).
  - Boş token'la cloudflared: `"cloudflared tunnel run" requires the ID or name of the tunnel to run` (ölçüldü) — preflight bu yüzden eklendi.
  - Docker Desktop `127.0.0.1` yayınını host loopback'ine bağlar, LAN adresinde dinlemez — ölçüldü (§Kabul 1–2).
  - Geliştirme `.env` iç anahtar uzunlukları 64 (değer okunmadan uzunluk ölçüldü).
  - `dotnet ef migrations script` çıktısı UTF-8 BOM'lu; `sqlcmd` stdin'de BOM'u reddeder, `QUOTED_IDENTIFIER` varsayılan kapalı (`-I` gerekir) — ikisi de ölçüldü, runbook §K.4'te.
- **Geliştirme stack'ine dokunulmadı:** `escrow` projesinin container'ı yoktu; `escrow_*` volume'leri (8) ölçüm öncesi ve sonrası aynı. Kendi projem `skinora-t161` `down -v --rmi local --remove-orphans` ile silindi (container/volume/ağ 0). Docker Desktop yeniden başlatılmadı. Ölçüm için kullanılan `.env.trial` ve `secrets/trial/` (atılık rastgele değerler) silindi.
- **Kararlar (öneriyle kaydedildi — ⚠):**
  1. ⚠ GÜVENLİK — Taban compose'ta iç servis portları **ayarsız** `127.0.0.1` (geliştirme stack'i dahil); tek ayar edge'in `NGINX_BIND_ADDRESS`'i, varsayılan loopback. Sonuç: geliştirme servislerine LAN'dan (ör. telefondan) erişim kalktı.
  2. ⚠ GÜVENLİK — İç anahtar her ortamda zorunlu ("dev modu" yok), taban 32 karakter (Ethereum ile aynı), sabit zamanlı karşılaştırma.
  3. Dev override'dan `ports:` kaldırıldı (eklemeli birleşme `0.0.0.0` bağı açardı).
  4. Deneme yayını ayrı tam dosya değil **overlay** (taban dosyayla sapma olmasın; mevcut compose bağlantı testleri tabanı okumaya devam eder); proje adı `skinora-trial`; container adları projeden türer.
  5. Operatör portları loopback'te kalır: edge 8090, Grafana 3011, Uptime Kuma 3012 (geliştirme portlarıyla çakışmaz).
  6. ⚠ GÜVENLİK — Trial'da `Production` modu zorlanır; Grafana parolası zorunlu; `.env.example`'da `GRAFANA_ADMIN_PASSWORD` boşaltıldı (geliştirme yine `admin`'e düşer).
  7. ⚠ GÜVENLİK — Trial ağının alt ağı sabit (`172.30.161.0/24`) ve backend'in forwarded-header güveni ona bağlı.
  8. cloudflared sürümü `2026.9.3` sabit; token Docker secret (`TUNNEL_TOKEN_FILE`), `tunnel` profili, preflight kapısı, `restart: unless-stopped`.
  9. Trial migration'ı host portu olmadan: idempotent betik → `docker compose cp` → container içi `sqlcmd -I`.
  10. ⚠ PARA (testnet) — Deneme yayını geliştirme cüzdanlarını (TRON tohum/sıcak cüzdan, Ethereum tohum/anahtarlar) **paylaşmaz** — runbook kuralı (aynı indeksler aynı depozitleri verir; iki imzacı aynı nonce'u yarıştırır).
  11. CI kapısı render edilmiş modeli okur (`docker compose config`), metin değil; `scripts` job'unda; yol süzgeci genişletildi.
  12. `docker-compose.e2e.yml` değiştirilmedi (CI'ı riske atmamak için).
  13. T172 görev adı değiştirilmedi ("Sunucuda prova"), yalnız kriteri deneme yayınına bağlandı (paralel dallarda status tablosu çakışmasın).
  14. ⚠ GÜVENLİK (kapanış, yönetici kararı, öneriyle kaydedildi) — Deneme overlay'i `SKINORA_TRIAL_STACK` işareti olmadan render etmez; işaret yalnız `.env.trial`'da, geliştirme `.env.example`'da yok. `--env-file` unutulursa deneme yayını geliştirme cüzdanlarıyla kalkamaz. `config`, `up`, `down` dahil her komut işaret ister (`down` için de `--env-file`, `$TRIAL` öneki zaten taşır).
  15. S4-2 / S4-5 / S4-6 için kod değişikliği yapılmadı (gözlem ya da kapsam dışı); S4-3 / S4-4 / S4-5 / S4-6 runbook §K'ya not düşüldü.
