using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP2_AddBuyerRefundUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_BuyerRefund_TransactionId",
                table: "BlockchainTransactions",
                column: "TransactionId",
                unique: true,
                filter: "[Type] = 'BUYER_REFUND'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_BuyerRefund_TransactionId",
                table: "BlockchainTransactions");
        }
    }
}
