# WP12 — Kullanıcı kenar durumları (User edge cases)

**Durum:** ✓ Tamamlandı — bağımsız validator PASS (2026-06-19)
**Branch:** `task/WP12-user-edge-cases` · **PR:** [#184](https://github.com/turkerurganci/Skinora/pull/184)
**Kaynak plan:** `PRE_F6_PLAN.md` WP12 (P4)
**Tarih:** 2026-06-19

---

## 1. Kapsam

Beş bağımsız kullanıcı-kenar-durumu kalemi (PRE_F6_PLAN WP12 backlog'u). Owner kararları (AskUserQuestion, bu chat — hepsi öneri seçildi):

| # | Kalem | Owner kararı |
|---|---|---|
| 1 | OPEN_LINK eşzamanlı accept yarışı → 409 | Karar gerektirmez (net fix) |
| 2 | Per-tx iade adresi override | **Saf snapshot** — accept profil/cooldown'a dokunmaz |
| 3 | Trade-offer URL DTO | **Backend DTO + cross-module port şimdi**; FE href WP13 |
| 4 | Hesap silme atomicity | **`BeginTransactionAsync` ile sarmala** |
| 5 | Timeout uyarı ayarı | **`timeout_warning_ratio` wire-up** + seeded default; `accept_timeout_minutes` doğrula |

---

## 2. Yapılan işler (kalem kalem)

### #1 — OPEN_LINK accept yarışı → 409 (T46-OpenLinkConcurrentAcceptRace)
**Boşluk:** İki eşzamanlı OPEN_LINK accept ikisi de CREATED state-guard'ını (Stage 2) geçer, ikisi de Transaction'ı mutate eder, ikisi de `SaveChangesAsync` (line 189) çağırır. RowVersion optimistic-concurrency token'ı (`AppDbContext.OnModelCreating` — SQL Server `IsRowVersion`, SQLite `IsConcurrencyToken`) tam birini kazandırır; race-loser'ın UPDATE'i 0 satır eşler → `DbUpdateConcurrencyException` → try/catch olmadığı için **HTTP 500**. Controller `AcceptTransactionStatus.AlreadyAccepted`'i zaten 409'a map ediyordu (`TransactionsController.cs`).
**Fix:** `AcceptAsync` SaveChanges `try/catch(DbUpdateConcurrencyException)` ile sarmalandı. Catch'te no-tracking status re-query → persisted status `ACCEPTED` ise `Failure(AlreadyAccepted)` (409, sequential guard ile aynı sözleşme), başka herhangi bir state ise orijinal exception **re-throw** (maskeleme yok — beklenmeyen state 500 olarak yüzeye çıkar). Tracked entity stale olduğu için re-query `AsNoTracking`.

### #2 — Per-tx iade adresi snapshot (T90 K4)
**Boşluk:** `AcceptAsync` accept-anı iade adresini `Transaction.BuyerRefundAddress` snapshot'ına yazıyordu (doğru) **ama ek olarak** `User.DefaultRefundAddress` + `User.RefundAddressChangedAt`'i de güncelliyordu (eski T46 satır 168-175). Bu, 02 §12.2 "işlem bazlı adres" + 04 §7.3 "**yalnızca bu işlem için geçerli adres değişikliği, profil adresi etkilenmez**" + 02 §12.3 snapshot prensibi ile çelişiyordu ve alıcıyı profil-cooldown penceresinde başka açık-link davetlerini kabul etmekten kilitleyebiliyordu.
**Fix:** Profil mutasyonu kaldırıldı. Accept yalnız `Transaction.BuyerRefundAddress`'i set eder (Stage 6). **Stage-5 cooldown gate'i korundu** — devam eden bir *profil* adres-değişimi cooldown'u (T34 wallet akışı) accept'i hâlâ bloklar (02 §12.3 "işlem kabul etme engellenir"), ama accept'in kendisi yeni cooldown başlatmaz.

### #3 — Trade-offer URL DTO (T90 K3)
**Boşluk:** 04 §7.3 `TRADE_OFFER_SENT_TO_SELLER` (satıcı) / `TRADE_OFFER_SENT_TO_BUYER` (alıcı) state'lerinde "Steam'e git linki" ister; `TradeOffer.SteamTradeOfferId` (Steam modülü) mevcut ama `TransactionDetailDto`'da URL alanı yoktu. Transactions modülü Steam'e referans **vermez** (Steam → Transactions; ters referans cycle olurdu).
**Fix:** Cross-module port deseni (anonymizer-port emsali):
- Arayüz `ISteamTradeOfferUrlResolver` (Skinora.Transactions.Application.Steam) — `TradeOfferDirection` (Shared enum) parametre alır.
- Null default `NullSteamTradeOfferUrlResolver` (Transactions, `TryAddScoped`).
- DB-backed `SteamTradeOfferUrlResolver` (Skinora.Steam.Application.Trade) — `services.Replace` ile prod'da swap. İstenen yöndeki en son gönderilmiş + `SteamTradeOfferId` taşıyan offer'ı seçer, `https://steamcommunity.com/tradeoffer/{id}/` döner.
- `TransactionDetailDto.SteamTradeOfferUrl` alanı (`WhenWritingNull`). `TransactionDetailService` yalnız iki ilgili state'te resolver'ı çağırır (status→direction map); diğer state'lerde + public/trimmed view'de `null`.

FE href wiring **WP13'e** (FE tamlık) bırakıldı — backend DTO alanı bu PR'da teslim.

### #4 — Hesap silme atomicity (T36)
**Boşluk:** `AccountLifecycleService.DeleteAsync` PII anonimleştirmeyi 3 ayrı `SaveChangesAsync` round-trip'ine yayıyordu (User → notification prefs/deliveries → auth refresh tokens), `BeginTransaction` sarması yoktu → adımlar arası hata yarı-anonimleştirilmiş hesap bırakabilirdi (User anonim ama notification `ExternalId` / refresh-token `DeviceInfo`/`IpAddress` hâlâ PII).
**Fix:** Üç adım tek `_db.Database.BeginTransactionAsync()` + `CommitAsync()` içine alındı (06 §6.2 atomik). Üç servis de aynı scoped `AppDbContext`'i paylaşır → tek bağlantıda tek transaction. Retry execution strategy yapılandırılmamış (`AppDbContext`) → doğrudan `BeginTransactionAsync` kullanıldı (mevcut `TransactionCancellationService` deseni). Cache eviction (auth anonymizer) transactional değil ama rollback yalnız hâlâ-geçerli token'ın zararsız DB re-read'ine yol açar.

### #5 — Timeout uyarı ayarı wire-up (T83a/T45)
**Boşluk A:** `DefaultTimeoutWarningPercent`=75 private const iki dosyada kopyalı (`TransactionDetailService`, `TransactionListService`) → read-path `warningThresholdPercent` (07 §7.1/§7.5) sabit dönüyordu, admin-tunable değildi.
**Önemli bulgu:** `timeout_warning_ratio` SystemSetting'i **zaten mevcuttu** (06 §3.17 row 7, `SystemSettingsValidator` ratio-key 0<x<1, `SystemSettingsCatalog`, `TimeoutSchedulingService.WarningRatioKey` uyarı job'ı için okuyordu). Keşif yeni bir `timeout_warning_percent` anahtarı önermişti — bu yanlış olurdu (`feedback_validate_placement`); doğru iş mevcut anahtarı bağlamak.
**Fix:** Yeni `TimeoutWarningThreshold` shared reader (Transactions.Application.Lifecycle) `timeout_warning_ratio`'yu okur (oran×100 → int percent, 0<x<1; unconfigured/invalid → fallback 75). Detail + List servisleri const yerine bunu çağırır (List: sayfa başına bir kez, satır başına değil). Seed: `timeout_warning_ratio` `Unconfigured`→`Default("0.75")` (mandatory listesinden çıkar; `TimeoutSchedulingService` uyarı bildirimi job'ı da artık default açık). Migration `WP12_SeedTimeoutWarningRatio` (`UpdateData`, şema yok — WP4a `price_deviation_threshold` emsali).
**Boşluk B (`accept_timeout_minutes`):** Backlog "seed'siz" diyordu. İnceleme: `accept_timeout_minutes` zaten seed'li ama `Unconfigured` (06 §3.17 default "—", env-mandatory fail-fast 06 §8.9) — bu **by-design**. Değişiklik yapılmadı; doğrulandı.

