using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <summary>
    /// T146 — per-network user payout/refund addresses (06 §1.4, §3.1a). Moves
    /// <c>Users.DefaultPayoutAddress</c> / <c>DefaultRefundAddress</c> and their
    /// <c>*ChangedAt</c> timestamps into <c>UserWalletAddresses</c> rows with
    /// <c>Network = 'TRON'</c> (every pre-F8 profile address is a TRON address),
    /// then drops the four columns. Create → copy → drop runs in one migration
    /// transaction, so a failure leaves the old columns in place.
    /// </summary>
    /// <inheritdoc />
    public partial class T146_UserWalletAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserWalletAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Network = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWalletAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserWalletAddresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            // Existing addresses → TRON rows. ChangedAt is copied as-is: NULL
            // marks an address that predates change tracking (T34) and carries
            // no cooldown, exactly as the NULL column did. CreatedAt is the
            // best known "address set" moment. A blank value is not an address
            // and is not carried over. The values are already canonical (T145).
            migrationBuilder.Sql(
                "INSERT INTO [UserWalletAddresses] ([Id], [UserId], [Network], [Type], [Address], [ChangedAt], [CreatedAt], [UpdatedAt]) " +
                "SELECT NEWID(), [Id], N'TRON', N'PAYOUT', [DefaultPayoutAddress], [PayoutAddressChangedAt], " +
                "COALESCE([PayoutAddressChangedAt], [CreatedAt]), COALESCE([PayoutAddressChangedAt], [CreatedAt]) " +
                "FROM [Users] WHERE [DefaultPayoutAddress] IS NOT NULL AND LTRIM(RTRIM([DefaultPayoutAddress])) <> N'';");

            migrationBuilder.Sql(
                "INSERT INTO [UserWalletAddresses] ([Id], [UserId], [Network], [Type], [Address], [ChangedAt], [CreatedAt], [UpdatedAt]) " +
                "SELECT NEWID(), [Id], N'TRON', N'REFUND', [DefaultRefundAddress], [RefundAddressChangedAt], " +
                "COALESCE([RefundAddressChangedAt], [CreatedAt]), COALESCE([RefundAddressChangedAt], [CreatedAt]) " +
                "FROM [Users] WHERE [DefaultRefundAddress] IS NOT NULL AND LTRIM(RTRIM([DefaultRefundAddress])) <> N'';");

            migrationBuilder.CreateIndex(
                name: "IX_UserWalletAddresses_Network_Address",
                table: "UserWalletAddresses",
                columns: new[] { "Network", "Address" });

            migrationBuilder.CreateIndex(
                name: "UQ_UserWalletAddresses_UserId_Network_Type",
                table: "UserWalletAddresses",
                columns: new[] { "UserId", "Network", "Type" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Users_DefaultPayoutAddress",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_DefaultRefundAddress",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultPayoutAddress",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DefaultRefundAddress",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PayoutAddressChangedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefundAddressChangedAt",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-T146 schema holds one TRON payout and one TRON refund
            // address per user. A non-TRON address has nowhere to go — refuse
            // rather than drop data.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [UserWalletAddresses] WHERE [Network] <> N'TRON') " +
                "THROW 50146, 'T146_UserWalletAddresses Down: non-TRON user addresses exist; the pre-F8 schema cannot hold them.', 1;");

            migrationBuilder.AddColumn<string>(
                name: "DefaultPayoutAddress",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultRefundAddress",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutAddressChangedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundAddressChangedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE u SET u.[DefaultPayoutAddress] = w.[Address], u.[PayoutAddressChangedAt] = w.[ChangedAt] " +
                "FROM [Users] u JOIN [UserWalletAddresses] w ON w.[UserId] = u.[Id] " +
                "WHERE w.[Network] = N'TRON' AND w.[Type] = N'PAYOUT';");

            migrationBuilder.Sql(
                "UPDATE u SET u.[DefaultRefundAddress] = w.[Address], u.[RefundAddressChangedAt] = w.[ChangedAt] " +
                "FROM [Users] u JOIN [UserWalletAddresses] w ON w.[UserId] = u.[Id] " +
                "WHERE w.[Network] = N'TRON' AND w.[Type] = N'REFUND';");

            migrationBuilder.DropTable(
                name: "UserWalletAddresses");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "DefaultPayoutAddress", "DefaultRefundAddress", "PayoutAddressChangedAt", "RefundAddressChangedAt" },
                values: new object[] { null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultPayoutAddress",
                table: "Users",
                column: "DefaultPayoutAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultRefundAddress",
                table: "Users",
                column: "DefaultRefundAddress");
        }
    }
}
