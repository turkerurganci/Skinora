# T168 — Hukuk metinleri

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-21

---

## Yapılan İşler

- **Kullanıcı Sözleşmesi (21 bölüm), Gizlilik Politikası + KVKK aydınlatma metni (13 bölüm), destek (5 bölüm)** dört dilde yazıldı; Türkçe esas, EN/ES/ZH anlamca eşdeğer (`frontend/src/i18n/messages/*.json` → `legal.*`). Eski metinlerin yer tutucu kısımları ("nihai sürümde belirtilecek", "SSS burada yer alacak") kalktı.
  - Sözleşme: tanımlar ve dil (TR esas) · kimler kullanabilir (18+, yaptırım/coğrafi engel, Steam hesabı, Mobile Authenticator, limited / 15 gün kısıtı) · hizmetin niteliği (eşyaya hiç dokunulmaz, emanette yalnız para, teslimat garanti edilemez, garanti iade) · 8 adımlı işlem akışı · ödeme/ağ/token (yalnız USDT, TRON TRC-20 ya da Ethereum ERC-20, tek ağ, işlem başına adres, tam tutar tek transfer, eksik/fazla/parçalı/yanlış token/yanlış EVM ağı/gecikmeli ödeme) · ücretler (komisyonu alıcı öder ve işlemde sabitlenir, TRON ağ ücreti satırı, Ethereum'da sweep ve payout ücretini platform karşılar, TRON payout koruma eşiği) · teslimat (12 sa, alıcı onayı / envanter kanıtı, gizli envanter, son kontrol, dondurma) · mutabakat 8 gün ve satıcıya ödeme (geri alma kontrolü, ayırt edilemeyen vaka yöneticide, tek gönderim adresi) · iptal ve cayma · iadeler (iade adresi, reddedilen ödemeler, TRON satırı sweep öncesi/sonrası, her iadede ağ ücreti düşülür, yedek ücret = min(yedek, son ölçülen), en düşük iade eşiği) · itirazlar · yükümlülükler · karşı tarafın gördüğü bilgiler · işlem sınırları ve hesap güvenliği · Türkiye'deki borsalar ve MASAK · sorumluluk (02 §20.1 garantileri, §20.2 sorumsuzlukları, emanetteki para yükümlülüğü sınırsız, dolaylı zarar hariç, kast/ağır kusur saklı) · tazminat · askıya alma/kapatma/silme · değişiklikler · **uygulanacak hukuk ve yetkili mahkeme** (`{courtVenue}`) · iletişim.
  - Gizlilik/KVKK: veri sorumlusu (`{legalName}`, `{address}`) · işlenen veriler (kodla karşılaştırıldı: Steam hesap ve takas uygunluğu, envanter okumaları, trade URL, ağ başına adresler + değişiklik geçmişi, işlem verileri, isteğe bağlı e-posta/Telegram/Discord, ToS sürüm/tarih + 18+ tarihi, IP/ülke, cihaz bilgisi, güvenlik sinyalleri) · toplama yöntemi · amaçlar ve hukuki sebepler · aktarım (Valve, blok zinciri ve erişim sağlayıcıları, e-posta hizmeti, Telegram/Discord, Cloudflare, karşı taraf, yetkili makamlar; yurt dışına aktarım) · blok zinciri kayıtlarının silinemezliği · saklama/silme (02 §19 ve `AccountLifecycleService` ile aynı: anonimleştirme, adresler silinir, işlem ve denetim kayıtları anonim süresiz) · **KVKK m. 11 hakları (9 madde)** ve kullanım yolu (hesabı ayarlardan ya da kabul etmeden "Şartları kabul etmiyorum" ile silme, 30 gün, Kurul'a şikâyet) · güvenlik · çerezler (kodla karşılaştırıldı: oturum yenileme çerezi, giriş güvenlik çerezleri, dil çerezi, tarayıcı depolaması; analitik yok) · 18 yaş altı · değişiklikler · iletişim.
  - Destek: iletişim, yanıt süreleri, itirazlar, MASAK (T167 bölümü korunarak), **SSS 11 soru** (ödeme yöntemi, yanlış ağ, eksik/fazla, ücret, teslimat 12 sa, teslim onayı, satıcı ödemesi 8 gün, Mobile Authenticator, Türk borsası, hesap silme, kimlik avı).
- **İşletmeci bilgisi tek yerde:** `frontend/src/lib/legal/operator.ts` — `OPERATOR_INFO` (unvan, adres, vergi dairesi, vergi no, MERSİS, KEP, yetkili mahkeme yeri), `SUPPORT_EMAIL`, `SUPPORT_HREF`, `LEGAL_TEXT_DATE`, `isOperatorInfoComplete()`, `operatorMessageValues()`. Bütün metinler bu değerleri ICU değişkeni olarak alır; boş alan `legal.operator.pending` ("[şirket bilgisi eklenecek]") olarak görünür. **Taslak etiketi** `isOperatorInfoComplete()` yanlışken görünür — gerekli alanlar (unvan, adres, vergi dairesi, vergi no, mahkeme yeri) dolunca kendiliğinden kalkar. Her hukuk sayfasının altında işletmeci bloğu boş alanları işaretli gösterir.
- **Alan adı:** `support@skinora.app` her yerden kalktı; tek adres `support@skinora.com` (`SUPPORT_EMAIL`; coğrafi engel, yaptırım, askı ekranları ve askı başlığı `SUPPORT_HREF`'i kullanıyor). Operatör alarmı `admin@skinora.com` zaten aynı alan adında.
- **MASAK beyanı (backlog ⚪ `MasakDeclarationEntityDetails`):** yeni `masak.entityDetails` satırı (unvan + adres, Yönetmelik 24/A-6) alıcının ödeme kutusunda, satıcının gönderim notunda ve destek `#masak` bölümünde; değerler işletmeci kaydından. Satır şirket bilgisi gelene kadar açık (güncellendi).
- **Backend — backlog 🟡 `TosNotAcceptedAccountCannotSelfDelete` ✅ (⚠ GÜVENLİK/HUKUK):** `POST /users/me/deactivate` (U13) ve `DELETE /users/me` (U14) `[SkipTosAcceptanceGate]` taşıyor. Başka uç açılmadı; uçların kendi kuralları (aktif işlem yok, birebir `SİL`) aynen geçerli.
- **Arayüz:** ToS modalına `declineSlot`; ilk kabulde (callback sayfası ve global kapı "first" modu) "Şartları kabul etmiyorum" → açıklama + hesap yönetimi (kapat/sil, ayarlardaki bileşenin aynısı). Yeniden onayda (sürüm artışı) gösterilmez. Modal özetindeki **yanlış "İşlem tamamlandığında satıcıdan komisyon kesilir"** cümlesi düzeltildi (02 §5: komisyonu alıcı öder; TRON'da ayrı ağ ücreti) — 4 dil.
- **LegalPage:** metin gövdesi düz metinden yapı çıkarır (`LegalBody`: boş satır = paragraf, `- ` = madde, `1. ` = numaralı liste), içindekiler (sözleşme ve gizlilik), "son güncelleme" tarihi, işletmeci bloğu. Bölüm listeleri `lib/legal/sections.ts`'te (App Router sayfası başka isim dışa veremez).
- **Belgeler:** `plan/11_F9.md` v1.21 T168 kriteri sahip kararına göre uyarlandı · 07 §4.4 v4.20 (muafiyet; sürümün sunucuda zorunlu tutulmaması T168'de yeniden ele alındı, değişmedi) · 02 v4.18 §22 "Uygulandı (T168)" notu ve §19 satırı · DOC_CHANGELOG (02, 07, 11) · DEFERRED_BACKLOG 53/158 → 52/159 · IMPLEMENTATION_STATUS T168 ⏳ · STATUS_CHANGELOG (T167 merge teyidi) · repo memory T168 satırı.

## Etkilenen Modüller / Dosyalar

- Backend: `backend/src/Skinora.API/Controllers/UsersController.cs`, `backend/src/Skinora.API/Filters/TosAcceptanceGateFilter.cs` (yorum), `backend/tests/Skinora.API.Tests/Integration/AccountLifecycleEndpointTests.cs`
- Frontend yeni: `src/lib/legal/operator.ts`, `src/lib/legal/sections.ts`, `src/components/legal/{LegalBody,OperatorInfoBlock}.tsx`, `src/components/legal/useOperatorValues.ts`, `src/components/legal/legal.test.tsx`, `src/components/legal/legal.complete.test.tsx`
- Frontend değişen: `src/components/legal/{LegalPage.tsx,index.ts}`, `src/app/[locale]/{terms,privacy,support}/page.tsx`, `src/components/auth/{TosModal,TosRepromptGate}.tsx`, `src/components/auth/TosRepromptGate.test.tsx`, `src/app/[locale]/auth/{callback,geo-block,sanctions,suspended}/page.tsx`, `src/components/layout/SuspendedHeader.tsx`, `src/components/masak/{BuyerExchangeNotice,PayoutSourceNotice,MasakHelpSection}.tsx`, `src/i18n/messages/{tr,en,es,zh}.json`
- Belgeler: `Docs/plan/11_F9.md`, `Docs/07_API_DESIGN.md`, `Docs/02_PRODUCT_REQUIREMENTS.md`, `Docs/DOC_CHANGELOG.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`, bu rapor

## Kabul Kriterleri Kontrolü

Kriterler `plan/11_F9.md` v1.21 (sahip kararıyla uyarlanmış metin).

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Şartlar + gizlilik/KVKK + destek metinleri 02 §22 P2P maddelerini eksiksiz ve tutarlı anlatıyor (escrow akışı, teslimat 12 sa, iade/itiraz, TRON + Ethereum, yalnız USDT, ağ ücreti kesintileri T159a/T160b/T160c, MASAK notu T167) | ✓ | §22'nin üç maddesi: `serviceDescription` + `liability` (custody yok, teslimat garanti edilemez), `settlement` + `liability` (geri alma: mutabakat penceresinde korunur, Steam'in kendi kararı kullanıcı riski — 02 §20.2 notuyla hizalı), `visibility` (Steam profili + trade URL). Akış `transactionFlow` (02 §2.2 sekiz adım), 12 sa `delivery`, iade `refunds` (02 §4.4/§4.6/§4.7, T159a her iade, T160b min(yedek, son ölçülen)), itiraz `disputes` (02 §10), ağ `paymentAndNetworks`, MASAK `masak`. T160c (kalıntının toplanması) henüz yapılmadı; metin yalnız kullanıcıyı ilgilendiren kesintiyi anlatır, kalıntının nereye gittiğini söylemez (doğru kalır). Testler: `legal.test.tsx` "terms — 02 §22 P2P clauses…" bloğu (9 test). |
| 2 | 4 dil (TR esas; EN/ES/ZH anlamca eşdeğer), i18n anahtar eşitliği | ✓ | `npm run i18n:check` → "i18n parity OK — 4 locales, 1653 keys each, identical key sets"; yeni anahtarlarda untranslatable uyarısı yok (20 uyarı önceden vardı). `legal.test.tsx` "translations keep the Turkish list items and placeholders": her dilde her bölümün madde sayısı, numaralı adım sayısı ve ICU değişkenleri TR ile aynı. Sözleşme §"Tanımlar ve dil": çeviride anlam farkında TR geçerli. |
| 3 | Şirket/kişi alanları tek yerde, boşken "şirket bilgisi eklenecek" işareti, taslak etiketi kalıyor | ✓ | `lib/legal/operator.ts` tek kayıt; `legal.test.tsx` "operator record", "terms: DRAFT notice, pending marker…", "privacy: data controller…"; `legal.complete.test.tsx` doluyken taslak yok, unvan ve mahkeme yeri metinde, işaret kalmıyor, MASAK satırında unvan + adres. |
| 4 | Uygulanacak hukuk / yetkili mahkeme maddesi metinde (yer şirket bilgisiyle doldurulur) | ✓ | `terms.sections.governingLaw` (4 dil `{courtVenue}`; test "governing law + competent court clause"); TR: "Türkiye Cumhuriyeti hukuku … {courtVenue} mahkemeleri ve icra daireleri yetkilidir", tüketici hakem heyeti / tüketici mahkemesi hakkı saklı. |
| 5 | `support@skinora.app` / `admin@skinora.com` tutarsızlığı tek alan adına (`skinora.com`) | ✓ | `grep -rn "skinora\.app"` frontend/backend/compose/.env.example → 0 (yalnız `operator.ts`'teki açıklama yorumu); test "one support domain: skinora.com" 4 dilin tamamında `skinora.app` yok. |
| 6 | Ek iş B4: kabul etmemiş kullanıcı `DELETE /users/me` + `/me/deactivate` kullanabilsin, başka uç açılmasın, testle; backlog kapansın | ✓ | `AccountLifecycleEndpointTests` T168 bloğu 9 test (kapatma ×2, silme + anonimleştirme ×2 — hiç kabul etmemiş ve yalnız 18+ damgası eksik —, `SİL` + aktif işlem kuralları, 4 `/users/me` ucu 403 `TOS_NOT_ACCEPTED`). Bozma: iki öznitelik kaldırılınca 5 test kırmızı (4 "kapıda kalır" testi beklendiği gibi yeşil). Backlog satırı ✅. |
| 7 | `MasakDeclarationEntityDetails` şirket alanına bağlandı, açık kaldı | ✓ | `masak.entityDetails` 3 yüzeyde; `legal.complete.test.tsx` "MASAK declaration carries the trade name and address"; backlog satırı güncel, kapanış koşulu yazılı. |
| 8 | 11_F9.md T168 metni sahip kararına göre güncellendi (DOC_CHANGELOG başlık kuralı) | ✓ | `plan/11_F9.md` v1.21 başlık (tek cümle 132 karakter) + DOC_CHANGELOG `11_IMPLEMENTATION_PLAN.md` bölümü. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend (hedefli) | ✓ 33/33 | `dotnet test tests/Skinora.API.Tests --filter "AccountLifecycleEndpointTests\|TosAcceptanceGateTests"` |
| Backend (bozma) | ✓ 5/5 yakalandı | iki `[SkipTosAcceptanceGate]` yoruma alındı → `Failed: 5, Passed: 19` |
| Backend (tam) | ✓ CI | CI run `39622520437` (HEAD `5f1012a0`): 3. Unit test ✓, 4. Integration test ✓, 5. Contract test ✓, 6. Migration dry-run ✓ (yerelde Docker kullanılmadı) |
| Frontend vitest | ✓ 438/438 | `npx vitest run` (45 dosya; yeni: `legal.test.tsx`, `legal.complete.test.tsx`, `TosRepromptGate.test.tsx` +2) |
| Frontend tsc / eslint | ✓ | `npx tsc --noEmit -p .` · `npm run lint` |
| Frontend i18n | ✓ | `npm run i18n:check` — 1653 × 4, eşit |
| Frontend prettier | ✓ | değişen dosyalarda `prettier --check` temiz (yerel CRLF artefaktı hariç — CI LF yetkili) |
| Frontend build | ✓ | `npm run build` (`/[locale]/terms`, `/privacy`, `/support` derlendi) |
| CI | ✓ success | run `39622520437` (HEAD `5f1012a0`): Lint, Build, Unit, Integration, Contract, Migration dry-run, Docker build (backend/frontend), vitest ✓; E2E advisory job'ları ✓ (T113 admin-flows run kapandığında kuyrukta kaldı, advisory) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (yeni env yok; `NEXT_PUBLIC_SUPPORT_URL` varsayılanı `mailto:support@skinora.com` oldu)
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T168-hukuk-metinleri`
- Commit: `f84195a0` — T168: Şartları kabul etmemiş hesap kendini kapatabilir/silebilir (ToS kapısı muafiyeti, KVKK silme hakkı)
- Commit: `5f1012a0` — T168: Hukuk metinleri — kullanım şartları, gizlilik/KVKK aydınlatma, destek (4 dil)
- PR: #370
- CI: ✓ PASS — run `39622520437` (kod HEAD `5f1012a0`); rapor commit'inin run'ı aşağıda son satırda

## Doldurulmayı bekleyen işletmeci alanları

`frontend/src/lib/legal/operator.ts` → `OPERATOR_INFO`:

1. `legalName` — ticari unvan (şirket) ya da ad soyad (şahıs) — **gerekli**
2. `address` — tebligat adresi — **gerekli**
3. `taxOffice` — vergi dairesi — **gerekli**
4. `taxId` — vergi kimlik numarası — **gerekli**
5. `courtVenue` — yetkili mahkeme ve icra dairelerinin yeri (ör. "İstanbul (Merkez)") — **gerekli**
6. `mersisNo` — MERSİS numarası (yalnız şirketse)
7. `kepAddress` — KEP adresi (varsa)

Doldururken ayrıca: `LEGAL_TEXT_DATE` güncellenir; sözleşme esaslı değiştiyse `NEXT_PUBLIC_TOS_VERSION` artırılır (her hesaptan yeniden onay — WP11). Gerekli beş alan dolunca taslak etiketi kendiliğinden kalkar. Bu alanlar backlog ⚪ `MasakDeclarationEntityDetails`'i de kapatır.

## ⚠ Kararlar (öneriyle kaydedildi — faz sonu listesine)

1. **⚠ GÜVENLİK/HUKUK — ToS muafiyeti yalnız U13/U14 (öneriyle kaydedildi — ⚠).** Kapı uç başına açıldı, controller düzeyinde değil; diğer `/users/me` uçları 403'te (testle).
2. **⚠ GÜVENLİK — "Şartları kabul etmiyorum" yalnız ilk kabulde (öneriyle kaydedildi — ⚠).** Daha önce kabul etmiş ve sürüm artışında yeniden sorulan hesap ayarlara zaten ulaşıyor (sürüm sunucuda zorunlu değil).
3. **⚠ GÜVENLİK — ToS sürümü şimdi artırılmadı (öneriyle kaydedildi — ⚠).** Gerçek kullanıcı yok (deneme yayını); sürüm, taslak etiketi kalktığında (şirket bilgisi) artırılır. 07 §4.4'e yazıldı.
4. **⚠ HUKUK — İşletmeci kaydı kodda bir TypeScript sabiti, ortam değişkeni değil (öneriyle kaydedildi — ⚠).** Hukuk metni değişikliği commit ve inceleme gerektirmeli; `NEXT_PUBLIC_*` zaten derleme anında gömülüyor, env'in esneklik avantajı yok.
5. **⚠ HUKUK — Gerekli alanlar: unvan, adres, vergi dairesi, vergi no, mahkeme yeri; MERSİS ve KEP isteğe bağlı (öneriyle kaydedildi — ⚠)** — şahıs işletmesinde ikisi yok.
6. **⚠ HUKUK — Türkçe metin esas; çeviride anlam farkında TR geçerli (öneriyle kaydedildi — ⚠).**
7. **⚠ HUKUK — Değişiklikler yalnız yayımdan sonra başlayan işlemlere uygulanır; esaslı değişiklikte yeniden onay (öneriyle kaydedildi — ⚠).** Mevcut WP11 yeniden onay mekanizmasına dayanır.
8. **⚠ HUKUK — Taslak etiketi metni "Skinora gerçek ağda (gerçek USDT ile) açılmaz" diyor (öneriyle kaydedildi — ⚠)** — sahibin "mainnet bunu bekler" kararının kullanıcıya açık hâli.
9. **⚠ HUKUK — KVKK başvurusu: tek adres `support@skinora.com`, "en geç 30 gün, ücretsiz" taahhüdü (öneriyle kaydedildi — ⚠).** Ayrı `kvkk@` adresi açılmadı.
10. **⚠ HUKUK — Aktarım alıcıları: Valve, Cloudflare, Telegram, Discord adlarıyla; e-posta ve blok zinciri erişim sağlayıcıları kategoriyle (öneriyle kaydedildi — ⚠).** Yurt dışına aktarım olgu olarak yazıldı; hukuki dayanağı yazılmadı (aşağıda soru 6).
11. **⚠ HUKUK — "Açık rızaya dayanan işleme yok, pazarlama iletisi yok" (öneriyle kaydedildi — ⚠)** — kodda analitik/pazarlama yok (`grep` gtag/posthog/sentry vb. → 0).
12. **⚠ HUKUK — Vergi yükümlülükleri kullanıcıya ait maddesi (öneriyle kaydedildi — ⚠).**
13. **⚠ HUKUK — Teslimat süresi metinde sabit "12 saat"** (sahip kararı 2026-10-04); ayar kurulumda 720 dk olmalı (sınırlamalar).
14. **TRON payout koruma eşiği metinde "varsayılan %10"; komisyon oranı rakamsız** ("fiyatın bir yüzdesi, kabulden önce gösterilir") — admin ayarı değişince metin eskimesin (öneriyle kaydedildi — ⚠).
15. **Hukuki atıflar:** yalnız doğrulanan madde numaraları yazıldı — KVKK m. 10 (aydınlatma) ve m. 11 (haklar; 9 madde kvkk.gov.tr metniyle birebir sırada), MASAK atıfları T167'den. KVKK işleme şartları madde numarası vermeden sözle sayıldı; Kurul'a şikâyetin madde numarası yazılmadı.

## Sahibe açık sorular (kullanıcıyı bağlayan hükümler — öneriyle yazıldı)

1. **Cayma hakkı.** Öneri metni: "Ödeme göndermeden önce ücretsiz vazgeçebilirsiniz; ödeme gönderildiğinde emanet hizmetinin ifası sizin talebinizle başlar, bu andan sonra alıcı tek taraflı iptal edemez." Tüketici mevzuatındaki cayma istisnasının bu modele uyup uymadığı hukukçuya sorulacak. Bu metinle kalsın mı?
2. **Sorumluluk sınırı.** Sayısal tavan koymadım: emanetteki parayı aktarma/iade yükümlülüğü sınırsız; bunun dışında dolaylı zarar (kâr, değer, fırsat kaybı) hariç; kast ve ağır kusur saklı. Tavan (ör. işlem başına komisyon kadar) ister misiniz?
3. **Tazminat.** Kullanıcı, kendi ihlalinden (çalıntı eşya, dolandırıcılık) doğan zararı ve üçüncü kişi taleplerini "hukukun izin verdiği ölçüde" karşılar. Kalsın mı?
4. **Uygulanacak hukuk ve mahkeme.** Türk hukuku + işletmeci merkezinin mahkemeleri; tüketici hakem heyeti/mahkemesi hakkı saklı. Yabancı kullanıcılar için ayrı bir hüküm istenir mi?
5. **7518 / SPK.** Metinlerde Skinora'nın lisanslı olduğu ya da lisans gerekmediği yazılmadı (test: 4 dilde "lisanslı/licensed/licenciad/持牌" yok). T167'nin "saklama hizmeti / platform tanımı Skinora'ya uygulanır mı" sorusu T175 öncesi hukukçuya sorulacak.
6. **Yurt dışına aktarımın hukuki dayanağı** (KVKK yurt dışı aktarım kuralları; sağlayıcılarla sözleşme) hukukçuya sorulacak — metin bugün yalnız aktarımın olduğunu söylüyor.

## Known Limitations / Follow-up

- **Teslimat süresi ayarı:** `delivery_timeout_minutes` seed'de `Unconfigured` (DEPLOY_RUNBOOK §A #6). Metin "12 saat" diyor; kurulumda (T169/T171) değer 720 dakika girilmeli, yoksa metin ile davranış ayrışır.
- **HTTPS cümlesi** (gizlilik "Güvenlik") deneme yayınında tünel/T162 ile doğru olur.
- **Hukukçu incelemesi yok** (sahip kararı 2026-10-04, risk kabul edildi). ES ve ZH çevirileri EN'den bir alt ajanla yapıldı, TR ile yapı testi geçiyor; anadili konuşan bir okuma yapılmadı.
- **T160c** (iade kesinti kalıntısının toplanması) bekliyor; metin kalıntının nereye gittiğini söylemiyor, T160c sonrası değişmesi gerekmez.
- **Deaktif hesap silme:** `AccountLifecycleService.LoadLiveUserAsync` deaktif hesabı bulmuyor; deaktif edilmiş hesap silmeden önce yeniden giriş yapar (giriş hesabı açar). Bu davranış değiştirilmedi; metin "kapatılan hesap yeniden girişte açılır" der.
- **Çerez listesi elle tutuluyor** — yeni bir çerez/depolama eklenince gizlilik metni güncellenmeli.
- Komisyon oranı ve mutabakat süresi admin ayarıdır; metin mutabakatı "8 gün" yazar (varsayılan, 7'nin altına inemez). Ayar değişirse metin güncellenmeli.

## Notlar

- **Working tree:** temiz (Adım -1). Dal `task/T168-hukuk-metinleri`, `origin/main` `daf0e301`'den.
- **Main CI startup (Adım 0):** son tamamlanmış 3 run — Docker Publish `39607353298` ✓, CI `39607353309` ✓ (d81d91f1), Docker Publish `39600854541` ✓ (e7e8acf1); daf0e301'in run'ları başlangıçta sürüyordu, sonra ✓ (CI `39615975640`, Docker Publish `39615975624`) — STATUS_CHANGELOG'a merge teyidi olarak işlendi.
- **Dış varsayımlar:** (1) KVKK madde numaraları — kvkk.gov.tr açıklama sayfaları, erişim 2026-10-21: [Aydınlatma Yükümlülüğü](https://www.kvkk.gov.tr/Icerik/2033/Aydinlatma-Yukumlulugu-) ("Kanunun 10. maddesi", (a)–(e)), [İlgili Kişinin Hakları](https://www.kvkk.gov.tr/Icerik/2036/Ilgili-Kisinin-Haklari) ("Kanunun 11. maddesi", 9 hak), [Şikâyet Hakkı](https://www.kvkk.gov.tr/Icerik/2063/Sikayet-Hakki) ("Kanunun 13. maddesi" başvuru). mevzuat.gov.tr Kanun metni okunamadı (sayfa yalnız gezinme, PDF sıkıştırılmış); bu yüzden m. 5 (işleme şartları) ve Kurul şikâyeti madde numarası metne yazılmadı. (2) MASAK rakamları T167'nin Resmî Gazete karşılaştırmasından; değiştirilmedi. (3) Yeni paket yok. (4) Docker kullanılmadı; backend testleri SQLite fabrikasıyla.
- Secret hook: EN metindeki 12 ardışık küçük harfli kelime BIP-39 kalıbına takıldı; cümle yeniden yazıldı (bypass kullanılmadı).
- Kod içi atıf: T168 kodu `SupportPage`'in MASAK bölümünü ve `MasakHelpSection`'ı (T167) korur; `masak.test.tsx` değişmeden geçiyor.
