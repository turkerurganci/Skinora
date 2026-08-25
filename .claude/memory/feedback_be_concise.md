---
name: feedback_be_concise
description: Yanitlari kisa tut; uzun ozet/tablo yerine az yazip soru sor
type: feedback
---

Kısa yaz. Uzun özetler, çok satırlı tablolar, madde madde gerekçe dökümleri isteme dışı. Sonucu birkaç cümlede söyle, karar gerekiyorsa kısa soru sor.

**Why:** 2026-08-22'de kullanıcı doğrudan söyledi: "çok detaylı yazıyorsun az yazarak sor". Gate check ve backlog turlarında her yanıt tablolarla + ölçüm dökümleriyle uzuyordu; detay zaten rapor dosyalarında ve PR açıklamalarında duruyor, sohbete tekrar kopyalanması gürültü.

**Soruyu dialog olarak sor.** Bu kural kendi dosyasında yaşıyor: [[feedback_ask_questions_via_modal]]. Burada yalnız bağlantısı var — kural iki yerde yarım yazılırsa biri kaçırılır.

**Soruları anlaşılır sor (2026-08-22, ayrı uyarı).** Seçenekleri kısaltayım derken `§5`, `⚪`, `T81`, `StubPayoutVerifier` gibi kodlarla soru sordum — kullanıcı "soruları anlaşılır sor" dedi. Soruda **ne yapılacağını günlük dille** yaz: bölüm numarası/kalem ID'si yerine işin kendisini anlat ("admin panelinde X yanlış görünüyor, düzelteyim mi"). ID'yi gerekiyorsa parantezde ver, başlık olarak değil. Kısalık, anlaşılırlığın yerine geçmez.

**How to apply:** Varsayılan yanıt birkaç cümle. Ölçüm/kanıt detayını rapor dosyasına, PR body'sine veya backlog satırına yaz — sohbete değil; sohbette yalnız sonucu ve linki ver. Tablo yalnız kullanıcı isterse. Karar noktasında uzun seçenek analizi yapma, tek cümlelik soru sor. Bu kısalık ölçüm disiplinini gevşetmez — [[feedback_verify_metric_definition]] ve doğrulama kuralları aynen geçerli, yalnız *anlatımı* kısalır.

**Kardeş kayıtlar:** [[feedback_plain_language]] *nasıl* yazılacağını söylüyor (kısa cümle, terimi çevir, tek konu); bu satır *ne kadar* yazılacağını. [[feedback_respond_in_turkish]] dili sabitliyor.
