# T122 — Gerçek Steam ölçümü (spike, kod teslimi yok)

**Dal:** `task/T122-steam-inventory-measurement` · **Tarih:** 2026-08-13 · **Tip:** spike / ölçüm — üretim kodu değişmedi

---

## Kapsam Değişikliği (proje sahibi kararı, 2026-08-13)

Plandaki T122 (11 §P2.5) **iki gerçek Steam hesabı arasında bir trade** yapılmasını şart koşuyordu.
Proje sahibi bu ölçümü yapamayacağını bildirdi. Bu, `task.md` Adım 4'ün tanımıyla **kırık bir dış
varsayımdır** ve scope'u etkileyen bir karardır; BLOCKED yerine şu bölünme sunuldu ve onaylandı:

| Parça | Kapsam | Durum |
|---|---|---|
| **T122-A** | Trade **gerektirmeyen** her şey — canlı Steam'e karşı salt-okunur ölçüm | ✓ bu raporda |
| **T122-B** | Tek hesap, sahibinin kendi oturumundan tek capture (opsiyonel, ~2 dk) | ✓ yapıldı — **sonuç kısmi**, B7 kapanmadı |
| **T122-C** | Trade olmadan ölçülemeyen kalanı T125'in tasarımından izole etmek | ✓ bu raporda (runbook §7) |

Kritik gözlem: T122'nin dört kabul kriterinden **üçü trade gerektirmiyordu.** Görev tanımı ölçümü tek bir
yönteme (gerçek trade) bağladığı için, yöntem imkânsızlaşınca görevin tamamı imkânsız görünüyordu.

---

## Yapılan İşler

Gerçek `steamcommunity.com`'a karşı anonim, salt-okunur ölçüm yapıldı (2026-08-13 UTC 17:06–17:35, ~45 istek,
proje sahibinin residential TR IP'si). `sidecar-fake` bu davranışların hiçbirini kanıtlayamaz — ne yazarsak
onu döner; buradaki her bulgu gerçek Steam yanıtından çıkarıldı.

