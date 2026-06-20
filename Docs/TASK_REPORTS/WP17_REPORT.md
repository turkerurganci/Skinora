# WP17 — Doc/spec/i18n mutabakat

**Faz:** P6 (F6 öncesi borç temizliği — PRE_F6_PLAN) | **Durum:** ⏳ Devam ediyor (yapım; doğrulama bekliyor) | **Tarih:** 2026-06-20

---

## Yapılan İşler

WP17, PRE_F6_PLAN'daki "Doküman↔kod hizalı" borç-temizliği paketi. Dört küme:

### Cluster A — Mekanik doc-drift (doc→kod hizalama, karar yok)
- **06 §3.25 SanctionedAddress index drift:** obsolete `IX_SanctionedAddresses_Address` satırı kaldırıldı; gerçek tek index `UQ_SanctionedAddresses_Address_Active` (filtered UNIQUE `WHERE IsActive=1`) match lookup hot-path'ini de karşılıyor (match sorguları zaten `IsActive=1` filtreler) — bu gerekçe filtered-UQ satırına işlendi.
- **04 §1 admin route tablosu:** S12 `/admin`→`/admin/dashboard`, S21 `/admin/audit-log`→`/admin/audit-logs` (gerçek FE route'ları).
- **04 §5 status badge tablosu:** `EMERGENCY_HOLD` ve `FLAGGED`'in `TransactionStatus` enum değeri değil, overlay/efektif-durum rozeti olduğunu belirten not eklendi (T84 K6 drift).
- **05 §3.3 custody tablosu:** sweep tetikleyicisi `PaymentReceivedEvent`→`ITEM_DELIVERED` state gate'i; payout/iade kaynağı "ikisi de hot wallet"→"payout sweep sonrası hot wallet, alıcı-iadesi sweep öncesi deposit adresinden" (WP2/WP3 owner kararlarının gerçeği; `SweepQueueJob` yorumu birebir).
- **07 §9.19 audit örneği:** `"action": "SELLER_PAYOUT_SENT"` (AuditAction enum'da yok) → `"WALLET_ESCROW_RELEASE"` (FUND_MOVEMENT kategorisinde gerçek değer; `AuditLogCategoryMap.cs:27`).

### Cluster B — Stale backlog (zaten çözülmüş, doğrula & kapat)
- **T33-SuccessRate-FractionVsPercent:** kod (`UserConfiguration.HasPrecision(5,4)`, `UserProfileService`), 06 §3.1 ve 07 §5.x örnekleri **hepsi fraction (0..1)** üzerinde hizalı (M1 2026-05-01 kapandı). Aksiyon gerekmedi.
- **permissioncatalog-xmldoc-drift:** `PermissionCatalog.cs:56` xmldoc zaten "14 catalog entries" diyor, `All` 14 içeriyor (backlog'taki "11→12" stale). Aksiyon gerekmedi.

### Cluster C — AD6-AD7 kontrat recon (owner kararı: kodu spec'e hizala)
04 §8.4-8.5 ↔ 07 §9.6/§9.7 arası 3 eksik alan eklendi:
- **AD6 list `cancelledAt`** (04 §8.4 "Tamamlanma/İptal" kolonu): `AdminTransactionListItemDto` + `TxListProjection` + `MapListItem` + her iki list query path (`ListAsync`/`ListForUserAsync`) + FE `AdminTransactionListItem` tipi + `TransactionListTable` (completedAt ?? cancelledAt). 07 §9.6 örneği güncellendi.
- **AD7 party `reputationScore`** (04 §8.5 "Taraf Detayları — skor"): yeni `AdminTransactionPartyDetailDto` (liste için light `AdminTransactionPartyDto` korunur) + `LoadPartyAsync` + `ComputeReputation` (kullanıcı-yüzlü `TransactionDetailService` deseni birebir: `ROUND(rate×5,1,ToZero)`, rate null→null) + FE party tipine optional `reputationScore` + `TransactionDetailView` (★ skor).
- **AD7 notification `content`** (04 §8.5 "içerik"): `AdminTxNotificationDto.Content` ← `Notification.Body` (`BuildNotificationHistoryAsync`) + FE tip + `TransactionDetailView`. 07 §9.7 tablosu güncellendi.
- **audit-doc-drift `SELLER_PAYOUT_SENT`:** Cluster A'da doc-fix ile kapatıldı (yeni AuditAction eklemek parity-test'li kod işi; doc-recon kapsamında örnek düzeltildi).

### i18n (owner kararı: hibrit — payload-tipine göre)
- **Notification template (backend .resx, mevcut desen):** `NotificationTemplates.{tr,es,zh}.resx` 28 tipin tamamına (56 entry) tamamlandı; es/zh 12→56, tr 30→56. Placeholder token'lar (`{ItemName}` vb.) + `USDT`/`Skinora` korundu; key sırası base ile birebir.
- **Dispute auto-check mesajları (8 mesaj):** auto-checker'lar artık stable `MessageKey` döndürür (`AutoCheckResult.Message`→`MessageKey`); yeni `DisputeAutoCheckMessages` (in-code en/tr/es/zh sözlüğü + EN fallback) `DisputeService`'te **disputing buyer'ın locale'inde** lokalize edilir (Open/SubmitTxHash/Escalate). Buyer hem open response'u hem stored `SystemCheckResult`'ı hem `DISPUTE_RESULT` bildirimini (tek alıcı) aynı dilde görür. FE değişmedi (DTO hâlâ lokalize text). Manual-escalate response mesajı da lokalize edildi (`ManualEscalated` key). Unit test eklendi.
- **Admin permission label (FE-key-mapping, mevcut desen):** eksik `VIEW_DISPUTES`/`MANAGE_DISPUTES` (WP5) FE i18n'e eklendi (`adminRoles.permissions` 12→14, 4 dil). Backend `PermissionCatalog` label'ı zaten fallback.
- **Admin settings label (FE-key-mapping):** yeni `adminSettings.labels.<key>` (59 ayar × 4 dil); backend setting key'lerindeki nokta'lar i18n için underscore'a sanitize edildi (next-intl nokta'yı path separator sayıyor). `SettingRow` `t.has(labelKey) ? t(labelKey) : setting.label` ile client-localize (backend TR label fallback).
- **T103-K4 steam warning:** ölü, TR-sabit `AdminSteamAccountsResponse.warningMessage` alanı kaldırıldı (FE zaten banner'ı hesapların `status`'ünden client-derive ediyordu). Backend `BuildWarning` + DTO alanı + FE tip + 07 §9.10 doc temizlendi; degraded banner client-derive olarak kaldı.
- **ToS/Privacy/Support taslak içeriği:** WP13 placeholder'ları gerçek **taslak** metinle dolduruldu (`legal.*`, 4 dil). Açıkça "taslak — hukuki tavsiye değildir, lansman öncesi hukuki review gerekir" çerçevesinde. Key yapısı değişmedi (LegalPage sabit section key'leri render eder).

