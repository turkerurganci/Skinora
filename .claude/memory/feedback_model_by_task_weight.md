---
name: feedback_model_by_task_weight
description: "Yönetici düzeninde her işi Opus'a verme — basit işleri Sonnet/Haiku'ya ver; bulgu bulan, kod yazan, doğrulayan ve karar veren işler Opus'ta kalır"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 05a18c17-d163-437f-bb77-9c997ee193cc
  modified: 2026-10-17T22:05:14.888Z
---

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
