# CHORE — Belge başlıklarını sadeleştir + planı faz başına böl

**Tür:** chore (kod değişikliği yok) | **Tarih:** 2026-10-20 | **Dal:** `chore/belge-basliklari-ve-plan-bolme` (origin/main `5306f88e`)

## Yapılanlar

1. **Başlık sadeleştirme (İş 1):** `Docs/` altında 1 KB'ı aşan başlık zinciri taşıyan 00–12 ve `DEPLOY_RUNBOOK.md` başlıkları `**Versiyon: vX.Y** | **Bağımlılıklar:** ... | **Son güncelleme:** YYYY-MM-DD — <≤150 karakter> · Değişiklik geçmişi: [DOC_CHANGELOG.md](DOC_CHANGELOG.md#...)` biçimine indi. Çıkarılan zincirler yeni `Docs/DOC_CHANGELOG.md`'ye harfiyen taşındı (belge başına bölüm). `IMPLEMENTATION_STATUS.md` "Son güncelleme" paragrafı `STATUS_CHANGELOG.md` en üstüne taşındı, yerine tek satır kondu.
2. **Plan bölme (İş 2):** `Docs/11_IMPLEMENTATION_PLAN.md` (v1.17 → v1.18) ana dosya olarak kaldı (atıf haritası + yönlendirme satırları). Yeni: `Docs/plan/11_F0-F7_ARCHIVE.md` (F0–F7, §6.2 F7 gate notu, §7.1–§7.5), `Docs/plan/11_F8.md` (F8, FK8 satırı, §7.6), `Docs/plan/11_F9.md` (F9).
3. **Kurallar (İş 3):** `.claude/skills/task.md`, `validate.md`, `gate-check.md`, `.claude/INSTRUCTIONS.md`, `.claude/CONTEXT.md`, `.github/pull_request_template.md`.

## Notlar / kararlar

- DEFERRED_BACKLOG.md: sürüm/"Son güncelleme" başlığı yok, "Durum" paragrafı 663 bayt (eşik altı) — değiştirilmedi. `PRODUCT_DISCOVERY_STATUS.md` ve `CI_CD_SETUP.md` başlıkları zaten tek kısa satır — değiştirilmedi. `BYPASS_LOG.md`, `PRE_F6_PLAN.md` başlık satırı yok.
- `00_PROJECT_METHODOLOGY.md` başlığında yalnız tarih vardı (cümle yok); biçime yalnız değişiklik geçmişi bağlantısı eklendi.
- `DEPLOY_RUNBOOK.md` başlığındaki `· **Kapsam:** ...` metni değişiklik geçmişi olmadığı için başlıkta bırakıldı.
- `Docs/00_PROJECT_METHODOLOGY.md` ve `Docs/12_VALIDATION_PROTOCOL.md` gövdelerindeki "11_IMPLEMENTATION_PLAN.md" atıfları değiştirilmedi (sürüm artışı gerektirirdi; genel plan atıfı olarak geçerli, ayrıntıyı atıf haritası verir). Süreç dosyaları (skill/INSTRUCTIONS) faz dosyasına yönlendiriyor.
- Taşınan metindeki iki göreli bağlantıya (`TASK_REPORTS/T137a_REPORT.md`, `TASK_REPORTS/T142_REPORT.md`) `../` eklendi (`plan/` klasöründen çalışsın diye); doğrulama bunu geri alarak karşılaştırır. `CHECKPOINT_REPORTS/GATE_CHECK_F5.md` bağlantısı (§8) ana dosyada kaldı.
- Belge sonlarındaki `*Skinora — X vY*` kapanış satırları (zaten kısa, bazıları başlıkla uyumsuz) dokunulmadı.
- Geçmiş raporlar (TASK/AUDIT/GPT/CHECKPOINT) değişmedi; bazı eski gate raporları `11_IMPLEMENTATION_PLAN.md#7x-...` çapalarına bağlanıyor (7.1–7.4 artık `plan/` altında) — raporlar değişmez kuralı gereği bırakıldı.
- `STATUS_CHANGELOG.md` mevcut kuralıyla taşınan metin "**Son güncelleme:**" yerine "**Önceki güncelleme:**" önekiyle yazıldı; metin harfiyen.

