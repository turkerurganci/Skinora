using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <summary>
    /// T147 — network choice in the transaction flow (06 §1.4, §3.5, §3.5a;
    /// 11 §F8 K3). Adds <c>TransactionNetworkOptions</c> (the networks a listing
    /// accepts, each with the seller's payout address fixed at creation),
    /// opens <c>Transactions.SellerPayoutAddress</c> to NULL (it is copied from
    /// the chosen row at acceptance) and adds <c>CK_Transactions_Network_Accepted</c>
    /// (an accepted row has its network and payout address — T145 decision v).
    /// Every existing transaction gets one option row on its own network
    /// (every pre-T147 transaction is a TRON transaction) carrying the payout
    /// address it already has, so an open pre-T147 listing stays acceptable on
    /// TRON exactly as before.
    /// </summary>
    /// <inheritdoc />
    public partial class T147_TransactionNetworkOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SellerPayoutAddress",
                table: "Transactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateTable(
                name: "TransactionNetworkOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Network = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SellerPayoutAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionNetworkOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionNetworkOptions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            // Existing transactions → one option row each, on the network the
            // row already has (TRON wherever it is set — T145) or TRON when
            // it was never accepted. Soft-deleted rows included: the snapshot
            // is history, not state. The payout address is already canonical
            // (T145). A blank value is not an address and gets no row.
            migrationBuilder.Sql(
                "INSERT INTO [TransactionNetworkOptions] ([Id], [TransactionId], [Network], [SellerPayoutAddress], [CreatedAt]) " +
                "SELECT NEWID(), [Id], COALESCE([Network], N'TRON'), [SellerPayoutAddress], [CreatedAt] " +
                "FROM [Transactions] " +
                "WHERE [SellerPayoutAddress] IS NOT NULL AND LTRIM(RTRIM([SellerPayoutAddress])) <> N'';");

            // T145 already set the network of every accepted row; this only
            // guards the CHECK below against a row that slipped past it.
            migrationBuilder.Sql(
                "UPDATE [Transactions] SET [Network] = N'TRON' " +
                "WHERE [AcceptedAt] IS NOT NULL AND [Network] IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Network_Accepted",
                table: "Transactions",
                sql: "(AcceptedAt IS NULL) OR (Network IS NOT NULL AND SellerPayoutAddress IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UQ_TransactionNetworkOptions_TransactionId_Network",
                table: "TransactionNetworkOptions",
                columns: new[] { "TransactionId", "Network" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-T147 schema is TRON-only: one payout address per
            // transaction, no record of other accepted networks. A listing or a
            // transaction on another network has nowhere to go — refuse rather
            // than drop it.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [TransactionNetworkOptions] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [Transactions] WHERE [Network] IS NOT NULL AND [Network] <> N'TRON') " +
                "THROW 50147, 'T147_TransactionNetworkOptions Down: non-TRON listings or transactions exist; the pre-F8 schema cannot hold them.', 1;");

            // A listing created after T147 and not yet accepted has its payout
            // address only in its TRON option row; put it back in the column.
            migrationBuilder.Sql(
                "UPDATE t SET t.[SellerPayoutAddress] = o.[SellerPayoutAddress] " +
                "FROM [Transactions] t JOIN [TransactionNetworkOptions] o ON o.[TransactionId] = t.[Id] " +
                "WHERE o.[Network] = N'TRON' AND t.[SellerPayoutAddress] IS NULL;");

            migrationBuilder.DropTable(
                name: "TransactionNetworkOptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Network_Accepted",
                table: "Transactions");

            migrationBuilder.AlterColumn<string>(
                name: "SellerPayoutAddress",
                table: "Transactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
