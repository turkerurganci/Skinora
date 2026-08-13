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
| `200` | `{assets, descriptions, asset_properties, more_items, last_assetid, total_inventory_count, success, rwgrsn}` | Envanter okundu |
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
3. **Gecikmeye duyarlı her sayı config'de kalır.** `trade_offer_buyer_timeout_minutes` bugün zaten
   `Unconfigured` (`SystemSettingSeed.cs:37`) — T122 bir varsayılan **dayatmıyor**, muhafazakâr yüksek bir
   değerle açılmasını ve ölçüm geldiğinde daraltılmasını öneriyor.
4. **T124'ün "tüketmeyen kapı" kararı bu riski zaten karşılıyor:** teslimat timeout'u T127'ye kadar iptal
   uygulamıyor. Yani B1 yanlış tahmin edilse bile kimsenin işlemi haksız iptal edilmiyor.
5. **Kanıt motoru saf kalır** (T125 AC'si). Ölçüm sonradan geldiğinde **sabitler** değişir, mantık değişmez.

### Kapanış kapısı (ölçüm üretimden gelsin)

Manuel spike yerine: ilk **N** gerçek teslimatta alıcı+satıcı envanterinin ham yanıtı saklanır ve **insan
incelemesinden geçmeden** envanter kanıtına dayalı otomatik para bırakma açılmaz. Böylece B1–B3 üretimin
kendisinden ölçülür. Bu kapı `DEPLOY_RUNBOOK` launch checklist'ine bağlanmalıdır — sahiplik T125.

---

## §8 Ham Veri

Ölçümün ham yanıtları oturum scratchpad'inde tutuldu ve **repo'ya commit edilmedi** (üçüncü şahıs Steam
hesaplarının envanter içeriği — kişisel veri; §1–§6'daki tüm bulgular anonimleştirilmiş özet olarak
buradadır). Tekrar üretmek için:

```bash
# Tek bir public envanterin ham yanıtı
curl -s --compressed "https://steamcommunity.com/inventory/<STEAMID64>/730/2?l=english&count=200"

# Statü semantiğini ayırmak için profil ucu
curl -s --compressed "https://steamcommunity.com/profiles/<STEAMID64>?xml=1"
```

> **Tekrar ederken:** 4 istek/dk'yı aşma ve burst yapma (§3). Steam'in limiti dakikadan uzun pencereli;
> art arda birkaç dakikalık tarama IP'yi 429'a sokar.
