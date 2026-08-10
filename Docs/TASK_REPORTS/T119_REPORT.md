# T119 — Reputation + cooldown sorumluluk eşlemesi

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-10

---

## Yapılan İşler

T119 bir **kapsam denetimi** görevidir: sorumluluk eşlemesinin kodu T117 dalında yazılmıştı (`ReputationAggregator` + `CancelCooldownEvaluator`), bu görevin işi o kodu 02 §3.1 / §13 karşısında bağımsız olarak denetlemekti.

**Denetimin sonucu: kod doğru, doküman ve testler değildi.**

1. **Sorumluluk haritası doğrulandı (kod değişikliği YOK).** `ReputationAggregator.ResponsibleForTimeout` ve `CancelCooldownEvaluator.IsResponsibleFor` dört fazın hepsinde 02 §3.1 (v3.0) ile birebir: `CREATED`→alıcı, `ACCEPTED`→satıcı, `SELLER_CONFIRMED`→alıcı, `PAYMENT_RECEIVED`→**satıcı**. `git diff origin/main...HEAD -- backend/src/` **boş**.

2. **06 §3.1 sorumluluk listesi v3.0'a çekildi (06 v6.2).** Liste custodial (v2.0) kaldığı için *"Teslim trade offer timeout'u (adım 6) → **alıcı**"* diyordu. 02 §13 bu listeyi "sorumluluk prensibi burada tanımlıdır" diye **tek doğru kaynak** olarak refere ediyor; 02 §3.1 tablosu ve kod ise satıcı diyor. Üç yerden ikisi doğruyken kanonik olan yanlıştı. Ek olarak adım 3'ün adı düzeltildi ("satıcı trade offer" → **hazırlık onayı**, 03 §2.3) ve her satıra `PreviousStatus` çapası eklendi — denetimin zorluğu dokümanın "adım", kodun `PreviousStatus` dilinde konuşmasıydı; çapa drift'i mekanik olarak gözlenebilir yapar.

