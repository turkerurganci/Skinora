---
name: feedback_verify_probe_subject
description: Bir probe cevap verdiğinde önce CEVABI KİMİN VERDİĞİNİ doğrula; ayakta kalmış eski süreç/build sessizce eski gerçeği raporlar
type: feedback
---

Bir ölçüm probe'u cevap verdiğinde, cevabın **az önce ürettiğin şeyden** geldiğini varsayma — önce özneyi doğrula. Süreç ayakta mı, hangi build'i servis ediyor, portu gerçekten senin başlattığın süreç mi tutuyor?

**Why:** Bu projede iki kez aynı aileden kusur çıktı. (1) `F7-N4`: 11 container "healthy" ve beş sağlık ucu 200 dönüyordu ama image'ler bir önceki fazdandı — sinyal doğruydu, öznesi yanlıştı. (2) F5 turu (2026-08-24): `/admin/users` sayfası yazıldıktan ve build alındıktan sonra koşulan statik probe *"eski iskelet hâlâ orada"* dedi; gerçek sebep port 3100'de **F6 turundan kalma eski `next start` süreciydi**. Yeni sunucu sessizce `EADDRINUSE` alıp ölmüştü (nohup log'unda duruyordu, çıktıya bakılmamıştı) ve probe eski build'i okuyordu. Bu bulgu "kod bozuk" diye teşhis edilseydi var olmayan bir hata kovalanacaktı.

**How to apply:**
- Arka planda bir sunucu/servis başlattıktan sonra **log'unu oku** — `nohup ... &` sessizce ölür; "curl 200 döndü" onun ayakta olduğunun kanıtı DEĞİLDİR.
- Bir probe beklenmedik/eski bir sonuç verdiğinde ilk hipotez **"yanlış özneyi ölçüyorum"** olsun, "kod bozuk" değil. `netstat -ano | grep :<port>` ile PID'i, PID'in yaşını ve neyi servis ettiğini kontrol et.
- Probe'un ölçebileceği şeyi de doğrula: F5'te SSR gövdesi "Skinora" dışında boştu, çünkü admin rotaları `AdminGuard` arkasında **istemci tarafında** çiziliyor — probe yeşil de dönse o sayfa hakkında hiçbir şey söylemiyordu. Ölçemediğini **ölçemedim** diye raporla.
- İlgili: [[feedback_verify_metric_definition]] (sayıyı üreten komut ne sayıyor) ve [[feedback_differential_before_causal_claim]] (belirtiyi nedene bağlamadan önce ayırt edici ölçüm).
