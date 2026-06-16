using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP4a_SeedPriceDeviationThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000012"),
                columns: new[] { "Description", "IsConfigured", "Value" },
                values: new object[] { "Piyasa fiyat sapma eşiği (oran; 1.0 = %100). |girilen−piyasa|/piyasa bu oranı aşarsa işlem FLAGGED. 08 §7.3 tek-kaynak varyansı için geniş tutulmasını önerir; >0 olmalı (open-(0,1) ratio değil).", true, "1.0" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000012"),
                columns: new[] { "Description", "IsConfigured", "Value" },
                values: new object[] { "Piyasa fiyat sapma eşiği", false, null });
        }
    }
}
