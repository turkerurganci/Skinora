# T168 — Hukuk metinleri

**Faz:** F9 | **Durum:** ⏳ Devam ediyor — 2. düzeltme turu, yeniden doğrulama bekliyor | **Tarih:** 2026-10-21 (düzeltme turları 2026-10-22)

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
- **Arayüz:** ToS modalına `declineSlot`; ilk kabulde (callback sayfası ve global kapı "first" modu) "Şartları kabul etmiyorum" → açıklama + hesap yönetimi (kapat/sil, ayarlardaki bileşenin aynısı). Aynı yol yeniden onayda (sürüm artışı) da gösterilir (düzeltme turu B3); kapatma/silme sonrası pencere oturumla birlikte kapanır (2. düzeltme turu B5). Modal özetindeki **yanlış "İşlem tamamlandığında satıcıdan komisyon kesilir"** cümlesi düzeltildi (02 §5: komisyonu alıcı öder; TRON'da ayrı ağ ücreti) — 4 dil.
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
| Doğrulama durumu | 1. tur ✗ FAIL (HEAD `9ce1f5f3`, 2026-10-22) → düzeltme turu yapıldı, yeniden doğrulama bekliyor |
| Bulgu sayısı | 3 × S1 ⚠ HUKUK (B1–B3) + 6 × S4 |
| Düzeltme gerekli mi | Evet — aşağıda "Düzeltme turu" |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (yeni env yok; `NEXT_PUBLIC_SUPPORT_URL` varsayılanı `mailto:support@skinora.com` oldu)
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T168-hukuk-metinleri`
- Commit: `f84195a0` — T168: Şartları kabul etmemiş hesap kendini kapatabilir/silebilir (ToS kapısı muafiyeti, KVKK silme hakkı)
- Commit: `5f1012a0` — T168: Hukuk metinleri — kullanım şartları, gizlilik/KVKK aydınlatma, destek (4 dil)
- Commit: `9ce1f5f3` — T168: Yapım raporu (T168_REPORT.md)
- Commit (düzeltme turu): `7aafbf01` — T168: Düzeltme turu — yeniden onayda kapat/sil yolu, giriş kayıtları 1 yıl, iptal/iade metni
- PR: #370
- CI (1. tur): ✓ PASS — run `39625283885` (HEAD `9ce1f5f3`)
- Commit (düzeltme turu): `664d3198` — T168: Düzeltme turu raporu, status ve memory — CI `39812829124` **cancelled**: bloke edici bütün joblar ve CI Gate ✓, yalnız advisory "E2E happy-path UI" 45 dk'da iptal; main birleştirmesiyle yeniden koşuldu
- Commit (düzeltme turu): main (T160c `403fe257`) birleştirmesi — CI sonucu PR #370'te (`gh run list --branch task/T168-hukuk-metinleri`)

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
2. ~~⚠ GÜVENLİK — "Şartları kabul etmiyorum" yalnız ilk kabulde.~~ **Düzeltme turunda geri alındı (doğrulama B3):** gerekçe yanlıştı — yeniden onay penceresi tam ekran ve `/settings`'i de örtüyor. Seçenek artık iki modda da var.
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
7. **Giriş kayıtlarının hukuki dayanağı (düzeltme turu).** Metin "güvenlik ve dolandırıcılık tespiti için 1 yıl" diyor, kanun atfı yok. 5651 sayılı Kanun'daki trafik bilgisi saklama yükümlülüğünün Skinora'ya uygulanıp uygulanmadığı (yer sağlayıcı mı?) hukukçuya sorulacak; uygulanıyorsa süre ve atıf eklenir.
8. **✓ Kapandı (2026-10-22, sahip kararı: kod doğru — 02 §7 v4.21 koda göre düzeltildi). 02 §7 iptal cooldown'u (düzeltme turu).** Ürün belgesi her ödeme sonrası satıcı iptalinde "itibar cezası ve cooldown" diyor; kod cooldown'u yalnız pencere içindeki sorumlu iptal sayısı `cancel_limit_count`'u aşınca uygular (`CancelCooldownEvaluator`). Hukuk metni bugün kodu anlatıyor; 02 §7 değiştirilmedi. Hangisi doğru?

## Düzeltme turu (2026-10-22)

Bağımsız doğrulama ✗ FAIL verdi (HEAD `9ce1f5f3`; 3 × S1 ⚠ HUKUK + 6 × S4).

**Ön kontroller:** working tree temiz, HEAD `9ce1f5f3` = `origin/task/T168-hukuk-metinleri`. Tur başında `origin/main` = `daf0e301` (dalın tabanı); tur sırasında T160c (#365, `403fe257`) birleşti ve **main dala merge commit ile alındı**. Çakışmalar yalnız belgelerde: 02 / 07 / `plan/11_F9.md` başlıkları (sürümler çakıştı → bir üstü: 02 v4.20, 07 v4.22, 11_F9 v1.23; DOC_CHANGELOG'a birleştirme girişleri), DOC_CHANGELOG (iki tarafın girişleri korundu), IMPLEMENTATION_STATUS "Son güncelleme" (T168 kaldı, T160c satırı STATUS_CHANGELOG'a taşındı), STATUS_CHANGELOG (iki taraf; T167 merge teyidi tek satırda "T160c ve T168 dallarında"), DEFERRED_BACKLOG (durum satırı + satırlar; T160c'nin yeni ⚪ `RefundFeeSpendOnFailedAttemptsNotBooked` korundu; dosyanın kendi `awk`'iyle main 53/159 → **52/160**). i18n dosyaları kendiliğinden birleşti. Main CI son 3 run: `39615975640` ✓, `39615975624` ✓, `39607353298` ✓. Docker kullanılmadı.

### B1 — Teslimat süresi 12 sa ↔ ayar 60 dk (⚠ HUKUK) — T169'a bağımlı

- **Bu dalda ayar değiştirilmedi** (yönetici talimatı): paralel T169 dalı seed/migration, `.env.example`, deneme yayını env'i ve DEPLOY_RUNBOOK örneğini 720 dk'ya çekiyor (sahip kararı listesi "teslimat süresi 720 dk ⚠"). **T168, T169'dan SONRA birleştirilmeli**; aksi hâlde metin ile davranış ayrışır.
- **Metni ayardan okumak yapılmadı.** `delivery_timeout_minutes`'ı ön yüze veren herkese açık bir uç yok (`grep delivery_timeout frontend/src` → 0; ayar yalnız `SystemSettingsCatalog`/seed ve teslimat süresi hattında okunuyor). Okumak için yeni anonim bir uç ve hukuk sayfalarının istemcide veri çekmesi gerekirdi; ucuz değil, ayrıca sözleşme metnini çalışan bir ayara bağlamak (admin değiştirince sözleşme sessizce değişir) istenen bir şey değil. Metin sabit "12 saat" kalır; `legal.test.tsx` "delivery window 12 hours" testi bekçidir.
- 02 §22 notunda süre, ayarın T169'da 720 dk olacağıyla birlikte yazıldı (S4-5 ile aynı satır).

### B2 — Silmede giriş kayıtları (⚠ HUKUK, KVKK aydınlatma)

Kod ölçüldü:
- `backend/src/Modules/Skinora.Users/Application/Account/AccountLifecycleService.cs:100`: "TransactionHistory / AuditLog / UserLoginLog rows are untouched".
- Silinen/anonimleşen: `AnonymizeUserInPlace` (Steam ID, ad, foto, e-posta, trade URL…), `UserWalletAddress` satırları silinir, bildirim kanalları maskelenir, refresh token satırlarında `DeviceInfo` ve `IpAddress` null'lanır (`backend/src/Modules/Skinora.Auth/Application/Session/AuthAccountAnonymizer.cs:73-74`).
- `UserLoginLog` (`backend/src/Modules/Skinora.Users/Domain/Entities/UserLoginLog.cs:13-22`): `IpAddress`, `UserAgent`, `HasVpnSignal` (Tor çıkış düğümü sinyali), `CreatedAt`. **`DeviceFingerprint` sütunu var ama hiçbir kod yazmıyor** (`backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/LoginAuditService.cs:22-28` yalnız IP, UserAgent, VPN sinyali yazar; `grep DeviceFingerprint backend/src` → yalnız okuyan `MultiAccountDetector`) — bu yüzden metin "cihaz parmak izi" demiyor.
- Süre: `retention.user_login_log_days` = 365 (`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs:115`; `backend/src/Skinora.API/Retention/UserLoginLogRetentionCleanupJob.cs:32-35` varsayılan 365); job `CreatedAt` eşiğine göre hard delete eder (aynı dosya :58-77), kayıtlı (`RetentionJobsRegistrar.cs:64`). Amaç: 06 §1 "IP / cihaz parmak izi kaydı (çoklu hesap tespiti)", 06 §8 "Fraud tespiti yeterli periyodu"; okuyan `MultiAccountDetector`.
- `AuditLog.IpAddress` yalnız admin eylemlerinde dolu (`SystemSettingsService`, `AdminMaintenanceService`, `AdminUserSuspensionService`, `OutboundRowClosureService`; kullanıcı kaynaklı yazımlar `IpAddress: null`) — kullanıcı metnine girmedi.

Metin (4 dil, `legal.privacy.sections.dataRetention` + `yourRights`):
- Silme maddesi: "…bildirim kanallarınız ve **oturum kayıtlarınızdaki cihaz ve IP bilgisi** silinir ya da anonimleştirilir" (önce "oturum kayıtlarınız (cihaz ve IP bilgisi dahil)" diyordu ve giriş kayıtlarını da kapsıyor gibi okunuyordu).
- Yeni madde: "Giriş kayıtları (her girişin zamanı, IP adresi, tarayıcı bilgisi ve IP adresinden üretilen güvenlik sinyali) hesap güvenliği, çoklu hesap ve dolandırıcılık tespiti için kayıt tarihinden itibaren 1 yıl saklanır, sonra kalıcı olarak silinir. Hesabınızı sildiğinizde bu kayıtlar hemen silinmez; anonim hesaba bağlı olarak bu süre dolana kadar kalır."
- "Haklarınız" > silme hakkının sınırı: "…giriş kayıtları 1 yıllık süreleri dolana kadar kalır".
- Onay penceresindeki reddetme açıklaması (`auth.tos.decline.description`): "silmede kişisel verileriniz silinir" → "Gizlilik Politikası'nda yazdığı gibi silinir ya da anonimleştirilir".
- **Hukuki dayanak yazılmadı.** 5651 sayılı Kanun'un trafik bilgisi saklama yükümlülüğü yer/erişim sağlayıcılara yöneliktir; Skinora'nın bu sıfatı taşıyıp taşımadığı hukuki bir nitelendirmedir, resmî metinden okunacak bir olgu değil. Metin amaç + süre söyler; hukuki sebep "Amaçlar ve hukuki sebepler" bölümündeki meşru menfaat (dolandırıcılığın önlenmesi, güvenlik) ile zaten kapsanıyor. Sahip sorusu 7.
- Test: `legal.test.tsx` "privacy — retention matches AccountLifecycleService + the login-log retention job" (2 test).

### B3 — Yeniden onay penceresinden çıkış yok (⚠ HUKUK)

- `frontend/src/components/auth/TosRepromptGate.tsx`: `declineSlot` artık iki modda da veriliyor (`<AccountManagementSection />`); önceki `mode === "first" ? … : undefined` kalktı, yorum gerekçeyi yazıyor (pencere tam ekran, `/settings` uyuyan rota değil; sunucu U13/U14'ü ilk kabulde muafiyetle, yeniden onayda sürüm zorunlu olmadığı için kabul eder). `TosModal.tsx` prop açıklaması güncellendi.
- Metin: sözleşme "Askıya alma…" maddesi "Şartları **ya da güncellenmiş bir sürümünü** kabul etmeden de…" (4 dil).
- 07 §4.4 (v4.21) ve 02 §19 satırı (v4.19) buna göre güncellendi.
- Test: `TosRepromptGate.test.tsx` — eski "re-prompt: no decline path" testi (yalnız düğmenin yokluğunu ölçüyordu) yerine "re-prompt (new version): the modal blocks /settings, so declining offers closure and deletion here too": yol `/en/settings`, hesap `0.9` sürümünde → yeniden onay başlığı, "kabul etmiyorum" → panelde kapat + sil → kapat onayı → `deactivateAccount` çağrıldı, `/en`'e yönlendi, `acceptTos` çağrılmadı.
- **Bozma:** `declineSlot` yeniden `mode === "first" ? … : undefined` yapıldı → bu test kırmızı (1 failed / 10 passed); geri alındı.
- Sunucu değişmedi. E2E: ToS penceresi / hesap silme için mevcut Playwright senaryosu yok; eklenmedi (vitest + bozma kanıtı).

### S4 bulguları

| # | Bulgu | Durum | Değişiklik |
|---|---|---|---|
| S4-1 | İptal: tek iptalde "yeni işlem açma yasağı" (kod: yasak yalnız `cancel_limit_count` aşılınca) | ✓ kapandı | `terms.cancellation`: "…para alıcıya iade edilir ve satıcının itibarı düşer." (yasak bir sonraki maddede: "kısa sürede çok sayıda iptal") — 4 dil; test `legal.test.tsx` S4-1/S4-2 |
| S4-2 | İadeler: "düşülen ücret bildirimde gösterilir" (kabul edilmiş ödemenin iade bildirimi ücret taşımıyor) | ✓ kapandı | `terms.refunds`: "Düşülen ücret işlem sayfasında, reddedilen ödemelerin iadesinde ayrıca bildirimde gösterilir." — 4 dil |
| S4-3 | "Mainnet şirket bilgisini bekler" T171/T173/T175'e yansımamış | ✓ kapandı | `plan/11_F9.md` v1.22: T173 kriterine "başlamadan önce işletmeci bilgisi dolu (`isOperatorInfoComplete()` doğru, taslak etiketi yok, `NEXT_PUBLIC_TOS_VERSION` artırıldı)", T175'e "hukuk metinlerinde taslak etiketi yok"; DOC_CHANGELOG |
| S4-4 | Callback sayfasındaki `declineSlot` testsiz | ✓ kapandı | `auth/callback/page.test.tsx` yeni test: `status=new_user` → "kabul etmiyorum" → sil (`SİL`) → `deleteAccount("SİL")`, `/tr`'ye yönlendi, panele gitmedi. Bozma: `declineSlot` satırı silindi → 1 kırmızı; geri alındı |
| S4-5 | 02 §22 notu "teslimat 12 sa (§9.2)" yanlış atıf | ✓ kapandı | 02 v4.19: "(proje sahibi kararı 2026-10-04, `plan/11_F9.md`; ayar `delivery_timeout_minutes` = 720 — T169)" |
| S4-6 | Metin esaslı değişti ama `NEXT_PUBLIC_TOS_VERSION` 1.0 | bilinen sınırlama | B3 ve B5 (2. tur) düzeldikten sonra sürüm artışı güvenli (yeni sürümü reddeden hesabın çıkışı var). Gerçek kullanıcı yok; sürüm işletmeci bilgisiyle birlikte artırılır — artık T173 kriterinde yazılı (S4-3) |

**02 §7 cooldown farkı (sahip sorusu 8):** 2. düzeltme turunda kapandı — sahip kararı 2026-10-22 "kod doğru"; 02 §7 v4.21 koda göre düzeltildi.

### Bu turun testleri

| Tür | Sonuç | Komut |
|---|---|---|
| Frontend lint | ✓ çıkış 0 | `npm run lint` |
| Frontend tsc | ✓ çıkış 0 | `npx tsc --noEmit -p .` |
| Frontend i18n | ✓ 1653 × 4 eşit (20 advisory, hepsi T168 öncesi) | `npm run i18n:check` |
| Frontend prettier | ✓ değişen dosyalar | `npx prettier --check <değişen dosyalar>` |
| Frontend vitest | ✓ 45 dosya, 443/443 (önceki 438 + 5 yeni test) | `npx vitest run` |
| Frontend build | ✓ | `npm run build` |
| Hedefli | ✓ `TosRepromptGate.test.tsx` 11/11, `callback/page.test.tsx` 14/14, `components/legal` 29/29 | `npx vitest run <dosya>` |
| Bozma | ✓ B3 → 1 kırmızı, S4-4 → 1 kırmızı (ikisi de geri alındı) | yukarıda |
| Backend | dokunulmadı | — |

## Düzeltme turu 2 (2026-10-22)

Yeniden doğrulama ✗ FAIL verdi (HEAD `dce74e72`; 1 × S1 ⚠ HUKUK + 2 × S4). İlk turun B2/B3 ve S4-1…S4-5'i kapandı; B1 T169'a bağlı (değişmedi). Tur başında working tree temiz, HEAD `dce74e72` = `origin/task/T168-hukuk-metinleri`, `origin/main` = `403fe257` (dal güncel, birleştirme gerekmedi). Docker kullanılmadı.

### B5 — Kapatma/silme sonrası onay penceresi kalıyordu (⚠ HUKUK)

Kök neden: `TosRepromptGate` modu `["auth","me"]` önbelleğinden hesaplıyordu; çıkışta sorgu yalnız `enabled: false` olur, önbellekteki son cevap durur. Kapı `[locale]` layout'unda olduğu için `/settings` → `/{locale}` geçişinde ayakta kalıyor ve oturumu kapanmış kullanıcıya "şartları kabul edin" penceresini göstermeye devam ediyordu.

- `frontend/src/components/auth/TosRepromptGate.tsx`: mod artık oturuma bağlı — `!accessToken || DORMANT_ROUTE.test(pathname) ? null : tosGateMode(...)`. Ek olarak token düşünce `queryClient.removeQueries({ queryKey: ["auth","me"] })`: bayat cevap atılır, sonraki giriş (belki başka hesap) kendi cevabını çeker.
- `frontend/src/app/[locale]/auth/callback/page.tsx`: ilk kabul penceresi `tokenReady && accessToken` ile çizilir (sayfa hâlâ takılıysa da oturumla kapanır).
- Testler gerçek çıkış akışıyla: `lib/auth/signOut` artık mock'lanmıyor; yalnız sunucu çağrısı (`logout`) mock. Önbelleğe bayat `me` önceden yazılıyor (AuthInitializer'ın bıraktığı cevap). Router `replace` mock'u yolu gerçekten değiştiriyor.
  - `TosRepromptGate.test.tsx` — `it.each` iki pencere: **ilk kabul** (sil) ve **yeniden onay** (kapat), `/en/settings`'ten başlar → `logout` çağrıldı, `/en`'e gidildi, store token null → `tos-submit`, `auth.tos.title`, `auth.tos.reprompt.title`, `tos-decline-toggle` yok; önbellekte `me` yok; `acceptTos` çağrılmadı.
  - `auth/callback/page.test.tsx` — callback sayfası + genel kapı aynı ağaçta, önbellekte "kabul etmemiş" `me`: sil → `/tr` → ne callback penceresi ne genel kapı.
- **Bozma (hepsi geri alındı):**
  - Kapı düzeltmesinin tamamı geri (mod satırı + önbellek temizliği) → `TosRepromptGate.test.tsx` 2 kırmızı / 11 yeşil (`expected <button …> to be null` — pencere kaldı, B5'in kendisi); callback testi 1 kırmızı / 14 yeşil.
  - Yalnız önbellek temizliği geri → 2 kırmızı (önbellek bayat kaldı).
  - Yalnız mod satırı geri → yeşil (önbellek temizliği tek başına pencereyi kapatıyor; mod satırı ikinci savunma hattı).
  - Callback'te yalnız `&& accessToken` geri → callback testi 1 kırmızı.

### S4-7 — Rapor özet satırı

"Yapılan İşler > Arayüz" satırı düzeltildi: red yolu ilk kabulde ve yeniden onayda gösterilir; kapatma/silme sonrası pencere oturumla kapanır.

### S4-8 — "Satıcıya ödemeler otomatik yapılır" ↔ T169 yönetici onayı (⚠ HUKUK)

T169 (PR #371) `outbound.auto_send_enabled` = `false` ile test boyunca her satıcı ödemesini ve her alıcı iadesini yönetici onayına bağlıyor. Metin ayardan okunmuyor (B1'deki gerekçe); her iki ayar durumunda doğru kalan bir dil seçildi (4 dil):

- `terms.disputes`: "Satıcıya ödemeler otomatik yapıldığı için…" → "Satıcıya ödemeyi, satıcının bir talebi gerekmeden Skinora başlattığı için satıcı tarafında itiraz yoktur…"
- `terms.settlement` yeni madde: "Satıcıya ödemeleri ve iadeleri Skinora gönderir. Bazı dönemlerde (ör. deneme yayını süresince) her satıcı ödemesi ve her iade, gönderilmeden önce bir yöneticinin onayını bekler. Onay tutarı ve alıcı adresini değiştirmez; yalnız gönderimi onay verilene kadar geciktirir."
- `terms.refunds` yeni madde: "Bazı dönemlerde iadeler de gönderilmeden önce yönetici onayı bekler ("Mutabakat süresi ve satıcıya ödeme")."
- `terms.paymentAndNetworks`: "Yanlışlıkla USDC gönderirseniz otomatik iade edilir." → "…talep etmeniz gerekmeden iade edilir."
- `support.faq` "Satıcı olarak param ne zaman gelir?": sona "Bazı dönemlerde (ör. deneme yayını) ödeme gönderilmeden önce yönetici onayı bekler; bu, ödemeyi biraz geciktirebilir."
- Kalan "otomatik" ifadeleri olumsuz ya da gönderimle ilgisiz (ör. "iade otomatik gönderilmez", "otomatik iade edilemez", ödemenin otomatik doğrulanması) — dokunulmadı. Hukuk metni dışı: ana sayfa `home.features.autoSettle` "Otomatik doğrulama ve ödeme" pazarlama başlığı T168 kapsamında değil, değiştirilmedi.

### Sahip sorusu 8 — 02 §7 iptal cooldown'u (kapandı)

Sahip kararı (2026-10-22): kod doğru — tek iptal cezalandırılmaz, cooldown yalnız pencere içindeki sorumlu iptal sayısı limiti aşınca (`CancelCooldownEvaluator`: `responsibleCount <= LimitCount` → yasak yok). `Docs/02_PRODUCT_REQUIREMENTS.md` v4.21 §7: ödeme sonrası satıcı iptali satırı ("iptal itibara yazılır; tek iptal yasak doğurmaz; §14.2") ve "İptal sonrası cooldown" satırı düzeltildi; başlık + DOC_CHANGELOG. Hukuk metni (`terms.cancellation`) zaten kodu anlatıyordu, değişmedi.

### 2. turun testleri

| Tür | Sonuç | Komut |
|---|---|---|
| Frontend lint | ✓ çıkış 0 | `npx eslint .` |
| Frontend tsc | ✓ çıkış 0 | `npx tsc --noEmit` |
| Frontend i18n | ✓ 1668 × 4 eşit (20 advisory) | `npm run i18n:check` |
| Frontend vitest | ✓ 45 dosya, 447/447 (444 + 3 yeni) | `npx vitest run` |
| Frontend build | ✓ çıkış 0 | `npm run build` |
| Bozma | ✓ yukarıda (B5) | — |
| Backend | dokunulmadı | — |

Secret hook: EN iade maddesinin ilk hâli 12 ardışık küçük harfli kelimeyle BIP-39 kalıbına takıldı; cümle "In some periods Skinora also holds refunds for admin approval before sending them" olarak yeniden yazıldı (anlam aynı, bypass yok).

## Known Limitations / Follow-up

- **Teslimat süresi ayarı — T169'a bağımlı (doğrulama B1):** metin "12 saat" diyor; ayar `delivery_timeout_minutes` seed'de `Unconfigured`, `.env.example` ve deneme yayını bugün 60 dk. Ayarı 720 dk'ya T169 dalı çekiyor (sahip kararı listesi "teslimat süresi 720 dk ⚠"); **T168, T169'dan sonra birleştirilmeli**. Ayrıntı "Düzeltme turu" B1.
- **Giriş kaydı saklama süresi metinde sabit "1 yıl"** (`retention.user_login_log_days` varsayılanı 365, admin ayarı). Ayar değişirse gizlilik metni de değişmeli. `UserLoginLog.DeviceFingerprint` bugün yazılmıyor; yazılmaya başlanırsa metne eklenmeli.
- **Paralel dal çakışması:** `plan/11_F9.md` (v1.22), 02 (v4.19), 07 (v4.21) başlıkları ve DOC_CHANGELOG bu turda değişti; T169 da aynı belgelere dokunursa birleştirmede başlık satırı çakışır (iki tarafı koruyarak sürüm sırayla artırılır).
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
