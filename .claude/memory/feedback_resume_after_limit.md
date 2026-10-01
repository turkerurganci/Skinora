---
name: feedback_resume_after_limit
description: "Uzun işlerde kullanım limiti dolarsa, limit açılınca işe kendiliğinden devam et (oturum içi cron ile); sahip onayı gereken adımları atlama"
type: feedback
---

Çalışma sürerken kullanım limiti dolarsa, limit açılınca işe kaldığı yerden kendiliğinden devam et. Kullanıcının "devam" demesini bekleme.

**Why:** Türker, 2026-10-01, #327 doğrulaması sırasında (arka planda uzun workflow'lar koşarken): "çalışma devam ederken limit dolarsa, limit açılınca tekrar devam et".

**How to apply:**
- Uzun, çok adımlı ya da arka plan workflow'lu bir işte oturum içi tekrarlayan bir `CronCreate` işi kur. Aralık ~30 dk olsun, :00 ve :30'dan kaçın (örn. `17,47 * * * *`).
- İş sürüyorsa cron tek satırla çıksın.
- İş limit yüzünden kesildiyse kaldığı adımdan devam etsin. Workflow kesildiyse `resumeFromRunId` ile devam ettirilir; önce journal okunur.
- İş bitince cron'u sil.
- Cron işleri yalnız oturumla yaşar (VS Code/Claude kapanınca gider) ve 7 günde kendiliğinden düşer. Kurarken kullanıcıya bunu söyle.
- Cron sahip onayı gereken adımları (merge, push, mail gönderimi) atlatmaz: onay yoksa bekler. Mail kuralı için bkz. kullanıcı seviyesi CLAUDE.md.

İlgili: [[feedback_claude_watches_ci_always]], [[feedback_no_edit_permission_asks]]
