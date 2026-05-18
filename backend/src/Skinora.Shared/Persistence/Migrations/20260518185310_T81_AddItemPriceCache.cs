using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T81_AddItemPriceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemPriceCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketHashName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MedianPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    LowestPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPriceCaches", x => x.Id);
                    table.CheckConstraint("CK_ItemPriceCaches_Source", "[Source] = 'STEAM_MARKET'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemPriceCaches_FetchedAt",
                table: "ItemPriceCaches",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "UQ_ItemPriceCaches_MarketHashName",
                table: "ItemPriceCaches",
                column: "MarketHashName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemPriceCaches");
        }
    }
}