3. **Dört fazın tamamı testle kapatıldı.** Denetim öncesi `PAYMENT_RECEIVED`→satıcı eşlemesinin (yani AC1'in) **hiçbir testi yoktu**; `CancelCooldownEvaluator`'ın timeout dalının ise hiç testi yoktu.

4. **Kapsam dışı dört açık kayda geçirildi** (proje sahibi kararı: plan + backlog) — aşağıda §Known Limitations.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/06_DATA_MODEL.md` | §3.1 `CANCELLED_TIMEOUT` sorumluluk listesi v3.0'a çekildi + `PreviousStatus` çapaları + v3.0 notu; sürüm **v6.2** |
| `Docs/11_IMPLEMENTATION_PLAN.md` | T123/T124'e timeout SystemSetting adlandırma kararı, T129'a `REFUNDED` itibar kararı kabul kriteri + gerekçe notları · *(doğrulama turu)* **T133a kapsamı 03 + 07 → 03 + 04 + 07** |
| `Docs/DEFERRED_BACKLOG.md` | §9'a 2 yeni satır (`P2P-NonDeliveryAbuseWindow`, `P2P-DeliveryTimeoutWarning`); aktif satır 34 → **36** |
| `Docs/04_UI_SPECS.md` | *(doğrulama turu — B2)* §16 Timeout Süreleri tablosunda iki satırın sorumluluğu v3.0'a çekildi + hizalama notu; sürüm **v4.1** |
| `.claude/memory/MEMORY.md` | *(doğrulama turu — B1)* T119 kaydı eklendi |
| `backend/tests/.../Integration/Reputation/ReputationAggregatorTests.cs` | +3 test, 2 test adı/yorumu emekli adlandırmadan arındırıldı |
| `backend/tests/.../Integration/Reputation/CancelCooldownEvaluatorTests.cs` | +6 test (4'ü `[Theory]` vakası), history helper'ı eklendi |

**`backend/src/` altında sıfır değişiklik.**

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `PAYMENT_RECEIVED` timeout'u SATICI'ya atfediliyor | ✓ | Kod: `ReputationAggregator.cs:184` (`PAYMENT_RECEIVED => TimeoutResponsibility.Seller`), `CancelCooldownEvaluator.cs:111` (`ACCEPTED or PAYMENT_RECEIVED => isSeller`). Test: `Recompute_Cancelled_Timeout_Delivery_Phase_Hits_Seller` (satıcı 0.5 / alıcı 1.0), `Timeout_Counts_Against_The_Phase_Owner_Only(PAYMENT_RECEIVED, 1, 0)`, `Delivery_Timeouts_Push_The_Seller_Into_Cooldown`. **Negatif prova:** harita alıcıya çevrildiğinde tam olarak bu 3 test kırıldı, başka hiçbiri etkilenmedi (26/29 geçti); mutasyon geri alındı ve `git diff src/` boş doğrulandı. Doküman: 06 §3.1 bu tura kadar **alıcı** diyordu → düzeltildi |
| 2 | `ACCEPTED` timeout'u satıcıya, `SELLER_CONFIRMED` alıcıya | ✓ | Kod: `ReputationAggregator.cs:176-177`, `CancelCooldownEvaluator.cs:111-112`. Test: `Recompute_Cancelled_Timeout_SellerConfirm_Phase_Hits_Seller`, `Recompute_Cancelled_Timeout_Payment_Phase_Hits_Buyer`, `Timeout_Counts_Against_The_Phase_Owner_Only(ACCEPTED, 1, 0)` ve `(SELLER_CONFIRMED, 0, 1)`. Dördüncü faz (`CREATED`→alıcı) de aynı Theory'de ve `Recompute_Cancelled_Timeout_Accept_Phase_Hits_Buyer`'da kapalı |

**Denetimin ikinci yarısı — sorumluluk haritasını besleyen yol.** Harita yalnızca `TransactionHistory.PreviousStatus` üzerinden çalışır; o satır yazılmazsa iptal **hiçbir tarafın** paydasına yazılmaz. Üretimdeki iki timeout yolu da satırı yazıyor ve ardından itibar/cooldown'u aynı DB transaction'ında tazeliyor: `TimeoutExecutor.cs:88` (ödeme fazı, per-tx Hangfire job) ve `DeadlineScannerJob.cs:148` (diğer üç faz, tarayıcı). Bu bağımlılık iki testle (`..._Without_History_Row_...`) davranış olarak sabitlendi.

**Denetimin komşu yüzeyleri (hepsi P2P-doğru bulundu, değişiklik gerekmedi):** `TransactionTimedOutNotificationConsumer` faz×taraf metinleri (teslimat → satıcıya *"Item'ı zamanında göndermediniz"*), `TimeoutSideEffectPublisher` teslimat fazı iade bacağı (alıcıya para iadesi, item iadesi yok), `TransactionCancellationService` cooldown'u yalnız iptali başlatan tarafa uyguluyor.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0 Error / 0 Warning | `dotnet build Skinora.sln -c Release` |
| Unit | ✓ 1330/1330 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — T118 ile aynı (yeni testlerin hepsi Integration) |
| Integration | ✓ 1086/1086 | `--filter "FullyQualifiedName~.Integration"` — T118'deki 1077 + 9 yeni |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` |
| Migration | Yok | Şema değişikliği yok |

Odaklı koşum (`FullyQualifiedName~Reputation`): **29/29** — aggregator 12 + cooldown 11 + refresher 6.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✓ PASS** (bağımsız chat, 2026-08-10 — yapım raporu görülmeden kendi verdict'i oluşturuldu) |
| Bulgu sayısı | **2** — ikisi de **S1 Sapma** (S2 Kırılma / S3 Eksik yok) |
| Düzeltme gerekli mi | Yapıldı — ikisi de merge öncesi aynı dalda kapatıldı |

### Validator kanıtları (bağımsız)

**Kapı kontrolleri:** working tree temiz · main CI son 3 run `success` (`31380447239`, `31380447166`, `31378243789`) · task branch CI **HEAD `a51d8e0`, run [`31402617945`](https://github.com/turkerurganci/Skinora/actions/runs/31402617945)**, CI Gate `success`, bloke edici job'ların hepsi yeşil (yapım raporu bir önceki commit'in run'ını gösteriyordu — `c72f260`/`31401459077`, o da `success`) · dal izolasyonu temiz (yalnız `T119`).

**AC1 + AC2 — kod ↔ doküman:** `ReputationAggregator.cs:173-187` ve `CancelCooldownEvaluator.cs:106-114` dört fazın hepsinde 02 §3.1 (satır 57–64) ile birebir eşleşiyor.

**Negatif prova — validator yapım turunun provasını tekrarlamadı, iki bağımsız mutasyon yaptı:**

| Mutasyon | Kırılan test | Sonuç |
|---|---|---|
| `ACCEPTED` → alıcı (her iki serviste; **yapım turunda denenmemişti**) | `Recompute_Cancelled_Timeout_SellerConfirm_Phase_Hits_Seller`, `Timeout_Counts_Against_The_Phase_Owner_Only(ACCEPTED,1,0)` | tam **2** kırık / 27 geçti |
| `PAYMENT_RECEIVED` → alıcı (her iki serviste) | `Recompute_Cancelled_Timeout_Delivery_Phase_Hits_Seller`, `Timeout_Counts_Against_The_Phase_Owner_Only(PAYMENT_RECEIVED,1,0)`, `Delivery_Timeouts_Push_The_Seller_Into_Cooldown` | tam **3** kırık / 26 geçti |

Her iki mutasyon geri alındı; `git status --short` boş doğrulandı.

**Ölçümler — yapım raporuyla birebir:** build 0E/0W · unit **1330/1330** (lokal tam süit) · integration **1086/1086** (CI job `93502286610` log toplamı; lokal `Skinora.Transactions.Tests` 316/316) · contract **9/9** · odaklı `~Reputation` **29/29**.

**Güvenlik:** secret sızıntısı yok · `backend/src/` sıfır değişiklik → auth/authorization ve input validation etkisi yok · yeni bağımlılık yok.

### Bulgular

| # | Seviye | Açıklama | Etkilenen dosya | Durum |
|---|---|---|---|---|
| B1 | S1 | **Repo memory drift.** `.claude/memory/MEMORY.md`'de T119 için satır yoktu — `task.md` Bitiş Kapısı 8. maddesi atlanmıştı. Validator Adım 0b'nin `grep "T119\b"` kontrolü **yeşil verdi**, çünkü T117 ve T118 kayıtları metinlerinde "T119" geçiriyor: kontrol, "TXX'in kendi kaydı var mı"yı "TXX bir kardeş kayıtta anılıyor mu"dan ayıramıyor (T118 B1 ile aynı sınıf — doğrulama regex'inin kapsamı dar) | `.claude/memory/MEMORY.md` | ✓ kapatıldı |
| B2 | S1 | **04 §16 admin ayar tablosunda sorumluluk ters.** *"Teslim trade offer timeout'u \| Alıcının teslim kabul süresi"* — T119'un 06 §3.1'de düzelttiği cümlenin doğrudan alt katmanı; komşu satır da `trade_offer_seller_timeout_minutes`'ı (hazırlık onayı fazı) "item gönderme süresi" diye anlatıyordu. 04 zaten **v4.0 = P2P sürümü**. Yapım turu bu ikilinin kod tarafını (`SystemSettingsCatalog.cs:54/58`) bulup T123'e yönlendirmiş, spec kaynağını atlamıştı — T134/T136 bu tabloyu okuyacaktı | `Docs/04_UI_SPECS.md` §16 | ✓ kapatıldı (**04 v4.1**) |

### Validator'ın açtığı kapsam dışı konu

**04_UI_SPECS v4.0'da custodial kalıntı yaygın ve sahipsizdi:** akış eşleme tablosunda emekli `ITEM_ESCROWED` / `TRADE_OFFER_SENT_TO_BUYER` adları (sat. 98, 111, 1574, 1740, 1781), *"çift iade"* (sat. 121), üç adet *"Item'ınız iade edildi"* (sat. 1077–1079 — P2P'de item iadesi diye bir işlem yoktur, 02 §3.2), bot recovery / emanet ekranları (sat. 1690–1741, katman T117'de silindi). **T133a yalnız 03 + 07'yi kapsıyordu.** Proje sahibi kararı (2026-08-10): **T133a kapsamı 03 + 04 + 07'ye genişletildi**, kabul kriterine 04 maddesi ve doğrulama grep'ine 04 eklendi. §16'nın iki satırı bu turda düzeltildiği için T133a onları tekrar açmayacak.

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**
- Yeni paket: **Yok**

## Commit & PR

- Branch: `task/T119-reputation-cooldown-audit`
- Commit: `4570e1c` — denetim (doküman + test) · `77724e3` — rapor + status · *(doğrulama turu)* B1 + B2 düzeltmeleri + rapor/status/memory finalize
- PR: [#226](https://github.com/turkerurganci/Skinora/pull/226)
- CI: ✓ PASS — yapım turu HEAD `a51d8e0`, run [`31402617945`](https://github.com/turkerurganci/Skinora/actions/runs/31402617945), CI Gate `success`. Bloke edici job'ların hepsi yeşil (Lint · Build · Unit · Integration · Contract · Migration dry-run · Docker build); `3b. JS test (vitest)` path filtresiyle atlandı (FE değişikliği yok). **8 advisory E2E leg'i kırmızı — T117'den beri beklenen:** tek kök sebep 8 leg'de de `Invalid object name 'PlatformSteamBots'` (e2e seed'i T117 migration'ının düşürdüğü tabloyu temizliyor); `continue-on-error` olduklarından gate'i bloke etmiyorlar, sahiplik T137 → T138

## Known Limitations / Follow-up

Denetimde çıkan, T119'un kabul kriterleri dışında kalan dört açık. Proje sahibi kararı: **plan + backlog'a kaydedilsin** (sadece raporda bırakılmasın).

| # | Açık | Nereye kaydedildi |
|---|---|---|
| 1 | **02 §14.2'nin teslim etmeme yaptırımı kodda yok.** v3.0'da eklenen yuvarlanan pencere kuralı (eşiği aşan ilk tekrar → `ABNORMAL_BEHAVIOR` flag, sonraki → otomatik askı) hiçbir F7 görevine ait değil. Bugünkü caydırıcılık yalnız itibar düşüşü + iptal cooldown'u | `DEFERRED_BACKLOG` §9 → `P2P-NonDeliveryAbuseWindow` 🟡 |
| 2 | **Timeout uyarısı yalnız ödeme fazında var.** 03 §4.5 uyarıyı *"Tüm timeout'lar için"* diye tanımlıyor (02 §3.4 de faz ayrımı yapmıyor), ama `WarningDispatcher` yalnız `SELLER_CONFIRMED`'ı uyarıyor — T48 kapsamı. Dört fazın üçü, bu arada P2P'nin kritik teslimat penceresi, uyarısız | `DEFERRED_BACKLOG` §9 → `P2P-DeliveryTimeoutWarning` 🟡 |
| 3 | **`REFUNDED` itibar formülünün paydasında yok.** Bugün doğru (v2.0'da yalnız admin dispute iadesiydi), ama T129 `delivery_reversed` ile ikinci bir giriş açıyor: trade'ini geri alan satıcı. Formül değişmezse en ağır dolandırıcılık senaryosu itibara hiç yansımaz | `11_IMPLEMENTATION_PLAN` → T129 kabul kriteri + gerekçe notu |
| 4 | **İki timeout SystemSetting'i custodial adında.** `trade_offer_seller_timeout_minutes` / `trade_offer_buyer_timeout_minutes` bugün üretimde **hiç okunmuyor** (yalnız seed + katalog + 4 dil FE etiketi), ama T123/T124 bu iki fazın deadline'ını armlarken kullanacak. Admin panelinde satıcının teslimat penceresini "Alıcı trade offer timeout süresi" adlı kutu yönetir hâle gelir | `11_IMPLEMENTATION_PLAN` → T123 kabul kriteri + rename/koru karar notu, T124'e çapraz referans |

## Notlar

- **Working tree (Adım -1):** temiz — `git status --short` boş.
- **Main CI startup check (Adım 0):** son 3 run `success` — `31380447239`, `31380447166`, `31378243789`.
- **Dış varsayım (Adım 4): yok.** Salt backend denetimi; yeni paket, dış API, plan tier veya ortam varsayımı yok. Yeni SystemSetting/env değişkeni eklenmedi.
- **Kapsam kararı (proje sahibi, 2026-08-10):** "doküman + test" — kod zaten doğru olduğu için üretim kaynağına dokunulmadı; 06 §3.1 düzeltmesi T133a doküman turuna bırakılmadı, çünkü 02 §13'ün refere ettiği kanonik satır yanlış kalırdı.
- **Negatif prova yöntemi:** `PAYMENT_RECEIVED` eşlemesi iki serviste de alıcıya çevrildi, süit koşuldu (3 kırık / 26 geçen), mutasyon geri alındı, `git diff` ile kaynağın main ile birebir olduğu doğrulandı. Guard'ların gerçekten koruduğu bu şekilde kanıtlandı (T118'de kurulan standart).
