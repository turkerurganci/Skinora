# Telegram Bot Kurulum Runbook (T79 — 08 §5.1–§5.5)

Bu runbook Telegram bildirim kanalını sıfırdan ayağa kaldırmak için
gerekli operasyon adımlarını tanımlar. Hem ilk kurulum (production
deploy) hem de secret rotation senaryolarını kapsar.

## 1. Bot Oluşturma (BotFather)

1. Telegram istemcisinde [`@BotFather`](https://t.me/BotFather) ile sohbet aç.
2. `/newbot` komutu → bot için bir isim (örn. `Skinora Notifications`)
   ve `@SkinoraBot` formatında bir username belirle.
3. BotFather şu cevabı verir:

   ```
   Done! Congratulations on your new bot. ...
   Use this token to access the HTTP API:
   123456789:ABCdefGhIJKlmNoPQRsTUVwxYZ
   ```

   Token formatı: `<bot_id>:<random_token>`. Token, `Bearer` prefix'i
   olmadan `https://api.telegram.org/bot{token}/{method}` URL'sinin
   ayrılmaz parçasıdır — sızdığı an bot kaçırılmış sayılır.

4. (Opsiyonel) `/setdescription`, `/setabouttext`, `/setuserpic`
   komutları ile bot profili düzenlenir.
5. `/setjoingroups Disable` — Skinora MVP'sinde bot yalnızca DM
   kullanır, sunucu/grup kullanımı kapatılır.

## 2. Webhook Secret Üretimi

`X-Telegram-Bot-Api-Secret-Token` header'ı için tahmin edilemez bir
secret üret (256 karaktere kadar A-Z a-z 0-9 _ -):

```bash
openssl rand -base64 48 | tr -d '+/=' | head -c 64
```

Çıktı production secret'tır — Docker Secrets / vault'a yaz.

## 3. Konfigürasyon

Backend `appsettings.json`'da varsayılan değerler:

```json
"Telegram": {
  "Provider": "logging",
  "BotToken": "REPLACE_IN_ENV",
  "BotUsername": "SkinoraBot",
  "BotUrl": "https://t.me/SkinoraBot",
  "BaseUrl": "https://api.telegram.org",
  "TimeoutSeconds": 10,
  "WebhookSecretToken": "REPLACE_IN_ENV",
  "CodeTtlSeconds": 600,
  "MaxFailedAttempts": 5,
  "IdempotencyTtlHours": 24,
  "PerChatRatePerSecond": 1,
  "GlobalRatePerSecond": 30
}
```

Production ortamı için env override:

| Değişken | Değer |
|----------|-------|
| `Telegram__Provider` | `telegram` |
| `Telegram__BotToken` | BotFather token'ı |
| `Telegram__BotUsername` | `SkinoraBot` |
| `Telegram__WebhookSecretToken` | §2'de üretilen secret |

`Provider=logging` (default) iken backend Telegram API'sına HTTP
çağrısı yapmaz — `LoggingTelegramNotificationChannelHandler` stub'ı
mesajları log'a basıp success döner. Bu sayede yanlış yapılandırılmış
bir ortam asla canlı bota mesaj göndermez (fail-closed).

## 4. setWebhook Çağrısı

Konfigürasyon ayağa kalktıktan sonra **tek seferlik** `setWebhook`
çağrısı yapılır. Telegram dokümante limitleri:

| Parametre | Skinora değeri | Açıklama |
|-----------|----------------|----------|
| `url` | `https://skinora.com/api/v1/webhooks/telegram` | Public, HTTPS zorunlu |
| `secret_token` | §2 secret | Her gelen update'te header doğrulanır |
| `max_connections` | `40` (varsayılan) | MVP trafiği için yeterli |
| `allowed_updates` | `["message"]` | Yalnızca mesaj update'leri (gereksizler filtreli) |
| `drop_pending_updates` | `true` (ilk kurulumda) | Eski test update'leri atılır |

CLI ile manuel çağrı:

```bash
curl -sS -X POST "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/setWebhook" \
  -H "Content-Type: application/json" \
  -d "$(cat <<EOF
{
  "url": "https://skinora.com/api/v1/webhooks/telegram",
  "secret_token": "${TELEGRAM_WEBHOOK_SECRET}",
  "max_connections": 40,
  "allowed_updates": ["message"],
  "drop_pending_updates": true
}
EOF
)"
```

Beklenen cevap:

```json
{"ok":true,"result":true,"description":"Webhook was set"}
```

Backend `ITelegramBotClient.SetWebhookAsync` aynı çağrıyı yapar; CLI
yerine `dotnet run --project tools/Skinora.TelegramSetup`-tipi bir
init job olarak deploy pipeline'ına bağlanabilir (T-future).

## 5. Doğrulama

1. **Webhook bilgisi kontrol:**

   ```bash
   curl -sS "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/getWebhookInfo"
   ```

   `url`, `pending_update_count: 0`, `last_error_date: 0` beklenir.

2. **Bağlantı testi:** Skinora UI'da test kullanıcısıyla
   `Telegram Bildirimlerini Aç`. Üretilen deep link
   (`https://t.me/SkinoraBot?start=SKN-...`) Telegram'da açılır, `/start`
   tıklanır. Skinora backend `UserNotificationPreference` satırını
   `IsEnabled=true` + `VerifiedAt` ile günceller.

3. **Smoke message:** Admin dashboard'dan kullanıcıya örnek bir
   notification gönder, Telegram'da mesaj alındığı doğrulanır. Mesaj
   `*Başlık*\n\nGövde` formatında ve MarkdownV2 escaping ile gelmelidir.

## 6. Secret Rotation

| Senaryo | Adım |
|---------|------|
| Webhook secret sızdı | (a) Yeni secret üret (§2). (b) `Telegram__WebhookSecretToken` env güncelle, redeploy. (c) `setWebhook` çağrısı yeni secret ile tekrarlanır (§4) — eski secret'a sahip Telegram retry'ları 401 ile reddedilir. |
| Bot token sızdı | (a) BotFather'da `/revoke` komutu → yeni token. (b) `Telegram__BotToken` env güncelle, redeploy. (c) `setWebhook` yeni token ile tekrar — Telegram chat_id'leri korunduğu için kullanıcı bağlantıları etkilenmez. |
| Bot kapanır / re-deploy | `/setwebhook` her zaman idempotent. Kurulum sırasında tekrar çağrılırsa Telegram aynı parametrelerle aynı endpoint'i yeniden kaydeder. |

## 7. İzleme ve Limitler

| Konu | Değer / Eşik |
|------|--------------|
| Per-chat send rate | 1 msg/s — `TelegramRateLimiter` semaphore ile sıralı kuyruk |
| Global send rate | 30 msg/s — sliding-window enforcer |
| 429 retry | Telegram'ın `retry_after` değerini bekle (rate limiter chat-gate'i o süre kadar park'lar) |
| Webhook idempotency | `update_id` 24 saat boyunca dedup edilir (`ProcessedNonces` tablosu, `Source="telegram"`) |
| Deferred-tier escalation | Immediate 3 retry sonrası `NotificationDelivery.Status = DEFERRED` → 30dk/1sa/4sa deferred-tier (08 §4.3 mirror) |

## 8. Sandbox ve Lokal Geliştirme

| Ortam | Provider | Notes |
|-------|----------|-------|
| Lokal (developer) | `logging` | Mesajlar console'a basılır, Telegram API'sına çıkmaz |
| CI | `logging` | `appsettings.json` default — entegrasyon testleri `LoggingTelegramNotificationChannelHandler` stub'ı kullanır |
| Staging | `telegram` (test botu) | Ayrı bir bot (`@SkinoraStagingBot`), staging webhook URL'si (`https://staging.skinora.com/...`) |
| Production | `telegram` | Ana bot (`@SkinoraBot`) |

> **Önemli:** `setWebhook` bir bot için tek webhook URL'sini destekler.
> Aynı bot'u hem prod hem staging için kullanmak Telegram tarafında
> sürekli URL çakışmasına yol açar — staging'e ayrı bot tahsis edin.

## 9. Yaygın Hatalar

| Hata | Sebep | Aksiyon |
|------|-------|---------|
| `401 Unauthorized` (webhook'a gelen) | `secret_token` mismatch | `Telegram__WebhookSecretToken` ile BotFather setWebhook payload'u eşleşmiyor — §2 + §4 tekrar |
| `400 Bad Request: chat not found` | Kullanıcı botu sildi / chat_id eskimiş | `TelegramNotificationChannelHandler` preference auto-disable → kullanıcıya yeniden bağlantı yönlendirmesi |
| `403 Forbidden: bot was blocked by the user` | Kullanıcı botu engelledi | Auto-disable + platform-içi bildirim (`UserNotificationPreference.IsEnabled=false`) |
| `429 Too Many Requests` | Per-chat veya global limit aşıldı | Rate limiter retry-after'ı uygulayarak chat'i park'lar; immediate-tier retry başarısız olursa deferred-tier devralır |
| `409 Conflict: terminated by other getUpdates request` | Polling + webhook çakışması | MVP yalnızca webhook kullanır — eğer test sırasında `getUpdates` denenirse webhook'u geçici olarak silmek gerekir (`deleteWebhook`) |
