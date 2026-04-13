---
name: feedback_propagate_effects
description: GPT cross-review sonrası diğer dokümanlarla uyumluluk kontrolü ve etki yansıtma yapılmalı
type: feedback
---

GPT cross-review tamamlandıktan sonra değişikliklerin diğer dokümanlarla uyumluluğunu kontrol et ve etkileri yansıt.

**Why:** Kullanıcı 03 review sonrası etki yansıtmayı hatırlattı — bu adım atlandığında dokümanlar arası tutarsızlık birikir. 02 sonrası 29 uyumsuzluk, 03 sonrası ~15, 04 sonrası ~9 güncelleme çıktı.

**How to apply:** Her GPT review TEMİZ döndüğünde, düzeltmeleri uygula → etki analizi yap (explore agent ile downstream/upstream kontrol) → uyumsuzlukları düzelt → versiyonları güncelle. Bu adım GPT review döngüsünün standart parçası.
