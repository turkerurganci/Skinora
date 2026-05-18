# Resend — Operational Runbook

> **Sahip:** Platform / DevOps. T78 ile ilk sürüm; her secret rotasyonu veya DNS değişikliği sonrası güncellenir.
>
> **Kapsam:** Production deploy öncesi gerekli DNS, secret ve doğrulama adımları. Kod tarafı [08 §4](../08_INTEGRATION_SPEC.md) ve [T78 raporu](../TASK_REPORTS/T78_REPORT.md) altında belgelenmiştir.

---

## 1. Resend Hesap Yapılandırması

1. **Domain ekle:** Resend dashboard → *Domains* → *Add domain* → `skinora.com` (veya operasyon ekibinin sahip olduğu domain).
2. **Region:** US (varsayılan) — domain doğrulaması bölge bağımsız.
3. **API Key oluştur:** *API Keys* → *Create API key* → Permission `Sending access` (sadece email gönderim, full access değil) → kopyala (sadece bir kez gösterilir).
4. **Webhook endpoint kaydet:** *Webhooks* → *Add endpoint* → URL `https://skinora.com/api/v1/webhooks/resend` → events: `email.bounced`, `email.delivery_delayed`, `email.complained`, `email.failed`, `email.suppressed` → kaydet → *Signing secret* göründüğünde kopyala (`whsec_…` formatında, sadece bir kez gösterilir).

---

## 2. DNS Kayıtları (DKIM / SPF / DMARC / Return-Path)

Resend domain ekledikten sonra dashboard'da dört kayıt üretir. Bunları DNS sağlayıcısında (Cloudflare / Route53 / Hetzner DNS) **birebir** ekleyin:

