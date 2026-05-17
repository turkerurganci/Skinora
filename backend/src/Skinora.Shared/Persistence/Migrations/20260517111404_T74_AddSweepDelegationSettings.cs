using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T74_AddSweepDelegationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000034"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Sweep / deposit-sourced refund öncesi sweeper hot wallet'tan deposit adresine geçici Energy delegation tutarı (SUN, 1 TRX = 1_000_000 SUN). Default 200 TRX — Stake 2.0 ile ~16.000 Energy headroom (TRC-20 transfer ~65k Energy, dış API oran dalgalanması payı dahil). 08 §3.3.", true, "blockchain.sweep_energy_delegation_sun", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "200000000" },
                    { new Guid("0aa51010-0000-0000-0000-000000000035"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Energy delegation başarısız olursa deposit adresine fallback olarak gönderilen TRX tutarı (SUN). Default 15 TRX (08 §3.3 — TRC-20 transferin gas için yaklaşık üst sınırı). Deposit bu TRX'i kendi gas'ı için yakar.", true, "blockchain.sweep_trx_fallback_sun", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "15000000" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000035"));
        }
    }
}
