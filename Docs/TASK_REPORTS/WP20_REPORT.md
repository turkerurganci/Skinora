# WP20 — canAccept fix (kayıtlı STEAM_ID alıcı kabul edebilmeli + EMERGENCY_HOLD detay projeksiyonu)

**Faz:** F6 (T107 keşfi — WP19-style promote-before-close) | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız validator bekliyor) | **Tarih:** 2026-06-22

---

## Bağlam

T107 (E2E Happy path) PR-3 bağımsız validator'ı **S1 sapması** keşfetti: `TransactionDetailService.BuildAuthenticatedActions` `canAccept = role=="buyer" && Status==CREATED && BuyerId is null`. STEAM_ID **kayıtlı** alıcıda `TransactionCreationService` create'te `BuyerId`'yi SET ediyor → detay `canAccept=false` → **UI AcceptForm disabled**, oysa accept **endpoint'i** (party guard yalnız SteamId eşleşmesine bakar, `BuyerId is null` kontrol etmez) kabule izin veriyor. Yani TRANSACTION_INVITE alan kayıtlı hedef alıcı UI'dan kabul **edemiyor** — 03 §3.2:195 ("Eşleşiyorsa → devam eder") ile çelişir; mainline UI happy-path kırık. Ek olarak `cannotAcceptReason` FE metni gerçek gate ile uyumsuz "Mobile Authenticator / cooldown" ifadesi taşıyordu.

**Owner kararı (AskUserQuestion 2026-06-22):** "önce düzelt, sonra T107 kapat" (WP19-style) → ayrı backend/FE fix-task = **WP20**. i18n için owner kararı: **"yeniden yaz + hold'u DTO/FE'ye yansıt"** (sadece metin değil, EMERGENCY_HOLD'u detay yüzeyine projekte et).

## Kök neden analizi (2 ayrı defekt)

1. **canAccept aşırı-gate (CHANGE A):** `role=="buyer"` zaten yalnız uygun alıcıya atanır (rol çözümü `TransactionDetailService.cs:70-96`: `callerId==BuyerId` **veya** `BuyerId null && STEAM_ID && callerSteamId==TargetBuyerSteamId` **veya** invite OPEN_LINK prospective). `Status==CREATED` zaten "henüz kabul edilmedi" demek. `&& BuyerId is null` koşulu, kayıtlı STEAM_ID alıcının create-time set edilen `BuyerId`'sini yanlışlıkla "kabul edilmiş" sanıp butonu kapatıyordu. Kanonik kural 07 §7.5:1329 = "buyer + CREATED + Steam ID eşleşme (veya açık link)" — `BuyerId` terimi **yok**.

2. **EMERGENCY_HOLD detay yüzeyinde yok (CHANGE B — pre-existing bug):** Detay DTO `Status: transaction.Status` (ham enum) emit ediyordu; oysa **list servisi** zaten `IsOnHold ? "EMERGENCY_HOLD" : Status` projekte ediyor (`TransactionListService.ProjectStatus`). FE detay ağacı (`page.tsx:115/129`, `StateActionPanel.tsx:70`, `helpers.ts:36`) hold banner + frozen panel'i `status==="EMERGENCY_HOLD"`'a bağlamış — ama backend bunu hiç göndermediği için **detay sayfasında hold banner hiç görünmüyordu** (04 §7.3:1086-1093 ihlali). `EMERGENCY_HOLD` bir `TransactionStatus` enum değeri değil (06 §2.20, 04 §7.3:338) — overlay projeksiyonu.

## Yapılan İşler

- **CHANGE A** — `TransactionDetailService.BuildAuthenticatedActions`: `&& transaction.BuyerId is null` koşulu kaldırıldı → `canAccept = role=="buyer" && Status==CREATED`. `IsOnHold` early-return (satır 457-466) hold'da tüm aksiyonları zaten false bırakır → değişmedi.
- **CHANGE B** — Detay DTO `Status` alanı `TransactionStatus` → `string` (list DTO `TransactionListItemDto.Status` ile birebir). Yeni özel `ProjectStatus(Transaction)` helper'ı (`IsOnHold ? "EMERGENCY_HOLD" : Status.ToString()`, list servisinin desenini yansıtır) hem authenticated (`:309`) hem public (`:352`) yanıt yolunda uygulanır. **Sıfır FE mantık değişikliği** — FE zaten `ExtendedStatus` (string union) bekliyor; CHANGE B yalnızca mevcut (bugüne dek ölü) hold yüzeyini aktive eder.
- **i18n** — `transactionDetail.actions.created.buyer.cannotAcceptReason` (en/tr/es/zh) yanıltıcı MA/cooldown metninden nötr fallback'e çevrildi ("Bu işlem şu anda kabul edilemiyor." vb.). CHANGE A+B sonrası bu metin **ulaşılamaz/defansif** hale gelir (hold artık frozen panel'de `StateActionPanel:70`'te yakalanır, CREATED+buyer+not-hold'da `canAccept` her zaman true). Key korundu → 4-dil parity bozulmadı.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `backend/.../Lifecycle/TransactionDetailDto.cs` | `Status` alanı enum→`string` (+ açıklama) |
| `backend/.../Lifecycle/TransactionDetailService.cs` | canAccept clause drop + `ProjectStatus` helper + 2 yanıt yolunda kullanım |
| `backend/tests/.../Lifecycle/TransactionDetailServiceTests.cs` | 1 flip (bug-encoding assert) + 1 hold-status assert + 2 yeni test |
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | `cannotAcceptReason` değeri (4 dil, key sabit) |

