---
name: Edit için izin sorma
description: Kullanıcı onay verdikten sonra edit/commit/push/PR adımları için tekrar izin isteme
type: feedback
---

Kullanıcı bir işi onayladıysa (örn. "yap", "onay", "devam"), o iş kapsamındaki dosya edit'leri, commit, push, PR açma gibi adımlar için tekrar **"hazırlayayım mı / yapayım mı"** diye sorma — doğrudan uygula.

**Why:** Kullanıcı 2026-04-23'te PR #51 (validate.md Adım 17 fix) sırasında açıkça "benden herhangi bir edit için izin isteme" dedi. Onay bir kez verildikten sonra implementasyon adımlarını parçalayarak tekrar onay istemek akışı yavaşlatıyor ve gereksiz.

**How to apply:**
- Kullanıcı bir öneriyi onayladığında: branch aç → edit yap → commit → push → PR aç — hepsi tek akışta, ara onay yok.
- Hâlâ onay gereken yerler (CLAUDE.md global kuralı geçerli):
  - Destructive/geri alınamaz işlemler (force-push, reset --hard, branch silme, migration drop, vb.)
  - Kapsam değişikliği (onaylanan işin dışına çıkan ek değişiklikler)
  - Paylaşımlı state (main'e merge, prod deploy, dış sistem mesajı)
- Lokal, geri alınabilir edit/commit/push adımları için soru yok.
