# T42 — AuditLog servisi

**Faz:** F2 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-05-01

---

## Yapılan İşler

- Merkezi `IAuditLogger` servisi (09 §18.6) `Skinora.Platform/Application/Audit/` paketinde yazıldı: `AuditLogger.LogAsync` çağıran taraf'ın `AppDbContext` change tracker'ına `AuditLog` row'unu Add eder, **`SaveChangesAsync` çağırmaz** — UoW disiplini korunur, audit row caller'ın iş değişikliğiyle aynı transaction'da commit'lenir.
- 06 §8.6a aktör invariantı `AuditLogger.ValidateActor` içinde zorlanır: `ActorType.SYSTEM` yalnız `SeedConstants.SystemUserId` ile, `ADMIN`/`USER` `Guid.Empty` veya SYSTEM Guid'i ile çağrılırsa `InvalidActorException` (InvalidOperationException) atılır. `EntityType` ve `EntityId` boş geçilirse de aynı exception.
- `AuditLogCategoryMap` 12 `AuditAction` değerini 07 §9.19'un üç API kategorisine eşler (`FUND_MOVEMENT`, `ADMIN_ACTION`, `SECURITY_EVENT`). Yeni `AuditAction` eklendiğinde `Every_AuditAction_Has_A_Category` testi build'i kırar — silent gap yok.
- Read tarafında `IAuditLogQueryService` + `AuditLogQueryService`: paged listeleme, 5 query filter (`category`, `dateFrom`, `dateTo`, `search` → EntityId LIKE, `transactionId` → `EntityType="Transaction" AND EntityId=tx`), default `pageSize=20`, max `100` cap. Sonuçlar `Id` desc (newest-first); single round-trip ile actor + subject `User` join hidrasyonu, SYSTEM aktör için `displayName="System"` + `steamId=null`. `OldValue/NewValue` JSON parse edilirse `JsonElement?` döner; parse edilemezse string node olarak surface edilir (06 §3.20: JSON tavsiye, zorunlu değil).
- 07 §9.19 AD18 endpoint'i: `GET /api/v1/admin/audit-logs` `AdminController` üzerine eklendi (`Permission:VIEW_AUDIT_LOG` policy, `[RateLimit("admin-read")]`, `PagedResult<AuditLogListItemDto>` response).
- Mevcut tek doğrudan INSERT call site (`SystemSettingsService.UpdateAsync`, T41) `IAuditLogger.LogAsync` çağrısına migrate edildi. Davranış aynı (action/actor/entity alanları değişmedi); xmldoc'ta T42 forward-devir notları temizlendi. `ISystemSettingsService` özeti merkezi audit pipeline'a referans gösterir.
- `PlatformModule.AddPlatformModule()` `IAuditLogger` (Scoped) + `IAuditLogQueryService` (Scoped) DI kayıtları eklendi. `AdminController` DI imzası `IAuditLogQueryService _auditLogs` ile genişletildi.

## Etkilenen Modüller / Dosyalar

**Yeni (10):**
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogEntry.cs`
- `backend/src/Modules/Skinora.Platform/Application/Audit/IAuditLogger.cs` (+ `InvalidActorException`)
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogger.cs`
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs`
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogDtos.cs` (`AuditLogListItemDto` + `AuditLogParticipantDto` + `AuditLogListQuery`)
- `backend/src/Modules/Skinora.Platform/Application/Audit/IAuditLogQueryService.cs`
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogQueryService.cs`
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/AuditLoggerTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/AuditLogQueryServiceTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/AdminAuditLogEndpointTests.cs`

