---
name: Check external assumptions before implementation
description: Implementasyona başlamadan önce kabul kriterlerindeki dış bağımlılık varsayımlarını (paid feature, plan tier, API rate limit, free tier kısıt) doğrula
type: feedback
---

Kabul kriterleri ya da plan dış bir servise/feature'a bağlıysa (GitHub plan tier, GitHub Pro feature, API rate limit, free tier kısıt, third-party trial), **implementasyon başlamadan önce 5 dakikalık bir doğrulama yap**: "bu varsayım gerçekten current environment'ta çalışıyor mu?" sorusuna kanıtla cevap ver.

**Why:** T11 close-out (2026-04-08) sırasında branch protection'ı `gh api PUT branches/main/protection` ile aktifleştirmeye çalıştım, **HTTP 403 "Upgrade to GitHub Pro"** aldım. GitHub Free + private repo'da branch protection ve rulesets paid feature olduğunu plan aşamasında kontrol etmemiştim. Sonuç: 3 düzeltme commit'i (0327315 → e44e3d2 → 8d7c3b1), yalan iddialar içeren bir close-out commit'i, validator'a re-validation talebi, INSTRUCTIONS.md ve CI_CD_SETUP.md'de discipline-only rejim oluşturma, T11_REPORT'ta BLOCKED bölümünün ekleme + sonradan "Çözüldü" formuna çevirme. ~1 saat ekstra iş + 4 ek commit + bir housekeeping PR.

**How to apply:**

Plan aşamasında (task scope sunmadan önce) şu kontrolleri yap:

1. **Kabul kriterleri tara:** "branch protection", "private repo", "rate limit", "API key", "trial", "subscription", "Pro", "Team", "Enterprise" gibi terimler içeren kriterler var mı?
2. **Dış bağımlılığı tespit et:** Hangi servise / feature'a bağlı? (GitHub plan, API provider, billing requirement, OS-spesifik araç)
3. **Varsayımı doğrula:** WebFetch / WebSearch ile "feature X for plan Y" kontrolü (5 dakika)
4. **Bulguyu scope'a yansıt:** Eğer assumption tutmuyorsa, alternatif planı **scope sunumunda** belirt — "X feature paid, Y discipline-only ile karşılayabiliriz, hangisi?"
5. **Plan aşamasında karar al:** Implementasyon başladıktan sonra kararı yapmak 3 düzeltme commit'i + yalan iddialar + re-validation gerektirir

**Spesifik tetikleyiciler (her zaman kontrol et):**
- "Branch protection / Required reviews / Code owners / Rulesets" → GitHub Team/Pro feature
- "GitHub Actions minutes / Runner / Artifact storage" → Free plan limitleri
- "Container registry (ghcr.io) public/private" → Plan-bağımlı
- "Secret scanning / Dependabot alerts" → Plan-bağımlı bazıları
- "Resend / Telegram / Discord / TronGrid API" → Free tier limitleri T78+ task'larda kritik olacak
- "TestContainers / Docker / SQL Server" → Lokal vs CI runner farkları
- "Steam Web API" → Rate limit ve API key approval süreci

**İstisna:** Eğer doğrulama 5 dakikadan fazla sürecekse (örn: deep doc reading, paid signup gerek), durdur ve kullanıcıya sor: "Bu varsayımı kontrol etmem gerekecek, X dakika alabilir, devam edeyim mi?".

**Validator notu:** Validator chat'leri bu tür blocker'ları ilk verdict'te "owner kanıtı bekler" diye işaretliyor. Bu sinyali görür görmez plan aşamasında çözülmemiş bir varsayım olduğunu kabul et — close-out'a kadar erteleme.
