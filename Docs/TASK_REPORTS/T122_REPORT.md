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

Aşağıdaki tablo **doğrulama sonrası** hâldir; yapım turunun kendi verdict'i ile validator'ın verdict'i
arasındaki iki fark "Doğrulama" bölümünde açıkça listelenmiştir.

| AC | Yapım | **Validator** | Kanıt |
|---|---|---|---|
| İki gerçek hesap arasında trade + iki envanterin ham yanıtı | ✗ | **✗ Karşılanmadı** | Trade yapılmadı. Dış varsayım kırık; yeniden yapımla kapanmaz. Kapsam bölünmesi 11 §P2.5'e işlendi (doğrulama bulgusu 1) |
| `classid`/`instanceid` beklendiği gibi mi · `assetid` değişiyor mu · Trade Protection nasıl işaretleniyor | ~ | **~ Kısmi** | `classid`/`instanceid` **ölçüldü** (B6, bağımsız yeniden üretildi: 219 asset → 174 ayrık, maks. 9 kopya) · Trade Protection'ın anonim görünürlüğü **ölçüldü** (B7) · `assetid` rotasyonu **ölçülemedi** — yalnız ikincil kaynak (`steam-tradeoffer-manager.d.ts:27-31`) |
| Ham yanıtlar `Docs/INTEGRATION_RUNBOOKS/`'a kaydedildi | ✓ | **~ Kısmi** | Üçüncü şahıs gövdeleri commit edilmedi (kişisel veri — gerekçe geçerli). Doğrulamada eklendi: sahibin ham capture'ı `data/T122_owner_capture.json` ✓ + anonimleştirilmiş şekil artefaktı `data/T122_validation_shape.json` ✓ (runbook §8) |
| 02 §9.2 kanıt kuralı **ve** delivery timeout varsayılanı teyit/revize edildi | ✓ | **~ Kısmi** | 02 §9.2 gerçekten ✓ (v3.1; kanıtı bağımsız yeniden üretildi). **Delivery timeout varsayılanı ne teyit ne revize edildi** — gecikme ölçülemedi. Doğrulamada `DEPLOY_RUNBOOK` §A#6'ya uyarı eklendi (bulgu 2) |

**Dürüst özet:** dört kriterden biri **karşılanmadı**, üçü **kısmi**. Karşılanmayan kriterin riski
kapatılmadı — **izole edildi** (runbook §7) ve kapanışı T125 launch kapısına bağlandı.

---

## Etkilenen Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` | **Yeni** — ölçümün tam kaydı |
| `Docs/02_PRODUCT_REQUIREMENTS.md` | **v3.0 → v3.1** — §9.2 revizyonu (sayım zorunluluğu gerekçelendirildi, aşınma/desen gerekçesi düzeltildi, anonim okuma sınırı normatif not) |
| `Docs/DEFERRED_BACKLOG.md` | 2 yeni kalem + `P2P-FloatVerification` önkoşulu çürütüldü (⚪ → 🟡); 36 → **38 aktif satır** |
| `Docs/TASK_REPORTS/T122_REPORT.md` | Bu rapor |
| `Docs/IMPLEMENTATION_STATUS.md` | T122 durumu |
| `.claude/memory/MEMORY.md` | T122 kaydı |
| `Docs/11_IMPLEMENTATION_PLAN.md` | T125'e 5 yeni AC + T122 notu · **doğrulama turu:** T122 bloğunun kendi kabul kriterleri revize edildi (bulgu 1) |
| `Docs/DEPLOY_RUNBOOK.md` | **Doğrulama turu:** §A tablosuna "#6 uyarısı" — `trade_offer_buyer_timeout_minutes` örneği ölçülmemiş (bulgu 2) |
| `Docs/INTEGRATION_RUNBOOKS/data/T122_owner_capture.json` | **Doğrulama turu:** T122-B ham capture gövdesi (bulgu 3) — B8/B9'un birincil kanıtı |
| `Docs/INTEGRATION_RUNBOOKS/data/T122_validation_shape.json` | **Doğrulama turu:** anonimleştirilmiş şekil artefaktı (bulgu 3) |

