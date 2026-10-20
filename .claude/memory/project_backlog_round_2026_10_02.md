---
name: project_backlog_round_2026_10_02
description: "2026-10-02 backlog turu — dört 🟡 satır tek dalda (feat/backlog-round-2026-10-02); sahip kararları, ölçümler ve iş planı"
metadata:
  type: project
  node_type: memory
  originSessionId: 386887c5-3a14-4f16-b960-a30b3c5abb7f
  modified: 2026-10-02T20:37:31.601Z
---

Kullanıcı 2026-10-02'de dört 🟡 satırı "hepsini beraber yap" dedi; tek dal `feat/backlog-round-2026-10-02` (origin/main `6525b4a`'dan), tek PR, doğrulama ayrı chat'te. #330 merge teyidi (main CI `36924045682` ✓ + Docker Publish `36924045588` ✓) bu dalda kayda girer.

**Sahip kararları (modal, 2026-10-02):**
1. `RefundActivationCostNotCharged` → **alıcı öder.** Depozit hesap değilse iade gas tahminine açılış eklenir: `getCreateNewAccountFeeInSystemContract` (1 TRX) + sıcak cüzdanın KİLİTLİ bandı yetmezse `getCreateAccountFee` (0,1 TRX; ücretsiz bant hesap açmaya kullanılamaz). Zincirden okunur, sabit yazılmaz.
2. `P2P-DeliveryPollingJob` → **kaydet + alıcıya bildir.** Kapı (`delivery.inventory_evidence_auto_release_enabled`) kapalıyken kanıt kaydı + alıcıya yeni bildirim ("item gelmiş görünüyor, kontrol edip onayla", 4 dil); kapı açıkken ITEM_DELIVERED. Bütçe: dakikada ≤1 işlem, işlem başına ≤10 dk'da bir; satıcı item'ı tutuyorsa tek okuma; yalnız aynı turda yeterlilik çıkarsa bayrak yazılır.
3. `T139-ActiveMonitorQuotaAlarm` → **ödeme sonrası izleme 15 dk + alarm.** 3 sn yalnız SELLER_CONFIRMED'da; PAYMENT_RECEIVED/ITEM_DELIVERED 15 dk. Ölçülen TronGrid istek hızı × 24 sa > günlük bütçenin %80'i → alarm (bütçe env, varsayılan 100.000).
4. `P2P-InventoryUnauthorizedMapping` → **yalnız mesaj.** 401 = "CS2 envanteri yok" mesajı; ek Steam çağrısı yok; teslimat kanıtı mantığı değişmez.

**Ölçümler (2026-10-02):** 401 = o uygulamanın envanteri hiç yok (gizlilikten önce gelir — gizli hesap 440/2'de 403, 570/2'de 401); 403 = var ama gizli; boş ama var olan envanter 200 + `total_inventory_count:0`. Profili kurulmamış hesaplar 730/2 401 + 753/6 403 → satırdaki "ikisi de 401 → gizlilik" kuralı yanlıştı. Community ucu 8 sn arayla 15 istekten sonra 429. TronGrid: 3 sn × 2 sorgu = adres başına günde 57.600 istek; ücretsiz plan ~100K/gün (Chainstack 2026; resmî sayfa sayı vermiyor, "aynı hesabın anahtarları kotayı paylaşabilir").

**Durum (2026-10-02 gece):** PR [#331](https://github.com/turkerurganci/Skinora/pull/331) açıldı, HEAD `3d07d20`, bağımsız doğrulama bekliyor (ayrı chat). Commit'ler: `97105aa` iade açılışı · `5d2848b` 401 · `a6eb29a`+`3d3f637`+`f1591d3` sıklık+alarm · `8c88f66`+`269f9f3` tarama · `3d07d20` kayıt. Backlog 17/142. Yerel ölçüm: backend 3209/3209 (sıralı koşu şart — paralel koşu Testcontainers çekişmesiyle sahte kırmızı veriyor), bozma 46/46. Canlı: yerel yığının blockchain sidecar'ı bu daldan derlendi (Grafana kuralı `health: ok`, bütçe 1.000'de pending → 100.000'e geri alındı). Docker test için açıldı; iş bitince `docker desktop stop`.

**Doğrulama (2026-10-02, ayrı chat): ✓ PASS, squash merge `ce18adc`.** Bağımsız ölçüm: birim 1685/1685, entegrasyon+sözleşme Transactions 657 / API 510 / Notifications 60, sidecar 412 + 138, frontend 266, EF "No changes"; canlı Steam 401 (7 istek), Nile açılışları 1.100.000 SUN, Grafana kuralı `health: ok`. 75 bozma / 68 yakalandı; üç test boşluğu (taramada ortadaki sıralama anahtarı · kapı açıkken capture · transfer durum probunun sayılması) ve eski "3 sn" / B1 cümleleri sahip kararıyla doğrulamada kapatıldı (`fb3441d`), kayıt `1ff452f`, PR CI `37058719843` ✓. **Kapsam dışı yeni 🔴 `MonitorCursorPagesBackward`** (sahip kararı 🔴): izleyici TronGrid `fingerprint`'ini saklıyor ama imleç eskiye gidiyor; bir fazda 20+ kayıtta o tarama kör. Backlog 18/142, 🔴 1. **Merge teyidi:** main CI `37060252692` ✓ + Docker Publish `37060252723` ✓ — repo kaydına sonraki dalda girecek ([[feedback_merge_teyit_not_direct_pushable]]).

**Why:** uzun iş; oturum kesilirse ya da bağlam özetlenirse kararlar ve ölçümler kaybolmasın.
**How to apply:** dalı sürdürürken bu kararları uygula; PR açılınca repo `.claude/memory/MEMORY.md` + bu dosya güncellenir, merge sonrası [[project_phase_history]]'ye taşınır.
