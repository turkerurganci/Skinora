using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <summary>
    /// T145 — network dimension (06 §1.4, §2.25; 11 §F8). One forward
    /// migration:
    /// <list type="bullet">
    ///   <item><description><c>Network</c> on <c>PaymentAddresses</c>,
    ///   <c>BlockchainTransactions</c>, <c>ColdWalletTransfers</c>,
    ///   <c>SellerPayoutIssues</c>, <c>WalletAddressHistory</c> (NOT NULL) and
    ///   <c>Transactions</c> (NULL until acceptance). Every existing row is a
    ///   TRON row and is backfilled to <c>'TRON'</c>; a transaction gets its
    ///   network only if it was already accepted (or already had a deposit
    ///   address) — an unaccepted one stays NULL, as the target model says.</description></item>
    ///   <item><description>Uniqueness keys lead with the network:
    ///   <c>(Network, Address)</c>, <c>(Network, HdWalletIndex)</c>,
    ///   <c>(Network, TxHash, EventIndex)</c>, <c>(Network, TxHash)</c>,
    ///   sanctions <c>(Network, Address) WHERE IsActive = 1</c>.</description></item>
    ///   <item><description><c>SanctionedAddresses.Network</c> moves from the
    ///   single pre-F8 value <c>'TRC-20'</c> to the enum names; the CHECK
    ///   accepts <c>'TRON'</c> and <c>'ETHEREUM'</c>.</description></item>
    ///   <item><description>The 20-block CONFIRMED / PENDING CHECKs become
    ///   TRON-only (Ethereum confirms on <c>finalized</c>, 08 §3.7.4).</description></item>
    /// </list>
    /// New NOT NULL columns are added nullable, backfilled, then tightened, so
    /// no DEFAULT constraint is left behind: a row inserted without a network
    /// fails instead of silently becoming TRON. Money columns are untouched
    /// (decimal(18,6) — Ethereum USDT has 6 decimals too).
    /// </summary>
    public partial class T145_NetworkDimension : Migration
    {
        private static readonly string[] NotNullNetworkTables =
        [
            "WalletAddressHistory",
            "SellerPayoutIssues",
            "PaymentAddresses",
            "ColdWalletTransfers",
            "BlockchainTransactions",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletAddressHistory_UserId_Type",
                table: "WalletAddressHistory");

            migrationBuilder.DropIndex(
                name: "UQ_SanctionedAddresses_Address_Active",
                table: "SanctionedAddresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SanctionedAddresses_Network",
                table: "SanctionedAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_PaymentAddresses_Address",
                table: "PaymentAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_PaymentAddresses_HdWalletIndex",
                table: "PaymentAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_ColdWalletTransfers_TxHash",
                table: "ColdWalletTransfers");

            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_TxHash_EventIndex",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Confirmed",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Pending",
                table: "BlockchainTransactions");

            // --- Network columns: add nullable → backfill TRON → NOT NULL ---
            foreach (var table in NotNullNetworkTables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Network",
                    table: table,
                    type: "nvarchar(20)",
                    maxLength: 20,
                    nullable: true);

                migrationBuilder.Sql($"UPDATE [{table}] SET [Network] = N'TRON';");

                migrationBuilder.AlterColumn<string>(
                    name: "Network",
                    table: table,
                    type: "nvarchar(20)",
                    maxLength: 20,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(20)",
                    oldMaxLength: 20,
                    oldNullable: true);
            }

            // Transactions.Network stays NULL until the buyer accepts (06 §1.4).
            // An existing row that was accepted — or already had a deposit
            // address allocated — is a TRON transaction.
            migrationBuilder.AddColumn<string>(
                name: "Network",
                table: "Transactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [t] SET [Network] = N'TRON' FROM [Transactions] AS [t] " +
                "WHERE [t].[AcceptedAt] IS NOT NULL OR [t].[BuyerId] IS NOT NULL " +
                "OR EXISTS (SELECT 1 FROM [PaymentAddresses] AS [p] WHERE [p].[TransactionId] = [t].[Id]);");

            // Sanctions list: the CHECK was dropped above; move the pre-F8 value
            // to the enum name before the widened CHECK is added back.
            migrationBuilder.Sql(
                "UPDATE [SanctionedAddresses] SET [Network] = N'TRON' WHERE [Network] = N'TRC-20';");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddressHistory_UserId_Network_Type",
                table: "WalletAddressHistory",
                columns: new[] { "UserId", "Network", "Type" });

            migrationBuilder.CreateIndex(
                name: "UQ_SanctionedAddresses_Network_Address_Active",
                table: "SanctionedAddresses",
                columns: new[] { "Network", "Address" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SanctionedAddresses_Network",
                table: "SanctionedAddresses",
                sql: "[Network] IN ('TRON', 'ETHEREUM')");

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_Network_Address",
                table: "PaymentAddresses",
                columns: new[] { "Network", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_Network_HdWalletIndex",
                table: "PaymentAddresses",
                columns: new[] { "Network", "HdWalletIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ColdWalletTransfers_Network_TxHash",
                table: "ColdWalletTransfers",
                columns: new[] { "Network", "TxHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_Network_TxHash_EventIndex",
                table: "BlockchainTransactions",
                columns: new[] { "Network", "TxHash", "EventIndex" },
                unique: true,
                filter: "[TxHash] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Confirmed",
                table: "BlockchainTransactions",
                sql: "(Status <> 'CONFIRMED') OR (ConfirmedAt IS NOT NULL AND (Network <> 'TRON' OR ConfirmationCount >= 20))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Pending",
                table: "BlockchainTransactions",
                sql: "(Status <> 'PENDING') OR (Network <> 'TRON' OR ConfirmationCount < 20)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-T145 schema cannot represent a non-TRON row: the old
            // keys would let an Ethereum address/index/hash collide with a TRON
            // one and the old sanctions CHECK accepts only 'TRC-20'. Refuse
            // rather than drop data.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [PaymentAddresses] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [BlockchainTransactions] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [ColdWalletTransfers] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [SellerPayoutIssues] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [WalletAddressHistory] WHERE [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [Transactions] WHERE [Network] IS NOT NULL AND [Network] <> N'TRON') " +
                "OR EXISTS (SELECT 1 FROM [SanctionedAddresses] WHERE [Network] <> N'TRON') " +
                "THROW 50145, 'T145_NetworkDimension Down: non-TRON rows exist; the pre-F8 schema cannot hold them.', 1;");

            migrationBuilder.DropIndex(
                name: "IX_WalletAddressHistory_UserId_Network_Type",
                table: "WalletAddressHistory");

            migrationBuilder.DropIndex(
                name: "UQ_SanctionedAddresses_Network_Address_Active",
                table: "SanctionedAddresses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SanctionedAddresses_Network",
                table: "SanctionedAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_PaymentAddresses_Network_Address",
                table: "PaymentAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_PaymentAddresses_Network_HdWalletIndex",
                table: "PaymentAddresses");

            migrationBuilder.DropIndex(
                name: "UQ_ColdWalletTransfers_Network_TxHash",
                table: "ColdWalletTransfers");

            migrationBuilder.DropIndex(
                name: "UQ_BlockchainTransactions_Network_TxHash_EventIndex",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Confirmed",
                table: "BlockchainTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Pending",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "Network",
                table: "Transactions");

            foreach (var table in NotNullNetworkTables)
            {
                migrationBuilder.DropColumn(
                    name: "Network",
                    table: table);
            }

            migrationBuilder.Sql(
                "UPDATE [SanctionedAddresses] SET [Network] = N'TRC-20' WHERE [Network] = N'TRON';");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddressHistory_UserId_Type",
                table: "WalletAddressHistory",
                columns: new[] { "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "UQ_SanctionedAddresses_Address_Active",
                table: "SanctionedAddresses",
                column: "Address",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SanctionedAddresses_Network",
                table: "SanctionedAddresses",
                sql: "[Network] IN ('TRC-20')");

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_Address",
                table: "PaymentAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_HdWalletIndex",
                table: "PaymentAddresses",
                column: "HdWalletIndex",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ColdWalletTransfers_TxHash",
                table: "ColdWalletTransfers",
                column: "TxHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_TxHash_EventIndex",
                table: "BlockchainTransactions",
                columns: new[] { "TxHash", "EventIndex" },
                unique: true,
                filter: "[TxHash] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Confirmed",
                table: "BlockchainTransactions",
                sql: "(Status <> 'CONFIRMED') OR (ConfirmationCount >= 20 AND ConfirmedAt IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockchainTransactions_Status_Pending",
                table: "BlockchainTransactions",
                sql: "(Status <> 'PENDING') OR (ConfirmationCount < 20)");
        }
    }
}