**`backend/src`, `sidecar-steam/src`, `frontend/src` altında sıfır değişiklik** — 11 §P2.5 *"kod teslimi yok"*.

---

## Test Sonuçları

Üretim kodu değişmediği için test koşusu **gerekmiyor** (yalnız doküman). Ölçümün kendisi kanıttır;
tekrar üretme komutları runbook §8'de.

---

## Doğrulama

Bağımsız doğrulama chat'i, 2026-08-13 — yapım raporu görülmeden başlatıldı, verdict önce bağımsız oluşturuldu.

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (bulgu 1 kapatıldıktan sonra; ilk verdict ⛔ BLOCKED — `PLAN_CORRECTION_REQUIRED`) |
| Bulgu sayısı | 4 (1× S3, 3× S1) — **hepsi doğrulama turunda kapatıldı** |
| Düzeltme gerekli mi | Yapıldı — düzeltmeler bu dalda |
| Kapılar | Adım -1 working tree ✓ temiz · Adım 0 main CI son 3 run `success` (`31524132478`, `31524132471`, `31508344655`) · Adım 0b repo memory ✓ T122 satırı var · Adım 8a task branch CI ✓ |

### Bağımsız ölçüm — runbook iddiaları sıfırdan yeniden üretildi

Validator, canlı `steamcommunity.com`'a karşı **ayrı bir oturumdan ve istemciden** 6 salt-okunur istek yaptı
(16 sn aralıklı, runbook §8'in kendi uyarısına uygun). Makine çıktısı:
[`data/T122_validation_shape.json`](../INTEGRATION_RUNBOOKS/data/T122_validation_shape.json).

| Bulgu | Yeniden üretildi mi |
|---|---|
| B1 / B2 — `403` + gövde literal `null` = envanter gizli | ✓ |
| B2 ince ayrım — `privacyState=public` olan hesabın envanteri yine `403` | ✓ |
| B3 — `401` + `null`, `?xml=1` → *"has not yet set up their Steam Community profile"* | ✓ |
| B3 ince ayrım — profili `public` olan bir hesapta da `401` (tek sebebe indirgenemiyor) | ✓ |
| B5 — `asset_properties` anonim; 5 property adı birebir | ✓ |
| B6 — 219 asset → **174 ayrık** `(classid,instanceid)`, en kalabalık sınıf **9 kopya** | ✓ |
| B7 — `owner_descriptions` / `cache_expiration` yok | ✓ |
| B8 — `market_tradable_restriction` **tüm** kayıtlarda `7`; `tradable:1` ve `tradable:0` ayrımsız | ✓ (iddiadan güçlü) |
| B9 — tam sayfada `more_items` / `last_assetid` **anahtarları yok** | ✓ |
| B4 — rate limit penceresi | **Bilinçli olarak yeniden üretilmedi** — kesin eşik ölçümü Steam'e kasıtlı aşırı yük demek |

Kod referanslarının tamamı yerinde doğrulandı: `users.js:599` (`403 && body===null` özel-kasa) ·
`InventoryService.ts:165` · `routes.ts:134` (`UNAVAILABLE` → 503) · `HttpSteamSidecarInventoryClient.cs:76-81` ·
`steam-tradeoffer-manager.d.ts:27-31` · `SystemSettingSeed.cs:37` (`Unconfigured`). **B3 zinciri gerçek.**

### Bulgular ve kapanışları

| # | Sev | Bulgu | Kapanış |
|---|---|---|---|
| 1 | **S3** | **Plan düzeltilmemişti.** 11 §P2.5'teki T122 bloğu `main` ile bayt bayt aynıydı; onaylanan A/B/C bölünmesi rapora, runbook'a, status'e ve memory'ye yazılmış ama **kabul kriterlerinin kaynağı olan plana** yazılmamıştı. Status ✓ yapılsaydı, plan gerçekleşmeyecek bir trade ölçümü talep ederken görev "tamamlandı" görünecekti — F7 gate check'in traceability taraması ya sessizce rasyonelleştirirdi ya geç keşfederdi | 11 §P2.5 T122 bloğu revize edildi: gerçekleşen kabul kriterleri + `KAPSAM BÖLÜNMESİ` + `ÖLÇÜLEMEYEN` notları (proje sahibi onayı, 2026-08-13) |
| 2 | S1 | AC4'ün ikinci yarısı açıkken ✓ işaretliydi. Repoda o ayarın tek somut sayısı `DEPLOY_RUNBOOK` §A#6: `trade_offer_buyer_timeout_minutes` örnek **60 dk**, hâlâ custodial dönem etiketiyle — oysa v3.0'da bu **satıcının teslimat penceresi** ve runbook §7.3 "muhafazakâr **yüksek** değer" öneriyor. İki artefakt ters yöne bakıyor, çapraz referans yok | `DEPLOY_RUNBOOK` §A tablosuna "#6 uyarısı" notu eklendi; AC4 ✓ → ~ |
| 3 | S1 | AC3 ✓ işaretliydi ama ham gövde yok. Üçüncü şahıs gerekçesi haklı — ancak B8/B9'un tek kaynağı olan **sahibin kendi** capture'ında bu engel yok ve ikisi de T125 kabul kriteri oldu | İki artefakt commit edildi: `data/T122_owner_capture.json` (ham gövde) + `data/T122_validation_shape.json` (anonimleştirilmiş şekil). Capture programatik olarak anonim ölçümle karşılaştırıldı: **26/26 alan, sıfır fark** — raporun "birebir aynı" iddiası teyitli. AC3 ✓ → ~ (üçüncü şahıs gövdeleri hâlâ yok, gerekçesiyle) |
| 4 | S1 | Runbook §8 *"§1–§6'daki tüm bulgular **anonimleştirilmiş** özet"* diyordu; §2 altı gerçek SteamID64'ü gizlilik durumlarıyla listeliyor — ikisi aynı anda doğru olamaz | §8 yeniden yazıldı: neyin commit edildiği/edilmediği tablosu + §2'nin anonim **olmadığı** açık uyarısı. Ayrıca §1'e `more_items`/`last_assetid`'in koşullu olduğu dipnotu eklendi |

### Güvenlik kontrolü

- Secret sızıntısı: **temiz** — diff'te cookie / token / `steamLoginSecure` / key deseni yok
- Auth / authorization etkisi: **yok** · Input validation etkisi: **yok** · Yeni bağımlılık: **yok**
- Üretim kodu: `backend/src`, `sidecar-steam/src`, `frontend/src` altında **sıfır** değişiklik
- Eklenen veri artefaktı SteamID64 / assetid / classid / item adı taşımıyor (grep ile doğrulandı)

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
- **CI ✓ PASS** — yapım turu HEAD `b38327c`, run [`31726231187`](https://github.com/turkerurganci/Skinora/actions/runs/31726231187), **CI Gate `success`**
- **CI ✓ PASS** — doğrulama öncesi son HEAD `4818f49`, run [`31729663319`](https://github.com/turkerurganci/Skinora/actions/runs/31729663319), **CI Gate `success`** (aynı `skipped` profili — doküman-only)

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
6. ~~Sahip capture'ının ham JSON'u commit kuyruğunda~~ → **kapandı** (doğrulama bulgusu 3, proje sahibi
   onayı 2026-08-13): [`data/T122_owner_capture.json`](../INTEGRATION_RUNBOOKS/data/T122_owner_capture.json)
   ham gövde olarak commit edildi. B8 ve B9 T125'in kabul kriteri olduğu için ikisinin de birincil kanıtı
   artık repo içinden tekrar üretilebilir.
7. **Delivery timeout varsayılanı hâlâ açık** (doğrulama bulgusu 2): `DEPLOY_RUNBOOK` §A#6'daki 60 dk
   ölçülmemiş bir örnektir ve uyarı notu eklendi, ama **gerçek launch değeri kararı verilmedi**. Sahiplik
   T123 (adlandırma) → T124 (tüketim) → T125 (launch kapısı) zincirinde.

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
