---
name: Sade ve anlaşılır yaz
description: Yanıtlar kısa cümlelerle, az terimle ve az biçimlendirmeyle yazılır; uzun teknik anlatım kullanıcıyı zorluyor
type: feedback
---

Bu kullanıcıya yazarken **sade dil** kullanılır. Uzun, yoğun teknik anlatım anlaşılmıyor.

**Why:** Kullanıcı 2026-08-09'da P2P pivot chat'inde "yazdıklarını anlamak güç oluyor, dilini daha anlaşılır hale getir, bu isteğimi not al her seferinde dikkate al" dedi. O sırada yanıtlar şöyleydi: her mesajda 2-3 tablo, cümle başına birden fazla İngilizce terim (custody, guard, invariant, baseline, delta, escrow), tek paragrafta birbirine bağlı üç ayrı konu. Kullanıcı .NET/C#/SQL Server deneyimli ([[user_profile]]) — sorun teknik seviye değil, **anlatım yoğunluğu**.

**How to apply:**

- **Kısa cümle.** Bir cümlede tek fikir. Yan cümle zinciri kurma.
- **Az tablo.** Tablo yalnız gerçekten karşılaştırma varsa (2+ satır, 2+ kolon anlamlı). Tek bir durumu anlatmak için tablo kurma; düz cümle yeter.
- **Terimi çevir.** İlk geçtiğinde Türkçesini yaz: "custody" → "item'ı platformun tutması", "guard" → "geçiş koşulu", "baseline" → "referans anlık görüntü", "delta" → "sayı farkı". Kod adları (`DeliveryEvidence`, `SELLER_CONFIRMED`) olduğu gibi kalır.
- **Tek konu.** Bir mesajda tek ana konu. Yan bulguları ayrı kısa başlık altına al ya da sonraki mesaja bırak.
- **Sonucu önce söyle.** "Şu oldu" → sonra gerekçe. Gerekçeyi baştan kurup sonuca varma.
- **Kısa tut.** Uzun mesaj yerine kısa mesaj + "detay ister misin?" tercih edilir.

**Tekrar — 2026-08-28.** Aynı geri bildirim yeniden geldi: *"daha anlaşılabilir ve çok kısa anlat"*. Tetikleyen yanıt, **"TronGrid API key ne işe yarıyor"** gibi tek cümlelik bir soruya beş paragraf + dört `dosya:satır` bağlantısı + kalın terimlerle cevap vermekti. Kod dayanağı doğruydu ama **soru onu istemiyordu**.

**Ek kural — soru tipine göre uzunluk.** *"X ne işe yarıyor / neden gerekli"* türü bir soru **2-3 kısa cümle** ile cevaplanır: dosya/satır bağlantısı yok, tablo yok, kod adı ancak zorunluysa. Kod dayanağı yalnız **istenirse** ya da bir iddia tartışmalıysa eklenir. Ölçüt basit: kullanıcı bir **kavramı** sorduysa kavramı anlat, **kanıtı** değil.

**ÜÇÜNCÜ TEKRAR — 2026-09-04. Kural artık "genelde uygula" değil, "İSTİSNASIZ uygula".** Gas fee turunda aynı geri bildirim üst üste geldi: *"ben anlamadım daha anlaşılır dilde anlat"* → *"artan 1 kuruşa neden 2 dolar masraf ödüyoruz"* → *"bundan sonra her şeyi daha anlaşılır anlat ve bu kuralı not al ve asla atlama"*. Üçüncü mesaj ayrıca **kuralın nereye yazıldığını** sorguladı: kural önce yalnız Claude'un özel auto-memory'sine yazılmıştı, repoya değil. **Kurallar repoda yaşar** (`.claude/memory/`), yoksa proje sahibi göremez ve başka bir oturum devralamaz.

**Tetikleyen desen — kanıt yığmak.** Yanıtlar teknik olarak doğruydu ama "GPT ne dedi" sorusuna kaynak adı, dosya yolu, İngilizce terim ve iç içe gerekçe ile cevap veriliyordu. Proje sahibi konuyu takip edemedi ve **iki kez daha sormak zorunda kaldı**. Sadeleştirmeyi ona hatırlatmak zorunda bırakmak, kuralın uygulanmadığının kanıtıdır.

**Bu turda eklenen iki somut kural:**

- **Rakamı hesabıyla göster.** "~2 dolar" yetmiyor; hangi çarpımdan çıktığı gösterilmeli. Proje sahibi tam olarak bunu sordu: *"artan 1 kuruşa neden 2 dolar masraf ödüyoruz"*. Bir sayı gerekçesiz verilirse bir sonraki soru o sayı olur.
- **Sonunda tek cümlelik soru.** Yanıt "ne yapmamı istersin?" ile kapanır. Uzun seçenek listesi değil, tek soru.

**Kontrol — göndermeden önce sor:** *"bu alanda çalışmayan biri okusa anlar mıydı?"* Anlamazsa yeniden yaz.

**Neyi bozmaz:** Doküman içeriği (`Docs/*.md`), task raporları ve commit mesajları teknik ve ayrıntılı kalmaya devam eder — bu kural **sohbet yanıtları** içindir. [[feedback_respond_in_turkish]] ile birlikte geçerlidir.
