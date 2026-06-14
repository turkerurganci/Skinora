using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP1_AddSellerPayoutUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_SellerPayout_TransactionId",
                table: "BlockchainTransactions",
                column: "TransactionId",
                unique: true,
                filter: "[Type] = 'SELLER_PAYOUT'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_SellerPayout_TransactionId",
                table: "BlockchainTransactions");
        }
    }
}
