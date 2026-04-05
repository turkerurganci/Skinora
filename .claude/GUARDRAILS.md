# Skinora — AI Sınırları ve Yasakları

**Son güncelleme:** 2026-03-11

---

## 1. Teknik Detay Kuralı

- Bulunduğun aşama teknik değilse, teknik detaya girme. "Bu teknik bir karar, şimdi ürün kararını alalım" de.
- Teknik mimari, teknoloji seçimi, API, veritabanı gibi konular sadece ilgili aşamada konuşulur.
- Hangi aşamada hangi konuların konuşulacağını `Docs/00_PROJECT_METHODOLOGY.md` dosyasından öğren.

---

## 2. Değiştirilemez Dosyalar

- Aşağıdaki dosyalar proje sahibinden açık onay almadan değiştirilemez:
  - `Docs/00_PROJECT_METHODOLOGY.md` — metodolojinin yapısı
  - `CLAUDE.md` — AI giriş noktası

---

## 3. Onay Gerektiren Aksiyonlar

- Yeni doküman oluşturma → proje sahibi onayı gerekli
- Mevcut dokümanda yapısal değişiklik (bölüm ekleme/silme) → proje sahibi onayı gerekli
- Kapsam değişikliği (MVP'ye özellik ekleme/çıkarma) → proje sahibi onayı gerekli
- Karar değiştirme (daha önce alınmış bir kararı revize etme) → proje sahibi onayı gerekli

---

## 4. Yıkıcı Önerilerde Kendini Sorgula

- Dosya silme, birleştirme, kaldırma veya büyük yapısal değişiklik önermeden önce "bu gerçekten gerekli mi?" sorusunu sor.
- Bir şeyin "gereksiz kaldığını" düşünmek yeterli değil — o şeyin hala taşıdığı veya gelecekte taşıyacağı değeri de değerlendir.
- Varsayılan tavır korumak olmalı, silmek değil. Silme ancak açık bir gerekçe varsa önerilir.

---

## 5. Doküman Kuralları

- "Muhtemelen", "belki", "olabilir" gibi belirsiz ifadeler doküman içinde kullanılamaz.
- Her doküman diğer dokümanlarla tutarlı olmalı. Tutarsızlık fark edilirse düzeltme önerilmeli.
- Doküman versiyonları korunmalı, sessizce üzerine yazılmamalı.

---

## 6. Karar Kuralları

- AI kendi başına karar almaz, her zaman proje sahibinden onay ister.
- Birden fazla seçenek sunduğunda öneri belirtir ama "ben bunu uyguluyorum" demez.
- Proje sahibine soru sorduğunda cevabını almadan başka konuya geçmez.

---

## 7. Yerleşim Kontrolü

- Proje sahibi "bunu X dosyasına ekle" dediğinde veya bir içeriğin nereye yazılacağını belirttiğinde, körü körüne uyma — o içeriğin gerçekten oraya ait olup olmadığını değerlendir.
- Belirtilen yer yanlışsa veya daha uygun bir yer varsa, doğru yeri gerekçesiyle öner.
- Bu öneri mevcut bir dosyada farklı bir bölüm olabileceği gibi, yeni bir dosya oluşturulması da olabilir.
