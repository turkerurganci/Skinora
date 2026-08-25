---
name: feedback_verify_metric_definition
description: Devralınan bir sayıyı aktarmadan önce onu üreten komutun ne saydığını oku — tutarlılık doğruluk değildir
type: feedback
---

Bir dokümandan/rapordan **devralınan sayıyı** kendi çıktına yazmadan önce, o sayıyı üreten komutu bul ve **ne saydığını** ölç. Sayının uzun süre tutarlı raporlanmış olması onu doğru yapmaz — yalnız *karşılaştırılabilir* yapar.

**Why:** 2026-08-22 F7 Gate Check'inde tam olarak bu oldu. Gate, testleri titizlikle ölçtü (yerel sayıları CI job loglarıyla **assembly bazında** karşılaştırdı, aggregate'e güvenmedi) ama `DEFERRED_BACKLOG.md`'nin "67 aktif / 65 çözülmüş" rakamını **dosyanın kendi komutundan devraldı ve komutu okumadı**. Komut (`grep -cE "^\| (🔴|🟡|⚪) "`) `🔝 Öne Çıkanlar` bölümündeki **kopya satırları** da sayıyordu; gerçek benzersiz öğe sayısı **61 aktif / 50 çözülmüş**'tü. Rakam etiketiyle ("aktif satır") teknik olarak tutarlıydı ama yirmi tur boyunca herkes onu öğe sayısı olarak okudu — gate raporu ve iki memory dosyası dahil. Hata sınıfı zincirin devamı: `T138-B1` (kanıt komutunun kapsamı) → `T140` (talimatın güncelliği) → T140 doğrulaması (tablonun kapsamı) → **bu tur (metriğin tanımı)**.

**How to apply:** Bir sayıyı rapora/statüye/memory'ye yazarken üç soruyu sor: (1) bu sayıyı hangi komut üretiyor? (2) komut **satır** mı **öğe** mi sayıyor — kopya, başlık, özet bölümü sayıma giriyor mu? (3) etiketi ("aktif satır" vs "aktif kalem") gerçekte ölçtüğü şeyi mi söylüyor? Ölçüm komutu dokümanda yazılıysa **çalıştır ve çıktısını kendi bağımsız sayımınla karşılaştır**; ikisi ayrışıyorsa aktarma, önce tanımı düzelt. Trend karşılaştırmaları eski metrikle geçerli kalır — düzeltirken bunu açıkça söyle ki geçmiş turlar geçersiz sanılmasın. İlgili: [[feedback_verify_status_before_quoting]], [[feedback_gpt_review_objectivity]].
