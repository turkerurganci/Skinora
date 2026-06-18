using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP10_AddBlockchainTxEventIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_TxHash",
                table: "BlockchainTransactions");

            migrationBuilder.AddColumn<int>(
                name: "EventIndex",
                table: "BlockchainTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_TxHash_EventIndex",
                table: "BlockchainTransactions",
                columns: new[] { "TxHash", "EventIndex" },
                unique: true,
                filter: "[TxHash] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_EventIndex",
                table: "BlockchainTransactions",
                sql: "([EventIndex] IS NULL) OR ([EventIndex] >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_TxHash_EventIndex",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_EventIndex",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "EventIndex",
                table: "BlockchainTransactions");

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_TxHash",
                table: "BlockchainTransactions",
                column: "TxHash",
                unique: true,
                filter: "[TxHash] IS NOT NULL");
        }
    }
}
