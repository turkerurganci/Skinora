# Steam Envanter Okuma — Canlı Davranış Ölçümü — T122

Gerçek Steam'e karşı yapılmış **salt-okunur** ölçümün kaydı. Referans: 02 §9.2, 08 §2.3, 08 §2.6, 08 §2.7.

> **Bu runbook neden var:** T122'nin plandaki hâli iki gerçek Steam hesabı arasında bir trade yapılmasını
> şart koşuyordu (11 §P2.5). Proje sahibi bu ölçümü yapamayacağını bildirdi (2026-08-13). Ölçümün
> **trade gerektirmeyen kısmı** ayrıştırıldı ve canlı Steam'e karşı yapıldı; trade gerektiren kalan üç
> bilinmeyen §7'de izole edildi. `sidecar-fake` bu davranışların hiçbirini kanıtlayamaz — ne yazarsak onu
> döner. Buradaki her satır **gerçek `steamcommunity.com` yanıtından** çıkarılmıştır.

**Ölçüm künyesi**

| | |
|---|---|
| Tarih | 2026-08-13 (UTC 17:06–17:35) |
| Uç | `GET https://steamcommunity.com/inventory/{steamId64}/730/2?l=english&count={n}` (anonim, auth yok) |
| Yardımcı uç | `GET https://steamcommunity.com/profiles/{steamId64}?xml=1` (statü semantiğini ayırmak için) |
| Çıkış noktası | Proje sahibinin residential IP'si, TR (`steamCountry=TR`) |
| Toplam istek | ~45 (rate limit gözlemi §3'te — kasıtlı olarak erken durduruldu) |
| Örneklem | 6 elle seçilmiş + 20 rastgele SteamID64 + 5 `csgotraders` grup üyesi |

---

## §1 Statü Kümesi — Gerçekte Dönen Yanıtlar

Anonim envanter ucu ölçümde **dört** statü döndürdü. Başarısızlık gövdelerinin **hepsi** literal `null` (gzip'li 4 byte, `Content-Type: application/json`).

| Statü | Gövde | Anlamı (ölçülen) |
|---|---|---|
| `200` | `{assets, descriptions, asset_properties, more_items*, last_assetid*, total_inventory_count, success, rwgrsn}` | Envanter okundu |

<sup>\* `more_items` ve `last_assetid` **koşulludur** — yalnız devam eden bir sayfa varken gelirler; son sayfada anahtar olarak hiç bulunmazlar (§4.2, B9).</sup>
| `401` | `null` | **Private değil** — ayrı bir başarısızlık modu, §2 |
| `403` | `null` | Envanter gizli (**private**) |
| `429` | `null` | Rate limit, §3 |

Ham 403 yanıtının başlıkları:

```
HTTP/1.1 403 Forbidden
Server: nginx
Content-Type: application/json; charset=utf-8
Cache-Control: no-cache
Content-Length: 24            # gzip; decode edilince: null
```

> **08 §2.3 etkisi:** doküman envanter okumasını "üç durumdan biri" (Public/Private/Unavailable) olarak
> tanımlıyor ve bu **doğru bir soyutlama**; ama alttaki wire protokolünde dört statü var ve bunların
> ikisi (`401`, `429`) dokümanda hiç anılmıyor. §2 bu boşluğun kullanıcıya dönen bir sonucu olduğunu gösteriyor.

---

## §2 `401` ≠ `403` — ve Bu Ayrım Bugün Kayboluyor

### Ölçüm

`?xml=1` profil ucuyla çapraz kontrol edildi:

| SteamID64 | Envanter | `privacyState` | Profil mesajı |
|---|---|---|---|
| `76561197960287930` | **403** | `friendsonly` | — |
| `76561198311457678` | **403** | `public` | — (profil açık, **envanter** gizli) |
| `76561198347608388` | **401** | — | *"This user has not yet set up their Steam Community profile."* |
| `76561199494563496` | **401** | — | *"This user has not yet set up their Steam Community profile."* |
| `76561199601092865` | **401** | — | *"This user has not yet set up their Steam Community profile."* |
| `76561199593523305` | **401** | `public` | — (profil **açık**, yine de 401) |

**Kanıtlanan:** `403` = envanter gizli. Kütüphanenin varsayımı doğru; hem `friendsonly` profil hem de
"profil açık ama envanter gizli" vakası 403 üretiyor.

**Kanıtlanan:** `401` **private değil.** Üç vakada Community profili hiç kurulmamış. Dördüncü vakada profil
açık — yani 401 tek bir sebebe indirgenemiyor (bu runbook onu tek sebebe bağlamıyor).

### Kaybolduğu yer — tam zincir

```
Steam 401
  └─> steamcommunity kütüphanesi: yalnız `HTTP error 403 && body === null` özel-kasa
      (node_modules/steamcommunity/components/users.js:599-606) → 401 generic Error'a düşer
      └─> sidecar InventoryService: message !== 'This profile is private.'  → UNAVAILABLE
          (sidecar-steam/src/trade/InventoryService.ts:165)
          └─> sidecar route: UNAVAILABLE → HTTP 503
              (sidecar-steam/src/api/routes.ts:134)
              └─> backend HttpSteamSidecarInventoryClient: !IsSuccessStatusCode → Unavailable
                  (…/Steam/Application/Inventory/HttpSteamSidecarInventoryClient.cs:76-81)
                  └─> T121 create ucu: Unavailable → 503 STEAM_UNAVAILABLE  ← "tekrar deneyin"
```

**Sonuç:** Community profilini kurmamış bir satıcıya *"Steam şu an erişilemiyor, tekrar deneyin"* deniyor.
Bu durum **kalıcıdır** — kullanıcı profilini kurana kadar hiçbir tekrar denemede düzelmez. Kullanıcı
sonsuz bir retry döngüsüne yönlendiriliyor; doğru yönlendirme *"Steam Community profilinizi kurun"*.

> **Bu, T121'in öldürdüğü çöktürmenin sınıfça aynısı, bir katman yukarısı.** T121 "item yok"u
> "envanter okunamadı"dan ayırdı. Burada ayrılmayan şey **kalıcı ile geçici**: `Unavailable`'ın sözleşmesi
> "tekrar denenebilir" (07 §6.1, 503) ama içine kalıcı bir durum akıyor.
> Fail-safe yönü doğru (asla "item yok" kanıtı üretmiyor) — kırık olan **kullanıcıya verilen talimat**.

**Kapsam:** platformda SteamID OpenID login'den geldiği için hesap her zaman *vardır*; kırılan senaryo
"hesabı olmayan kullanıcı" değil, **Community profilini hiç kurmamış kullanıcı** — yeni Steam
hesaplarında yaygın bir durum.

**Sahiplik:** bu görev kod teslim etmez (11 §P2.5 *"spike, kod teslimi yok"*). Kayıt:
`DEFERRED_BACKLOG` → `P2P-InventoryUnauthorizedMapping`.

---

## §3 Rate Limit — 08 §2.6'nın Tahmini İlk Kez Ölçüldü

08 §2.6 IP başına **10–20 istek/dk** *tahmin* ediyor; T120 muhafazakâr uç olarak 10/dk'yı varsayılan yaptı
(`STEAM_COMMUNITY_REQUESTS_PER_MINUTE`). Ölçülen:

| Gözlem | Veri |
|---|---|
| 429 başlangıcı | ~90 sn içinde ~18 istek (6 sn aralık = **10/dk** sürdürülebilir hız) |
| Kısmi toparlanma | ~40 sn sonra normal yanıtlar geri geldi (17:19:09) |
| **Burst sonrası kalıcılık** | **15 sn aralıkta (4/dk) bile 429 sürdü** — 5 istekte 3× 429 |

**Çıkarım:** limitleyicinin penceresi **bir dakikadan uzun**. Yalnız dakikalık hıza bakan bir limiter
(bugünkü `RateLimitedQueue`, 10/60sn) uzun pencereli bir bütçeyi tüketip 429'a girebilir; dakikalık hız
"uyumlu" görünürken.

> **T120'nin 10/dk seçimi güvenli tarafta ama "bol" değil — sınırın hemen altında.** Bu ölçüm 10/dk'yı
> *çürütmüyor*, "10 rahat bir değer" okumasını çürütüyor. Üstelik ölçüm **residential TR IP'sinden**
> yapıldı; üretim datacenter IP'sinden çıkacak ve Steam datacenter aralıklarına tipik olarak **daha sert**
> davranır — yani gerçek üretim marjı buradan dar olabilir, geniş olamaz.

**429 yanıtı gövde taşımıyor** (`null`) ve `Retry-After` başlığı **gözlenmedi** — yani bekleme süresi
yanıttan okunamıyor, geri çekilme (backoff) tahmini olmak zorunda.

**Ölçümün kendi sınırı:** kesin eşik (pencere uzunluğu, bütçe) ölçülmedi. Ölçmek Steam'e kasıtlı ve
sürekli aşırı yük bindirmeyi gerektirirdi; proje sahibinin IP'si üzerinden bu yapılmadı. Kesin
karakterizasyon istenirse ayrı bir çıkış IP'sinden yapılmalıdır.

---

## §4 `200` Yanıtının Şekli

`total_inventory_count: 220` olan gerçek bir envanterden (`count=200`):

```jsonc
{
  "assets": [                      // 199 kayıt — İNCE, yalnız kimlik
    { "appid": 730, "contextid": "2", "assetid": "47366849065",
      "classid": "7993036716", "instanceid": "519977179", "amount": "1" }
  ],
  "descriptions": [ /* 159 kayıt — (classid,instanceid) başına BİR tane */ ],
  "asset_properties": [ /* 91 kayıt — asset başına, §5 */ ],
  "more_items": 1,                 // sayfalama devam ediyor
  "last_assetid": "44319851424",   // sonraki sayfanın start_assetid'i
  "total_inventory_count": 220,    // sayfadan bağımsız GERÇEK toplam
  "success": 1,
  "rwgrsn": -2
}
```

`descriptions[]` alanları: `appid, classid, instanceid, currency, background_color, icon_url, descriptions,
tradable, actions, name, name_color, type, market_name, market_hash_name, market_actions, commodity,
market_tradable_restriction, market_marketable_restriction, marketable, tags, sealed,
market_bucket_group_name, market_bucket_group_id, sealed_type, market_name_inside_group, market_bucket_id`

### §4.1 `(classid, instanceid)` bir item'ı **tanımlamaz**

199 asset → **159 ayrık** `(classid, instanceid)`. 19 sınıfın birden fazla kopyası var; en kalabalığı **9 asset**.

> **02 §9.2'nin sayım tabanlı kanıt kararı bununla doğrulanmıştır.** "Beklenen item sayısı referans anlık
> görüntüye göre arttı" ifadesi zorunlu: sınıf başına *varlık* kontrolü yapan bir tasarım, alıcının o
> skin'den zaten bir kopyası varken teslimatı hiç göremezdi.

### §4.2 Sayfalama

`count=200` → 199 asset döndü, `more_items: 1`, `last_assetid` dolu. 08 §2.3'ün `start_assetid`/`more_items`
döngüsü gerçek; `steamcommunity` kütüphanesi bu döngüyü kendi içinde yürütüyor (`InventoryService.ts:96`).
`total_inventory_count` sayfadan bağımsız gerçek toplamı veriyor — **eksik sayfalanmış bir okumanın tespiti
için kullanılabilir** ve bugün kullanılmıyor.

**Son sayfada `more_items` ve `last_assetid` alanları hiç gelmiyor** (T122-B capture'ı, tek item'lık envanter:
üst seviye anahtarlar yalnız `assets, descriptions, asset_properties, total_inventory_count, success, rwgrsn`).
Yani "devam yok" sinyali `more_items: 0` **değil**, alanın **yokluğu**dur. `more_items`'ı her yanıtta var
sayan bir tüketici `undefined` okur; JS'te bu doğru dallanır (`if (more_items)`), ama alanın varlığını
şart koşan bir şema doğrulaması son sayfayı geçersiz sayar.

---

## §5 `asset_properties` — 02 §9.2'yi Değiştiren Keşif

Anonim yanıt, **asset başına** şu alanları taşıyor:

```jsonc
{ "appid": 730, "contextid": "2", "assetid": "45172914261",
  "asset_properties": [
    { "propertyid": 1, "int_value": "744",                  "name": "Pattern Template" },
    { "propertyid": 2, "float_value": "0.0608838982880115509", "name": "Wear Rating" },
    { "propertyid": 6, "string_value": "B0A0654…3FF0",      "name": "Item Certificate" }
  ] }
```

Gözlenen property tipleri: **Pattern Template · Wear Rating · Item Certificate · Name Tag · Charm Template**.
199 asset'in 91'inde mevcut (silahlarda var, madalya/rozet gibi kolektiblelerde yok).

**Etki — 02 §9.2 son maddesi artık teknik olarak yanlış.** Doküman şunu diyordu:

> *"Aynı sınıftan iki item arasındaki aşınma/desen farkı otomatik doğrulamanın kapsamı dışındadır — bu
> ayrım `WRONG_ITEM` dispute'una tabidir."*

Aşınma (`Wear Rating`, float) ve desen (`Pattern Template`) **anonim olarak, teslimat doğrulamasının zaten
yaptığı okumanın içinde** geliyor. Kapsam dışı olmalarının teknik gerekçesi ortadan kalktı.

**Bu runbook kuralı değiştirmiyor, gerekçesini düzeltiyor** (revizyon 02 §9.2'de yapıldı): ayrım artık
"veri yok" diye değil, **kanıt eşiği kararı** olarak kapsam dışı — hangi float farkının "yanlış item"
sayılacağı bir ürün kararıdır ve T125/T130'un konusudur.

> **`Item Certificate`** asset başına benzersiz görünen bir string. Trade'i hayatta kalıyorsa `assetid`
> değişse bile item'ı takip etmenin doğru anahtarı olabilir — bu, kanıt motorunun temelini değiştirir.
> **Ölçülemedi** (§7); anlamı ve kalıcılığı doğrulanmadan hiçbir tasarım buna dayandırılmamalıdır.

---

## §6 Anonim Görünümün Sınırı — Cooldown Tarihi Okunamıyor

Ölçülen envanterlerde `owner_descriptions` ve `cache_expiration` alanları **yok** (`undefined`). Bunlar
Steam'in yalnız **sahibinin kendi oturumuna** döndürdüğü alanlar; "Tradable After &lt;tarih&gt;" bilgisi
buradan gelir.

Platform envanteri **anonim** okuyor (sidecar'ın Steam hesabı kimlik bilgisi taşımaması T115/T133 kararı).
Dolayısıyla:

- Bir item'ın **7 günlük Trade Protection kilidinin bitiş tarihi anonim görünümde okunamıyor.**
- `tradable` alanı `descriptions[]` üzerinde, yani **sınıf seviyesinde** — aynı skin'in biri kilitli biri
  serbest iki kopyası tek kayıtla temsil ediliyor. Asset başına kilit durumu bu yapıda ifade edilemiyor
  (Steam'in kilitli kopyayı ayrı bir `instanceid`'ye ayırıp ayırmadığı **ölçülemedi** — §7).
- Gözlenen tüm `market_tradable_restriction` değerleri `7` — 7 gün kuralı sınıf seviyesinde sabit olarak
  görünüyor, asset başına geri sayım olarak değil.

Bulunan 14 adet `tradable: 0` item'ın hepsi **doğası gereği** trade edilemez (madalya, rozet, müzik kiti) —
hiçbiri cooldown vakası değil. Cooldown'lu bir silahın anonim görünümde nasıl işaretlendiği, yeterli sayıda
public envanter örneklenemediği için **ölçülemedi** (§3 rate limit).

### §6.1 TUZAK — `market_tradable_restriction: 7` bir kilit göstergesi **değildir**

Proje sahibinin kendi hesabından alınan capture (T122-B, 2026-08-13) bunu kesinleştirdi: **`tradable: 1`**
olan, yani **şu anda serbestçe trade edilebilen** bir item'da da `market_tradable_restriction: 7` geliyor.

```jsonc
{ "name": "Tec-9 | Groundwater", "tradable": 1, "marketable": 1,
  "market_tradable_restriction": 7, "market_marketable_restriction": 7 }
```

Alan, item'ın **o anki kilit durumunu** değil, sınıfının **politikasını** taşıyor: *"bu sınıftan bir item
market/trade yoluyla edinildiğinde 7 gün kısıtlanır."* Kilitli ve serbest item'da **aynı değeri** alır.

> **Neden tuzak:** `market_tradable_restriction`'ı "bu item 7 gün kilitli" diye okuyan bir tüketici, serbest
> her item'ı kilitli sanır. Kilidin tek göstergesi `tradable` alanıdır — o da §6'da anlatıldığı gibi **sınıf
> düzeyindedir** ve anonim görünümde bitiş tarihi taşımaz. T125 bu alanı kanıt olarak kullanmamalıdır.

### §6.2 T122-B — sahip oturumu capture'ı (kısmi sonuç)

Proje sahibi kendi hesabından tek item'lık envanterinin JSON'unu verdi. Sonuç:

| Alan | Sahip capture'ında |
|---|---|
| `owner_descriptions` | **YOK** |
| `owner_actions` | **YOK** |
| `cache_expiration` | **YOK** |
| Alan kümesi | Anonim görünümle **birebir aynı** — fazladan ya da eksik tek alan yok |

> **Doğrulamada teyit edildi (2026-08-13):** capture ([`data/T122_owner_capture.json`](data/T122_owner_capture.json))
> ile validator'ın anonim ölçümü programatik olarak karşılaştırıldı — aynı sınıftaki (silah) bir
> `descriptions[]` kaydında **her iki tarafta da 26 alan, sıfır fark**. `owner_descriptions`,
> `owner_actions`, `cache_expiration` **hiçbirinde** yok. Capture aynı zamanda B9'un ikinci bağımsız
> kanıtıdır: tek item'lık envanterde de (`total_inventory_count: 1`) `more_items` ve `last_assetid`
> anahtarları gelmiyor.

**B7 kapanmadı.** Capture'daki tek item `tradable: 1`, yani **kilitli değil**; dolayısıyla iki açıklama
ayırt edilemiyor: (a) sahip-özel alanlar yalnız bir kilit/hold varken üretiliyor, (b) yanıt zaten anonim
şekil (oturum çerezleri bu uçta etkili değil). Tek bir kilitsiz capture bu ikisini ayıramaz.

> **Doğru deney sahip görünümü değil, anonim görünümdür.** Platform envanterleri anonim okuduğu için asıl
> soru *"kilitli bir item ANONİM görünümde nasıl görünür"*dur — sahip görünümü öğrenilse bile platformun
> **göremediği** bir şeyi anlatır. Deney tasarımı: hesapta kilitli bir item oluştur (market alımı veya oyun
> içi drop — ikisi de 7 gün trade kısıtı doğurur) → envanteri Public yap → **anonim** oku → buradaki
> kilitsiz baseline ile karşılaştır. Tek fark, kilidin platforma görünen imzasıdır.

**KARAR (proje sahibi, 2026-08-13): bu deney yapılmayacak.** B7 bu görevde **ölçülemedi** olarak kapanır ve
açık bir eylem maddesi değildir. Kilit imzası bilinmediği için T125'in kanıt değerlendirmesi item'ın **kilit
durumuna dayanamaz** — bu, T125'e kabul kriteri olarak yazıldı (11 §P3). Bilinmeyen, bir varsayıma
dönüşmeden önce tasarımdan **dışlandı**.

---

## §7 Ölçülemeyenler — ve Neden T125'i Bloklamıyorlar

Trade yapılmadan **hiçbir yolla** ölçülemeyen üç bilinmeyen:

| # | Bilinmeyen | Neden ölçülemez |
|---|---|---|
| B1 | Kabul → item'ın alıcı envanterinde görünmesi arasındaki gecikme | Gerçek bir trade'in kabul anı gerekiyor |
| B2 | `assetid`'nin trade'de gerçekten değişmesi | Aynı fiziksel item'ın iki sahipteki kaydını eşleştirmek gerekiyor |
| B3 | `Item Certificate`'in trade'i hayatta kalması + cooldown'un anonim işaretlenmesi | Aynı sebep |

**B2 için ikincil kaynak var (ölçüm değil):** repo'nun kendi tip tanımı — `new_assetid` *"the id the item now
carries in the recipient's inventory after the trade settled (the original assetid is stale once moved)"*
(`sidecar-steam/src/types/steam-tradeoffer-manager.d.ts:27-31`). Kütüphane seviyesinde belgelenmiş bir
sözleşme; varsayımdan güçlü, ölçümden zayıf.

### İzolasyon stratejisi (proje sahibi kararı, 2026-08-13)

Bu üçü **mantığı değil sabitleri** belirliyor. T125 aşağıdaki kurallara uyduğu sürece bilinmeyenlerden
bağımsız yazılabilir:

1. **`BUYER_CONFIRMED` tek başına yeterli yol olarak kalır.** Zaten öyle
   (`DeliveryEvidence.IsSufficientForDelivery`) — alıcının onayı kendi aleyhine olduğu için gecikme
   ölçümüne ihtiyaç duymaz.
2. **Envanter kanıtı tek başına para hareketi tetiklemez.** `SELLER_ASSET_GONE ∧ INVENTORY_DELTA`
   konjonksiyonu korunur; yanlış-teslimat imzası (`IsMisdeliverySignature`) **admin'e** gider, para
   hareketine değil — yani B1/B2 yanlışsa sonuç bir insana düşer, bir ödemeye değil.
3. **Gecikmeye duyarlı her sayı config'de kalır.** `delivery_timeout_minutes` bugün zaten
   `Unconfigured` (`SystemSettingSeed.All`, satır Id 6) — T122 bir varsayılan **dayatmıyor**, muhafazakâr
   yüksek bir değerle açılmasını ve ölçüm geldiğinde daraltılmasını öneriyor.
   *(Anahtar T122 sırasında `trade_offer_buyer_timeout_minutes` adındaydı; T123'te
   `delivery_timeout_minutes` olarak yeniden adlandırıldı — 06 §8. Ölçüm iddiası değişmedi.)*
4. **T124'ün "tüketmeyen kapı" kararı bu riski zaten karşılıyor:** teslimat timeout'u T127'ye kadar iptal
   uygulamıyor. Yani B1 yanlış tahmin edilse bile kimsenin işlemi haksız iptal edilmiyor.
5. **Kanıt motoru saf kalır** (T125 AC'si). Ölçüm sonradan geldiğinde **sabitler** değişir, mantık değişmez.

### Kapanış kapısı (ölçüm üretimden gelsin)

Manuel spike yerine: ilk **N** gerçek teslimatta alıcı+satıcı envanterinin ham yanıtı saklanır ve **insan
incelemesinden geçmeden** envanter kanıtına dayalı otomatik para bırakma açılmaz. Böylece B1–B3 üretimin
kendisinden ölçülür. Bu kapı `DEPLOY_RUNBOOK` launch checklist'ine bağlanmalıdır — sahiplik T125.

> **UYGULANDI (T125, 2026-08-14) — bir sapmayla.** Kapının kendisi kuruldu:
> `delivery.inventory_evidence_auto_release_enabled` (06 §8, seed default `false`), kanıt tablosu
> `DeliveryEvidenceCaptures` (06 §3.5a) ve açma prosedürü **DEPLOY_RUNBOOK §H**.
>
> **Sapma — "ham yanıt" saklanmıyor; kapsam bilinçli olarak daraltıldı.** Sidecar envanteri
> `steamcommunity` kütüphanesi üzerinden okur ve kütüphane sayfalama + `assets[] × descriptions[] ×
> asset_properties` birleştirmesini **kendi içinde** yapıp `CEconItem[]` döndürür (`components/users.js`).
> Kütüphanenin **public API'ı** ham gövdeyi vermez; erişilebilir en zengin katman `CEconItem`'dır ve
> `asset_properties` orada mevcuttur (`classes/CEconItem.js:89-91`).
>
> *(Düzeltme — T125 doğrulaması, 2026-08-14: bu satır önce "ham JSON hiçbir noktada dışarı verilmez"
> diyordu. Bu **fazla kesin**: `SteamCommunity.prototype.httpRequest` değiştirilebilir bir seam'dir ve ham
> sayfa gövdeleri oradan geçer — T125'in kendi contract testi `SteamInventoryReadContract.test.ts` tam o
> seam'i kullanıyor. Yani ham yakalama teknik olarak **mümkündü**; yapılmama sebebi imkânsızlık değil,
> aşağıdaki kapsam kararıdır. Bu ayrım önemli: "imkânsız" diye okuyan bir sonraki görev, var olmayan bir
> sınırı veri sanardı.)*
>
> **Kapsam kararı:** ham gövdeyi saklamak, kütüphanenin private bir metodunu sarmalamayı (sürüm
> yükseltmelerinde sessizce kırılabilir) ve iki tarafın **tüm** envanterini — işlemle ilgisiz üçüncü şahıs
> varlıkları dahil — DB'ye indirmeyi gerektirirdi (§8 kişisel veri). Kanıt kaydı bu yüzden işlemin **kendi
> item sınıfıyla** sınırlandı.
>
> **Onun yerine saklanan:** işlemin **kendi item sınıfı** için iki tarafın gözlemi — görünürlükler, baseline ↔
> gözlenen sayımlar, asset ID listeleri, **asset başına `asset_properties`** ve gecikme türetmek için zaman
> damgaları. B1 (`ObservedAt` − `PaymentReceivedAt`), B2 (`SellerItemAssetId` ↔ `NewAssetIds`) ve
> B3 (`Item Certificate` iki tarafta) **üçü de bu kayıttan cevaplanabilir**; kaybedilen tek şey işlemle
> ilgisiz asset'lerin gövdesidir, ki o zaten üçüncü şahıs verisidir (§8).
>
> **Ölçüm nasıl işlenir:** DEPLOY_RUNBOOK §H.3 adım 3 — inceleme sonucu bu bölüme (§7) geri yazılır ve
> B1–B3'ün "ölçülemez" satırları kapatılır veya revize edilir.

---

## §8 Ham Veri

**Neyin commit edildiği, neyin edilmediği:**

| Kaynak | Repo'da | Gerekçe |
|---|---|---|
| Proje sahibinin kendi tek-item capture'ı (T122-B) | ✓ [`data/T122_owner_capture.json`](data/T122_owner_capture.json) — ham gövde, olduğu gibi | Sahibin kendi hesabı, üçüncü şahıs verisi yok; yanıt SteamID64 taşımıyor. **B8** ve **B9**'un birincil kanıtı ve ikisi de T125 kabul kriteri oldu (11 §P3) → repo içinden tekrar üretilebilir olmalı. Commit kararı T122 doğrulamasında alındı (2026-08-13) |
| Üçüncü şahıs envanterlerinin ham gövdeleri (~45 istek) | ✗ | Başka kullanıcıların envanter içeriği = kişisel veri. §1–§6'da **türetilmiş bulgu** olarak özetlendi |
| Bağımsız doğrulama ölçümü (validator) | ✓ [`data/T122_validation_shape.json`](data/T122_validation_shape.json) | Yalnız **şekil** — SteamID / assetid / classid / item adı taşımaz |

> **Dikkat — §2 anonim değildir.** Yukarıdaki "kişisel veri" gerekçesi ham *envanter içeriği* içindir.
> §2'nin tablosu, statü semantiğinin kanıtı olarak **altı gerçek SteamID64'ü** profil ve envanter gizlilik
> durumlarıyla birlikte listeler. SteamID64 public bir tanımlayıcıdır ve bu repo private'tır, ama bu bölüm
> "anonimleştirilmiş özet" **değildir** — bulguyu doğrulamak isteyen okuyucunun aynı hesapları sorgulaması
> gerektiği için bilinçli olarak bırakılmıştır.

### Bağımsız doğrulama (validator, 2026-08-13)

Bu runbook'un ölçüme dayalı iddiaları, yapım turundan **bağımsız** bir oturumda, ayrı bir istemciden,
6 salt-okunur istekle yeniden üretildi. Sonuç: **B1, B2, B3, B5, B6, B7, B8, B9 birebir tuttu.**

| İddia | Yeniden üretilen kanıt |
|---|---|
| B1/B2 (§1, §2) | `403` + gövde literal `null` (4 byte) |
| B3 (§2) | `401` + `null`; `?xml=1` → *"has not yet set up their Steam Community profile"* |
| B2 ince ayrım (§2) | `privacyState=public` **olan** bir hesabın envanteri yine `403` |
| B3 ince ayrım (§2) | profili `public` olan bir hesapta yine `401` — 401 tek sebebe indirgenemiyor |
| B5 (§5) | `asset_properties` anonim; gözlenen adlar `Pattern Template · Wear Rating · Item Certificate · Name Tag · Charm Template` |
| B6 (§4.1) | 219 asset → **174 ayrık** `(classid,instanceid)`, en kalabalık sınıf **9 kopya** |
| B7 (§6) | `owner_descriptions` ve `cache_expiration` **yok** |
| B8 (§6.1) | Envanterdeki **tüm** `market_tradable_restriction` değerleri `7` — `tradable: 1` ve `tradable: 0` ayrımsız |
| B9 (§4.2) | Tam sayfa (`count=500`, 219/220 asset) yanıtında `more_items` ve `last_assetid` **anahtarları yok** |
| B4 (§3) | **Yeniden üretilmedi** — Steam'e kasıtlı aşırı yük gerektirirdi (§3'ün kendi gerekçesi) |

`data/T122_validation_shape.json` bu tablonun makine tarafından üretilmiş hâlidir. B6'nın sayıları
`total_inventory_count: 220` ile eşleştiği için ölçümün **aynı envanterden** geldiği değerlendirilmiştir —
yani B6 bağımsız bir örneklem değil, bağımsız bir **yeniden üretimdir**; şekil iddiaları (B5/B7/B8/B9)
örneklemden bağımsızdır.

### Tekrar üretmek için

```bash
# Tek bir public envanterin ham yanıtı
curl -s --compressed "https://steamcommunity.com/inventory/<STEAMID64>/730/2?l=english&count=200"

# Statü semantiğini ayırmak için profil ucu
curl -s --compressed "https://steamcommunity.com/profiles/<STEAMID64>?xml=1"
```

> **Tekrar ederken:** 4 istek/dk'yı aşma ve burst yapma (§3). Steam'in limiti dakikadan uzun pencereli;
> art arda birkaç dakikalık tarama IP'yi 429'a sokar.
