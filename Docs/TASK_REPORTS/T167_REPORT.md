# T167 — MASAK akışı

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-21

---

## Yapılan İşler

- **Resmî metin karşılaştırması** (KK5): MASAK Genel Tebliği (Sıra No: 29), Tedbirler Yönetmeliği m. 5 ve m. 24/A (güncel birleşik metin + 7/10/2026 değişikliği), 7518 sayılı Kanun ve SPK III-35/B.1 / B.2 tebliğleri Resmî Gazete ve mevzuat.gov.tr'den okundu; her rakam madde/fıkra ile alıntılandı. İkincil kaynakla iki önemli fark çıktı (aşağıda "Resmî metin karşılaştırması"). Ekran metinleri resmî metne göre yazıldı.
- **Alıcı ödeme ekranı** (SELLER_CONFIRMED, `PaymentInfoBlock`): uyarıların altında "Türkiye'deki bir borsadan ödeyecekseniz" kutusu (`BuyerExchangeNotice variant="full"`): USDT önceden hazır olmalı (48 sa, ilk çekimde 72 sa — m. 4/3), günlük 3.000 $ / aylık 50.000 $ sabit değerli kripto sınırı ve bu sınırı aşan tutarın Türk borsasından tek seferde gönderilemeyeceği (m. 4/4), alıcı beyanı — alıcı Skinora, adres bu işlemin ödeme adresi, her işlemde farklı (24/A-6; T141(b)), 20 karakterlik açıklama (m. 4/5), "kuralları borsanız uygular, hukuki tavsiye değildir" + `/support#masak` bağlantısı.
- **Kabul formu** (CREATED, alıcı, `AcceptForm`): gönder butonunun üstünde kısa not — USDT'yi şimdiden alın (48/72 sa, günlük 3.000 $) + bağlantı (`variant="short"`). Ödeme penceresi kabulden sonra başladığı için not kabulden **önce** de gösteriliyor.
- **Satıcı görünümleri** (`PayoutSourceNotice`): ITEM_DELIVERED mutabakat notu (`SettlementNotice`, ödeme gelmeden önce), COMPLETED ödeme özeti (`SellerPayoutSummary`) ve profil "Satıcı Ödeme Adresi" bölümü (`WalletSection`, tüm ağlar): "Skinora ödeme gönderim adresi (<ağ>)" + kopyala; borsa göndericiyi sorarsa gönderici Skinora'dır, ödemeler her ağda hep aynı adresten çıkar (24/A-6); adres değişirse burada görünür. Adres okunamazsa "göndericiyi işlem hash'inden görebilirsiniz" yazar.
- **Destek (yardım) sayfası**: yeni `masak` bölümü (`#masak` çapası) — giriş paragrafı + `MasakHelpSection` (alıcı listesi, satıcı gönderim adresleri, resmî kaynak satırı). `LegalPage`'e bölüm `id` ve isteğe bağlı `sectionExtras` yuvası eklendi; kullanım şartları / gizlilik metinlerine dokunulmadı (T168).
- **Backend P3** `GET /api/v1/platform/payout-sources` (herkese açık, `public` hız sınırı): `IPlatformWalletAddressProvider`'dan yapılandırılmış her ağın sıcak cüzdan adresi, gösterim biçiminde (EIP-55); yapılandırılmamış ağ listede yok; soğuk / yakıt cüzdanı yok.
- **i18n**: 4 dilde 19 yeni anahtar (`masak.*`, `legal.support.sections.masak.*`); parity OK; yeni untranslatable uyarısı yok (20 → 20).
- **Doküman**: 04 v4.13 (§7.3 SELLER_CONFIRMED + ITEM_DELIVERED notları, S08 cüzdan bölümü), 07 v4.18 (yeni §10.3 P3), `DOC_CHANGELOG.md`.

## Etkilenen Modüller / Dosyalar