**Ana çıktı:** [`Docs/INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](../INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md)

### Bulgular

| # | Bulgu | Kanıt |
|---|---|---|
| **B1** | Anonim envanter ucu **dört** statü döndürüyor: `200 / 401 / 403 / 429`. Hata gövdelerinin hepsi literal `null`. 08 §2.3 bunlardan yalnız ikisini tanıyor | runbook §1 |
| **B2** | `403` = envanter gizli — kütüphanenin varsayımı **doğrulandı** (hem `friendsonly` profil hem "profil açık + envanter gizli" vakası 403) | runbook §2 |
| **B3** | **`401` private değil** ve zincirde kayboluyor → kullanıcıya **kalıcı** bir durum *"tekrar deneyin"* diye sunuluyor. Üç vakada sebep *"Community profili kurulmamış"* | runbook §2 |
| **B4** | Rate limit penceresi **dakikadan uzun**: 10/dk'da ~90 sn'de 429; **4/dk'ya düşülmesine rağmen 429 sürdü**. `Retry-After` yok | runbook §3 |
| **B5** | `asset_properties` asset başına **`Wear Rating` (float)** ve **`Pattern Template`** taşıyor — anonim olarak | runbook §5 |
| **B6** | `(classid, instanceid)` bir item'ı **tanımlamıyor** (199 asset → 159 sınıf, en kalabalığı 9 kopya) → 02 §9.2'nin sayım tabanlı kararı **doğrulandı** | runbook §4.1 |
| **B7** | `owner_descriptions` / `cache_expiration` anonim görünümde **yok** → Trade Protection kilidinin bitiş tarihi okunamıyor; `tradable` sınıf düzeyinde | runbook §6 |
| **B8** | **TUZAK:** `market_tradable_restriction: 7` bir **kilit göstergesi değil** — `tradable: 1` olan serbest bir item'da da aynı değeri taşıyor. Sınıf **politikası**, item durumu değil | runbook §6.1 |
| **B9** | Son sayfada `more_items` / `last_assetid` **hiç gelmiyor** — "devam yok" sinyali `more_items: 0` değil, alanın **yokluğu** | runbook §4.2 |

### T122-B sonucu (proje sahibi capture'ı, 2026-08-13)

Proje sahibi kendi hesabından tek item'lık envanterinin JSON'unu verdi. `owner_descriptions`,
`owner_actions`, `cache_expiration` — **üçü de yok**; alan kümesi anonim görünümle **birebir aynı**
(fazladan ya da eksik tek alan yok, programatik olarak karşılaştırıldı).

**B7 kapanmadı** ve kapanmama sebebi dürüstçe kaydedilmelidir: capture'daki tek item `tradable: 1`, yani
**kilitli değil**. Dolayısıyla iki açıklama ayırt edilemiyor — (a) sahip-özel alanlar yalnız bir kilit
varken üretiliyor, (b) yanıt zaten anonim şekil. **Kilitsiz tek bir capture bu ikisini ayıramaz.**

Capture yine de B8 ve B9'u üretti — ikisi de tüketici tarafında yanlış okumaya açık alanlar.

> **Deney tasarımı düzeltmesi:** T122-B'nin kurgusu (sahip görünümünü oku) **yanlış görünüme** bakıyordu.
> Platform envanterleri **anonim** okur; doğru soru *"kilitli bir item ANONİM görünümde nasıl görünür"*dur.
> Doğru deney: hesapta kilitli bir item oluştur (market alımı veya oyun içi drop) → envanteri Public yap →
> **anonim** oku ve bu kilitsiz baseline ile karşılaştır. Sahip görünümü öğrenilse bile platformun
> göremediği bir şeyi anlatır.

### B3 — kullanıcıya dönen defekt (tam zincir)

```
Steam 401 → steamcommunity (users.js:599, yalnız 403+null özel-kasa) → generic Error
  → sidecar InventoryService.ts:165 → UNAVAILABLE → routes.ts:134 → HTTP 503
  → HttpSteamSidecarInventoryClient.cs:76-81 → Unavailable
  → T121 create ucu → 503 STEAM_UNAVAILABLE ("tekrar deneyin")
```

Fail-safe yönü **doğru** (asla "item yok" kanıtı üretmiyor); kırık olan kullanıcıya verilen talimat. Bu,
T121'in öldürdüğü çöktürmenin sınıfça aynısı bir katman yukarıda: ayrılmayan şey artık "item yok /
okunamadı" değil, **kalıcı / geçici**.

---

## Kabul Kriterleri Kontrolü

Plandaki dört kriter, kapsam bölünmesine göre:

| AC | Durum | Kanıt |
|---|---|---|
| İki gerçek hesap arasında trade + iki envanterin ham yanıtı | ✗ **Yapılamadı** — dış varsayım kırık (proje sahibi trade yapamıyor) | runbook §7 B1–B3, izolasyon stratejisiyle karşılandı |
| `classid`/`instanceid` beklendiği gibi mi · `assetid` değişiyor mu · Trade Protection nasıl işaretleniyor | **Kısmen** — `classid`/`instanceid` semantiği **ölçüldü** (B6) · Trade Protection'ın **anonim görünürlüğü ölçüldü** (B7: kilit tarihi okunamıyor) · `assetid` rotasyonu ölçülemedi (ikincil kaynak: `steam-tradeoffer-manager.d.ts:27-31` `new_assetid` sözleşmesi) | runbook §4.1, §6, §7 |
| Ham yanıtlar `Docs/INTEGRATION_RUNBOOKS/`'a kaydedildi | ✓ | runbook §1–§6; ham gövdeler §8'deki gerekçeyle özet olarak (üçüncü şahıs envanter içeriği = kişisel veri) |
| 02 §9.2 kanıt kuralı ve delivery timeout varsayılanı teyit/revize edildi | ✓ **teyit + revizyon** — sayım tabanlı kanıt **doğrulandı** (B6), aşınma/desen gerekçesi düzeltildi (B5), anonim okuma sınırı normatif not oldu (B7). Delivery timeout: T122 bir sayı **dayatmıyor** — gecikme ölçülemedi; gerekçe ve izolasyon runbook §7'de | 02 v3.1 |

**Dürüst özet:** dört kriterin ikisi tam, biri kısmen, biri yapılamadı karşılandı. Yapılamayan kriterin
riski kapatılmadı — **izole edildi** (runbook §7) ve kapanışı üretimden gelen ölçüme bağlandı.

---

## Etkilenen Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` | **Yeni** — ölçümün tam kaydı |
| `Docs/02_PRODUCT_REQUIREMENTS.md` | **v3.0 → v3.1** — §9.2 revizyonu (sayım zorunluluğu gerekçelendirildi, aşınma/desen gerekçesi düzeltildi, anonim okuma sınırı normatif not) |
| `Docs/DEFERRED_BACKLOG.md` | 2 yeni kalem + `P2P-FloatVerification` önkoşulu çürütüldü (⚪ → 🟡); 36 → **38 aktif satır** |
| `Docs/TASK_REPORTS/T122_REPORT.md` | Bu rapor |
| `Docs/IMPLEMENTATION_STATUS.md` | T122 ⏳ |
| `.claude/memory/MEMORY.md` | T122 kaydı |

**`backend/src`, `sidecar-steam/src`, `frontend/src` altında sıfır değişiklik** — 11 §P2.5 *"kod teslimi yok"*.

---

## Test Sonuçları

Üretim kodu değişmediği için test koşusu **gerekmiyor** (yalnız doküman). Ölçümün kendisi kanıttır;
tekrar üretme komutları runbook §8'de.

---

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** yok — ölçüm anonim, auth kullanılmadı, `STEAM_API_KEY` boş ve kullanılmadı
- **Auth/authorization etkisi:** yok
- **Input validation etkisi:** yok
- **Yeni dış bağımlılık:** yok
- **Kişisel veri:** üçüncü şahıs Steam envanterlerinin ham gövdeleri **repo'ya commit edilmedi** (yalnız
  anonimleştirilmiş bulgular); SteamID64'ler zaten public ve statü semantiğinin kanıtı olarak gerekli
