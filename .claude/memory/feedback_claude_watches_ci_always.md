---
name: Her acilan PR'in CI'sini Claude izler, kullaniciya sormaz
description: Actigim her PR'in CI run'ini ben izlerim — task/chore/docs/infra ayrimi yok, "sen mi izleyeceksin" sorusu yasak
type: feedback
---
Actigim her PR'in CI run'ini `gh run watch <ID> --exit-status` (veya esdeger polling) ile **concluded+success** olana kadar **ben izlerim**. Kullaniciya "CI'yi sen mi izleyeceksin ben mi" diye sormam. task PR, chore PR, infra PR, docs PR — ayrim yok.

**Why:** `.claude/skills/task.md` Bitis Kapisi'nda CI watch yazili ama **sadece task scope'unda**. PR #16 (chore: hook branch isolation) actigimda kullaniciya "CI'yi izle — gh run watch ile takip edeyim mi, yoksa sen mi bakacaksin?" diye sordum — bu implicit rasyonelizasyon ("chore icin opsiyonel"). Kullanici uyardi (2026-04-12): *"CI her zaman sen izleyeceksin bunu konusmadik mi? bu konuda eksik bir durum mu var"*. Konusulmus norm yaziliya genellestirilmemisti; ben de soru sorarak sorumlulugu kullaniciya attim. Bu, T11.2 rasyonelizasyon kaliplarinin kucuk bir versiyonu ("bu sefer kucuk degisiklik, CI bekleyemez" yerine "bu chore, kullanici bakabilir"). Yasak.

**How to apply:**

1. Her `gh pr create` sonrasinda, PR numarasi gelir gelmez:
   ```bash
   gh run list --branch <branch> --limit 1 --json databaseId,status,conclusion
   ```
   ile run ID'yi al. Run in_progress ise:
   ```bash
   gh run watch <ID> --exit-status --interval 20
   ```
   **arka planda** baslat (run_in_background). Boylece kullanici baska konuya gecebilir ve background notification gelince raporlarim.

2. Final verdict'i **kendim** kullaniciya raporlarim:
   - `success` → "CI run #XXX basarili, merge icin hazir" (kullanicidan merge onayini bekle — merge disinda karar vermem)
   - `failure` → root cause'u `gh run view <ID> --log-failed` ile ara, root cause + duzeltme onerisi sun, kullaniciya onaylatip duzelt + yeniden push + yeniden izle.
   - `cancelled`/`timed_out`/`action_required`/`startup_failure` → hepsi `failure` gibi ele alinir.

3. Kullanici "CI'yi izle" demez — **default davranis budur**. Izlemek icin onay istemem. Sadece CI sonucuna gore alinacak _aksiyonlar_ (merge, yeniden push, root cause duzeltme) icin onay isterim.

4. Kullanici konusmayi degistirse bile background watch devam eder. Notification geldiginde konusmanin o noktasinda sonucu rapor ederim.

5. **Istisna yok:** task PR, chore PR, infra PR, docs PR, validator fix PR — hepsi ayni. Task dalinda pre-push Layer 2 (CI guard) zaten failure'i blokluyor ama CI watch farkli bir sorumluluk (sonuc raporlama + root cause takibi).

6. Kullaniciya CI ile ilgili soru sorma sablonlari **yasak**:
   - "CI'yi sen mi izleyeceksin?"
   - "gh run watch ile takip edeyim mi?"
   - "CI yesillenince haber verir misin?"
   - "Merge'den once CI'yi kontrol ettin mi?"
   
   Hepsi: **hayir**. Sorumluluk bende, kullanici raporlama bekler.

**Iliski:** Bu kural T11.2 Adim 0 (main CI startup check HARD STOP) ve task.md Bitis Kapisi (CI run tamamlandi+success) kurallarinin PR yasam dongusu boyunca genellestirilmis halidir. Task-only yazilmisti, evrensellestirildi.