## Doğrulama betiği çıktısı

(Betik scratchpad'de `verify.py`: `git show origin/main:<yol>` ile eski sürümü okur, satır sonlarını normalize eder. Plan bölmede orijinal 11'in her satırı yeni dört dosyaya `difflib.SequenceMatcher(autojunk=False)` ile eşlenir; hiçbir dosyada eşleşmeyen orijinal satır = kayıp.)

```
== 1. Başlık zinciri harfiyen DOC_CHANGELOG içinde mi (satır sonları normalize)
00_PROJECT_METHODOLOGY       chain=    10 bayt  bulundu=True  bölüm-fazlası=0 bayt 
01_PROJECT_VISION            chain=   115 bayt  bulundu=True  bölüm-fazlası=0 bayt 
02_PRODUCT_REQUIREMENTS      chain= 11679 bayt  bulundu=True  bölüm-fazlası=0 bayt 
03_USER_FLOWS                chain=  5884 bayt  bulundu=True  bölüm-fazlası=0 bayt 
04_UI_SPECS                  chain=  4431 bayt  bulundu=True  bölüm-fazlası=0 bayt 
05_TECHNICAL_ARCHITECTURE    chain= 11762 bayt  bulundu=True  bölüm-fazlası=0 bayt 
06_DATA_MODEL                chain= 16289 bayt  bulundu=True  bölüm-fazlası=0 bayt 
07_API_DESIGN                chain= 13444 bayt  bulundu=True  bölüm-fazlası=0 bayt 
08_INTEGRATION_SPEC          chain= 24686 bayt  bulundu=True  bölüm-fazlası=0 bayt 
09_CODING_GUIDELINES         chain=  1519 bayt  bulundu=True  bölüm-fazlası=0 bayt 
10_MVP_SCOPE                 chain=   461 bayt  bulundu=True  bölüm-fazlası=0 bayt 
11_IMPLEMENTATION_PLAN       chain= 83296 bayt  bulundu=True  bölüm-fazlası=336 bayt (yeni giriş)
12_VALIDATION_PROTOCOL       chain=   949 bayt  bulundu=True  bölüm-fazlası=0 bayt 
DEPLOY_RUNBOOK               chain=  5492 bayt  bulundu=True  bölüm-fazlası=0 bayt 
IMPLEMENTATION_STATUS chain STATUS_CHANGELOG en üstte: True | 340 bayt

== 2. Başlık dışı gövde değişmedi mi (belge: eski[3. satır(+4)] atılır, yeni[3. satır] atılır, kalan birebir)
00_PROJECT_METHODOLOGY       gövde aynı=True
01_PROJECT_VISION            gövde aynı=True
02_PRODUCT_REQUIREMENTS      gövde aynı=True
03_USER_FLOWS                gövde aynı=True
04_UI_SPECS                  gövde aynı=True
05_TECHNICAL_ARCHITECTURE    gövde aynı=True
06_DATA_MODEL                gövde aynı=True
07_API_DESIGN                gövde aynı=True
08_INTEGRATION_SPEC          gövde aynı=True
09_CODING_GUIDELINES         gövde aynı=True
10_MVP_SCOPE                 gövde aynı=True
12_VALIDATION_PROTOCOL       gövde aynı=True
DEPLOY_RUNBOOK               gövde aynı=True
IMPLEMENTATION_STATUS        gövde aynı=True
STATUS_CHANGELOG             eski satırlar yeni dosyanın 6. satırdan sonrasında aynen: True (eklenen satır=2)
  başlık 00_PROJECT_METHODOLOGY        186 bayt; cümle=0
  başlık 01_PROJECT_VISION             282 bayt; cümle=76
  başlık 02_PRODUCT_REQUIREMENTS       349 bayt; cümle=105
  başlık 03_USER_FLOWS                 314 bayt; cümle=84
  başlık 04_UI_SPECS                   346 bayt; cümle=103
  başlık 05_TECHNICAL_ARCHITECTURE     392 bayt; cümle=100
  başlık 06_DATA_MODEL                 383 bayt; cümle=88
  başlık 07_API_DESIGN                 394 bayt; cümle=84
  başlık 08_INTEGRATION_SPEC           386 bayt; cümle=86
  başlık 09_CODING_GUIDELINES          397 bayt; cümle=55
  başlık 10_MVP_SCOPE                  304 bayt; cümle=78
  başlık 11_IMPLEMENTATION_PLAN        471 bayt; cümle=78
  başlık 12_VALIDATION_PROTOCOL        454 bayt; cümle=54
  başlık DEPLOY_RUNBOOK                433 bayt; cümle=107

== 3. Plan bölme: orijinal 11 (başlık satırları 3-4 hariç) satırı kayıp mı
main     satır=  242  eşleşen=  225  eklenen=  17
archive  satır= 4608  eşleşen= 4600  eklenen=   8
f8       satır=  463  eşleşen=  455  eklenen=   8
f9       satır=   99  eşleşen=   93  eklenen=   6
Orijinal satır sayısı: 5350 | 3-4 hariç kayıp satır sayısı: 0 []

-- Eklenen (yeni) satırlar, dosya başına --
[main]
   + **Versiyon: v1.18** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_D
   + **Atıf haritası (v1.18):** Plan faz başına bölündü; bir görevin kabul kriteri için tüm bu dosyayı değil yalnız ilgili faz dosyasını oku. Bölüm numaraları ve başlık metinl
   + | Atıf | Dosya |
   + |---|---|
   + | §F0–F7 (T01–T140), §7.1–§7.5, §6.2 F7 gate notu | [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md) |
   + | §F8 (T141–T160, K1–K9, "Txxx kararları", T159a, T159b), §7.6, FK8 (§6.2 F8 satırı) | [`plan/11_F8.md`](plan/11_F8.md) |
   + | §F9 (T161–T175) | [`plan/11_F9.md`](plan/11_F9.md) |
   + | §1–§4, §5 girişi, §6 (faz kapıları özeti), §7 girişi, §8, §9 | bu dosya |
   + > **§F0–F7 (T01–T140) görev listeleri** taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)
   + > **§F8 — Çok Ağlı Ödeme: TRON + Ethereum (T141–T160)** taşındı → [`plan/11_F8.md`](plan/11_F8.md)
   + > **§F9 — Canlıya Çıkış (T161–T175)** taşındı → [`plan/11_F9.md`](plan/11_F9.md)
   + | F8 | **FK8-1 … FK8-6** — ayrıntı → [`plan/11_F8.md`](plan/11_F8.md) (§6.2 F8 satırı) |
   + > **F7 satırı neden bu içerikle yazıldı** (F7 Gate Check, bulgu F7-N3) notu taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)
   + > **§7.1–§7.5** (Veri Modeli / API / Entegrasyon / UI eşlemeleri + F7 kaynak öğeleri) taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)
   + > **§7.6 F8 — Çok ağlı ödeme kaynak öğeleri** taşındı → [`plan/11_F8.md`](plan/11_F8.md)
[archive]
   + # Skinora — Implementation Plan · F0–F7 (arşiv)
   + **Versiyon: v1.18** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_D
   + > Bu dosya [`11_IMPLEMENTATION_PLAN.md`](../11_IMPLEMENTATION_PLAN.md)'nin parçasıdır (v1.18'de faz başına bölündü): tamamlanmış F0–F7 fazlarının görev listeleri (T01–T14
   + ## 5. Task Listesi — F0–F7
   + ## 6. Faz Geçiş Kapıları — F7 gate notu (ana dosya §6.2)
   + ## 7. Traceability Matrix — §7.1–§7.5 (ana dosya §7 girişi)
[f8]
   + # Skinora — Implementation Plan · F8 (Çok Ağlı Ödeme)
   + **Versiyon: v1.18** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_D
   + > Bu dosya [`11_IMPLEMENTATION_PLAN.md`](../11_IMPLEMENTATION_PLAN.md)'nin parçasıdır (v1.18'de faz başına bölündü): F8 — Çok Ağlı Ödeme fazı: başlık bloğu, K1–K9, tüm "T
   + ## 5. Task Listesi — F8
   + ## 6. Faz Geçiş Kapıları — F8 satırı (FK8; ana dosya §6.2)
   + ## 7. Traceability Matrix — §7.6 (ana dosya §7 girişi)
[f9]
   + # Skinora — Implementation Plan · F9 (Canlıya Çıkış)
   + **Versiyon: v1.18** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_D
   + > Bu dosya [`11_IMPLEMENTATION_PLAN.md`](../11_IMPLEMENTATION_PLAN.md)'nin parçasıdır (v1.18'de faz başına bölündü): F9 — Canlıya Çıkış fazının görev listesi (T161–T175).
   + ## 5. Task Listesi — F9

-- Orijinal satırın birden çok dosyada bulunması (boş/ayraç dışı) --
2 [(5123, '| Faz | Ek Kontrol |'), (5124, '|---|---|')]

== 4. Göreli bağlantılar
kırık bağlantı: 0

== 5. Satır sonu: değişen/yeni dosyalarda yalnız CRLF mi
tamam

== 6. Boyut tablosu (LF-normalize bayt; ~token = bayt/4)
| Dosya | Önce bayt | Sonra bayt | Önce ~token | Sonra ~token |
|---|---:|---:|---:|---:|
| Docs/00_PROJECT_METHODOLOGY.md | 35684 | 35773 | 8921 | 8943 |
| Docs/01_PROJECT_VISION.md | 6355 | 6425 | 1588 | 1606 |
| Docs/02_PRODUCT_REQUIREMENTS.md | 89110 | 77658 | 22277 | 19414 |
| Docs/03_USER_FLOWS.md | 72658 | 66967 | 18164 | 16741 |
| Docs/04_UI_SPECS.md | 104602 | 100381 | 26150 | 25095 |
| Docs/05_TECHNICAL_ARCHITECTURE.md | 117134 | 105586 | 29283 | 26396 |
| Docs/06_DATA_MODEL.md | 218115 | 202014 | 54528 | 50503 |
| Docs/07_API_DESIGN.md | 164973 | 151717 | 41243 | 37929 |
| Docs/08_INTEGRATION_SPEC.md | 250805 | 226316 | 62701 | 56579 |
| Docs/09_CODING_GUIDELINES.md | 105575 | 104222 | 26393 | 26055 |
| Docs/10_MVP_SCOPE.md | 13896 | 13619 | 3474 | 3404 |
| Docs/11_IMPLEMENTATION_PLAN.md | 508740 | 14952 | 127185 | 3738 |
| Docs/12_VALIDATION_PROTOCOL.md | 66467 | 65683 | 16616 | 16420 |
| Docs/DEPLOY_RUNBOOK.md | 157851 | 152574 | 39462 | 38143 |
| Docs/IMPLEMENTATION_STATUS.md | 241092 | 240935 | 60273 | 60233 |
| Docs/STATUS_CHANGELOG.md | 486069 | 486436 | 121517 | 121609 |
| Docs/plan/11_F0-F7_ARCHIVE.md (yeni) | - | 283468 | - | 70867 |
| Docs/plan/11_F8.md (yeni) | - | 126533 | - | 31633 |
| Docs/plan/11_F9.md (yeni) | - | 5741 | - | 1435 |
| Docs/DOC_CHANGELOG.md (yeni) | - | 181771 | - | 45442 |

SONUÇ: sorun=0
```
