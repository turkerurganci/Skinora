using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T56_AddExchangeAddressesSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000025"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Çoklu hesap kontrolünde 'aynı gönderim adresi' destekleyici sinyalinden hariç tutulan bilinen exchange/custodial cüzdan adresleri (CSV). 'NONE' = hiç adres hariç değil. Adresler exact-match (case-sensitive) karşılaştırılır.", true, "multi_account.exchange_addresses", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000025"));
        }
    }
}
