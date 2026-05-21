# T88 — Dashboard (S05)

**Faz:** F5 | **Durum:** ⛔ BLOCKED | **Tarih:** 2026-05-21

---

## BLOCKED Bilgisi

- **Alt tür:** `PLAN_CORRECTION_REQUIRED`
- **Neden:** T88 plan kabul kriteri *"GET /transactions çağrısı"* (11 §T88 dördüncü madde, doc-ref [04 §7.1](../04_UI_SPECS.md)) backend'de implement edilmemiş bir endpoint'e dayanıyor. `GET /api/v1/transactions` (T1 — [07 §7.1](../07_API_DESIGN.md), kullanıcının kendi işlem listesi) hiçbir F0–F4 task'ında üretilmemiş.
- **Etkilenen dokümanlar:**
  - [11_IMPLEMENTATION_PLAN.md](../11_IMPLEMENTATION_PLAN.md) — T45 doc-ref drift (§7.1–§7.4 yazıyor ama kabul kriterleri yalnız §7.2–§7.4'ü sayıyor)
  - [Docs/IMPLEMENTATION_STATUS.md](../IMPLEMENTATION_STATUS.md) — F4 traceability matrix (§7.2 ✓)
- **Etkilenen task'lar:** T88 (Dashboard — bu task), dolaylı olarak F6 E2E happy-path (Dashboard işlem listesi gözlenemez)

## Bulgu Detayı

### Backend ↔ T88 plan kabul kriteri eşleşmesi

| Plan'da çağrılması istenen endpoint | Doc | Backend'de var mı? | Üretici task |
|---|---|---|---|
| `GET /api/v1/transactions` (T1, list) | 07 §7.1 | **✗ YOK** | — (orphan) |
| `GET /api/v1/users/me/stats` (U2) | 07 §5.2 | ✓ | T33 (`UsersController.cs:94`) |

### Kanıt

```
$ grep -nE "^\s+\[Http(Get|Post|Put|Delete|Patch)" backend/src/Skinora.API/Controllers/TransactionsController.cs
48:    [HttpGet("eligibility")]      ← T3 (07 §7.3)
60:    [HttpGet("params")]           ← T4 (07 §7.4)
70:    [HttpPost]                    ← T2 (07 §7.2)
125:   [HttpGet("{id:guid}")]        ← T5 (07 §7.5)
156:   [HttpPost("{id:guid}/accept")]    ← T6 (07 §7.6)
202:   [HttpPost("{id:guid}/cancel")]    ← T7 (07 §7.7)
262:   [HttpPost("{id:guid}/report-payout-issue")] ← T11 (07 §7.11)
```

→ `[HttpGet("")]` (`/transactions` list route) **yok**. `AdminController.cs:203` üzerindeki `GET /admin/users/{steamId}/transactions` yalnız admin endpoint; user-self list değil.

### F5 Readiness Sistematik Tarama (BLOCKED kapsamı için yan ürün)

T88 BLOCKED sebebi araştırılırken F5'in tüm 23 task'ı için backend endpoint envanteri çıkarıldı. Tek eksik kalem: **T1**. Diğer F5 endpoint'leri tam (✓).

| F5 Task | İstenen endpoint(ler) | Durum |
|---|---|---|
| T86 Landing | `/platform/stats`, `/platform/maintenance` | ✓ tam |
| T87 Auth | `/auth/*` (F1 üretti) | ✓ tam |
| **T88 Dashboard** | `/transactions` (T1), `/users/me/stats` (U2) | ✗ **T1 EKSİK** |
| T89 İşlem oluşturma | `/transactions/eligibility`, `/params`, `/steam/inventory`, POST `/transactions` | ✓ tam |
| T90 Detay | `GET /transactions/:id` | ✓ tam |
| T92 Dispute UI | `POST /transactions/:id/disputes`, `/submit-txhash`, `/escalate` | ✓ tam |
| T93 Profil | `/users/me`, `/users/:steamId`, wallet endpoint'leri | ✓ tam |
| T94 Hesap ayarları | `/users/me/settings` ailesi, deactivate, delete | ✓ tam |
| T95 Bildirimler | `/notifications`, `/unread-count`, `/mark-all-read`, `/{id}/read` | ✓ tam |
| T99 Admin Dashboard | `/admin/dashboard` | ✓ tam |
| T100 Admin Flag | `/admin/flags`, `/admin/flags/{id}`, `/approve`, `/reject` | ✓ tam |
| T101 Admin İşlem | `/admin/transactions`, `/admin/transactions/:id` | ✓ tam |
| T102 Admin Parametre | `/admin/settings`, `PUT /admin/settings/{key}` | ✓ tam |
| T103 Admin Steam hesapları | `/admin/steam-accounts` | ✓ tam |
| T104 Admin Roller | `/admin/roles` (GET/POST/PUT/DELETE) | ✓ tam |
| T105 Admin Kullanıcı detay | `/admin/users/{steamId}`, `/transactions` | ✓ tam |
| T106 Admin Audit log | `/admin/audit-logs` | ✓ tam |

**Sonuç:** F5'in 23 task'ı için backend tarafında **tek eksik** GET /api/v1/transactions. Bu endpoint eklendiğinde T88 unblock olur ve F5 sıra bozulmadan ilerleyebilir.

## Çözüm Önerileri

1. **Yeni backend task tanımla (Önerilen):** F4 sonuna küçük bir kurtarma task'ı eklenir (örn. `T83a — Kullanıcı işlem listesi endpoint'i (T1)`), yalnız `GET /api/v1/transactions` endpoint'ini implement eder. Plan §F4 traceability matrix güncellenir. Tahmini efor: 1 küçük dosya çifti (`ITransactionListService` + `TransactionListService` + controller method + integration test). T88 bu task PASS olduktan sonra sıraya dönsün.
2. **T45 retrospektif düzeltme:** T45 spec'i §7.1–§7.4 demesine rağmen yalnız §7.2–§7.4'ü implement etmiş. T45'in scope'una T1'i geri çekmek mümkün ama T45 zaten ✓ Tamamlandı + main'e merge — bu seçenek "tamamlanmış task'ı yeniden açma" olduğu için reddedilebilir.
3. **T88 plan kabul kriterini düşür (KABUL EDİLEMEZ):** "GET /transactions çağrısı" kriterini mock veriyle geçmek `feedback_check_external_assumptions.md` ve `feedback_think_through_fully.md` MEMORY kurallarını ihlal eder; F6 E2E'de ortaya çıkar.

## Proje Sahibi Kararı

- **Karar:** BLOCKED, Seçenek 1 yönünde — yeni backend task'ı tanımlanacak, ardından T88'e dönülecek
- **Tarih:** 2026-05-21
- **Tarafından:** Proje sahibi (AskUserQuestion onayı)

## Etki Analizi

- T88 unblock için: yeni backend task PASS + main'e merge → T88 dış varsayım yeniden doğrulanır → kod yazımı başlayabilir.
- F5 sıralaması bozulmuyor; T89/T90 bağımsız ilerleyebilir (paralel) ama kullanıcı kararı: önce T88 unblock edilsin.
- T87 ✓ PASS durumu etkilenmez.

## Branch & İlerleme

- Branch: `task/T88-dashboard` (açıldı, BLOCKED rapor + status için kullanılıyor)
- Working tree check: temiz ✓ (Adım -1)
- Main CI startup check: son 3 run ✓ success — [26186153921](https://github.com/turkerurganci/Skinora/actions/runs/26186153921), 26186153991, 26181175543 (Adım 0)
- Bağımlılık kontrolü: T84 ✓, T85 ✓
- Doküman okuma: 04 §7.1 + 07 §5.2 + 07 §7.1 tamam
- Dış varsayım doğrulama: T1 endpoint kırık (Adım 4)

## Notlar

- Bu rapor BLOCKED akışını (INSTRUCTIONS.md §3.5, §3.9) izler.
- Bitiş Kapısı 8 kapı'sı BLOCKED durumunda anlamlı değil (PR henüz açılmaz — yeni backend task PASS olunca T88 yapım resume edilir, ayrı PR T88 sonunda açılır).
- Repo memory ([.claude/memory/MEMORY.md](../../.claude/memory/MEMORY.md)) `Current Status` bloğuna BLOCKED satırı eklenecek.
- F5 readiness scan tek seferlik bonus iş — bir sonraki F5 yapım task'ı (T89+) için dış varsayım doğrulamayı kolaylaştırır.