- Backend: `backend/src/Skinora.API/Controllers/PlatformController.cs`, `Services/IPlatformPublicService.cs`, `Services/PlatformPublicService.cs`, `Services/PlatformPublicDtos.cs`; test `backend/tests/Skinora.API.Tests/Integration/PlatformPublicEndpointTests.cs` (+3 test, fabrika sahte cüzdan sağlayıcısı)
- Frontend yeni: `frontend/src/components/masak/{BuyerExchangeNotice,PayoutSourceNotice,MasakHelpSection,index}.ts(x)`, `masak.test.tsx`, `frontend/src/lib/hooks/usePayoutSources.ts`
- Frontend değişen: `lib/api/platform.ts`, `app/[locale]/support/page.tsx`, `components/legal/LegalPage.tsx`, `components/profile/WalletSection.tsx`, `components/transactions/detail/{AcceptForm,PaymentInfoBlock,SellerPayoutSummary,SettlementNotice,StateActionPanel}.tsx`, `networkDisplay.test.tsx`, `i18n/messages/{en,tr,es,zh}.json`
- Doküman: `Docs/04_UI_SPECS.md`, `Docs/07_API_DESIGN.md`, `Docs/DOC_CHANGELOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`, bu rapor

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Tebliğ 29'un etkisi (bekleme, günlük 3.000 $ sınırı, karşı tarafı beyan) ödeme ekranlarında ve yardım metinlerinde, 4 dil, i18n anahtar eşitliği | ✓ | Alıcı: `PaymentInfoBlock` → `BuyerExchangeNotice full` (bekleme, sınır, beyan, açıklama); `AcceptForm` → `short`. Satıcı: `SettlementNotice`/`SellerPayoutSummary`/`WalletSection` → `PayoutSourceNotice` (gönderici beyanı). Yardım: destek sayfası `#masak`. `npm run i18n:check` → "i18n parity OK — 4 locales, 1595 keys each" (öncesi 1576; +19). `masak.test.tsx` "the official figures in every locale" her dilde 48/72/3.000/50.000/20/32940 rakamlarını arar. Bekleme metni resmî metne göre "48 sa, ilk çekimde 72 sa" (plandaki "48–72" aralığı değil — aşağıda fark 1). |
| 2 | Payout'lar ağ başına tek sabit adresten çıkıyor (ölç; değilse düzelt); beyan edilecek adres kullanıcıya gösteriliyor mu | ✓ | **Ölçüm — değişiklik gerekmedi:** (a) TRON `sidecar-blockchain/src/transfer/TransferService.ts` `payout()` yalnız sıcak cüzdan adresi ve sıcak cüzdan anahtarıyla imzalar (`fromAddress` = `hotWalletAddress`) (`HOT_WALLET_ADDRESS` tek ortam değişkeni); `payoutHandler` gövdesinde depozit indeksi alanı yok. (b) Ethereum `sidecar-ethereum/src/transfer/TransferService.ts` `payout()` → `signer: 'HOT', depositIndex: null, from: requireHotWallet()`; açılışta `checkWalletKeys` sıcak anahtarın `ETHEREUM_HOT_WALLET_ADDRESS`'i kontrol ettiğini doğrular. (c) Backend: `SELLER_PAYOUT` satırını oluşturan tek yer `SellerPayoutQueueJob.cs:312`; `HttpBlockchainTransferClient.BuildRequest` payout gövdesinde depozit alanı yok; `OutboundTransferExpectation.Sender` payout göndericisini ağın sıcak cüzdanı sayar; admin cüzdan ekranı servis/back-end sıcak adres farkını `AddressMismatch` ile işaretler. Mevcut testler: ETH `TransferService.test.ts` "payout — what gets signed" (`tx.from == HOT.address`), TRON `TransferService.test.ts` "broadcasts from hot wallet with the USDT contract". **Gösterim (yeni):** P3 + `PayoutSourceNotice` — satıcının işlem ekranı (ITEM_DELIVERED, COMPLETED), profil, yardım sayfası. Testler: `PlatformPublicEndpointTests.PayoutSources_*` (3), `masak.test.tsx` "PayoutSourceNotice" (4). |
| 3 | T141(b) kararı uygulanmış (depozit işlem başına) — ölç, kanıtla | ✓ | `PaymentAddressConfiguration.cs:76-89`: `UQ_PaymentAddresses_TransactionId` (işlem başına tek depozit), `UQ_PaymentAddresses_Network_Address`, `UQ_PaymentAddresses_Network_HdWalletIndex` (aynı adres/indeks iki işleme verilemez); `PaymentAddressAllocator.cs:127` yeni indeks `MAX(HdWalletIndex)+1` (ağ başına). Mevcut entegrasyon testleri: `PaymentAddressAllocatorTests.Increments_Index_Across_Successive_Transactions`, `HdWalletIndex_Is_Counted_Per_Network`, `Second_Allocate_For_Same_Transaction_Returns_AlreadyExisted_Without_Calling_Sidecar`. Kullanıcı başına adres kodu yok. Sonucu metinlere de yansıdı: alıcıya "her işlemin ödeme adresi farklıdır — önceki işlem için kaydettiğiniz adrese göndermeyin". |
| 4 | Alıcının USDT'yi önceden tutması gerektiği (48–72 sa çekilemez, ödeme penceresi kısa) ödeme ekranında ve yardımda | ✓ | `masak.buyer.holdInAdvance` (ödeme ekranı, kalın ilk madde) + `masak.buyer.short` (kabul formu) + yardım sayfası `holdInAdvance` + `masak.help.paymentWindow`. Test: `masak.test.tsx` "full: the wait…", "short (accept form)…", "support page — MASAK section". Not: plandaki "ödeme penceresi 15–60 dk" bir platform kuralı değil — pencereyi satıcı, admin'in `payment_timeout_min/max/default_minutes` aralığında seçer (02 §3.1; seed'de "Unconfigured"). Metin sabit bir dakika yazmaz; "ödeme süresi bu bekleme süresinden kısaysa yeni alınan USDT zamanında gelmez" ve "ödeme süresi işlem sayfasında geri sayım olarak görünür" der. |
| 5 | MASAK ayrıntıları Resmî Gazete metnine karşı doğrulandı | ✓ | Aşağıdaki "Resmî metin karşılaştırması" bölümü (kaynak URL'leri, erişim tarihi 2026-10-21, madde/fıkra alıntıları, ikincil kaynakla farklar). Tebliğ 29 m. 4 ve Yönetmelik 24/A birleşik metni yerel indirmeden ayrıca `grep` ile tekrar okundu. |

## Resmî metin karşılaştırması

Erişim tarihi (hepsi): **2026-10-21**. Bu bölüm kayıt amaçlıdır; yorum ve hukuki değerlendirme içermez.

### Kaynaklar

| # | Metin | Resmî Gazete | URL |
|---|---|---|---|
| 1 | MASAK Genel Tebliği (Sıra No: 29) — kripto varlık hizmet sağlayıcıların müşterileriyle ilişkilerinde sıkılaştırılmış tedbirler | 28/6/2025 – 32940 (m. 6: yayımı tarihinde yürürlüğe girer) | https://www.resmigazete.gov.tr/eskiler/2025/06/20250628-4.htm |
| 2 | Suç Gelirlerinin Aklanmasının ve Terörün Finansmanının Önlenmesine Dair Tedbirler Hakkında Yönetmelik — güncel birleşik metin (m. 5, m. 24/A) | — | https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=200713012&MevzuatTur=21&MevzuatTertip=5 |
| 3 | Yönetmelikte değişiklik — CK 9305 (24/A'yı ekledi; yürürlük 25/2/2025) | 25/12/2024 – 32763 | https://www.resmigazete.gov.tr/eskiler/2024/12/20241225-1.pdf |
| 4 | Yönetmelikte değişiklik — CK 11845 (m. 5, 16, 24, 24/A, 38; eşik 15.000 → 30.000 TL) | 7/10/2026 – 33393 | https://www.resmigazete.gov.tr/eskiler/2026/10/20261007-9.pdf |
| 5 | 7518 sayılı Sermaye Piyasası Kanununda Değişiklik Yapılmasına Dair Kanun | 2/7/2024 – 32590 | https://www.resmigazete.gov.tr/eskiler/2024/07/20240702-1.htm |
| 6 | SPK Tebliğ III-35/B.1 — Kripto Varlık Hizmet Sağlayıcılarının Kuruluş ve Faaliyet Esasları | 13/3/2025 – 32840 | https://www.resmigazete.gov.tr/eskiler/2025/03/20250313-5.htm |
| 7 | SPK Tebliğ III-35/B.2 — Çalışma Usul ve Esasları ile Sermaye Yeterliliği | 13/3/2025 – 32840 | https://www.resmigazete.gov.tr/eskiler/2025/03/20250313-6.htm |

Erişim notları: #3 ve #4'ün RG PDF'leri taranmış görüntü, metin olarak okunamadı; değişiklikler #2'deki birleşik metinden ve dipnotlarından alındı (dipnot (3)/(4) ve (9): "7/10/2026 tarihli ve 33393 sayılı Resmî Gazete'de yayımlanan 11845 sayılı Cumhurbaşkanı Kararının … 'Onbeşbin TL' ibareleri 'Otuzbin TL' şeklinde değiştirilmiştir"). masak.hmb.gov.tr mevzuat sayfası JavaScript ile yüklendiği için içerik alınamadı. Tebliğ 29'u değiştiren sonraki bir MASAK tebliği bulunamadı (Sıra 30: uyum görevlisi lisansı; Sıra 32: Sıra 19'da uzaktan kimlik doğrulama; Sıra 33: Sıra 5'te tutar; Sıra 34: BM listeleri — Sıra 34 yalnız ikincil özetten görüldü; Sıra 31'in konusu doğrulanamadı).

### Rakamlar — madde ve birebir alıntı

| Rakam | Resmî metin | Birebir alıntı | İkincil kaynak / T141 notu | Uyuşuyor mu |
|---|---|---|---|---|
| **48 sa / 72 sa** | Tebliğ 29 m. 4/(3) | "Platformlar, Tedbirler Yönetmeliğinin 24/A maddesinin altıncı ve yedinci fıkraları kapsamında aracılık ettikleri kripto varlık çekim işlemlerinde, başka platformlara yapılan transfer işlemleri dahil, kripto varlık transferini, transfer edilecek kripto varlığın alım, takas veya yatırma işleminden en az 48 saat sonra gerçekleştirir. Bu süre, ilk kripto varlık çekim işlemleri bakımından en az 72 saat olarak uygulanır." | (a) bazı ikincil kaynaklar: "Travel Rule bilgisi eksik transferde 48 sa yurt içi / 72 sa yurt dışı bekletme"; T141: "borsada yeni alınan kripto 48–72 saat çekilemez" | **Kısmen.** Yurt içi / yurt dışı ayrımı ve "bilgi eksikliği" bağlantısı resmî metinde **yok**. Doğrusu: alım/takas/yatırmadan **en az 48 sa** sonra; **ilk çekimde en az 72 sa**. T141'in "yeni alınan kripto çekilemez" özü doğru, "48–72" aralığı yanlış okunabilir → ekran metni "48 sa, ilk çekimde 72 sa" yazıldı. |
| Bilgi eksik transfer | Yönetmelik m. 24/A-(3) | "…kripto varlık transfer mesajı alan kripto varlık hizmet sağlayıcı, bu mesajı gönderen kripto varlık hizmet sağlayıcıdan eksik olan bilgilerin tamamlanmasını talep eder. Bilgilerin tamamlanmaması halinde söz konusu kripto varlık transferini iade eder." | (a) | Metinde saat yok (iade kuralı). |
| **3.000 $ / gün, 50.000 $ / ay** | Tebliğ 29 m. 4/(4) | "Platformlar tarafından aracılık edilen, bir veya birden fazla resmî para birimi de dahil olmak üzere başka bir değere, hakka veya bunların birleşimine referans vererek istikrarlı bir değeri korumayı amaçlayan kripto varlıkların çekim işlemlerinde, başka platformlara yapılan transfer işlemleri dahil, günlük 3.000 ABD Doları ve aylık 50.000 ABD Doları karşılığı tutarda limit uygulanır. Ancak Tedbirler Yönetmeliğinin 24/A maddesinin birinci fıkrasının uygulanması durumunda bu limitler 2 kat uygulanabilir." | (b) "günlük 3.000 $ sabit para sınırı" | **Evet**, eksik anlatılmış: yalnız sabit değerli kripto (stablecoin) **çekimine**; ayrıca **aylık 50.000 $**; 24/A-1 uygulanırsa **2 kat**. İlk transfer / beyan yokluğu gibi bir koşul metinde yok. |
| **20 karakter açıklama** | Tebliğ 29 m. 4/(5) | "Platformlar, tüm kripto varlık transfer işlemlerinde müşteriden işlemin mahiyetine ilişkin en az 20 karakter uzunluğunda bir işlem açıklaması temin eder." | — (T141'de yoktu) | Yeni bilgi; ekrana eklendi. |
| **Karşı taraf beyanı — kayıtsız cüzdan** | Yönetmelik m. 24/A-(6) | "Herhangi bir kripto varlık hizmet sağlayıcı nezdinde kayıtlı olmayan bir cüzdan adresine gönderilen veya böyle bir cüzdan adresinden alınan kripto varlık transferlerinde kripto varlık hizmet sağlayıcı nezdinde söz konusu transferin tarafı olan müşteriden; kayıtlı olmayan cüzdan adresi sahibi gönderici ya da alıcının gerçek kişilerde adı ve soyadı, tüzel kişilerde ise unvan bilgisi ile adresi veya doğum yeri ve tarihi veya müşteri numarası, vatandaşlık numarası, pasaport numarası, vergi kimlik numarası gibi kişiyi belirlemeye yarayan bilgilerden en az birine ilişkin beyan alınır." | (c) "Tebliğ 29'un karşı tarafı beyan kuralı" | **Evet**, ama kaynak **Tebliğ 29 değil, Yönetmelik m. 24/A** (CK 9305 ile eklendi, yürürlük 25/2/2025). Hem gönderim (alıcı) hem alım (satıcı) yönünü kapsar. |
| Karşı taraf beyanı — bilgi paylaşmayan yurt dışı sağlayıcı | Yönetmelik m. 24/A-(7) | "Yurt dışında yerleşik ve kendi mevzuatına göre gönderene ve alıcıya ilişkin bilgileri paylaşma yükümlülüğü bulunmayan kripto varlık hizmet sağlayıcı veya kripto varlık transfer yetkisini haiz bir finansal kuruluştan alınan veya bunlara gönderilen kripto varlık transferinde, … müşteriden; göndericinin ya da alıcının … ilişkin beyan alınır." | (c) | Evet. |
| Yeterli bilgi yoksa | Yönetmelik m. 24/A-(8) | "…İşlemin tarafları hakkında yeterli bilgi elde edilememesi durumunda transferin gerçekleştirilmemesi veya söz konusu finansal kuruluşla yapılan işlemlerin sınırlandırılması ya da iş ilişkisine son verilmesi hususları göz önünde bulundurulur." | — | — |
| **Eşik ≥ 30.000 TL** (gönderen bilgisi + teyit) | Yönetmelik m. 24/A-(1) | "Kripto varlık hizmet sağlayıcılar tarafından aracılık edilen Otuzbin TL veya üzeri kripto varlık transfer işlemine ilişkin mesajlarda, gönderenin; a) Adı ve soyadına … b) Cüzdan adresine … c) Adresi veya doğum yeri ve tarihi veya müşteri numarası … en az birine, yer verilmesi zorunlu olup bu bilgilerin doğruluğu ayrıca teyit edilir." | (d) "15.000 TL eşiği" | **Eskimiş.** 15.000 TL 25/2/2025–6/10/2026 arasında geçerliydi; CK 11845 ile **7/10/2026'dan beri 30.000 TL**. |
| Eşik < 30.000 TL | Yönetmelik m. 24/A-(2) | "…Otuzbin TL'nin altındaki kripto varlık transfer mesajlarında gönderene ve alıcıya ilişkin olarak birinci fıkranın (a) ve (b) bentlerinde belirtilen bilgilere yer verilir. Bu bilgilerin teyidi zorunlu değildir." | (d) | Aynı not. |
| Kimlik tespiti 30.000 TL | Yönetmelik m. 5/(1)(b), (c) | (b) "…üçyüzyetmişbin TL (kripto varlık hizmet sağlayıcılar için otuzbin TL) veya üzerinde olduğunda," (c) "Elektronik transferler ile kripto varlık hizmet sağlayıcılar tarafından gerçekleştirilen kripto varlık transferlerinde işlem tutarı … otuzbin TL veya üzeri olduğunda," | (d) | Önceki değer 15.000 TL; CK 11845 ile 30.000 TL. |
| 1.000 USD eşiği | — | **Resmî metinde bulunamadı** | (d) bazı ikincil kaynaklar | Yok. |
| İstisna | Tebliğ 29 m. 4/(6), (7), (8) | (6) likidite sağlayıcılığı / piyasa yapıcılığı / arbitraj amaçlı transferde, müşteri bazında yönetim kurulu onayıyla 3. ve 4. fıkralar uygulanmayabilir; (7) "Saklama kuruluşlarının platform müşterileri nam veya hesabına gerçekleştirecekleri transferler bu maddedeki kısıtlamalara tabidir."; (8) platform–saklama kuruluşu arası sermaye piyasası transferleri hariç. | — | Skinora kullanıcısı için ilgili değil. |

İkincil kaynaklar (karşılaştırma için): Cumhuriyet — https://www.cumhuriyet.com.tr/ekonomi/kripto-varliklara-72-saat-kurali-geldi-masak-tan-yeni-teblig-2413299 · DonanımHaber — https://www.donanimhaber.com/kripto-para-duzenlemesi-yayinlandi--193215 (ikisi 48/72 saati ve 3.000/50.000 $'ı doğru aktarıyor; yurt içi/yurt dışı ayrımı ve Travel Rule bağlantısı başka ikincil kaynaklardan gelmiş).

### Resmî metinden çıkan, ekrana yansıyan okuma

- Bu yükümlülükler **Türk platformlarınındır** (borsa); Skinora'ya değil kullanıcının borsasına uygulanır. Skinora'nın depozit ve sıcak cüzdan adresleri borsa açısından "herhangi bir kripto varlık hizmet sağlayıcı nezdinde kayıtlı olmayan" cüzdandır → alıcının çekimi 24/A-6 kapsamına girer (m. 4/3 bekleme, m. 4/4 sınır, m. 4/5 açıklama, alıcı beyanı); satıcının borsasına gelen payout da 24/A-6 kapsamındadır (gönderici beyanı).
- **Yorum gerektiren açık nokta** (karar vermedik): m. 4/3'teki "24/A maddesinin altıncı ve yedinci fıkraları kapsamında" ile "başka platformlara yapılan transfer işlemleri dahil" ifadelerinin birlikte okunuşu; ve m. 4/4'teki "2 kat" koşulunun pratikte hangi transferleri kapsadığı. Ekran metni "borsanız bazı durumlarda iki katını uygulayabilir" der, kesin konuşmaz.

### 7518 / SPK — sahibe bilgi (karar vermiyoruz)

Aşağıdaki metinler yalnız kayıt içindir; Skinora'nın kapsama girip girmediğini **söylemez**. Kapsam değerlendirmesi hukukçunun işidir (avukat incelemesi proje sahibi kararıyla sonraya bırakıldı, 11 §F9).

- **6362 m. 3 (7518 m. 1 ile eklenen bentler):**
  - "cc) Kripto varlık hizmet sağlayıcı: Platformları, kripto varlık saklama hizmeti sağlayan kuruluşları ve bu Kanuna dayanılarak yapılacak düzenlemelerde kripto varlıkların ilk satış ya da dağıtımı dâhil olmak üzere kripto varlıklarla ilgili olarak hizmet sağlamak üzere belirlenmiş diğer kuruluşları,"
  - "çç) Kripto varlık saklama hizmeti: Platform müşterilerinin kripto varlıklarının veya bu varlıklara ilişkin cüzdandan transfer hakkı sağlayan özel anahtarların saklanmasını, yönetimini veya Kurulca belirlenecek diğer saklama hizmetlerini,"
  - "dd) Platform: Kripto varlık alım satım, ilk satış ya da dağıtım, takas, transfer, bunların gerektirdiği saklama ve belirlenebilecek diğer işlemlerin bir veya daha fazlasının gerçekleştirildiği kuruluşları,"
- **6362 m. 35/B-(1) (7518 m. 3):** "Kripto varlık hizmet sağlayıcıların kurulabilmesi ve faaliyete başlaması için Kuruldan izin alınması zorunlu olup, bunlar münhasıran Kurulca belirlenecek faaliyetleri yerine getirir."
- **6362 m. 109/A-(1) (7518 m. 12):** "İzin almaksızın kripto varlık hizmet sağlayıcı olarak faaliyet yürüttüğü tespit edilen gerçek kişiler ve tüzel kişilerin yetkilileri üç yıldan beş yıla kadar hapis ve beş bin günden on bin güne kadar adli para cezası ile cezalandırılırlar."
- **SPK III-35/B.1 m. 4/(1):** (i) kripto varlık hizmet sağlayıcı ve (j) kripto varlık saklama hizmeti tanımları 6362 ile aynı özde ("Müşterilerin kripto varlıklarının veya bu varlıklara ilişkin cüzdandan transfer hakkı sağlayan özel anahtarların saklanmasını, yönetimini …"); (r) platform tanımı 6362 dd ile aynı. İzin maddeleri: m. 5-(1) "Kripto varlık hizmet sağlayıcılara Kurulca izin verilebilmesi için; a) Anonim ortaklık şeklinde kurulmaları, …"; m. 9-(1) faaliyet izni genel şartları; m. 17-(1) kuruluş izninden sonra 6 ay içinde faaliyet iznine başvurulmazsa hak düşer.
- **Sahibe not:** Skinora, alıcının USDT'sini platformun anahtarlarını tuttuğu depozit adreslerinde emanette tutar ve sonra transfer eder. Yukarıdaki "saklama hizmeti" (çç) ve "platform" (dd — "transfer, bunların gerektirdiği saklama") tanımlarının bu modele uygulanıp uygulanmadığı açık bir hukuk sorusudur; uygulanırsa m. 35/B izin zorunluluğu ve m. 109/A cezai yaptırım gündeme gelir. Bu soru T175 (çıkış kapısı / açılış kararı) öncesinde hukukçuya sorulacaklar listesinde durmalı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend integration (P3) | ✓ 9/9 | `dotnet test tests/Skinora.API.Tests --filter FullyQualifiedName~PlatformPublicEndpointTests` → "Passed! - Failed: 0, Passed: 9" (3 yeni P3 testi + 6 mevcut P1/P2) |
| Backend API.Tests tam | ✓ 931/931 | `dotnet test tests/Skinora.API.Tests` → "Passed! - Failed: 0, Passed: 931, Skipped: 0, Total: 931, Duration: 8 m 26 s" (SQLite in-memory; Docker/LocalDB gerekmedi). Diğer test projeleri değişmedi; CI'da tam koşar |
| Frontend vitest (tam) | ✓ 411/411 | `npx vitest run` → "Test Files 43 passed (43), Tests 411 passed (411)" (yeni `masak.test.tsx` 11 test) |
| Frontend lint | ✓ | `npm run lint` (eslint) — hata yok |
| Frontend tsc | ✓ | `npx tsc --noEmit` — çıktı yok |
| Frontend prettier (değişen dosyalar) | ✓ | `npx prettier --check <değişen dosyalar>` → "All matched files use Prettier code style!" (tam `format:check` yerelde CRLF artifaktı nedeniyle 261 dosya uyarır — CI "1. Lint" LF ile yetkili) |
| i18n | ✓ | `npm run i18n:check` → "i18n parity OK — 4 locales, 1595 keys each"; untranslatable uyarı sayısı 20 → 20 |
| Frontend build | ✓ | `npm run build` — başarılı (route listesi üretildi) |
| CI | (aşağıda) | PR #368 — sonuç "Commit & PR"da |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici, ayrı alt ajan) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (P3 mevcut `HOT_WALLET_ADDRESS` / `ETHEREUM_HOT_WALLET_ADDRESS` okumasını kullanır)
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T167-masak-akisi`
- Commit: `9b757cb8` — T167: MASAK notları ödeme ekranlarında ve yardımda + P3 payout gönderim adresi (+ rapor/status commit'i)
- PR: #368
- CI: ilk run'lar raporu yazarken sürüyordu; sonuç yöneticiye son mesajda bildirilir

## ⚠ Kararlar (öneriyle kaydedildi — faz sonu listesine)

1. **P3 herkese açık (öneriyle kaydedildi — ⚠ GÜVENLİK).** Gönderim adresi oturumsuz yardım sayfasında da okunur. Gerekçe: payout alan her satıcı adresi zincirde görür, gizli değildir; satıcı beyanı ilk ödemeden önce hazırlayabilmeli. Bedel: sıcak cüzdan bakiyesi herkesçe izlenebilir (zaten zincirde açık). Alternatif: yalnız oturum açmış kullanıcıya (tek satır `[Authorize]` + yardım sayfasında adresin gizlenmesi).
2. **Metin dili (öneriyle kaydedildi — ⚠).** Ekranlar "kuralları borsanız uygular, Skinora değil; pratik özet, hukuki tavsiye değildir" der; MASAK maddeleri yalnız destek sayfasının kaynak satırında anılır. Rakamlar resmî metinden: "48 sa, ilk çekimde 72 sa" (plandaki "48–72" değil), "günlük 3.000 $ / aylık 50.000 $ (bazı durumlarda iki katı)".
3. **"Ödeme penceresi 15–60 dk" yazılmadı (öneriyle kaydedildi — ⚠).** Pencere sabit değil (satıcı, admin aralığında seçer; seed "Unconfigured"); metin işlemin kendi geri sayımına atıf yapar.
4. **Kabul formunda da not (öneriyle kaydedildi — ⚠).** Kriter "ödeme ekranında" diyor; ödeme penceresi kabul + satıcı onayından sonra başladığı için not kabulden önce de gösterildi.
5. **Para akışında değişiklik yok.** Payout adresi ve depozit modeli zaten kriterlere uyuyor; dur-sor gerektiren bir para kararı çıkmadı.

## Known Limitations / Follow-up

- **T169 girdisi (sahibe bilgi):** Tebliğ 29 m. 4/4 günlük 3.000 $ sabit değerli kripto çekim sınırı nedeniyle, toplamı (fiyat + komisyon + TRON ağ ücreti) 3.000 $'ı (24/A-1 uygulanırsa 6.000 $) aşan bir satış Türk borsasından tek transferle ödenemez; platform "tam tutarı tek seferde gönderin" kuralını uygular. T169'daki "en yüksek işlem tutarı" kararında dikkate alınmalı. Bu görevde sınır koymadık (karar T169'un).
- **Şirket bilgisi:** 24/A-6 beyanı ad/unvan + bir tanımlayıcı (adres, vergi no vb.) ister. Metinler göndericiyi/alıcıyı "Skinora" olarak verir; şirket unvanı ve tanımlayıcı T168 (hukuk metinleri, şirket bilgisi) sonrasında yardım bölümüne eklenebilir.
- **TRON servisinde açılış denetimi yok:** Ethereum servisi sıcak anahtarın `ETHEREUM_HOT_WALLET_ADDRESS`'i kontrol ettiğini açılışta doğrular (`checkWalletKeys`); TRON servisi sıcak cüzdan anahtarı ile `HOT_WALLET_ADDRESS` uyumunu denetlemez. Uyuşmazsa işlem `owner_address` ≠ imzacı olduğu için zincirde reddedilir — payout **başka bir adresten çıkmaz**, ama her payout başarısız olur. İsteğe bağlı küçük iş (T171 kurulum günü kontrol listesine ya da backlog'a).
- **Adres değişimi:** Sıcak cüzdan değişirse (T171 yeni mainnet cüzdanları, anahtar sızıntısı) P3 yeni adresi döner ve metin "borsanızdaki kaydı güncelleyin" der; satıcıya proaktif bildirim yok.
- **COMPLETED özeti mevcut adresi gösterir:** Özetin altındaki gönderim adresi P3'ün **bugünkü** adresidir; geçmiş bir payout'un gerçek göndericisi işlem hash bağlantısından görülür (adres değişmediği sürece aynıdır).
- `networkDisplay.test.tsx`'teki T156 iddiası ("sayfada 'exchange' kelimesi yok") daraltıldı: artık yalnız uyarı listesine bakıyor (K9 — "borsadan göndermeyin" uyarısı yok); MASAK kutusu bilinçli olarak borsadan ödemeyi anlatır.

## Notlar

- **Working tree:** temiz (Adım -1; `git status` → "nothing to commit, working tree clean", dal `task/T167-masak-akisi` = `origin/main` `e7e8acf1`).
- **Main CI startup check (Adım 0), son 3 tamamlanmış main run'ı:** `39600854541` success (T166) · `39582270184` success (T160b) · `39582270230` success (T160b). O sırada süren `39600854571` (T166, CI) sonradan success.
- **Bağımlılık:** plan T167 için bağımlılık yazmıyor; T141 ✓ (kararlar), T156 ✓ (ağ arayüzü).
- **Dış varsayımlar:**
  1. "Tebliğ 29 rakamları ikincil kaynaktaki gibi" — **kırık çıktı**, resmî metne göre düzeltildi (yukarıda fark tablosu; kaynak URL'leri).
  2. "Payout ağ başına sabit adresten çıkar" — kodda ölçüldü ✓ (KK2 kanıtı).
  3. "Depozit işlem başına" — kodda ölçüldü ✓ (KK3 kanıtı).
  4. "Ödeme penceresi 15–60 dk" — platform kuralı değil (02 §3.1, `SystemSettingSeed.cs` "Unconfigured"); metin buna dayanmıyor.
  5. Paket/ortam: yeni bağımlılık yok; worktree'de `npm ci` (502 paket) ile test edildi; Docker kullanılmadı, backend testleri SQLite in-memory.
- **Mini güvenlik kontrolü:** secret yok (yalnız public adres); yeni uç herkese açık, salt okunur, `public` hız sınırında, girdisi yok; soğuk/yakıt adresi ve özel anahtar sızmaz (testte soğuk adresin yanıtta olmadığı doğrulanıyor); yeni dış bağımlılık yok.
- Araştırma alt ajanı (opus/high) resmî metinleri indirip alıntıladı; Tebliğ 29 m. 4 ve Yönetmelik 24/A birleşik metni + CK 11845 dipnotları yapımcı tarafından indirilen ham metinde ayrıca `grep` ile okundu.
