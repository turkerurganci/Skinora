# Skinora — Implementation Plan

**Versiyon: v0.9** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `09_CODING_GUIDELINES.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-08-20 (**T139 doğrulaması tur 2 ✗ FAIL → düzeltme turu 2** — §F7 P7 T139'a **DÜZELTME TURU 2** bloğu yazıldı: bir bloke edici bulgu (**B1-2** — `EnsurePaymentMonitorJob` adayları tek `Take(BatchSize=200)` + `CreatedAt` **artan** sırayla alıyordu; tavan `EnsurePaymentAddressJob`'dan kopyalandı ama orada küme işlendikçe **drene olur**, burada arm satırı `ACTIVE` bıraktığı için olmaz → küme 200'ü aşınca her tur aynı en eski dilimi mutabık kılıyor ve **en yeni ödeme pencerelerine hiç sıra gelmiyor**; kümeyi bu görevin kendi N1 bulgusu ≈ bir haftalık hacim olarak ölçmüştü, eşik ~29 tx/gün, ve aç kalan popülasyon tam olarak para-kritik olan) ve iki bloke etmeyen madde (**N1-2** AC5'in "port metotları statü eşlemesi dahil" yarısı hiç test edilmemişti — yol / alan adları / `SendCommandAsync` eşlemesi; **N2-2** `skinora_blockchain_active_monitors` etiketsiz tek gauge'a iki registry ayrı ayrı `.set(size)` yazdığı için toplam değil son yazan raporlanıyordu ve bu turun N1 telafisi ile runbook §G.4 kanıtı ona bağlıydı). Üçü de proje sahibi kararıyla **aynı dalda** kapatıldı. **KALICI DERS:** bir tavan, üzerinde durduğu kümenin **drene olup olmadığı** sorulmadan kopyalanamaz — iki job'ın `Take` satırı birbirinin aynısıydı, fark koddan değil kümenin davranışından geliyordu; ve bir kararın bedelini bir metriğe havale etmek, o metriğin doğru olduğunu **ayrıca** doğrulamayı gerektirir.) · 2026-08-20 (**T139 doğrulaması tur 1 ✗ FAIL → düzeltme turu** — §F7 P7 T139'a **DÜZELTME TURU** bloğu yazıldı: bir bloke edici bulgu (**B1** — `PaymentMonitorStartDispatcher` DI'a hiç kaydedilmemiş; `Skinora.Transactions` MediatR tarama listesinde olmadığı için üç kardeş handler elle kayıtlıyken dördüncüsü atlanmış ve yerine "assembly scan bulur" diyen yanlış bir yorum yazılmıştı — sıfır handler'a yayınlanan olay sessizce PROCESSED damgalanıyor, sidecar hiç çağrılmıyordu) ve iki bloke etmeyen madde (**N1** D3'ün ölçülmemiş bedeli: pencere 7 günlük mutabakat tabanı yüzünden bir haftadan uzun açık kalıyor, eşzamanlı izleyici sayısı bir haftalık hacimle ölçekleniyor · **N2** reconciler'ın bayat snapshot'ı devir sonrası aktif izleyiciyi diriltebiliyor, disarm kolu ise `RowVersion` sayesinde ezemiyor ama istisnayı ele almıyordu). Üçü de proje sahibi kararıyla **aynı dalda** kapatıldı. **KALICI DERS:** bir handler'ın VAR olması ile ULAŞILABİLİR olması aynı şey değildir ve birim testi ikincisini göstermez — B1 tam olarak T139'un kapattığı kusurun ikizidir (orada bağlanmamış caller, burada bağlanmamış consumer) ve AC3'ün reconciler'ı onu ürün seviyesinde maskeliyordu; bu yüzden düzeltme tek örneği değil **sınıfı** kapatıyor (reflection bekçisi).) · 2026-08-20 (**T139 yapımı** — §F7 P7'ye **T139** eklendi: aktif ödeme izleyicisinin backend'de hiç çağıranı yok (`POST /api/monitor/start`), kaynağı T133b doğrulamasının B4 bulgusu / `DEFERRED_BACKLOG` `T133b-PaymentMonitorUnarmed`. Ölçüm satırın yazdığından geniş çıktı: kurma, restart kurtarması ve durdurma halkalarının **üçünün de** sahibi yok — `MonitoringStatus.ACTIVE` mutlu yolda hiç STOPPED'a dönmüyor. **Dört kapsam kararı proje sahibi onaylı ve plana yazıldı:** D1 numaralı task (doğrudan fix PR değil) · D2 tam yaşam döngüsü (yalnız arm reddedildi — hiç durmayan izleyici + iptal yolunda çift registry) · D3 pencere sweep'e kadar (`PAYMENT_RECEIVED`'da durdurma reddedildi çünkü 02 §4.4 fazla-tutar ve 03 §5.5 ikinci-ödeme kolları ödeme onayından sonra gelen transferi görmeyi gerektirir) · D4 enum'a değer eklenmez, ACTIVE'in anlamı 06 §3.7'ye yazılır (migration + parity maliyeti karşılığında hiçbir davranış kazanılmıyor). F7 aralığı T115–T138 → **T115–T139**.) · 2026-08-19 (**T133a yapımı** — §P6 T133a bloğuna **KAPSAM NETLEŞTİRMESİ** yazıldı: dört karar (D1 emekli gövdelerin silinmesi · D2 02 §18.2 kapsama alınması · D3 sürüm numaralarının düzeltilmesi · D4 sınıf yeniden adlandırması) ve yetki parity'sinin makinece doğrulanabilir hale getirilmesi. **Kriterin iki ifadesi bayat çıktı ve KAYNAK dokümanda düzeltildi** (T122'nin kalıcı dersi): (a) "07 v3.1 + 03 v3.2 sürüm notları" T118 dönemine aitti — harfiyen uygulansa sürüm GERİ giderdi; beş doküman bump'landı. (b) Doğrulama "üç tablodaki KEY kümesini karşılaştır" diyordu ama 04 §8.8'de ANAHTAR YOKTU — `Anahtar` kolonu eklendi, kriter artık makinece koşuyor. **Sahipsiz iki kalem sahiplendirildi:** 02 §18.2 (bildirim kataloğunun dördüncü nüshası, hiçbir görevin kapsamında değildi) bu turda kapatıldı; üç FE nüsha sapması T134/T136 kabul kriterlerine yazıldı ve DEFERRED_BACKLOG §9'a altı satır açıldı. **KALICI DERS:** bir kabul kriterinin DOĞRULAMA YÖNTEMİ, kriterin kendisi kadar bakım ister — "key kümesini karşılaştır" diyen bir kriter, karşılaştıracağı tabloda key olmadığı için WP5'ten beri sessizce ölçülemez durumdaydı ve iki eksik satır tam olarak bu yüzden aylarca görünmedi.) · 2026-08-18 (**T137 doğrulaması tur 2 ✓ PASS → görev kapandı; bir gözlem proje sahibi onayıyla plana işlendi.** Düzeltme turunun sekiz kabul kriterinin (B1-a…f + N1 + N2) **sekizi de** bağımsız kanıtla karşılandı, yapım turunun beş kriteri regresyon teyidi olarak yeniden üretildi (validator fake'i lokalde kendi portlarında koşturup per-steamId envanteri, trade + assetId rotasyonunu + ters bacağı, 422/503 paritesini, sürülebilir trade-hold'u ve emekli custody uçlarının 404'ünü kendi HTTP çağrılarıyla üretti; assetId'ler tur 1'inkiyle birebir çıkarak `fakeAssetId` determinizmini de kanıtladı), **bloke edici bulgu 0**. **Ölçüm sıfırdan yeniden sayıldı:** taban `787b1b3` 10/32 ↔ dal HEAD 10/32, leg dağılımı birebir; raporun "sayı değil KÜME aynı" iddiası **programatik olarak** doğrulandı (karışık üç leg'in tüm test başlıkları süre damgaları soyulup `diff`'lendi → üçünde de IDENTICAL). Mekanizma: `ITEM_NOT_IN_INVENTORY` yedi leg'de 0 / downtime'da 2, `Invalid object name`+`Invalid column name`+`PK_Users` sekiz leg'de 0, `ITEM_NOT_TRADEABLE`+`INVENTORY_PRIVATE`+`STEAM_UNAVAILABLE` sekiz leg'de 0, ve kalan **18 poll timeout'unun 18'i de** `(last status=ACCEPTED)` — create ve accept geçiyor, işlem T117'de emekli edilen custody durumunda takılıyor. Aynı kod **altı** bağımsız run'da 10/32 verdi. **G1 (gözlem → §T138'e işlendi):** `downtime.spec.ts:167,238` `resetFakeSteamState()`'i seed'den SONRA test gövdesinde çağırıp ortak seed'i siliyor; §T138'in envanter-seed kriteri vakayı sınıf olarak zaten kapsıyordu ama mekanizma adıyla yalnız T137 raporunda duruyordu — proje sahibi onayıyla kriterin altına **adıyla** yazıldı. **KALICI DERS (T137'nin kendi B1'inin bir ölçek küçüğü):** bir vakayı kapsayan bir kriterin VARLIĞI, o vakanın kaynak dokümanda ADININ geçmesini gereksiz kılmaz — B1 tam olarak "bedel yalnız task raporunda duruyordu" diye bloke etmişti; aynı sınıfın kalan tek örneğini raporda bırakmak, kapatılan dersi yarım bırakmak olurdu.) · 2026-08-18 (**T137 doğrulaması ✗ FAIL → düzeltme turu** — rapor [`T137_REPORT.md`](TASK_REPORTS/T137_REPORT.md) §Doğrulama — Tur 1. Dal `task/T137-fake-drivable-inventory`, PR [#246](https://github.com/turkerurganci/Skinora/pull/246). Proje sahibi onayıyla bir bloke edici bulgu (**B1**) ve iki bloke etmeyen madde (**N1**, **N2**) §P7 T137'ye **düzeltme turu kabul kriteri** olarak yazıldı; yapım turunun beş kararı (**D1–D5**) da **NİHAİ ŞEKİL** olarak aynı bloğa alındı. Beş kabul kriterinin **beşi de** bağımsız olarak yeniden üretildi ve doğru bulundu (canlı HTTP probe: per-steamId envanter · trade + assetId rotasyonu + ters bacak · 422/503 sidecar paritesi · sürülebilir trade-hold · emekli custody uçlarında 404; 38/38 unit; dal HEAD CI run 32146723383 success, bloke edici 14/14). **Bloke eden şey kod değil, kararın ve sahipliğin KAYDIDIR. B1:** D1 ("bilinmeyen steamId → boş envanter") bedeli ölçülüp kabul edilmişti — advisory e2e merge-base 787b1b3'te **10/32** iken dal HEAD'de **4/32** (timeout 1→0 · fraud-flags 3→0 · admin-flows 6→4; imza tek ve deterministik: `ITEM_NOT_IN_INVENTORY`, çünkü `TransactionCreationService` Stage 5 satıcı envanterini okuyor ve **hiçbir spec/harness `setFakeInventory` çağırmıyor**) — ama ne D1–D5 ne de bu bedel plana yazılmıştı, ve seed yükümlülüğü **hiçbir task'ın kabul kriterinde** geçmiyordu (yalnız T137 raporunun Known Limitations maddesinde). **KARAR: "T137'de kapat + plana yaz"** — `seedHappyPath()` satıcının fake envanterini seed eder (tek noktalı harness değişikliği: 9 spec'in **tamamı** bu fonksiyonu çağırıyor ve o, spec'lerin `beforeEach` reset'inden SONRA koşuyor), seed **yalnız satıcıya** yapılır ki alıcının SIFIR baseline'ı — D1'in kazancı — korunsun; hiçbir spec senaryosuna dokunulmaz (yeniden yazım T138'de kalır). **N1:** §T138'in "yalnız admin-flows T137'den bağımsız" iddiası ölçümle yalanlandı (6/7 → 4/7, üç düşüşün üçü de create aşamasında) ve düzeltildi. **N2:** `e2e/src/db.ts:43-44` yorumu bayat. **KALICI DERS:** bir kararın bedeli ölçülüp kabul edilmiş olsa bile, o bedel KAYNAK dokümana yazılmadıkça ve bedeli geri alacak işin bir SAHİBİ olmadıkça karar yarım kalır — T122'nin "kaynak dokümana yaz" dersiyle T129 tur 3'ün "advisory sinyal sahipsiz olduğu için ölür" dersi T137'de aynı anda gerçekleşti.) · 2026-08-18 (**T131 düzeltme turu UYGULANDI** — §P5 T131'deki düzeltme turu bloğunun üç maddesi (**B1** · **N1** · **N2**) kapatıldı ve turun iki kapsam kararı **NİHAİ ŞEKİL** olarak aynı bloğa işlendi; her ikisi de seçenekli sunulup **önerilen seçenekler onaylandı**. **B1 (D6):** ayırt edici alan `Transactions.TimeoutReleasedByAdminRulingAt` (1 additive nullable kolon, migration `T131_TimeoutReleasedByAdminRulingAt`); damgayı yalnız teslimat turunun admin-serbest-bırakma kolu, kararın yanında yazar ve `ReputationAggregator` + `CancelCooldownEvaluator` damgalı satırı hem sorguda filtreler hem switch'te açık guard'la reddeder (T129 `REFUNDED` bölmesinin deseni — sorgunun ileride genişlemesi sessizce yeniden ceza yazmamalı). Statü `CANCELLED_TIMEOUT` kalır. **D6a:** D7'nin gereği olarak akışın ürettiği TÜM yan kayıtlar tarandı (`TransactionTimedOutEvent` → yalnız bildirim/realtime · `PaymentRefundToBuyerRequestedEvent` · `TransactionHistory` · itibar + cooldown); satıcının kaydına yazan başka yol yok, yani B1'in adlandırdığı iki tüketici akışın tamamı. **N2:** karşılaştırma `Disputes.ResolvedAt` ↔ imzanın **ilk** gözlem anı; belirsiz girdiler ("eşit an", NULL `ResolvedAt`) "görmemiş" sayılır, **en eski** capture okunur. Karar port'un iki ucu arasında bölündü — tur imza anını parametre olarak verir (ilk turda kendi saati, dolayısıyla mevcut her karar zorunlu olarak ondan önce), adapter yeni `ReEscalatedAfterRuling` döner, tur `Held`'e çevirir ve damgayı yazmaz. Yeniden eskalasyonda **aynı satır** `ESCALATED`'a döner, `ResolvedAt` temizlenir, `AdminNote`/`AdminId` korunur, önceki karar audit'te kalır (06 §3.20); kural 02 §10.2'ye yazıldı — oradaki yasak ikinci bir dispute SATIRIDIR. `CLOSED` kolun dışındadır. **N1:** `DeliveryTimeoutDecision.Cancel` doc'undan tek üreticiye ait "timeout recorded against the seller" cümlesi çıkarıldı, iki üretici de adıyla yazıldı. **Test:** backend **2779/2779** (taban 2768 → **+11**) · build 0E/0W. **Doküman:** 02 v3.6→v3.7 (§10.2 yeniden eskalasyon) · 03 v3.5→v3.6 (§6.4 iki alt madde) · 06 v6.9→v6.10 (§3.1 formül bölmesi + §3.5 kolon).) · 2026-08-18 (**T131 doğrulaması ✗ FAIL → düzeltme turu** — proje sahibi onayıyla bir bloke edici bulgu (**B1**) ve iki bloke etmeyen madde (**N1**, **N2**) §P5 T131'e **düzeltme turu kabul kriteri** olarak yazıldı. Dört kabul kriterinin **üçü** (AC1 dört kalıntı · AC2 D1-D2-D3 · AC3 ekran + 4 dil parity) bağımsız olarak yeniden üretildi ve doğru bulundu; AC4'ün D5 kolu da doğru. Açık kalan **D4'ün karar metnidir**. **B1:** kapı "admin baktı, iptal artık sessiz değil" gerekçesiyle açılıyor ama açılan akış `PAYMENT_RECEIVED → CANCELLED_TIMEOUT` üretiyor ve `ReputationAggregator` + `CancelCooldownEvaluator` bu geçişi satıcıya yazıyor — yani admin'in `SELLER_FAVOR` ile **açıkça akladığı** satıcının `SuccessfulTransactionRate`'i düşüyor ve aynı satır cooldown penceresine giriyor; itibarın admin düzeltme yüzeyi olmadığı için ceza kalıcı. D4 ise "satıcının kaydına kusur yazılmaz" diyor ve bu iddia bu görevde 03 §6.4'e de yazıldı — repo'da yanlış bir normatif cümle bırakıyor. **KARAR D6: kod düzeltilir** (sorumluluk haritaları admin kararıyla serbest bırakılmış satırı ayırt eder ve sayım dışı bırakır, `CANCELLED_ADMIN` sınıfı gibi); dokümanı düzeltip bedeli kabul etme alternatifi reddedildi. **KALICI DERS (D7):** bir kapıyı açan karar, o kapının ARDINDAKİ akışın ürettiği YAN KAYITLARI da denetlemelidir — T129 B3'ün ikizi (orada formülün TETİKLEYİCİSİ eksikti, burada tetiklenen formülün GİRDİSİ yanlış sınıflanıyor). **N1:** `DeliveryTimeoutDecision.Cancel` XML doc'u yeni üreticisi için yanlış ("seller still holds the item" — imza tam tersini söyler); AC1'in kapattığı "kod değişti, sözleşme eski sözü veriyor" sınıfının genişleme yönündeki ikizi. **N2:** ilk-tur serbest bırakma, admin'in hiç görmediği bir imzayı iptale çevirebiliyor (karar imzadan ÖNCE verilmişse); D5'in gerekçesi o vakayı kapsamaz, iptal hâlâ sessizdir.) · 2026-08-17 (**T130 doğrulaması ✓ PASS → görev kapandı; iki bloke etmeyen bulgu proje sahibi onayıyla işlendi.** 5/5 kabul kriteri bağımsız olarak yeniden üretildi (dal HEAD `aa075fb`, validator'ın kendi koştuğu unit 1433/1433 + integration 1312/1312 exit 0), merge `523dc97` (PR #242), post-merge main CI + Docker Publish ✓. **N1 (doküman hijyeni, bu turda DÜZELTİLDİ):** T130 dört kaynak dokümanı değiştirmişti (ikisi şema/sözleşme) ama hiçbirinin sürüm/`Son güncelleme` başlığını güncellememişti — T129 aynı dosyalarda güncellediği için konvansiyon yerleşikti; 02 v3.4→v3.5, 03 v3.3→v3.4, 06 v6.7→v6.8, 07 v3.4→v3.5 bump edildi ve T130 girişleri yazıldı. **N2 (sahipsiz yüzey → T131'e devredildi):** `deliveredItemName` AD28 cevabına kadar geliyor ama admin EKRANINDA hiç gösterilmiyor (`DisputeResolveModal.tsx` yalnız `systemCheckResult` render ediyor, TS tipinde alan yok); yapım raporu bunu T134/T135'e havale ediyordu ama o task'ların kabul kriterleri admin dispute ekranını kapsamıyor (T136 admin **bot** sayfaları) — yani alan sahipsizdi. **KALICI DERS:** bir alanın API sözleşmesine ulaşması onu kullanılır yapmaz; zinciri bitiren YÜZEYİN de bir sahibi olmalıdır — T137'nin "advisory sinyal sahipsiz olduğu için ölür" dersinin ikizi, bu kez ölen şey bir kanıt alanı.) · 2026-08-17 (**T129 üçüncü doğrulama ✓ PASS → görev kapandı; E2E ağı için sıra düzeltmesi** — T129 merge edildi (`2dbd0c1`, PR #240). Doğrulamanın T129 kapsamı dışında kalan bulgusu proje sahibi onayıyla F7 bloğuna işlendi: sekiz advisory E2E leg'in hepsi T117'den beri `e2e/src/db.ts`'nin emekli `PlatformSteamBots` atıfları yüzünden **setup'ta** ölüyor, yani F7'nin para hareket ettiren görevleri uçtan uca ağ hiç çalışmazken iniyor. **T138 öne ALINMADI** (bağımlılığı T135 gerçek — hareketli FE'ye karşı yazılan spec iki kez yazılır); **T137 P5 ile paralel başlatılıyor** (planın kendi notunun gereği, tek bağımlılığı T120 çoktan kapandı, görev [RİSKLİ] ve tüm E2E'nin tek blokeni); yeni **T137a** ölçüm görevi harness'i onarıp ağın ne kadarının ayakta olduğunu ölçer ve T138'in "9 spec" TAHMİNİNİ gerçek sayıyla değiştirir. **KALICI DERS:** advisory bir sinyal "bloke etmediği" için değil **sahibi olmadığı** için ölür — dört görev boyunca her turda doğru şekilde "benim kaynaklı değil" denip hiç kapatılmadı.) · 2026-08-17 (**T129 ikinci düzeltme turu uygulandı** — §P4 T129'daki İKİNCİ DÜZELTME TURU bloğunun dört maddesi (B4 · N8 · N9 · N10) kapatıldı ve turun iki kapsam kararı **NİHAİ ŞEKİL** olarak aynı bloğa işlendi. **D3:** B4(a) sınıf bazlı koşul yerine **tam sıralama** olarak uygulandı (`SettlementReviewReasons.Strength`: UNREADABLE = NO_DELIVERY_REFERENCE < AMBIGUOUS_DEPARTURE < REVERSAL_GATED; yeni tur yalnız kesin olarak daha güçlüyse yazar) — kriterin harfini karşılar, üstüne sınıf içi düşürmeyi de kapatır çünkü DEPLOY_RUNBOOK §I.3 kapı açma kararını `REVERSAL_GATED` satırlarını sayarak verir. **D4:** N9 hem 02 §4.5.1 "Bilinen sonuçları"na hem runbook §I.2'ye yazıldı (`DEFERRED_BACKLOG` reddedildi — bu bir iş maddesi değil, ürün kararının bilinen bedeli). **D5 (turun kendi bulduğu kenar durum):** yapışkanlık yanlış ilk etiketi de sabitlediği için referans-yok kontrolü `VerifyAsync`'in başına alındı — çözülemeyen alıcı artık kalıcı olarak cevapsız bir vakayı `SETTLEMENT_UNREADABLE` etiketiyle §I.3'e göndermiyor. Şema değişikliği yok. **KALICI DERS uygulandı:** rank'lanmamış bir kod parayı serbest bırakan yönde sessizce kaybolacağı için rank tablosunun kendisi reflection guard'ıyla test edildi.) · 2026-08-17 (**T129 yeniden doğrulaması ✗ FAIL → ikinci düzeltme turu** — proje sahibi onayıyla bir bloke edici bulgu (**B4**) ve üç bloke etmeyen madde (N8–N10) §P4 T129'a **ikinci düzeltme turu kabul kriteri** olarak yazıldı. Düzeltme turunun on dört maddesinden **on üçü** (B1a–d · B2 · B3 · N1 · N3–N7) bağımsız olarak yeniden üretildi ve doğru bulundu; açık kalan **N2**'dir. **B4:** yapışkanlık `SettlementEscalationReason` alanının DEĞERİNE bağlandı ama alanı yazan kol düşürmeye karşı korunmadı — `EscalateAsync` gerekçeyi koşulsuz üzerine yazıyor ve eşik geçmişken gelen tek bir `Inconclusive` tur `SETTLEMENT_REVERSAL_GATED`'i `SETTLEMENT_UNREADABLE`'a çeviriyor; sonraki `Verified` turunda `ClearForPayout` artık bloklamıyor ve para, açık bir `ADMIN_ESCALATION`'ın üstünden, ikinci bir bildirim gitmeden çıkıyor. **KALICI DERS:** yapışkanlığı bir alanın değerine bağlayan tasarım, o alanı yazan HER kolun düşürme yönünü de denetlemelidir — "kural yazıldı" ile "kural korunuyor" yine farklı çıktı ve iki karşıt yapışkanlık testi yalnız yükseltme yönünü sabitliyordu (B3 dersinin ikizi: orada formülün TETİKLEYİCİSİ eksikti, burada kuralın KORUYUCUSU).) · 2026-08-16 (**T129 doğrulaması ✗ FAIL → düzeltme turu** — proje sahibi onayıyla üç bloke edici bulgu ve beş bloke etmeyen madde §P4 T129'a **düzeltme turu kabul kriteri** olarak yazıldı. **B1 için karar SEÇENEK 1** (ayrı verdict + admin aksiyonu): alıcı envanteri `SELLER_CONFIRMED` anında gizli olan işlemlerde mutabakatın karar girdisi (`baseline` / `DeliveredBuyerAssetId`) hiç üretilemiyor, kontrol sonsuza kadar `Inconclusive` kalıyor ve payout + sweep + COMPLETED üçü birden kalıcı kilitleniyor — admin'in kolu yok çünkü `admin_resolve_refund` yalnız alıcının açabileceği bir dispute üzerinden ateşleniyor. Alternatifler (ITEM_DELIVERED'da envanter okuması / kontrolden muafiyet) reddedildi: ilki gizli-envanter vakasını kapatmıyor, ikincisi 02 §4.5.1'in korumasını o popülasyon için kaldırıyor. **B2** `DELIVERY_REVERSED` ne 06 §2.11'de ne bir admin yüzeyinde, **B3** itibar formülünü çalıştıran refresher geri alma yolunda çağrılmıyor (06 §3.1 ↔ §8.2 çelişkisi). **İKİ KALICI DERS:** (1) bir kapı, koruduğu değerin yazarlarını denetlemekle bitmez — kendi KARAR GİRDİSİNİN hiç üretilemediği popülasyonu da denetlemelidir; girdisi asla gelemeyecek bir fail-closed kapı fail-closed değil **fail-frozen**'dır (T124/T126 dersinin bir sonraki halkası). (2) Denormalize bir alanda "kural yazıldı" ile "kural işliyor" farklı şeylerdir ve birim testi ikincisini göstermez — formülü genişleten görev, o formülü ÇALIŞTIRAN tetikleyiciyi de genişletmelidir.) · 2026-08-15 (**T127 doğrulaması ✗ FAIL → düzeltme turu** — proje sahibi onayıyla dört karar plana işlendi: **T127 AC3'ün NİHAİ ŞEKLİ** yazıldı (dal tüketiyor ama ayrı sorgu + ayrı tavanla; kriterin özü karşılandı, harfi bilinçli olarak karşılanmadı — T122'nin kalıcı dersi gereği onaylanan sapma KAYNAK dokümana geçti), **üç bloke edici bulgu (B1 re-entry niteleyicisi · B2 teslimat penceresi açlığı · B3 SYSTEM dispute'unda alıcı bildirimi) T127'ye kabul kriteri olarak** eklendi, ve **B5 (kapı kapalıyken auto-checker'ın alıcının eskalasyon yolunu kapatması) T130'a** launch öncesi kapatılmak üzere kabul kriteri olarak devredildi. **KALICI DERS:** bir kapı, kendi bıraktığı KALICI DURUMUN sonraki turda nasıl okunacağını ve o durumun biriktiği KUYRUĞUN drene olup olmadığını da denetlemeli — üç bulgunun üçünü de kabul kriteri listesi değil, "bu satır bir daha buraya geldiğinde ne olur?" sorusu yakaladı) · 2026-08-14 (**T124 doğrulaması** — iki bulgu proje sahibi onayıyla plana işlendi: **T127'nin başlığı düzeltildi** (`TimeoutExecutor'a ...` → `DeadlineScannerJob'a ...`; teslimat fazı 05 §4.4 uyarınca scanner-driven ve `TimeoutExecutor` `Status != SELLER_CONFIRMED` ise no-op eder — yanlış başlık T124 kapısının hiç kalkmamasına yol açabilirdi) ve **T127'ye freeze/resume faz kayması ön koşulu** kabul kriteri olarak eklendi (pre-existing T50 yolu `DeliveryDeadline`'a ödeme fazının artığını yazabiliyor; kapı kalkmadan kapatılmalı)) · 2026-08-14 (T124 — üç yapım kararı (kapı şekli, fallback sabiti, doküman yansıması) §P3 T124'e yazıldı; **T127'ye kapı kaldırma kabul kriteri eklendi** — T124'ün scanner kapısı kalkmazsa teslimat timeout'u hiç ateşlenmez. AC1'in T117'de zaten karşılandığı kanıtıyla kayda geçti.) · 2026-08-13 (T123 — adlandırma kararı (seçenek **a**) ve T123 yapımında bulunan **plan boşluğu** (`SellerConfirmDeadline`'ı yazan kod yoktu) §P3'e kabul kriteri olarak yazıldı; T124'ün SystemSetting'i adıyla sabitlendi. T122'nin kalıcı dersi uygulandı: onaylanmış kapsam değişikliği, kabul kriterlerinin KAYNAK dokümanına yazılmadıkça gerçekleşmemiştir.) · 2026-08-10 (T119 doğrulaması: T133a kapsamı 03 + 07 → **03 + 04 + 07** genişletildi. Önceki: T119 denetimi — T123/T124'e timeout SystemSetting adlandırma kararı, T129'a `REFUNDED` itibar kararı kabul kriteri olarak eklendi)

---

## 1. Amaç ve Kapsam

Bu doküman, Skinora escrow platformunun MVP implementasyonunu sıralı, bağımlılıklı ve test beklentili task'lara böler. Her task'ın ne olduğunu, neye bağımlı olduğunu, hangi dokümanlarla yapılacağını ve nasıl kabul edileceğini tanımlar.

**Kapsam:** 10_MVP_SCOPE §2'de "dahil" olarak tanımlanan tüm özellikler (MVP-IN-001 – MVP-IN-069). "Hariç" özellikler §9 Post-MVP Parkı'nda kayıt altındadır.

**Denetim mekanizması:**
- **Traceability Matrix** (§7): Her doküman çıktısı en az bir task'a eşlenmiştir. Eşlenmeyen çıktı = eksik task.
- **Boşluk kontrolü**: Çift yönlü — ileriye (çıktı→task) ve geriye (task→çıktı).
- **MVP filtresi**: 10_MVP_SCOPE "hariç" öğeler task listesinden çıkarılmış, §9'da kayıt altındadır.

---

## 2. Kaynak Envanteri Özeti

Aşağıdaki dokümanlardan implementasyon öğeleri çıkarılmıştır:

| Doküman | Öğe Sayısı | Kapsam |
|---|---|---|
| 02 Product Requirements | 23 section | İş kuralları, kısıtlamalar, edge case'ler |
| 03 User Flows | 336 öğe | Akış adımları, dallanmalar, validasyonlar, bildirimler |
| 04 UI Specs | 199 öğe | 25 ekran, 17 ortak bileşen, 8 modal, 20 form, 62 state varyantı, 18 validasyon |
| 05 Technical Architecture | 113 öğe | Servisler, altyapı, middleware, job'lar, güvenlik, monitoring |
| 06 Data Model | 211 öğe | 25 entity, 23 enum, 56 constraint, 35 index, 3 seed data |
| 07 API Design | 299 öğe | 67 endpoint, 103 DTO, 40 validasyon, 16 SignalR event, 58 hata tanımı |
| 08 Integration Spec | 157 öğe | Entegrasyonlar, API çağrıları, webhook'lar, hata yönetimi |
| 09 Coding Guidelines | 66 öğe | Proje iskeleti, middleware, pattern, EF Core config, CI/CD, test altyapısı |
| 10 MVP Scope | 87 öğe | 69 dahil, 18 hariç |

> Detaylı envanter listeleri bu dokümanın hazırlanmasında kullanılmış olup doküman içine dahil edilmemiştir. Traceability Matrix (§7) her task'ın hangi kaynak öğeleri kapsadığını gösterir.

---

## 3. Faz Tanımları

Tüm task'lar 7 faza ayrılmıştır. Her faz bir önceki faz tamamlanmadan başlamaz (§3.1'de belirtilen kontrollü paralellik istisnaları hariç).

| Faz | Ad | Kapsam | Task Aralığı |
|---|---|---|---|
| F0 | Proje İskeleti | Solution yapısı, Docker, middleware, logging, CI/CD, test altyapısı, shared kernel | T01–T16 |
| F1 | Veri Katmanı | Entity'ler, enum'lar, constraint'ler, index'ler, migration, seed data | T17–T28 |
| F2 | Çekirdek Servisler | Auth, kullanıcı yönetimi, bildirim altyapısı, admin altyapısı, audit log | T29–T43 |
| F3 | İş Mantığı | Escrow akışı, state machine, timeout, komisyon, fraud, dispute, SignalR, retention | T44–T63b |
| F4 | Entegrasyonlar | Steam sidecar, blockchain sidecar, email, Telegram, Discord, sanctions, geo-block | T64–T83 |
| F5 | Kullanıcı Arayüzü | Tüm ekranlar, bileşenler, formlar, state varyantları, responsive, i18n | T84–T106 |
| F6 | Uçtan Uca Doğrulama | E2E testler, senaryo testleri, regresyon | T107–T114 |
| F7 | P2P Geçişi | Item custody'nin kaldırılması, teslimat doğrulama, bot katmanının emekliye ayrılması | T115–T139 |

> **F7 neden var:** MVP (F0–F6) custodial bot escrow modeliyle tamamlandı. Steam Trade Protection (16.07.2025) ve trade cooldown reworku (02.2026) sonrası item bir envantere trade ile girdiğinde 7 gün transfer edilemiyor; bot item'ı emanete aldığı anda alıcıya gönderemez. Çift-trade modeli bu kural altında çalışamadığı için item custody kaldırılıp P2P modeline geçilmektedir (02 §2.1). Para escrow'u değişmemektedir.

### 3.1 Faz Bağımlılık Diyagramı

```
F0 → F1 → F2 → F3 → F4 → F5 → F6
                 ↘         ↗
                  F4 (kısmen F2 ile paralel başlayabilir — sidecar iskeletleri F0'da kurulur)
```

> **Not:** F4'ün sidecar iskeletleri F0'da kurulur (T14, T15). F4'ün iş mantığı task'ları F3'e bağımlıdır. F5 frontend task'ları F2+F3+F4 API'lerine bağımlıdır.

---

## 4. Hata Sınıflandırması ve Çözüm Akışı

### 4.1 Hata Seviyeleri

| Seviye | Tanım | Örnek |
|---|---|---|
| S1 — Sapma | Task tamamlandı ama dokümanla uyumsuz | Endpoint path dokümanla eşleşmiyor, entity field eksik, validasyon kuralı atlanmış |
| S2 — Kırılma | Bir task başka bir task'ın çıktısını bozuyor | Migration değişikliği mevcut servisi kırıyor, API sözleşmesi değişmiş ama consumer güncellenmemiş |
| S3 — Eksik | Traceability'de eşlenmiş bir öğe implement edilmemiş | Bir iş kuralı hiçbir yerde uygulanmamış, bir endpoint tanımlı ama kodu yok |

### 4.2 Tespit Mekanizması

| Seviye | Tespit Yöntemi | Zamanlama |
|---|---|---|
| Tümü | Task kabul kriterleri + doğrulama kontrol listesi | Her task tamamlandığında |
| S1, S3 | Traceability matrix "implemented" kolonu kontrolü | Her faz sonunda (gate check) |
| S2 | Regresyon testi (önceki fazların testleri tekrar çalıştırılır) | Her faz sonunda (gate check) |
| S3 | Boşluk taraması (eşlenip de implement edilmeyen öğeler) | Her faz sonunda (gate check) |

> **Not:** Doğrulama kontrol listesi, kodu yazan agent'tan farklı bir context'te çalıştırılır. Detaylı kurallar, VAL maddeleri, kanıt standardı ve süreç tanımı `12_VALIDATION_PROTOCOL.md`'de tanımlıdır.

### 4.3 Çözüm Akışı

```
Hata tespit edildi
  → Seviye belirlenir (S1/S2/S3)
  → Etki analizi: başka hangi task'lar etkileniyor?
  → Düzeltme task'ı oluşturulur (mevcut faza eklenir)
  → Düzeltme task'ı tamamlanır
  → Etkilenen task'ların doğrulama kontrol listeleri tekrar çalıştırılır
  → Gate check tekrar değerlendirilir
```

**Kritik kural:** Düzeltme task'ı bir sonraki faza ertelenmez — hatanın oluştuğu fazda çözülür.

### 4.4 Tıkanma Stratejisi

Bir task ilerleyemiyorsa:
1. Task daha küçük alt task'lara bölünür
2. Alt task'ların bağımlılıkları güncellenir
3. Faz kapısı (gate check) yeniden değerlendirilir
4. Tıkanmanın sebebi (eksik bilgi, teknik kısıt, dış bağımlılık) kayıt altına alınır

---

## 5. Task Listesi

### Task Yapısı

Her task aşağıdaki bilgileri içerir:

```
Task TXX: [Task adı]
  Bağımlılık: [Önceden tamamlanmış olması gereken task'lar]
  Dokümanlar: [Agent'a verilecek dosyalar]
  Kabul kriterleri: [Ne olduğunda "tamam"]
  Test beklentisi: [Unit / Integration / Contract / Yok]
  Doğrulama kontrol listesi: [Cross-check'te neye bakılacak]
```

---

### F0 — Proje İskeleti (T01–T16)

```
Task T01: .NET Solution ve proje yapısı oluşturma
  Bağımlılık: Yok
  Dokümanlar: 09 §4.1, §4.2
  Kabul kriterleri:
    - Skinora.sln oluşturuldu
    - src/ altında tüm modül projeleri var: Transactions, Payments, Steam, Users, Auth, Notifications, Admin, Disputes, Fraud
    - Skinora.Shared ve Skinora.API projeleri var
    - tests/ altında her modül için test projesi var
    - Proje referans kuralları doğru (API → modüller + Shared; modüller → Shared; modüller arası referans yok)
    - dotnet build başarılı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.2'deki klasör yapısı birebir eşleşiyor mu?
    - [ ] Proje referans kuralları (§4.2.2) ihlal edilmiyor mu?
```

```
Task T02: Docker Compose ve ortam konfigürasyonu
  Bağımlılık: T01
  Dokümanlar: 05 §8.1, 09 §4.1
  Kabul kriterleri:
    - docker-compose.yml ve docker-compose.override.yml (dev) oluşturuldu
    - Servisler: backend (.NET), frontend (Next.js), steam-sidecar, blockchain-sidecar, sqlserver, redis, nginx
    - Her servis için Dockerfile var
    - .env.example dosyası tüm ortam değişkenlerini listeliyor
    - docker-compose up ile tüm servisler ayağa kalkıyor
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 05 §8.1'deki container listesi eksiksiz mi?
    - [ ] Health check tanımları var mı?
    - [ ] Secret'lar .env.example'da açıklanmış mı (değerler hariç)?
```

```
Task T03: Shared Kernel — base sınıflar, exception'lar, interface'ler
  Bağımlılık: T01
  Dokümanlar: 09 §4.2, §6.4, §8.3
  Kabul kriterleri:
    - BaseEntity (Id, CreatedAt, UpdatedAt), IAuditableEntity, ISoftDeletable (IsDeleted, DeletedAt) tanımlı
    - IDomainEvent interface (EventId GUID + OccurredAt) tanımlı
    - Exception hiyerarşisi: DomainException, BusinessRuleException, NotFoundException, IntegrationException
    - ApiResponse<T>, PagedResult<T> tanımlı
    - IUnitOfWork, IOutboxService interface tanımlı
    - Shared enum'lar: StablecoinType, NotificationType, TransactionStatus (ve diğer 06 §2'deki tüm enum'lar)
  Test beklentisi: Unit — enum değer kontrolleri
  Doğrulama kontrol listesi:
    - [ ] 06 §2'deki tüm enum'lar tanımlı mı?
    - [ ] IDomainEvent'te EventId ve OccurredAt zorunlu mu (09 §6.4)?
    - [ ] Shared/Events altında modüller arası event contract'lar var mı?
```

```
Task T04: EF Core global konfigürasyon
  Bağımlılık: T03
  Dokümanlar: 09 §7.1, §10.3, §10.4, §10.6
  Kabul kriterleri:
    - UtcDateTimeConverter oluşturuldu, ConfigureConventions'da tüm DateTime'lara uygulandı
    - Soft delete global query filter: HasQueryFilter(e => !e.IsDeleted) tüm ISoftDeletable entity'lerde
    - RowVersion property base'de tanımlı, IsRowVersion() EF config'de
    - Tüm FK'lerde DeleteBehavior.NoAction zorunlu
    - Nullable reference types aktif
  Test beklentisi: Integration — UTC converter doğru çalışıyor mu, soft delete filtresi aktif mi
  Doğrulama kontrol listesi:
    - [ ] 09 §7.1 UTC kuralı uygulanmış mı?
    - [ ] 09 §10.6 cascade kuralı uygulanmış mı?
    - [ ] Soft delete query filter'ı IgnoreQueryFilters() olmadan silinmiş kayıtları getirmiyor mu?
```

```
Task T05: Middleware pipeline
  Bağımlılık: T03
  Dokümanlar: 09 §8.1–§8.3, §18.4, 05 §6.3
  Kabul kriterleri:
    - ExceptionHandlingMiddleware: global exception → HTTP status mapping, error envelope, traceId, loglama
    - CorrelationIdMiddleware: X-Correlation-Id header üretme/okuma, tüm loglara taşıma
    - ApiResponseWrapperFilter: başarılı response'ları ApiResponse<T> ile sarmalama
    - CORS middleware (sadece kendi domain)
    - CSRF koruması (SameSite cookie + anti-forgery)
    - CSP header middleware
    - HTTPS zorlaması
    - Pipeline sıralaması doğru
  Test beklentisi: Integration — exception middleware doğru status dönüyor mu, correlation ID taşınıyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §2.4 hata envelope formatı eşleşiyor mu?
    - [ ] 05 §6.3'teki güvenlik middleware'leri eksiksiz mi?
    - [ ] 500 hataları Error, diğerleri Warning seviyesinde loglanıyor mu (09 §8.3)?
```

```
Task T06: Authentication altyapısı
  Bağımlılık: T03
  Dokümanlar: 05 §6.1, §6.2, 07 §2.3
  Kabul kriterleri:
    - JWT Bearer authentication konfigüre edildi (15dk access token)
    - Refresh token mekanizması tanımlı (HttpOnly + Secure + SameSite=Strict cookie)
    - Policy-based authorization tanımlı (kullanıcı, admin, permission bazlı)
    - [Authorize], [AllowAnonymous] attribute'ları kullanıma hazır
    - JWT signing key rotation desteği (grace period)
  Test beklentisi: Integration — geçerli/geçersiz JWT, expired token, policy kontrolü
  Doğrulama kontrol listesi:
    - [ ] 05 §6.1 JWT konfigürasyonu eşleşiyor mu?
    - [ ] Refresh token cookie flag'leri doğru mu (07 §2.3)?
    - [ ] Admin endpoint'leri permission kontrolü yapıyor mu?
```

```
Task T07: Rate limiting konfigürasyonu
  Bağımlılık: T05
  Dokümanlar: 07 §2.9, 05 §6.3
  Kabul kriterleri:
    - Redis-based rate limiting konfigüre edildi
    - Endpoint grupları: Auth 10/dk, GET 60/dk, POST/PUT/DELETE 20/dk, Steam inventory 5/dk, Admin okuma 120/dk, Admin yazma 30/dk, Public 30/dk
    - 429 response + Retry-After header
    - X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset header'ları
  Test beklentisi: Integration — rate limit aşıldığında 429 dönüyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §2.9'daki tüm endpoint grupları tanımlı mı?
    - [ ] Header'lar doğru formatda mı?
```

```
Task T08: Logging altyapısı
  Bağımlılık: T02
  Dokümanlar: 05 §9.1, 09 §18.1, §18.3, §18.5
  Kabul kriterleri:
    - Serilog → Loki sink konfigüre edildi (.NET)
    - Pino → Loki push konfigüre edildi (Node.js sidecar'lar)
    - Structured JSON format, zorunlu field'lar: timestamp, level, message, correlationId
    - Secret maskeleme: private key, API key, refresh token, cüzdan adresi loglardan maskeleniyor
    - Grafana'da log görüntüleme çalışıyor
  Test beklentisi: Yok (altyapı doğrulaması — docker-compose up ile test)
  Doğrulama kontrol listesi:
    - [ ] 09 §18.5 maskeleme listesi eksiksiz mi?
    - [ ] CorrelationId tüm log'larda var mı?
```

```
Task T09: Hangfire setup ve background job altyapısı
  Bağımlılık: T02, T04
  Dokümanlar: 05 §2.2, 09 §13.1, §13.3–§13.7
  Kabul kriterleri:
    - Hangfire SQL Server storage konfigüre edildi
    - UTC timezone ayarı
    - AutomaticRetry(Attempts = 3) varsayılan
    - Timeout scheduling pattern tanımlı (delayed job schedule/cancel)
    - Job handler state doğrulama pattern tanımlı (güncel state kontrol, koşul tutmuyorsa no-op)
    - Timeout freeze/resume pattern tanımlı
    - Hangfire dashboard erişilebilir
  Test beklentisi: Integration — job schedule/cancel çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 09 §13.3 timeout scheduling pattern uygulanmış mı?
    - [ ] 09 §13.6 freeze/resume pattern uygulanmış mı?
    - [ ] Hangfire dashboard admin auth arkasında mı?
```

```
Task T10: Outbox pattern altyapısı
  Bağımlılık: T04, T09
  Dokümanlar: 05 §5.1, 09 §9.3, §13.4
  Kabul kriterleri:
    - IOutboxService implementasyonu: entity + outbox event yazma aynı DB transaction'da
    - Outbox Dispatcher: Hangfire self-rescheduling delayed job, saniye bazlı polling, distributed lock
    - Consumer idempotency: ProcessedEvent tablosu, EventId bazlı duplikasyon kontrolü
    - Program.cs'de dispatcher başlangıç tetiklemesi
    - External idempotency: X-Idempotency-Key header gönderim/alma pattern'ı, ExternalIdempotencyRecord lease mekanizması (05 §5.1)
    - Dispatcher PENDING ve FAILED durumları birlikte işler, max retry sonrası admin alert tetiklenir
  Test beklentisi: Integration — outbox event yazılıyor ve dispatcher tarafından işleniyor mu, duplikasyon engeliyor mu, external idempotency lease çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 05 §5.1 outbox pattern kuralları uygulanmış mı?
    - [ ] Atomik commit garantisi var mı (entity + event aynı transaction)?
    - [ ] Dispatcher distributed lock kullanıyor mu?
    - [ ] External idempotency key gönderim/alma ve lease mekanizması çalışıyor mu?
    - [ ] Dispatcher PENDING + FAILED birlikte işliyor mu, max retry sonrası admin alert tetikleniyor mu?
```

```
Task T11: CI/CD pipeline
  Bağımlılık: T01, T02
  Dokümanlar: 05 §8.4, 09 §21.1–§21.4
  Kabul kriterleri:
    - GitHub Actions workflow: Lint → Build → Unit test → Integration test → Contract test → Migration dry-run
    - Branch protection: main'e doğrudan push yasağı, CI geçmeden merge yasağı
    - Branch stratejisi: main, develop, feature branches
    - Docker image build ve push (ghcr.io)
  Test beklentisi: Yok (CI/CD kendisi test altyapısı)
  Doğrulama kontrol listesi:
    - [ ] 09 §21.4'teki 6 adımlı sıralama doğru mu?
    - [ ] Branch protection kuralları aktif mi?
```

```
Task T11.1: CI close-out — tüm pipeline step'lerini canlı hale getir
  Bağımlılık: T11, T13, T14, T15 (frontend + sidecar'lar yerinde olmalı)
  Dokümanlar: 09 §21.4, T11 close-out notları
  Gerekçe: T20 validator chat'inde (2026-04-11) ortaya çıktı — T13 chore'dan (2026-04-09) itibaren main CI kesintisiz FAIL,
    toplam 5+ merge silently CI fail ile geçti. T11 discipline "merge için CI PASS zorunlu" kuralı fiilen unutulmuş.
    Lint step'i T20 PR'ında (A1 fix) düzeltildi ama arkasındaki Build → Frontend → Unit → Integration → Contract →
    Migration dry-run → Docker build chain'i T13'ten beri hiç çalışmamış. F1 veri katmanı için **migration dry-run
    kritik** — T17-T20 şemaları CI'da hiç test edilmedi.
  Kabul kriterleri:
    - Lint: ✓ PASS (T20 PR #11'de bd8d713 sonrası düzeltildi — sidecar tsc --noEmit)
    - Build: Frontend `@parcel/watcher` linux-x64-glibc lockfile/platform sorunu çözülür; backend + frontend Linux CI'da temiz build verir
    - Unit test: dotnet test `!~.Integration` filter GHA runner'da çalışır
    - Integration test: TestContainers MsSql GHA runner'da çalışır (F1 migration için kritik)
    - Contract test: sidecar ↔ backend JSON schema doğrulaması çalışır
    - Migration dry-run: EF Core migration CI'da çalışır — T17-T20 şemaları doğrulanır
    - Docker build: 4-component matrix (backend, frontend, sidecar-steam, sidecar-blockchain) temiz build verir
    - Main branch üzerinde en az 1 ardışık CI run tamamen ✓ PASS olmalı
    - F0 Gate Check yeniden değerlendirilir (önceki gate check CI kırıkken PASS verilmişti, 2026-04-10)
  Test beklentisi: Yok — CI altyapısının kendisi doğrulanıyor
  Doğrulama kontrol listesi:
    - [ ] 7 CI step (Lint, Build, Unit, Integration, Contract, Migration, Docker) sırasıyla ✓
    - [ ] Bir özellik branch'inde + main push'unda CI yeşil
    - [ ] T17-T20 migration script'leri CI migration dry-run'dan temiz geçiyor
    - [ ] BYPASS_LOG.md T11 manuel disiplin ihlal kayıtları (T14-T19 dönemi) retro-aktif not düşüldü
  Durum: F1 ilerleyişi için blocker — T21 başlamadan önce tamamlanmalı
```

```
Task T11.2: CI disiplin savunma katmanları
  Bağımlılık: T11.1
  Dokümanlar: T11.1 retrospektif dersleri, 09 §21.4, INSTRUCTIONS.md §3.1, §3.3
  Gerekçe: T11.1 close-out retrospektifi T13-T20 döneminde 5 üst üste disiplin
    ihlalini ortaya çıkardı — (1) main CI T13'ten beri ardışık FAIL sessizce
    devam etti, (2) T15+T16 task chat'leri bitti ama PR açılmadı, kodları
    F0 Gate Check PR #10'a "bundled" olarak geldi, (3) T17+T18+T19 ayrı PR
    yerine T20 branch'ine gömüldü, (4) T20 validator'ı CI kırmızıyken
    "lokal temiz, geç" rasyonelizasyonuyla PASS verdi, (5) "merge için CI
    PASS zorunlu" kuralı manuel disiplinde çözündü. Tek savunma katmanı
    yetmiyor — mekanik + şablon + skill kurallarıyla birden fazla ağ gerekli.
  Kabul kriterleri:
    - Startup check (task + validate skill'lerinde): `/task TXX` ve `/validate
      TXX` skill'lerinin Adım 0 olarak main CI son 3 run conclusion'ını
      kontrol etmesi; biri bile FAIL ise **hard stop** — kullanıcıya sebep
      sorulur, root cause çözülmeden task'a başlanmaz. Rasyonelizasyon
      (lokal temiz, ilgisiz vb.) yasak.
    - Pre-push CI guard (scripts/git-hooks/pre-push): push öncesi mevcut
      branch'in son CI run'ı FAIL ise push bloklanır. `gh` CLI yoksa uyarı
      verir ve geçer. Bypass `SKINORA_ALLOW_DIRECT_PUSH=1` + BYPASS_LOG
      otomatik kayıt (mevcut pattern genişletilir).
    - Validator "CI kırmızı = finding" kuralı: .claude/skills/validate.md
      Faz 1'e zorunlu madde — "Son 3 main CI run'ın conclusion'ı nedir?
      Biri bile failure ise S2 Kırılma finding olarak kayıtlanır, sessizce
      PASS verilemez. CI kırılması önceki bir task'ın sorumluluğundaysa
      BLOCKED (DEPENDENCY_MISMATCH)." INSTRUCTIONS.md §3.3'e de paralel
      madde eklenir.
    - Task chat bitiş kapısı: .claude/skills/task.md sonuna "Bitiş Kapısı"
      check listesi — branch push edildi mi, `gh pr create` çağrıldı mı,
      PR numarası TXX_REPORT.md'ye yazıldı mı, CI run başladı mı. Dört
      maddenin hepsi ✓ olmadan task "yapım bitti" sayılmaz; raporda
      "PR: Henüz oluşturulmadı" ifadesi görülürse otomatik BLOCKED.
    - BYPASS_LOG düzeltmesi: T11.1'de hatalı retro kaydedilen T14 satırı
      kaldırılır (T14 PR #8 ile düzgün merge olmuş), T15+T16 için doğru
      pattern adı eklenir ("PR açılmadı, F0 Gate Check PR #10'a bundled").
      T17-T19 satırı aynen kalır (T20 PR #11'e bundled zaten doğru).
    - Dokümantasyon: 09 §21.4'e notification/guard katmanı başlığı eklenir.
  Test beklentisi: Yok — skill + hook + doküman. Manuel smoke: bilerek
    kırık bir branch push denemesi hook tarafından bloklanmalı; validator
    çağrılırken main CI sahte kırmızı senaryosu hard stop üretmeli.
  Doğrulama kontrol listesi:
    - [ ] task.md + validate.md Adım 0 startup check mevcut, hard stop wording net
    - [ ] pre-push hook CI status kontrolü yapıyor, bypass path çalışıyor
    - [ ] validate.md Faz 1'de "CI kırmızı = finding" maddesi var
    - [ ] task.md sonunda bitiş kapısı check listesi var, BLOCKED trigger net
    - [ ] BYPASS_LOG.md T14 satırı kaldırıldı, T15+T16 "bundled PR" notu eklendi
    - [ ] INSTRUCTIONS.md §3.3'te validator rasyonelizasyon yasağı var
    - [ ] 09 §21.4 guard katmanı başlığı eklendi
  Durum: T21 öncesi zorunlu — T11.1 deliklerini kapatır, F1 ilerleyişi bu ağla başlar
```

```
Task T11.3: Test infra — shared MsSqlContainer fixture refactor
  Bağımlılık: T12 (test altyapısı)
  Dokümanlar: 09 §19.6 (test altyapısı), 12_VALIDATION_PROTOCOL
  Gerekçe: 2026-04-19 T26 validator zinciri üç ardışık post-merge main CI
    FAIL üretti — her seferinde farklı bir test sınıfı, farklı exit kodları
    (118, 255), hep DotNet.Testcontainers.ContainerNotRunningException.
    Root cause: IntegrationTestBase IAsyncLifetime olduğundan xUnit her
    test sınıfı için ayrı MsSqlContainer açıyor; collection- ve assembly-
    level parallelization default on → ubuntu-latest'in 7.75 GB RAM'i
    10+ SQL Server 2022 container'ı taşıyamıyor, Docker daemon OOM
    kill ediyor. Hot-fix (PR #34) xunit.runner.json ile paralelizasyonu
    kapattı; Integration step ~5 dk → ~22 dk. Her yeni entity task'ı
    (T25 +37 test, T26 +11 test, T27+ daha fazla) maliyeti artırıyor.
    Kalıcı çözüm: tek shared MsSqlContainer, her test class unique DB,
    paralelizasyon geri açılır.
  Kabul kriterleri:
    - Tek MsSqlContainer tüm integration testlere servis (ICollectionFixture
      pattern; xUnit 09 §19.6 referansı, standart yaklaşım)
    - Her test class kendi unique DB oluşturur (collision yok), test
      sonunda kendi DB'sini drop eder — isolation korunur
    - AppDbContext connection string her test için unique DB'yi gösterir
    - xunit.runner.json: parallelizeTestCollections=true, parallelizeAssembly=true
      (hot-fix'te false'du)
    - .github/workflows/ci.yml integration step: -m:1 kaldırılır
    - Integration step CI runtime ≤ 10 dk (hedef; baseline hot-fix'in
      ~22 dk'sından sonra)
    - Mevcut 48+ integration test regresyon PASS
    - Cross-test isolation smoke test: aynı tabloya farklı test
      sınıflarının yazması çakışma yaratmıyor (gerçek paralel execution)
  Test beklentisi: Integration — tüm mevcut testler PASS, yeni cross-test
    isolation smoke test eklenir
  Doğrulama kontrol listesi:
    - [ ] ICollectionFixture<SqlServerFixture> pattern uygulanmış
    - [ ] Unique DB per test class (TestContext.CurrentContext.Test veya
      GUID bazlı isim)
    - [ ] Paralel execution geri açık, test runtime ≤ 10 dk
    - [ ] Tüm integration test sınıfları yeni fixture kullanıyor
      (IntegrationTestBase güncellenmiş)
    - [ ] -m:1 kaldırıldı, xunit.runner.json paralel ayarları geri
    - [ ] Hot-fix gerekçesi (BYPASS_LOG ve rapor) closing note ile
      kapatıldı
  Durum: T27 öncesi zorunlu — mevcut hot-fix'in CI maliyeti T27+T28+F1
    Gate Check için kümülatif; ayrıca her yeni task daha fazla test
    ekler, hot-fix erode olabilir (serial dahi 30 dk üstüne çıkarsa
    başka timeout'lar tetiklenebilir).
```

```
Task T12: Test altyapısı
  Bağımlılık: T01, T04
  Dokümanlar: 09 §19.2, §19.6, §12.7
  Kabul kriterleri:
    - xUnit + Moq test projeleri her modül için kuruldu
    - IntegrationTestBase: TestContainers ile SQL Server container, EF Core migration, seed
    - Contract test altyapısı: sidecar ↔ backend sözleşme doğrulama (JSON schema)
    - Test naming convention: {MethodName}_{Scenario}_{ExpectedResult}
    - Test yapısı: Arrange-Act-Assert
  Test beklentisi: Yok (test altyapısının kendisi)
  Doğrulama kontrol listesi:
    - [ ] 09 §19.6 IntegrationTestBase yapısı doğru mu?
    - [ ] Her modül için Unit/ ve Integration/ klasörleri var mı?
```

```
Task T13: Next.js Frontend iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.3, §16.3, §16.4, §16.6
  Kabul kriterleri:
    - Next.js App Router projesi oluşturuldu
    - [locale] route grupları: auth, main, admin
    - Klasör yapısı: components/ui/, components/features/, lib/api/, lib/hooks/, lib/signalr/, types/, i18n/
    - API client (lib/api/client.ts): fetch wrapper, ApiResponse<T> unwrap, ApiError, Bearer token
    - State management: TanStack Query + Zustand
    - i18n: next-intl, 4 dil (EN, ZH, ES, TR), fallback EN
    - TypeScript enum'ları (C# karşılıkları): types/enums.ts
    - SignalR client setup: lib/signalr/connection.ts
    - ESLint + Prettier konfigüre
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.3 klasör yapısı eşleşiyor mu?
    - [ ] API client 07 §2.4 envelope formatını unwrap ediyor mu?
    - [ ] i18n 4 dil dosyası mevcut mu?
```

```
Task T14: Steam Sidecar Node.js iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.4.1, §17.1–§17.9, 08 §2.5
  Kabul kriterleri:
    - Node.js TypeScript projesi oluşturuldu
    - Klasör yapısı: bot/, trade/, api/, webhook/, health/, config/
    - Kütüphaneler: steam-tradeoffer-manager ^3.x, steamcommunity ^3.x, steam-totp ^2.x, steam-user ^5.x
    - Webhook callback gönderim modülü: HMAC-SHA256 imzalama, timestamp/nonce/signature header
    - Health check endpoint: /health
    - Error class hiyerarşisi: SidecarError, SteamApiError, BotSessionExpiredError
    - Rate limiting istek kuyruğu (Steam API'ye)
    - Graceful shutdown handler
    - Pino logger (Loki push, correlationId)
    - ESLint + Prettier
    - Dockerfile
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.4.1 klasör yapısı eşleşiyor mu?
    - [ ] 08 §2.5 kütüphane versiyonları doğru mu?
    - [ ] Webhook imzalama 05 §3.4 ile uyumlu mu?
```

```
Task T15: Blockchain Sidecar Node.js iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.4.2, §17.1–§17.9, 08 §3.1
  Kabul kriterleri:
    - Node.js TypeScript projesi oluşturuldu
    - Klasör yapısı: wallet/, monitor/, transfer/, api/, webhook/, health/, config/
    - TronWeb ^5.x kütüphanesi kuruldu
    - TronGrid API bağlantısı konfigüre (Mainnet + Testnet URL, API key)
    - Webhook callback gönderim modülü
    - Health check endpoint: /health
    - Error class hiyerarşisi: SidecarError, InsufficientGasError, TransactionFailedError
    - Pino logger + graceful shutdown + rate limiting queue
    - USDT/USDC kontrat adresleri config'de tanımlı
    - Dockerfile
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 08 §3.1 TronGrid endpoint'leri doğru mu?
    - [ ] 08 §3.3 kontrat adresleri doğru mu?
```

```
Task T16: Monitoring altyapısı
  Bağımlılık: T02, T08
  Dokümanlar: 05 §9.1–§9.5
  Kabul kriterleri:
    - Prometheus konfigüre (docker-compose'da)
    - .NET Prometheus client: metrics endpoint /metrics
    - prom-client (Node.js): metrics endpoint /metrics
    - Grafana dashboard konfigüre
    - Grafana Alerting: Telegram + Email (Critical/Warning/Info)
    - Uptime Kuma: HTTP/TCP external monitoring
    - Health check endpoint: /health (DB, Redis, Steam API, Tron node kontrolleri)
  Test beklentisi: Yok (altyapı doğrulaması)
  Doğrulama kontrol listesi:
    - [ ] 05 §9.2 metrik kaynakları tanımlı mı?
    - [ ] 05 §9.3 alert seviyeleri konfigüre mi?
    - [ ] 05 §9.5 health check bileşenleri eksiksiz mi?
```

---

### F1 — Veri Katmanı (T17–T28)

```
Task T17: Enum tanımları (C# + EF Core migration)
  Bağımlılık: T03
  Dokümanlar: 06 §2
  Kabul kriterleri:
    - 23 enum tanımlı: TransactionStatus (13), StablecoinType (2), BuyerIdentificationMethod (2), CancelledByType (4), BlockchainTransactionType (9), BlockchainTransactionStatus (4), TradeOfferDirection (3), TradeOfferStatus (6), DisputeType (3), DisputeStatus (3), FraudFlagType (4), ReviewStatus (3), NotificationType (20), NotificationChannel (3), PlatformSteamBotStatus (4), MonitoringStatus (5), OutboxMessageStatus (4), ActorType (3), AuditAction (12), TimeoutFreezeReason (4), FraudFlagScope (2), PayoutIssueStatus (5), DeliveryStatus (3)
    - EF Core'da string olarak saklanıyor (HasConversion)
    - Her enum değeri 06 §2 ile birebir eşleşiyor
  Test beklentisi: Unit — enum değer sayıları ve isimleri doğru mu
  Doğrulama kontrol listesi:
    - [ ] 06 §2'deki her enum tanımlı mı?
    - [ ] Her enum'ın değer sayısı dokümanla eşleşiyor mu?
```

```
Task T18: User, UserLoginLog, RefreshToken entity'leri
  Bağımlılık: T04, T17
  Dokümanlar: 06 §3.1–§3.3, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - User entity: tüm field'lar 06 §3.1'e göre (SteamId, DisplayName, AvatarUrl, DefaultPayoutAddress, DefaultRefundAddress, CompletedTransactionCount, SuccessfulTransactionRate, ReputationScore, CooldownExpiresAt, MobileAuthenticatorActive, Language, TosAcceptedAt, TosVersion, IsDeactivated, IsDeleted, vb.)
    - UserLoginLog entity: 06 §3.2'ye göre
    - RefreshToken entity: 06 §3.3'e göre (Token, ReplacedByTokenId self-ref)
    - Unique constraint'ler: User.SteamId, RefreshToken.Token
    - FK ilişkileri: UserLoginLog→User, RefreshToken→User, RefreshToken→RefreshToken (self)
    - Index'ler: User.DefaultPayoutAddress, User.DefaultRefundAddress, UserLoginLog.UserId/IpAddress/DeviceFingerprint, RefreshToken.UserId
    - Soft delete: User (kalıcı), RefreshToken (kalıcı)
  Test beklentisi: Integration — CRUD + soft delete + unique constraint violation
  Doğrulama kontrol listesi:
    - [ ] 06 §3.1 tüm User field'ları var mı?
    - [ ] 06 §5.1 unique constraint'ler tanımlı mı?
    - [ ] 06 §5.2 index'ler tanımlı mı?
```

```
Task T19: Transaction, TransactionHistory entity'leri
  Bağımlılık: T04, T17, T18
  Dokümanlar: 06 §3.5–§3.6, §4.1, §5.1, §5.2, §8.3, §8.7
  Kabul kriterleri:
    - Transaction entity: tüm field'lar 06 §3.5'e göre (Status, SellerId, BuyerId, item bilgileri, fiyat/komisyon, timeout süreleri, iptal bilgileri, hold bilgileri, freeze bilgileri, RowVersion, vb.)
    - TransactionHistory entity: 06 §3.6'ya göre (append-only)
    - Check constraint'ler: iptal state'lerinde CancelledBy/CancelReason/CancelledAt NOT NULL, hold constraint'leri, freeze constraint'leri, BuyerIdentificationMethod constraint
    - FK'ler: Transaction→User (seller, buyer, holdAdmin), TransactionHistory→Transaction, TransactionHistory→User (actor)
    - Unique: Transaction.InviteToken (filtered, WHERE NOT NULL)
    - Index'ler: Status (filtered), SellerId, BuyerId, CreatedAt, EscrowBotId; TransactionHistory.TransactionId
    - Optimistic concurrency: RowVersion
    - Computed field'lar: CommissionAmount, TotalAmount (06 §8.3)
  Test beklentisi: Integration — CRUD + check constraint + RowVersion concurrency
  Doğrulama kontrol listesi:
    - [ ] 06 §3.5 tüm field'lar ve check constraint'ler var mı?
    - [ ] RowVersion (§8.7) optimistic concurrency çalışıyor mu?
    - [ ] Komisyon hesaplama formülü doğru mu (§8.3)?
```

```
Task T20: PaymentAddress, BlockchainTransaction entity'leri
  Bağımlılık: T04, T17, T19
  Dokümanlar: 06 §3.7–§3.8, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - PaymentAddress entity: 06 §3.7'ye göre (Address, HdWalletIndex, MonitoringStatus, vb.)
    - BlockchainTransaction entity: 06 §3.8'e göre (TxHash, Type, Status, Amount, ConfirmationCount, ActualTokenAddress, vb.)
    - Unique: PaymentAddress.TransactionId, PaymentAddress.Address, PaymentAddress.HdWalletIndex; BlockchainTransaction.TxHash (filtered)
    - Check constraint'ler: BlockchainTransaction type-specific kurallar (BUYER_PAYMENT, WRONG_TOKEN_*, SPAM_*, giden transferler, status-specific)
    - FK'ler: PaymentAddress→Transaction, BlockchainTransaction→Transaction, BlockchainTransaction→PaymentAddress
    - Index'ler: BlockchainTransaction.TransactionId, Status (filtered PENDING), FromAddress; PaymentAddress.MonitoringStatus (filtered)
  Test beklentisi: Integration — CRUD + check constraint + unique constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.8 check constraint'leri eksiksiz mi?
    - [ ] BlockchainTransactionType + status kombinasyonları doğru mu?
```

```
Task T21: TradeOffer, PlatformSteamBot entity'leri
  Bağımlılık: T04, T17, T19
  Dokümanlar: 06 §3.9–§3.10, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - TradeOffer entity: 06 §3.9'a göre (SteamTradeOfferId, Direction, Status, SentAt, RespondedAt, vb.)
    - PlatformSteamBot entity: 06 §3.10'a göre (SteamId, BotName, Status, ActiveEscrowCount, DailyTradeOfferCount, vb.)
    - Unique: TradeOffer.SteamTradeOfferId (filtered), PlatformSteamBot.SteamId
    - Check constraint'ler: TradeOffer status-specific (SENT→SentAt NOT NULL, ACCEPTED→RespondedAt NOT NULL, vb.)
    - FK'ler: TradeOffer→Transaction, TradeOffer→PlatformSteamBot; Transaction→PlatformSteamBot
    - Index'ler: TradeOffer.TransactionId, PlatformSteamBotId
    - Soft delete: PlatformSteamBot (kalıcı)
    - Denormalized: ActiveEscrowCount, DailyTradeOfferCount
  Test beklentisi: Integration — CRUD + check constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.9 check constraint'leri doğru mu?
    - [ ] Bot denormalized field'ları güncellenebilir mi?
```

```
Task T22: Dispute, FraudFlag entity'leri
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.11–§3.12, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - Dispute entity: 06 §3.11'e göre (Type, Status, AutoCheckResult, EscalationDetail, AdminNote, vb.)
    - FraudFlag entity: 06 §3.12'ye göre (Type, Scope, ReviewStatus, Evidence, vb.)
    - Unique: Dispute (TransactionId + Type) unfiltered
    - Check constraint'ler: Dispute CLOSED→ResolvedAt NOT NULL; FraudFlag scope-specific + review-specific
    - FK'ler: Dispute→Transaction, User(opener), User(admin); FraudFlag→Transaction(opt), User(opt), User(reviewer)
    - Index'ler: Dispute.TransactionId, Status (filtered); FraudFlag.TransactionId, UserId, Status (filtered)
  Test beklentisi: Integration — CRUD + unique constraint (aynı türde tekrar dispute açılamaz)
  Doğrulama kontrol listesi:
    - [ ] 06 §3.12 FraudFlag scope constraint'leri doğru mu?
```

```
Task T23: Notification, NotificationDelivery, UserNotificationPreference entity'leri
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.4, §3.13–§3.13a, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - Notification entity: 06 §3.13'e göre (Type, UserId, TransactionId, Message, IsRead, vb.)
    - NotificationDelivery entity: 06 §3.13a'ya göre (Channel, DeliveryStatus, TargetExternalId, LastError, RetryCount, vb.)
    - UserNotificationPreference entity: 06 §3.4'e göre (Channel, IsEnabled, ExternalId, vb.)
    - Unique: NotificationDelivery (NotificationId + Channel); UserNotificationPreference (UserId + Channel, filtered); (Channel + ExternalId, filtered)
    - Check constraint'ler: DeliveryStatus-specific
    - FK'ler: Notification→User, Transaction(opt); NotificationDelivery→Notification; UserNotificationPreference→User
    - Index'ler: Notification (UserId + IsRead) composite, CreatedAt
    - Soft delete: UserNotificationPreference (kalıcı)
  Test beklentisi: Integration — CRUD + unique constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.13a check constraint'leri doğru mu?
    - [ ] 06 §5.1 UserNotificationPreference unique filtered index'leri doğru mu?
```

```
Task T24: Admin entity'leri (AdminRole, AdminRolePermission, AdminUserRole)
  Bağımlılık: T04, T17, T18
  Dokümanlar: 06 §3.14–§3.16, §4.1, §5.1
  Kabul kriterleri:
    - AdminRole entity: 06 §3.14'e göre (Name, Description)
    - AdminRolePermission entity: 06 §3.15'e göre (Permission string)
    - AdminUserRole entity: 06 §3.16'ya göre (AssignedByAdminId)
    - Unique: AdminRole.Name; AdminRolePermission (AdminRoleId + Permission, filtered); AdminUserRole (UserId + AdminRoleId, filtered)
    - FK'ler: AdminRolePermission→AdminRole; AdminUserRole→User, AdminRole, User(assigner)
    - Soft delete: AdminRole, AdminRolePermission, AdminUserRole (kalıcı)
  Test beklentisi: Integration — CRUD + unique
  Doğrulama kontrol listesi:
    - [ ] 06 §3.14–§3.16 field'ları eksiksiz mi?
```

```
Task T25: Altyapı entity'leri (SystemSetting, OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord, AuditLog, ColdWalletTransfer, SystemHeartbeat, SellerPayoutIssue)
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.17–§3.23, §3.8a, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - SystemSetting: Key, Value, DataType, Category, Description, IsConfigured, vb. Check: DataType IN ('int','decimal','bool','string')
    - OutboxMessage: EventType, Payload, Status, ProcessedAt, ErrorMessage, vb. Check: status-specific
    - ProcessedEvent: EventId, ConsumerName. Unique: (EventId + ConsumerName)
    - ExternalIdempotencyRecord: ServiceName, IdempotencyKey, Status, LeaseExpiresAt, ResultPayload, vb. Check: status-specific + Status IN (...)
    - AuditLog: ActorType, ActorId, Action, EntityType, EntityId, Detail, vb. FK: ActorId→User, UserId→User(opt). Append-only (immutable)
    - ColdWalletTransfer: TxHash (unique), Amount, vb. Append-only
    - SystemHeartbeat: Id CHECK (Id = 1) singleton, LastHeartbeat
    - SellerPayoutIssue: 06 §3.8a. Check: status-specific. Unique: TransactionId (filtered WHERE != RESOLVED)
    - Tüm index'ler tanımlı (§5.2)
  Test beklentisi: Integration — CRUD + constraint'ler + AuditLog immutability (update/delete engeli)
  Doğrulama kontrol listesi:
    - [ ] 06 §3.17–§3.23 ve §3.8a tüm entity'ler ve constraint'ler var mı?
    - [ ] AuditLog'a UPDATE/DELETE yapılamıyor mu?
    - [ ] SystemHeartbeat singleton garantisi var mı?
```

```
Task T26: Seed data
  Bağımlılık: T18, T25
  Dokümanlar: 06 §8.9
  Kabul kriterleri:
    - SYSTEM service account: User tablosunda sabit GUID (00000000-0000-0000-0000-000000000001), SteamId="00000000000000001", IsDeactivated=true
    - SystemHeartbeat: Id=1 ile tek satır
    - SystemSetting: 28 platform parametresi seed edildi (accept_timeout_minutes, commission_rate, hot_wallet_limit, vb.), varsayılanı olanlar IsConfigured=true, olmayanlar false
    - Env var bootstrap: SKINORA_SETTING_{KEY_UPPER} formatında env var ile SystemSetting hydration
    - Startup fail-fast: IsConfigured=false olan zorunlu parametreler kontrol edildi
  Test beklentisi: Integration — seed data doğru yükleniyor mu, fail-fast çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 06 §8.9'daki tüm seed kayıtları var mı?
    - [ ] 28 SystemSetting parametresi eksiksiz mi?
    - [ ] Env var bootstrap doğru çalışıyor mu?
```

```
Task T27: Performans index'leri ve filtered index'ler
  Bağımlılık: T18–T25
  Dokümanlar: 06 §5.2
  Kabul kriterleri:
    - 06 §5.2'deki tüm index'ler tanımlı (35 index)
    - Filtered index'ler HasFilter() ile SQL Server'a özgü tanımlanmış
    - Composite index'ler doğru sırada
  Test beklentisi: Yok (migration ile doğrulanır)
  Doğrulama kontrol listesi:
    - [ ] 06 §5.2'deki her index migration'da var mı?
    - [ ] Filtered index koşulları doğru mu?
```

```
Task T28: Initial migration ve migration testi
  Bağımlılık: T17–T27
  Dokümanlar: 05 §2.4, 09 §21.4
  Kabul kriterleri:
    - dotnet ef migrations add InitialCreate ile migration oluşturuldu
    - Migration boş bir SQL Server'a uygulandığında hatasız çalışıyor
    - Seed data migration sonrası doğru yükleniyor
    - CI pipeline'da migration dry-run adımı var
  Test beklentisi: Integration — temiz DB'ye migration + seed doğrulaması
  Doğrulama kontrol listesi:
    - [ ] Tüm entity'ler, constraint'ler, index'ler migration'da var mı?
    - [ ] Migration idempotent mi (tekrar çalıştırılınca hata vermiyor mu)?
```

---

### F2 — Çekirdek Servisler (T29–T43)

```
Task T29: Steam OpenID authentication (login + callback + token üretimi)
  Bağımlılık: T06, T18
  Dokümanlar: 07 §4.2–§4.3, 08 §2.1, 03 §2.1
  Kabul kriterleri:
    - GET /auth/steam → Steam OpenID sayfasına redirect
    - GET /auth/steam/callback → assertion doğrulama, kullanıcı oluşturma/güncelleme, JWT + refresh token üretimi
    - Güvenlik: assertion backend'de doğrulanır (claimed_id güvenilmez), return URL kontrolü, nonce replay koruması, HTTPS zorunlu
    - İlk kez giriş: ToS gösterilmeli (tosAccepted kontrolü)
    - returnUrl sadece relative path kabul eder
    - GetPlayerSummaries çağrısı ile profil bilgileri çekilir
    - Geo-block kontrolü (IP bazlı, yasaklı bölge → engel)
    - Sanctions eşleşmesi kontrolü (profil adresi)
    - Hesap askıya alınmış mı kontrolü (kısıtlı oturum)
  Test beklentisi: Integration — geçerli/geçersiz callback, yeni kullanıcı oluşturma, mevcut kullanıcı güncelleme
  Doğrulama kontrol listesi:
    - [ ] 08 §2.1 güvenlik kuralları uygulanmış mı?
    - [ ] 03 §2.1 akış adımları karşılanmış mı?
    - [ ] 07 §4.2–§4.3 endpoint sözleşmesi eşleşiyor mu?
```

```
Task T30: ToS kabul, yaş gate, geo-block
  Bağımlılık: T29, T26
  Dokümanlar: 07 §4.4, 02 §21.1, 03 §2.1, §11a
  Kabul kriterleri:
    - POST /auth/tos/accept → ToS versiyonu kaydedilir
    - Yaş gate: 18+ beyanı + Steam hesap yaşı kontrolü, başarısız → erişim engeli
    - Geo-block: IP bazlı coğrafi engelleme, yasaklı ülke listesi admin tarafından yönetilebilir
    - VPN/proxy tespiti destekleyici sinyal olarak (tek başına engelleme sebebi değil)
  Test beklentisi: Integration — ToS kabul, geo-block engeli, yaş gate engeli
  Doğrulama kontrol listesi:
    - [ ] 07 §4.4 ToS endpoint sözleşmesi doğru mu?
    - [ ] 02 §21.1 erişim kuralları eksiksiz mi?
```

```
Task T31: Steam re-verify ve authenticator kontrolü
  Bağımlılık: T29
  Dokümanlar: 07 §4.6–§4.8, 08 §2.2
  Kabul kriterleri:
    - POST /auth/steam/re-verify → Steam re-auth başlatma (purpose + returnUrl)
    - GET /auth/steam/re-verify/callback → reAuthToken üretimi (kısa ömürlü)
    - POST /auth/check-authenticator → GetTradeHoldDurations ile MA kontrolü
    - Referrer-Policy: same-origin (reAuthToken sızma koruması)
    - X-ReAuth-Token header doğrulaması (cüzdan değişikliğinde kullanılacak)
  Test beklentisi: Integration — re-verify akışı, authenticator kontrolü
  Doğrulama kontrol listesi:
    - [ ] 07 §4.6–§4.8 endpoint sözleşmeleri doğru mu?
    - [ ] 08 §2.2 GetTradeHoldDurations çağrısı doğru mu?
```

```
Task T32: Refresh token yönetimi
  Bağımlılık: T29
  Dokümanlar: 07 §4.9–§4.10, 05 §6.1
  Kabul kriterleri:
    - POST /auth/refresh → access token yenileme (refresh cookie'den)
    - POST /auth/logout → refresh token revoke, cookie temizleme
    - GET /auth/me → mevcut oturum bilgisi
    - Token rotation: kullanılan refresh token invalidate, yeni üretilir
    - DB source of truth + Redis cache
    - Expired/revoked token cleanup (periyodik)
  Test beklentisi: Integration — refresh, logout, expired token, rotation
  Doğrulama kontrol listesi:
    - [ ] 07 §4.9–§4.10 sözleşmeleri doğru mu?
    - [ ] Token rotation çalışıyor mu?
    - [ ] Kullanılmış refresh token ile tekrar istek → 401?
```

```
Task T33: User profil servisi
  Bağımlılık: T29, T18
  Dokümanlar: 07 §5.1–§5.2, §5.5
  Kabul kriterleri:
    - GET /users/me → kendi profil (wallet adresleri, skor, istatistikler)
    - GET /users/me/stats → dashboard hızlı istatistikler
    - GET /users/:steamId → public profil (sınırlı alanlar)
  Test beklentisi: Integration — kendi profil, public profil, user not found
  Doğrulama kontrol listesi:
    - [ ] 07 §5.1–§5.5 response DTO'ları doğru mu?
```

```
Task T34: Cüzdan adresi yönetimi
  Bağımlılık: T31, T33
  Dokümanlar: 07 §5.3–§5.4, 02 §12, 03 §9
  Kabul kriterleri:
    - PUT /users/me/wallet/seller → satıcı ödeme adresi kaydet/güncelle
    - PUT /users/me/wallet/refund → alıcı iade adresi kaydet/güncelle
    - Merkezi doğrulama pipeline: TRC-20 format + sanctions screening
    - Mevcut adres varsa X-ReAuth-Token zorunlu (Steam re-verify)
    - Cooldown: satıcı → yeni işlem başlatma engeli; alıcı → yeni işlem başlatma + kabul engeli
    - Aktif işlemler eski adresle tamamlanır (snapshot prensibi)
    - Adres onay adımı
  Test beklentisi: Integration — adres kayıt, güncelleme, format validation, sanctions block, cooldown
  Doğrulama kontrol listesi:
    - [ ] 02 §12 tüm kurallar uygulanmış mı?
    - [ ] Sanctions eşleşmesinde hesap flag'leniyor mu?
    - [ ] Cooldown mekanizması çalışıyor mu?
```

```
Task T35: Hesap ayarları (dil, bildirim tercihleri, Telegram/Discord bağlama)
  Bağımlılık: T33, T23
  Dokümanlar: 07 §5.6–§5.15, §5.16a
  Kabul kriterleri:
    - GET /users/me/settings → hesap ayarları
    - PUT /users/me/settings/language → dil değiştirme (en, zh, es, tr)
    - PUT /users/me/settings/notifications → bildirim tercihleri
    - POST/DELETE telegram/discord bağlantı endpoint'leri
    - Email doğrulama akışı (send-verification + verify)
    - PUT /users/me/settings/steam/trade-url → trade URL kayıt + MA doğrulama
  Test beklentisi: Integration — dil değiştirme, bildirim tercih, trade URL kayıt
  Doğrulama kontrol listesi:
    - [ ] 07 §5.6–§5.16a tüm endpoint'ler var mı?
    - [ ] Trade URL kaydında MA kontrolü yapılıyor mu (08 §2.2)?
```

```
Task T36: Hesap deaktif ve silme
  Bağımlılık: T33
  Dokümanlar: 07 §5.17, 02 §19, 06 §6.2
  Kabul kriterleri:
    - POST /users/me/deactivate → hesap deaktif (aktif işlem kontrolü)
    - DELETE /users/me → hesap silme (confirmation="SİL", aktif işlem kontrolü)
    - Silme: soft delete + PII temizleme (SteamId→ANON_{GUID}, DisplayName→"Deleted User", adresler temiz)
    - UserNotificationPreference soft delete + ExternalId temiz
    - RefreshToken revoke + soft delete
    - NotificationDelivery.TargetExternalId masked format
    - İşlem geçmişi ve audit log anonim olarak saklanır
  Test beklentisi: Integration — deaktif, silme, PII temizleme, aktif işlem engeli
  Doğrulama kontrol listesi:
    - [ ] 06 §6.2 anonimleştirme formatı birebir eşleşiyor mu?
    - [ ] Silinen kullanıcının audit log'ları korunuyor mu?
```

```
Task T37: Bildirim altyapı servisi
  Bağımlılık: T10, T23
  Dokümanlar: 05 §7.2–§7.5, 02 §18
  Kabul kriterleri:
    - Domain event → Notification entity dönüşümü
    - Kanal dispatching: kullanıcı tercihlerine göre hangi kanallara gönderileceği belirlenir
    - Bildirim retry stratejisi: exponential backoff, 3 deneme, başarısızlıkta admin alert
    - NotificationDelivery kaydı oluşturma (kanal bazlı teslimat takibi)
    - Lokalizasyon altyapısı: .resx resource dosya yapısı, 4 dil desteği, kanal bazlı format (placeholder metinlerle — final mesaj içerikleri Post-MVP)
  Test beklentisi: Integration — event → notification oluşturma, kanal dispatching, retry
  Doğrulama kontrol listesi:
    - [ ] 02 §18.2 tüm bildirim tetikleyicileri tanımlı mı?
    - [ ] 05 §7.5 retry stratejisi uygulanmış mı?
```

```
Task T38: Platform içi bildirim kanalı
  Bağımlılık: T37
  Dokümanlar: 05 §7.2, 07 §8.1–§8.4
  Kabul kriterleri:
    - GET /notifications → bildirim listesi (paginated)
    - GET /notifications/unread-count → okunmamış sayı
    - POST /notifications/mark-all-read → tümünü okundu
    - PUT /notifications/:id/read → tek bildirim okundu
    - Notification tablosuna yazma
  Test beklentisi: Integration — bildirim listeleme, okundu işaretleme, sayaç
  Doğrulama kontrol listesi:
    - [ ] 07 §8.1–§8.4 endpoint sözleşmeleri doğru mu?
```

```
Task T39: Admin rol ve yetki yönetimi
  Bağımlılık: T24, T06
  Dokümanlar: 07 §9.11–§9.18, 02 §16
  Kabul kriterleri:
    - GET /admin/roles → rol listesi + mevcut yetkiler
    - POST /admin/roles → yeni rol oluşturma
    - PUT /admin/roles/:id → rol güncelleme
    - DELETE /admin/roles/:id → rol silme (atanmış kullanıcı varsa engel)
    - GET /admin/users → admin kullanıcı listesi
    - GET /admin/users/:steamId → kullanıcı detay
    - PUT /admin/users/:id/role → rol atama
    - 11 yetki tanımı (MANAGE_FLAGS, CANCEL_TRANSACTIONS, EMERGENCY_HOLD, vb.)
  Test beklentisi: Integration — rol CRUD, yetki atama, rol silme engeli
  Doğrulama kontrol listesi:
    - [ ] 07 §9.11–§9.18 endpoint'leri eksiksiz mi?
    - [ ] Atanmış kullanıcılı rol silinemez mi?
```

```
Task T40: Admin RBAC (policy-based authorization)
  Bağımlılık: T39
  Dokümanlar: 05 §6.2, 07 §9
  Kabul kriterleri:
    - Her admin endpoint'inde permission kontrolü
    - Policy-based authorization .NET built-in ile
    - Dinamik rol grupları (DB'den okunan yetkiler)
    - INSUFFICIENT_PERMISSION (403) hata dönüşü
  Test beklentisi: Integration — yetkili/yetkisiz erişim, dinamik yetki değişikliği
  Doğrulama kontrol listesi:
    - [ ] 07 §9'daki her endpoint'in hangi yetkiyi gerektirdiği belirli mi?
```

```
Task T41: Admin parametre yönetimi
  Bağımlılık: T26, T40
  Dokümanlar: 07 §9.8–§9.9, 02 §16.2
  Kabul kriterleri:
    - GET /admin/settings → tüm platform parametreleri
    - PUT /admin/settings/:key → tek parametre güncelleme
    - Parametre değişikliği anında aktif olur, aktif işlemleri etkilemez
    - AuditLog kaydı oluşturulur
    - Tüm 02 §16.2'deki parametreler yönetilebilir
  Test beklentisi: Integration — parametre okuma/güncelleme, audit log
  Doğrulama kontrol listesi:
    - [ ] 02 §16.2'deki tüm parametreler mevcut mu?
    - [ ] 07 §9.8–§9.9 sözleşmeleri doğru mu?
```

```
Task T42: AuditLog servisi
  Bağımlılık: T25, T03
  Dokümanlar: 05 §5.4, 06 §3.20, 09 §18.6
  Kabul kriterleri:
    - Merkezi AuditLog servisi: tüm audit kayıtları bu servis üzerinden yazılır
    - ActorType + ActorId invariantı zorunlu
    - Doğrudan INSERT yasağı (sadece servis üzerinden)
    - Immutable kayıt (UPDATE/DELETE engeli)
    - GET /admin/audit-logs → audit log listesi (paginated, filtrelenebilir)
    - 12 AuditAction türü destekleniyor
  Test beklentisi: Integration — audit kaydı oluşturma, listeleme, filtreleme, immutability
  Doğrulama kontrol listesi:
    - [ ] 06 §3.20 AuditLog yapısı doğru mu?
    - [ ] 09 §18.6 merkezi servis kuralları uygulanmış mı?
```

```
Task T43: User itibar skoru hesaplama
  Bağımlılık: T18, T19
  Dokümanlar: 02 §13, 06 §3.1, 06 §8.2
  Kabul kriterleri:
    - Tamamlanan işlem sayısı denormalized güncelleme (COMPLETED'da)
    - Başarılı işlem oranı hesaplama (sorumluluk bazlı — kimin iptal ettiğine göre)
    - Hesap yaşı hesaplama
    - İptal oranı skoru etkiliyor
    - Wash trading: aynı çift arasındaki işlemler skora etki etmiyor (1 ay penceresi, 02 §14.1)
    - CooldownExpiresAt hesaplama (iptal limiti aşıldığında)
    - Composite reputationScore (06 §3.1 formülü) read path'te hesaplanır:
        ROUND(SuccessfulTransactionRate × 5, 1) ya da yetersiz veri eşiklerinden biri
        karşılanırsa null. Eşikler: reputation.min_account_age_days (default 30) ve
        reputation.min_completed_transactions (default 3) — yeni SystemSetting (kategori
        reputation, IsConfigured=true), 32 → 34 seed satırı; SystemSettingsCatalog +
        SystemSettingsValidator (>0 numeric) ile bağlanır. T33'ün UserProfileDto/
        UserStatsDto/PublicUserProfileDto null devri kapatılır — UserProfileService
        composite skoru hesaplayan ortak helper'a bağlanır.
  Test beklentisi: Unit — skor hesaplama formülleri (rate + composite + wash filter +
    cooldown); Integration — denormalized güncelleme + UserProfile DTO composite
    reputationScore (06 §3.1 örnek tablosu birebir).
  Doğrulama kontrol listesi:
    - [ ] 02 §13 skor kriterleri (formül + eşikler) uygulanmış mı?
    - [ ] 06 §3.1 composite reputationScore formülü ve örnek tablosu birebir doğrulandı mı?
    - [ ] 06 §8.2 denormalized field güncelleme kuralları doğru mu?
    - [ ] reputation.min_account_age_days + reputation.min_completed_transactions
          SystemSetting seed (default 30 / 3) ve catalog/validator entry'leri eklendi mi?
    - [ ] T33 UserProfileService null devri (reputationScore) composite hesaplamayla
          kapandı mı? UserProfileEndpointTests güncellendi mi?
```

---

### F3 — İş Mantığı (T44–T63)

```
Task T44: Transaction State Machine
  Bağımlılık: T19, T03
  Dokümanlar: 05 §4.1–§4.5, 09 §9.2
  Kabul kriterleri:
    - Stateless kütüphanesi ile TransactionStateMachine sınıfı
    - 13 durum, tüm geçişler deklaratif olarak tanımlı
    - Guard mekanizması: geçersiz geçişler DomainException fırlatır
    - RowVersion doğrulama guard'da
    - OnEntry/OnExit side effect handler'ları (bildirim, timeout başlatma)
    - Emergency hold mekanizması (IsOnHold flag, dondurma/çözme)
    - 06 §3.5 status → zorunlu field matrisi guard olarak uygulanmış (FLAGGED state kuralları dahil: tüm deadline/job NULL)
  Test beklentisi: Unit — her durum × her trigger (geçerli + geçersiz), 05 §4.1 durum geçiş tablosuyla birebir eşleşme
  Doğrulama kontrol listesi:
    - [ ] 05 §4.1 durum geçiş tablosu birebir eşleşiyor mu?
    - [ ] Geçersiz geçişler DomainException fırlatıyor mu?
    - [ ] RowVersion guard çalışıyor mu?
    - [ ] 06 §3.5 status → zorunlu field matrisi birebir eşleşiyor mu?
```

```
Task T45: İşlem oluşturma akışı
  Bağımlılık: T44, T34, T43
  Dokümanlar: 07 §7.1–§7.4, 02 §2, §6, §8, §14.4, 03 §2.2
  Kabul kriterleri:
    - GET /transactions/eligibility → uygunluk kontrolü (MA, concurrent limit, cooldown, new account limit, flag, cooldown)
    - GET /transactions/params → form parametreleri (fiyat aralığı, komisyon, timeout aralığı, stablecoin'ler)
    - POST /transactions → işlem oluşturma
    - Validasyonlar: stablecoin, fiyat min/max, timeout aralığı, buyerIdentificationMethod, Steam ID, item tradeable
    - Steam envanter okuma (API çağrısı T67'de implement edilecek, burada interface üzerinden)
    - Fraud pre-check: fiyat sapması eşiği → FLAGGED (pre-create)
    - Alıcı belirleme: Steam ID veya açık link (admin toggle)
    - Cüzdan adresi zorunluluk kontrolü
    - Outbox event: TransactionCreatedEvent
    - Bildirim: alıcıya davet (kayıtlıysa), satıcıya davet linki
  Test beklentisi: Unit — validasyonlar, fraud pre-check; Integration — tam oluşturma akışı
  Doğrulama kontrol listesi:
    - [ ] 07 §7.1–§7.4 endpoint sözleşmeleri doğru mu?
    - [ ] 02 §2, §6, §8, §14.4 iş kuralları eksiksiz mi?
    - [ ] 03 §2.2 akış adımları karşılanmış mı?
```

```
Task T46: Alıcı kabul akışı
  Bağımlılık: T44, T34
  Dokümanlar: 07 §7.5–§7.6, 02 §6, 03 §3.1–§3.2
  Kabul kriterleri:
    - GET /transactions/:id → işlem detay (public/authenticated, role bazlı varyant)
    - POST /transactions/:id/accept → alıcı kabulü
    - Steam ID eşleşme kontrolü (Yöntem 1) veya açık link (Yöntem 2, ilk gelen)
    - İade adresi zorunlu (TRC-20 format + sanctions)
    - Alıcı refund-address cooldown kontrolü
    - State geçişi: CREATED → ACCEPTED
    - Outbox event: BuyerAcceptedEvent
    - Bildirim: satıcıya "alıcı kabul etti"
  Test beklentisi: Unit — Steam ID eşleşme, validasyonlar; Integration — kabul akışı
  Doğrulama kontrol listesi:
    - [ ] 07 §7.5–§7.6 sözleşmeleri doğru mu?
    - [ ] 03 §3.1–§3.2 akış adımları karşılanmış mı?
    - [ ] Yöntem 1 ve 2 ayrımı doğru mu?
```

```
Task T47: Timeout scheduling
  Bağımlılık: T44, T09
  Dokümanlar: 02 §3, 05 §4.4, 09 §13.3
  Kabul kriterleri:
    - Her state geçişinde ilgili timeout Hangfire delayed job olarak schedule edilir
    - Job ID entity'ye kaydedilir
    - İptal/tamamlanma/state değişikliğinde mevcut job temizlenir ve yeni schedule yapılır
    - Deadline scanner/poller job: AcceptDeadline, TradeOfferToSellerDeadline, TradeOfferToBuyerDeadline enforce
    - Heartbeat job: 30sn periyodik, LastHeartbeat güncelleme
    - Restart recovery: outage window hesaplama, aktif işlem timeout'larını uzatma
  Test beklentisi: Integration — job schedule/cancel, deadline enforce
  Doğrulama kontrol listesi:
    - [ ] 02 §3 tüm timeout adımları schedule ediliyor mu?
    - [ ] 05 §4.4 heartbeat ve recovery pattern'ları uygulanmış mı?
```

```
Task T48: Timeout warning
  Bağımlılık: T47, T37
  Dokümanlar: 02 §3.4, 05 §4.4
  Kabul kriterleri:
    - Timeout süresi dolmadan önce uyarı (admin tarafından ayarlanabilir oran)
    - TimeoutWarningEvent üretimi
    - Bildirim: ilgili tarafa tüm kanallarda "süreniz dolmak üzere"
  Test beklentisi: Unit — uyarı eşiği hesaplama; Integration — uyarı event üretimi
  Doğrulama kontrol listesi:
    - [ ] 02 §3.4 uyarı kuralları uygulanmış mı?
```

```
Task T49: Timeout execution
  Bağımlılık: T47, T44
  Dokümanlar: 02 §3.2, 03 §4.1–§4.5
  Kabul kriterleri:
    - Kabul timeout → CANCELLED_TIMEOUT (iade gerekmez)
    - Trade offer timeout → CANCELLED_TIMEOUT (iade gerekmez — item henüz platformda değil)
    - Ödeme timeout → CANCELLED_TIMEOUT (item satıcıya iade)
    - Teslim timeout → CANCELLED_TIMEOUT (item satıcıya iade, ödeme alıcıya iade)
    - Her senaryoda doğru iade tetikleme
    - Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası)
  Test beklentisi: Unit — her timeout senaryosu; Integration — state geçişi + iade tetikleme
  Doğrulama kontrol listesi:
    - [ ] 03 §4.1–§4.5 timeout sonuçları birebir eşleşiyor mu?
    - [ ] 02 §3.2 iade kuralları doğru mu?
```

```
Task T50: Timeout freeze/resume
  Bağımlılık: T47
  Dokümanlar: 02 §3.3, 05 §4.4–§4.5
  Kabul kriterleri:
    - Platform bakımı: tüm aktif işlemlerin timeout'ları dondurulur
    - Steam kesintisi: Steam bağımlı adımlardaki timeout'lar dondurulur
    - Blockchain degradasyonu: ödeme adımındaki timeout'lar dondurulur
    - Emergency hold: tek işlem dondurma
    - TimeoutFrozenAt, TimeoutFreezeReason, TimeoutRemainingSeconds set edilir
    - Resume: frozen süre hesaplanır, deadline uzatılır, job yeniden schedule
  Test beklentisi: Unit — freeze/resume hesaplama; Integration — freeze/resume cycle
  Doğrulama kontrol listesi:
    - [ ] 02 §3.3 tüm freeze senaryoları var mı?
    - [ ] 05 §4.5 emergency hold mekanizması doğru mu?
```

```
Task T51: İptal akışı
  Bağımlılık: T44, T37
  Dokümanlar: 07 §7.7, 02 §7, 03 §2.5, §3.3
  Kabul kriterleri:
    - POST /transactions/:id/cancel → satıcı/alıcı iptali
    - Kontroller: ödeme gönderilmişse iptal engeli, taraf kontrolü, state kontrolü
    - İptal sebebi zorunlu (min 10 karakter)
    - Item platformdaysa → satıcıya iade tetikleme
    - Ödeme alınmışsa → alıcıya iade tetikleme (fiyat + komisyon - gas fee)
    - CANCELLED_SELLER / CANCELLED_BUYER / CANCELLED_TIMEOUT / CANCELLED_ADMIN state'leri
    - İptal kaydı itibar skoruna yansıtılır
    - İptal cooldown hesaplama
    - Bildirimler: karşı tarafa iptal bildirimi
  Test beklentisi: Unit — her iptal senaryosu, validasyonlar; Integration — iptal + iade + bildirim
  Doğrulama kontrol listesi:
    - [ ] 02 §7 tüm iptal kuralları uygulanmış mı?
    - [ ] 07 §7.7 sözleşmesi doğru mu?
```

```
Task T52: Komisyon ve finansal hesaplamalar
  Bağımlılık: T19
  Dokümanlar: 02 §5, §4.6–§4.7, 06 §8.3, 09 §14
  Kabul kriterleri:
    - CommissionAmount = ROUND(Price × CommissionRate, 6, MidpointRounding.ToZero)
    - TotalAmount = Price + CommissionAmount
    - İade tutarı = Price + CommissionAmount - GasFee
    - Gas fee koruma eşiği: gas fee > komisyon × %10 → aşan kısım satıcı payından kesilir
    - decimal kullanımı zorunlu, ara adımda yuvarlama yok
    - Payment validation: gelen tutar beklenenle tam eşleşme (tolerance yok)
  Test beklentisi: Unit — tüm hesaplama formülleri, boundary value analysis (09 §14.5)
  Doğrulama kontrol listesi:
    - [ ] 09 §14 hesaplama kuralları eksiksiz mi?
    - [ ] 06 §8.3 formüller birebir eşleşiyor mu?
```

```
Task T53: Gas fee yönetimi
  Bağımlılık: T52
  Dokümanlar: 02 §4.7, 09 §14
  Kabul kriterleri:
    - Satıcıya gönderim: gas fee komisyondan karşılanır
    - Koruma eşiği aşılırsa: aşan kısım satıcı payından kesilir
    - İade: gas fee iade tutarından düşülür
    - Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert
  Test beklentisi: Unit — eşik hesaplamaları, minimum iade kontrolü
  Doğrulama kontrol listesi:
    - [ ] 02 §4.7 gas fee kuralları eksiksiz mi?
```

```
Task T54: Fraud flag sistemi
  Bağımlılık: T22, T44, T42
  Dokümanlar: 02 §14.0, 07 §9.2–§9.5, 03 §7–§8.2
  Kabul kriterleri:
    - Hesap flag: fon akışı aksiyonları engellenir, mevcut işlemler devam eder
    - İşlem flag (pre-create): işlem CREATED öncesi durdurulur, timeout başlamaz
    - Admin flag kuyruğu: GET /admin/flags, GET /admin/flags/:id
    - Admin onay: POST /admin/flags/:id/approve → işlem devam
    - Admin red: POST /admin/flags/:id/reject → işlem iptal
    - Yüksek risk durumlarında (sanctions, hesap ele geçirme): aktif işlemlere otomatik EMERGENCY_HOLD
    - AuditLog kaydı tüm flag aksiyonlarında
    - Bildirimler: admin'e flag bildirimi, taraflara sonuç bildirimi
  Test beklentisi: Unit — flag oluşturma kuralları; Integration — flag → admin review → sonuç
  Doğrulama kontrol listesi:
    - [ ] 02 §14.0 flag kategorileri ve etkileri doğru mu?
    - [ ] 07 §9.2–§9.5 endpoint sözleşmeleri doğru mu?
```

```
Task T55: AML kontrolü (fiyat sapması, yüksek hacim)
  Bağımlılık: T54
  Dokümanlar: 02 §14.4, 03 §7.1–§7.2
  Kabul kriterleri:
    - Piyasa fiyatından sapma eşiği kontrolü (işlem oluşturma anında)
    - Eşik aşılırsa → FLAGGED (pre-create), timeout başlamaz
    - Kısa sürede yüksek hacim tespiti → flag
    - Dormant hesap anomali tespiti: hesap yaşı vs işlem hacmi orantısızlığı (hiç işlem yapmayan hesabın aniden yüksek hacimli işlem yapması)
    - Eşikler admin tarafından SystemSetting'den okunur
  Test beklentisi: Unit — sapma hesaplama, hacim kontrolü, dormant hesap anomali; Integration — flag oluşturma
  Doğrulama kontrol listesi:
    - [ ] 02 §14.4 AML kuralları eksiksiz mi?
    - [ ] Dormant hesap anomali tespiti çalışıyor mu?
```

```
Task T56: Çoklu hesap tespiti
  Bağımlılık: T54, T18
  Dokümanlar: 02 §14.3, 03 §7.4
  Kabul kriterleri:
    - Güçlü sinyal: aynı cüzdan adresi birden fazla hesapta → flag
    - Destekleyici sinyal: aynı gönderim adresi (exchange hariç) → tek başına flag değil
    - Destekleyici sinyal: aynı IP/cihaz parmak izi → tek başına flag değil
    - Sinyal kombinasyonu değerlendirmesi
    - Admin'e bildirim
  Test beklentisi: Unit — sinyal eşleştirme mantığı; Integration — flag oluşturma
  Doğrulama kontrol listesi:
    - [ ] 02 §14.3 tüm sinyal türleri uygulanmış mı?
```

```
Task T57: Wash trading kontrolü
  Bağımlılık: T43
  Dokümanlar: 02 §14.1
  Kabul kriterleri:
    - Aynı alıcı-satıcı çifti arasında ardışık işlemler arasında min 1 ay kontrolü
    - Bu süreden kısa → işlem engellenmez, skor etkisi kaldırılır
  Test beklentisi: Unit — 1 ay kuralı, skor etkisi
  Doğrulama kontrol listesi:
    - [ ] 02 §14.1 kuralları birebir mi?
  Not: T43 kapsamında implement edildi (PR #72 — `WashTradingFilter` + `ReputationAggregator` wiring + 7 unit + 2 integration test). T57 audit trail'i doc-only confirmation PR #88 (TASK_REPORTS/T57_REPORT.md) ile kapatıldı — kabul kriterleri T43 kanıt zinciriyle 1:1 eşlenmiştir.
```

```
Task T58: Dispute sistemi
  Bağımlılık: T44, T22, T37
  Dokümanlar: 07 §7.8–§7.10, 02 §10, 03 §6
  Kabul kriterleri:
    - POST /transactions/:id/disputes → dispute açma (sadece alıcı)
    - 3 tür: PAYMENT, DELIVERY, WRONG_ITEM
    - Otomatik doğrulama: blockchain kontrol (ödeme), Steam kontrol (teslim), item karşılaştırma (yanlış item)
    - POST /transactions/:id/disputes/:disputeId/submit-txhash → TX hash ile yeniden doğrulama
    - POST /transactions/:id/disputes/:disputeId/escalate → admin'e iletme
    - Dispute timeout'u durdurmaz
    - Aynı türde tekrar açılamaz, eşzamanlı farklı türler mümkün
    - Rate limiting: işlem başına
  Test beklentisi: Unit — otomatik doğrulama mantığı; Integration — dispute açma → otomatik kontrol → eskalasyon
  Doğrulama kontrol listesi:
    - [ ] 02 §10 dispute kuralları eksiksiz mi?
    - [ ] 07 §7.8–§7.10 sözleşmeleri doğru mu?
```

```
Task T59: Emergency hold
  Bağımlılık: T44, T50, T40
  Dokümanlar: 07 §9.20–§9.22, 02 §7
  Kabul kriterleri:
    - POST /admin/transactions/:id/cancel → admin doğrudan iptal
    - POST /admin/transactions/:id/emergency-hold → hold uygulama
    - POST /admin/transactions/:id/release-hold → hold kaldırma (RESUME veya CANCEL)
    - CANCEL_TRANSACTIONS ve EMERGENCY_HOLD ayrı yetkiler
    - ITEM_DELIVERED hold'unda CANCEL yasak, yalnızca RESUME
    - Timeout durur, akış bekler
    - Tüm aksiyonlar AuditLog'a yazılır
    - Bildirimler: taraflara hold/release bildirimi
  Test beklentisi: Unit — yetki kontrolü, ITEM_DELIVERED kısıtı; Integration — hold → resume/cancel cycle
  Doğrulama kontrol listesi:
    - [ ] 02 §7 emergency hold kuralları eksiksiz mi?
    - [ ] 07 §9.20–§9.22 sözleşmeleri doğru mu?
```

```
Task T60: Satıcı payout issue
  Bağımlılık: T44, T25, T37
  Dokümanlar: 07 §7.11, 02 §10.3, 06 §3.8a
  Kabul kriterleri:
    - POST /transactions/:id/report-payout-issue → sadece COMPLETED işlemler, sadece satıcı
    - Otomatik doğrulama: tx hash ile blockchain kontrolü
    - Retry: gönderim başarısız/stuck ise otomatik yeniden deneme
    - Eskalasyon: otomatik çözüm başarısızsa admin'e
    - SellerPayoutIssue entity state'leri: REPORTED → VERIFYING → RETRY_SCHEDULED / ESCALATED → RESOLVED
  Test beklentisi: Integration — sorun bildirme, otomatik doğrulama, retry, eskalasyon
  Doğrulama kontrol listesi:
    - [ ] 06 §3.8a SellerPayoutIssue yapısı doğru mu?
    - [ ] 07 §7.11 sözleşmesi doğru mu?
```

```
Task T61: SignalR hub — işlem real-time güncellemeler
  Bağımlılık: T44
  Dokümanlar: 07 §11.1
  Kabul kriterleri:
    - /hubs/transactions hub'ı
    - Client→Server: JoinTransaction, LeaveTransaction
    - Server→Client: TransactionStatusChanged, CountdownSync (30sn + freeze/unfreeze), PaymentDetected, PaymentConfirmed, DisputeUpdate, FlagResolved, EmergencyHoldApplied, EmergencyHoldReleased
    - JWT authentication (query param)
    - Grup bazlı mesajlaşma (transaction ID)
  Test beklentisi: Integration — hub bağlantısı, event push
  Doğrulama kontrol listesi:
    - [ ] 07 §11.1 tüm event'ler tanımlı mı?
    - [ ] Auth doğru çalışıyor mu?
```

```
Task T62: SignalR hub — bildirim push
  Bağımlılık: T38
  Dokümanlar: 07 §11.2
  Kabul kriterleri:
    - /hubs/notifications hub'ı
    - T38'den gelen Notification entity'lerini real-time push olarak iletir
    - Server→Client: NewNotification, UnreadCountChanged, TelegramConnected, DiscordConnected, MaintenanceStatusChanged
    - User bazlı mesajlaşma (user ID)
  Test beklentisi: Integration — bildirim push, T38 notification → real-time iletim
  Doğrulama kontrol listesi:
    - [ ] 07 §11.2 tüm event'ler tanımlı mı?
    - [ ] T38 Notification entity'leri real-time push ediliyor mu?
```

```
Task T63: Admin dashboard ve işlem yönetimi API
  Bağımlılık: T40, T42, T19
  Dokümanlar: 07 §9.1, §9.6–§9.7, §9.19, 02 §16
  Kabul kriterleri:
    - GET /admin/dashboard → özet (aktif işlem, flag sayısı, Steam hesap durumu)
    - GET /admin/transactions → tüm işlem listesi (paginated, filtrelenebilir)
    - GET /admin/transactions/:id → işlem tam admin görünümü (status history, payment detail, payout, refund, notification, dispute, flag history)
    - GET /admin/audit-logs → audit log listesi (paginated, filtrelenebilir)
    - GET /admin/users/:steamId/transactions → kullanıcının işlem geçmişi
    - GET /admin/steam-accounts → Steam bot hesapları durumu
  Test beklentisi: Integration — dashboard veri, işlem listesi/filtre, audit log
  Doğrulama kontrol listesi:
    - [ ] 07 §9.1–§9.19 admin endpoint'leri eksiksiz mi?
```

```
Task T63a: Platform public endpoint'leri (backend)
  Bağımlılık: T04
  Dokümanlar: 07 §10.1–§10.2
  Kabul kriterleri:
    - GET /platform/stats → platform istatistikleri (tamamlanan işlem sayısı, toplam hacim vb.), 15dk cache
    - GET /platform/maintenance → bakım durumu (aktif/pasif, mesaj, tahmini bitiş)
    - Anonim erişim (auth gerekmez)
  Test beklentisi: Integration — stats endpoint doğru veri döndürüyor mu, maintenance durumu doğru mu, cache çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §10.1–§10.2 endpoint sözleşmeleri doğru mu?
    - [ ] Cache mekanizması çalışıyor mu?
```

```
Task T63b: Retention job'ları (toplu temizlik)
  Bağımlılık: T09, T25, T23
  Dokümanlar: 06 §8.2, §3.18, §3.19, §3.20
  Kabul kriterleri:
    - Hangfire recurring job: OutboxMessage + ProcessedEvent + ExternalIdempotencyRecord — 30 gün sonra toplu hard delete (silme sırası: önce ProcessedEvent, sonra OutboxMessage)
    - Hangfire recurring job: Bağımsız bildirimler (Notification, TransactionId = NULL) + ilgili NotificationDelivery kayıtları — retention süresi sonrası toplu purge (önce delivery, sonra notification)
    - Soft-deleted entity'ler için retention-based hard purge (06 §8.2 lifecycle'a uygun)
    - Retention süreleri SystemSetting'den okunur (admin tarafından ayarlanabilir)
    - Batch büyüklüğü sınırlandırılmış (DB yükü kontrolü)
  Test beklentisi: Integration — retention süresi dolmuş kayıtlar temizleniyor mu, silme sırası doğru mu, batch limit çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 06 §8.2 retention kuralları eksiksiz uygulanmış mı?
    - [ ] Silme sırası FK-safe mi (ProcessedEvent → OutboxMessage)?
    - [ ] Bağımsız bildirim retention ayrımı doğru mu?
```

---

### F4 — Entegrasyonlar (T64–T83)

```
Task T64: Steam Sidecar — bot session yönetimi
  Bağımlılık: T14
  Dokümanlar: 08 §2.4–§2.5, §2.7, 05 §3.2
  Kabul kriterleri:
    - Bot login: username, password, shared_secret ile oturum açma
    - Session expire tespiti ve otomatik re-login
    - Health check: 60sn periyodik Steam bot session kontrolü
    - Failover: session başarısız → cookie yenileme → re-login → bot havuzdan çıkarma → admin alert
    - steam-totp ile mobile confirmation otomatik onayı
  Test beklentisi: Unit — session state yönetimi; Contract — sidecar ↔ backend sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.7 hata yönetimi zinciri doğru mu?
    - [ ] Bot health check periyodu ve logic doğru mu?
```

```
Task T65: Steam Sidecar — trade offer gönderme
  Bağımlılık: T64
  Dokümanlar: 08 §2.4, 05 §3.2
  Kabul kriterleri:
    - Trade offer gönderme (satıcıya item emanet, alıcıya item teslim, satıcıya iade)
    - steam-tradeoffer-manager ile offer oluşturma ve gönderme
    - Mobile confirmation otomatik onayı
    - Retry: exponential backoff (5s, 15s, 45s), timeout süresi içinde
    - Counter offer handling: desteklenmiyor, orijinal offer iptal
    - Webhook callback: trade offer durumu backend'e bildirilir
  Test beklentisi: Contract — offer gönderim/durum callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.4 trade offer durum yönetimi eksiksiz mi?
```

```
Task T66: Steam Sidecar — trade offer durum izleme
  Bağımlılık: T65
  Dokümanlar: 08 §2.4, §2.7
  Kabul kriterleri:
    - 10sn aralıkla polling (steam-tradeoffer-manager built-in)
    - Durum değişikliğinde webhook callback: Accepted, Declined, Expired, Countered, InvalidItems
    - InvalidItems → kullanıcıya bilgi, işlem iptal
    - FAILED durumu: retry geçerli
  Test beklentisi: Contract — durum callback payload sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.4 tüm offer durumları ele alınmış mı?
    - [ ] 08 §2.7 hata senaryoları karşılanmış mı?
```

```
Task T67: Steam Sidecar — envanter okuma
  Bağımlılık: T64
  Dokümanlar: 08 §2.3, 07 §6.1
  Kabul kriterleri:
    - Steam Community envanter endpoint: inventory/{steamId}/730/2
    - Pagination desteği (5000+ item, start_assetid/more_items)
    - Assets + descriptions merge (classid + instanceid join)
    - Redis cache: 2dk TTL, işlem sonrası invalidation
    - API endpoint: GET /steam/inventory (backend → sidecar HTTP çağrısı)
    - Private envanter tespiti → kullanıcıya uyarı
  Test beklentisi: Contract — envanter response sözleşmesi; Integration (backend) — API endpoint
  Doğrulama kontrol listesi:
    - [ ] 08 §2.3 pagination ve merge kuralları doğru mu?
    - [ ] 07 §6.1 endpoint sözleşmesi doğru mu?
```

```
Task T68: Steam Sidecar — webhook callback ve backend entegrasyonu
  Bağımlılık: T65, T66, T05
  Dokümanlar: 05 §3.4, 09 §11.3, §17.5
  Kabul kriterleri:
    - Sidecar → Backend webhook: HMAC-SHA256 imzalama, timestamp, nonce, signature header
    - Backend webhook handler: WebhookSignatureMiddleware ile doğrulama
    - Replay koruması: timestamp ±5dk, nonce tekrar kontrolü (ProcessedNonce)
    - Trade offer durum güncellemelerini backend'de işleme → state machine tetikleme
    - Idempotent işleme
  Test beklentisi: Integration — webhook doğrulama, durum güncellemesi → state geçişi
  Doğrulama kontrol listesi:
    - [ ] 05 §3.4 güvenlik kuralları eksiksiz mi?
    - [ ] Replay koruması çalışıyor mu?
```

```
Task T69: Steam Sidecar — bot failover ve capacity-based seçim
  Bağımlılık: T64, T21
  Dokümanlar: 05 §3.2, 02 §15
  Kabul kriterleri:
    - Capacity-based bot seçimi: en az emanet item olan aktif bot
    - Kısıtlı bot tespiti: yeni işlemler diğer botlara yönlendirme
    - Kısıtlı botta emanet item'lar: recovery/manual intervention akışı
    - Admin bildirim: bot kısıtlandı uyarısı
  Test beklentisi: Unit — bot seçim algoritması; Integration — failover senaryosu
  Doğrulama kontrol listesi:
    - [ ] 02 §15 bot yönetimi kuralları doğru mu?
```

```
Task T70: Blockchain Sidecar — HD wallet adres üretimi
  Bağımlılık: T15
  Dokümanlar: 08 §3.2, 05 §3.3
  Kabul kriterleri:
    - BIP-44 derivation path: m/44'/195'/0'/0/{index}
    - Backend → sidecar HTTP çağrısı ile adres üretimi
    - Index artırma, DB kayıt (PaymentAddress), UNIQUE constraint
    - Master seed güvenliği: vault/secrets (prod), env var (dev)
    - Private key sadece imzalama anında memory'ye yüklenir, sonra temizlenir
  Test beklentisi: Unit — derivation path doğru mu; Contract — adres üretim sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.2 HD wallet kuralları eksiksiz mi?
    - [ ] 06 §5.1 PaymentAddress.HdWalletIndex UNIQUE mi?
```

```
Task T71: Blockchain Sidecar — ödeme izleme
  Bağımlılık: T70
  Dokümanlar: 08 §3.4, 05 §3.3
  Kabul kriterleri:
    - 3sn polling aralığı ile deposit adresi izleme
    - Aşama 1: beklenen token sorgusu (contract_address filtreli, only_confirmed, fingerprint pagination)
    - Aşama 2: yanlış token taraması (filtresiz, tüm TRC-20)
    - Kayıt türü filtresi: yalnızca Transfer türü (Authorization/Approval/TRC-721 skip)
    - 20 blok minimum onay (finality: currentSolidBlock - txBlock >= 20)
    - İdempotent işleme: txid + event_index bileşik anahtar
    - Wrong-token: allowlist'te → iade, spam → ignore + log
    - Backend'e webhook callback: PaymentDetected, PaymentConfirmed
  Test beklentisi: Unit — finality hesaplama, wrong-token logic; Contract — izleme callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.4 tüm izleme kuralları uygulanmış mı?
    - [ ] Finality hesaplaması doğru mu?
```

```
Task T72: Blockchain Sidecar — tutar doğrulama ve edge case'ler
  Bağımlılık: T71
  Dokümanlar: 08 §3.4, 02 §4.4
  Kabul kriterleri:
    - Doğru tutar → PAYMENT_RECEIVED
    - Eksik tutar → iade + bildirim
    - Fazla tutar → doğru tutarı kabul, fazlayı iade + bildirim
    - Yanlış token (desteklenen TRC-20) → iade + bildirim
    - Desteklenmeyen token → admin review
    - Çoklu/parçalı ödeme → birleştirmez, ilk doğru kabul, sonraki iade
    - Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert
    - İade kaynak adrese gönderilir (source address parse)
  Test beklentisi: Unit — her edge case senaryosu; Contract — callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 02 §4.4 tüm edge case'ler karşılanmış mı?
```

```
Task T73: Blockchain Sidecar — TRC-20 transfer (payout, refund, sweep)
  Bağımlılık: T70
  Dokümanlar: 08 §3.1, §3.3, 05 §3.3
  Kabul kriterleri:
    - Satıcıya payout: TRC-20 transfer, retry 3 deneme (1dk, 5dk, 15dk), başarısızlıkta admin alert
    - Alıcıya refund: TRC-20 transfer, retry 3 deneme
    - Sweep: deposit → hot wallet, sweep sonrası delegation geri alımı
    - Sweep hata yönetimi: retry + fallback (deposit'ten doğrudan gönderim)
    - Transaction broadcasting: broadcasttransaction endpoint
    - Onay takibi: gettransactioninfobyid ile doğrulama
  Test beklentisi: Contract — transfer callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.1 TronGrid API çağrıları doğru mu?
    - [ ] Retry stratejisi doğru mu?
```

```
Task T74: Blockchain Sidecar — energy delegation
  Bağımlılık: T73
  Dokümanlar: 08 §3.3
  Kabul kriterleri:
    - Sweep öncesi deposit adresine geçici Energy delegation
    - delegateresource çağrısı
    - Sweep sonrası undelegateresource ile geri alım
    - Fallback: delegation başarısızsa deposit adresine minimum TRX transfer
  Test beklentisi: Contract — delegation callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.3 energy delegation akışı doğru mu?
```

```
Task T75: Blockchain Sidecar — gecikmeli ödeme izleme
  Bağımlılık: T71
  Dokümanlar: 08 §3.4, 02 §4.4
  Kabul kriterleri:
    - İptal sonrası kademeli polling: 30s → 5dk → 1sa → durdur (MonitoringStatus: POST_CANCEL_24H → 7D → 30D → STOPPED)
    - Gecikmeli ödeme tespit edilirse → alıcının iade adresine otomatik iade
    - Gas fee düşülür
  Test beklentisi: Unit — polling aralığı geçişleri; Contract — callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 06 §2.16 MonitoringStatus değerleri doğru mu?
    - [ ] 02 §4.4 gecikmeli ödeme kuralları eksiksiz mi?
```

```
Task T76: Blockchain Sidecar — reconciliation job
  Bağımlılık: T73
  Dokümanlar: 05 §3.3
  Kabul kriterleri:
    - Günlük reconciliation: on-chain bakiye vs platform ledger karşılaştırma
    - Uyumsuzluk tespit edilirse admin alert
  Test beklentisi: Yok (operasyonel job)
  Doğrulama kontrol listesi:
    - [ ] 05 §3.3 reconciliation kuralları doğru mu?
```

```
Task T77: Blockchain Sidecar — hot wallet yönetimi
  Bağımlılık: T73, T25
  Dokümanlar: 05 §3.3, 06 §3.22
  Kabul kriterleri:
    - Hot wallet bakiye monitoring (TRX + USDT + USDC)
    - Limit aşımında admin alert (cold wallet transferi MVP'de admin tarafından manuel başlatılır — 05 §3.3)
    - Manuel cold wallet transferi sonrası ColdWalletTransfer ledger kaydı (DB'de, tx hash + tutar + tarih)
    - Hot wallet TRX bakiyesi eşik altında → admin alert
  Test beklentisi: Unit — limit kontrolü, alert tetikleme; Integration — manuel transfer sonrası ledger kaydı + reconciliation eşleşmesi
  Doğrulama kontrol listesi:
    - [ ] 06 §3.22 ColdWalletTransfer yapısı doğru mu?
```

```
Task T78: Email entegrasyonu (Resend)
  Bağımlılık: T37
  Dokümanlar: 08 §4.1–§4.3
  Kabul kriterleri:
    - IEmailSender interface + Resend implementasyonu
    - POST /emails çağrısı (Authorization: Bearer)
    - Email şablonları: .resx ile 4 dil, kanal bazlı format (işlem, güvenlik, hesap, timeout)
    - Retry: 5xx → 3 deneme (1dk, 5dk, 15dk), 422 → retry yok
    - Deferred: geçici hata → DEFERRED state, arka plan job (30dk, 1sa, 4sa)
    - Resend webhook handler: bounced, delivery_delayed, complained, failed, suppressed
    - Webhook güvenlik: Svix header doğrulama, replay koruması (5dk), idempotency (svix-id)
    - DNS: DKIM, SPF, DMARC, Return-Path
  Test beklentisi: Integration — email gönderim (sandbox), webhook handler
  Doğrulama kontrol listesi:
    - [ ] 08 §4.1–§4.3 tüm webhook event'leri ele alınmış mı?
    - [ ] Güvenlik kuralları eksiksiz mi?
```

```
Task T79: Telegram entegrasyonu
  Bağımlılık: T37, T35
  Dokümanlar: 08 §5.1–§5.5
  Kabul kriterleri:
    - Telegram Bot: BotFather ile oluşturma, token alma
    - Deep Link bağlantı: benzersiz kod (10dk TTL, single-use, 122+ bit entropy), /start ile eşleşme, chat_id kayıt
    - Webhook: POST /webhooks/telegram, secret_token doğrulaması
    - Webhook idempotency: update_id ile duplicate filtreleme (Redis, 24sa TTL)
    - sendMessage: MarkdownV2 format, escape helper
    - Rate limit: chat başına 1 msg/s, farklı chat'ler 30 msg/s, sıralı kuyruk
    - Hata yönetimi: 429 → retry_after bekle, 403 neden ayrıştırma (blocked/deactivated/can't send/can't initiate), 400 → bağlantı kopmuş, 5xx → 3 deneme
    - setWebhook: url, secret_token, max_connections=40, allowed_updates=["message"]
  Test beklentisi: Integration — bağlantı akışı, mesaj gönderimi, webhook handler
  Doğrulama kontrol listesi:
    - [ ] 08 §5.1–§5.5 tüm entegrasyon detayları uygulanmış mı?
    - [ ] 403 neden ayrıştırma doğru mu?
```

```
Task T80: Discord entegrasyonu
  Bağımlılık: T37, T35
  Dokümanlar: 08 §6.1–§6.5
  Kabul kriterleri:
    - Discord Bot: Developer Portal, OAuth2 scope: identify
    - MVP Guild Install: Skinora sunucusu, bot invite
    - OAuth2 bağlantı: identify scope, callback, discord_user_id kayıt
    - State parametresi: server-side session correlation (CSRF koruması)
    - DM kanal: POST /users/@me/channels → POST /channels/{id}/messages
    - Mention koruması: allowed_mentions: { "parse": [] }
    - Rate limit: header-driven (X-RateLimit-*), kuyruk + throttle
    - Hata yönetimi: 401 → admin alert, 403 → DM kapalı/mutual guild yok, 404 → kanal devre dışı, 5xx → 3 deneme
    - DM channel ID cache: Redis
  Test beklentisi: Integration — OAuth2 akışı, DM gönderimi
  Doğrulama kontrol listesi:
    - [ ] 08 §6.1–§6.5 tüm entegrasyon detayları uygulanmış mı?
```

```
Task T81: Steam Market fiyat API
  Bağımlılık: T67
  Dokümanlar: 08 §7.1–§7.4
  Kabul kriterleri:
    - Steam Market priceoverview çağrısı (public, auth yok)
    - Fiyat parse: median_price → lowest_price → no-price (kontrol atla)
    - Currency sembolü strip, binlik ayracı kaldır, nokta ondalık
    - Cache: SQL Server ItemPriceCache, 24s fresh / 48s stale / 48+ expired
    - On-demand fetch: cache kontrol → stale ise arka plan yenileme → expired ise API çağrısı
    - IPriceService interface ile abstraction
    - Rate limit: ~20 req/dk, bekleme + cache kullan
    - Erişilemez → cache ≤48s kullan, yoksa kontrol atla + log
  Test beklentisi: Unit — fiyat parse, cache logic; Integration — API çağrısı + cache
  Doğrulama kontrol listesi:
    - [ ] 08 §7.1–§7.4 tüm kurallar uygulanmış mı?
```

```
Task T82: Sanctions screening servisi
  Bağımlılık: T34
  Dokümanlar: 02 §21.1, §12.3, 03 §11a.3
  Kabul kriterleri:
    - Cüzdan adresi yaptırımlı adres listesiyle karşılaştırma
    - Eşleşme: yeni işlem/adres kaydı engellenir, hesap flag'lenir
    - Yüksek risk: aktif işlemlere otomatik EMERGENCY_HOLD
    - Tarama listesi admin tarafından güncellenebilir
    - Merkezi doğrulama pipeline'ın parçası
  Test beklentisi: Unit — adres eşleştirme; Integration — flag oluşturma + hold tetikleme
  Doğrulama kontrol listesi:
    - [ ] 02 §21.1 sanctions kuralları eksiksiz mi?
```

```
Task T83: Geo-block servisi
  Bağımlılık: T30
  Dokümanlar: 02 §21.1, 03 §11a.1
  Kabul kriterleri:
    - IP adresinden coğrafi konum tespiti
    - Yasaklı bölge → bilgilendirme sayfası, erişim engeli
    - Yasaklı ülke listesi admin tarafından yönetilebilir
    - VPN/proxy tespiti destekleyici sinyal (tek başına engelleme değil)
  Test beklentisi: Unit — IP → ülke eşleşme; Integration — engelleme akışı
  Doğrulama kontrol listesi:
    - [ ] 02 §21.1 geo-block kuralları eksiksiz mi?
```

---

### F5 — Kullanıcı Arayüzü (T84–T106)

```
Task T84: Ortak UI bileşenleri (C01–C17)
  Bağımlılık: T13
  Dokümanlar: 04 §5
  Kabul kriterleri:
    - C01 Status Badge: 14 durum, renk kodlu
    - C02 Countdown Timer: gerçek zamanlı, renk geçişli, frozen state
    - C03 Item Card: Compact / Detailed / Selectable
    - C04 User Card: Compact / Detailed
    - C05 Transaction Timeline: 8 adımlı ilerleme çubuğu
    - C06 Cancel Modal: sebep textarea, iade bilgisi, onay
    - C07 Dispute Form: 3 adımlı
    - C08 Maintenance Banner: 4 varyant
    - C09 Toast Notification: bilgi/başarı/uyarı/hata
    - C10 Language Selector: 4 dil
    - C11 Wallet Address Input: TRC-20 validation + sanctions + onay
    - C12 Copy Button
    - C13 Empty State
    - C14 Loading State: Skeleton/Spinner/Progress
    - C15 Error State
    - C16 Pagination
    - C17 Filter Bar
  Test beklentisi: Yok (görsel bileşenler — E2E'de test edilecek)
  Doğrulama kontrol listesi:
    - [ ] 04 §5'teki tüm bileşenler ve varyantları var mı?
```

```
Task T85: Global layout (header, navigation, footer)
  Bağımlılık: T84
  Dokümanlar: 04 §7.1 (header), §8.1 (admin header/menü)
  Kabul kriterleri:
    - Kullanıcı header: logo, bildirim, profil, dil, ayarlar
    - Suspended header: logo, dil, destek, çıkış (kısıtlı)
    - Admin header: logo, admin adı, çıkış
    - Admin sol menü: dashboard, flag'ler, işlemler, ayarlar, steam hesapları, roller, kullanıcılar, audit log
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.1 ve §8.1 layout tanımları doğru mu?
```

```
Task T86: Landing page (S01)
  Bağımlılık: T85
  Dokümanlar: 04 §6.1, 07 §10.1–§10.2
  Kabul kriterleri:
    - Hero section, "Nasıl Çalışır" bölümü, güven göstergeleri, footer
    - GET /platform/stats çağrısı (15dk cache)
    - GET /platform/maintenance → bakım durumu gösterimi
    - Bakım state: C08 banner aktif, CTA devre dışı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §6.1 tüm bölümler var mı?
```

```
Task T87: Auth akışı ekranları
  Bağımlılık: T85
  Dokümanlar: 04 §6.2–§6.7
  Kabul kriterleri:
    - S02 Steam Login: pre-redirect loading, callback loading, auth başarısız
    - S03 MA Uyarısı: adım adım talimat, kontrol et butonu
    - S03a Geo-Block: bilgilendirme sayfası
    - S03b Yaş Gate: 18+ onay
    - S03c Sanctions Uyarı
    - S03d Hesap Askıya Alındı: kısıtlı oturum
    - ToS Modal: 18+ checkbox + ToS checkbox
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §6.2–§6.7 tüm ekranlar ve state'ler var mı?
```

```
Task T88: Dashboard (S05)
  Bağımlılık: T85, T84
  Dokümanlar: 04 §7.1
  Kabul kriterleri:
    - İşlem listesi: tab yapısı (Aktif/Tamamlanan/İptal), satır: ID, item, status badge, fiyat, karşı taraf, tarih, countdown
    - Hızlı istatistik kartları: işlem sayısı, başarı oranı, skor
    - State'ler: yeni kullanıcı (empty), aktif işlem var, yükleniyor (skeleton), hata, suspended session
    - GET /transactions, GET /users/me/stats çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.1 tüm state'ler var mı?
```

```
Task T89: İşlem oluşturma (S06)
  Bağımlılık: T84
  Dokümanlar: 04 §7.2
  Kabul kriterleri:
    - 4 adımlı form: Adım 1 (item seçimi), Adım 2 (detaylar), Adım 3 (alıcı + cüzdan), Adım 4 (özet)
    - Adım göstergesi (step indicator)
    - Envanter grid: arama/filtre, skeleton loading, boş/hata state
    - Validasyonlar: fiyat min/max, timeout aralığı, Steam ID format, non-tradeable engel, payout adresi zorunlu
    - Engel state'leri: concurrent limit, cooldown, yeni hesap limiti, MA pasif, flag aktif, address cooldown
    - GET /transactions/eligibility, /params, /steam/inventory çağrıları
    - POST /transactions çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.2 tüm adımlar, validasyonlar ve engel state'leri var mı?
```

```
Task T90: İşlem detay sayfası (S07) — tüm state varyantları
  Bağımlılık: T84, T96
  Dokümanlar: 04 §7.3
  Kabul kriterleri:
    - State × role varyantları: CREATED (satıcı/alıcı/public), ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED, CANCELLED_*, FLAGGED, EMERGENCY_HOLD
    - Her state'te satıcı ve alıcı görünümü farklı
    - Suspended session override
    - Ödeme edge case banner'ları: eksik/fazla/yanlış token/gecikmeli ödeme
    - Dispute aktif gösterimi
    - İptal bilgileri (sebep, tür, iade özeti)
    - GET /transactions/:id çağrısı
    - SignalR real-time güncellemeler (T96 ile bağlantılı)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.3 tüm state × role varyantları var mı?
    - [ ] 07 §7.5 TransactionDetailResponse tüm alanları ekrana yansıtılmış mı?
```

```
Task T91: Ödeme bilgileri ve edge case UI
  Bağımlılık: T90
  Dokümanlar: 04 §7.3 (ödeme section)
  Kabul kriterleri:
    - Ödeme bilgileri bölümü: adres, tutar, token, ağ, exchange uyarısı
    - Copy button (adres kopyalama)
    - Ödeme özeti: fiyat, gas fee, net ödeme, tx hash
    - Edge case banner'lar: eksik tutar uyarı, fazla tutar bilgi, yanlış token uyarı, gecikmeli ödeme iade bilgisi
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] Tüm ödeme edge case'leri UI'da gösterilmiş mi?
```

```
Task T92: Dispute UI
  Bağımlılık: T90
  Dokümanlar: 04 §7.3 (dispute section), §5 (C07)
  Kabul kriterleri:
    - C07 Dispute Form: 3 adımlı (tür seçimi → otomatik kontrol → eskalasyon)
    - Dispute tür seçimi: ödeme, teslim, yanlış item
    - Otomatik kontrol sonucu gösterimi
    - TX hash girme imkanı (ödeme dispute)
    - Admin'e iletme butonu + detay textarea
    - Dispute durumu gösterimi
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] C07 3 adım doğru mu?
```

```
Task T93: Profil sayfaları (S08, S09)
  Bağımlılık: T85
  Dokümanlar: 04 §7.4–§7.5
  Kabul kriterleri:
    - S08 Kendi profil: avatar, ad, Steam ID, skor, istatistikler, cüzdan adresleri (C11 ile yönetim)
    - S09 Public profil: sınırlı bilgi (avatar, ad, skor, işlem sayısı, hesap yaşı)
    - Cüzdan adresi değişikliği: Steam re-auth akışı tetikleme
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.4–§7.5 tüm alanlar var mı?
```

```
Task T94: Hesap ayarları (S10)
  Bağımlılık: T85
  Dokümanlar: 04 §7.6
  Kabul kriterleri:
    - Bildirim tercihleri: platform içi, email (toggle+input), Telegram (toggle + bağlama akışı), Discord (toggle + OAuth)
    - Dil tercihi (dropdown)
    - Telegram bağlama: doğrulama kodu + bot link
    - Discord bağlama: Discord OAuth
    - Hesabı deaktif et / sil modal'ları
    - Hesap sil: "SİL" yazarak onay
    - Aktif işlem kontrolü (deaktif/sil engeli)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.6 tüm ayarlar ve modal'lar var mı?
```

```
Task T95: Bildirimler sayfası (S11)
  Bağımlılık: T85
  Dokümanlar: 04 §7.7
  Kabul kriterleri:
    - Bildirim listesi: okunmamış vurgusu, ikon, metin, zaman, tıklanabilir
    - "Tüm bildirimleri okundu işaretle" linki
    - State'ler: yok (empty), yeni bildirimler, yükleniyor
    - Pagination
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.7 tüm state'ler var mı?
```

```
Task T96: SignalR client entegrasyonu
  Bağımlılık: T13, T61, T62
  Dokümanlar: 07 §11.1–§11.2, 04 §7.3 (countdown)
  Kabul kriterleri:
    - Transaction hub bağlantısı: join/leave room, event listener'lar
    - Notification hub bağlantısı: real-time bildirim push
    - CountdownSync: 30sn periyodik + freeze/unfreeze
    - PaymentDetected/PaymentConfirmed → UI güncelleme
    - TransactionStatusChanged → state varyantı değişimi
    - MaintenanceStatusChanged → banner gösterimi
    - JWT authentication (query param)
    - Bağlantı kopma/yeniden bağlanma
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 07 §11.1–§11.2 tüm event'ler client'ta dinleniyor mu?
```

```
Task T97: i18n (4 dil desteği)
  Bağımlılık: T13
  Dokümanlar: 04 §10
  Kabul kriterleri:
    - next-intl ile 4 dil: EN, 中文, ES, TR
    - Tarih/saat formatı dil bazlı
    - Sayı formatı dil bazlı (stablecoin hariç)
    - Çevrilmeyecek terimler listesi (USDT, Steam ID, Trade offer vb.)
    - Metin uzunluk esnekliği (EN 1.5x'e kadar)
    - Tüm ekranlarda dil desteği
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §10 tüm lokalizasyon kuralları uygulanmış mı?
```

```
Task T98: Responsive tasarım
  Bağımlılık: T84–T97
  Dokümanlar: 04 §9
  Kabul kriterleri:
    - 3 breakpoint: Desktop ≥1024, Tablet 768-1023, Mobil <768
    - Dashboard responsive: 3 layout
    - İşlem oluşturma: merkezi form → tam genişlik
    - İşlem detay: 2 kolon → tek kolon
    - Admin: sol menü → hamburger menü
    - Tablo → kart dönüşümü (mobilde)
    - Timeline yatay → dikey (mobilde)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §9 tüm responsive kuralları uygulanmış mı?
```

```
Task T99: Admin Dashboard (S12)
  Bağımlılık: T85
  Dokümanlar: 04 §8.1
  Kabul kriterleri:
    - Özet kartları: aktif işlemler, bekleyen flag'ler, günlük/haftalık tamamlanan
    - Son flag'lenmiş işlemler tablosu (son 5)
    - Steam hesapları durum kartları
    - Kısıtlı/banned bot uyarısı
    - GET /admin/dashboard çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.1 tüm bileşenler var mı?
```

```
Task T100: Admin Flag kuyruğu + detay (S13, S14)
  Bağımlılık: T85
  Dokümanlar: 04 §8.2–§8.3
  Kabul kriterleri:
    - S13 Flag kuyruğu: filtreleme (kategori, tür, durum, tarih), liste
    - S14 Flag detay: işlem flag varyantı (fiyat sapması, yüksek hacim) + hesap flag varyantı
    - Admin notu textarea, "devam ettir" / "iptal et" butonları
    - Onay modal'ı
    - GET /admin/flags, GET /admin/flags/:id, POST approve/reject çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.2–§8.3 tüm varyantlar ve aksiyonlar var mı?
```

```
Task T100a: Admin Flag hesap-varyant DTO genişletme (AD2/AD3, S13/S14)
  Bağımlılık: T100
  Dokümanlar: 04 §8.2–§8.3, 07 §9.2–§9.3
  Bağlam: T100 validasyonunda hesap-flag içerik tamlığı eksiklikleri (K2/K9/K10)
          backend AD2/AD3 projeksiyonuna ertelendi (proje sahibi onayı 2026-06-06).
          Bu task o projeksiyon boşluklarını full-stack kapatır (backend DTO + frontend render).
  Kabul kriterleri:
    - K10: AD3 MULTI_ACCOUNT flagDetail.supportingSignals (IP_ADDRESS / DEVICE_FINGERPRINT /
      SOURCE_ADDRESS — type, value, linkedAccounts[]) backend DTO'da projekte edilir ve S14'te
      gösterilir (07 §9.3:1742; veri zaten FraudFlag.Details JSON'unda mevcut)
    - K9: AD3 hesap-flag "Aktif İşlemler" — flag'lenen kullanıcının aktif (terminal-olmayan)
      işlem sayısı + listesi backend DTO'da projekte edilir ve S14'te gösterilir (04 §8.3 madde 4)
    - K2: AD2 hesap-flag liste kolonları — Sinyal Detayı / İlişkili Hesaplar / Aktif İşlem Sayısı
      backend liste projeksiyonunda doldurulur (yalnız ACCOUNT_LEVEL) ve S13 hesap-varyant
      tablosunda gösterilir (04 §8.2)
    - Aktif işlem tanımı AD19d (07 §9.22a) predikatıyla tutarlı: her iki taraf, 5 terminal-state
      hariç (FLAGGED aktif sayılır)
  Test beklentisi: Backend integration (Fraud) — supportingSignals roundtrip, AD3 activeTransactions
    sayı+liste, AD2 hesap-flag alanları (signalSummary/linkedAccountCount/activeTransactionCount)
  Doğrulama kontrol listesi:
    - [ ] 07 §9.3 MULTI_ACCOUNT supportingSignals kontratı backend↔frontend birebir mi?
    - [ ] 04 §8.3 hesap-varyant "Aktif İşlemler" (sayı+liste) gösteriliyor mu?
    - [ ] 04 §8.2 hesap-flag tablo kolonları (Sinyal/İlişkili/Aktif) dolu mu?
```

```
Task T101: Admin İşlem listesi + detay (S15, S16)
  Bağımlılık: T85
  Dokümanlar: 04 §8.4–§8.5
  Kabul kriterleri:
    - S15 İşlem listesi: filtre (durum, tarih, kullanıcı, tutar, stablecoin), sayfalama
    - S16 İşlem detay (admin): durum geçmişi timeline, ödeme/payout/refund detayları, admin aksiyonlar (iptal, hold)
    - Admin iptal modal'ı, emergency hold modal'ı
    - GET /admin/transactions, GET /admin/transactions/:id çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.4–§8.5 tüm bileşenler ve aksiyonlar var mı?
```

```
Task T102: Admin Parametre yönetimi (S17)
  Bağımlılık: T85
  Dokümanlar: 04 §8.6
  Kabul kriterleri:
    - Parametre grupları: timeout, komisyon, işlem limitleri, iptal kuralları, yeni hesap, gas fee, fraud, alıcı belirleme, erişim/uyumluluk, blockchain health
    - Inline edit: düzenle → kaydet/iptal
    - Etki kapsamı bilgi kutusu (yeni işlem vs. runtime)
    - GET /admin/settings, PUT /admin/settings/:key çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.6 tüm parametre grupları var mı?
```

```
Task T103: Admin Steam hesapları (S18)
  Bağımlılık: T85
  Dokümanlar: 04 §8.7
  Kabul kriterleri:
    - Hesap kartları: Steam ID, durum (aktif/kısıtlı/banned), emanet sayısı, günlük trade, son kontrol
    - State'ler: aktif (yeşil), kısıtlı (turuncu + banner + emanet listesi), banned (kırmızı + acil uyarı)
    - Recovery queue: işlem ID, item, taraflar, state, recovery durumu, sorumlu admin, not
    - GET /admin/steam-accounts çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.7 tüm state'ler ve recovery queue var mı?
```

```
Task T104: Admin Rol & yetki yönetimi (S19)
  Bağımlılık: T85
  Dokümanlar: 04 §8.8
  Kabul kriterleri:
    - Roller listesi tablosu: ad, açıklama, atanmış kullanıcı, aksiyonlar
    - Yetki matrisi: 11 yetki checkbox listesi
    - Yeni rol oluştur modal'ı
    - Kullanıcı-rol atama (dropdown)
    - GET /admin/roles, POST/PUT/DELETE roles çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.8 tüm bileşenler var mı?
```

```
Task T105: Admin Kullanıcı detay (S20)
  Bağımlılık: T85
  Dokümanlar: 04 §8.9
  Kabul kriterleri:
    - Profil bilgileri: avatar, ad, Steam ID, hesap yaşı, durum badge'leri
    - İstatistikler kartı: toplam işlem, başarı/iptal/flag sayıları, hacim, son işlem
    - Cüzdan adresi geçmişi (mevcut + önceki, tarihlerle)
    - Alıcı-satıcı ilişkileri tablosu
    - GET /admin/users/:steamId çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.9 tüm bileşenler var mı?
```

```
Task T105a: Hesap askıya alma (backend + S03d) [PLAN DÜZELTMESİ — T100 sonrası eklendi]
  Bağımlılık: T54 (flag review), T37 (notification dispatcher), T32 (/auth/me)
  Dokümanlar: 02 §14.0 + §16.2, 03 §2.1 + §8.3, 04 §8.3 + §16.2 + S03d
  Gerekçe: Hesap askıya alma 02/03/04'te tarif edilir ama F5'te hiçbir task'a
    atanmamıştı (plan boşluğu). T100 S14 "Askıya Al" butonunu bu task'a devretti.
    Proje sahibi onayı (2026-06-05): ayrı adanmış task; enforcement = kısıtlı oturum.
  Kabul kriterleri:
    - User suspension state: IsSuspended/SuspendedAt/SuspensionReason/SuspensionExpiresAt + migration
    - POST /admin/users/:userId/suspend (reason ≥10, durationDays null=kalıcı / N=geçici) — MANAGE_FLAGS
    - DELETE /admin/users/:userId/suspend (unsuspend) — MANAGE_FLAGS
    - Enforcement: kısıtlı oturum — login serbest, fon-akışı mutation'ları (işlem
      oluştur/kabul/iptal + cüzdan/ayar) suspended kullanıcıyı reddeder; read'ler serbest;
      /auth/me isSuspended flag'i
    - Geçici blok auto-unsuspend Hangfire job'u (SuspensionExpiresAt geçince kaldırır)
    - ACCOUNT_SUSPENDED/ACCOUNT_UNSUSPENDED bildirim (kullanıcıyı bilgilendir) + USER_BANNED/USER_UNBANNED audit
    - S03d /auth/suspended ekranı + /auth/me isSuspended → SuspendedHeader (kısıtlı oturum)
  Test beklentisi: Suspend/unsuspend + enforcement + auto-unsuspend integration testleri
  Doğrulama kontrol listesi:
    - [ ] 02 §14.0/§16.2 askıya alma kuralları + 03 §2.1 kısıtlı oturum + 04 S03d karşılandı mı?
  Notlar: SignalR canlı force-restrict ertelendi (sonraki istek/login suspended'ı algılar).
    S14 "Askıya Al" buton wiring'i T100 merge sonrası ayrı küçük adımda eklenir.
```

```
Task T105b: Kullanıcı detay backend tamamlama (S20 — wallet history + reputation breakdown)
  [PLAN DÜZELTMESİ — T105 validator sonrası eklendi (2026-06-08, proje sahibi onayı).
   Plan T105'i "salt frontend" sanıyordu; AD16 backend boşlukları validator'da doğrulandı.]
  Bağımlılık: T105, T34 (cüzdan yönetimi), T54 (profil/reputation)
  Dokümanlar: 04 §8.9.1 + §8.9.3, 06 §3.1, 07 §9.16
  Kabul kriterleri:
    - WalletAddressHistory entity + migration; payout/refund adres değişimi tarihçeye yazılır
    - AD16 walletHistory[] geçmiş adresleri döndürür (current=false + setAt) → §8.9.3 "önceki adresler (tarihlerle)"
    - Reputation breakdown DTO: tamamlanan işlem sayısı + başarı oranı % + iptal oranı % (04 §7.4.2 deseni)
    - AD16 profile breakdown alanlarını expose eder; FE UserProfileCard breakdown'u render eder
  Test beklentisi: walletHistory geçmiş + reputation breakdown integration testleri
  Doğrulama kontrol listesi:
    - [ ] 04 §8.9.3 önceki adresler (tarihlerle) + §8.9.1 reputation breakdown karşılandı mı?
```

```
Task T103b: Steam hesapları backend tamamlama (S18 — emanet item listesi + Recovery Queue) — ⬚ ERTELENDİ
  [PLAN DÜZELTMESİ — T103 deferral'ı incelendi (2026-06-13, proje sahibi onayı = Option C "ertele + belgele").
   T103 raporu bu boşluğu "→ T69 forward" diyordu; ancak T69 (PR #110) MERGED ve admin projeksiyonunu bağlamadı.
   Kod incelemesi (2026-06-13) gösterdi ki bu bir "salt-UI gap-fill" DEĞİL:
     - Transaction.EscrowBotId hiçbir yerde atanmıyor (SqlBotSelectionService.SelectAsync'in çağıranı yok) → emanet→bot bağı kurulmamış.
     - PlatformSteamBot.ActiveEscrowCount hiç artırılmıyor (ölü sayaç; kartta "Emanet: N" pratikte 0).
     - RestrictionReason / FailoverStatus / recovery queue domain'i (recoveryStatus/responsibleAdmin/adminNote) HİÇ kurulmadı; T69 failover'ı bilinçli K-list'e attı.
     - Tek gerçek bot↔işlem bağı: TradeOffer.PlatformSteamBotId (webhook handler doldurur) — dolaylı.]
  Bağımlılık: T64–T69 (Steam bot + escrow akışı wiring), recovery/failover feature spec
  Dokümanlar: 04 §8.7 (S18 emanet listesi + Recovery Queue), 02 §15, 03 §11.2a, 07 §9.10
  Ertelenme ön-koşulları: (a) escrow akışına bot-atama wiring'i (SelectAsync çağrısı + EscrowBotId persist + ActiveEscrowCount artırımı); (b) recovery/failover feature spec'i (discovery turu olası).
  Kapsam (açıldığında): kısıtlı/banned hesabın emanet item listesi + Recovery Queue satır verisi + MANAGE_STEAM_RECOVERY aksiyonları (Manual Recovery / not / sorumlu admin).
  Not: T103 (S18 UI) zaten 4/4 PASS — owner-onaylı boş/structural Recovery Queue + emanet sayısı kalır; bu erteleme F5 Gate Check'i BLOKLAMAZ.
  GÜNCELLEME 2026-06-13: ön-koşul (a) escrow→bot wiring = T106a (Escrow Trade-Offer Dispatch Engine) ile KARŞILANDI.
    Kalan = T103b-2 (recovery/failover discovery + spec) / T103b-3 (recovery queue domain + MANAGE_STEAM_RECOVERY + emanet item listesi impl).
  GÜNCELLEME 2026-06-13 (T103b-2): owner kararı "tasarla+uygula birleşik" → T103b-2 + T103b-3 TEK task'ta uygulandı.
    Yapıldı: BotRecoveryItem entity + migration; SteamWebhookHandler RESTRICTED/BANNED tetik → BotRestrictedEvent;
    BotRestrictionRecoveryConsumer (eager materyalize + otomatik EMERGENCY_HOLD, idempotent); AdminSteamBotQueryService
    canlı RestrictionReason/FailoverStatus/RecoveryTransactionCount; AD25 GET recovery-queue (VIEW_STEAM_ACCOUNTS) +
    AD26 PATCH (MANAGE_STEAM_RECOVERY enforcement); FE recovery kuyruğu + aksiyonlar + i18n. Tetik = RESTRICTED+BANNED
    (OFFLINE geçici). Rapor Docs/TASK_REPORTS/T103b-2_REPORT.md. Durum: yapım bitti, bağımsız validator bekliyor.
```

```
Task T106: Admin Audit log (S21)
  Bağımlılık: T85
  Dokümanlar: 04 §8.10
  Kabul kriterleri:
    - Filtre formu: kategori, tarih, kullanıcı, işlem ID
    - Log tablosu: kategori, aksiyon, aktör, konu, işlem ID, detay, tarih
    - State'ler: log var, filtre sonucu boş, yükleniyor
    - GET /admin/audit-logs çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.10 tüm bileşenler ve state'ler var mı?
```

```
Task T106a: Escrow Trade-Offer Dispatch Engine (T69-K1 resmileştirme)
  [PLAN EKLEMESİ 2026-06-13 — "task 103b" yeniden ele alınırken keşfedildi: T103b ön-koşul (a)
   "escrow→bot wiring" aslında T69-K1 dispatch caller'ıdır (T69 raporu 2026-05-16'da "plan'da
   ayrı task olarak tanımsız, T-future devir" diye proje sahibi onayıyla ertelemişti). Doğrulama:
   SendTradeOfferToSeller/ToBuyer hiç fire edilmiyor, sidecar /trade-offers/send'i çağıran client
   yok, trade_offer.accepted asset-id taşımıyor → escrow happy-path motoru tümüyle kurulmamış.
   Owner kararı (3-tur AskUserQuestion 2026-06-13): tanımla+tasarla+uygula; kapsam = 3 yön +
   sidecar değişiklikleri dahil; tetikleyici = Hangfire per-minute scan.]
  Bağımlılık: T64–T69 (Steam bot pipeline), T73 (dispatch job deseni)
  Dokümanlar: 05 §3.2, 06 §3.9/§3.10, 08 §2.4/§2.7
  Kabul kriterleri:
    - Escrow bacağı: ACCEPTED → SelectAsync bot seç + EscrowBotId persist + SendTradeOfferToSeller fire + SELLER_TO_BOT POST (botAccountName hint, atomik)
    - Delivery bacağı: PAYMENT_RECEIVED → escrow botunu yeniden kullan + SendTradeOfferToBuyer fire + BOT_TO_BUYER POST
    - Refund bacağı: ItemRefundToSellerRequestedEvent tüketicisi → BOT_TO_SELLER_REFUND dispatch
    - Asset-id yakalama (sidecar getExchangeDetails → trade_offer.accepted) → EscrowBotAssetId/DeliveredBuyerAssetId; guard'lar geçer, sessiz takılma yok
    - ActiveEscrowCount: +1 ITEM_ESCROWED, −1 delivery & refund accepted; negatif olmaz; yalnız backend yazar
    - Transient retry (sidecar unavailable) / kalıcı hata → TradeOfferDispatchFailedEvent; idempotency (state-flip + yön-bazlı TradeOffer satır varlığı)
  Test beklentisi: dispatch job + webhook handler + dispatch client + refund consumer + sidecar (hint, asset-id) testleri
  Doğrulama kontrol listesi:
    - [ ] 3 yön de uçtan uca dispatch ediliyor + asset-id yakalama + sayaç yaşam döngüsü doğru mu?
  Not: T103b ön-koşul (a)'yı kapatır. (b) recovery/failover spec + recovery queue = T103b-2/-3 ertelenmiş kalır.
```

---

### F6 — Uçtan Uca Doğrulama (T107–T114)

```
Task T107: E2E — Happy path (tam escrow akışı)
  Bağımlılık: F5 tamamlanmış
  Dokümanlar: 03 §2–§3
  Kabul kriterleri:
    - Satıcı giriş → işlem oluştur → alıcı kabul → item emanet → ödeme → doğrulama → teslim → payout → COMPLETED
    - Tüm bildirimler doğru tetikleniyor
    - Tüm state geçişleri UI'da doğru gösteriliyor
  Test beklentisi: E2E — Playwright (staging)
  Doğrulama kontrol listesi:
    - [ ] 03 §2–§3 tüm happy path adımları çalışıyor mu?
```

```
Task T108: E2E — İptal senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §2.5, §3.3
  Kabul kriterleri:
    - Satıcı iptali (ödeme öncesi)
    - Alıcı iptali (ödeme öncesi)
    - Admin iptali
    - Her senaryoda doğru iade + bildirim
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Her iptal senaryosunda iade doğru mu?
```

```
Task T109: E2E — Timeout senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §4
  Kabul kriterleri:
    - Kabul timeout, trade offer timeout, ödeme timeout, teslim timeout
    - Her senaryoda doğru iade tetikleme + bildirim
    - Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası)
  Test beklentisi: E2E (kısa timeout ile)
  Doğrulama kontrol listesi:
    - [ ] 03 §4 tüm timeout senaryoları çalışıyor mu?
```

```
Task T110: E2E — Ödeme edge case'ler
  Bağımlılık: T107
  Dokümanlar: 03 §5
  Kabul kriterleri:
    - Eksik tutar → iade
    - Fazla tutar → kabul + fazla iade
    - Yanlış token → iade
    - Gecikmeli ödeme → iade
    - Çoklu ödeme → ilk kabul, sonraki iade
  Test beklentisi: E2E (testnet)
  Doğrulama kontrol listesi:
    - [ ] 03 §5 tüm edge case'ler çalışıyor mu?
```

```
Task T111: E2E — Fraud/flag senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §7–§8
  Kabul kriterleri:
    - Fiyat sapması → flag → admin onay/red
    - Yüksek hacim → flag
    - Hesap flag'i → fon akışı engeli
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Flag akışı uçtan uca çalışıyor mu?
```

```
Task T112: E2E — Emergency hold
  Bağımlılık: T107
  Dokümanlar: 03 §8.8
  Kabul kriterleri:
    - Hold uygulama → timeout durur → resume → devam
    - Hold uygulama → cancel (ITEM_DELIVERED hariç)
    - ITEM_DELIVERED'da hold → sadece resume
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Hold/resume/cancel akışları doğru mu?
```

```
Task T113: E2E — Admin akışları
  Bağımlılık: T107
  Dokümanlar: 03 §8
  Kabul kriterleri:
    - Admin giriş ve dashboard
    - Flag inceleme ve onay/red
    - İşlem listesi ve detay
    - Parametre değişikliği
    - Rol yönetimi
    - Audit log görüntüleme
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Admin paneli tüm akışları çalışıyor mu?
```

```
Task T114: E2E — Downtime ve bakım senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §11
  Kabul kriterleri:
    - Platform bakımı: timeout dondurma, bakım banner, resume sonrası devam
    - Steam kesintisi: timeout dondurma, bildirim
    - Blockchain degradasyonu: ödeme timeout dondurma
  Test beklentisi: E2E (simüle)
  Doğrulama kontrol listesi:
    - [ ] Downtime senaryolarında freeze/resume doğru mu?
```

---

### F7 — P2P Geçişi (T115–T139)

Sıra: P0 → P1 → P2 → P2.5 → P3 → P4 → P5 → P6 → P7. T137 (`sidecar-fake`) P2 ile paralel başlayabilir; tüm E2E'yi bloklar. **T139** P7'ye eklendi ama FE görevlerine bağlı değildir (backend borcu, kaynağı T133b doğrulaması) — T134–T136 ile paralel koşabilir; sırayı bekletmesi gereken tek şey kendi doğrulamasıdır.

> **E2E ağı karanlıkta — T137 paralel başlatılıyor, T137a eklendi (T129 doğrulaması tur 3, 2026-08-17; proje sahibi onaylı).** Sekiz advisory E2E leg'in **hepsi**, T117'nin custody emekliliğinden beri kırmızı: `e2e/src/db.ts` harness setup'ı emekli `PlatformSteamBots` tablosuna dört atıf yapıyor (satır 97 · 102 · 131 · 284) ve legler spec'lere **hiç ulaşmadan** ölüyor — imza 8/8 leg'de leg başına **tam 1** `Invalid object name` izi. Legler bilinçli olarak `continue-on-error` + `ci-gate.needs` dışında (proje sahibi kararı, `ci.yml:612-625`), dolayısıyla hiçbir CI Gate'i kırmadılar ve dört görev boyunca (T126 · T127 · T128 · T129) "T129 kaynaklı değil" diye doğru şekilde kayda geçirilip **hiç kapatılmadılar**. Sonuç: F7'nin **para hareket ettiren** görevleri uçtan uca ağ hiç çalışmazken iniyor ve sırada gelen T130/T131 dispute/iade bacaklarıdır. Karar: (a) **T138 öne ALINMAZ** — bağımlılığı T135 gerçek, hareketli FE'ye karşı yazılan spec iki kez yazılır; (b) **T137 P5 ile paralel başlatılır** — planın kendi notunun gereği, tek bağımlılığı T120 zaten kapandı; (c) yeni **T137a** ölçüm görevi harness'i onarıp ağın ne kadarının ayakta olduğunu ölçer ve T138'in "9 spec" tahminini gerçek sayıyla değiştirir. **KALICI DERS:** advisory bir sinyal, "bloke etmediği" için değil "kimsenin sahibi olmadığı" için ölür; her turda doğru şekilde "benim kaynaklı değil" demek onu kapatmaz — bir sahibi ve bir kapatma tarihi olmalıdır.

> **T137a ölçümü — yukarıdaki tanının iki düzeltmesi (2026-08-17, rapor [`T137a_REPORT.md`](TASK_REPORTS/T137a_REPORT.md), CI run [`32050987594`](https://github.com/turkerurganci/Skinora/actions/runs/32050987594)).** (1) **Atıf sayısı dört değil sekiz:** `TradeOffers` de aynı T117 migration'ında düşmüş ve harness iki yerde ona bakıyordu (cleanup satırı + `pollRefundOfferAccepted`); ayrıca T117 deadline kolonlarını faz-koruyarak yeniden adlandırmış (`TradeOfferToSellerDeadline → SellerConfirmDeadline`, `TradeOfferToBuyerDeadline → DeliveryDeadline`) ama harness'ın allow-list'i eski adları taşıyordu — tabloların arkasındaki ikinci duvar. (2) **"Spec'lere hiç ulaşmadan ölüyor" yanlıştı:** spec'ler koşuyor. SQL Server ad-hoc batch'in adlarını **compile anında** çözümlediği için tek bilinmeyen tablo cleanup'ın **tamamını** no-op'a çeviriyordu; ilk test bot INSERT'ünde `Invalid object name`, sonraki testler cleanup çalışmadığından `PK_Users` duplicate'iyle düşüyordu — "leg başına tam 1 iz" bunun sonucu, "hiç başlamadı" imzası değil (T113 leg'i o hâlde bile 3 test geçiriyordu). **Ölçüm sonucu:** harness onarıldıktan sonra 32 testin **10'u geçiyor** (öncesi 3), kalan 22'si custody durumlarında (`ITEM_ESCROWED` / `TRADE_OFFER_SENT_TO_*`) takılıyor — yani ağ artık "karanlık" değil, **P2P akışına göre yeniden yazılmayı bekliyor**. **KALICI DERS (yukarıdakinin üstüne):** ölü bir sinyal teşhis edilirken de sahipsizdir — dört tur boyunca "benim kaynaklı değil" diyen notlar aynı zamanda **yanlış** bir mekanizma tespitini de dört tur taşıdı; sinyali kapatan tur, tanıyı da sıfırdan doğrulamak zorundadır.

> **T117 doğrulaması sonrası düzeltmeler (2026-08-09):** P1'e **T119a** eklendi — 07 §7.6 accept ucunun v3.0 alanlarını (`steamTradeUrl` → `BuyerTradeUrl`, MA kontrolü) üstlenen görev listede yoktu. **T124**'e teslimat-timeout kapısı kabul kriteri eklendi — 05 §4.4 iptalden önce doğrulama turu şart koşuyor ama o tur T127'de, zincir T124'ü öne zorluyor.

> **T119 denetimi sonrası düzeltmeler (2026-08-10):** **T123/T124**'e timeout SystemSetting adlandırma kararı, **T129**'a `REFUNDED` itibar kararı kabul kriteri olarak eklendi (ikisi de aşağıda). İki açık DEFERRED_BACKLOG §9'a düştü (`P2P-NonDeliveryAbuseWindow`, `P2P-DeliveryTimeoutWarning`) — teslimat fazı satıcıya devredildi ama fazın **yaptırım** ve **uyarı** bacaklarının F7'de sahibi yok.

> **T119 doğrulaması sonrası düzeltmeler (2026-08-10):** **T133a** kapsamı 03 + 07'den **03 + 04 + 07**'ye genişletildi. 04, v4.0'da P2P'ye çekilmiş olmasına rağmen custodial kalıntı taşıyor ve hiçbir görevin kapsamında değildi — oysa T134/T135/T136 onu ekran spesifikasyonu olarak okuyacak. §16'nın iki timeout satırı (sorumluluğu tersine yazıyordu) doğrulama turunda düzeltildi (**04 v4.1**); geri kalanı T133a'ya bırakıldı.

```
--- P0: Doküman ---

Task T115: Docs P2P geçişi (02/03/04/05/06/07/08/10/11)
  Bağımlılık: —
  Kabul kriterleri:
    - Dokümanlar arası tutarlılık (GUARDRAILS §5), belirsiz ifade yok
    - Traceability matrisleri güncel
    - Kaldırılan bölümlerin numaraları korunmuş (referans kayması yok)

Task T116: DEFERRED_BACKLOG kayıtları
  Bağımlılık: T115
  Kabul kriterleri:
    - Bot-custody kodu için arşiv işaretçisi (commit sha)
    - DeliveryPollingJob, float/inspect doğrulaması, satıcı borç defteri satırları

--- P1: Domain çekirdeği ---

Task T117: Enum + Transaction alanları + migration [RİSKLİ]
  Bağımlılık: T115
  Dokümanlar: 06 §2.1, §2.24, §3.5
  Kabul kriterleri:
    - TransactionStatus/Trigger/TimeoutPhase yeniden tanımlandı
    - DeliveryEvidence enum'u eklendi
    - Teslimat doğrulama alanları + deadline rename
    - Tek forward migration temiz DB'ye uygulanıyor, snapshot regenerate
  Test beklentisi: Unit (enum parity), migration rehearsal

Task T118: TransactionStateMachine yeniden yazımı [RİSKLİ]
  Bağımlılık: T117
  Dokümanlar: 05 §4.2
  Kabul kriterleri:
    - 05 §4.2'deki her geçişin geçen bir testi var
    - Hiçbir test emekli status'e referans vermiyor
    - ApplyEmergencyHold PAYMENT_RECEIVED + DeliveryDeadline dalını içeriyor
  Test beklentisi: Unit (geçiş tablosu tam kapsam)

Task T119: Reputation + cooldown sorumluluk eşlemesi
  Bağımlılık: T118
  Dokümanlar: 02 §3.1, §13
  Kabul kriterleri:
    - PAYMENT_RECEIVED timeout'u SATICI'ya atfediliyor
    - ACCEPTED timeout'u satıcıya, SELLER_CONFIRMED alıcıya
  Not: Kod karşılığı T117 dalında yazıldı (ReputationAggregator +
       CancelCooldownEvaluator). T119'a kalan iş, 02 §3.1 karşısında
       bağımsız kapsam denetimi.

Task T119a: POST /transactions/:id/accept — v3.0 alanları
  Bağımlılık: T118
  Dokümanlar: 07 §7.6, 06 §3.5, 02 §2.2, 08 §2.2
  Kabul kriterleri:
    - steamTradeUrl zorunlu alan olarak isteğe eklendi; format doğrulaması
      (partner + token ayrıştırılabilmeli) başarısızsa 400 INVALID_TRADE_URL
    - Değer Transaction.BuyerTradeUrl'e yazılıyor (06 §3.5: ACCEPTED ve
      sonrasında NOT NULL)
    - Alıcının Mobile Authenticator'ı doğrulanıyor; hold süresi 0 değilse
      403 MOBILE_AUTHENTICATOR_REQUIRED
  Test beklentisi: Integration (kabul akışı), Unit (trade URL parse)
  Neden ayrı task (T117 doğrulaması, 2026-08-09): 07 §7.6 bu üç maddeyi
       v3.0'da zorunlu kıldı ama F7 listesinde accept ucunu üstlenen görev
       yoktu. T117 kolonu ekledi, yazan kod yok — kapanmazsa BuyerTradeUrl
       kalıcı NULL kalır ve PAYMENT_RECEIVED'da satıcıya gösterilen trade
       CTA'sı boş döner (TransactionDetailService). P2P akışının çekirdeği.

--- P2: Envanter okuma yolu ---

Task T120: Sidecar envanter — cache bypass + ayrı limiter + visibility
  Bağımlılık: T115
  Dokümanlar: 08 §2.3, §2.6
  Kabul kriterleri:
    - refresh parametresi cache'i atlıyor
    - Community ucu için Web API'den ayrı kuyruk
    - Yanıt görünürlüğü Public/Private/Unavailable olarak ayrıştırıyor
  Test beklentisi: vitest

Task T121: Backend envanter portu — üç değerli visibility [RİSKLİ]
  Bağımlılık: T120
  Dokümanlar: 08 §2.3
  Kabul kriterleri:
    - private != unavailable != boş, port seviyesinde gözlenebilir
    - Mevcut null'a çöktürme davranışı kaldırıldı (money-safety)

--- P2.5: Gerçek Steam probu ---

Task T122: Gerçek Steam ölçümü (spike, kod teslimi yok) [RİSKLİ]
  Bağımlılık: T121
  Dokümanlar: 02 §9.2 (v3.1), 08 §2.3, §2.6
  Çıktı: INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md
  Kabul kriterleri (2026-08-13'te revize edildi — aşağıdaki KAPSAM BÖLÜNMESİ):
    - Anonim envanter ucunun statü kümesi ve gövde şekli canlı Steam'e karşı
      ölçüldü; 08 §2.3'ün üç durumuyla eşleşmeyen taraf kayda geçti
    - `classid`/`instanceid`'nin bir item'ı TANIMLAMADIĞI ölçümle gösterildi ve
      02 §9.2'nin sayım tabanlı kanıt kararı buna dayandırıldı
    - Trade Protection'ın ANONİM görünümdeki okunabilirliği ölçüldü (kilit bitiş
      tarihi okunamıyor; `tradable` sınıf düzeyinde)
    - Community rate limit davranışı ölçüldü ve T120'nin 10/dk varsayılanı bu
      ölçüme göre değerlendirildi
    - Ölçüm kaydı Docs/INTEGRATION_RUNBOOKS/'a yazıldı; ham yanıtlardan üçüncü
      şahıs verisi içermeyeni repoya commit edildi
    - 02 §9.2 kanıt kuralı teyit/revize edildi
    - Trade olmadan ölçülemeyen kalan T125'in TASARIMINDAN İZOLE edildi ve
      izolasyon T125 kabul kriterlerine yazıldı
  KAPSAM BÖLÜNMESİ (proje sahibi kararı, 2026-08-13): bu görevin ilk tanımı
       ölçümü İKİ GERÇEK STEAM HESABI ARASINDA BİR TRADE'e bağlıyordu. Proje
       sahibi bu ölçümü yapamayacağını bildirdi — `task.md` Adım 4 anlamında
       kırık dış varsayım. BLOCKED yerine bölünme onaylandı: T122-A trade
       GEREKTİRMEYEN her şey (yapıldı) · T122-B sahibin kendi oturumundan tek
       capture (yapıldı, sonuç kısmi) · T122-C ölçülemeyeni T125'ten izole
       etmek (yapıldı). Yukarıdaki kriterler bölünme sonrası GERÇEKLEŞEN
       kapsamdır; ilk tanımın trade'e bağlı kısmı aşağıda.
       Ders: dört kriterin ÜÇÜ trade gerektirmiyordu — tanım ölçümü tek bir
       yönteme bağladığı için yöntem düşünce görevin tamamı düşmüş göründü.
  ÖLÇÜLEMEYEN (kapanışı T125 launch kapısına devredildi): teslimat gecikmesi ·
       `assetid` rotasyonunun ölçümle teyidi (ikincil kaynak var:
       `steam-tradeoffer-manager.d.ts:27-31`) · `Item Certificate`'in trade'i
       hayatta kalması + cooldown'un anonim imzası. T125'i bloklamıyorlar çünkü
       MANTIĞI değil SABİTLERİ belirliyorlar; izolasyon runbook §7 + T125
       kabul kriterleri. Bu yüzden delivery timeout VARSAYILANI bu görevde
       teyit/revize EDİLMEDİ: sayı T124/T125'te config'den gelir ve
       DEPLOY_RUNBOOK §A#6 örneği (60 dk) ölçüm gelene kadar bağlayıcı değildir.
  Not: sidecar-fake bu davranışı kanıtlayamaz — fake ne yazarsak onu döner.
       Ölçüm bunu doğruladı ve genişletti: dönen dört statüden ikisi (`401`,
       `429`) sidecar-fake'te hiç modellenmemiş.

--- P3: Yeni ileri yol ---

Task T123: SELLER_CONFIRMED + POST /transactions/:id/confirm-ready
  Bağımlılık: T118, T121
  Dokümanlar: 07 §7.6a, 03 §2.3
  Kabul kriterleri:
    - Item envanterden çıkmış satıcı ITEM_NO_LONGER_AVAILABLE alıyor
    - Alıcı MA kontrolü yapılıyor
    - Baseline yazılıyor; alıcı envanteri gizliyse işlem bloklanmıyor
    - Ödeme adresi ancak bu adımdan sonra ifşa ediliyor. Kapı `SellerReadyConfirmedAt`
      damgasıdır, status kümesi DEĞİL (adres oluşturmada tahsis edilir; CREATED'dan
      iptal olmuş bir işlemde de vardır ama penceresi hiç açılmamıştır)
    - Envanter okumaları ÖNBELLEKSİZ (08 §2.3 `refresh`): hem item kontrolü hem
      baseline. Sidecar bayrağı T120'den beri var ama backend hiç göndermiyordu
    - Satıcı envanteri GİZLİ ise `ITEM_NO_LONGER_AVAILABLE` DEĞİL, 422
      `INVENTORY_PRIVATE` döner (T121 üç değerli ayrımı; 07 §7.6a'ya eklendi)
    - Baseline okunamazsa üç `BuyerBaseline*` kolonu da NULL kalır (sıfır YAZILMAZ)
    - SellerConfirmDeadline'ı besleyen SystemSetting'e karar verildi (aşağıdaki
      not) ve seçilen anahtarın açıklaması/etiketi v3.0 fazını anlatıyor
    - SellerConfirmDeadline FİİLEN ARMLANIYOR (kabul geçişinde) — aşağıdaki
      plan boşluğu notu
  KARAR (proje sahibi, 2026-08-13) — adlandırma: seçenek (a) UYGULANDI.
       `trade_offer_seller_timeout_minutes` → `seller_confirm_timeout_minutes`,
       `trade_offer_buyer_timeout_minutes` → `delivery_timeout_minutes`.
       Kapsam: seed + `SystemSettingsCatalog` + migration `T123_RenameTimeoutSettings`
       (satır Id'leri sabit → `UpdateData`, admin değerleri korunur) +
       `SKINORA_SETTING_*` env adları (.env.example, docker-compose.yml,
       docker-compose.e2e.yml, `SettingsBootstrapTests`) + 4 dil i18n +
       06 §8 + 04 §16 + DEPLOY_RUNBOOK §A.
  PLAN BOŞLUĞU (T123 yapımında bulundu, proje sahibi onayıyla bu göreve alındı):
       `SellerConfirmDeadline`'ı YAZAN kod hiç yoktu. Tüm okuyucular vardı
       (`DeadlineScannerJob` ACCEPTED dalı, `TimeoutFreezeService`,
       `CountdownSyncBroadcaster`, detay/liste timeout blokları) ama hiçbiri
       ateşlenemiyordu: `AcceptDeadline` oluşturmada, `DeliveryDeadline` T124'te
       armlanıyor, aradaki ACCEPTED fazı sahipsizdi. Sonuç, kabul edip susan
       satıcıya HİÇBİR zaman sınırı olmamasıydı. Kabul geçişinde armlandı
       (`TransactionAcceptanceService`); rename edilen anahtarın ilk üretim
       tüketicisi de budur — aksi hâlde T119'un şikâyet ettiği "üretimde hiç
       okunmayan ayar" durumu adı düzelmiş hâlde sürerdi.
  Not (T119 denetimi, 2026-08-10 — KAPANDI): iki timeout ayarı custodial
       adındaydı ve ikisi de üretimde hiç okunmuyordu; rename maliyeti bu yüzden
       en düşük seviyedeydi. Gerekçe: admin panelinde satıcının teslimat
       penceresini "Alıcı trade offer timeout süresi" adlı bir kutu yönetiyordu —
       T119'un düzelttiği sorumluluk çevirmesinin admin yüzündeki kalıntısı.
  Test beklentisi: Integration (confirm-ready üç kapı + baseline + payment
       penceresi), Unit (sidecar refresh bayrağı, sınıf bazında baseline sayımı)

Task T124: ConfirmPayment yeniden bağlanması + DeliveryDeadline
  Bağımlılık: T123
  Kabul kriterleri:
    - AmountValidationService SELLER_CONFIRMED -> PAYMENT_RECEIVED
    - DeliveryDeadline armlanıyor ve zamanında ateşleniyor; süreyi besleyen
      SystemSetting **`delivery_timeout_minutes`**'tır (T123 adlandırma kararı
      uygulandı; eski adı `trade_offer_buyer_timeout_minutes` idi ve SATICININ
      penceresini yönetiyordu — T119 denetimi). Değer ölçülmemiştir: launch'ta
      muhafazakâr YÜKSEK tutulur (DEPLOY_RUNBOOK §A #6 uyarısı)
    - DeadlineScannerJob'ın PAYMENT_RECEIVED dalı T127 gelene kadar
      TÜKETMİYOR: süre dolduğunda iptal uygulanmaz, işlem taranabilir
      kalır. Kapı T127'de kaldırılır.
  Not (T117 doğrulaması, 2026-08-09): 05 §4.2/§4.4 teslimat timeout'unda
       iptalden ÖNCE bir doğrulama turu şart koşuyor; o tur T127'de
       yazılıyor ama zincir (T127 -> T125 -> T124) T124'ü öne zorluyor.
       Kapı olmadan, aradaki pencerede item'ı gerçekten göndermiş ama
       alıcısı onay vermemiş satıcının işlemi haksız yere iptal edilir ve
       para alıcıya iade edilir. Sıra değiştirilemediği için koruma kapı.
  Not (T124 yapımı, 2026-08-14): AC1 kod olarak T117'de KARŞILANMIŞTI
       (`82bff4d` yalnız `ITEM_ESCROWED` -> `SELLER_CONFIRMED` rename'i yaptı);
       uçtan uca kanıtı `BlockchainWebhookEndpointTests.PaymentConfirmed_
       ExactAmount_*`. Bu görevin fiilî işi AC2 + AC3'tü. "Yeniden bağlanma"
       başlığı T117 öncesi durumu anlatıyor.
  KARAR (proje sahibi, 2026-08-14) — üç yapım kararı onaylandı:
       (a) Kapı ŞEKLİ: süresi dolmuş PAYMENT_RECEIVED satırları tüketen
           sorgudan çıkarıldı ve AYRI bir salt-okunur sorguyla raporlanıyor.
           Döngü içinde atlamak `DeadlineScannerBatchSize`'ı kalıcı gated
           satırlarla doldurup diğer üç fazın timeout'unu sessizce durdurabilirdi.
       (b) `delivery_timeout_minutes` okunamazsa kod fallback'i 1440 dk (24 sa).
           Startup fail-fast + validator `>0` nedeniyle üretimde ulaşılamaz;
           yön DEPLOY_RUNBOOK §A #6'nın "muhafazakâr YÜKSEK" uyarısından geliyor
           (kısa fallback, T127 sonrası teslim etmiş satıcıyı haksız iptal eder).
       (c) Doküman yansıması: T127'ye kapı kaldırma kriteri + DEPLOY_RUNBOOK
           §A #6 + DEFERRED_BACKLOG `P2P-DeliveryTimeoutWarning` ön koşulu.
  Ara dönem etkisi (T127'ye kadar): deadline armlandığı an detay/liste ekranları
       ve `CountdownSyncBroadcaster` teslimat geri sayımını göstermeye başlar;
       süre dolunca hiçbir şey olmaz, işlem PAYMENT_RECEIVED'da kalır ve scanner
       her taramada uyarı logu üretir. Planın sıra kararının kabul edilmiş sonucu.
  Test beklentisi: Integration (deadline setting'den armlanıyor, Hangfire job
       açılmıyor, yanlış state reddediliyor, gated satır iki taramada da
       tüketilmiyor, gated satırlar batch'i doldurunca diğer fazlar hâlâ
       işleniyor), Unit (ConfirmPayment armlıyor; eksik/multi/hold armlamıyor)

Task T125: DeliveryVerificationService + DeliveryEvidence [ÇOK RİSKLİ]
  Bağımlılık: T122, T124
  Dokümanlar: 02 §9.2 (v3.1), 06 §2.24,
              INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md (T122 ölçümü)
  Kabul kriterleri:
    - 02 §9.2 tuzak matrisinin HER SATIRI için bir test
    - Servis saf/yan etkisiz kanıt değerlendirmesi yapıyor (polling'e hazır)
    - `market_tradable_restriction` kanıt olarak KULLANILMIYOR (T122 B8: bu alan
      kilit göstergesi değil, sınıf politikası — `tradable: 1` olan serbest bir
      item'da da 7 geliyor). Bir test bu alanı okumanın yanlış sonuç verdiğini
      sabitlemeli
    - Kanıt değerlendirmesi item'ın KİLİT DURUMUNA dayanmıyor (T122 B7 ölçülemedi:
      cooldown'un anonim görünümdeki imzası bilinmiyor; `tradable` alanı sınıf
      düzeyinde ve kilit bitiş tarihi anonim okunamıyor — runbook §6)
    - Sayfalama tüketicisi "devam yok"u `more_items`'ın YOKLUĞUNDAN anlıyor,
      `more_items == 0`'dan değil (T122 B9)
    - LAUNCH KAPISI: ilk N gerçek teslimatta alıcı+satıcı envanterinin ham yanıtı
      saklanıyor ve insan incelemesinden geçmeden envanter kanıtına dayalı
      otomatik para bırakma AÇILMIYOR. Kapı DEPLOY_RUNBOOK launch checklist'ine
      bağlanmalı
  Not: Money-safety çekirdeği. Ayrı chat'te bağımsız doğrulama zorunlu.
  Not (T122 ölçümü, 2026-08-13): T122 gerçek trade yapılamadığı için üç bilinmeyeni
       KAPATAMADI — teslimat gecikmesi, `assetid` rotasyonu, `Item Certificate`
       kalıcılığı + cooldown'un anonim imzası. Bunlar T125'i bloklamıyor çünkü
       MANTIĞI değil SABİTLERİ belirliyorlar; izolasyon runbook §7'de beş maddede
       tanımlı ve yukarıdaki AC'ler onun kodlanmış hâlidir. Bu AC'ler kaldırılırsa
       ölçülmemiş varsayımlar sessizce para hareketine bağlanır.

Task T126: POST /transactions/:id/confirm-receipt
  Bağımlılık: T125
  Kabul kriterleri: yalnız alıcı, yalnız PAYMENT_RECEIVED, idempotent
    - LAUNCH KAPISI İNVARİANTI (T125 doğrulama bulgusu F3, 2026-08-14):
      `DeliveryVerificationResult.AutoReleaseGated == true` dönen bir turda
      `Transaction.DeliveryVerifiedAt` DAMGALANMAZ. Gerekçe: kapı yalnız
      `DeliveryVerificationService` katmanında; state machine guard'ı
      `HasDeliveryEvidence()` (`IsSufficientForDelivery() &&
      DeliveryVerifiedAt.HasValue`) kapıdan habersizdir. Çağıran gated bir
      turda hem `Evidence`'ı persist eder hem `DeliveryVerifiedAt`'i
      damgalarsa kapı sessizce atlanır ve envanter çıkarımı insan incelemesi
      olmadan para bırakır (02 §9.2 launch kapısı, DEPLOY_RUNBOOK §H).
      Kapıyı fiilen tutan alan `DeliveryVerifiedAt`'tir. Bir test bunu
      sabitlemeli: kapı kapalı + envanter kanıtı tam → `DeliveryVerifiedAt`
      NULL kalır ve `CanDeliverItem` false döner.
      (Bu uçta `BUYER_CONFIRMED` yolu kapıdan ETKİLENMEZ — alıcının kendi
      onayı platformun çıkarımı değildir; `AutoReleaseGated` zaten false
      döner, yani invariant o yolu kısıtlamaz.)

Task T127: DeadlineScannerJob'a teslimat doğrulama turu
  Bağımlılık: T125
  BAŞLIK DÜZELTMESİ (T124 doğrulaması, 2026-08-14 — proje sahibi onayı):
       başlık `TimeoutExecutor'a ...` idi ve v2.0 kalıntısıydı. Teslimat fazı
       05 §4.4 "Aşama ayrımı" uyarınca scanner-driven'dır; `TimeoutExecutor`
       ilk satırında `Status != SELLER_CONFIRMED` ise no-op eder, yani teslimat
       timeout'unu yapısal olarak tüketemez. Yalnız başlığı okuyan bir yapımcı
       turu yanlış executor'a bağlar ve aşağıdaki kapı hiç kalkmaz — teslimat
       timeout'u kalıcı olarak ölü kalırdı.
  Kabul kriterleri:
    - Kanıt tamsa timeout iptal yerine ITEM_DELIVERED üretiyor
    - SELLER_ASSET_GONE ve delta yoksa dispute'a yükseltiliyor
    - T124 SCANNER KAPISI KALDIRILDI: `DeadlineScannerJob.ReportGatedDelivery
      TimeoutsAsync` silinir ve `PAYMENT_RECEIVED && DeliveryDeadline < now`
      dalı tüketen sorguya geri konur. Kapı T124'te kondu çünkü 05 §4.4 iptalden
      ÖNCE doğrulama turu şart koşuyor ve o tur bu görevdedir; kapı kalkmazsa
      teslimat timeout'u ateşlenmez, süresi dolan işlemler PAYMENT_RECEIVED'da
      birikmeye devam eder (T124 uyarı logu bunları sayar)
      NİHAİ ŞEKİL (yapım + doğrulama sonrası, proje sahibi onayı 2026-08-15):
       dal TÜKETİYOR ama **ayrı sorgu + ayrı tavan** ile; tek sorguya
       dönmüyor. Gerekçe T124 kararı (a)'nın kendisidir: beş verdict'ten üçü
       satırı `PAYMENT_RECEIVED`'da ve süresi KALICI dolmuş bırakır (kapıda
       inceleme bekleyen, dispute'a yükseltilmiş, okunamayan), launch'ta kapı
       kapalı olduğu için birincisi alıcısı onay vermeyen her teslimatın
       BEKLENEN sonucudur, ve bunlar `DeadlineScannerBatchSize`'ı paylaşırsa
       accept / seller-confirm / payment timeout'larını sessizce durdururlar.
       Ek olarak bir tur bir state kontrolü değil iki rate-limited Steam
       okumasıdır (08 §2.2). Kriterin ÖZÜ (kapı kalktı, dal tüketiyor)
       karşılanmıştır; HARFİ (tek sorgu) bilinçli olarak karşılanmamıştır.
    - Kapının kalkışıyla birlikte T124'te ters çevrilen iki test eski
      beklentisine döner: `DeadlineScannerJobTests.Scanner_Does_Not_Consume_
      Overdue_PAYMENT_RECEIVED_Until_T127` ve `DeadlineScannerJobSideEffects
      Tests.Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127`
    - LAUNCH KAPISI İNVARİANTI (T125 doğrulama bulgusu F3, 2026-08-14):
      `InventoryEvidencePendingReview` verdict'i alan bir tur ne ITEM_DELIVERED
      üretir ne de işlemi İPTAL eder — işlem PAYMENT_RECEIVED'da bırakılır ve
      `DeliveryEvidenceCaptureRecorder` ile kayda geçer (DEPLOY_RUNBOOK §H.2).
      Bu turda `DeliveryVerifiedAt` DAMGALANMAZ: state machine guard'ı
      `HasDeliveryEvidence()` kapıdan habersizdir, dolayısıyla kapıyı fiilen
      tutan tek alan odur (T126'daki aynı madde ile ikiz). İptal edilmemesi
      ayrıca zorunludur çünkü kanıt item'ın ULAŞTIĞINI söylüyor — iptal,
      teslim etmiş satıcıyı haksız yere kaybettirirdi
    - ÖN KOŞUL — FREEZE/RESUME FAZ KAYMASI KAPATILDI (kapı kalkmadan ÖNCE):
      `DeliveryDeadline`'a ödeme fazının artığının yazılabildiği yol kapatılmış
      ve bir testle sabitlenmiş olmalı. Zincir bugün ulaşılabilir: işlem
      SELLER_CONFIRMED iken bulk freeze (`PLATFORM_MAINTENANCE` /
      `BLOCKCHAIN_DEGRADATION`) `TimeoutRemainingSeconds`'ı ÖDEME deadline'ından
      yakalar → freeze sürerken ödeme onaylanabilir (`Application/Webhooks/`
      altında `TimeoutFrozenAt` kontrolü YOKTUR; state machine yalnız
      `IsOnHold`'a bakar) → T124 teslimat penceresini armlar → `ResumeAsync`
      artığı GÜNCEL state'e göre dağıttığı için `SetActiveDeadline`
      `PAYMENT_RECEIVED` dalı ödeme kalanını `DeliveryDeadline`'a yazar.
      İki kabul edilebilir çözümden biri seçilir: (a) `ConfirmPayment` yolunu
      `TimeoutFrozenAt`'e karşı korumak, (b) freeze altında faz değişince
      artığı yeniden yakalamak.
      Neden T127'nin ön koşulu: kapı kalkana kadar ezilen değer TÜKETİLMEDİĞİ
      için zararsızdır; kapı kalktığı anda süresi saniyelere inmiş bir teslimat
      penceresi iptal + alıcıya iade + satıcıya kusur üretir — yani tam olarak
      kapının önlemek için kurulduğu vaka. Bu bir T124 regresyonu DEĞİLDİR
      (`TimeoutFreezeService` T50 kodudur, T124 dokunmadı); T124 öncesinde de
      main'de aynı ezilme scanner tarafından tüketilip iptal üretiyordu.
    - DOĞRULAMA BULGULARI KAPATILDI (bağımsız doğrulama 2026-08-15 ✗ FAIL →
      düzeltme turu, proje sahibi onayı aynı gün). Üçü de testlerin BAKMADIĞI
      yollardaydı — 1382 unit + 88 odaklı integration yeşilken bulundular:
      (B1, S1) RE-ENTRY KAPISI MOTORUN NİTELEYİCİSİNİ TAŞIR. Tur, kanıt
       bayrağına (`IsMisdeliverySignature()` = `SELLER_ASSET_GONE &&
       !INVENTORY_DELTA`) değil, önceki turun KAYITLI VERDICT'ine bakar
       (`DeliveryEvidenceCaptures.Verdict = 'MisdeliverySignature'`). Motor aynı
       kararı `sellerSideKnown && buyerSideKnown` ile niteliyor; bayrak bu
       niteleyiciyi taşımadığı için "satıcının asseti gitti + alıcı envanteri
       gizli" vakası (verdict `Inconclusive`, tur 1 doğru bekletir) bir sonraki
       taramada teslim etmiş olabilecek satıcı hakkında dispute açıyordu — K4
       kararının ve 08 §2.3'ün ihlali. Test: aynı bayrakları taşıyan iki vaka,
       biri eskale eder biri etmez
      (B2, S2) TESLİMAT PENCERESİ AÇ KALMAZ. Sorgu `OrderBy(DeliveryDeadline)`
       değil, `Transaction.DeliveryRoundAt` (§06 3.5, yeni kolon, NULL'lar
       önce) ile sıralanır ve `DeliveryRoundRecheckSeconds` (varsayılan 900)
       aralığından yeni geçmiş satırlar pencereye girmez. Gerekçe: hiçbir kol
       `DeliveryDeadline`'ı veya durumu değiştirmediği için kalıcı bekleyen
       satırlar sorgudan HİÇ çıkmıyor ve en eski oldukları için deadline
       sırasında pencerenin başında kalıyorlardı; `DeliveryVerificationBatch
       Size` kadar biriktiğinde YENİ hiçbir teslimat timeout'u tur çalıştıramaz
       hâle geliyordu — T124'ün adını koyduğu açlık yok edilmemiş, teslimat
       fazının içine taşınmış oluyordu. Aralık satırı emekliye ayırmaz (08 §2.3
       "okunamadı" sonuçlanmış sayılamaz), yalnız sırasını bekletir. Test: üç
       taramada üç FARKLI satır + hiç turlanmamış satır kalıcı bekleyenleri
       geçer + aralık dolunca geri gelir
      (B3, S2) SYSTEM AÇILAN DISPUTE ÇÖZÜLÜNCE GERÇEK ALICI BİLDİRİM ALIR.
       `AdminDisputeService` `DisputeResolvedEvent.BuyerId`'yi
       `dispute.OpenedByUserId`'den değil `transaction.BuyerId`'den çözer. K1
       `OpenedByUserId = SYSTEM` yazan ilk yol olduğu için, alıcı ile açan
       kişinin aynı olduğu invariant T127'de kırıldı; okuyan taraf düzeltilmesi
       kaynağı düzeltir (tüm dispute'lar için doğru kalır). Test: SYSTEM açılan
       dispute çözülür → event alıcıyı taşır
  Not (T124 doğrulaması, 2026-08-14): yukarıdaki ön koşul bağımsız doğrulama
       turunda bulundu (Bulgu 2, S1 dayanıklılık, pre-existing). Proje sahibi
       kararı: DEFERRED_BACKLOG kalemi yerine T127 kabul kriteri — zarar tam
       olarak bu görevde doğduğu için atlanamaz olması gerekiyor.
       KALICI DERS: bir kapı, yalnız kapattığı yolu değil, koruduğu DEĞERİN
       diğer yazarlarını da denetlemeli. T124 teslimat penceresinin
       tüketicisini kapattı ama aynı kolonun ikinci (ve yanlış) yazarını
       görmedi.

Task T128: (SellerId, ItemAssetId) tekillik kapısı
  Bağımlılık: T117
  Kabul kriterleri: ikinci create ITEM_ALREADY_LISTED dönüyor

--- P4: Payout tamponu ---

Task T129: Mutabakat süresi + trade geri alma koruması [RİSKLİ]
  Bağımlılık: T125
  Dokümanlar: 02 §4.5.1, 05 §4.2, 06 §3.5
  Kabul kriterleri:
    - payout_settlement_days SystemSetting (varsayılan 8) eklendi
    - ITEM_DELIVERED girişinde PayoutEligibleAt hesaplanıyor
    - SellerPayoutQueueJob yalnız PayoutEligibleAt geçmiş işlemleri alıyor
      DURUM: bu madde T126 doğrulamasında (Bulgu F1, 2026-08-15) ERKEN
      UYGULANDI — `SellerPayoutQueueJob` sorgusu artık `PayoutEligibleAt != null
      && <= now` filtreliyor ve döngü içi yeniden doğrulama da aynı kapıyı
      okuyor (2 test: NULL → atlanır, gelecek tarih → atlanır, saat ilerleyince
      akar). Gerekçe: T126 `DeliverItem`'ın ilk üretim çağıranıdır, yani
      ITEM_DELIVERED'ı ilk kez erişilebilir yapar; kapı olmadan alıcı onayı
      ~1 dk içinde zincire payout gönderirdi ve Steam'in 7 günlük geri alma
      penceresi tamamen açık kalırdı (02 §4.5.1'in kapatmak için var olduğu
      vaka). T129 bu filtreyi KALDIRMAZ; üstüne `payout_settlement_days` ile
      hesaplanan `PayoutEligibleAt` yazıcısını ve ödeme öncesi son kontrolü
      ekler. Kapı bugün fail-closed: kolonu kimse yazmadığı için job hiçbir şey
      kuyruğa almaz, teslim edilmiş işlemler ITEM_DELIVERED'da bekler.
      KALICI DERS (T124 dersinin ikizi): bir üreticiyi açan görev, açtığı
      DEĞERİN tüketicilerinin kapılı olduğunu da doğrulamalı — T126 kabul
      kriterlerinin hepsini karşıladığı hâlde uyandırdığı tüketici kapısızdı.
    - Ödeme ÖNCESİ son kontrol: item hâlâ alıcının envanterinde mi?
        item var    -> SettlementVerifiedAt damgalanır, ödeme akar
        item yok    -> delivery_reversed trigger, REFUNDED, alıcıya iade,
                       satıcıya fraud flag, admin bildirimi
        okunamıyor  -> karar verilmez, tekrar denenir; ısrar ederse admin
      YAPIM KARARLARI (proje sahibi, 2026-08-16 — dördü de öneri yönünde
      onaylandı; ayrıntı Docs/TASK_REPORTS/T129_REPORT.md):
        (K1) Kontrol İKİ TARAFLI. "Item alıcıda yok" tek başına geri alma
             sayılmaz: Steam trade ile edinilen item'ı 7 gün kısıtlar
             (T122 runbook §6.1) ama pencere 8 gün, yani son bir gün alıcı
             skini meşru devredebilir. Tek taraflı okuma o alıcıya tam iade
             verir, item'ı da bırakır ve teslim etmiş satıcıyı fraud'la
             cezalandırır — kuralın satıcıya karşı kapattığı dolandırıcılığın
             SİMETRİĞİ. Geri alma item'ı satıcıya döndürür, devir döndürmez;
             ayırt edici sinyal budur. Ayırt edilemeyen vaka (alıcıdan gitti,
             satıcıda görünmüyor) otomatik karara BAĞLANMAZ → admin.
        (K2) Negatif dal LAUNCH KAPISI arkasında
             (settlement.reversal_auto_refund_enabled, varsayılan false) —
             T125'in kapısının ikizi. Kapalıyken imza kaydedilir + admin'e
             eskale edilir, para parkta; açıkken plandaki otomatik dal işler.
             Gerekçe: T122 gerçek bir rollback ölçemedi (runbook §7).
        (K3) Okunamaz dal settlement.unreadable_escalation_hours (varsayılan
             48) sonrası admin'e eskale; ödeme her hâlükârda parkta kalır.
        (K4) Fraud işareti yeni FraudFlagType.DELIVERY_REVERSED, HESAP
             düzeyinde (02 §4.5.1 "satıcı hesabına"; §14.2 tekrarı sayar).
    - COMPLETED guard'ı: SettlementVerifiedAt NOT NULL && DeliveryReversedAt NULL
    - Süre içinde açılan dispute ödemeyi bloklar
    - SweepQueueJob aynı kapıya bağlandı
    - delivery_reversed ile REFUNDED'a düşen işlemin satıcının itibarına
      etkisine karar verildi ve 06 §3.1 karara göre yazıldı (aşağıdaki not)
  Not (T119 denetimi, 2026-08-10): 06 §3.1 oran formülünün paydası
       COMPLETED + CANCELLED_SELLER + CANCELLED_BUYER + CANCELLED_TIMEOUT.
       `REFUNDED` paydada YOK — bugün doğru, çünkü v2.0'da REFUNDED yalnız
       admin dispute iadesiydi (platform kararı, CANCELLED_ADMIN ile aynı
       gerekçeyle hariç). Bu görev REFUNDED'a ikinci bir giriş açıyor:
       trade'ini geri alan satıcı. Bu, platform kararı değil kanıtlanmış satıcı
       kusurudur; formül değişmezse en ağır dolandırıcılık senaryosu itibar
       skoruna HİÇ yansımaz (yalnız fraud flag'i kalır). Karar ya "payda
       DeliveryReversedAt NOT NULL olan REFUNDED satırlarını da satıcıya yazar"
       (06 §3.1 + ReputationAggregator + test) ya da "fraud flag yeterli,
       gerekçesi belgelendi" olmalı — sessizce geçilemez.
       KARAR (proje sahibi, 2026-08-16): BİRİNCİSİ. `DeliveryReversedAt NOT
       NULL` olan REFUNDED satırları satıcının paydasına yazılır; admin dispute
       iadesi (kolon NULL) CANCELLED_ADMIN ile aynı gerekçeyle dışarıda kalır.
       06 §3.1 formülü + ReputationAggregator + 2 test bu ayrımı uygular.
  Not: Beklemek tek başına korumaz — korumayı süre sonundaki KONTROL sağlar.
       Bu ikisi ayrılamaz; sadece gecikme uygulayan bir sürüm güvenli değildir.

  DÜZELTME TURU KABUL KRİTERLERİ (T129 doğrulaması ✗ FAIL, 2026-08-16 —
  bağımsız validator; üç bloke edici bulgu proje sahibi onayıyla plana
  işlendi. İlk tur AC1/2/3/5/6/7'yi karşıladı, AC4 + AC8 ~ Kısmi kaldı):

    - (B1) MUTABAKATIN KARAR GİRDİSİ ÜRETİLEMEYEN VAKA İÇİN ÇIKIŞ YOLU.
      Bulgu: alıcının envanteri SELLER_CONFIRMED anında gizliyse
      `BuyerBaselineCapturedAt`/`BuyerBaselineClassCount` bilinçli olarak
      NULL bırakılıyor (03 §2.3 gereği bloke ETMEMELİ) ve alıcı-onaylı
      teslimat yolunda `DeliveredBuyerAssetId` de NULL kalıyor. İki kolon da
      ITEM_DELIVERED'dan SONRA hiçbir yolla dolmuyor, dolayısıyla mutabakat
      kontrolü o işlem için sonsuza kadar Inconclusive; `SettlementVerifiedAt`
      asla damgalanmıyor ve payout + sweep + COMPLETED üçü birden kalıcı
      kilitleniyor. Admin'in kolu YOK: `admin_resolve_refund` yalnız ESCALATED
      bir dispute üzerinden ateşlenir ve dispute'u yalnız ALICI açabilir.
      Sonuç: dürüst satıcı hiçbir zaman ödenemez, alıcının parası süresiz
      donar. K2 launch kapısı bunu hafifletmez — kapı yalnız geri alma dalını
      etkiler, bu dal launch'ta VARSAYILAN yoldur.
      KARAR (proje sahibi, 2026-08-16): SEÇENEK 1 — ayrı verdict + admin
      aksiyonu. Koruma seviyesini düşürmeyen tek seçenek budur; alternatifler
      reddedildi: ITEM_DELIVERED'da envanter okuması gizli-envanter vakasını
      kapatmıyor, "baseline'sız işlemleri kontrolden muaf tut" ise 02 §4.5.1'in
      korumasını o popülasyon için tamamen kaldırıyor.
      Uygulama:
        a) Mutabakat kontrolüne "karar girdisi ÜRETİLEMEZ" için ayrı bir
           verdict/gerekçe kodu (mevcut `SETTLEMENT_UNREADABLE` yanıltıcı:
           okunamayan bir envanter yok, EKSİK OLAN BASELINE'dır ve o kolon
           SELLER_CONFIRMED'dan sonra hiçbir yolla dolmaz). Gerekçe admin
           kuyruğunda ayırt edilebilir olmalı — DEPLOY_RUNBOOK §I.3'ün
           "Steam trade geçmişi rollback gösteriyor mu" triyaj prosedürü bu
           vakaya UYMAZ, ayrı bir prosedür gerekir.
        b) Eşiği beklemeden eskale et: bu sınıfta retry'ın kazanacağı bir şey
           yoktur, `settlement.unreadable_escalation_hours` boyunca beklemek
           yalnız satıcının ödemesini geciktirir.
        c) Admin'in mutabakatı SATICI LEHİNE kapatabileceği bir aksiyon
           (dispute gerektirmeyen, ITEM_DELIVERED'dan `Complete`'i açan yol).
           DEPLOY_RUNBOOK §I.4 `SettlementVerifiedAt`'i elle damgalamayı
           yasaklıyor ve yerine bir çare göstermiyor — bu madde o boşluğu
           kapatır. Yetki `MANAGE_DISPUTES` sınıfında, audit + history satırı
           zorunlu, aktör ADMIN.
        d) DEPLOY_RUNBOOK §I.1/§I.4 ve T129_REPORT §Known Limitations'taki
           "karar `admin_resolve_refund` ile verilir" cümlesi düzeltilir
           (bkz. N3 — o yol bu vakada erişilebilir DEĞİL ve erişilebilir
           olduğu vakada bile aynı sonucu üretmiyor).
      KALICI DERS: bir kapı, koruduğu değerin yazarlarını denetlemekle
      bitmez — kendi KARAR GİRDİSİNİN hiç üretilemediği popülasyonu da
      denetlemelidir. T129 `PayoutEligibleAt` için bunu yapısal olarak
      uyguladı (giriş guard'ı) ama kendi girdisi için hiç sormadı. Girdisi
      asla gelemeyecek bir fail-closed kapı artık fail-closed değil,
      FAIL-FROZEN'dır.

    - (B2) `DELIVERY_REVERSED` kataloglara ve admin yüzeyine yansıtıldı.
      Enum değeri eklendi ama K4'ün amacı ("§14.2 tekrarı sayar, dolayısıyla
      vaka admin kuyruğunda AYIRT EDİLEBİLİR olmalı" — 02 §4.5.1, T129'un
      kendi eklediği cümle) karşılanmadı:
        - 06 §2.11 kanonik FraudFlagType tablosu beş değerde kaldı. T82
          `SANCTIONS_MATCH`'i eklerken §2.11 satırını yazmıştı; ayrıca
          T129'un 02 §4.5.1'e eklediği satır 06'da TANIMLI OLMAYAN bir ada
          normatif atıf yapıyor (sarkan referans).
        - FE `admin/flags/page.tsx` `TYPE_VALUES` 5 elemanlı → yeni tipe göre
          filtre yok; `parseEnum` beyaz listesi elle URL denemesini de düşürür.
        - Üç i18n haritası (`adminFlags.type`, `adminDashboard.flagType`,
          `adminUserDetail.flags.type`) dört dilde de eksik → ekrana ham
          anahtar basar (`i18n/request.ts` fallback tanımlamıyor). Üçüncüsü
          §14.2'nin "tekrarı say" kuralının fiilen okunduğu HESAP bazlı flag
          geçmişi tablosudur.
        - 07 §9.3 `flagDetail` tablosuna `DELIVERY_REVERSED` satırı (job on
          alanlı payload yazıyor ve AD3 üzerinden admin ekranına düşüyor).
          Not: tablo `SANCTIONS_MATCH`'i de içermiyor — o satır da eklenir.

    - (B3) İtibar tetikleyicisi bağlandı. `SuccessfulTransactionRate`
      denormalized bir `User` kolonudur ve yalnız
      `ITransactionReputationRefresher.RefreshAsync` çağrıldığında yazılır.
      Formülü etkileyen her terminal geçiş bunu çağırıyor
      (`PayoutCompletedConsumer`, `TimeoutExecutor`, `DeadlineScannerJob`,
      `TransactionCancellationService`) — T129'un eklediği
      `DeliveryReversed → REFUNDED` çağırmıyor, dolayısıyla AC8'in formülü
      ÜRETİMDE ETKİSİZ: geri alma yapan satıcının skoru, o satıcının BAŞKA
      bir işlemi terminal olana kadar (hiç olmayabilir) güncellenmiyor.
      Uygulama: `SettlementVerificationJob`'a refresher enjekte edilir,
      `ApplyReversalAsync`'te terminal statü flush edildikten SONRA
      `RefreshAsync(sellerId, buyerId, evaluateCooldown: false, ct)` çağrılır
      (aggregator `AsNoTracking` okuduğu için sıra zorunlu — 06 §8.2).
      Ayrıca 06 §8.2 tetikleyici satırı ("İşlem COMPLETED veya CANCELLED
      olduğunda") ve §3.1 giriş cümlesi geri alma dalıyla genişletilir —
      bugün §3.1 ile §8.2 birbiriyle ÇELİŞİYOR ve kod §8.2'yi izliyor.
      KALICI DERS: denormalize bir alanda "kural yazıldı" ile "kural işliyor"
      farklı şeylerdir ve birim testi ikincisini göstermez — aggregator testi
      yeşilken üretimde hiç çağrılmıyordu. Formülü genişleten görev, o formülü
      ÇALIŞTIRAN tetikleyiciyi de genişletmelidir.

    - (N1, kapı açılmadan ÖNCE kapatılmalı) Geri alma imzası "satıcıya
      döndü"yü ölçsün, "satıcıda var"ı değil. 02 §4.5.1 "satıcıya DÖNMÜŞSE" /
      "YENİDEN BELİRMESİ" diyor; kod saf bulunma testi yapıyor
      (`sellerRead.Item is not null`) ve asset'in daha önce satıcıdan
      AYRILDIĞINI hiçbir yerde doğrulamıyor — `DeliveryEvidence`'ın
      `SELLER_ASSET_GONE` biti Settlement katmanında hiç okunmuyor. Alıcı
      onayıyla kapanan teslimatta platform hiç envanter okumadığı için
      satıcının orijinal `ItemAssetId`'si envanterinde dururken
      ITEM_DELIVERED'a girilebiliyor (satıcı aynı sınıftan BAŞKA kopyayı
      gönderdi — 02 §9.2 sayım kuralı bunu geçerli teslimat sayar) → dürüst
      satıcı için yanlış-pozitif `ReversalSignature`, yani K1'in kapatmak
      için yazıldığı simetrik zararın ta kendisi. K2 kapalıyken sonuç
      `AmbiguousDeparture` ile aynı (admin eskalasyonu, para parkta), bu
      yüzden turu bloke etmez — ama `settlement.reversal_auto_refund_enabled`
      açılmadan önce kapatılması ZORUNLUDUR (DEPLOY_RUNBOOK §I.3'e ön koşul
      olarak yazılır).

    - (N2) Eskalasyon yapışkan hâle getirildi. Bugün geri alma imzası hiçbir
      kolonda saklanmıyor (`SettlementEscalatedAt` dışında iz yok ve onu
      hiçbir para kapısı okumuyor); sonraki turda alıcı tarafı "item duruyor"
      derse `ClearForPayoutAsync` koşulsuz `SettlementVerifiedAt` damgalıyor
      ve para, admin kutusunda AÇIK bir `ADMIN_ESCALATION` dururken ve
      admin'e HİÇ haber verilmeden çıkıyor; §I.3 triyaj sorgusu
      `SettlementVerifiedAt`'i seçmediği için bu görülmüyor. İki kabul
      edilebilir şekil: (a) verdict'i kalıcı bir kolona yaz ve
      `ClearForPayout`'u ona bağla, (b) otomatik çözülmede "eskalasyon
      kendiliğinden kapandı" olayı yayımla. ÖNERİ: (a) — imzanın kaybolmaması
      02 §4.5.1 launch-kapısı notunun ve DEPLOY_RUNBOOK §I.1'in verdiği söz.

    - (N3–N7, doküman hijyeni) DEPLOY_RUNBOOK §I.1/§I.4'ün
      "`admin_resolve_refund` ile AYNI sonucu üretir" iddiası düzeltilir (o
      yol `DeliveryReversedAt` yazmaz → ne itibar paydası ne fraud flag; aynı
      iddia `SystemSettingSeed` ayar açıklamasına ve T129_REPORT §Known
      Limitations'a da sızmış) · 06 §3.17'deki launch kapısı satırı
      DEPLOY_RUNBOOK §H yerine §I'yi göstermeli · SystemSetting anahtar
      sayısı üç yerde üç farklı (06 §3.17 "58", 07 §9.8 "63", DEPLOY_RUNBOOK
      §C "60 satır"/"altı satır"; GERÇEK 63 = seed = katalog) tek değere
      çekilir · 05'in sweep tetikleyicisi satırı (05:313) hâlâ yalnız
      "ITEM_DELIVERED state gate'i" diyor, T129'un genişlettiği çifti yazmalı.

    DÜZELTME TURU — NİHAİ ŞEKİL (2026-08-17, proje sahibi onaylı; T122'nin
    kalıcı dersi gereği sapmalar ve genişlemeler KAYNAK dokümana yazıldı):

      - (B1c NİHAİ ŞEKİL) Aksiyon bir state machine geçişi DEĞİLDİR.
        Kriter "ITEM_DELIVERED'dan Complete'i açan yol" diyordu; reentrant
        bir ITEM_DELIVERED geçişi kullanılamaz, çünkü o state'in OnEntry'si
        `ItemDeliveredAt`'i admin'in tıkladığı ana yeniden damgalar. Uç
        bunun yerine `SettlementVerifiedAt`'i damgalar — yani üç para
        kapısının (payout, sweep, COMPLETED guard) okuduğu tek kolonu — ve
        statüye dokunmaz; COMPLETED yine payout'un arkasından
        `PayoutCompletedConsumer` ile gelir. History satırı, geçiş olmadığı
        için enum trigger yerine string etiketle yazılır
        (`"AdminClearSettlement"`, genesis satırı emsali).
        Uç: AD32 `POST /admin/transactions/:id/clear-settlement`,
        yetki `MANAGE_DISPUTES`, doküman 07 §9.22b (§9.23 doluydu; numara
        işlem ailesinin yanında tutuldu). Ön koşul olarak `SettlementEscalatedAt
        NOT NULL` arar: admin, platformun SORDUĞU vakayı kapatır — pencere
        dolmadan satıcıyı ödeme yoluna sokmanın yolu değildir. Karşı yön
        (alıcı lehine) EKLENMEDİ; o karar dispute üzerinden AD29'dur.
        Yeni `AuditAction.SETTLEMENT_CLEARED_ADMIN` (ADMIN_ACTION).

      - (N2 NİHAİ ŞEKİL) Kalıcı kolon `SettlementEscalationReason`
        (nvarchar(64), SettlementReviewReasons kodu). Yapışkanlık gerekçeye
        göre bölündü: AYRILMAYI GÖZLEMLEMİŞ eskalasyonlar
        (`SETTLEMENT_AMBIGUOUS_DEPARTURE`, `SETTLEMENT_REVERSAL_GATED`)
        `ClearForPayout`'u bloklar; HİÇBİR ŞEY GÖZLEMEMİŞ olanlar
        (`SETTLEMENT_UNREADABLE`, `SETTLEMENT_NO_DELIVERY_REFERENCE`)
        bloklamaz — orada sonraki turun okuması gerçekten yeni bilgidir ve
        güvenli yöndedir. Kolon aynı zamanda B1(a)'nın "gerekçe admin
        kuyruğunda ayırt edilebilir olmalı" şartını karşılar (gerekçe daha
        önce yalnız outbox event'indeydi, tabloda yoktu).
        İkinci kolon `SettlementClearedByAdminId` (uniqueidentifier): hiçbir
        para kapısı okumaz, yalnız "kararı insan mı verdi" sorusunu triyaj
        sorgusunda yanıtlar. Migration `T129_SettlementEscalationColumns`.

      - (N1 KAPSAM KARARI, proje sahibi 2026-08-17) Kodda kapatıldı, yalnız
        runbook ön koşulu olarak bırakılmadı: `ReversalSignature` artık
        teslimatta gözlenmiş `DeliveryEvidence.SELLER_ASSET_GONE` biti ile
        şimdi satıcıda görünmeyi BİRLİKTE ister; yalnız ikincisi varsa vaka
        `AmbiguousDeparture`'dır ve ayrım Detail metninde korunur. Kapı
        kapalıyken gözlenebilir davranış değişmez (ikisi de eskalasyon), kapı
        açıldığında dürüst satıcıya yanlış-pozitif iade riski kapanır.

      - (B2 KAPSAM GENİŞLEMESİ, proje sahibi 2026-08-17) 07 §9.3'e satır
        yazmak tek başına yanlış olurdu: `FraudFlagAdminQueryService` bu tipi
        `_ => null` dalına düşürdüğü için AD3 `flagDetail` boş dönüyordu ve
        admin ekranı "sinyal detayı yok" gösteriyordu. Tura backend
        projeksiyonu (`DeliveryReversedFlagDetail`, 12 alan — N7'deki "on
        alan" sayımı YANLIŞTI), AD2 liste özeti (`ItemName`) ve FE
        `FlagDetailView` dalı da dahil edildi. 07 §9.3 tablosuna
        `SANCTIONS_MATCH` satırı da eklendi ("projeksiyon yok" olarak).

      - (N5 EK BULGU) 06 §3.17 metni "58" derken tablosu da eksikti: 62 satır
        listeliyor, `blockchain.payout_gas_fee_estimate_usdt` yok. Sayı 63'e
        çekilirken eksik satır da eklendi.

    Yeniden doğrulama: düzeltme sonrası AYRI bir doğrulama chat'i açılır
    (INSTRUCTIONS §3.3 izolasyon kuralı); ilk turda ✓ kanıtlanan
    AC1/2/3/5/6/7 için kanıt korunur, AC4 + AC8 sıfırdan doğrulanır.

  İKİNCİ DÜZELTME TURU KABUL KRİTERLERİ (T129 yeniden doğrulaması ✗ FAIL,
  2026-08-17 — bağımsız validator, tur 2; bir bloke edici bulgu proje sahibi
  onayıyla plana işlendi. Düzeltme turunun on dört maddesinden on üçü
  (B1a–d · B2 · B3 · N1 · N3–N7) bağımsız olarak yeniden üretildi ve DOĞRU
  bulundu; AC1/2/3/5/6/7/8 ✓, AC4 ~ Kısmi kaldı):

    - (B4) ESKALASYON GEREKÇESİNİN YAPIŞKANLIĞI DÜŞÜRÜLEMEZ OLMALI.
      Bulgu: N2'nin NİHAİ ŞEKLİ yapışkanlığı `SettlementEscalationReason`
      alanının DEĞERİNE bağladı, ama o alanı yazan kolu düşürmeye karşı
      korumadı. `EscalateAsync` zaten eskale edilmiş bir satırda gerekçe
      farklıysa KOŞULSUZ üzerine yazıyor (kod yorumu yalnız "upgrade"
      diyor; uygulama düşürmeyi de yapıyor), ve `HandleInconclusiveAsync`
      eşiği `PayoutEligibleAt`'ten ölçtüğü için eskalasyondan sonra
      `settlement.unreadable_escalation_hours` geçmişken gelen TEK BİR
      `Inconclusive` tur — alıcının envanterini gizlemesi yeter —
      `SETTLEMENT_REVERSAL_GATED` / `SETTLEMENT_AMBIGUOUS_DEPARTURE`
      gerekçesini `SETTLEMENT_UNREADABLE`'a çeviriyor. Sonraki `Verified`
      turunda `ClearForPayoutAsync`'in `ObservedDeparture(...)` kontrolü
      artık `false` dönüyor → `SettlementVerifiedAt` damgalanıyor → payout
      ve sweep açılıyor; `SettlementEscalatedAt` hâlâ dolu, admin kutusunda
      `ADMIN_ESCALATION` açık ve İKİNCİ BİR BİLDİRİM GİTMİYOR. Yani N2'nin
      kapatmak için yazıldığı zararın ta kendisi geri geliyor ve bu sefer
      yalnız ayırt edilemeyen ayrılmayı değil GERİ ALMA İMZASINI da siliyor.
      Dönüş rotası gerçek: `DeliveredBuyerAssetId` NULL olan alıcı-onaylı
      teslimatlarda kontrol sayım rotasına düşer ve alıcı aynı sınıftan
      başka kopya edindiği anda "item duruyor" der (raporun kendi §Known
      Limitations'ında yazılı zayıflık).
      Çelişilen kaynak cümleler: 02 §4.5.1 launch kapısı notu ("Kaydedilen
      imza YAPIŞKANDIR — ayrılmayı gözlemlemiş bir eskalasyon açıkken
      sonraki turun 'item duruyor' okuması ödemeyi serbest bırakmaz"),
      06 §3.5 `SettlementEscalationReason` satırının (2) numaralı işi,
      DEPLOY_RUNBOOK §I.1.
      Kanıt: validator reprosu (geçici test, koşumdan sonra geri alındı) —
      `r1 reason=SETTLEMENT_REVERSAL_GATED → r2 reason=SETTLEMENT_UNREADABLE
      → SettlementVerifiedAt damgalandı, SettlementEscalatedAt dolu`.
      Uygulama:
        a) `EscalateAsync`'te gerekçe yalnız GÜÇLENDİRİLEBİLİR:
           `ObservedDeparture(mevcut) && !ObservedDeparture(yeni)` ise
           mevcut gerekçe korunur (yalnız log). Ters yön (hiçbir şey
           gözlememiş → gözlemlemiş) serbest kalır; bugünkü davranış budur.
        b) Düşürme yönü için regresyon testi. Mevcut iki karşıt yapışkanlık
           testi yalnız yükseltme yönünü sabitliyor — kriterin yarısı test
           edilmemiş durumda.
      (a)'NIN NİHAİ ŞEKLİ (proje sahibi kararı D3, 2026-08-17): koşul
      SINIF BAZLI değil TAM SIRALAMA olarak uygulandı.
      `SettlementReviewReasons.Strength`: `SETTLEMENT_UNREADABLE` =
      `SETTLEMENT_NO_DELIVERY_REFERENCE` (0) < `SETTLEMENT_AMBIGUOUS_DEPARTURE`
      (1) < `SETTLEMENT_REVERSAL_GATED` (2), bilinmeyen/NULL (−1); yeni tur
      yalnız KESİN OLARAK daha güçlüyse yazar. Kriterin (a) maddesini tam
      karşılar, üstüne SINIF İÇİ düşürmeyi (`REVERSAL_GATED` →
      `AMBIGUOUS_DEPARTURE`) ve eşit güçteki üzerine yazmayı da kapatır.
      GEREKÇE: DEPLOY_RUNBOOK §I.3 kapı açma kararını `SETTLEMENT_REVERSAL_
      GATED` satırlarını SAYARAK verir; satıcı geri dönen item'ı devrederse
      sonraki tur `AmbiguousDeparture` okur ve sınıf içi düşürme tam o kanıtı
      siler — kuyruk "hiç gerçek geri alma gözlenmedi" gibi görünür. Aynı
      gerekçe AD32'nin kapanış audit satırı için de geçerli. Sınıf içi
      düşürmede para yine parkta kalır (her iki kod da `ObservedDeparture`),
      kaybedilen TRİYAJ DOĞRULUĞUDUR — bu yüzden bloke edici sayılmadı ama
      maliyeti bir rank fonksiyonu olduğu için aynı turda kapatıldı.
      EK GUARD: rank'lanmamış bir kod −1 alır ve mevcut gerekçenin üstüne
      HİÇ yazılamaz, yani yeni bir bulgu parayı serbest bırakan yönde sessizce
      kaybolur. Bu yüzden `SettlementReviewReasonsTests` reflection ile "her
      sabit rank'lanmış" invariant'ını ve `ObservedDeparture` ↔ `Strength ≥ 1`
      tutarlılığını test eder — bulgunun kalıcı dersinin rank tablosunun
      KENDİSİNE uygulanması.
      DOKÜMAN YANSIMASI: kural üç kaynak cümlede tamamlandı — 06 §3.5
      `SettlementEscalationReason` satırı (güç sırası + eşit güç kuralı),
      DEPLOY_RUNBOOK §I.1 (operasyonel sonuç: §I.3 sorgusunda görülen gerekçe
      o işlem için gözlenmiş EN GÜÇLÜ bulgudur), 02 §4.5.1 launch kapısı notu
      (yapışkanlık gerekçeyi de kapsar).
      TURUN KENDİSİNİN BULDUĞU KENAR DURUM (proje sahibi kararı D5,
      2026-08-17): eşit-güç koruması gerekçeyi İLK kaydedildiği hâlde
      sabitlediği için, yanlış ilk etiket artık kalıcı olur.
      `SettlementVerificationService.VerifyAsync` referans-yok kontrolünü
      alıcının Steam ID çözümünden SONRA yapıyordu; çözülemeyen bir alıcı
      (soft-delete) kalıcı olarak cevapsız bir vakayı önce `Inconclusive`
      yapıyor, 48 saat sonra `SETTLEMENT_UNREADABLE` etiketiyle eskale
      ediyordu — ve o etiket admin'i DEPLOY_RUNBOOK §I.3'e gönderiyor, oysa
      bu sınıfın prosedürü §I.5'tir ve §I.3 triyajı ona AÇIKÇA UYMUYOR.
      Kontrol `VerifyAsync`'in başına alındı (`HasNoDeliveryReference` —
      saf kolon testi, okuma gerektirmez): yol yapısal olarak erişilemez oldu,
      vaka 48 saat erken ve DOĞRU etiketle admin'e düşüyor, bir rate-limited
      Steam okuması da tasarruf ediliyor. Para davranışı değişmedi (her iki
      hâlde de park + admin). Test `NoDeliveryReference_IsDecided_
      BeforeTheBuyerAccountIsResolved` + negatif prova.
      KALICI DERS: bir alanı YAPIŞKAN yapan görev, o alana İLK DEĞERİ yazan
      yolun doğruluğunu da denetlemelidir — yapışkanlık yalnız doğru değeri
      değil, yanlış değeri de sabitler.
      KALICI DERS: yapışkanlığı bir alanın DEĞERİNE bağlayan tasarım, o
      alanı yazan HER kolun düşürme yönünü de denetlemek zorundadır.
      "Kural yazıldı" ile "kural korunuyor" yine farklı şeyler çıktı ve
      birim testleri ikincisini göstermedi (B3 dersinin ikizi: orada
      formülün TETİKLEYİCİSİ eksikti, burada kuralın KORUYUCUSU).

    - (N8, bloke etmeyen) Rapor §Güvenlik Kontrolü düzeltme turu için
      güncellenmemiş: "Auth/authorization: yeni endpoint yok" diyor, oysa
      aynı rapor AD32'yi belgeliyor. Kod temiz doğrulandı (policy
      `MANAGE_DISPUTES`, `RateLimit("admin-write")`, gerekçe ≥10 karakter,
      yeni permission tanımlanmamış) — yanlış olan Katman-1 kaydı.

    - (N9, bloke etmeyen) Sweep artık payout ile AYNI ANDA açılıyor: T129
      öncesi sweep `ITEM_DELIVERED`'da (T+0), payout `PayoutEligibleAt`'te
      (T+8g) çalışıyordu; artık ikisinin de kapısı `SettlementVerifiedAt`.
      Kapı doğru ve AC7'nin talebi (05 §3.3 gerekçesi yazılı), eksik olan
      SONUCUN KAYDI: hot wallet artık ilgili depozit girişini payout
      çıkışından önce görmüyor, yani 8 günlük bir float ihtiyacı doğuyor.
      02 §4.5.1 "Bilinen sonuçları" yalnız toplam tutulan paranın artışını
      yazıyor. Bir satır DEPLOY_RUNBOOK §I'ye veya DEFERRED_BACKLOG'a.
      NİHAİ ŞEKİL (proje sahibi kararı D4, 2026-08-17): İKİSİ BİRDEN —
      02 §4.5.1 "Bilinen sonuçları" listesine dördüncü madde VE
      DEPLOY_RUNBOOK §I.2'ye launch öncesi nakit akışı kontrolü.
      `DEFERRED_BACKLOG` reddedildi: sonuç yapılacak bir İŞ değil, ürün
      kararının bilinen bedeli; kaynak dokümana yazılmazsa runbook satırı
      türev kalır ve aynı boşluk sonraki turda geri gelir. Mekanizmanın
      doğru ifadesi "8 günlük float" değildir: eskiden süpürme T+0, ödeme
      T+8g olduğu için sıcak cüzdan depoziti ödemeden GÜNLER ÖNCE görüyordu
      ve bu gecikme onu kendiliğinden fonluyordu; artık iki kapı da
      `SettlementVerifiedAt` ve iki job birbirini beklemediği için ödeme
      karşılık gelen depozitle AYNI PENCEREDE ısmarlanır — ortadan kalkan şey
      ön fonlamadır ve fark platformun kendi işletme bakiyesiyle kapatılır
      (`hot_wallet_limit`, runbook §A satır 18). Kapının kendisi
      DEĞİŞTİRİLMEDİ: WP3 gerekçesi (depozit, iadenin çekilebileceği yerde
      kalmalı) geçerliliğini sürdürüyor.

    - (N10, bloke etmeyen) Rapor §Yeni kalıcı yüzeyler migration adını
      `20260817081454_...` yazıyor; dosya `20260817084800_...`. Yalnız kayıt
      hatası — migration'ın kendisi doğru (snapshot senkron, CI migration
      dry-run yeşil).

    Üçüncü doğrulama: düzeltme sonrası yine AYRI bir doğrulama chat'i
    açılır. Tur 2'de ✓ kanıtlanan AC1/2/3/5/6/7/8 ve B1(a–d) · B2 · B3 · N1 ·
    N3–N7 için kanıt korunur; AC4 + N2 (B4 ekseninde) sıfırdan doğrulanır.

--- P5: Dispute ---

Task T130: DisputeEligibility + AutoChecker yeniden yazımı
  Bağımlılık: T125
  Dokümanlar: 02 §10.1, 03 §6.2/§6.3
  Kabul kriterleri:
    - "Satıcı başka yere gönderdi" imzası auto-escalate ediyor
    - WRONG_ITEM PAYMENT_RECEIVED'dan da açılabiliyor
    - Yanlış item vakasında gelen item'ın adı admin'e kanıt olarak taşınıyor
    - LAUNCH KAPISI ÇIKMAZI KAPATILDI (T127 doğrulaması Bulgu B5, 2026-08-15 —
      proje sahibi kararı: sahiplik T130, LAUNCH'TAN ÖNCE kapanmalı):
      kapı kapalıyken biriken YETERLİ envanter kanıtı, alıcı `DELIVERY`
      dispute'u açtığında `DeliveryDisputeAutoChecker` üzerinden `Resolved:
      true` üretiyor → dispute CLOSED + `CanEscalate = false` olarak AÇILIYOR.
      Sonuç: kapı parayı bıraktırmıyor VE alıcının eskalasyon yolu kapalı —
      para kilitli, çıkış yok. Auto-checker kapı kapalıyken "teslim edildi"
      sonucu üretmemeli (dispute OPEN + eskale edilebilir kalmalı).
      Tohum T126 (alıcının kendi confirm-receipt çağrısı), ama T127 bunu alıcı
      HİÇBİR ŞEY YAPMADAN ve launch'ta HER teslimatta erişilebilir yapıyor
    - AYNI DOSYADA İKİNCİ MADDE (T127 düzeltme turunda görüldü, bloke etmeyen):
      `DeliveryDisputeAutoChecker.cs:69` da çıplak `IsMisdeliverySignature()`
      okuyor — B1 ile aynı niteleyici eksiği, ama **zararsız yönde**: sonuç
      `Unresolved` + `CanEscalate = true`, yani satıcı hakkında iddia üretmiyor,
      yalnız alıcıya gösterilen mesaj ("asset gitti, ulaşmadı") alıcı envanteri
      okunamayan vakada yanlış olabiliyor. Yeniden yazımda mesaj seçimi de
      motorun verdict'ine bağlanmalı (bayrağa değil)

  YAPIM ÖNCESİ BULGU + PROJE SAHİBİ KARARLARI (2026-08-17, T130 ön-uçuş):
       BULGU (SPEC_GAP): "gelen item'ın adı admin'e taşınır" kriteri mevcut
       veriyle KARŞILANAMAZ. Zincir: baseline sınıf-kapsamlı alınıyor
       (`CaptureClassBaselineAsync(steamId, ItemClassId, ItemInstanceId)`) →
       `CandidateDeliveredAssetId` o sınıfın yeni asset'i →
       `DeliveredBuyerAssetId`'nin tek iki yazarı bu adayı yazıyor →
       `WrongItemDisputeAutoChecker` adayın sınıfını `ItemClassId` ile
       karşılaştırıyor ve **her zaman eşleşiyor**. Yanlış sınıf geldiğinde ise
       sayaç hiç artmadığı için kolon NULL kalıyor. Uyuşmazlık dalı iki yönde
       de erişilemez; 06 §3.5 satır 617'nin `BuyerBaselineAssetIds`'e yüklediği
       "yanlış item tespiti" görevi sınıf-kapsamlı bir baseline'la yapılamaz.
       D1 — 03 §6.2'ye **Sonuç E** eklendi (kapı kapalı + kanıt var): dispute
            OPEN kalır ve eskale edilebilir; yeni mesaj anahtarı
            `DELIVERY_EVIDENCE_UNDER_REVIEW`. DEPLOY_RUNBOOK §H.2'ye dispute
            satırı eklendi.
       D2 — Gelen item'ın adı `Disputes.DeliveredItemName` **kolonuna** yazılır
            (`SystemCheckResult` içine gömülmez: o metin alıcının dilinde
            üretiliyor, admin o dili okumayabilir).
       D3 — `Transactions.BuyerBaselineClassIds` eklendi: SELLER_CONFIRMED'da
            alıcı envanterinin TÜM sınıf kimlikleri kaydedilir; dispute anında
            envanter taze okunup diff alınır. Ek Steam çağrısı maliyeti YOK —
            sidecar zaten her istekte tüm envanteri döndürüyor, iki mevcut
            okuma da onu istemci tarafında filtreliyor.

Task T131: AdminDisputeService — item-refund bacağı + override
  Bağımlılık: T130
  Kabul kriterleri:
    - Item iade bacağı kaldırıldı. NİHAİ KAPSAM (2026-08-17, proje sahibi
      onaylı): bacak KODDA zaten yoktu (T117 `ItemRefundToSellerRequestedEvent`
      tipini sildi, `AdminDisputeService` açık negatif yorum taşıyor). Kapatılan
      şey, bacağın hâlâ VAR olduğunu söyleyen dört kalıntı: 07 §9.30 AD29
      ("item platformdaysa ItemRefundToSellerRequestedEvent yayınlanır"),
      06 §3.11 WP5 notu ("item platformdaysa satıcıya iade"), ve iki XML doc
      (`DisputeResolutionOutcome`, `DisputeResolvedEvent` + `IAdminDisputeService`).
      07 §9.20/§9.22 iade tabloları bu göreve DEĞİL T133a'ya aittir (aşağıdaki
      T133a kriteri) — T131 yalnız kendi ucunun (AD29) sözleşmesini kapatır,
      çünkü kodu "kaldırıldı" işaretleyip sözleşmeyi vaat eder hâlde bırakmak
      "kod kaldırdı, sözleşme hâlâ söz veriyor" penceresini açar
    - Kanıtlanmış teslimatta BUYER_FAVOR gerekçe istiyor.
      NİHAİ ŞEKİL (2026-08-17, proje sahibi onaylı — kriterin ilk metni
      "INVENTORY_DELTA kanıtlı ITEM_DELIVERED" idi; kapsam o metnin ÜST
      KÜMESİNE genişletildi, dolayısıyla harfi de karşılanır):
      D1 — KAPI KOŞULU `Status == ITEM_DELIVERED`, kanıt bayrakları DEĞİL.
           Gerekçe: bu duruma tek bir giriş kenarı vardır (`DeliverItem`) ve
           guard'ı zaten 02 §9.2 kanıtını + `DeliveryVerifiedAt` damgasını
           şart koşar — yani ITEM_DELIVERED olmak teslimatın kanıtlanmış
           OLMASIDIR. Bayrağa bağlamak, admin'in gerekçe yazma yükümlülüğünü
           vakanın gücüne değil Steam'in o an okunabilir olmasına bağlardı
           (`INVENTORY_DELTA` yalnız alıcı envanteri Public iken yazılır), ve
           launch'ta kapı kapalı olduğu için en kalabalık popülasyonu
           (alıcı onayıyla gelen teslimatlar) korumasız bırakırdı
      D2 — GEREKÇE AYRI BİR ALANDIR: AD29 body'sine `overrideReason`
           (zorunlu, ≥20 karakter) ve kalıcı kolon
           `Disputes.ResolutionOverrideReason` (06 §3.11). 03 §6.4 "gerekçesi
           AYRICA kayda geçirilir" diyor; `adminNote`'un içine gömmek o
           ayrımı yok ederdi. Eşik 1 değil 20: "ok" kabul eden bir kapı hiçbir
           şey kaydetmez. Override'ı olmayan bir kararda alan gönderilse bile
           SAKLANMAZ — böylece dolu kolon her zaman tek bir şey demektir
           (platformun kendi kanıtına rağmen verilmiş karar) ve istisna
           kaydı aranabilir kalır. Audit satırına da yazılır: kolon okunduğu
           yer, audit (06 §3.20, değiştirilemez) silinemediği yer
      D3 — AD28 `buyerFavorRequiresOverride` (sunucuda hesaplanmış bool)
           döndürür. Kural tek yerde (servis) yaşar; istemci yalnız cevabı
           çizer. Aksi hâlde predicate iki dilde iki kez yazılırdı
    - AD28 `deliveredItemName` admin EKRANINDA gösteriliyor (T130 doğrulaması
      bulgu N2, 2026-08-17 — proje sahibi kararı: sahiplik T131):
      alan AD28 cevabına kadar geliyor ama `DisputeResolveModal.tsx` yalnız
      `systemCheckResult` + `dispute.itemName` render ediyor ve
      `AdminDisputeDetail` TS tipinde alan hiç yok — yani 03 §6.3 Sonuç B'nin
      "admin karşılaştırmayı elle yapmak zorunda kalmaz" ve 07 AD28'in "yan yana
      görür" ifadeleri bugün karşılanmıyor; admin'in kanıtı görmesi için ham
      API'ye bakması gerekiyor. Gelen item'ın adı, işlemin item adının YANINDA
      gösterilmeli (alan yoksa satır hiç çizilmez — NULL "bilinmiyor" değil,
      "bu vaka değil" demektir). i18n: 4 dil parity zorunlu (`i18n:check`).
      KALICI DERS (T137 dersinin ikizi): bir alanın API sözleşmesine ulaşması
      onu KULLANILIR yapmaz — zinciri bitiren yüzeyin de bir sahibi olmalıdır,
      yoksa "taşındı" denip hiç görülmeyen bir kanıt olarak kalır
    - SELLER_FAVOR sonrası terminal disposition (T127 doğrulaması gözlem G3,
      2026-08-17 — proje sahibi kararıyla T131'e ALINDI; T127 raporunda
      "sahiplik T131" deniyordu ama kabul kriteri olarak yazılmamıştı):
      Teslimat penceresi dolmuş ve misdelivery imzası kayıtlı bir satırda admin
      dispute'u SELLER_FAVOR ile kapattığında işlem PAYMENT_RECEIVED'da ve
      süresi dolmuş kalıyordu. Re-entry kapısı (`DeliveryTimeoutRound`) her
      turda `AlreadyResolved` görüp Held dönüyor; timeout hiç ateşlenmiyor,
      teslimat olmuyor, iptal olmuyor. Sonuç yalnız log gürültüsü değil:
      alıcının parası emanette KALICI olarak asılı, tek çıkış belgesiz bir elle
      AD19 admin-cancel.
      D4 — KARAR (seçilen: normal timeout akışına dön): admin KARAR VERDİYSE
           satır Held'den serbest bırakılır ve süresi dolmuş pencere olağan
           seyrini izler (03 §4.4) → CANCELLED_TIMEOUT + alıcıya iade.
           Dayanak: 02 §9.2'nin koruması "SESSİZCE iptal edilmez"dir; bir
           admin o satırı okuyup karar verdiği an iptal sessiz olmaktan çıkar.
           Ayrıca 07 §9.30 SELLER_FAVOR'un payout etkisini zaten
           ITEM_DELIVERED'a koşullamıştır — yani "işlem onaylanır" bu durumda
           "satıcıya öde" anlamına GELEMEZ. Dispute RESOLVED_FOR_SELLER kalır
           (satıcının kaydına kusur yazılmaz), para kanıt kuralına göre akar.
      D5 — KAPI YALNIZ ADMİN KARARIYLA AÇILIR, "dispute artık aktif değil" ile
           değil: `CLOSED` sistemin kendi otomatik çözümüdür (06 §2.10), yani
           kimse bakmamıştır ve orada iptal HÂLÂ sessiz olurdu. Bu ayrım için
           port enum'una `MisdeliveryEscalationOutcome.AlreadyRuledByAdmin`
           eklendi (adapter dispute statüsünü görür, tur göremez — modül yönü
           Disputes → Transactions)
      Reddedilen alternatifler: (a) ITEM_DELIVERED'a taşı + payout — 02 §9.2'nin
      kanıt kuralını admin eliyle deler, alıcı hem parasını hem item'ı kaybeder;
      (c) yalnız rotasyondan düşür — para asılı kalmaya devam eder, T130'un
      kapattığı "otomatik yol kapalı, elle yol belgesiz" sınıfına geri düşer

    --- DÜZELTME TURU KABUL KRİTERLERİ (T131 doğrulaması 2026-08-18 ✗ FAIL;
        proje sahibi onayıyla plana işlendi) ---
    - B1 (BLOKE EDİCİ) — Admin kararıyla serbest bırakılan satır, admin'in
      AKLADIĞI satıcıya kusur yazıyor. D4'ün karar metni "Dispute
      RESOLVED_FOR_SELLER kalır (satıcının kaydına kusur yazılmaz)" diyor ve bu
      iddia bu görevde 03 §6.4'e de yazıldı; ama serbest bırakma
      `PAYMENT_RECEIVED → CANCELLED_TIMEOUT` üretiyor, `DeadlineScannerJob`
      satıcıyı `affected`'a ekleyip `RefreshAsync(evaluateCooldown: true)`
      çağırıyor ve iki tüketici de bu geçişi satıcıya yazıyor:
      `ReputationAggregator.ResponsibleForTimeout` (PAYMENT_RECEIVED → Seller;
      paydaya girer, başarı sayılmaz → `SuccessfulTransactionRate` düşer) ve
      `CancelCooldownEvaluator` (aynı satır cooldown penceresine girer →
      `User.CooldownExpiresAt` damgalanabilir). İtibarın admin düzeltme yüzeyi
      YOK ve satır kalıcı olduğu için ceza da kalıcı. T131 öncesi bu popülasyon
      erişilemezdi (satır sonsuza kadar `Held` dönüyordu) — yani yeni davranış.
      Kodun kendi port sözleşmesi bunu zaten söylüyor: `DeliveryTimeoutDecision`
      `Cancel` doc'u "timeout recorded against the seller" diyor — 03 §6.4'ün
      inkâr ettiği sonucu.
      D6 — KARAR (seçilen: KODU düzelt, seçenek (a)): itibar ve cooldown
           sorumluluk haritaları, `CANCELLED_TIMEOUT` satırının admin kararıyla
           serbest bırakılmış olup olmadığını ayırt etmeli; ayırt ediyorsa
           satıcıya kusur YAZILMAZ — satır iki tüketicide de sayım dışı kalır,
           `CANCELLED_ADMIN`'in 02 §13 gerekçesiyle aynı sınıf (platform kararı,
           kullanıcı kusuru değil). Reddedilen alternatif (b) dokümanı düzeltip
           bedeli kabul etmek: 02 §9.2/§10.4'ün tüm mantığı "bir insan baktı ve
           karar verdi" üzerine kuruludur; o kararın sistemin kendi kaydında
           hiçbir izi olmaması, kapıyı açan gerekçeyi kendi içinde çürütür ve
           kullanıcıya sessiz, geri alınamaz bir ceza bırakır.
           NİHAİ ŞEKİL (2026-08-18, proje sahibi onaylı — üç seçenek sunuldu,
           öneri yönünde karar verildi): ayırt edici, `Transactions` üzerinde
           yeni bir nullable damgadır: `TimeoutReleasedByAdminRulingAt`
           (migration `T131_TimeoutReleasedByAdminRulingAt`, 1 additive kolon,
           seed/CHECK/index yok). Damgayı YALNIZ teslimat turunun admin-serbest
           bırakma kolu yazar (`DeliveryTimeoutRound.ReleasedByAdminRuling`,
           kararın yanında — scanner yalnız `Cancel` görür ve o değerin iki
           üreticisi oradan birbirinin aynısıdır). `ReputationAggregator` ve
           `CancelCooldownEvaluator` damgalı satırı HEM sorgu katmanında
           filtreler HEM switch içinde açık guard taşır (T129'un `REFUNDED`
           bölmesindeki desenin aynısı: sorgunun ileride genişletilmesi sessizce
           yeniden ceza yazmaya başlamamalı). Statü `CANCELLED_TIMEOUT` KALIR —
           B1'in metni bunu şart koşuyor, ayrıca iade akışı/bildirim/state
           machine o statüye bağlı. Reddedilen iki alternatif: (i) kolonsuz
           çıkarım (`MisdeliverySignature` capture'ı olan bir CANCELLED_TIMEOUT
           ancak admin serbest bırakmasıyla oluşabilir) — doğru ama doğruluğu
           BAŞKA bir dosyadaki re-entry kapısının değişmemesine bağlı;
           (ii) statüyü `CANCELLED_ADMIN`'e çevirmek — B1'in metnine aykırı ve
           dalga boyu geniş.
      D6a — DENETLENEN YAN KAYITLARIN TAMAMI (D7'nin gereği): serbest bırakılan
           iptalin ürettiği yan kayıtlar tek tek tarandı —
           `TransactionTimedOutEvent` (yalnız Notifications + Realtime
           tüketicisi), `PaymentRefundToBuyerRequestedEvent` (alıcıya iade,
           doğru), `TransactionHistory` (audit, doğru) ve itibar + cooldown.
           Satıcının kaydına yazan başka yol YOKTUR (fraud flag yolu dahil), yani
           B1'in adlandırdığı iki tüketici bu akışın tamamıdır.
      D7 — KALICI DERS: bir kapıyı açan karar, o kapının ARDINDAKİ akışın
           ürettiği YAN KAYITLARI da denetlemelidir. T129 B3'ün ikizi — orada
           itibar formülünü çalıştıran TETİKLEYİCİ eksikti, burada tetiklenen
           formülün GİRDİSİ yanlış sınıflanıyor; iki kez de "kural yazıldı" ile
           "kural doğru şeyi yazıyor" farklı çıktı.
    - N1 (bloke etmeyen) — `DeliveryTimeoutDecision.Cancel` XML doc'u ikinci
      üreticisi için yanlış: "The platform can PROVE the seller still holds the
      item" diyor, oysa yeni kolda imza item'ın satıcıdan AYRILDIĞINI söyler.
      Doc iki üreticiyi de anlatacak şekilde güncellenir (kanıtlı teslimatsızlık
      + admin kararıyla serbest bırakma) ve "timeout recorded against the
      seller" ifadesi B1'in kararıyla hizalanır. AC1'in kapatmak için var olduğu
      "kod değişti, sözleşme eski sözü veriyor" sınıfının aynısı — bu kez
      daraltma değil genişleme yönünde.
      NİHAİ ŞEKİL (2026-08-18): "timeout recorded against the seller" cümlesi
      doc'tan ÇIKARILDI (tek bir üreticiye aitti); yerine iki üretici de adıyla
      yazıldı — (a) kanıtlı teslimatsızlık (kusur satıcınındır, 06 §3.1 ona
      yazar), (b) admin kararıyla serbest bırakma (imza item'ın satıcıdan
      AYRILDIĞINI söyler, karar satıcıyı aklamıştır ve satır
      `TimeoutReleasedByAdminRulingAt` ile damgalanıp iki haritadan da düşer).
      Çağıranın işi iki kolda da aynıdır; farklı olan satırın satıcının kaydına
      ne ifade ettiğidir ve o bilgi enum'da değil işlem satırında taşınır.
    - N2 (bloke etmeyen) — İlk-tur serbest bırakma, admin'in HİÇ GÖRMEDİĞİ bir
      imzayı iptale çevirebiliyor: dispute daha önce (imza oluşmadan) karara
      bağlanmışsa `EscalateAsync` kolu `AlreadyRuledByAdmin` görüp `Cancel`
      dönüyor (`A_First_Round_Signature_Also_Releases_When_An_Admin_Has_Ruled`
      bu davranışı bilerek sabitliyor). D5'in gerekçesi ("admin o satırı okuyup
      karar verdiği an iptal sessiz olmaktan çıkar") bu vakayı KAPSAMAZ —
      imzayı kimse okumamıştır, dolayısıyla iptal hâlâ sessizdir. Kriterin harfi
      karşılanıyor, muhakemesi karşılanmıyor. Düzeltme yönü: taze imza kararın
      TARİHİNDEN sonra oluştuysa serbest bırakma uygulanmaz (satır yeniden
      eskale edilir veya Held kalır); nihai şekil düzeltme turunda yazılır.
      NİHAİ ŞEKİL (2026-08-18, proje sahibi onaylı — üç seçenek sunuldu, öneri
      yönünde "YENİDEN ESKALE ET" seçildi):
      N2a — KARŞILAŞTIRMA: `Disputes.ResolvedAt` ile imzanın İLK kayda geçtiği
           an (`DeliveryEvidenceCaptures` içindeki en ESKİ MisdeliverySignature
           satırının `ObservedAt`'i). Serbest bırakma yalnız ruling imzadan
           KESİN OLARAK sonraysa uygulanır. İki belirsiz girdi — eşit an ve
           (şema regresyonu olan) NULL `ResolvedAt` — "görmemiş" sayılır: iki
           hata simetrik değildir, gereksiz inceleme admin'in zamanına, yanlış
           serbest bırakma satıcının malına mal olur. EN ESKİ capture okunur, EN
           YENİ değil — sonraki bir turun aynı bulguyu yeniden kaydetmesi,
           kanıtı GÖRMÜŞ bir kararı görmemişe çeviremez.
      N2b — KARARI PORT'UN İKİ UCU BÖLÜŞÜR: tur, imzanın ilk gözlem anını
           `EscalateAsync`'e PARAMETRE olarak verir (ilk turda kendi saati —
           imza o an oluşuyorsa mevcut her karar zorunlu olarak ondan öncedir);
           adapter dispute'u görebilen taraf olduğu için karşılaştırmayı yapar ve
           yeni `MisdeliveryEscalationOutcome.ReEscalatedAfterRuling` döner. Tur
           bu değeri diğer serbest-bırakmayan cevaplar gibi `Held`'e çevirir ve
           damgayı YAZMAZ.
      N2c — YENİDEN ESKALASYON: aynı satır `ESCALATED`'a döner,
           `SystemCheckResult` yeni bulguyla (alıcının dilinde) yazılır,
           `ResolvedAt` TEMİZLENİR (`CK_Disputes_Resolved_ResolvedAt` ikisini
           eşler), `AdminNote` + `AdminId` KORUNUR (sonraki admin kimin ne
           gerekçeyle karar verdiğini görmeli) ve iki tarafa inceleme bildirimi
           gider. Önceki karar kaybolmaz: değiştirilemez `DISPUTE_RESOLVED`
           audit kaydındadır (06 §3.20). Kural 02 §10.2'ye yazıldı — oradaki
           yasak ikinci bir dispute SATIRIDIR, aynı satırın yeni bir OLGUYLA
           yeniden ele alınması değil. `CLOSED` bu kolun DIŞINDADIR (06 §2.10 —
           insan kararı yok, korunacak/gözden geçirilecek bir hüküm de yok) ve
           orada mevcut davranış (hold) zaten para akışını kilitlemiyor.
           Reddedilen iki alternatif: (i) sessizce Held kalmak — D4'ün kapatmak
           için var olduğu fail-frozen sınıfını bu popülasyon için geri getirir
           (para emanette asılı, tek çıkış belgesiz AD19); (ii) dispute'a
           dokunmadan `Transaction`'a triyaj damgası — 02 §10.2 sorusunu
           doğurmaz ama kimsenin okumadığı bir yüzey yaratır (T130 N2 / T137'nin
           "sahipsiz sinyal ölür" dersi).

--- P6: Emeklilik ---

Task T132: Backend bot/dispatch/webhook/recovery yüzeyi silme [RİSKLİ]
  Bağımlılık: T127, T130
  Kabul kriterleri:
    - Bot entity'leri, dispatch job, recovery, admin endpoint'leri kaldırıldı
    - Webhook HMAC/nonce altyapısı KORUNDU (blockchain sidecar paylaşımlı)

  KAPSAM NETLEŞTİRMESİ (proje sahibi onaylı, 2026-08-19 — göreve başlarken
  yapılan ölçüm). Yukarıdaki iki kriterin BİRİNCİSİ T117'de fiilen
  karşılanmıştı: entity, job, `SteamWebhooksController` ve admin uçları o
  dalda silindi (enum'dan değer silmek 136 dosyayı birden kırdığı için
  T117+T118+T132 tek dalda birleşmişti). Ölçüm, geriye ÇALIŞMAYAN AMA DURAN
  bir yüzeyin kaldığını gösterdi — yazıcısı olmayan sabitler ve hâlâ
  yayınlanan sözleşme girdileri. T132 bu turda ONLARI kapatır:
    A — `AuditAction`'ın dört bot değeri (`BOT_STATUS_CHANGED`,
        `BOT_SESSION_FAILED`, `BOT_RECOVERY_ITEM_CREATED`,
        `BOT_RECOVERY_UPDATED`) + `AuditLogCategoryMap` girdileri. Üretimde
        tek yazıcıları yoktu. Silmek kodu KAYNAĞA hizalar: 06 §2.19 tablosu
        bu dördünü zaten içermiyordu (T115 doküman turunda düşmüşlerdi),
        yani kod-doküman driftinin kapanan tarafı koddur.
    B — `VIEW_STEAM_ACCOUNTS` + `MANAGE_STEAM_RECOVERY` permission'ları
        (katalog 14 → 12). Hiçbir endpoint enforce etmiyordu ama AD11
        cevabında yayınlanıyordu — var olmayan bir ekran (S18) için rol
        tanımlanabiliyordu. Sözleşme değişikliği olduğu için doküman yarısı
        AYNI PR'da: 07 §9 tablosu + §9.11 kataloğu + 04 §8.8 matrisi.
        T133a'ya bırakılmadı; kod ile spec bir task boyunca bile çelişik
        bırakılmaz (INSTRUCTIONS §4).
    C — `AdminBotStatusChanged` realtime kanalı (payload + port + SignalR
        event). Üretimde tek çağıranı yoktu; yalnız beş test double'ı
        taşıyordu.
    D — Steam webhook yüzeyinin BACKEND yarısı: `/api/v1/webhooks/steam`
        prefix'i, `steam-sidecar` nonce source'u, `SteamSharedSecret` +
        appsettings/compose env'i. Ardında serve edilen uç yoktu; bu yollar
        T117'den beri zaten 404'tü, dolayısıyla kaldırma davranışı
        değiştirmez ve T133'ü BEKLEMEZ. HMAC/nonce hattının kendisi
        (middleware, `ProcessedNonces`, cleanup job, blockchain dalı)
        ikinci kriter gereği KORUNDU — `BlockchainWebhookEndpointTests`'in
        yabancı-secret izolasyon testi bunu kanıtlamaya devam eder.
    E — emekli katmanı anlatan bayat XML doc / yorumlar.
  KAPSAM DIŞI (sahibi belli, bu turda dokunulmadı): FE `enums.ts` bot
  değerleri / `PlatformSteamBotStatus` / `updateBotRecoveryItem` /
  `useAdminSteamAccounts` → T134 ve T136 · `sidecar-steam` bot+trade
  modülleri ve `SidecarWebhookRouteContractTests`'in adı konmuş istisnası →
  T133 · 03/04/07 custodial DİL kalıntısı → T133a.
  ÖLÇÜLEN YAN BULGU (T132 öncesinden var, T133a'ya yazıldı): 04 §8.8 yetki
  matrisi `VIEW_DISPUTES`/`MANAGE_DISPUTES` satırlarını WP5'ten beri
  taşımıyor — T132 sonrası tablo 10 satır, kod kataloğu 12. Bu turda
  kapatılmadı çünkü kaldırma değil EKLEME işidir ve 04'ün hizalama turu
  T133a'dır; oraya kabul kriteri olarak işlendi (T137'nin kalıcı dersi:
  ölçülen açık, sahibi olan bir kriterde yazılı değilse ölür).

Task T133: sidecar-steam salt-okunur proxy'ye küçültme [RİSKLİ]
  Bağımlılık: T132
  Kabul kriterleri:
    - Sidecar Steam hesap kimlik bilgisi olmadan boot ediyor
    - secrets/, compose ve 08 §9'dan bot credential'ları düştü
    - Steam webhook secret'ının SIDECAR yarısı da düştü (backend yarısı
      T132'de gitti — T132 doğrulaması, gözlem N1): `sidecar-fake/src/
      config.ts` `steamWebhookSecret` bağlaması (bugün tüketicisi YOK),
      `sidecar-fake/README.md`'nin `Webhook__SteamSharedSecret` atfı ve
      `docker-compose.e2e.yml`'daki `STEAM_WEBHOOK_SECRET` satırı
    - `SidecarWebhookRouteContractTests.RetiredWithBotCustodyLayer` listesi
      BOŞALDI: `sidecar-steam`'in bot/trade yayıncıları silindiğinde emekli
      iki yol artık hiç publish edilmiyor, dolayısıyla adı konmuş istisna
      gerekçesiyle birlikte kalkar (bekçi `RetiredPathsAreStillPublished_
      UntilT133` bu turda kaldırılır — kendini iptal etmesi tasarımdır)

  KAPSAM NETLEŞTİRMESİ (proje sahibi onaylı, 2026-08-19 — göreve başlarken
  yapılan ölçüm). Yukarıdaki dört kriter KORUNUR; aşağıdakiler onlara EK
  olarak bu turda kapatılır. Gerekçe T132 doğrulamasının B1 dersidir: bir
  sözleşme girdisi koddan kaldırılırken "hangi doküman bu girdiyi VAAT
  ediyor" sorusu HER MADDE İÇİN AYRI sorulmalıdır. Aşağıdaki altı kalemin
  hiçbirinin başka sahibi YOKTUR — T133a yalnız 03 + 04 + 07'yi kapsar, 08'i
  ve deploy/ops yüzeyini hiçbir görev kapsamıyor:
    F — 08 §2.5 kütüphane tablosu: dört satırın ÜÇÜ (`steam-tradeoffer-
        manager`, `steam-totp`, `steam-user`) bu turda `package.json`'dan
        siliniyor. `steamcommunity` KALIR ve tablodaki amacı daralır —
        oturum/login/confirmation değil, yalnız ANONİM envanter okuma
        (`InventoryService`, 02 §9.2'nin tek aracı). §2.4 zaten "kaldırılan
        bağımlılıklar" derken `steamcommunity`'yi yalnız oturum/çerez
        rolüyle sayıyor; §2.5 o daralmayı yansıtmıyor.
    G — 08 §2.4 polling tablosu (`steam-tradeoffer-manager` built-in
        polling) ve §2.7 hata tablosunun bot/trade satırları ("Bot session
        expired → sidecar otomatik yönetir" + üç trade-offer satırı).
        Başlıklar v3.0'da emekliye ayrıldı, ALT TABLOLAR kaldı — kaldırılan
        koda ait davranış vaat etmeye devam ediyorlar.
    H — `scripts/bootstrap/02-register-bot.sql` + `scripts/bootstrap/
        README.md` adım 2. Script `PlatformSteamBots`'a INSERT ediyor; o
        tablo T117'de DÜŞTÜ (model snapshot'ta sıfır eşleşme), yani script
        bugün ZATEN çalışmıyor. `secrets/README.md` ve DEPLOY_RUNBOOK §G.2
        ona link veriyor → AC2'nin `secrets/` bacağı bu bağı çözmeden
        kapanamaz.
    I — DEPLOY_RUNBOOK: §B `STEAM_BOTS_CONFIG_PATH` satırı · §B
        `WEBHOOK_SECRET` satırının "sidecar'lar" ifadesi (steam sidecar'ın
        imzalayacağı webhook kalmıyor) · §G.0 "Steam bot hesabı" ön koşulu ·
        §G.1 `secrets/steam-bots.json` · §G.2 adım 2 ve adım 7'nin bot
        yarısı · §G.4 kontrol 4-5 ("1/1 bots ready", "Bot credentials
        loaded") · §G.5 bot-MA trade-hold tuzağı.
    J — Grafana `integration-metrics.json` (`skinora_steam_active_bot_
        sessions`) ve `business-metrics.json` (`skinora_steam_trade_offers_
        total`) panelleri: bu turun sildiği iki metriğin TEK tüketicileri;
        yazıcısı olmayan bir panel kalıcı "No data" gösterir.
    K — `sidecar-steam` içinde kriterlerin adını anmadığı ama bot/trade
        katmanına AİT olan kalıntı: `src/webhook/` dizininin tamamı
        (`sendCallback`'in bot/trade dışında sıfır çağıranı var ve emekli
        iki yolu yayınlayan yer burasıdır — AC4 bu dizin gitmeden
        kapanamaz), `src/trade/types.ts` (tamamı trade sözleşmesi;
        `InventoryService`/`TradeHoldService` kullanmıyor), `routes.ts`'in
        üç bot/trade ucu + `normalizeDeps` backward-compat shim'i,
        `HealthController.botStatusFactory` + `buildBotSessionCheck`,
        `metrics.ts`'in iki bot/trade metriği ve `BotSessionExpiredError`
        (bugün zaten sıfır kullanım).
  LOKAL SIR (proje sahibi kararı): `secrets/steam-bots.json` bu turda
  SİLİNİR. Dosya gitignored'dır ve repo'ya hiç girmemiştir; taşıdığı Steam
  hesabı parolası için ROTASYON önerilir — platform artık bot hesabı
  kullanmıyor, ama parola diskte açık metin durdu.
  ARŞİV KAYDI: `DEFERRED_BACKLOG` `P2P-BotCodeArchive` satırı bu turun
  squash sha'sı yazılarak KAPATILIR — satırın kendi tanımladığı zincir
  ("sidecar tarafı T133'te silinecek, sha'sı o zaman eklenecek") burada
  bitiyor.
  İŞARETÇİ SAPMASI — NİHAİ ŞEKİL (doğrulama turu, proje sahibi onaylı,
  2026-08-19): işaretçi **squash sha'sı değil PR numarasıdır** (#248).
  Kriterin harfi bu turda karşılanamaz çünkü sha merge ANINDA doğar —
  satır kendi kapanışını hiçbir zaman yazamıyordu ve T132 turunda tam bu
  yüzden boş kalıp işi T133'e devretmişti. `git log --grep "(#248)"`
  squash commit'i sha'dan bağımsız bulur, yani kriterin ÖZÜ (silme
  commit'inin git geçmişinde işaretlenmesi) karşılanır. Geçmiş halkalar
  sha ile yazılır (T117 `82bff4d`, T132 `eb0e49d`) — sha yalnız KENDİ
  turunda yazılamaz, sonraki turda yazılabilir. T122'nin kalıcı dersi
  gereği sapma KAYNAK dokümana burada kayda geçti.
  KAPSAM DIŞI (sahibi işaretlendi, bu turda dokunulmadı): DEPLOY_RUNBOOK
  §G.4 kontrol 10'un happy path anlatısı hâlâ custodial ("trade offer →
  ITEM_ESCROWED"). Bu bir YENİDEN YAZIMDIR, bu turun sildiği bir şeyin
  sonucu değil; T133b'ye kabul kriteri olarak yazıldı.

Task T133b: DEPLOY_RUNBOOK §G happy path anlatısının P2P'ye çekilmesi
  Bağımlılık: T133
  Dokümanlar: `Docs/DEPLOY_RUNBOOK.md` §G.4 · referans 02 §2.1, 05 §4.1
  Kabul kriterleri:
    - §G.4 kontrol 10'un uçtan uca prova adımı v3.0 P2P akışını anlatıyor:
      emekli `ITEM_ESCROWED` ve "trade offer" adımları yerine satıcı
      onayı → alıcı ödemesi → P2P trade → teslimat doğrulaması (02 §9.2)
      → `COMPLETED` + payout zinciri
    - §G tablolarında kalan custodial adım adı yok
  Neden ayrı task (T133 kapsam netleştirmesi, 2026-08-19): T133 kendi
       sildiği koda ait vaatleri kapatır; §G.4/10 ise T117'de emekliye
       ayrılan bir AKIŞIN anlatısıdır — kaldırma değil yeniden yazım işidir
       ve prova adımlarının canlı stack üzerinde doğrulanması gerekir.

  KAPSAM NETLEŞTİRMESİ (proje sahibi onaylı, 2026-08-20 — göreve başlarken
  kod üzerinden yapılan ölçüm). Yukarıdaki iki kabul kriteri KORUNUR;
  aşağıdaki üç karar onlara EK olarak bu turda uygulandı:
    E1 — KONTROL 10 İKİYE BÖLÜNDÜ (10 + 10a). Kriterin harfi kuyruğu
        ("→ `COMPLETED` + payout zinciri") tek prova adımı olarak istiyor,
        ama o adım TEK OTURUMDA GÖZLENEMEZ: `payout_settlement_days`'in
        tabanı 7 gündür ve admin altına inemez — `SystemSettingsValidator.
        MinimumSettlementDays` sert kural olarak reddeder (02 §4.5.1, T129).
        Yani eski tek satır yalnız custodial DEĞİL, aynı zamanda
        GERÇEKLEŞTİRİLEMEZ bir vaatti ve yeniden yazım onu olduğu gibi
        P2P'ye çevirseydi hata sınıf değiştirerek hayatta kalırdı. KARAR:
        kontrol 10 = tek oturumda gözlenebilen zincir (… → `ITEM_DELIVERED`,
        `PayoutEligibleAt` damgalandı); kontrol 10a = pencere sonrası
        mutabakat + payout kuyruğu. Kriterin ÖZÜ (uçtan uca zincirin P2P
        anlatısı) iki satırın toplamında karşılanır.
    E2 — PROVA KISAYOLU YAZILDI (kontrol 10a'nın altı). Kuyruğu aynı
        oturumda görmek için `UPDATE Transactions SET PayoutEligibleAt =
        SYSUTCDATETIME()`. Kısayol GUARD ZAYIFLATMAZ: satır kuyruğa
        düştükten sonra `SettlementVerificationJob` alıcının envanterini
        gerçekten yeniden okur ve `SettlementVerifiedAt`'i ancak item
        alıcıda durduğu için damgalar — sahte olan tek şey saattir, verdict
        değil. Alternatif ("ayarı düşür") MÜMKÜN DEĞİL (E1'deki taban),
        alternatif ("8 gün bekle") prova reçetesini kullanılamaz kılıyordu.
        Üretimde yasak olduğu satırda açıkça yazılı.
    E3 — İKİ SAHİPSİZ KOMŞU KALEM KAPATILDI (proje sahibi onaylı). İkisi
        de custodial DEĞİL — bayat — ama aynı §G'de duruyor, başka görevin
        kapsamında değil ve provayı yanlış yola sokuyorlardı. Gerekçe T133
        doğrulamasının kalıcı dersi: bir turun kapattığı kalıntı SINIFI, o
        sınıfın kriterde adı geçmeyen komşularını da kapsar.
        · N1 — §G.4 kontrol 8 "59 satır; 19'u configured" diyordu. Gerçek
          **63** (`SystemSettingsCatalog` 63 giriş, `SeedDataTests` 63'ü
          assert eder) ve boot SONRASI 63'ün 63'ü configured'dır (44'ü
          seed'den, 19'u §A env'lerinden). Sapma tek yönlü değildi: aynı
          runbook'un GİRİŞ paragrafı zaten "63 satır" diyordu, yani
          doküman kendi içinde çelişiyordu.
        · N2 — §G.5 trade-hold tuzağı yalnız ALICI kapısını anlatıyordu.
          Ölçüm: kapı üç yerde (satıcı için `TransactionEligibilityService`
          kalıcı bayrakla; alıcı için `TransactionAcceptanceService` ve
          `TransactionReadinessService` canlı probeyle). Daha önemlisi
          P2P'de trade'i SATICI gönderir — hold'un ağır tarafı taraf
          değiştirdi ve tuzak bunu söylemiyordu. Prova sonucu §H.2'nin
          verdict tablosuna bağlandı (iptal mi, dispute mu) çünkü hangisinin
          olacağı item'ın satıcı envanterinden düşüp düşmediğine bağlıdır.
  CANLI STACK NOTU: "Neden ayrı task" gerekçesi prova adımlarının canlı
  stack üzerinde doğrulanmasını istiyor. Bu tur onu YAPMADI — gerçek Steam
  hesabı çifti, `STEAM_API_KEY` ve fonlu Nile testnet cüzdanı gerektiriyor
  ve hiçbiri bu ortamda yok. Anlatının her adımı bunun yerine KOD üzerinden
  doğrulandı (uç adları, state machine geçişleri, cron ifadeleri, ayar
  tabanı); ad doğruluğu için bu daha güçlü, davranış doğruluğu için daha
  zayıf bir kanıttır. Canlı prova ilk gerçek deploy hazırlığına devredildi —
  `DEFERRED_BACKLOG` satırı açıldı.

Task T133a: 03 + 04 + 07 custodial kalıntı turu (doküman hizalaması)
  Bağımlılık: Yok (doküman). T134/T135/T136'dan ÖNCE tamamlanmalı —
       üçü de 03'ü UI akışı, 04'ü ekran spesifikasyonu, 07'yi API
       sözleşmesi olarak okuyor.
  Dokümanlar: 03 §1.1, §3.3/6, §5.3a/3+5, §5.4/1, §8.7 · 04 §2 (akış
       eşleme tablosu), §8.8 (yetki matrisi), §11 (S07 iade bilgisi
       satırları), §16 (admin), §17 (Steam/recovery ekranları) · 07 §7.1,
       §7.5, §8.1, §9.20, §9.22 (+ örnek JSON payload'ları) · 06 §2.19
       (AuditAction kataloğu — aşağıdaki parity kriteri) ·
       referans: 06 §2.13, 07 §9.11
  Kabul kriterleri:
    - 03, 04 ve 07'de item-custody dili kalmadı; emekli status adları yalnız
      "v3.0'da kaldırıldı" biçiminde, emekliliği BELGELEYEN satırlarda geçiyor
    - 04'te (v4.0 P2P sürümü olmasına rağmen kalan kalıntı — T119 doğrulaması,
      2026-08-10): akış eşleme tablosunda emekli ITEM_ESCROWED /
      TRADE_OFFER_SENT_TO_BUYER adları, "çift iade" ifadesi ve üç adet
      "Item'ınız iade edildi" satırı — P2P'de item iadesi diye bir işlem
      yoktur (02 §3.2); admin iptal aralığını "CREATED → TRADE_OFFER_SENT_TO_
      BUYER" diye tanımlayan üç satır; bot recovery / emanet ekranları
      (katman T117'de silindi). §16 Timeout Süreleri tablosunun iki satırı
      T119 doğrulamasında düzeltildi (04 v4.1) — tur o satırları tekrar
      açmamalı
    - Yetki kataloğunun ÜÇ nüshası birebir aynı (12 giriş): kod
      `PermissionCatalog` (T132'de 12'ye indi, NORMATİF) · 07 §9.11
      `availablePermissions` (T132'de hizalandı) · 07 §9 permission tablosu ·
      04 §8.8 yetki matrisi. **İki nüsha bugün eksik** ve ikisi de T132
      ÖNCESİNDEN gelir (T132 kaldırma turuydu, hiçbir satır eklemedi;
      ölçüm o turda yapıldı ve sahibi buraya verildi):
        · 07 §9 tablosu **11 satır** — `MANAGE_SANCTIONS` (T82) eksik
        · 04 §8.8 matrisi **10 satır** — `VIEW_DISPUTES` ("İtirazları
          görüntüle") ve `MANAGE_DISPUTES` ("İtirazları çöz") WP5'ten beri
          eksik
      T132'nin her iki tablonun altına bıraktığı "Bilinen açık" notları bu
      kriter kapandığında SİLİNMELİ — kapanmış bir açığın notu yeni bir
      drift kaynağıdır. Doğrulama: üç tablodaki key kümesini
      `PermissionCatalog.All` ile karşılaştır, fark boş olmalı
    - 06 §2.19 `AuditAction` tablosu kod enum'uyla birebir (bugün **29**
      değer). Tablo BUGÜN 17 satır — kod enum'unun bir ALT KÜMESİ; on iki
      değerin satırı yok: `FRAUD_FLAG_CREATED` / `_APPROVED` / `_REJECTED` /
      `_AUTO_HOLD` (T54), `RECONCILIATION_MISMATCH` (T76),
      `COLD_WALLET_TRANSFER_INITIATED` + `HOT_WALLET_THRESHOLD_BREACHED`
      (T77), `SANCTIONS_LIST_ADDRESS_ADDED` / `_REMOVED` (T82),
      `MAINTENANCE_MODE_CHANGED` (WP7), `TIMEOUT_AUTO_EXTENDED` +
      `PLATFORM_OUTAGE_DETECTED` (WP16). Boşluk T132 ÖNCESİNDEN gelir ve
      birikmelidir — T132 yalnız dört bot değerini SİLDİ, hiçbir satır
      eklemedi; ölçüm T132 doğrulamasında yapıldı (bulgu B2) ve sahibi
      buraya verildi. **Yön normatiftir: eksik olan DOKÜMANDIR** — enum
      değeri silerek hizalama YASAK, her değerin bir yazıcısı vardır.
      Doğrulama: `Enum.GetValues<AuditAction>()` ile 06 §2.19 key kümesini
      karşılaştır, fark boş olmalı. `EnumTests.AuditAction_ShouldHave29Values`
      yorumundaki "NOT full parity" bloğu bu kriter kapandığında SİLİNMELİ
    - 07 §8.1 bildirim tipi kataloğu 06 §2.13 ile birebir (26 tip). Bu katalog
      üç yerde tutuluyor; T118'de 06 ve 03 hizalandı, 07 nüshası bayat kaldı
    - 07 §7.5 detay blok koşulları güncel durumlara göre yazıldı;
      `steamTradeOfferUrl` satırı kodun FİİLEN ürettiği davranışı anlatıyor
      (PAYMENT_RECEIVED + satıcı → alıcının BuyerTradeUrl'i,
      TransactionDetailService:227-234) — emekli TRADE_OFFER_SENT_TO_* değil
    - 07 §7.1 `active` sekmesi "terminal olmayan" tanımına çekildi;
      EMERGENCY_HOLD status olarak listelenmiyor (05 §4.5: IsOnHold bayrağı +
      TimeoutFreezeReason değeri, TransactionStatus üyesi değil)
    - 07 §9.20/§9.22 iade kuralları tablosundan item-iadesi bacakları kaldırıldı
    - Kaynak katmanındaki emekli-status XML doc kalıntıları temizlendi
      (~14 dosya). En yanıltıcı ikisi adı konarak kapatılmalı:
      DisputeService per-type dispute matrisi (emekli durumlar + tek doğru
      kaynak DisputeEligibility'nin WRONG_ITEM@PAYMENT_RECEIVED maddesi eksik)
      ve EscrowedAndTradeOfferNotificationConsumer özetinin iki bacağı da
      "buyer-facing" demesi (biri satıcıya gidiyor)
    - 07 v3.1 + 03 v3.2 sürüm notları yazıldı
  Test beklentisi: Kod değişikliği yalnız XML doc → build 0 warning + mevcut
       süit yeşil. Doküman doğrulaması:
       `grep -n "ITEM_ESCROWED\|TRADE_OFFER_SENT_TO" Docs/03_USER_FLOWS.md
        Docs/04_UI_SPECS.md Docs/07_API_DESIGN.md` → yalnız belgeleyici
       satırlar; ek olarak 04'te "çift iade" ve "Item'ınız iade edildi" yok
  Neden ayrı task (T118 doğrulaması, 2026-08-10): T115 dokümanları v3.0'a
       taşırken 07'de 16 satır, 03'te 6 bölge custodial dilde kaldı. Kod bu
       bölgelerde zaten P2P'ye taşınmış durumda — bu tur dokümanı KODA
       hizalar, davranış değiştirmez. Dağınık cümle yaması yerine tek tur
       tercih edildi (proje sahibi kararı): iki doküman aynı kalıntı sınıfını
       taşıyor ve tek turda yapılması tutarlı sonuç veriyor.

  KAPSAM NETLEŞTİRMESİ (proje sahibi onaylı, 2026-08-19 — göreve başlarken
  yapılan dokuz ajanlı ölçüm, 193 bulgu). Yukarıdaki kabul kriterleri KORUNUR;
  aşağıdaki dört karar onlara EK olarak bu turda uygulandı:
    D1 — EMEKLİ GÖVDELERİN SİLİNMESİ. 07 §9.28 (AD25) ve §9.29 (AD26) ile
        04 §8.7 (S18) kaldırma notu taşıyor AMA altlarında tam sözleşme /
        ekran tasarımı "tarihsel referans" olarak duruyordu. Bu gövdeler
        kriterin doğrulama grep'ini KIRIYORDU (§9.28'in örnek JSON'unda iki
        canlı `ITEM_ESCROWED`) ve §9.29 T132'de enum'dan silinmiş
        `BOT_RECOVERY_UPDATED` audit satırını vaat ediyordu. Aynı 07
        dokümanındaki §9.10 (AD10) ve 03 §8.5 ise TERS konvansiyonu
        kullanıyordu: gövde silinmiş, yalnız kaldırma notu kalmış. KARAR:
        gövdeler silindi, §9.10 desenine hizalandı. Alternatif ("gövdeleri
        koru, emekli adları nötrle") reddedildi: aynı dokümanda iki zıt
        konvansiyon bırakır ve 04 §8.7'nin canlı üsluplu 50 satırlık S18
        tasarımı FE'nin (T136) dayanak olarak okuyabileceği bir kaynak
        kalırdı.
    D2 — 02 §18.2 KAPSAMA ALINDI. Bildirim tipi kataloğunun DÖRDÜNCÜ nüshası
        02 §18.2'deydi ve tümüyle custodial'dı ("item platforma ulaştı",
        "item gönderildi — trade offer'ı kabul et"). Kriter kataloğu "üç
        yerde tutuluyor" diyerek bu nüshayı saymamıştı ve kalan HİÇBİR görev
        (T133b/T134/T135/T136/T137a/T138) 02'yi doküman olarak kapsamıyordu
        — yani kalem SAHİPSİZDİ. KARAR: bu turda kapatıldı (02 v3.7 → v3.8)
        ve tablonun altına kataloğun tek doğru kaynağının 06 §2.13 olduğunu
        söyleyen kaynak notu yazıldı. Gerekçe T137'nin kalıcı dersi: ölçülen
        açık, sahibi olan bir kriterde yazılı değilse ölür.
    D3 — SÜRÜM NUMARALARI DÜZELTİLDİ. Kriterin "07 v3.1 + 03 v3.2 sürüm
        notları yazıldı" ifadesi T118 dönemine (2026-08-10) aittir ve
        BAYATTIR: tur başında 07 v3.7, 03 v3.6 idi, harfiyen uygulansa sürüm
        GERİ giderdi. Ayrıca tur 04 ve 06'ya da dokunuyor, kriter onların
        bump'ından hiç söz etmiyordu — oysa T130 doğrulamasının N1 bulgusu
        tam olarak bunu yasaklamıştı. KARAR: beş doküman da bump'landı —
        02 v3.8 · 03 v3.7 · 04 v4.5 · 06 v6.12 · 07 v3.8, her birine T133a
        changelog girişi. Bonus hijyen: beş dosyanın ALTBİLGİSİ de donmuştu
        (02 "v3.1", 03 "v2.2", 04 "v3.0", 06 "v4.9", 07 "v2.2") — başlıkla
        hizalandı.
    D4 — SINIF YENİDEN ADLANDIRILDI. `EscrowedAndTradeOfferNotificationConsumer`
        adının HER İKİ yarısı da emekli kavramdı; sınıf bugün SELLER_CONFIRMED
        ve PAYMENT_RECEIVED bacaklarını işliyor. KARAR:
        `HappyPathMilestoneNotificationConsumer` olarak yeniden adlandırıldı
        (1 kaynak + 7 test satırı). Davranış nötr: idempotency anahtarı
        `ConsumerName` sabiti ("notifications.transaction-status-changed")
        DEĞİŞMEDİ ve DI kaydı sınıf adına bağlı değil. Bu karar yukarıdaki
        test beklentisinin "yalnız XML doc" ifadesini AŞAR — beklenti
        "yalnız XML doc + davranış-nötr bir yeniden adlandırma" olarak
        okunmalıdır.
  YETKİ PARITY'SİNİN DOĞRULANABİLİRLİĞİ (aynı tur, proje sahibi onaylı):
    Kriter doğrulamayı "üç tablodaki KEY kümesini `PermissionCatalog.All` ile
    karşılaştır" diye tanımlıyor, ama 04 §8.8 tablosunda ANAHTAR YOKTU
    (kolonlar `| Yetki | Açıklama |`) — yani kriterin kendi doğrulama yöntemi
    o nüshada makinece çalışamıyordu ve `VIEW_DISPUTES`/`MANAGE_DISPUTES`
    satırlarının WP5'ten beri gözden kaçmasının sebebi tam olarak buydu.
    KARAR: tabloya `Anahtar` kolonu eklendi ve 12 satırın hepsi dolduruldu;
    ayrıca `EMERGENCY_HOLD` etiketi kod kataloğuna hizalandı ("Emergency hold
    uygula/kaldır" → "İşlemleri acil dondurma/kaldırma"). Etiket
    değişikliğinin FE ayağı T134'e kabul kriteri olarak yazıldı.
  BLOKE ETMEYEN BULGULAR (turda DEĞİŞTİRİLMEDİ, DEFERRED_BACKLOG §9'a altı
  satır olarak yazıldı): 07 §7.5'in `payment.txHash`/`status`/`confirmedAt`
  ve `dispute` blokları sözleşmede vaat ediliyor ama kod sabit `null`
  döndürüyor — sözleşme KASITLI olarak korundu, eksik olan koddur
  (`T133a-PaymentDetailNulls`, `T133a-DisputeBlockNulls`); `REFUNDED` işlem
  iki sayaçta hâlâ "aktif" sayılıyor (`T133a-ActiveCounterRefunded`); üç FE
  nüsha sapması (`T133a-FeI18nEmergencyHoldLabel`,
  `T133a-FePermissionCatalogKeys`, `T133a-Doc02NotificationCopy`).

--- P7: Frontend + test ---

Task T134: FE enum/StatusBadge/Timeline/i18n
  Bağımlılık: T118
  Kabul kriterleri: npm run i18n:check yeşil, 4 dil parity
    - **T133a devri (2026-08-19, ölçüldü):** `frontend/src/types/enums.ts`
      `NotificationType` **28 değer** taşıyor, kod enum'u 26. İki v3.0 tipi
      EKSİK (`PAYMENT_WINDOW_OPEN`, `DELIVERY_EXPECTED`) ve dört emekli tip
      DURUYOR (`ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER`, `ITEM_RETURNED`,
      `ADMIN_STEAM_BOT_ISSUE`); aynı sapma `lib/utils/notification-icons.ts`
      eşlemesinde de var. Kabul: FE nüshası `NotificationType` ile birebir
      (26 değer) ve ikonlar/etiketler yeni iki tipi kapsıyor. Katalog artık
      beş nüshalı (kod + 06 §2.13 + 07 §8.1 + 03 §12 + FE) ve parity'yi
      zorlayan bir bekçi testi yok — yalnız `NotificationTemplateParityTests`
      resx tarafını kapsıyor; bekçi bu turda düşünülmelidir
      (`T133a-Doc02NotificationCopy`)
    - **T133a devri:** `adminRoles.permissions.EMERGENCY_HOLD` i18n etiketi
      dört dilde de "Emergency hold uygula/kaldır" diyor; kod kataloğu ve
      07 §9.11 "İşlemleri acil dondurma/kaldırma" diyor ve T133a'da 04 §8.8
      koda hizalandı (proje sahibi kararı) — yani ekranda basılan metnin
      doküman dayanağı kalmadı. Kabul: dört dilin etiketi kod kataloğuyla
      hizalı (`T133a-FeI18nEmergencyHoldLabel`)

Task T135: StateActionPanel state×rol matrisi
  Bağımlılık: T123, T126
  Kabul kriterleri:
    - ACCEPTED'da satıcıya "hazırım", SELLER_CONFIRMED'da alıcıya ödeme
    - PAYMENT_RECEIVED'da satıcıya trade deep link + alıcıya "aldım"

Task T136: Admin bot sayfaları silme + create-flow metinleri
  Bağımlılık: T132
  Kabul kriterleri:
    - **T133a devri (2026-08-19, ölçüldü):** `frontend/src/lib/admin/permissionCatalog.ts`
      `KNOWN_PERMISSION_KEYS` **14 anahtar** taşıyor, kod kataloğu 12 —
      T132'de silinen `VIEW_STEAM_ACCOUNTS` ve `MANAGE_STEAM_RECOVERY` hâlâ
      listede (dosyanın kendi yorumu "12" diyor) ve aynı iki ölü etiket dört
      i18n dosyasının hepsinde duruyor. S19 sunucudan gelen listeyi çizdiği
      için kullanıcıya yansımıyor — nüsha yanlış, davranış değil. Kabul: FE
      nüshası `PermissionCatalog` ile birebir 12 anahtar, ölü i18n etiketleri
      düştü (`T133a-FePermissionCatalogKeys`)
    - `/admin/steam-accounts` rotası ve `RecoveryQueuePanel` /
      `useAdminSteamAccounts` yüzeyi silindi — doküman dayanağı T133a'da
      kaldırıldı (04 §8.7 ve 07 §9.10/§9.28/§9.29 artık yalnız kaldırma notu
      taşıyor, gövde yok)

Task T137a: E2E harness custodial seed triyajı [ÖLÇÜM GÖREVİ]
  Bağımlılık: Yok. P5 ile PARALEL koşar, hiçbir görevi bloklamaz.
  Dokümanlar: bu bloğun altındaki "E2E ağı karanlıkta" notu
  Kabul kriterleri:
    - `e2e/src/db.ts`in emekli tabloya yaptığı dört atıf (satır 97 · 102 ·
      131 · 284 — `PlatformSteamBots` INSERT/DELETE/SELECT) P2P gerçekliğine
      göre kaldırıldı veya karşılığıyla değiştirildi; harness setup'ı 8 leg'in
      hepsinde spec'lere ULAŞIYOR
    - 8 leg koşuldu ve sonuç leg bazında kayda geçti: hangisi geçiyor, hangisi
      hangi ADIMDA düşüyor (envanter adımı mı, akış sırası mı, data-status mı)
    - Ölçüm T138'in kabul kriterlerine işlendi: "9 spec" ifadesi kaç spec'in
      GERÇEKTEN yeniden yazılacağıyla değiştirildi
  Not (bu görevin ÇIKTISI yeşil leg değil, ÖLÇÜMDÜR): legler bugün spec'lere
       hiç ulaşmadan setup'ta ölüyor — `Invalid object name
       'PlatformSteamBots'`, 8/8 leg'de leg başına TAM 1 iz. Bir spec çalışıp
       fail etse iz sayısı leg'e göre değişirdi; desen "hiç başlamadı"
       imzasıdır. Dolayısıyla "E2E kırık" biliniyor ama "ne kadar kırık"
       bilinmiyor ve T138'in "9 spec" sayısı bugün bir TAHMİN. Harness
       onarıldıktan sonra bazı legler T137 eksikliğinden envanter adımında
       düşebilir — bu bir başarısızlık değil, aranan cevabın ta kendisidir
       (T137'nin aciliyetini ölçer).

Task T137: sidecar-fake sürülebilir envanter [RİSKLİ]
  Bağımlılık: T120 (2026-08-11'de tamamlandı — görev BUGÜN başlatılabilir)
  Kabul kriterleri: steamId başına envanter kontrol edilebiliyor, trade simüle
  Not: Tüm E2E'yi bloklar; P2 ile paralel başlar.
  SIRA DÜZELTMESİ (proje sahibi onayı, 2026-08-17 — T129 doğrulaması tur 3):
       Görev P7 bloğunda yazılı olduğu için fiilen P7'ye ertelenmiş durumda,
       oysa kendi notu "P2 ile paralel başlar" diyor ve tek bağımlılığı T120
       T121'den önce kapandı. Yani **plan değişmiyor, plana UYULUYOR**:
       T137 P5 ile paralel başlatılır, T130/T131'i beklemez.
       GEREKÇE: (1) görev [RİSKLİ] ve E2E'nin tamamının tek blokeni —
       riskini P7'de, kritik yolun üstündeyken keşfetmek mümkün olan en
       pahalı zamanlama; (2) paralelleştirilebilir olduğu için T130/T131'i
       geciktirmez; (3) F7'nin para hareket ettiren görevleri (T126, T127,
       T129 ve sırada T130/T131) uçtan uca ağ hiç çalışmazken iniyor.
       KALICI DERS: bir görevin "paralel başlayabilir" notu, görev listedeki
       SIRASINDA durduğu sürece kendiliğinden uygulanmaz — planın kendi
       talimatı da bir kapı gibi denetlenmelidir, yoksa sessizce ertelenir.

  DÜZELTME TURU (bağımsız doğrulama tur 1 ✗ FAIL, 2026-08-18 — proje sahibi
  onaylı). Doğrulama beş kabul kriterinin beşini de bağımsız olarak yeniden
  üretti ve doğru buldu (canlı HTTP probe + 38/38 unit + dal HEAD CI run
  32146723383 success, bloke edici 14/14). Bloke eden şey KOD DEĞİL, kararın
  ve sahipliğin kaydıdır.

  YAPIM TURUNUN KARARLARI — NİHAİ ŞEKİL (hepsi proje sahibi onaylı, bu bloğa
  kaynak dokümana yazılmak üzere alındı; T122'nin kalıcı dersi gereği):
    D1 — sürülmemiş steamId'nin varsayılanı **BOŞ envanter** (önerilen "eski
         2 item herkese" reddedildi). Kazanç: alıcı SIFIR baseline'la başlar,
         teslimat deltası gerçek dünyadaki hâliyle ölçülür ve seed
         sorumluluğu görünür kalır. Bedeli seçenek metninde yazılıydı ve
         kabul edildi (aşağıdaki ölçüm).
    D2 — custody dönemi trade yüzeyi (`POST /api/trade-offers/send` +
         `/__e2e/trade/*` + `fakeOfferId` + `FAKE_BOT_STEAM_ID` +
         `FAKE_TRADE_ACCEPT_DELAY_MS`) T137'de EMEKLİ edildi. Kanıt:
         backend'de çağıran istemci 0, `/api/v1/webhooks/steam/trade-events`
         ucu yok (ikisi de T117/T118 ile gitti).
    D3 — visibility'de GERÇEK SIDECAR PARİTESİ: 200 / 422 INVENTORY_PRIVATE /
         503 STEAM_UNAVAILABLE, `visibility` gövdede de taşınır. Okunamaz
         cevap `items` dizisi TAŞIMAZ — o çöküş "profil gizli"yi "item
         envanterde yok"a çevirir.
    D4 — `GET /api/trade-hold/:steamId` per-steamId SÜRÜLEBİLİR; varsayılan
         bugünkü değer (MA açık, hold 0) korundu, mevcut akışlar etkilenmedi.
    D5 — yapım sırasında bulunan CI açığı T137'de DÜZELTİLDİ: `frontend-test`
         job'ının `needs`'i `[changes, build]` → `[changes]`. `build` `code`
         filtresine bağlı, `code` ise `sidecar-fake/**` içermiyor; GitHub
         atlanan bir `needs` halkasının ardındaki job'ı da atladığı için
         fake'in unit testleri CI'da HİÇ koşamıyordu. Bloke edicilik
         değişmedi (`ci-gate.needs` aynı). Alternatif (`code`ye
         `sidecar-fake/**` eklemek) reddedildi — fake-only PR'da
         backend+frontend build'ini de tetiklerdi.

  D1'İN ÖLÇÜLEN BEDELİ (advisory e2e, iki run'da birebir tekrarlandı —
  32142151605 ve dal HEAD 32146723383; taban = merge-base main 787b1b3,
  run 32133727298):
    happy-path 0/1→0/1 · cancellation 0/4→0/4 · timeout 1/4→**0/4** ·
    payment-edge 0/6→0/6 · fraud-flags 3/4→**0/4** · emergency-hold 0/3→0/3 ·
    admin-flows 6/7→**4/7** · downtime 0/3→0/3 · **TOPLAM 10/32 → 4/32**
    Mekanizma tek ve deterministik: sürülmemiş steamId boş okunuyor,
    `TransactionCreationService` Stage 5 satıcı envanterini okuyor, create
    `ITEM_NOT_IN_INVENTORY` ile reddediliyor. Legler `continue-on-error` +
    `ci-gate.needs` dışında olduğu için CI Gate etkilenmez.

  DÜZELTME TURU KABUL KRİTERLERİ (B1 — bloke edici; proje sahibi seçimi:
  "T137'de kapat + plana yaz"):
    - `e2e/src/db.ts` `seedHappyPath()` satıcının fake envanterini SEED EDER
      (`api.setFakeInventory(seed.sellerSteamId, …)`), böylece create yeniden
      geçer. Seed YALNIZ satıcıya yapılır — alıcının SIFIR baseline'ı D1'in
      kazancıdır ve korunmalıdır. Tek noktalı harness değişikliği yeterlidir:
      9 spec'in TAMAMI `seedHappyPath()` çağırıyor ve fonksiyon spec'lerin
      `beforeEach` `resetFakeSteamState()` çağrısından SONRA koşuyor. Hiçbir
      spec senaryosuna dokunulmaz (yeniden yazım T138'de kalır).
    - Ölçüm yeniden alınır ve rapora yazılır: hedef, tabandaki 10/32'nin geri
      gelmesi. Sayı 10'un altında kalırsa fark leg bazında açıklanır.
    - Bu blok (D1–D5 + ölçüm) plana yazılmış olur — B1'in doküman yarısı.
    - N1 (bloke etmeyen): §T138'deki "yalnız admin-flows T137'den bağımsız"
      iddiası ölçümle yalanlandı (6/7 → 4/7, üç düşüşün üçü de
      `ITEM_NOT_IN_INVENTORY`); §T138'de düzeltildi.
    - N2 (bloke etmeyen): `e2e/src/db.ts:43-44` yorumu ("Must match the fake's
      inventory item") bayat — fake'in artık VARSAYILAN envanter item'ı yok,
      sabit yalnız `ITEM_CATALOG` şablonuyla eşleşiyor. Yorum güncellenir.

  KALICI DERS (iki dersin kesişimi, ikisi de bu projede daha önce yazıldı):
       Bir kararın BEDELİ ölçülüp kabul edilmiş olsa bile, o bedel KAYNAK
       dokümana yazılmadıkça ve bedeli geri alacak işin bir SAHİBİ
       olmadıkça karar yarım kalır. T122: "onaylanmış kapsam değişikliği,
       kabul kriterlerinin KAYNAK dokümanına yazılmadıkça gerçekleşmemiştir."
       T129 tur 3: "advisory bir sinyal 'bloke etmediği' için değil sahibi
       olmadığı için ölür." T137'de ikisi aynı anda gerçekleşti: bedel yalnız
       task raporunda duruyordu ve seed yükümlülüğü hiçbir task'ın kabul
       kriterinde geçmiyordu — legler advisory olduğu için T138 seed'i hiç
       eklemeden kapanabilir ve kimse fark etmezdi.

Task T138: E2E spec'lerinin yeniden yazımı
  Bağımlılık: T135, T137, T137a
  Not (T129 doğrulaması tur 3, 2026-08-17): Görev ÖNE ALINMADI ve alınmamalı.
       Spec'ler `data-status` değerlerine göre yazılıyor, o değerleri T135
       sabitliyor; T135'ten önce yazılan spec hareketli bir FE'ye karşı
       yazılır ve iki kez yazılır. Ağın bugün karanlık olmasının cevabı
       T138'i öne almak değil, T137a'nın ölçümü + T137'nin paralel
       başlatılmasıdır.
  Kabul kriterleri (T137a ÖLÇÜMÜYLE GÜNCELLENDİ, 2026-08-17 — "9 spec" tahmini
  yerine ölçülen gerçek dağılım; kanıt CI run 32050987594, rapor T137a_REPORT):
    - **7 spec yeniden yazıldı** (21 test — 20'si CI'da ÖLÇÜLDÜ, +1 ölçülmedi
      çünkü happy-path.ui CI matrisinde yok; alttaki üçüncü maddeye bak) —
      akışları emekli custody durumlarına dayanıyor, hepsi bugün
      `ACCEPTED`'da takılıyor:
      happy-path.smoke (1) · happy-path.ui (1) · cancellation (4) ·
      timeout (3/4 — accept-timeout testi P2P'de zaten geçiyor) ·
      payment-edge-cases (6) · emergency-hold (3) · downtime (3)
    - **2 spec noktasal düzeltmeyle kapandı** (yeniden yazım DEĞİL):
      · admin-flows AC1 — `steamAccounts` assertion'ı AD1 DTO'sunda artık
        olmayan bir alana bakıyor (07 §9.1'de yok); assertion kalkar veya
        03 §8.1'in bot bloğu doküman turunda emekliye ayrılır (T133a/T136)
      · fraud-flags high-volume testi — ikinci create'i T128'in
        (SellerId, ItemAssetId) tekillik kapısı `ITEM_ALREADY_LISTED` ile
        reddediyor; ikinci assetId (fake'te `11111111002` mevcut) ile
        kurtulur, ama o item'ın ItemPriceCache satırı yok → fiyat sapması
        flag'i tetiklenmesin diye harness'a ikinci cache satırı gerekir
    - `happy-path.ui.spec.ts` için CI'da leg YOK (matris 8 suite, `test:ui`
      dışarıda) — yeniden yazımı doğrulayacak bir sinyal de yok; T138 ya
      9. leg'i ekler ya da spec'in yalnız lokal koşulduğunu açıkça yazar
    - Yeni specler: alıcı-onay hızlı yolu, delivery timeout -> satıcı kusurlu
      iptal, satıcı-başka-yere-gönderdi -> auto-escalation
    - **Envanter seed sorumluluğu (T137 düzeltme turundan devir,
      2026-08-18):** T137 sonrası sürülmemiş steamId BOŞ envanter okuyor
      (D1), yani her senaryonun satıcı envanteri seed edilmiş olmalı; aksi
      hâlde create `ITEM_NOT_IN_INVENTORY` ile reddedilir. Ortak seed
      `seedHappyPath()` içinde T137 düzeltme turunda kapatıldı; T138 yeni
      veya yeniden yazılan HER spec için (a) ortak seed yetmiyorsa kendi
      `api.setFakeInventory(...)` çağrısını yazar, (b) teslimat kanıtını
      `api.simulateFakeTrade(satıcı, alıcı, assetId)` ile üretir. Alıcının
      SIFIR baseline'ı korunur — teslimat deltası ondan ölçülür.
      · **Bilinen tek vaka, adıyla (T137 doğrulaması tur 2 — G1):**
        `downtime.spec.ts`'in iki testi `api.resetFakeSteamState()`'i test
        gövdesinin İÇİNDE, `seedHappyPath()`'ten SONRA çağırıyor (satır
        167 · 238) ve ortak seed'i siliyor — o iki testte create hâlâ
        `ITEM_NOT_IN_INVENTORY` alıyor (T137 sonrası sekiz leg'de kalan
        TEK `ITEM_NOT_IN_INVENTORY` imzası budur). Yukarıdaki (a) şıkkı
        vakayı sınıf olarak zaten kapsıyor; buraya adıyla yazılmasının
        sebebi T137'nin kendi bloke edici bulgusunun (B1) tam olarak "bir
        şey yalnız task raporunda duruyor" olmasıdır. Düzeltme spec
        senaryosuna dokunmayı gerektirdiği için T137'de kasten
        yapılmadı; leg tabanda da 0/3 olduğu için 10/32 hedefini
        etkilemedi.
  T137 bağımlılığının ölçülen gerekçesi: fake'in `/api/inventory/:steamId`
       ucu `steamId`'yi YOK SAYIYOR (tek sabit liste, satıcı = alıcı envanteri),
       dolayısıyla teslimatın kanıtı (item satıcıdan çıkıp alıcıda görünmeli,
       T125 baseline diff'i) simüle edilemiyor. 8 spec'in 7'si bu yüzden
       T137'siz yeşile dönemez.
       DÜZELTME (T137 doğrulaması tur 1, 2026-08-18 — N1): bu bloğun
       önceki hâli "yalnız admin-flows T137'den bağımsız" diyordu; ÖLÇÜM
       bunu yalanladı. T137 sonrası admin-flows 6/7 → 4/7 düştü ve düşen
       üç testin üçü de `ITEM_NOT_IN_INVENTORY` ile create aşamasında
       kaldı (AC1 · AC2 · AC3) — yani admin-flows da satıcı envanterinin
       seed edilmesine bağımlıdır. T137'den GERÇEKTEN bağımsız olan tek
       şey, admin-flows'un envantere hiç dokunmayan 4 testidir.

Task T139: Ödeme izleyicisinin bağlanması (arm / re-arm / disarm)
  Bağımlılık: T71 (sidecar `POST /api/monitor/start` ucu) · T75 (post-cancel
       izleyicinin kurma/kurtarma deseni) · T123 (`SELLER_CONFIRMED` geçişi) —
       üçü de ✓ Tamamlandı.
  Dokümanlar: 08 §3.4 (aktif izleme parametreleri) · 05 §3.3 (blockchain
       servisi, sweep tetikleyicisi) · 02 §4.4 (tutar doğrulama) · 06 §3.7
       (PaymentAddress / MonitoringStatus)
  Kaynak: `DEFERRED_BACKLOG` §4 `T133b-PaymentMonitorUnarmed` — T133b
       doğrulamasının B4 bulgusu (2026-08-20).
  ÖLÇÜLEN AÇIK (kapsam bu ölçümden çıktı, backlog satırından geniştir):
       aktif ödeme izleyicisinin ÜÇ halkasının üçünün de backend'de sahibi
       yok. (a) KURMA: `IBlockchainSidecarClient` beş metot taşır (derive ·
       post-cancel-start · post-cancel-stop · balances · cold-wallet) ve
       `POST /api/monitor/start`'ı çağıran hiçbir kod yoktur; `MonitorRegistry
       .start` yalnız o route'tan erişilebilir (`routes.ts:56`). (b) KURTARMA:
       `PostCancelMonitorRecoveryHook` POST_CANCEL_* satırlarını host start'ta
       yeniden kurar, ACTIVE satırlarının eşdeğeri yoktur — sidecar registry'si
       in-memory olduğu için sidecar restart'ında açık penceredeki izleyiciler
       sessizce düşer. (c) DURDURMA: `MonitoringStatus.ACTIVE`'i yazan tek yer
       `PaymentAddressAllocator:123`, ACTIVE'den çıkan tek yol iptal kolundaki
       `PostCancelMonitorStarter`'dır — yani mutlu yolda satır sonsuza kadar
       ACTIVE kalır ve `ReconciliationService:183` `!= STOPPED` olan her adresi
       günlük snapshot'a aldığı için kapsam sınırsız büyür.
       Sonuç: gerçek stack'te alıcının transferi `payment-detected` üretmez,
       işlem `SELLER_CONFIRMED`'da sessizce durur. F6 E2E'si yakalayamazdı —
       on-chain bacak backend seam'inde simüle edilir (T110 `/__e2e/payment/*`).
  KAPSAM KARARLARI (proje sahibi onaylı, 2026-08-20 — üçü de öneri kabul
  edildi; gerekçeleriyle birlikte BURAYA yazıldı çünkü sahibi olmayan karar
  ölür, T137'nin kalıcı dersi):
    D1 — Kayıt: iş bir F7 task'ı olarak numaralandırıldı (T139), doğrudan
       fix PR'ı olarak DEĞİL. Kod + test + doküman dokunuşu var ve kabul
       kriterleri bir kaynak dokümanda yazılı olmadan doğrulanamaz.
    D2 — Kapsam TAM YAŞAM DÖNGÜSÜ: arm + self-heal + disarm birlikte. Yalnız
       arm reddedildi: hiç durmayan bir izleyici sızıntısı yaratır ve iptal
       yolunda aynı adresi iki registry (`MonitorRegistry` +
       `PostCancelMonitorRegistry`) birden yoklar.
    D3 — İzleme penceresi SWEEP'E KADAR: `SELLER_CONFIRMED`'da kurulur,
       deposit sweep ile boşalınca durur. `PAYMENT_RECEIVED`'da durdurma
       reddedildi çünkü 02 §4.4'ün fazla-tutar iadesi ve 03 §5.5'in ikinci
       ödeme kolu ödeme onayından SONRA gelen transferi görmeyi gerektirir;
       o kolu kapatmak davranışı gerçek stack'te öldürür ve testler bunu
       yakalamaz (on-chain bacak seam'de simüle). Terminale kadar açık
       tutma da reddedildi: sweep sonrası boş adresi yoklamanın faydası yok.
    D4 — `MonitoringStatus`'e YENİ DEĞER EKLENMEZ (bu turun kararı, D1-D3'ün
       gereği). ACTIVE iki durumu birden taşır ("tahsis edildi, pencere henüz
       açılmadı" ve "kurulu"); ayrımın kaydı sidecar registry'sidir, kolon
       değil. Gerekçe: enum'a değer eklemek migration + kardeş projelerdeki
       parity testleri + `IX_PaymentAddresses_MonitoringStatus_Active` filtre
       listesi demektir ve karşılığında hiçbir davranış kazanılmaz — kolonun
       anlamı ("bu adres platformun aktif izleme yaşam döngüsünde") zaten
       doğrudur. Anlam 06 §3.7'ye AÇIKÇA yazılır; yazılmazsa D4 bir sonraki
       turda yeniden keşfedilecek bir belirsizliktir.
  Kabul kriterleri:
    AC1 — `IBlockchainSidecarClient` `StartMonitoringAsync` +
       `StopMonitoringAsync` taşır ve `HttpBlockchainSidecarClient` ikisini de
       mevcut `SendCommandAsync` üzerinden `api/monitor/start` ve
       `api/monitor/stop` yollarına bağlar. Doğrulama: sidecar'ın
       `startMonitorHandler` zorunlu beş alanı (`address`, `paymentAddressId`,
       `transactionId`, `expectedContract`, `expectedSymbol`) ile gövde
       birebir eşleşir; eksik alan 400 `INVALID_MONITOR_REQUEST` üretir.
    AC2 — ARM: `TransactionReadinessService` `ACCEPTED → SELLER_CONFIRMED`
       geçişiyle AYNI `SaveChangesAsync` içinde `PaymentMonitorStartRequested
       Event`'i outbox'a yayınlar; `PaymentMonitorStartDispatcher` olayı
       tüketip sidecar'ı kurar. Atomiklik zorunlu (09 §13.3): geri alınan bir
       geçiş izleyici kurmamalıdır. `PaymentAddress` satırı yoksa (tahsis
       başarısız veya FLAGGED) yayın atlanır ve AC3 kurtarır — geçiş
       BLOKLANMAZ, çünkü adres yokluğu alıcının ödeyememesidir, satıcının
       hazır olmaması değil.
    AC3 — SELF-HEAL: `EnsurePaymentMonitorJob` (Hangfire, `* * * * *`,
       `EnsurePaymentAddressJob` deseni) kurulu olması gereken kümeyi her
       turda idempotent olarak yeniden kurar. Üç vakayı birden kapatır:
       backend restart · sidecar restart · geçici sidecar arızasında düşen
       outbox teslimi. Sidecar `start`'ı idempotenttir (`started:false`,
       cursor/dedup durumu korunur), dolayısıyla tekrar kurma güvenlidir.
    AC4 — DISARM: (a) iptal devrinde `PostCancelMonitorStarter` aktif
       izleyiciyi ÖNCE durdurur, sonra post-cancel'ı kurar — aynı adresi iki
       registry yoklamaz; (b) pencere kapandığında (sweep CONFIRMED veya
       işlem terminal statüde) `EnsurePaymentMonitorJob` izleyiciyi durdurur
       ve satırı `MonitoringStatus = STOPPED` damgalar. Doğrulama: mutlu yol
       sonunda `PaymentAddress` satırı STOPPED'dır — bugün ACTIVE kalıyor.
    AC5 — Test: kurma/durdurma port metotları (statü eşlemesi dahil),
       dispatcher'ın üç kolu (Success · InvalidRequest terminal · Unavailable
       → yeniden fırlat), readiness servisinin atomik yayını, ve
       `EnsurePaymentMonitorJob`'ın karar tablosu (arm / disarm / dokunma)
       birim + entegrasyon testleriyle kaplanır. Post-cancel ikizlerinin
       mevcut test dosyaları şablondur.
    AC6 — Doküman: 08 §3.4'e aktif izleyicinin YAŞAM DÖNGÜSÜ yazılır (kim
       kurar, hangi anda, kim durdurur) — bugün yalnız parametreler (3 sn,
       20 blok, iki aşamalı sorgu) yazılı, çağıran hiç yazılı değil; 06
       §3.7'ye D4'ün ACTIVE anlamı işlenir; `DEPLOY_RUNBOOK` §G.4'teki elle
       `curl` kurma notu kaldırılır (T133b onu ölçülmüş boşluk olarak
       yazmıştı); `DEFERRED_BACKLOG` `T133b-PaymentMonitorUnarmed` ✅
       işaretlenir.
  Test beklentisi: birim + entegrasyon. Migration YOK (D4 gereği enum ve
       şema değişmez). Yeni SystemSetting yok. Yeni dış bağımlılık yok.
  Doğrulama kontrol listesi:
    - `grep -rn "api/monitor/start" backend/src` → en az bir üretim çağıranı
      (bugün 0)
    - Mutlu yol entegrasyon testi sonunda `PaymentAddress.MonitoringStatus`
      == STOPPED
    - `MonitorRegistry.ts`'nin "backend calls start when a transaction enters
      PENDING_PAYMENT (T44 state)" yorumu güncellenir — o statü T117'de
      emekli edildi, yani yorum hiç var olmamış bir çağıranı emekli bir
      statüyle tarif ediyor

  DÜZELTME TURU (T139 doğrulaması, 2026-08-20 — bir bloke edici bulgu + iki
  bloke etmeyen madde; proje sahibi onayıyla "hepsi aynı dalda kapatılsın"
  kararı verildi ve bulgular BURAYA yazıldı çünkü sahibi olmayan bulgu ölür):
    B1 (BLOKE EDİCİ — S3 Eksik, AC2'nin tüketim kolu) — `PaymentMonitor
       StartDispatcher` DI'a hiç kaydedilmemişti. `OutboxModule.GetMediatRScan
       Assemblies()` tam olarak üç assembly tarar (API host · Notifications ·
       Realtime); `Skinora.Transactions` bunların arasında DEĞİLDİR, bu yüzden
       o assembly'deki üç kardeş handler `TransactionsModule`'de tek tek elle
       kaydedilmiştir ve her birinin üstündeki yorum tam bu tuzağı adlandırır.
       T139 dördüncüyü eklerken kaydı atladı ve yerine "MediatR discovers it by
       assembly scan" diyen bir yorum yazdı — bu assembly için yanlış. Sonuç:
       `IPublisher.Publish` sıfır handler'la sessizce döner, outbox satırı
       PROCESSED damgalanır, sidecar hiç çağrılmaz. **Kusurun sınıfı T139'un
       kapattığı kusurun aynısıdır** (bağlanmamış uç), yalnız yönü terstir:
       orada çağıran yoktu, burada tüketici. Ürün ölmedi çünkü AC3'ün
       reconciler'ı ≤60 sn içinde aynı işi yapıyor — yani AC3, AC2'nin
       yokluğunu MASKELEDİ; bu da bulgunun neden yalnız kompozisyon
       seviyesinde görülebildiğini açıklar.
       KARAR: kayıt eklenir + yanlış yorum düzeltilir + **sınıf kapatılır**:
       `TransactionsModuleNotificationHandlerTests` reflection ile bu
       assembly'deki HER `INotificationHandler<T>`'nin DI'da kayıtlı olduğunu
       zorlar. Bekçinin ayırt ediciliği kanıtlandı — kayıt geçici olarak
       kaldırıldığında test `Unregistered: PaymentMonitorStartRequestedEvent`
       diyerek düşüyor. (Tek örneği düzeltmek yetmezdi: bu assembly'ye eklenen
       bir sonraki handler aynı sessiz düşüşü tekrar ederdi.)
    N1 (bloke etmez — kararın ölçülmemiş bedeli) — D3 pencereyi sweep'e kadar
       açık tutuyor, sweep ise `SettlementVerifiedAt` damgalanmadan
       kuyruklanamıyor ve `payout_settlement_days`'in sert tabanı 7 gündür.
       Yani her deposit adresi teslimattan sonra **bir hafta veya daha uzun**
       süre 3 saniyelik aktif kadansta kalıyor; eşzamanlı izleyici sayısı iki
       saatlik değil bir haftalık hacimle ölçekleniyor ve TronGrid istek
       hacmi buna orantılı. `EnsurePaymentMonitorJob`'ın kendi XML doc'u ise
       "An active payment window is 30-120 minutes" diyerek kendi
       `ArmedStates`'iyle çelişiyordu. Bedel D3'ün kabul edilmiş sonucudur
       (pencereyi kısaltmak 02 §4.4 + 03 §5.5 kollarını öldürür) ama hiçbir
       kaynak dokümanda yazılı değildi. KARAR: doc cümlesi düzeltilir, bedel
       08 §3.4'e yazılır, alarm eşiği `DEFERRED_BACKLOG` →
       `T139-ActiveMonitorQuotaAlarm`'a sahiplendirilir (eşik TronGrid plan
       bütçesi bilinmeden sayı olarak yazılamaz).
    N2 (bloke etmez — AC4(a)'nın yarış hâlinde hayatta kalması) —
       `EnsurePaymentMonitorJob` candidate'ları tek sorguda çekip sırayla
       sidecar çağırıyor ve döngü içinde durumu YENİDEN OKUMUYOR. (a) Arm
       kolu: fetch'ten sonra commit'lenen bir iptal, post-cancel devrini
       yapıp aktif izleyiciyi durdurur; bayat snapshot'tan gelen arm onu
       geri kurar ve satır artık ACTIVE olmadığı için job bir daha bakmaz —
       iki registry aynı adresi sidecar restart'ına kadar yoklar. (b) Disarm
       kolu: aynı yarış STOPPED damgasını POST_CANCEL_24H'in üstüne yazmaya
       çalışır; bu PARA İLGİLİ olurdu (gecikmeli ödeme kurtarma penceresini
       emekliye ayırır) ama `RowVersion` concurrency token'ı (09 §10.4) körü
       körüne ezmeyi `DbUpdateConcurrencyException`'a çeviriyor — yani koruma
       zaten vardı, eksik olan onu ELE ALMAKTI (istisna tüm batch'in damgasını
       düşürüyordu). KARAR: (a) tur sonunda tek ek sorgu ile ACTIVE'den çıkmış
       armed adresler bulunur ve `stop` ile telafi edilir; (b)
       `DbUpdateConcurrencyException` yakalanır, change tracker temizlenir,
       damga bir sonraki tura bırakılır (damgalama idempotenttir).
    Düzeltme turu test beklentisi: B1 için DI bekçisi (`Skinora.API.Tests`),
       N2 için iki entegrasyon testi — devir yarışının her iki kolu da
       sidecar stub'ına eklenen `OnMonitorStart`/`OnMonitorStop` kancalarıyla
       deterministik olarak üretilir.

  DÜZELTME TURU 2 (T139 doğrulaması tur 2, 2026-08-20 — bir bloke edici bulgu
  + iki bloke etmeyen madde; proje sahibi kararı yine "hepsi aynı dalda"):
    B1-2 (BLOKE EDİCİ — AC3'ün özü): `EnsurePaymentMonitorJob` aday kümesini
       tek bir `Take(BatchSize=200)` ile alıyor ve `CreatedAt` ARTAN sırada
       diziyordu. Tavan `EnsurePaymentAddressJob`'dan kopyalandı ama iki
       kümenin davranışı aynı değil: orada işlenen satır kümeden DÜŞER
       (`t.PaymentAddress == null` artık doğru değil), burada arm satırı
       `ACTIVE` bıraktığı için satır pencerenin tamamı boyunca aday kalır.
       Sonuç: küme 200'ü aştığı anda her tur AYNI en eski dilimi mutabık
       kılıyor ve en yeni pencerelere hiç sıra gelmiyor. Bu turun kendi N1
       bulgusu kümenin boyutunu zaten ölçmüştü — `ArmedStates` `ITEM_DELIVERED`
       içerdiği ve sweep 7 günlük tabandan önce kuyruklanamadığı için küme ≈
       **bir haftalık hacim**; eşik ~29 işlem/gün. Aç kalan popülasyon tam
       olarak para-kritik olan: şu anda ödeme yapan alıcının adresi, sıranın
       SONUNDA doğuyor ve önündeki (küme−200) satır drene olana kadar (100
       tx/gün'de ≈ 5 gün) işlenmiyor — 30-120 dakikalık pencere için pratikte
       HİÇ. Böylece AC3'ün kapattığı üç vakadan ikisi (sidecar restart ·
       düşen outbox teslimi) tam da hızlı yolun çalışmadığı anda ölüyor.
       `BatchSize` XML doc'u ise tersini iddia ediyordu ("idle allocations
       ... can never crowd armed windows out of the batch") — başka bir
       kalabalıklaşma kaynağını adlandırıp geçerli olanı atlıyordu.
       KARAR: tek `Take` yerine **tüm aday kümesi sayfalanır** (`PageSize`
       = DB gidiş-dönüş boyu, `MaxAddressesPerRun` = 5000 wedge guard).
       Tavan bir throughput ayarı değil kama korumasıdır ve **çarpıldığında
       WARN loglanır** ("sessiz kırpma tam kapsama gibi okunur" kuralı).
       Bedeli düşük: izleyici başına DAKİKADA bir `start`, sidecar'ın aynı
       adres için zaten 3 saniyede bir yaptığı işin ~%0.5'i.
    N1-2 (bloke etmez — AC5'in ölçülmemiş yarısı): AC5 "kurma/durdurma port
       metotları (**statü eşlemesi dahil**)" diyordu ama
       `HttpBlockchainSidecarClientTests` yalnız `DeriveAddressAsync`'i
       kapsıyordu; `api/monitor/start|stop` YOLU, JSON ALAN ADLARI ve
       `SendCommandAsync`'in dört yönlü statü eşlemesi hiçbir testte
       koşmuyordu. AC5'in "post-cancel ikizlerinin test dosyaları şablondur"
       cümlesi yazarı yanıltmıştı — o ikizlerin de port testi yoktu, yani
       şablon boştu. Eksik olan tam olarak AC1'in konusu: alan adı ya da yol
       sapması sidecar'da 400 `INVALID_MONITOR_REQUEST` üretir, dispatcher
       400'ü TERMİNAL sayar ve ödeme bacağı sessizce ölür.
       KARAR: port testleri yazılır ve **post-cancel ikizlerini de** kapsar.
    N2-2 (bloke etmez — N1 telafisinin dayandığı ölçüm yanlıştı):
       `skinora_blockchain_active_monitors` etiketsiz TEK bir gauge ve iki
       registry (`MonitorRegistry` + `PostCancelMonitor`) ikisi de
       `.set(this.monitors.size)` yazıyordu — yani gauge toplamı değil **en
       son yazanı** raporluyordu, ve `shutdown()` birinden çağrıldığında
       diğeri hâlâ yoklarken düz 0 yayınlıyordu. Kusur T71/T75'ten geliyor
       ama **taşıyıcı hâle getiren T139**: 08 §3.4 kapasite planlamasını bu
       sayıya, `DEPLOY_RUNBOOK` §G.4 kurulum kanıtını bu sayıya,
       `integration-metrics` Grafana paneli ve `T139-ActiveMonitorQuotaAlarm`
       da bu sayıya bağlı.
       KARAR: metrik ADI ve (boş) ETİKET KÜMESİ korunur — mevcut panel
       dokunulmadan doğrulanmış olur — ve iki registry tek bir toplayıcıdan
       (`activeMonitorGauge.ts`) yazar. Post-cancel izleyiciler de aynı
       TronGrid bütçesini tükettiği için toplam zaten kapasite planlamasının
       istediği sayıdır; `help` metni ve 08 §3.4 buna göre düzeltilir.
    Düzeltme turu 2 test beklentisi: B1-2 için sayfa sınırını aşan iki
       entegrasyon testi (arm ve disarm kolları ayrı), N1-2 için port
       yol/gövde/statü testleri, N2-2 için toplayıcı birim testleri +
       gerçek iki registry ile "biri diğerini sıfırlamaz" testi. Üçünün de
       ayırt ediciliği, düzeltmeler geçici olarak devre dışı bırakılıp
       kanıtlanır.
```

---

## 6. Faz Geçiş Kapıları (Gate Check)

Her faz tamamlandığında aşağıdaki kontroller yapılır. Tümü geçmedikçe bir sonraki faza geçilmez.

### 6.1 Genel Gate Check (tüm fazlarda)

| # | Kontrol | Açıklama |
|---|---|---|
| G1 | Task tamamlanma | Bu fazdaki tüm task'lar "tamamlandı" durumunda mı? |
| G2 | Kabul kriterleri | Her task'ın kabul kriterleri karşılandı mı? |
| G3 | Doğrulama kontrol listesi | Her task'ın kontrol listesi geçti mi? |
| G4 | Test | Bu fazdaki tüm task'ların test beklentileri karşılandı mı? |
| G5 | CI | CI pipeline (build + test) yeşil mi? |
| G6 | Regresyon | Önceki fazların testleri hâlâ geçiyor mu? |
| G7 | Boşluk | Traceability matrix'te bu faza ait eşlenip implement edilmeyen öğe var mı? |

### 6.2 Faz-Spesifik Kontroller

| Faz | Ek Kontrol |
|---|---|
| F0 | Docker-compose up ile tüm servisler ayağa kalkıyor mu? Monitoring dashboard'lar çalışıyor mu? |
| F1 | Migration temiz DB'ye hatasız uygulanıyor mu? Seed data doğru mu? Tüm constraint'ler test edildi mi? |
| F2 | Auth akışı uçtan uca çalışıyor mu? Admin RBAC doğru mu? |
| F3 | State machine tüm geçişleri doğru mu? Timeout scheduling doğru mu? Finansal hesaplamalar boundary value ile test edildi mi? |
| F4 | Sidecar'lar health check'te yeşil mi? Webhook iletişimi çift yönlü çalışıyor mu? |
| F5 | Tüm ekranlar 3 breakpoint'te doğru mu? 4 dil çalışıyor mu? SignalR real-time çalışıyor mu? |
| F6 | Tüm E2E senaryoları geçiyor mu? Staging ortamında test edildi mi? |

---

## 7. Traceability Matrix

Bu bölüm her task'ın hangi kaynak doküman öğelerini kapsadığını gösterir. "Implemented" kolonu, task tamamlandığında ✓ olarak işaretlenir.

> **Not:** Öğe ID'leri envanter taramasından alınmıştır. Her satır "bu öğe şu task'ta implement ediliyor" ilişkisini gösterir. Eşlenmeyen öğe = eksik task.

### 7.1 Veri Modeli → Task Eşleme (06)

| Öğe Grubu | Öğe ID Aralığı | Task | Implemented |
|---|---|---|---|
| Enum'lar | DM-026 – DM-048 | T17 | |
| User, UserLoginLog, RefreshToken | DM-001 – DM-003, DM-049–050, DM-105–107, DM-141–143, DM-159–163, DM-166, DM-179–181, DM-195–196, DM-200–202, DM-204 | T18 | |
| UserNotificationPreference | DM-004, DM-060–061, DM-108, DM-201, DM-204 | T23 | |
| Transaction, TransactionHistory | DM-005–006, DM-056, DM-070–075, DM-109–114, DM-141–146, DM-184–186, DM-188, DM-199, DM-206–207 | T19 | |
| PaymentAddress, BlockchainTransaction | DM-007–008, DM-051–054, DM-076–084, DM-115–117, DM-147–149, DM-165, DM-208 | T20 | |
| TradeOffer, PlatformSteamBot | DM-010–011, DM-055, DM-057, DM-088–089, DM-118–119, DM-150–151, DM-182–183, DM-204, DM-208 | T21 | |
| Dispute, FraudFlag | DM-012–013, DM-064, DM-090–093, DM-120–125, DM-154–158, DM-206 | T22 | |
| Notification, NotificationDelivery | DM-014–015, DM-068, DM-094–095, DM-126–128, DM-152–153, DM-198, DM-203, DM-206, DM-208 | T23 | |
| AdminRole, AdminRolePermission, AdminUserRole | DM-016–018, DM-058, DM-062–063, DM-129–132, DM-204 | T24 | |
| Altyapı entity'leri | DM-019–025, DM-059, DM-065–067, DM-069, DM-085–087, DM-096–104, DM-133–139, DM-164, DM-167–175, DM-197, DM-205, DM-209–211 | T25 | |
| Seed data | DM-176–178, DM-193–194 | T26 | |
| Performans index'leri | DM-141–175 | T27 | |
| Migration | DM-187–192 | T28, T04 | |
| Cascade | DM-140 | T04 | |
| Retention | DM-195–199 | T18, T25, T63b (retention job'ları) | |
| Anonimleştirme | DM-200–203 | T36 | |

### 7.2 API → Task Eşleme (07)

| Endpoint Grubu | API ID Aralığı | Task | Implemented |
|---|---|---|---|
| Auth (login, callback, ToS, me, re-verify, authenticator, logout, refresh) | API-001 – API-009 | T29, T30, T31, T32 | |
| User profil ve wallet | API-010 – API-014 | T33, T34 | |
| User settings | API-015 – API-027 | T35, T36 | |
| Steam inventory | API-028 | T67 | |
| Transactions (list, create, eligibility, params, detail, accept, cancel) | API-029 – API-035 | T45, T46, T51 | |
| Disputes | API-036 – API-038 | T58 | |
| Payout issue | API-039 | T60 | |
| Notifications | API-040 – API-043 | T38 | |
| Admin dashboard, flags | API-044 – API-048 | T63, T54 | |
| Admin transactions, settings, steam, roles, users, audit | API-049 – API-065 | T63, T41, T39, T59, T42 | |
| Platform public | API-066 – API-067 | T63a (backend), T86 (frontend) | |
| Telegram webhook | API-068 | T79 | |
| SignalR | API-069 – API-085 | T61, T62 | |
| DTO'lar | API-086 – API-189 | İlgili endpoint task'ları | |
| Validasyonlar | API-190 – API-229 | İlgili endpoint task'ları | |
| Middleware | API-230 – API-241 | T05, T06, T07, T68 | |
| Hata tanımları | API-242 – API-299 | İlgili endpoint task'ları | |

### 7.3 Entegrasyon → Task Eşleme (08)

| Entegrasyon | INT ID Aralığı | Task | Implemented |
|---|---|---|---|
| Steam OpenID | INT-001 – INT-007 | T29 | |
| Steam Web API | INT-008 – INT-011 | T29, T31, T67 | |
| Steam Community (envanter) | INT-012 – INT-015 | T67 | |
| Steam Trade Offer | INT-016 – INT-019, INT-157 | T65, T66 | |
| Steam Sidecar setup | INT-020 – INT-022 | T14 | |
| Steam hata yönetimi | INT-023 – INT-032 | T64, T65, T66 | |
| TRON setup | INT-033 – INT-043 | T15, T73, T74 | |
| HD Wallet | INT-044 – INT-048 | T70 | |
| TRON token config | INT-049 – INT-056 | T15, T73, T74 | |
| Ödeme izleme | INT-057 – INT-067 | T71, T72, T75 | |
| TRON hata yönetimi | INT-068 – INT-076 | T71, T73 | |
| Email (Resend) | INT-077 – INT-099 | T78 | |
| Telegram | INT-100 – INT-116 | T79 | |
| Discord | INT-117 – INT-134 | T80 | |
| Steam Market fiyat | INT-135 – INT-145 | T81 | |
| Cross-cutting | INT-146 – INT-156 | T05, T08, T16, T36 (circuit breaker: T64–T80'de uygulanır) | |

### 7.4 UI → Task Eşleme (04)

| Ekran/Bileşen Grubu | UI ID Aralığı | Task | Implemented |
|---|---|---|---|
| Ortak bileşenler (C01–C17) | UI-026 – UI-042 | T84 | |
| Modal'lar | UI-043 – UI-050 | T84, T87, T94, T100, T101 | |
| Landing page (S01) | UI-001, UI-081–082 | T86 | |
| Auth ekranları (S02, S03, S03a–d) | UI-002 – UI-007, UI-083–086 | T87 | |
| Dashboard (S05) | UI-008, UI-087–091, UI-174, UI-179–181 | T88 | |
| İşlem oluşturma (S06) | UI-009, UI-051–054, UI-092–101, UI-147–152, UI-161, UI-178 | T89 | |
| İşlem detay (S07) | UI-010, UI-055–056, UI-102–132, UI-153–155, UI-175, UI-182–183 | T90, T91, T92 | |
| Profil (S08, S09) | UI-011–012, UI-057–058 | T93 | |
| Hesap ayarları (S10) | UI-013, UI-059–062, UI-156–160 | T94 | |
| Bildirimler (S11) | UI-014, UI-133–135, UI-196–197 | T95 | |
| Admin Dashboard (S12) | UI-015, UI-136, UI-176–177, UI-184–186 | T99 | |
| Admin Flag (S13, S14) | UI-016–018, UI-050, UI-063–065 | T100 | |
| Admin İşlemler (S15, S16) | UI-019–020, UI-048–049, UI-066, UI-187 | T101 | |
| Admin Parametreler (S17) | UI-021, UI-067, UI-071–080, UI-198–199 | T102 | |
| Admin Steam (S18) | UI-022, UI-070, UI-137–139, UI-188–189 | T103 | |
| Admin Roller (S19) | UI-023, UI-047, UI-068, UI-159, UI-190–191 | T104 | |
| Admin Kullanıcı (S20) | UI-024, UI-192–195 | T105 | |
| Admin Audit Log (S21) | UI-025, UI-069, UI-140–142 | T106 | |
| Responsive | UI-162 – UI-168 | T98 | |
| Lokalizasyon | UI-169 – UI-173 | T97 | |
| Suspended session | UI-143 | T87, T90 | |

---

## 8. Boşluk Raporu

Kaynak envanteri taraması ve traceability matrix eşlemesi sonucunda tespit edilen boşluklar:

| # | Açıklama | Durum |
|---|---|---|
| — | Tüm kaynak öğeleri en az bir task'a eşlenmiştir | ✓ Boşluk yok |
| F-INVITE-01 | F5 Gate Check'te tespit edildi: S07 OPEN_LINK `/invite/:token` davet-tüketim rotası uçtan uca eksikti (FE rota + backend token-çözümleme endpoint'i). §7.4 S07→T90/T91/T92 eşlemesinin alt-özelliği; F3'te "T45 OPEN_LINK invitation path → backlog" forward-deferred edilmişti. | ✓ Kapatıldı (F5 gate, 2026-06-13): backend `GET /transactions/by-invite/:token` + FE `/invite/[token]` + 07 §7.5a + 6 test. Detay [`GATE_CHECK_F5.md`](CHECKPOINT_REPORTS/GATE_CHECK_F5.md). |

> **Not:** Bu bölüm ilk yazılım sırasında boştur. İmplementasyon sürecinde yeni boşluklar tespit edilirse buraya eklenir ve ilgili düzeltme task'ları oluşturulur.

---

## 9. Post-MVP Parkı

Aşağıdaki özellikler MVP kapsamı dışıdır (10_MVP_SCOPE §3). Task listesine dahil edilmemiştir ancak kayıp olmaması için kayıt altındadır.

| ID | Özellik | Kaynak |
|---|---|---|
| MVP-OUT-001 | Barter (item-item takas) | 10 §3.1 |
| MVP-OUT-002 | Çoklu item işlemleri | 10 §3.1 |
| MVP-OUT-003 | Trade lock'lu item desteği | 10 §3.1 |
| MVP-OUT-004 | Diğer Steam oyunları (Dota 2, TF2, Rust) | 10 §3.1 |
| MVP-OUT-005 | Platform cüzdanı (bakiye yükleme) | 10 §3.2 |
| MVP-OUT-006 | Ek blockchain ağları | 10 §3.2 |
| MVP-OUT-007 | Fiat ödeme desteği | 10 §3.2 |
| MVP-OUT-008 | Mobil uygulama | 10 §3.3 |
| MVP-OUT-009 | Kullanıcı yorum/değerlendirme sistemi | 10 §3.3 |
| MVP-OUT-010 | Kullanıcıya piyasa fiyatı gösterimi | 10 §3.3 |
| MVP-OUT-011 | Premium üyelik | 10 §3.4 |
| MVP-OUT-012 | Ek gelir kanalları (komisyon dışı) | 10 §3.4 |
| MVP-OUT-013 | KYC | 10 §3.5 |
| MVP-OUT-014 | Admin eskalasyon süreci detayları | 10 §3.6 |
| MVP-OUT-015 | Kullanıcı sözleşmesi içeriği (metin yazılmadı) | 10 §3.6 |
| MVP-OUT-016 | Bildirim mesaj içerikleri — final/polished metinler (MVP'de placeholder metinler kullanılır, T37) | 10 §3.6 |
| MVP-OUT-017 | Platform Steam hesapları yönetim detayları | 10 §3.6 |
| MVP-OUT-018 | Steam Mobile Authenticator kontrol mekanizması detayları | 10 §3.6 |

---

*Skinora — Implementation Plan v0.5*
