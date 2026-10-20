---
name: Doğrulama her zaman ayrı chat'te yapılır
description: "Doğrulama yapım chat'inde yapılmaz; güncel akış genel SubAgentIndependentValidation skill'i (izole alt ajan, opus+high), F8 boyunca eski yönetici düzeni istisnası — eski içerik tarihçede"
metadata:
  type: feedback
---
**Güncel akış (2026-10-20):** Doğrulama yine yapan ≠ denetleyen ilkesine dayanır, ama artık ayrı chat yerine izole alt ajanla yapılır. Her doğrulama işinde ÖNCE `SubAgentIndependentValidation` skill'i yüklenir (`C:\Users\teknodeveloper\.claude\skills\SubAgentIndependentValidation\SKILL.md`; genel `~/.claude/CLAUDE.md` > "Doğrulama (validasyon) akışı", proje bağımsız): talimatı ben yazarım ve Türker'e gösteririm; "başlat" demeden başlatmam; alt ajan `general-purpose` (fork DEĞİL), model `opus` + effort `high`, görev metni yalnız talimat yolunu içeren tek satır; sonuç ajanın dönüş metninden değil talimattaki rapor dosyasından (yol, zaman, başlık kontrol edilerek) okunur; üst üste iki tur yalnız Düşük → temiz kabul + Türker'e bildir.

**F8 istisnası:** F8 bitene kadar [[project_f8_yonetici_duzeni]] sürer — doğrulama ajanını yönetici başlatır ve birleştirir (talimat gösterme / "başlat" onayı yok), ama doğrulama ajanı `opus` + `high`. Genel akış F9'dan itibaren. Model/effort tablosu: [[feedback_model_by_task_weight]].

**Why:** Türker 2026-10-20: önce ayrı session'da yapılıyordu; alt ajanın sohbeti görmediği denendi ve görüldü (yeni session ile aynı bağlam); "izole olmasına ve verdiğin metin ile çalıştığından emin ol, validasyon sonucunu doğru yerden okuduğuna emin ol".

## Tarihçe — ayrı chat kuralı (artık geçerli olan akış yukarıdadır)

Doğrulama (validate) asla yapım chat'inde yapılmaz — her zaman ayrı bir chat'te açılır.

**Why:** Yapan ≠ denetleyen prensibi. Aynı chat'te doğrulama yapılırsa anchoring riski oluşur, bağımsızlık bozulur.

**How to apply:** Task yapım chat'i bittiğinde sadece "doğrulama için yeni chat aç" de. Validate skill'ini çalıştırmaya kalkışma.
