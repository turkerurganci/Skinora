---
name: project_f8_devir_notu
description: "F8 BİTTİ (2026-10-21, tag phase/F8-pass, main 5ad1e6b7) — F9 açılışında ilk işler T160a–c, sahibe sorulacak düzen sorusu ve devir dosyalarının yeri"
metadata:
  node_type: memory
  type: project
  originSessionId: df47804d-abb7-454d-abcb-9a5cf4b3fdef
  modified: 2026-10-21T04:40:46.155Z
---

F8 (Çok Ağlı Ödeme, T141–T160) 2026-10-21'de kapandı: T160 Gate Check ✓ PASS (PR #361, main `5ad1e6b7`, tag `phase/F8-pass`). Sahip ⚠ listesini (PARA 23, GÜVENLİK 7) modalda olduğu gibi onayladı.

**F9'a devreden zorunlu işler (sahip kararı 2026-10-21, `Docs/plan/11_F9.md`):**
- T160a — F9'un İLK işi: `next@16.2.3` critical RCE danışmaları → yükseltme + CI frontend audit (backlog `FrontendNextAdvisories` 🔴).
- T160b — canlı öncesi ⚠ PARA: iade gas tahmini tek deneme → yeniden deneme (backlog `RefundGasEstimateSingleAttempt`).
- T160c — canlı öncesi ⚠ PARA: reddedilen ödeme iadesinde kesilen gas kalıntısı depozitte kalıyor → sweep ile topla (backlog `RefundGasDeductionDustStaysOnDeposit`).

**Why:** F9 açılırken "nerede kaldık" sorusunun cevabı ve sahibin verdiği öncelik sırası burada; status dosyası tek doğru kaynak olarak kalır.

**How to apply:** Sahip 2026-10-21: "F8 gibi çalışacağız" — F9 boyunca da yönetici düzeni (birleştirme yetkisi yöneticide, doğrulamayı yönetici başlatır, opus+high; [[project_f8_yonetici_duzeni]]). F9 yeni session'da sonuna kadar yürütülür. T160 merge teyidi (main 5ad1e6b7): CI 39545306202 ✓, Docker Publish 39545306158 ✓ — F9'un ilk dalında STATUS_CHANGELOG'a girer. Ayrıntılı ilerleme, süre ve maliyet günlüğü: `C:\Users\teknodeveloper\.claude\projects\c--projects-Escrow\f8-handoff\` (PROGRESS.md, COST_LOG.md — ölçülen ~13,4 M token: Opus ~11,1 M, Sonnet ~2,3 M, T149 sonrası). Ders: PC gece düzenli yeniden başlayınca arka plan ajanı yarıda kaldı — uzun doğrulamalarda ajana raporu ara ara dosyaya yazdır.
