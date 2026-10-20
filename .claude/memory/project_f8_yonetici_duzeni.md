---
name: project_f8_yonetici_duzeni
description: "F8 boyunca Shopfolio'nun yönetici düzeni — her görev ve doğrulama temiz bağlamlı alt ajanda, PASS + CI yeşilse ben merge ederim, yalnız çok kritik sorular; F9 açılışında yeniden sorulur | 2026-10-20: F8 bitene kadar bu düzen sürer (doğrulama ajanı opus+high); SubAgentIndependentValidation akışı F9'dan itibaren"
metadata:
  type: project
---

Türker 2026-10-17'de F8'in (TRON + Ethereum, T141–T160) Shopfolio'daki yöntemle, benim yönetimimde yapılmasını istedi ("shopfolioda son zamanlarda uyguladığımız yöntemle bu fazı yapacaksın"). Modalda iki açık karar verdi: **F8 boyunca birleştirme yetkisi bende** ve **yalnız çok kritik sorular** sorulacak.

**Why:** Shopfolio'da bu düzenle bir aşamanın on altı adımı tek günde yürüdü; Türker her adımda yeni chat açmak ve kolay soruları cevaplamak istemiyor. Shopfolio'nun otonom koşu betiği (`scripts/autopilot`) hâlâ denemede ve yalnız doküman yazımına kurulu, bu yüzden F8'de Agent alt ajanlarıyla yürütülür.

**How to apply:**
- Her görev için temiz bağlamlı yapım alt ajanı; ardından yapım raporunu görmeyen ayrı doğrulama alt ajanı (INSTRUCTIONS §3.3 izolasyon girdileri aynen). PASS + PR CI yeşilse squash-merge, merge sonrası CI izlenir; teyit run ID'leri sonraki dalda kayda girer ([[feedback_merge_teyit_not_direct_pushable]]).
- Kolay kararlar öneriyle verilir, `(öneriyle kaydedildi — ⚠)` işaretlenir, F8 Gate Check'ten önce tek listede gösterilir. Para, güvenlik, geri dönüşü zor kararlar sorulur.
- Yarım saati aşan adımda tek satır ara durum; faz sonunda tahmin–gerçekleşen süre tablosu (ölçüm scratchpad'deki PROGRESS.md'de).
- Bekleyen #335 ve #334 da bu düzenle doğrulanıp birleştirilir (Türker 2026-10-17: "onu da sen yapacaksın").
- **Her merge'den sonra ana checkout'ta `git pull` (main).** Hook'lar yerel `main` ref'ine bakıyor; bayat kalırsa paralel yapım ajanları main'in yeni commit'lerini "başka görev" sayıp engelleniyor ve `SKINORA_ALLOW_BUNDLED` ile atlatmaya gidiyor (T144, 2026-10-18).
- Yetki ve soru düzeni yalnız F8 içindir; F9 açılışında yeniden sorulur. Kural katmanındaki evi: `.claude/INSTRUCTIONS.md` §3.3 "F8 yönetici düzeni" maddesi.

- **2026-10-20 proje sahibi kararı (genel `SubAgentIndependentValidation` akışı ile ilişki):** F8 bitene kadar (T160 F8 Gate Check dahil) bu eski düzen sürer — doğrulamaları yönetici başlatır ve PASS + CI yeşilse birleştirir, "talimatı göster → başlat" onayı beklenmez. Tek değişiklik model: doğrulama ajanı artık `opus` + `high` ile açılır (2026-10-18'deki "para dışı doğrulama Sonnet" maddesi bununla değişti, bkz. [[feedback_model_by_task_weight]]). `SubAgentIndependentValidation` akışı (`C:\Users\teknodeveloper\.claude\skills\SubAgentIndependentValidation\SKILL.md`: talimatı yazıp Türker'e göster, "başlat" demeden başlatma, `general-purpose` alt ajan, rapor dosyasından oku) F9'dan itibaren geçerli. Bkz. [[feedback_validation_separate_chat]].
