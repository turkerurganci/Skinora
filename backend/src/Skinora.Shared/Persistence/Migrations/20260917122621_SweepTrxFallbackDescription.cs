using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SweepTrxFallbackDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000035"),
                column: "Description",
                value: "Depozit kaynak planı hiç hesaplanamazsa (zincir probu arızası) depozite gönderilen sabit TRX tutarı (SUN). Default 15 TRX = en pahalı TRC-20 transfer 13,03 TRX + bandı 0,35 TRX (08 §3.3). Kilidin yetmediği transfer bu ayarı kullanmaz; yakacağı TRX'i transfer başına hesaplar.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000035"),
                column: "Description",
                value: "Energy delegation başarısız olursa deposit adresine fallback olarak gönderilen TRX tutarı (SUN). Default 15 TRX (08 §3.3 — TRC-20 transferin gas için yaklaşık üst sınırı). Deposit bu TRX'i kendi gas'ı için yakar.");
        }
    }
}
