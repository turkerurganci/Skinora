---
name: project_ci_advisory_vs_setup_failure
description: Skinora CI'da run "failure" ama bloke edici joblar yeşilse — advisory legleri suçlamadan önce "Set up job" kırılmasına bak
type: project
---

Skinora CI'da 8 E2E leg (`e2e-smoke`) `continue-on-error: true`. T117'den beri hepsi
kırmızı (`Invalid object name 'PlatformSteamBots'`, leg başına tam 1 iz = "spec'lere hiç
ulaşmadı") ve **normalde run sonucunu düşürmez** — T129'un yeşil run'ında da 8'i job
düzeyinde kırmızıydı, run `success`'ti.

`continue-on-error` yalnız **adım** hatalarını tolere eder. Bir leg *"Set up job"*
aşamasında ölürse (runner düzeyi — ör. `actions/setup-dotnet` codeload'dan 429/503
alınca) tolerans işlemez ve **run `failure`** olur. Pre-push Layer 2 hook run
conclusion'a baktığı için push da bloklanır.

**Why:** T130'da (2026-08-17 GitHub kesintisi) run'ın kırmızılığını önce "advisory
legler yüzünden" diye açıkladım — yanlıştı. Bu gerekçe bir kez kabul edilirse gerçek
bir bloke edici kırılma da aynı cümleyle geçiştirilebilir; tam olarak
[[feedback_verify_status_before_quoting]] ve validator CI rasyonelizasyon yasağının
önlemeye çalıştığı hata.

**How to apply:** Run `failure` ama bloke edici joblar yeşilse önce
`gh run view <ID> --log-failed | grep -c "Failed to download archive"` çalıştır. Sıfır
değilse kırılma altyapıdır → `gh run rerun <ID> --failed`, kesinti geçene kadar tekrarla;
bypass değişkenine gitme, temiz run üret. Sıfırsa kırılma gerçektir, araştır.
Retry döngüsü yazarken `gh` çıktısının **boş** dönmesini (API hatası) "kırılma yok" ile
karıştırma — conclusion string'inin gerçekten `success` olduğunu doğrula.
