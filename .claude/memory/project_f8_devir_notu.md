---
name: project_f8_devir_notu
description: "F8 kaldığı yer (2026-10-20, yeni sohbete devir) — açık PR'lar, kalan adımlar, bekleyen sahip sorusu, ⚠ listesi ve maliyet günlüğünün yeri"
metadata:
  node_type: memory
  type: project
  originSessionId: 05a18c17-d163-437f-bb77-9c997ee193cc
  modified: 2026-10-20T20:08:39.083Z
---

F8 (Çok Ağlı Ödeme) 2026-10-20'de sahibin "durdur" talimatıyla durduruldu; yeni sohbette devam edilecek. Main `baefd8d9`.

**Bitti (main'de):** T141–T156, T158, T159 (koşullu PASS). Ayrıca PR #357 (belge başlıkları → `Docs/DOC_CHANGELOG.md`, plan → `Docs/plan/11_F8.md` vb.) ve #358 (hafıza temizliği).

**Açık PR'lar ve kalan adımlar (sırayla):**
1. **#354 T157** (dal `task/T157-admin-ag-basina`, HEAD `03721f96`): yeniden doğrulama ✓ PASS. Kapanış yarım kaldı: main'i birleştir (#357 belge düzeni + T159 çakışması), 4 küçük test (alıcı adresi harfli; başka ağ sınırı; çözülemeyen token belirsizliği; endpoint eşleme tablosuna 409/422), runbook §D 7.4 (elle bağlamadan önce verify MATCH + AuditLog satırı; N özdeş satırda toplam sayı+tutar yeter — yönetici kararı ⚠), rapor/status/MEMORY ✓.
2. **#356 T159a** (dal `task/T159a-ethereum-gas-tarifesi`, HEAD `16cc489e`): gas sabit varsayılmıyor (`eth_estimateGas`). Doğrulama yarıda durduruldu → main'i birleştir, sonra bağımsız doğrulama (opus/high).
3. **T159b**: Sepolia giden bacağı (yakıt, sweep, iade, yanlış token iadesi) elle ETH/ön token OLMADAN yeniden prova. Prova sırasında `blockchain.ethereum.fee_cap_usd_per_transfer` geçici 1,00 (sonra 0,50). Sepolia cüzdanlarında ~0,049 ETH var; sırlar `C:\projects\Escrow\secrets\` (yazdırma), prova env `secrets/sepolia-rehearsal.env`.
4. **T160 F8 Gate Check**: ⚠ listesini sahibe göster (para/güvenlik vurgulu), süre tablosu.

**Sahibe sorulacak (PARA, modal):** reddedilen ödeme iadelerinde (eksik/fazla/çoklu/yanlış token/geç ödeme) gas alıcıdan kesilmiyor; belgeler (08 §3.4, 02 §4.7, 05 §3.3) kesilir diyor; TRON'da iade başı platform maliyeti ~2,5–5 $. Seçenekler: belgeyi koda çek / kodu belgeye çek / karma.

**Dosyalar:** ⚠ listesi + süre tablosu + ayrıntılı devir: `C:\Users\teknodeveloper\.claude\projects\c--projects-Escrow\f8-handoff\PROGRESS.md`; maliyet günlüğü (sahip iş sonunda isteyecek — kanıtlı çıkarımlar): aynı klasörde `COST_LOG.md`.

**Düzen:** F8 bitene kadar yönetici düzeni (doğrulamaları ben başlatır/birleştiririm, doğrulama ajanı opus+high) — [[project_f8_yonetici_duzeni]]; her alt ajanda model+effort; paralel Docker kullanan ajan varken diğerine Docker kapattırma; CI'ı `gh run watch` yerine 2 dk `gh api` yoklamasıyla izle ([[reference_gh_secondary_rate_limit]]). Agent worktree "core.worktree redirect" hatası verirse önce `cd /c/projects/Escrow` çalıştır.

**Not:** Bu dosya henüz repo `.claude/memory/`'de yok — ilk açılan dalda (ör. T157 kapanışı) repoya kopyala ve repo MEMORY.md dizinine ekle.
