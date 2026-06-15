using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP3_AddSweepConstraintAndIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_Sweep_TransactionId",
                table: "BlockchainTransactions",
                column: "TransactionId",
                unique: true,
                filter: "[Type] = 'SWEEP'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Type_Sweep",
                table: "BlockchainTransactions",
                sql: "(Type <> 'SWEEP') OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_Sweep_TransactionId",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Type_Sweep",
                table: "BlockchainTransactions");
        }
    }
}