---

## Etkilenen Modüller / Dosyalar

**Dokümanlar:** `Docs/04_UI_SPECS.md`, `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/06_DATA_MODEL.md`, `Docs/07_API_DESIGN.md`

**Backend (kod):**
- Disputes: `AutoCheckers/IDisputeAutoCheckers.cs` (Message→MessageKey), `PaymentDisputeAutoChecker.cs`, `DeliveryDisputeAutoChecker.cs`, `WrongItemDisputeAutoChecker.cs`, **yeni** `AutoCheckers/DisputeAutoCheckMessages.cs`, `Disputes/DisputeService.cs`
- Notifications: `Resources/NotificationTemplates.{tr,es,zh}.resx`
- Steam: `Application/Admin/AdminSteamBotDtos.cs`, `Application/Admin/AdminSteamBotQueryService.cs`
- Transactions: `Application/Admin/AdminTransactionQueryDtos.cs`
- API: `Services/AdminTransactionQueryService.cs`

**Backend (test):** `AdminT63EndpointTests.cs`, `DisputesEndpointTests.cs`, `DisputeServiceTests.cs`, **yeni** `Skinora.Disputes.Tests/Unit/DisputeAutoCheckMessagesTests.cs`

**Frontend:** `lib/api/admin.ts`, `components/admin/{SettingRow,SteamAccountsView,TransactionDetailView,TransactionListTable}.tsx`, `i18n/messages/{en,tr,es,zh}.json`

---

## Kabul Kriterleri Kontrolü

WP17'nin formal AC listesi yok; "Doküman↔kod hizalı" yeteneği DEFERRED_BACKLOG §6 + ilgili kalemlerle ölçülür.

