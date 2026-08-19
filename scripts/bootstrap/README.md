# `scripts/bootstrap/` — ilk kurulum SQL'leri

Migration'lar şemayı kurar ve `SystemSetting` kataloğunu seed'ler, ama **bir şey
kasıtlı olarak seed'lenmez** — kuruluma özgüdür ve kod içinde bir varsayılanı
olamaz:

| # | Ne | Neden seed yok | Script |
|---|---|---|---|
| 1 | Süper admin rolü + atama | `AdminRoles`/`AdminUserRoles` için `HasData` yok; ilk admin kimliği kuruluma özgü | [`01-super-admin.sql`](01-super-admin.sql) |

Bu dosyada **sır yoktur** — SteamID64 `sqlcmd -v` parametresiyle dışarıdan
verilir, bu yüzden repo'da izlenir.

> **`02-register-bot.sql` T133'te silindi.** Yazdığı `PlatformSteamBots` tablosu
> T117'de düşürüldüğü için script zaten çalışmıyordu; platform hiçbir Steam bot'u
> çalıştırmıyor (02 §2.1, 05 §3.2), dolayısıyla açılacak bir havuz kaydı da yok.

## Çalıştırma

Container içindeki `sqlcmd` ile (ekstra araç kurmadan):

```bash
# Süper admin — ÖNCE Steam ile bir kez giriş yapın (Users satırı oluşsun)
docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
  -v SteamId="76561198000000000" \
  -i /dev/stdin < scripts/bootstrap/01-super-admin.sql
```

PowerShell'de `$MSSQL_SA_PASSWORD` yerine `.env`'deki değeri doğrudan geçin veya
`$env:MSSQL_SA_PASSWORD` kullanın.

Script **idempotent**'tir: tekrar çalıştırmak güvenlidir.

## Sonrası

- **Çıkış yapıp tekrar giriş yapın** — `super_admin` claim'i JWT'ye token üretimi
  anında işlenir, mevcut access token'da bulunmaz.

Tam kurulum sırası: [`Docs/DEPLOY_RUNBOOK.md` §G](../../Docs/DEPLOY_RUNBOOK.md).