**Sıfır migration / config / Docker / yeni dependency.** Admin DTO (`AdminTransactionDetailDto`) ayrı tip — dokunulmadı. Controller yalnız `.Body.Id` okur — derleme etkisi yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kayıtlı STEAM_ID hedef alıcı CREATED işlemi kabul edebilir (canAccept=true) | ✓ | `Registered_Steam_Id_Buyer_Can_Accept_Created_Transaction` + flip `Returns_Buyer_View...` (canAccept True); spec 03 §3.2:195 / 07 §7.5:1329 |
| 2 | canAccept yalnız uygun alıcı + CREATED; uygun olmayana sızmaz; hold'da false | ✓ | role çözümü non-party'ye 403 verir; `Held_Created_Transaction_Freezes_Accept...` (hold → canAccept false); endpoint party guard değişmedi (advisory bit) |
| 3 | EMERGENCY_HOLD detay yüzeyinde projekte edilir → FE hold banner/frozen panel tetiklenir | ✓ | `Emergency_Hold_Forces_All_Actions_False` + `Held_Created...` → `Status=="EMERGENCY_HOLD"`; list deseniyle tutarlı; 04 §7.3:1086-1093 |
| 4 | cannotAcceptReason yanıltıcı metin → nötr fallback (4 dil, parity korunur) | ✓ | git diff 4×1 satır; `check-i18n.mjs` → 1291×4 identical key sets |
| 5 | Sıfır regresyon; spec-sadık | ✓ | build 0W/0E; Transactions 803/803; API lifecycle 25/25; FE vitest 28/28; `dotnet format` exit0 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0W/0E | `dotnet build Skinora.sln -c Debug` |
| Unit+Integration (Transactions) | ✓ 803/803 | `dotnet test --filter ~Skinora.Transactions.Tests` (SQLite EnsureCreated; detay sınıfı 25/25 incl. 3 yeni) |
| Integration (API HTTP) | ✓ 25/25 | `dotnet test --filter ~TransactionLifecycleEndpointTests` (status JSON wire değişmedi) |
| Format (backend) | ✓ exit0 | `dotnet format --verify-no-changes` |
| i18n parity | ✓ | `check-i18n.mjs` 1291×4 identical (15 advisory "Gas fee" pre-existing) |
| Prettier (FE) | ✓ | `prettier --check --end-of-line=auto` clean (çıplak check = Windows-CRLF false-pos, WP18 dersi; CI LF-authoritative) |
| FE vitest | ✓ 28/28 | `vitest run` (i18n values-only değişiklik testleri etkilemez) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Yapım bitti — bağımsız validator (ayrı chat) bekliyor |
| Bulgu sayısı | — |

## Altyapı Değişiklikleri

- Migration: **Yok** (DTO tip değişikliği yalnız serileştirme; şema yok). Enum **üyesi eklenmedi** (EMERGENCY_HOLD projeksiyon-string) → Shared EnumTests / AuditLogCategoryMap parity etkilenmez.
- Config/env: **Yok**
- Docker: **Yok**

## Commit & PR

- Branch: `task/WP20-canaccept-fix`
- Commit: `b5ca885` — WP20: canAccept fix + EMERGENCY_HOLD detay projeksiyonu
- PR: [#199](https://github.com/turkerurganci/Skinora/pull/199)
- CI: ⏳ izleniyor

## Known Limitations / Follow-up

- **e2e UI smoke mainline reverti T107 PR-3'e devredildi:** `e2e/tests/happy-path.ui.spec.ts` + `e2e/src/db.ts`'deki deferred-buyer (`includeBuyer:false` + sonradan `insertBuyer`) workaround'ı yalnız bu bug'ı atlatmak için vardı; o dosyalar **yalnız `task/T107-e2e-ui` branch'inde** (main'de değil). WP20 main'e merge olunca T107 rebase edip UI smoke'u mainline kayıtlı-alıcıya çevirecek (owner planı: "sonra UI harness registered-buyer akışını da doğrular → T107 öyle kapanır").
- **cannotAcceptReason artık defansif/ulaşılamaz** — silmek yerine nötr fallback ile korundu (4-dil key-removal + kod-edit riskinden kaçınmak için).

## Notlar

- **Adım -1 (working tree):** temiz.
- **Adım 0 (main son-3 CI):** success — `27915545862` / `27915545858` / `27912352893`.
- **Dış varsayım:** yok (salt iç refactor + i18n; yeni dış bağımlılık/feature-tier yok).
- **Public-path projeksiyon kararı (reviewer onayına):** CHANGE B EMERGENCY_HOLD'u public/anonim yanıtta da (`:352`) yüzeye çıkarır. Bu kasıtlı: bastırmak gerçek alt-durumu (ör. "ITEM_ESCROWED") anonim çağırana sızdırır ki bu daha kötü; public shape zaten item/price/hold-reason gizler. List projeksiyonuyla tutarlı.
- **Spec-sadıklık:** CHANGE B spec ihlalini **düzeltir** (04 §7.3:1086-1093 detay hold yüzeyini zorunlu kılar; 07 §7.1:977 status projeksiyonu genel API kuralı). CHANGE A 02 §6.1 ("kayıtlı vs kayıtsız davet ikisi de geçerli, kayıt bir bildirim ayrımı, kabul gate'i değil") çelişkisini çözer.
- **Recon:** 2 adversarial workflow (canAccept güvenlik doğrulaması: SAFE, sızma yok; blast-radius: contract test yok, tek non-test compile sitesi 2 atama, FE sıfır-edit).
