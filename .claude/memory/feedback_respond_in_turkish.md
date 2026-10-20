---
name: feedback_respond_in_turkish
description: "Bu kullanıcıyla sohbet/açıklama/rapor iletişimi Türkçe yapılır; kod ve kod yorumları İngilizce kalır (durum özetleri/ilerleme dahil, TÜM yanıtlar)"
metadata:
  type: feedback
  node_type: memory
  originSessionId: be82ed34-ec00-4cc4-a2dd-9d6ffb4a11c5
---

Bu kullanıcıyla tüm sohbet iletişimi (açıklamalar, özetler, sorular, durum raporları) **Türkçe** yapılır. Soru sorarken (AskUserQuestion dahil) ve cevap verirken Türkçe yaz; İngilizce'ye kayma.

**Why:** Kullanıcı 2026-06-14'te T103b-2 düzeltme chat'inde tekrar tekrar "türkçe yaz", "türkçe sor", "türkçe yazılacak" dedi. Proje zaten "Türkçe dokümanlar, İngilizce kod" prensibini izliyor ([[user_profile]]); kullanıcının doğal dili Türkçe.

**How to apply:**
- Sohbet yanıtları, özetler, açıklamalar, AskUserQuestion soru/seçenek metinleri → **Türkçe**.
- Görev raporları (`Docs/TASK_REPORTS/`), repo memory, status notları → **Türkçe** (zaten konvansiyon).
- **İngilizce kalanlar (kod katmanı):** kod, tanımlayıcı/isimler, kod yorumları (`//`, XML doc), test isimleri, log mesajları — proje "İngilizce kod" kuralı. Commit `Co-Authored-By` trailer'ı İngilizce (CLAUDE.md sabiti).
- Commit mesajları Türkçe yazılır (mevcut repo geçmişiyle tutarlı).
