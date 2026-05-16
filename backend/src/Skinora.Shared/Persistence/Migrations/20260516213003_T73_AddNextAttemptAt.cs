using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T73_AddNextAttemptAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_DispatchScan",
                table: "BlockchainTransactions",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" },
                filter: "[Status] = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockchainTransactions_DispatchScan",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "BlockchainTransactions");
        }
    }
}