**Değişen (5):**
- `backend/src/Modules/Skinora.Platform/PlatformModule.cs` — `IAuditLogger` + `IAuditLogQueryService` DI.
- `backend/src/Modules/Skinora.Platform/Application/Settings/ISystemSettingsService.cs` — özet T42 forward-devir → merkezi audit pipeline referansı.
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsService.cs` — `IAuditLogger` ctor inject + `LogAsync` çağrısı (doğrudan `Set<AuditLog>().Add` yerine).
- `backend/src/Skinora.API/Controllers/AdminController.cs` — `PolicyViewAuditLog` policy const + `IAuditLogQueryService` inject + `ListAuditLogs` endpoint (AD18).
- `backend/tests/Skinora.Platform.Tests/Integration/SystemSettingsServiceTests.cs` — `CreateService` artık `AuditLogger` örneğini de inject eder (3-param ctor).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Merkezi AuditLog servisi: tüm audit kayıtları bu servis üzerinden yazılır | ✓ | `IAuditLogger`/`AuditLogger` `Skinora.Platform/Application/Audit/`. Mevcut tek call site (`SystemSettingsService.UpdateAsync`) `_auditLogger.LogAsync(...)` çağrısına migrate edildi. Audit yazımı için yeni doğrudan `Set<AuditLog>().Add` çağrısı **yok** (production code grep clean — `AuditLogs.Add`/`Set<AuditLog>().Add` referansları yalnız test seed'lerinde, audit-write yolunda değil). |
| 2 | ActorType + ActorId invariantı zorunlu | ✓ | `AuditLogger.ValidateActor` 4 kuralı zorlar: SYSTEM → `SystemUserId` zorunlu; ADMIN/USER → `Guid.Empty` ve `SystemUserId` yasak. `AuditLoggerTests` 3 negative test (`System_Actor_With_NonSystem_Guid_Throws`, `Admin_Actor_With_Empty_Guid_Throws`, `User_Actor_With_System_Guid_Throws`). |
| 3 | Doğrudan INSERT yasağı (sadece servis üzerinden) | ✓ kısmi | Üretim kodunda merkezi servis dışında doğrudan INSERT yok; rapor yazma anında grep ile doğrulandı. **Mekanik garanti not:** EF Core seviyesinde DbContext'in `Set<AuditLog>().Add` çağrısını kim yaptığını ayırt etme yolu yok (caller-aware enforcement runtime'da kolay değil — analyzer/architecture test ile uygulanabilir, T42 scope dışı). Architecture test ekleme T63b'de retention/cleanup job'ları gözden geçirilirken yapılacak — devir Notlar bölümünde belgeli. Fonksiyonel etki yok: yeni audit yazımları zorunlu olarak `IAuditLogger`'dan geçer (PR review + xmldoc rehberliği). |
| 4 | Immutable kayıt (UPDATE/DELETE engeli) | ✓ | `AppDbContext.EnforceAppendOnly` (T25) `IAppendOnly` impl olan `AuditLog` için `Modified`/`Deleted` state'lerinde `InvalidOperationException` atar. `AuditLoggerTests.LogAsync_Persisted_Row_Cannot_Be_Updated_Or_Deleted` (T25 `AuditLogEntityTests` UPDATE/DELETE testleriyle paralel — T42 yazım yolu çıkışlarının da aynı guard'a tabi olduğunu doğrular). |
| 5 | `GET /admin/audit-logs` → audit log listesi (paginated, filtrelenebilir) | ✓ | `AdminController.ListAuditLogs` AD18; `Permission:VIEW_AUDIT_LOG` + `admin-read` rate-limit. `AdminAuditLogEndpointTests` 7 senaryo: 401 anon / 403 user / 403 admin-no-perm / 200 admin / 200 super-admin / category filter / transactionId filter. `AuditLogQueryServiceTests` 12 senaryo: tüm filter kombinasyonları + paging cap + actor/subject hidrasyon + non-JSON detail surface. |
| 6 | 12 AuditAction türü destekleniyor | ✓ | `AuditAction` enum (06 §2.19) 12 değer: 5 Fon + 6 Admin + 1 Güvenlik. `AuditLogCategoryMapTests.Every_AuditAction_Has_A_Category` — yeni enum değeri map'e eklenmezse test build'i bozar. `CategoryFor` 12 InlineData satırıyla 1:1 doğrulanır. |

## Doğrulama Kontrol Listesi

| # | Madde | Sonuç |
|---|---|---|
| 1 | 06 §3.20 AuditLog yapısı doğru mu? | ✓ — `AuditLog` entity (T25) zaten 06 §3.20 1:1 (Id long, UserId nullable Guid, ActorId Guid NOT NULL, ActorType enum, Action enum, EntityType nvarchar(100), EntityId nvarchar(50), OldValue/NewValue text NULL, IpAddress nvarchar(45) NULL, CreatedAt). T42 yapı değişikliği yapmaz — yalnız servis katmanı ekler ve append-only invariant'ı yazma yolunda kullanır. |
| 2 | 09 §18.6 merkezi servis kuralları uygulanmış mı? | ✓ — "Merkezi servis üzerinden yazılır" → `IAuditLogger.LogAsync` tek yazım yolu (production). "Aktör invariantı (ActorType + ActorId)" → `ValidateActor` 4 negatif kuralla zorlanır. "İmmutable" → `EnforceAppendOnly` zaten T25'te. "Doğrudan INSERT yasağı" → mevcut tek call site migrate edildi; yeni call site eklenecekse `IAuditLogger` zorunlu. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (T42 yeni — `AuditLogCategoryMapTests`) | ✓ 19/19 | `dotnet test tests/Skinora.Platform.Tests --filter "Audit"` — 12 InlineData (CategoryFor) + 1 Every_AuditAction guard + 3 ActionsInCategory + 1 unknown-category empty + 5 IsValidCategory InlineData. |
| Integration (T42 yeni — `AuditLoggerTests`) | ✓ 8/8 | Admin actor stamping + SYSTEM actor + 3 negative invariant + 2 empty-EntityType (theory) + empty-EntityId + UPDATE/DELETE rejection. |
| Integration (T42 yeni — `AuditLogQueryServiceTests`) | ✓ 12/12 | NoFilters + 3 category branch + 2 date-range + search + transactionId + actor/subject hidrasyon + system-actor displayName + non-JSON detail + paging splits + pageSize cap. |
| Integration (T42 yeni endpoint — `AdminAuditLogEndpointTests`) | ✓ 7/7 | 401 anon + 403 user + 403 admin-no-perm + 200 paginated + category filter + transactionId filter + super-admin bypass. |
| Tüm Skinora.Platform.Tests | ✓ 133/133 | 23 s — T41'den 89 → 133 (+44; 19 unit + 8 + 12 + retro 5'er fact'lı diğerleri). |
| Tüm Skinora.API.Tests | ✓ 246/246 | 4 m 4 s — T41'den 239 → 246 (+7 yeni endpoint testi). |
| Solution toplam | ✓ tümü PASS (regresyon yok) | Payments 6 / Admin 20 / Disputes 11 / Notifications 63 / Fraud 12 / Auth 93 / Steam 21 / Shared 166 / Transactions 68 / Platform 133 / API 246 = 839. |
| Build (Release) | ✓ 0W/0E | `dotnet build -c Release` Build succeeded. |
| Format | ✓ temiz | `dotnet format --verify-no-changes` exit=0. |

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni entity/column/index yok. `AuditLog` şeması T25/T28'de set edildi; T42 sadece servis katmanı.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni NuGet paketi:** Yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok.
- **Auth/AuthZ:** Yeni endpoint `[Authorize(Policy = "Permission:VIEW_AUDIT_LOG")]` arkasında. Anonymous → 401; non-admin → 403; admin without permission → 403; super-admin bypass `PermissionAuthorizationHandler` üzerinden. 7 entegrasyon testiyle doğrulandı.
- **Input validation:** Query parametreleri opsiyonel; bilinmeyen `category` 400 yerine empty result set döner (07 §9.19 üç enum verir; AD15'in unknown-roleId davranışıyla simetrik). `pageSize > 100` → cap (DoS koruması). `search` LIKE wildcard'ı server-side eklenir, kullanıcı `%`/`_` ile özel pattern enjekte etmiş olsa bile EF `Functions.Like` parametrize edilir (SQL injection güvenli).
- **Aktör invariantı (06 §8.6a):** `AuditLogger` SYSTEM Guid'inin USER/ADMIN için kullanılmasını engeller — audit row tahrifi/spoofing riski azalır. ActorId boş Guid yasak.
- **PII surface:** `Detail` JSON'u `NewValue` ham içeriği — bu T42'de değişmedi. T78–T80 PII redaction devri T37'de mevcuttu, audit log'da PII içeriği kaynak servisin sorumluluğu (örn: `WALLET_ADDRESS_CHANGED` rotation cüzdan adresi yazar — bu zaten admin görünür alan, KVKK/GDPR uyumu T36 hesap silme akışında "kişisel veri anonim, audit log korunur" şeklinde belgeli — 05 §6.5).
- **Audit immutability:** `EnforceAppendOnly` UPDATE/DELETE'i `InvalidOperationException` ile reddeder; AuditLogger merkezi servisi kullanılsa bile guard yazma yolu dışında da aktiftir (test ile doğrulandı).
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- **Branch:** `task/T42-audit-log-service`
- **Commit:** TBD
- **PR:** TBD
- **CI:** TBD

## Known Limitations / Follow-up

- **Architecture test ile mekanik enforcement:** Bugün "doğrudan INSERT yasağı" PR review + xmldoc rehberliğine dayanıyor. Üretim kodunda `Set<AuditLog>().Add` çağrılarını yasaklayan bir `NetArchTest` veya Roslyn analyzer kuralı T63b/T106 (admin audit ekranları + retention) sırasında scope'a alınacak — bu task'larda audit log etrafındaki kullanım yoğunluğu artar, mekanik guard maliyeti kendini karşılar. Bugün için forward-devir.
- **`detail` field semantiği:** Şu an `NewValue` JSON parse edilip surface ediliyor. 07 §9.19 örneğinde detail tx hash + amount + stablecoin gibi alanlar gösteriyor — bu içerik T44+ wallet/transaction servislerinin AuditLog yazarken yapılandıracağı `NewValue` JSON şemasına bağlı. T42 sadece pass-through; içerik şeması yazıcı task'ın sorumluluğu.
- **`transactionId` filter semantiği:** `EntityType="Transaction" AND EntityId=tx` üzerinden çalışır. T44+ wallet event'leri Transaction'a referans verir ama EntityType "Wallet"/"Transaction" karışık olabilir. İhtiyaç duyulursa filter EntityType'sız `EntityId=tx OR JSON_VALUE(NewValue, '$.transactionId')=tx` şeklinde genişletilebilir — bugün gereksinim yok, tek seed senaryomuzda WALLET_REFUND'lar EntityType="Transaction" yazıyor.
- **07 §9.19 örneğindeki `SELLER_PAYOUT_SENT` action enum'da yok:** 06 §2.19 12 değer source-of-truth; örnekteki action ya `WALLET_ESCROW_RELEASE`'in yeniden adlandırılmış hali ya da tipo. Doc-pass'te 07 örneği enum'a uyumlanacak (devir Notlar). Backend kod source-of-truth ile uyumlu.
- **`AuditLogListItemDto.Action` string olarak serialize edilir:** 07 §9.19 örnekleri string action gösteriyor; default `JsonStringEnumConverter` API'da yapılandırılmış olabilir/olmayabilir, DTO bağımsız davransın diye `Action: string` (action.ToString()) tercih edildi. Tüketici tarafta enum→string mapping kayıp değil — değer `nameof(AuditAction.X)` ile aynı.

## Notlar

- **Working tree (Adım -1):** Temiz başladı.
- **Main CI startup check (Adım 0):** Son 3 main run'ı `success` (run id'leri `25226007307`, `25226007297`, `25223615598`).
- **Dış varsayımlar (Adım 4):** Yok — saf in-process servis + EF Core; yeni paket veya dış API yok. T25 entity + EnforceAppendOnly + T17 enum-as-string converter yerinde.
- **Scope onayı:** Kullanıcı 2026-05-01 onayladı (8 yeni dosya + 4 düzenleme, T41 forward-devir SystemSettingsService migration dahil).
- **Test fixture detayı:** `AuditLoggerTests` ve `AuditLogQueryServiceTests` `IntegrationTestBase` shared SQL Server fixture'ı kullanır; SYSTEM user `UserConfiguration.HasData` (06 §8.9) tarafından `EnsureCreatedAsync`'te seed edildiği için test SeedAsync'ler yalnız test-specific row ekler (ilk implementasyon HasData'yı tekrar add edip duplicate-PK aldı, fix tek satır comment ile).
- **`ListAsync_NoFilters_Returns_All_Rows_Newest_First` ordering assertion'ı:** İlk yazım kategori-at-slot assert ediyordu — fragile (insertion order'a bağlı). Final form `Id` desc invariant'ı doğrudan test eder.