- **Dış sisteme yük:** kasıtlı olarak sınırlandı — 429 gözlenince tarama **durduruldu**, kesin eşik
  ölçülmedi (Steam'e sürekli aşırı yük gerektirirdi), proje sahibinin IP'si korundu

---

## Commit & PR

- **Dal:** `task/T122-steam-inventory-measurement`
- **Commit:** `8d27733` — T122: Gerçek Steam ölçümü — salt-okunur canlı ölçüm + kapsam bölünmesi
- **PR:** [#230](https://github.com/turkerurganci/Skinora/pull/230)
- **Branch izolasyon check:** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+…'` → **`T122`** (tek)
- **CI ✓ PASS** — HEAD `b38327c`, run [`31726231187`](https://github.com/turkerurganci/Skinora/actions/runs/31726231187), **CI Gate `success`**

### CI'nin kendisi "sıfır üretim diff" iddiasını doğruladı

Job sonuçları:

| Sonuç | Job |
|---|---|
| `success` | Detect changed paths · **1. Lint** · **CI Gate** |
| `skipped` | 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build · **E2E (advisory, 8 leg)** |

`Detect changed paths` hiçbir kod yolunda değişiklik görmediği için tüm derleme/test job'ları atlandı. Bu,
raporun "üretim kodu değişmedi" iddiasının **bağımsız mekanik teyididir** — `git diff` benim ölçümüm,
job atlaması pipeline'ın kendi ölçümü.

**E2E notu:** T117'den beri kırmızı olan 8 advisory leg bu run'da **kırmızı değil, `skipped`** — değişiklik
hiçbir kod yoluna dokunmadığı için hiç koşmadılar. Yani T120/T121'de yapılan "aynı 8 leg, aynı imza"
karşılaştırması bu task için **konusuz**; yeni kırılma ihtimali yapısal olarak sıfır.

---

## Known Limitations / Follow-up

1. **Ölçülemeyen üç bilinmeyen (B1–B3, runbook §7):** teslimat gecikmesi · `assetid` rotasyonu ·
   `Item Certificate` kalıcılığı + cooldown'un anonim işaretlenmesi. **T125'i bloklamıyorlar** çünkü
   mantığı değil sabitleri belirliyorlar; izolasyon stratejisi runbook §7'de beş maddede tanımlı.
2. **Kapanış kapısı T125'e devredildi:** ilk N gerçek teslimatta ham envanter yanıtları saklanmalı ve insan
   incelemesinden geçmeden envanter kanıtına dayalı otomatik para bırakma açılmamalı; `DEPLOY_RUNBOOK`
   launch checklist'ine bağlanmalı.
3. **`P2P-InventoryUnauthorizedMapping`** (B3) — kod düzeltmesi bu görevin kapsamı değil (spike). 401'in
   sebebi tek başına netleşmedi (bir vakada profil `public` olduğu hâlde 401): düzeltme yeni bir statü
   ayrımı yapmadan önce sebebi netleştirmeli.
4. **`P2P-SteamRateLimitWindow`** (B4) — T120'nin 10/dk varsayılanı çürütülmedi ama "rahat" okuması
   çürütüldü; uzun pencereli ikinci bütçe katmanı + 429 backoff gerekiyor.
5. **T122-B yapıldı ama B7'yi kapatmadı** (yukarıda) — capture'daki item kilitli değildi. Doğru deney
   (kilitli item oluştur → envanteri Public yap → **anonim** oku → kilitsiz baseline ile karşılaştır)
   tasarlandı ve proje sahibine sunuldu; **proje sahibi kararı (2026-08-13): yapılmayacak.**
   **B7 "ölçülemedi" olarak kapanır, açık eylem maddesi değildir.** Sonucu tasarıma taşındı: T125'in kanıt
   değerlendirmesi item'ın **kilit durumuna dayanamaz** — 11 §P3'te T125 kabul kriteri olarak yazıldı.
   Bilinmeyen, bir varsayıma dönüşmeden önce tasarımdan **dışlandı**.

---

## Notlar

- **Working tree hygiene (Adım -1):** temiz — `git status --short` boş.
- **Main CI startup check (Adım 0):** son 3 main run `success` — `31524132478`, `31524132471`, `31508344655`.
- **Bağımlılık:** T121 ✓ Tamamlandı (doğrulama PASS, PR #229, main `4435ad6`).
- **Dış varsayımlar (Adım 4):**
  - *"İki gerçek Steam hesabı arasında trade yapılabilir"* → **KIRIK** (proje sahibi yapamıyor) → kapsam
    bölünmesi sunuldu ve onaylandı (yukarıda).
  - *"`steamcommunity.com` envanter ucu anonim okunabilir"* → **DOĞRULANDI**: `HTTP 200` + tam gövde alındı.
  - *"`sidecar-fake` bu davranışı kanıtlayamaz"* (plan notu) → **DOĞRULANDI ve genişledi**: ölçülen dört
    statüden ikisi (`401`, `429`) fake'te hiç modellenmemiş.
- **Ölçüm etiği:** istekler proje sahibinin residential IP'sinden çıktı. 429 gözlenir gözlenmez tarama
  durduruldu; kesin rate-limit eşiği bilinçli olarak **ölçülmedi**.
