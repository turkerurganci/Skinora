using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP12_SeedTimeoutWarningRatio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000007"),
                columns: new[] { "IsConfigured", "Value" },
                values: new object[] { true, "0.75" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000007"),
                columns: new[] { "IsConfigured", "Value" },
                values: new object[] { false, null });
        }
    }
}
