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