---

## 3. Etkilenen modüller / dosyalar

**Üretim kodu:**
- `Skinora.Transactions/Application/Lifecycle/TransactionAcceptanceService.cs` — #1 catch + #2 profil-mutasyonu kaldırma
- `Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs` — #3 resolver inject + #5 warning read
- `Skinora.Transactions/Application/Lifecycle/TransactionListService.cs` — #5 warning read
- `Skinora.Transactions/Application/Lifecycle/TransactionDetailDto.cs` — #3 `SteamTradeOfferUrl` alanı
- `Skinora.Transactions/Application/Lifecycle/TimeoutWarningThreshold.cs` — **YENİ** #5 shared reader
- `Skinora.Transactions/Application/Steam/ISteamTradeOfferUrlResolver.cs` + `NullSteamTradeOfferUrlResolver.cs` — **YENİ** #3 port + null default
- `Skinora.Steam/Application/Trade/SteamTradeOfferUrlResolver.cs` — **YENİ** #3 DB-backed impl
- `Skinora.Users/Application/Account/AccountLifecycleService.cs` — #4 transaction wrap
- `Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — #5 seed flip
- `Skinora.API/Configuration/TransactionsModule.cs` (#3 TryAddScoped) + `SteamModule.cs` (#3 Replace)
- `Skinora.Shared/Persistence/Migrations/20260619110914_WP12_SeedTimeoutWarningRatio.cs` — **YENİ** (+ Designer + snapshot)

**Test kodu:**
- `Skinora.Transactions.Tests/Integration/Lifecycle/TransactionAcceptanceServiceTests.cs` — #1 (2 yeni: race→409 swallow + concurrent-cancel re-throw) + #2 (Happy_Path assert revize + `Accept_With_Different_Address_Does_Not_Mutate_Profile_Default`) + `RaceAcceptDbContext` seam
- `Skinora.Transactions.Tests/Integration/Lifecycle/TransactionDetailServiceTests.cs` — #3 (3 yeni wiring testi + recording stub + BuildSut resolver param)
- `Skinora.Steam.Tests/Integration/SteamTradeOfferUrlResolverTests.cs` — **YENİ** #3 resolver (3 test)
- `Skinora.Users.Tests/Unit/Account/AccountLifecycleServiceTests.cs` — **YENİ** #4 rollback + happy-path (SQLite; csproj'a Sqlite paketleri eklendi)
- `Skinora.Transactions.Tests/Unit/Lifecycle/TransactionListServiceTests.cs` — #5 read-path-live testi (ratio 0.5→50)
- `Skinora.Transactions.Tests/Integration/Timeouts/TimeoutSchedulingServiceTests.cs` — #5 `NoWarning_When_Ratio_Unconfigured` explicit unconfigure
- `Skinora.Platform.Tests/Integration/SeedDataTests.cs` + `SettingsBootstrapTests.cs` — #5 sayım güncelleme (configured 39→40, unconfigured 20→19, env var listesinden `TIMEOUT_WARNING_RATIO` çıkarıldı)

**Doküman:** 06 §3.17 (`timeout_warning_ratio` default "—"→0.75) · 07 §7.5 (`steamTradeOfferUrl` alanı + sample) · DEFERRED_BACKLOG (5 kalem ✅) · PRE_F6_PLAN (WP12 ✅ + migration-taşıyan listesi).

---

## 4. Kabul kriterleri (self-check)

| # | Kriter | Durum | Kanıt |
|---|---|---|---|
| 1 | OPEN_LINK race-loser 500 yerine 409 ALREADY_ACCEPTED | ✅ | `Open_Link_Concurrent_Accept_Race_Loser_Returns_AlreadyAccepted_Not_500` + re-throw testi (`RaceAcceptDbContext`) |
| 2 | Accept profil iade adresini/cooldown'u mutate etmez | ✅ | `Accept_With_Different_Address_Does_Not_Mutate_Profile_Default` + Happy_Path revize |
| 3 | `steamTradeOfferUrl` iki trade-offer state'inde dolu, diğerlerinde null | ✅ | Detail wiring 3 testi + Steam resolver 3 testi |
| 4 | Silme adımlarından biri hata verirse User anonimleştirme geri alınır | ✅ | `Delete_RollsBack_User_Anonymization_When_Downstream_Anonymizer_Throws` |
| 5 | `warningThresholdPercent` `timeout_warning_ratio`'dan türetilir; seeded 0.75 | ✅ | `ActiveTimeout_WarningThreshold_Reflects_Configured_Ratio` (0.5→50) + SeedData configured |
| 6 | `accept_timeout_minutes` env-mandatory by-design doğrulandı | ✅ | SettingsBootstrapTests + 06 §3.17 |
| 7 | Migration drift yok | ✅ | `dotnet ef migrations has-pending-model-changes` → "No changes" |
| 8 | Build temiz, mevcut testler regresyonsuz | ✅ | Debug+Release 0W/0E; Unit suite'leri yeşil |

---

## 5. Test sonuçları (lokal)

- `dotnet build Skinora.sln -c Debug` → **0 error** · `-c Release` → **0 Warning / 0 Error**
- `dotnet ef migrations has-pending-model-changes` → **"No changes have been made to the model since the last migration."**
- `dotnet format Skinora.sln --verify-no-changes --severity error` → **temiz**
- Transactions.Tests `Category=Unit` → **102/102** (+1 read-path-live)
- Users.Tests → **22/22** (+2 delete atomicity, SQLite)
- Platform.Tests `SystemSettingsValidatorTests|SystemSettingsCatalogTests` → **77/77** (timeout_warning_ratio validasyonu değişmedi)
- **Integration testler CI-authoritative:** lokal Docker çalışmıyor (`Testcontainers DockerUnavailableException npipe://./pipe/docker_engine`) → acceptance race/snapshot, detail trade-url wiring, Steam resolver, SeedData, SettingsBootstrap, TimeoutScheduling, AccountLifecycle endpoint testleri CI'da koşar.

