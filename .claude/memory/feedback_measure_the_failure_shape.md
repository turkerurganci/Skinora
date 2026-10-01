---
name: feedback_measure_the_failure_shape
description: Bir dis probu entegre ederken BASARISIZLIGIN nasil gorundugunu de olc; basarili cevapla ayni kalibi tasiyan hata cevabi sessizce gecer — ve bir ret kontrolu, ozellik bozukken de gelen reddi bekliyor olabilir (#325 runbook adim 6)
type: feedback
---

Bir dış API/prob entegre ederken **yalnız başarılı cevabı ölçmek yetmez** — aynı
ucun **başarısızlık şeklini de** ölçüp koda o ayrımı yazmak gerekir. Birçok API,
hatayı HTTP 200 ve "başarılı" görünen bir zarfla döndürür; kod yalnız zarfa
bakarsa hatayı geçerli bir cevap sanar.

**Neden:** 2026-09-06, PR #315 doğrulaması. TRON `triggerconstantcontract`,
revert eden bir çağrıya da `result.result: true` diyor; başarısızlık
`result.message` ("REVERT opcode executed") ve `transaction.ret: [{ret:"FAILED"}]`
içinde duruyor. Kod yalnız `result.result` ve `energy_used`'a baktığı için revert
geçerli tahmin sayılıyordu — Nile'da ölçüldü: revert **1.984** enerji, aynı
transferin başarılısı **29.650**. Kardeşi aynı turda: `getcontract`, kontrat
olmayan bir adrese **boş gövde** dönüyor ve kod bunu "sahibi öder = 0" sanıyordu.

Yapım turu üç kusuru "canlı ölçümle" bulmuştu ama **her ölçümü başarılı bir
cevap üzerinden** yapmıştı; iki kusur da bu yüzden hayatta kaldı. İkisi de
dosyanın **hiç testi olmayan** tek katmanındaydı — test boşluğu ile ölçüm
boşluğu aynı yerdeydi.

**Aynı aile, doğrulama adımında (PR #325 yeniden doğrulaması, 2026-09-19).** Runbook §C.2 adım 6, kilit hesabının iznini sınamak için yapılan TRX transferinin `"… is not contained of permission"` almasını bekliyordu. java-tron önce `checkPermission` (yalnız izin kimliği ≠ 0 iken: işlem türü kapsamda mı → `Permission denied`), sonra `checkWeight` (imzacı izinde mi → `not contained of permission`) çalıştırır. Beklenen mesaj **kimliksiz** denemenindi ve active iznin kapsamı ne olursa olsun geliyordu — izin "her şeye yetki" diye kurulsa bile kontrol geçerdi. Tuzağın ikizi aynı turun kendi geçmişinde yazılıydı (v1 Nile ölçümü: izin hiç kurulmadığı hâlde retler "eşleşti"), ama runbook yine yanlış reddi seçmişti. **Sor:** *"özellik bozuk olsaydı bu ret yine gelir miydi?"* Evet ise ret hiçbir şey kanıtlamıyor; kontrolü özelliğin bozulabildiği yere taşı (burada `Permission_id: 2`). Kardeşi: hex adres `TronWeb.isAddress`'ten geçiyor ama düğüm `visible: true` okumasında onu **HTTP 200 + `Error` gövdesiyle** reddediyor — okuyucu 0 sayar.

**Nasıl uygulanır:** Yeni bir dış uç entegre ederken en az iki probe koş —
biri başarılı, biri kasten başarısız (bakiyesi olmayan adres, olmayan kaynak,
yanlış parametre) — ve iki cevabı **yan yana koy**. Farkı ayırt eden alanı koda
yaz. Testte de iki şekli birden sabitle: yalnız mutlu yolu test eden bir suite,
bu sınıf kusuru asla yakalayamaz. İlgili: [[feedback_verify_probe_subject]]
(cevabı KİM verdi) — bu satır onun ikizi: cevap NE anlama geliyor.
