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
# Süper admin — ÖNCE Steam ile bir kez giriş yapın (Users satırı oluşsun).
# T171b — sa parolası konteynerin kendi secret dosyasından okunur ve sqlcmd'ye
# SQLCMDPASSWORD ile verilir: ne komut satırına ne host'un ekranına girer.
MSYS_NO_PATHCONV=1 docker exec -i skinora-db bash -c \
  'SQLCMDPASSWORD="$(cat /run/secrets/mssql_sa_password)" exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -d Skinora -v SteamId="$0" -i /dev/stdin' \
  "76561198000000000" < scripts/bootstrap/01-super-admin.sql
```

`-I` (QUOTED_IDENTIFIER ON) zorunludur: script filtreli indeksi olan tablolara yazar;
onsuz SQL Server `Msg 1934 … QUOTED_IDENTIFIER` ile durur ve işlem geri alınır
(hiçbir şey yazılmaz). Deneme yayınının ilk kurulumunda ölçüldü (2026-10-24).

PowerShell'de aynı komut `docker exec -i skinora-db bash -c '...' "<steamid64>"`
biçiminde, `Get-Content scripts/bootstrap/01-super-admin.sql | docker exec -i ...`
ile çalışır. Parola `.env`'de değildir (T171b — `secrets/mssql_sa_password.txt`).

Script **idempotent**'tir: tekrar çalıştırmak güvenlidir.

## Sonrası

- **Çıkış yapıp tekrar giriş yapın** — `super_admin` claim'i JWT'ye token üretimi
  anında işlenir, mevcut access token'da bulunmaz.

Tam kurulum sırası: [`Docs/DEPLOY_RUNBOOK.md` §G](../../Docs/DEPLOY_RUNBOOK.md).