| Kayıt | Tür | Host | Değer (örnek) | Notlar |
|---|---|---|---|---|
| **DKIM 1** | TXT veya CNAME | `resend._domainkey.skinora.com` | `resend._domainkey.skinora.com.dkim.resend.com` (CNAME) veya `p=MIGfMA0GCSqGSIb3DQEBAQUA…` (TXT) | Resend hangi formatı verirse onu kullan; her ikisi de geçerli. |
| **DKIM 2** | TXT veya CNAME | `resend2._domainkey.skinora.com` | dashboard'daki ikinci kayıt | Yedek imzalama anahtarı; eksikse "1 of 2 records" uyarısı gelir. |
| **SPF** | TXT | `skinora.com` (root) | `v=spf1 include:amazonses.com ~all` | Mevcut SPF kaydı varsa `include:amazonses.com` parçasını **ekleyerek** güncelle; **yeni satır açma** (RFC 7208 — bir domain için en fazla bir SPF TXT). |
| **DMARC** | TXT | `_dmarc.skinora.com` | `v=DMARC1; p=quarantine; rua=mailto:dmarc-reports@skinora.com; adkim=s; aspf=s` | MVP'de `p=quarantine` yeterli; tam mainstream sonrası `p=reject`'e geçilir. `rua` mailbox'ı operasyon ekibinin izlediği bir adres olmalı. |
| **Return-Path** | CNAME | `bounce.skinora.com` (veya dashboard'un istediği subdomain) | `feedback-smtp.us-east-1.amazonses.com` | "Custom Return-Path" özelliği; bounce yönetimi için zorunlu. |

**Doğrulama:**
- Resend dashboard'da *Verify DNS* → tüm dört kayıt yeşil ✓ olana kadar tekrarla (DNS propagation 5 dk–1 sa arası).
- CLI ile manuel:
  ```bash
  dig +short txt resend._domainkey.skinora.com
  dig +short txt skinora.com
  dig +short txt _dmarc.skinora.com
  dig +short cname bounce.skinora.com
  ```
- DKIM/SPF/DMARC sağlamlığını dışarıdan: [https://www.mail-tester.com/](https://www.mail-tester.com/) → test mailbox'a sandbox gönderimi → skor 9/10+.

---

## 3. Secret Dağıtımı

| Secret | Where | Env var | Notes |
|---|---|---|---|
| Resend API key | Resend dashboard (§1.3) | `Resend__ApiKey` | Production: Docker Secrets / vault. Staging: `.env.staging`. Dev: stub mode (`Resend__Provider=logging`). |
| Webhook signing secret | Resend dashboard (§1.4) | `Resend__WebhookSigningSecret` | `whsec_…` formatı **birebir** korunur (prefix dahil). Verifier prefix'i `whsec_` ile başlamayan değerleri reddeder. |
| From address | Sabit konfigürasyon | `Resend__FromAddress` | `Skinora <noreply@skinora.com>` formatı. Verified domain'in mailbox'ı olmalı. |
| Provider switch | App config | `Resend__Provider` | `logging` (default, test/dev/CI) veya `resend` (staging/production). |

**Rotasyon prosedürü:**

1. Resend dashboard'da yeni API key veya webhook secret oluştur (eski **bırak**; iki anahtarın paralel çalıştığı bir geçiş penceresi olur).
2. Vault/Docker Secrets'a yeni değeri yaz.
3. `docker compose up -d backend` ile rolling restart.
4. 5 dakika boyunca `email.delivery_delayed` / `bounce` event'lerini dashboard'dan izle — herhangi bir 401 görürsen rollback (eski env değerine geri dön, sonra hata sebebini araştır).
5. Bir saat boyunca temizse Resend dashboard'dan eski anahtarı sil.

---

## 4. Lokal / CI / Production Davranışı

| Ortam | `Resend__Provider` | Notlar |
|---|---|---|
| **CI** | `logging` (default) | `ResendEmailClient` DI'ya hiç girmez; CI hiçbir koşulda Resend API'sini çağırmaz. Webhook endpoint hâlâ çalışır — Svix imzalı test event'leri lokal SQLite üzerinden tüketilir. |
| **Local dev** | `logging` (default) | `LoggingEmailSender` + `EmailNotificationChannelHandler` stub'ları çalışır. Real email göndermek istiyorsan `.env`'e `Resend__Provider=resend` + key bilgilerini yaz. |
| **Staging** | `resend` | Verified `skinora-staging.com` benzeri ayrı domain önerilir — production'a karışmayan suppression/complaint kayıtları için. |
| **Production** | `resend` | Tüm DNS yeşil, secret rotation prosedürü §3 uygulanmış olmalı. |

---

## 5. Sandbox / Smoke Test

Resend'in özel test mailbox'ları sandbox event'lerini tetikler. Production-like akış doğrulaması için staging'de çalıştır (CI'da değil — gerçek API çağrısı):

| Test recipient | Beklenen sonuç | Kullanım |
|---|---|---|
| `delivered@resend.dev` | 200 → `email.delivered` webhook'u | Happy path duman testi. Backend tarafı şu an `delivered` event'ini dinlemiyor (08 §4.3 listesinde yok); sadece dashboard'da görünür. |
| `bounced@resend.dev` | 200 → `email.bounced` webhook'u | EMAIL preference disable akışını doğrular. Test user için `UserNotificationPreference.IsEnabled=false` olmalı. |
| `complained@resend.dev` | 200 → `email.complained` webhook'u | Spam complaint akışı — preference disable + admin alert. |

**Manuel akış:**
```bash
# Staging deploy edildi, Resend dashboard'da webhook ✓ olarak görünüyor
curl -X POST https://api.resend.com/emails \
  -H "Authorization: Bearer $RESEND_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "from": "Skinora <noreply@skinora-staging.com>",
    "to": ["bounced@resend.dev"],
    "subject": "T78 sandbox bounce",
    "html": "<p>test</p>"
  }'
# → 200 OK + {"id":"..."}
# 30 sn içinde Resend dashboard "email.bounced" event'i fire eder
# → Skinora backend webhook handler'ı preference disable eder
# → DB'de test user'ın EMAIL preference'ı IsEnabled=false olmalı
```

---

## 6. İzleme

| Sinyal | Nerede | Aksiyon eşiği |
|---|---|---|
| `email.bounced` artışı | Resend dashboard + Grafana (T16 application-metrics) | Saatlik %1 üstü → DNS / list hygiene kontrol |
| `email.complained` artışı | Resend dashboard | Tek bir kullanıcıdan 2+ → manuel inceleme; toplam %0.1 üstü → şablon revizyonu |
| `email.delivery_delayed` artışı | Backend logu (`Resend webhook (email.delivery_delayed)`) | 5 dk içinde 10+ → SMTP provider durumu |
| `Resend permanent failure (4xx)` | Backend logu (`ResendEmailClient permanent failure`) | Tek seferlik tolere; tekrarlayan 401 → API key invalid (rotasyon kontrol) |
| `NotificationDelivery DEFERRED birikmesi` | DB (`SELECT COUNT(*) FROM NotificationDeliveries WHERE Status='DEFERRED'`) | 100+ → deferred tier job'larının runner'da koştuğunu doğrula (Hangfire dashboard) |

---

## 7. Yaygın Hatalar

| Hata | Sebep | Çözüm |
|---|---|---|
| 401 `WEBHOOK_SIGNATURE_INVALID` | Dashboard'daki signing secret ile env mismatch | §3 secret rotasyon prosedürü; yeni secret env'e yaz, restart. |
| 401 `WEBHOOK_TIMESTAMP_OUT_OF_WINDOW` | Server saati 5 dk'dan fazla drift | NTP servisi (chronyd / systemd-timesyncd) çalışıyor mu? |
| 401 `WEBHOOK_HEADERS_MISSING` | Resend dashboard'da event eksik veya bir proxy header'ları temizliyor | Reverse proxy (nginx) konfig kontrol — `proxy_pass_request_headers on` |
| `Resend HTTP 422 validation_error` | Geçersiz `from` (verified domain dışı) veya boş `to`/`subject` | `Resend__FromAddress` doğru mu? Resend dashboard'da domain hala verified mi? |
| `Resend HTTP 401 unauthorized` | API key revoke edildi veya yanlış kopyalandı | Yeni key oluştur, §3 rotasyon. |
| `NotificationDelivery DEFERRED kalıyor, retry'lanmıyor` | Hangfire worker kapalı veya tier 2/3 job'ları schedule edilmemiş | Hangfire dashboard → Scheduled → `DeferredNotificationDeliveryJob.Execute`. Yoksa scheduler bir restart sırasında düşmüş, root cause investigate. |

---

## 8. Bağlı Dokümantasyon

- [08_INTEGRATION_SPEC.md §4](../08_INTEGRATION_SPEC.md) — Resend kontratı (API, error matrix, webhook event listesi).
- [05_TECHNICAL_ARCHITECTURE.md §7](../05_TECHNICAL_ARCHITECTURE.md) — Bildirim altyapısı genel.
- [T78_REPORT.md](../TASK_REPORTS/T78_REPORT.md) — T78 implementasyon detayları + kabul kriterleri.
