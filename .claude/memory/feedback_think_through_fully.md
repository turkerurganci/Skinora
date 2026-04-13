---
name: Çözümü sonuna kadar düşün
description: Önerilen bir değişikliğin tüm sonuçlarını (bağımlı dosyalar, gereksiz kalacak yapılar, mimari tutarsızlıklar) ilk seferde düşün — kullanıcının ikinci kez sormasını bekleme
type: feedback
---

Bir yapısal değişiklik önerirken, sadece "ne yapılacak"ı değil "bunun sonucunda başka ne değişmeli" sorusunu da ilk seferde yanıtla.

**Why:** Kullanıcı "PROMPTS.md index olarak kalır" önerisini kabul etmek zorunda kaldı ve ardından kendisi fark edip düzeltti. AI'ın bu tutarsızlığı ilk seferde yakalaması gerekiyordu.

**How to apply:** Herhangi bir taşıma, refactor veya yapısal değişiklik önerirken şu kontrol listesini uygula:
1. Bu değişiklik sonucunda gereksiz kalacak dosya/bölüm var mı?
2. Bu değişiklikten etkilenen referanslar (CLAUDE.md, CONTEXT.md vb.) var mı?
3. Önerdiğim ara çözüm (index dosyası, placeholder) gerçekten gerekli mi, yoksa temiz çözüm daha mı basit?
4. Yeni oluşturulan şeyin keşfedilebilirlik ve kullanım yolu tanımlı mı? (Örn: skill oluşturuluyorsa tetikleyicisi nerede belirtilecek?)
Kullanıcıyı yarım çözüme yönlendirme — tam çözümü ilk seferde sun.
