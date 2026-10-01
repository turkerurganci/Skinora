---
name: feedback_added_latency_check_caller_timeout
description: "Senkron bir çağrıya bekleme/gecikme eklemeden önce ÇAĞIRANIN zaman aşımını, yeniden deneme davranışını ve eşzamanlılık kilidini oku; \"düzeltme\" yeni bir çift-yürütme açabilir"
type: feedback
---

Bir düzeltme senkron bir çağrının süresini uzatıyorsa (blok bekleme, polling, retry), kodu yazmadan önce **çağıranı** oku: HTTP zaman aşımı kaç sn, zaman aşımında ne yapıyor (yeniden deneme mi, terminal mi), aynı işi iki kez başlatabilecek eşzamanlı bir tetik var mı.

**Why:** 2026-09-17, PR #323 düzeltmesi. B2 "her adımı bloğa girene kadar bekle" diyordu — doğru istek. Ama backend sidecar'ın transfer çağrısını `TimeoutSeconds × 3 = 30 sn`'de kesiyor ve 1 dk sonra **yeniden deniyordu**; blok beklemeleri çağrıyı ~10–25 sn'ye (en kötü dakikalara) çıkarıyordu. Kesilen çağrının sidecar'da yayınladığı transfer kaydedilmez, deneme boşalmış depozitte koşar (15 TRX yedeği + zincirde revert). Ayrıca dakikalık gönderim işinde `[DisableConcurrentExecution]` yoktu — uzun tur bir sonrakiyle çakışıp aynı depozit için akışı iki kez çalıştırırdı. İstenen düzeltme tek başına uygulansaydı yeni bir para-yolu kusuru açardı; proje sahibine modal ile soruldu (süre 300 sn + sidecar yayın penceresi 150 sn + iş kilidi).

**How to apply:** Gecikme ekleyen her değişiklikte sor: *"bu çağrıyı kim, hangi süreyle bekliyor ve süre dolunca ne yapıyor?"* — ve çağıran vazgeçtikten sonra çağrılanın **geri döndürülemez adımı** (yayın, ödeme) atmamasını sağla (bizde: süre penceresi geçtiyse yayınlama). İlgili: [[feedback_think_through_fully]], [[feedback_measure_effective_on_mainnet]].
