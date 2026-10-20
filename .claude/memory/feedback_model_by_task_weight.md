---
name: feedback_model_by_task_weight
description: "Alt ajan açarken model + effort BİRLİKTE verilir: Explore haiku/low, özet-derleme sonnet/medium, inceleme-plan-zor hata opus/high, bağımsız doğrulama opus/high (Türker 2026-10-20; 2026-10-18 'para dışı doğrulama Sonnet' maddesi değişti)"
metadata:
  type: feedback
  node_type: memory
  originSessionId: 05a18c17-d163-437f-bb77-9c997ee193cc
  modified: 2026-10-17T22:05:14.888Z
---
Türker 2026-10-20'de genel kuralı koydu (`C:\Users\teknodeveloper\.claude\CLAUDE.md` > "Alt ajan modeli ve effort", proje bağımsız): açtığım HER alt ajanda (Agent aracı) `model` ve `effort` BİRLİKTE verilir, işe göre seçilir. Türker başka model/effort derse o geçer.

**Güncel kural (2026-10-20):**
- Arama, yer bulma, liste çıkarma (Explore) → `haiku` / `low`
- Özet, derleme, mekanik düzenleme → `sonnet` / `medium`
- İnceleme, plan, zor hata teşhisi → `opus` / `high`
- Bağımsız doğrulama → `opus` / `high` (para/güvenlik olsun olmasın; önce medium'du, 2026-10-20'de high'a çıktı)
- `fork` türünde model/effort verilemez (ana modeli miras alır); fork yalnız bağlam gerektiren işler için, doğrulama için ASLA.
- Garanti: `~/.claude/settings.json` PreToolUse (Agent) hook'u `~/.claude/hooks/subagent-model-guard.js` model/effort eksikse, Explore haiku değilse, görev metninde DOĞRULAMA geçen ajan opus değilse ya da fork ise çağrıyı engeller; her çağrı `~/.claude/logs/subagent-calls.log`'a yazılır. Engellerse kurala uygun model+effort ile yeniden çağır, hook'u atlatmaya çalışma.

**Why:** Maliyet iyileştirmesi; Türker 2026-10-20: "model ve effort bilgisi beraber olacak unutma", uygulanacağının garantisi istendi.

**Ölçülmüş ders (2026-10-20):**
- Büyük yapım işinde Sonnet ucuz çıkmadı: T157 yapımı 705 bin token ve 3,4 saat sürdü. Ucuz model yalnız küçük, sınırı belli işte ucuzdur; büyük yapımda daha çok tur ve token harcayıp toplamı pahalılaştırabilir.
- Geçmişi büyük (yüz binlerce token) ajana devam mesajı (SendMessage) pahalı: her adım tüm geçmişi yeniden okur. Kısa, hedefli istemle yeni ajan açmak daha ucuz (2026-10-18'deki itiraz bununla ölçüyle doğrulandı).

**Hâlâ geçerli kalanlar (2026-10-18'den):** tek satırlık iş için ajan açma (doğrudan komut, ajan proje kurallarını yükleyince daha pahalı); CI izleme, merge, PR yorumu gibi işler ajan açmadan doğrudan Bash ile; ucuz modelin çıktısını yönetici gözden geçirir; ajan istemleri kısa tutulur.

İlgili: [[project_f8_yonetici_duzeni]] · [[feedback_validation_separate_chat]].

## Tarihçe — 2026-10-18 kuralı (DEĞİŞTİ: yukarıdaki 2026-10-20 kuralı geçerli; özellikle "para dışı görevlerin doğrulaması Sonnet" ve "efor varsayılan medium" maddeleri artık geçerli değil)

Türker 2026-10-18'de, F8 yönetici düzeninde her alt ajanın Opus'la koştuğunu görünce: *"her işi opusla yapma, basit işleri daha ucuz modellere yaptır!"*

**Why:** Maliyet. Shopfolio'nun otonom koşu düzeninde de aynı ayrım var (`scripts/autopilot/README.md` "Modeller ve ayarlar"): bulgu bulan/doğrulayan/karar veren iş Opus, mekanik iş Haiku, metin taslağı Sonnet.

**How to apply (Agent tool `model` parametresi):**
- **Opus (varsayılan, model verme):** kod yazan yapım ajanları, bağımsız doğrulama ajanları, karar analizi, ölçüm görevleri (T142 gibi), mimari/akış tasarımı.
- **Sonnet:** küçük doküman düzeltme turları (bulgu listesi verilmiş, yalnız metin değişikliği), PR/rapor/hafıza metni taslağı, kayıt (status/rapor/hafıza) güncellemeleri, verilen formülle tutarlılık düzeltmeleri.
- **Haiku:** saymak, listelemek, grep ile var/yok kontrolü, worktree/dal temizliği, run ID toplama gibi mekanik işler.
- Emin değilsen: işin içinde "bulgu bulmak, doğrulamak ya da karar vermek" varsa Opus; yoksa Sonnet/Haiku. Ucuz modele verilen işin çıktısını yönetici olarak gözden geçir.
- CI izleme, merge, PR yorumu gibi işleri ajan açmadan doğrudan Bash ile yap.

**Ek (Türker, 2026-10-18): "daha pahalı olmasın! maliyeti düşürmeye çalışıyorum".** Model seçimi işin RİSKİNE göre, yalnız türüne göre değil:
- **Opus yalnız para/güvenlik görevlerinde:** para gönderen, imzalayan, tutar hesaplayan, ödeme izleyen görevlerin yapımı ve doğrulaması (F8'de T152, T153 ve benzeri).
- **Sonnet:** para/güvenlik dışı görevlerin yapımı ve doğrulaması (arayüz, admin ekranı, sahte servis/E2E, ayar/runbook), belge çakışmalı birleştirme ve kapanışlar, bulgusu verilmiş düzeltme turları.
- **Haiku ya da doğrudan komut:** tek satırla bitecek iş için ajan açma (ajan açmak proje kurallarını yükleyip komutu çalıştırmaktan pahalı); çok adımlı ama karar gerektirmeyen işler Haiku.
- Ucuz modelin çıktısını yönetici gözden geçirir; şüphe varsa yalnız o noktaya hedefli Opus kontrolü (tam doğrulama değil).
- Ajan istemlerini kısa tut. Devam mesajı (SendMessage) YALNIZ ajanın geçmişi küçükse ve düzeltme birkaç satırsa ucuz; geçmişi büyük (yüz binlerce token) ajanda her adım tüm geçmişi yeniden okur (önbellek süresi dolduysa tam fiyat) — o durumda kısa, hedefli istemle yeni ajan aç (Türker'in itirazı, 2026-10-18).

**Efor (Türker, 2026-10-18): "efor en fazla medium olsun, çok gerekli olan yerde high veya üstü olacak".** Her alt ajana Agent `effort` parametresiyle açıkça ver: varsayılan `medium` (ucuz mekanik işte `low`); `high`/üstü yalnız para gönderen/imzalayan/tutar hesaplayan görevlerin yapımı ve doğrulamasında (F8'de T152, T153), gerekçesi ilerleme notunda.

İlgili: [[project_f8_yonetici_duzeni]].
