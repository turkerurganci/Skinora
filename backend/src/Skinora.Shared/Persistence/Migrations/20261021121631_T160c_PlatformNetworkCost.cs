using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T160c_PlatformNetworkCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PlatformNetworkCost",
                table: "BlockchainTransactions",
                type: "decimal(38,0)",
                precision: 38,
                scale: 0,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformNetworkCostUsdt",
                table: "BlockchainTransactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlatformNetworkCost",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "PlatformNetworkCostUsdt",
                table: "BlockchainTransactions");
        }
    }
}
