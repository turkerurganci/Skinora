# `scripts/bootstrap/` — ilk kurulum SQL'leri

Migration'lar şemayı kurar ve `SystemSetting` kataloğunu seed'ler, ama **iki şey
kasıtlı olarak seed'lenmez** — ikisi de kuruluma özgüdür ve kod içinde bir
varsayılanı olamaz:

| # | Ne | Neden seed yok | Script |
|---|---|---|---|
| 1 | Süper admin rolü + atama | `AdminRoles`/`AdminUserRoles` için `HasData` yok; ilk admin kimliği kuruluma özgü | [`01-super-admin.sql`](01-super-admin.sql) |
| 2 | Escrow bot havuz kaydı | `PlatformSteamBots` için admin endpoint'i yok (AD10/T63 salt-okunur) | [`02-register-bot.sql`](02-register-bot.sql) |

Bu dosyalarda **sır yoktur** — SteamID64 ve görünen ad `sqlcmd -v` parametresiyle
dışarıdan verilir, bu yüzden repo'da izlenirler.

## Çalıştırma

Container içindeki `sqlcmd` ile (ekstra araç kurmadan):

```bash
# 1) Süper admin — ÖNCE Steam ile bir kez giriş yapın (Users satırı oluşsun)
docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
  -v SteamId="76561198000000000" \
  -i /dev/stdin < scripts/bootstrap/01-super-admin.sql

# 2) Escrow bot — BotDisplayName, steam-bots.json'daki accountName ile AYNI olmalı
docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
  -v BotSteamId="76561198000000000" -v BotDisplayName="skinora_bot_01" \
  -i /dev/stdin < scripts/bootstrap/02-register-bot.sql
```

> **⚠ `DisplayName` serbest metin değildir.** `SteamWebhookHandler` botu
> `WHERE DisplayName == data.AccountName` ile arar — yani bu alan
> `secrets/steam-bots.json` içindeki `accountName` ile **birebir aynı** olmalı.
> "Skinora Escrow Bot 1" gibi insan-dostu bir ad verirseniz bot lifecycle
> event'leri satırı bulamaz ve **sessizce atlanır**: bot `ACTIVE` kalır, escrow
> için seçilmeye devam eder, admin uyarılmaz.

PowerShell'de `$MSSQL_SA_PASSWORD` yerine `.env`'deki değeri doğrudan geçin veya
`$env:MSSQL_SA_PASSWORD` kullanın.

Her iki script de **idempotent**'tir: tekrar çalıştırmak güvenlidir. `02` mevcut
bir botun `ActiveEscrowCount` / `DailyTradeOfferCount` sayaçlarını **sıfırlamaz**
(canlı emanet sayısını sıfırlamak bot seçimini bozar), yalnız adı tazeler ve
durumu `ACTIVE`'e geri alır.

## Sonrası

- `01` sonrası **çıkış yapıp tekrar giriş yapın** — `super_admin` claim'i JWT'ye
  token üretimi anında işlenir, mevcut access token'da bulunmaz.
- `02` sonrası bot'un `secrets/steam-bots.json` içindeki hesapla **aynı SteamID64**
  olduğunu doğrulayın; ikisi ayrışırsa backend bot seçer ama sidecar oturumu
  bulamaz.

Tam kurulum sırası: [`Docs/DEPLOY_RUNBOOK.md` §G](../../Docs/DEPLOY_RUNBOOK.md).
