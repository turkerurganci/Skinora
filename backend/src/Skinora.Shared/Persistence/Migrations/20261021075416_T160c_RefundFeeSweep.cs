using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T160c_RefundFeeSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefundFeeSweepNextCheckAt",
                table: "PaymentAddresses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundFeeRecovered",
                table: "BlockchainTransactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_RefundFeeSweep_InFlight",
                table: "BlockchainTransactions",
                column: "TransactionId",
                unique: true,
                filter: "[Type] = 'REFUND_FEE_SWEEP' AND [Status] IN ('PENDING', 'DETECTED')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_RefundFeeRecovered",
                table: "BlockchainTransactions",
                sql: "(RefundFeeRecovered IS NULL) OR (Type IN ('SWEEP', 'REFUND_FEE_SWEEP') AND RefundFeeRecovered > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Type_RefundFeeSweep",
                table: "BlockchainTransactions",
                sql: "(Type <> 'REFUND_FEE_SWEEP') OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL AND RefundFeeRecovered IS NOT NULL AND RefundFeeRecovered = Amount)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_RefundFeeSweep_InFlight",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_RefundFeeRecovered",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Type_RefundFeeSweep",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "RefundFeeSweepNextCheckAt",
                table: "PaymentAddresses");

            migrationBuilder.DropColumn(
                name: "RefundFeeRecovered",
                table: "BlockchainTransactions");
        }
    }
}
