---
name: feedback_scripted_doc_edits_verify_against_head
description: "Doküman satırlarını betikle değiştirdikten sonra HEAD'e karşı satır sonu normalize edilmiş diff al; perl'e çok satırlı metni ortam değişkeniyle değil dosyadan byte modunda ver"
type: feedback
---

Bir doküman dosyasında satırları perl/sed ile değiştirdiğinde, commit'ten önce dosyayı **HEAD'e karşı** satır sonu normalize ederek karşılaştır (`git show HEAD:f | tr -d '\r'` ile `tr -d '\r' < f`) ve yalnız hedeflenen satırların değiştiğini gör.

**Why:** 2026-09-16, aynı oturumda iki kez sessiz bozulma. (1) `perl -CSD` ile ortam değişkeninden gelen Türkçe metin çift kodlandı ("§" → "Â§") — perl `%ENV`'i çözülmemiş bayt olarak alır, `-CSD` yalnız dosya/akışları çözer. (2) `R="$(cat satir.txt)"` ile bir backlog satırını iki satırla değiştirdim; `$(cat)` sondaki satır sonunu sildiği için **sonraki satır yeni satırın sonuna yapıştı** ve bir kayıt kaybolmuş göründü. İkisini de gözle kontrol yakalamadı (`cut -c1-80` satırın sonunu göstermiyordu); ikincisini dosyanın kendi `awk` sayacının beklenen +1'i vermemesi, ardından HEAD diff'i yakaladı. İlk "düzeltmem" (satırı HEAD'den geri koymak) teşhis yanlış olduğu için çift kayıt üretti — önce yapışmayı ölçmek gerekirdi.

**How to apply:** Çok satırlı içeriği perl'e `$ENV` ile verme; küçük bir `.pl` betiği yaz, içeriği betik içinde `open(..., '<:raw', dosya)` ile oku, dosyayı byte modunda yaz. `$(cat)` kullanırsan sona satır sonunu kendin ekle. Değişiklikten sonra: (a) HEAD'e karşı normalize diff yalnız hedef satırları göstermeli, (b) dosyada bir sayaç varsa (backlog `awk`'i gibi) beklenen farkı vermeli, (c) değiştirilen satırın **sonunu** da oku (`tail -c`). Bir kayıt kaybolmuş görünüyorsa önce başka bir satıra yapışıp yapışmadığını ölç, sonra geri koy. İlgili: [[feedback_differential_before_causal_claim]], [[feedback_verify_metric_definition]].
