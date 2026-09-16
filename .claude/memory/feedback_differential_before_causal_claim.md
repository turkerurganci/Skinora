---
name: feedback_differential_before_causal_claim
description: Belirtiyi bir nedene bağlamadan önce ayırt edici (diferansiyel) ölçüm yap; ilk uyan sinyalde durma
metadata:
  type: feedback
---

**Bir sinyal görmek onu açıklamak değildir.** Hikâyene uyan **ilk** ölçümde durma; alternatif sebepleri **eleyen** ikinci ölçümü yap. Aynı probu tekrar koşmak **tutarlılık** verir, **doğruluk** vermez.

İki alışkanlık:
1. **Dış bağımlılık arızasında önce bağımlılığa doğrudan sor** — bizim konfigümüzü/kodumuzu suçlamadan önce. Servise doğrudan bir istek, teşhisi çoğu zaman ilk dakikada bitirir.
2. **Kontrol ölç.** "X yüzünden Y" demek için X'i görmek yetmez; X-olmayan bir örnekte Y'nin **olmadığını** göstermek gerekir.

**Why:** 2026-08-23'te envanterin okunamamasını önce `STEAM_API_KEY`'e (devralınan yanlış bir kayıttan), sonra Steam rate-limit'ine bağladım; **ikisi de yanlıştı**. Doğru cevabı yalnızca kıyas verdi: kullanıcının envanteri `403` + gövde `null` (gizli profil), kontrol hesabı `429` (rate limit) — iki farklı kod, iki farklı sebep. Tek ölçüm bunları ayıramıyordu ve ben aynı probu iki kez koşup "tutarlı" diye güvenmiştim. Aynı tur içinde bu ders üç kez daha işe yaradı: dört admin rotasının "yetki hatası" sanılması (gerçek sebep 429), "Steam avatarları görünmüyor" (gerçek sebep ekran görüntüsü zamanlaması), envanter 429'unun UA/URL biçimi hipotezleri (ikisi de elendi, doğru cevap istemci yığınına daralttı).

**How to apply:** Bir nedensellik iddiası yazmadan önce sor — *"bu ölçüm hangi alternatifi eledi?"* Cevap yoksa iddia değil **gözlem** yaz. Rapora yalnız bulguyu değil, **hangi ölçümün neyi çürüttüğünü** de koy; çürütülen tezlerin listesi raporun kalitesinin ölçüsüdür.

**Kardeş ders:** [[feedback_verify_metric_definition]] aynı şeyi **sayılar** için söylüyor ("devraldığın sayıyı üreten komutu oku"); bu satır **nedensellik iddiaları** için. Ayrıca ölçüm aracının kendisi de ölçülmeli — aynı turda aracın dört kusuru bulundu (dinleyici sızıntısı, erken ekran görüntüsü, tek sekmede token arama, turun kendi rate-limit kovasını tüketmesi) ve düzeltmeden önceki sayılar kullanılmadı.

**Testte aynısı (2026-09-16, PR #321 doğrulaması):** bir test *"değer X kaynağından gelir"* iddiasını taşıyorsa, düzenekte X ile **en basit alternatif sabit** farklı değer almalı — yoksa test kaynağı değil sabiti ölçer. *"Çözülemeyen kontratta tahmin beklenen token'a düşer, testle pinli"* testi her zaman USDT bekleyen bir düzenekle koşuyordu; geri düşüşü sabit `StablecoinType.USDT` yapan mutasyon süiti yeşil bıraktı. Tek başına USDC'ye çevirmek de yetmezdi (bu kez sabit USDC geçerdi); test iki beklentiyle (USDT + USDC) koşulunca iki sabit de birer koşuyu kırdı. Sor: *"bu iddiayı bozan en basit sabit hangisi, düzenek onu yakalar mı?"*

**"En basit bozan değişiklik" yalnız sabit değer değildir (2026-09-16, PR #322 doğrulaması).** Yapım turu #321 dersini değerler için uygulamıştı (varsayılan dışı eşik testleri) ve yine de beş mutasyon yeşil kaldı, çünkü bozan değişikliğin dört başka biçimi vardı: **(1) karşılaştırma operatörü** — bütün askı testleri sayıyı tam eşiğe koyuyordu, orada `>=` ile `==` ayrışmaz; "admin kaldırdıktan sonra yeni olay askıyı geri getirir" kuralı eşiğin **bir üstünde** yaşıyordu. **(2) koleksiyonun bir kısmı** — "iki işlemi de adlandırmalı" diyen test yalnız sonuncuyu arıyordu. **(3) filtrenin eleyeceği değer hiç kurulmamış** — hesap düzeyi filtresini sınayan tek test yalnız hesap düzeyi flag kuruyordu. **(4) paylaşılan kodun yalnız bir çağıranı** — ortak yazıcı aktörü parametreden alıyordu ama yalnız SYSTEM çağıranı test ediliyordu; sabit SYSTEM yazan mutasyon admin testlerini de geçti. Sor: *"bu kuralı bozan en basit değişiklik hangisi — `==`, sabit, yalnız son eleman, filtresiz sorgu, tek çağıran — ve düzeneğim onun üreteceği sonuçtan farklı bir şey bekliyor mu?"* Eşik için eşiğin üstünü, filtre için elenmesi gereken değeri, paylaşılan davranış için her çağıranı kendi ayırt edici değeriyle koş.
