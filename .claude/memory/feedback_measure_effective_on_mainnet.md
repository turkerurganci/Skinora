---
name: feedback_measure_effective_on_mainnet
description: "Testnet'te gözlenemeyen bir zincir davranışını koda taşımadan önce ana ağdaki ETKİN sonucu makbuzlardan ölç; ayar alanı ve düğümün \"bekleyen\" hesap durumu gerçeği söylemez"
type: feedback
---

Bir zincir davranışı testnet'te **gözlenemiyorsa** (ör. Nile test USDT'si enerjiyi kendisi ödüyor), onu koda taşımadan önce **ana ağda etkin sonucu** ölç: gerçek işlemlerin makbuzları (`gettransactioninfobyblocknum` → `receipt`). Bir ayar alanını (`consume_user_resource_percent`) okumak etkin sonucu vermez.

**Why:** 2026-09-17, PR #323 doğrulaması. Kod ve 08 §3.1a *"Mainnet Tether bu değeri 100 yapar"* diyordu; hiç ölçülmemişti. Mainnet `getcontract` → **30**. Ama sahibin (origin) kalan enerjisi 0 olduğu için tek bir bloktaki 115 USDT çağrısının 115'inde de **çağıran %100** ödedi. PR'ın derlenmiş planı bu yüzden 64.285 enerjilik transfer için 19.286 enerji ayarlıyordu → her sweep/iade zincirde `OUT_OF_ENERGY` olurdu. Yapım turu yalnız Nile'da ölçmüş, Nile bu soruyu cevaplayamıyordu. Aynı turda ikinci ders: "enerji geldi / TRX vardı" kontrolleri TronGrid düğümünün **bekleyen** (bloğa girmemiş) durumunu okuyordu — devretme, transfer ve geri alma aynı blokta çıktı (Nile 71.019.348 #2/#5/#6). Hesap durumu okuması **onay değildir**; onay `gettransactioninfobyid` blok numarasıdır.

**How to apply:** Testnet'in cevaplayamadığı her varsayım için sor: *"bunun ana ağdaki etkin hâli hangi makbuz alanında görünür?"* — ve o alanı çok örnekle ölç (tek işlem değil, bir blok). PR'ın **kendi derlenmiş kodunu** canlı ana ağ durumuna karşı, kayıt tutan sahte imzacıyla (hiçbir şey yayınlamadan) koşmak en ucuz ayırt edici ölçümdür. İlgili: [[feedback_measure_the_failure_shape]] (hata cevabının şekli), [[feedback_check_external_assumptions]] (dış varsayımlar), [[feedback_verify_probe_subject]] (cevabı kim verdi).
