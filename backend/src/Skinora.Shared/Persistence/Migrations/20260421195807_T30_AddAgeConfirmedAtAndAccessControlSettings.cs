using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T30_AddAgeConfirmedAtAndAccessControlSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AgeConfirmedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000001d"), "AccessControl", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Geo-block — ISO-3166-1 alpha-2 ülke kodları CSV (örn: 'IR,KP,CU'); 'NONE' hiçbir ülke engellenmemiş demektir. Admin tarafından yönetilir.", true, "auth.banned_countries", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" },
                    { new Guid("0aa51010-0000-0000-0000-00000000001e"), "AccessControl", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Steam hesap minimum yaş eşiği (gün) — burner/fake hesap caydırıcı. Hesap yaşı bu değerden az ise giriş engellenir (02 §21.1, 03 §11a.2).", true, "auth.min_steam_account_age_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "AgeConfirmedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000001d"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000001e"));

            migrationBuilder.DropColumn(
                name: "AgeConfirmedAt",
                table: "Users");
        }
    }
}
