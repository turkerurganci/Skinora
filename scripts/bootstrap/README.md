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

# 2) Escrow bot — secrets/steam-bots.json içindeki hesabın SteamID64'ü
docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
  -v BotSteamId="76561198000000000" -v BotDisplayName="Skinora Escrow Bot 1" \
  -i /dev/stdin < scripts/bootstrap/02-register-bot.sql
```

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