---

## 6. Dış varsayımlar (ön-uçuş)

- Yeni NuGet/npm paketi: **yok** (Users.Tests'e mevcut `Microsoft.Data.Sqlite`/`EFCore.Sqlite` 9.0.3 eklendi — test-only, kardeş projelerle aynı sürüm).
- Paid feature / plan tier: **yok**.
- Retry execution strategy: **yapılandırılmamış** (grep `EnableRetryOnFailure`/`CreateExecutionStrategy` boş) → #4 `BeginTransactionAsync` doğrudan çalışır.
- Cross-module: Steam → Transactions referansı mevcut, ters yön **yok** → #3 port (Transactions arayüz, Steam impl) cycle üretmez.
- `accept_timeout_minutes`: zaten seed'li (`Unconfigured`) → "seed'siz" backlog notu yanlıştı; by-design env-mandatory.
- `timeout_warning_ratio`: mevcut anahtar (keşif ajanı kaçırmıştı) → yeni key DEĞİL.

---

## 7. Altyapı / migration değişiklikleri

- **Migration `WP12_SeedTimeoutWarningRatio`:** saf `UpdateData` (SystemSettings Id `…0007`: `IsConfigured=true`, `Value="0.75"`; Down → `false`/`null`). Şema değişikliği **yok**. Seed satır sayısı değişmez (59); mandatory (unconfigured) 20→19, configured 39→40.
- **Owner sapması:** plan WP12'yi migration-taşımaz sayıyordu; #5 seed kararı bir `UpdateData` migration ekledi (WP4a `price_deviation_threshold` emsali). PRE_F6_PLAN güncellendi.

---

## 8. Mini güvenlik kontrolü

- Secret sızıntısı: yok.
- Auth/authorization: #4 silme akışı atomikleşti (PII güvenliği **iyileşti**); diğer kalemler yetki yüzeyini değiştirmez. `steamTradeOfferUrl` yalnız işlem taraflarına (seller/buyer) döner, public view'de null.
- Input validation: değişmedi (#5 ratio mevcut validator ile 0<x<1).
- Yeni dış bağımlılık: yok (test-only Sqlite hariç).

---

## 9. Known limitations

- **#3 FE href:** `steamTradeOfferUrl` backend DTO'da; FE `StateActionPanel` href wiring **WP13** (FE tamlık).
- **#2:** inline iade yolları (T72 ayrı senaryo) dokunulmadı; yalnız accept-path snapshot semantiği düzeltildi.
- **#4:** cache eviction transactional değil (rollback'te zararsız re-read); kalan cross-instance senaryolar MVP-dışı.
- **misc-user-features / multi-account user UI:** WP12 kapsamında değil (backlog'da açık kaldı).

---

## 10. Notlar (startup check — audit trail)

- **Adım -1 (working tree):** temiz (session başında).
- **Adım 0 (main CI son 3):** hepsi `success` — `27784885109` / `27784884990` (WP11 #182) / `27756292902` (WP10 follow-up #181).
- **WP11 PR #182:** main'e merged (`cd7dd49`, 2026-06-18) — WP12 bağımlılıkları (WP2 BUYER_REFUND dahil) merged.
- **Branch izolasyonu:** yalnız WP12 commit'leri.

---

## 11. Commit & PR

- **Branch:** `task/WP12-user-edge-cases`
- **PR:** [#184](https://github.com/turkerurganci/Skinora/pull/184)
- **Task CI:** HEAD `8753449` run [`27823238832`](https://github.com/turkerurganci/Skinora/actions/runs/27823238832) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate) — integration + seed migration SQL Server'da temiz. Sonraki HEAD `58c6153` (CI sonucu commit'i) run [`27823682521`](https://github.com/turkerurganci/Skinora/actions/runs/27823682521) de **tüm job success** (validator teyit).

---

## 12. Bağımsız Doğrulama Sonucu (Validator — ayrı chat, rapor görülmeden)

**Tarih:** 2026-06-19 · **Branch:** `task/WP12-user-edge-cases` · **HEAD commit:** `58c6153`

### Verdict: ✓ PASS — 8/8 AC, 0 bloke-edici bulgu

**Kapılar:**
- **Adım -1 (working tree):** temiz (`git status --short` boş).
- **Adım 0 (main CI son 3):** hepsi `success` (`27815897497` / `27815897107` docs(WP7) / `27784885109` WP11).
- **Adım 0b (repo memory drift):** WP12 satırı `.claude/memory/MEMORY.md`'de mevcut.
- **Adım 8a (task CI):** HEAD `58c6153` run [`27823682521`](https://github.com/turkerurganci/Skinora/actions/runs/27823682521) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate). Önceki HEAD `8753449` run `27823238832` de success.

**Validator lokal yeniden çalıştırma (Release; Docker UP — bu ortamda integration testleri de koştu, yapım ortamında Docker yoktu):**
- `dotnet build Skinora.sln -c Release` → **0 Warning / 0 Error**.
- `dotnet ef migrations has-pending-model-changes` → **"No changes have been made to the model since the last migration."** (3 uyarı pre-existing global-query-filter — WP12 kaynaklı değil).
- `dotnet format Skinora.sln --verify-no-changes` → **temiz** (exit 0).
- **Skinora.Transactions.Tests → 794/794** (Integration dahil; race→409 swallow + concurrent-cancel re-throw + no-mutate-profile hedefli filtre 3/3).
- **Skinora.Users.Tests → 22/22** (delete atomicity SQLite rollback + happy hedefli filtre 2/2).
- **Skinora.Platform.Tests → 177/177** (SeedData configured 40 / unconfigured 19 + SettingsBootstrap fail-fast).
- **Skinora.Steam.Tests → 106/106** (SteamTradeOfferUrlResolver: direction filter / null / latest-by-SentAt).

**Bağımsız kod/spec teyidi (5 kalem):**
1. **#1 OPEN_LINK 409** — `try/catch(DbUpdateConcurrencyException)` no-tracking re-query → yalnız persisted=ACCEPTED'de `AlreadyAccepted` (controller 409), aksi re-throw (maskeleme yok). Mekanizma RowVersion concurrency token'a dayanır → `AppDbContext.OnModelCreating` SQL Server `IsRowVersion()` / SQLite `IsConcurrencyToken()` doğrulandı.
2. **#2 Saf snapshot** — eski `User.DefaultRefundAddress`+`RefundAddressChangedAt` mutasyonu kaldırıldı (diff teyit); Stage-6 yalnız `Transaction.BuyerRefundAddress` set eder; Stage-5 cooldown gate profil cooldown'unu yalnız **okur** (02 §12.2 / 04 §7.3 / 02 §12.3 birebir).
3. **#3 Trade-offer URL** — `TradeOfferDirection` Shared enum (TO_SELLER/TO_BUYER mevcut), `TradeOffer` entity Direction/SteamTradeOfferId/SentAt/TransactionId mevcut. Port Transactions'ta, DB-backed impl Steam'de (`services.Replace`), null default `TryAddScoped` → bağımlılık yönü Steam→Transactions, cycle yok. Detail yalnız iki TRADE_OFFER state'inde çağırır, public view null. 07 §7.5 doc + DTO `WhenWritingNull` uyumlu.
4. **#4 Delete atomicity** — `NotificationAccountAnonymizer` + `AuthAccountAnonymizer` ikisi de aynı scoped `AppDbContext`'i inject eder → `BeginTransactionAsync` üç `SaveChanges`'i gerçekten kapsar; cache eviction non-transactional ama rollback yalnız geçerli token'ın zararsız re-read'ine yol açar (yorum doğru).
5. **#5 Timeout ratio** — `TimeoutWarningThreshold` `timeout_warning_ratio` okur (0<x<1 → ×100, fallback 75); Detail + List const'ları kaldırıldı (List sayfa başına bir okuma). Seed `Unconfigured`→`Default("0.75")`; migration `WP12_SeedTimeoutWarningRatio` saf `UpdateData` (Id …0007 IsConfigured=true/Value="0.75"; Down false/null) — seed sayısı 59 sabit, mandatory 20→19, configured 39→40 (SeedData/SettingsBootstrap assert'leri tutarlı güncellenmiş). `accept_timeout_minutes` Unconfigured (env-mandatory by-design) teyit.

**Güvenlik mini-kontrol:** secret sızıntısı yok (yalnız test connection-string referansı); auth yüzeyi #4 ile **iyileşti** (PII anonimleştirme atomik), `steamTradeOfferUrl` yalnız taraflara döner; input validation değişmedi; yeni dış bağımlılık yok (Users.Tests'e test-only `Microsoft.Data.Sqlite`/`EFCore.Sqlite` 9.0.3, kardeş projelerle aynı sürüm).

**Non-blocking gözlem (bloke etmez, verdict'i etkilemez):**
- **N1 (doc-drift, pre-existing → WP17):** 06 §3.17 başlığı "toplam **58 anahtar**" diyor; gerçek seed 59 (40 configured + 19 unconfigured). Bu +1 drift **WP1'den** gelir (`payout_gas_fee_estimate_usdt` eklendi, başlık T102'den beri 58'de kaldı) — WP12 hiç anahtar eklemedi (yalnız bir satırı flip etti), dolayısıyla drift'i ne yarattı ne de büyüttü. PRE_F6_PLAN doc-drift'i zaten WP17'ye yönlendiriyor (`audit-doc-drift`).

**Yapım raporu karşılaştırması:** Tam uyumlu. Rapordaki 8/8 AC self-check'i kanıtla doğrulandı; test sayıları tutarlı (rapor Transactions Unit 102 / validator full project 794 superset; Platform validator-catalog 77 / validator full 177 superset; Users 22 = 22; Steam resolver CI-authoritative / validator full 106). Yapım ortamında Docker yoktu (rapor integration'ı CI-authoritative işaretledi); validator ortamında Docker UP → integration testleri de lokal yeşil (daha güçlü kanıt). Rapor N1 doc-drift'ine değinmiyordu (WP12 kapsamı dışı, WP1 borcu).