| # | Kalem | Sonuç | Kanıt |
|---|---|---|---|
| 1 | T33-SuccessRate fraction/percent | ✓ (no-op) | kod+06+07 fraction hizalı; M1 kapalı |
| 2 | AD6-AD7-contract-recon (K3/K4/K6) | ✓ | 3 alan eklendi; AdminT63 testleri PASS |
| 3 | audit-doc-drift (SELLER_PAYOUT_SENT) | ✓ | 07 §9.19 → WALLET_ESCROW_RELEASE (enum'da var) |
| 4 | permissioncatalog-xmldoc-drift | ✓ (no-op) | xmldoc zaten "14" |
| 5 | datamodel-sanctioned-index-drift | ✓ | 06 §3.25 obsolete index kaldırıldı |
| 6 | admin-route-table-drift | ✓ | 04 §1 S12/S21 düzeltildi |
| 7 | T84-emergencyhold-status-doc-drift | ✓ | 04 §5 overlay-rozet notu |
| 8 | WP3-deferred 05 §3.3 sweep tetik/iade | ✓ | 05 §3.3:316/317 WP2/WP3 gerçeğine hizalandı |
| 9 | backend-i18n-migration (notification es/zh) | ✓ | resx 56/56/56, key parity ✓ |
| 10 | dispute auto-check i18n | ✓ | stable key + buyer-locale lokalizasyon + unit test |
| 11 | T103-K4 (AD10 warningMessage) | ✓ | ölü TR alan kaldırıldı, FE client-derive |
| 12 | settings/permission label i18n | ✓ | adminSettings.labels 59×4 + permission 14×4 |
| 13 | content-authoring (ToS/Privacy/Support) | ~ Taslak | gerçek taslak metin 4 dil; **hukuki review gerekir** |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend build (Debug) | ✓ 0W/0E | `dotnet build Skinora.sln -c Debug` |
| Disputes.Tests | ✓ 50/50 | yeni tr-locale uçtan-uca + `DisputeAutoCheckMessages` unit + escalate assertion |
| API.Tests | ✓ 537/537 | Cluster C (reputationScore/cancelledAt/content) + steam-warning kaldırma + dispute endpoint |
| Notifications.Tests | ✓ 153/153 | resx fallback testleri unsupported-locale'e güncellendi (CI-fix; aşağıya bkz.) |
| dotnet format --verify | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes` exit 0 |
| FE `next build` | ✓ | tüm route'lar (privacy/terms/support/admin dahil) derlendi; type-check + lint geçti |
| FE prettier | ✓ | değişen FE dosyaları `prettier --check` temiz (write sonrası) |
| i18n parity (4 dil) | ✓ 1291×4 | en/tr/es/zh leaf key set birebir (0 missing/0 extra); adminSettings.labels 59, adminRoles.permissions 14 |
| Integration (Docker/SQL Server) | CI-authoritative | lokal SQLite ile API.Tests yeşil |

---

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

---

## Altyapı Değişiklikleri
- **Migration:** Yok (şema değişikliği yok; yalnız DTO/i18n/doc).
- **Yeni paket:** Yok (`Microsoft.Extensions.Localization` zaten Notifications'ta; dispute lokalizasyonu in-code sözlük — Disputes modülüne yeni paket eklenmedi).
- **Config/env:** Yok.
- **Docker:** Yok.
- **Kontrat değişikliği:** AD6 list `cancelledAt` (+), AD7 party `reputationScore` (+) + notification `content` (+); AD10 `warningMessage` (−, ölü TR alan). Hepsi pre-launch, tüketici uyumlu.

## Dış Varsayımlar
- i18n altyapısı (.resx + IStringLocalizer, next-intl) mevcut — yeni paket/middleware gerekmedi (hibrit yaklaşım request-locale middleware ihtiyacını ortadan kaldırdı). **Kanıt:** `NotificationsModule.cs:51 AddLocalization()`, `Microsoft.Extensions.Localization 9.0.3`.
- es/zh/tr çevirileri + ToS taslağı bu çalışmada üretildi (ücretli çeviri servisi değil) — best-effort, profesyonel ton; ToS hukuki review gerektirir.

## Commit & PR
- Branch: `task/WP17-doc-spec-i18n`
- Commit: `ef4719c` (impl) + PR-ref commit
- PR: [#190](https://github.com/turkerurganci/Skinora/pull/190)
- CI: ✓ run [`27870277748`](https://github.com/turkerurganci/Skinora/actions/runs/27870277748) (`0287f20`) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker BE+FE/Gate) — CI-fix sonrası. İlk run (`3b1041b`) 2 fallback testinde fail'di → düzeltildi (bkz. CI-fix bölümü).

## Known Limitations / Follow-up
- **Notification `{Outcome}` fragment per-recipient lokalizasyonu (kalan):** `DisputeEscalatedNotificationConsumer`'ın **auto-escalated (iki-taraf)** dalı + `DisputeResolvedNotificationConsumer` hâlâ TR-sabit outcome fragment'ı enjekte ediyor. Dispatcher template'i recipient-locale'inde render eder ama `{Outcome}` param'ını verbatim enjekte eder; iki-recipient'te her tarafın kendi locale'i gerektiği için bu, notification-mimari düzeltmesi gerektirir (ertelendi). **WP17'de lokalize edilenler:** API response'ları (open/submit/escalate `message`) + auto-resolved bildirimi + **manual-escalate bildirimi** (tek-recipient buyer; `DisputeEscalatedEvent.OutcomeText` ile produce-time buyer-locale pre-localize — yapım-içi review F1 düzeltmesi).
- **Dispute `SystemCheckResult` admin görünümü:** buyer-locale'inde saklanır (notification bağı + tek çeviri kaynağı için bilinçli); admin bu tanı alanını buyer'ın dilinde görür.
- **Stored audit reason'ları:** `AdminDisputeService` dispute-çözüm reason'ı + `AdminTransactionService` hold-cancel reason'ı TR-sabit kalır — bunlar saklanan tarihsel/audit verisidir (UI label değil), lokalize edilmez.
- **ToS/Privacy/Support metni TASLAKTIR** — jurisdiction/governing-law/şirket-entity belirsiz; lansman öncesi hukuk danışmanı review'u şart.

## Yapım-içi Adversarial Review (Workflow)
WP17 diff'i, commit öncesi 5-boyutlu adversarial Workflow ile gözden geçirildi (review → refute-default verify, 12 ajan): **7 ham bulgu → 4 onaylı (3×S3 + 1×S2), 0 bloke-edici.** Dördü de bu PR'da kapatıldı:
- **F4 (S2 — doc):** 04 §5 notu `FLAGGED`'i yanlışlıkla "TransactionStatus enum değeri değil" sayıyordu (gerçekte kanonik enum değeri — `TransactionStatus.cs`, oluşturmada atanır, 06 §2.1 / 07 §9.6). Not düzeltildi → yalnız `EMERGENCY_HOLD` overlay olarak işaretli.
- **F1 (S3 — i18n):** manual-escalate **bildirimi** hâlâ TR `{Outcome}` enjekte ediyordu (API response lokalizeyken — asimetri). `DisputeEscalatedEvent.OutcomeText` ile produce-time buyer-locale lokalizasyonu eklendi (tek-recipient buyer; auto-escalated iki-taraf deferred).
- **F3 (S3 — doc):** 07 §7.8-§7.10 dispute `message` alanlarına buyer-locale lokalizasyon notu eklendi (§9.10 steam konvansiyonu ile tutarlı).
- **F2 (S3 — test):** non-en (tr) buyer ile DisputeService uçtan uca integration testi eklendi (stored `SystemCheckResult` + open/escalate response + event `OutcomeText`).
3 ham bulgu verify aşamasında çürütüldü (refute-default).

## CI-fix (ilk push sonrası)
İlk push (commit `3b1041b`) CI'ında Unit + Integration job'ları **2 notification fallback testi**nde kırıldı: `ResxNotificationTemplateResolverTests.Resolve_LocaleMissingForKey_FallsBackToEnglish` + `NotificationDispatcherTests.DispatchAsync_FallsBackToEnglishWhenLanguageUnsupportedForKey`. **Root cause:** ikisi de "tr.resx `TRANSACTION_FLAGGED`'i atlıyor → İngilizce fallback" varsayıyordu; WP17 resx parity'yi tamamlayınca (tr/es/zh hepsi 56 entry) tr artık o key'i çeviriyor → fallback tetiklenmiyor. **Fix:** fallback artık **desteklenmeyen locale** (`fr`, resx'i yok → neutral=İngilizce) ile test ediliyor (mekanizma coverage'ı korundu; testler `_UnsupportedLocale_`/`_WhenLanguageUnsupported` olarak yeniden adlandırıldı). **Doğrulama:** Notifications.Tests **153/153** + tüm-solution `Category=Unit` 0 fail. **Ders:** shared/resx/cross-cutting değişikliklerde tüm suite lokalde çalıştırılmalı — ilk push Disputes+API ile sınırlıydı, Notifications.Tests atlanmıştı.

## Notlar
- **Working tree:** Adım -1 temiz (session başında clean).
- **Adım 0 (main CI startup):** son 3 main run success (`27859178443`/`27859178445` WP16 #189, `27848423788` WP15).
- **i18n hibrit kararları (owner, AskUserQuestion):** ToS=taslak yaz · backend i18n=tam migrasyon · AD6-AD7=kodu spec'e hizala · mekanizma=hibrit (notification backend .resx, admin/dispute FE-key-mapping/in-code, steam ölü-alan-kaldır).
