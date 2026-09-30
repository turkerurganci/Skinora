---
name: Status sorusu — kaynağı oku, kısa cevap ver
description: "Sırada ne var / hangi task / nerede kaldık" sorularında MEMORY snapshot'a güvenme, Docs/IMPLEMENTATION_STATUS.md'den oku; cevap en fazla 3 kısa satır (neredeyiz + sıradaki adım + tek soru)
type: feedback
---

"Sırada ne var?", "hangi task?", "nerede kaldık?", "F1'de kalan ne?" gibi durum sorularına **MEMORY.md "Current Status" / "Next" alanlarına bakarak** cevap verme. Önce [`Docs/IMPLEMENTATION_STATUS.md`](../../Docs/IMPLEMENTATION_STATUS.md) dosyasını oku, oradaki tabloyu (✓/⏳/⬚ kolonları) kaynak kabul et.

**Why:** 2026-04-19'da kullanıcıya "sıradaki T26" dedim — gerçekte T26 ✓ PASS olmuştu (`c090b14` #30, validate `a1bf832`), sıradaki T27'ydi. Hatanın kaynağı: T26 validator chat'inde MEMORY.md "Next" satırı güncellenmemişti, IMPLEMENTATION_STATUS.md ise doğru duruyordu. Ben kontrol etmeden hafızadan konuştum, yanlış cevap verdim. Memory snapshot her zaman bir adım geride olabilir; tracker dosyası git'te tutulduğu için her commit'te güncellenir, tek doğru kaynak odur.

**How to apply:**
- Kullanıcı "sırada ne var / nerede kaldık / hangi task" diye sorduğu **her seferde**: (1) auto-memory `MEMORY.md` "Current Status" hızlı-cevabına bak (artık kapanış akışında güncel tutuluyor), (2) `IMPLEMENTATION_STATUS.md`'de ilgili **TXXX satırını `grep`'le** (örn. `grep -n "^| T102 " ...`), tüm dosyayı `Read` ile açmaya çalışma. İkisi çelişirse tracker satırını kaynak kabul et + auto-memory'i düzelt.
- MEMORY.md'deki "Next" / "Current Status" alanını bilgilendirici bir özet say, otoriter kaynak sayma. İki kaynak çelişirse tracker'ı kabul et ve memory'i güncelle.
- Validator/task kapanış akışında MEMORY.md "Current Status" + "Next" alanlarını (hem repo `.claude/memory/MEMORY.md` hem auto-memory `MEMORY.md`) güncellemeyi unutma — bu boşluk yine bu hatayı doğurur.
- Aynı kural diğer "snapshot" memory alanları için de geçerli: completed docs versiyonları, audit/GPT review listeleri, checkpoint sayısı vs. — kullanıcıya rakam/durum söylemeden önce kaynak dosyadan teyit et.

**Cevap biçimi — KISA (2026-09-30, kullanıcı: "bunu unutma").** Araştırma yukarıdaki gibi eksiksiz yapılır; kısalan yalnız cevaptır. "Nerede kaldık" cevabı **en fazla 3 kısa satır**:

1. **Neredeyiz** — bekleyen iş ve kimde (PR numarasıyla, tek cümle).
2. **Sıradaki tek adım.**
3. **Karar gerekiyorsa tek soru** — modal ile ([[feedback_ask_questions_via_modal]]).

Cevaba girmez, sorulursa verilir: sıranın gerekçesi, çakışma analizi, CI iş sayıları, backlog sayıları, adım adım plan, hatırlatma listesi. Bir hatırlatma yalnız **bugün** aksiyon gerektiriyorsa yarım satırla girer.

*Why:* 2026-09-30'da "nerede kaldık" sorusuna ~20 satırla cevap verdim: iki PR maddesi, sıranın neden önemli olduğu, iki PR'ın çakışmadığı, dört adımlı plan, backlog notu, bir hatırlatma ve soru. Kullanıcı: *"bundan sonra nerede kaldık diye sorunca daha kısa cevap ver, bunu unutma!"* Genel kısalık ([[feedback_be_concise]]) ve sade dil ([[feedback_plain_language]]) kuralları zaten vardı; durum cevabı yine uzadı, çünkü "bağlamı eksiksiz ver" refleksi bu soru tipinde kısalığı eziyor. O yüzden bu sorunun kendi kalıbı var.

*Örnek — o günün cevabının olması gereken hâli:*

> İki PR seni bekliyor: #326 (prova kaydı) birleştirilecek, #327 (gas tahmini düzeltmesi) doğrulanacak.
> #326 birleşince #327'ye kayıt commit'ini eklerim.
> *(modal: "#326'yı birleştireyim mi?")*

**Okunabilirlik (2026-06-06 düzeltmesi — "başka sessionda nerede kaldık bulamıyor" şikâyeti):** Kaynak dosyalar Read araç sınırlarını aşacak kadar şişmişti → "nerede kaldık" cevaplanamıyordu. (a) `IMPLEMENTATION_STATUS.md` satır-3 "Son güncelleme" başlığı 63.5K-char tek satırdı → Read truncate, grep "[Omitted long matching line]"; yalnız EN SON girdiye indirildi, tarihsel changelog [`Docs/STATUS_CHANGELOG.md`](../../Docs/STATUS_CHANGELOG.md)'ye taşındı. (b) Repo `.claude/memory/MEMORY.md` 326KB'ydi → Read'in 256KB sınırını tümden aşıyordu (hiç açılamıyordu); ayrıntılı T23–T97 changelog [`MEMORY_ARCHIVE.md`](MEMORY_ARCHIVE.md)'ye taşındı, MEMORY.md ~27KB'a indi. **Ders:** "Son güncelleme" başlığını ve repo MEMORY.md'yi prepend-only changelog'a çevirme — güncel snapshot kısa kalsın, tarihsel detay arşiv dosyalarında + `TASK_REPORTS/`'ta. Tek task detayı gerekince arşivi `grep`'le.
