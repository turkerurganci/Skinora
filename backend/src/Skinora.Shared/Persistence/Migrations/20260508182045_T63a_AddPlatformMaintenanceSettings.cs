using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T63a_AddPlatformMaintenanceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000026"), "Platform", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Platform/Steam/blockchain bakım veya kesinti aktif mi (07 §10.2). true iken type set edilmiş olmalı.", true, "platform.maintenance.active", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" },
                    { new Guid("0aa51010-0000-0000-0000-000000000027"), "Platform", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Bakım/kesinti tipi: PLANNED_MAINTENANCE | PLATFORM_MAINTENANCE | STEAM_OUTAGE | BLOCKCHAIN_DEGRADATION | NONE (NONE = aktif değil, 07 §10.2).", true, "platform.maintenance.type", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" },
                    { new Guid("0aa51010-0000-0000-0000-000000000028"), "Platform", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Kullanıcıya gösterilecek bilgilendirme mesajı. 'NONE' = mesaj yok (07 §10.2 maintenance banner).", true, "platform.maintenance.message", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" },
                    { new Guid("0aa51010-0000-0000-0000-000000000029"), "Platform", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Tahmini bitiş zamanı (ISO 8601 UTC, ör: '2026-03-16T18:00:00Z'). 'NONE' = bilinmiyor / aktif değil (07 §10.2).", true, "platform.maintenance.planned_end", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000026"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000027"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000028"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000029"));
        }
    }
}
