using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T82_AddSanctionedAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SanctionedAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Network = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ListedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanctionedAddresses", x => x.Id);
                    table.CheckConstraint("CK_SanctionedAddresses_Network", "[Network] IN ('TRC-20')");
                    table.CheckConstraint("CK_SanctionedAddresses_Source", "[Source] IN ('OFAC', 'EU', 'UN', 'MANUAL')");
                    table.ForeignKey(
                        name: "FK_SanctionedAddresses_Users_AddedByAdminId",
                        column: x => x.AddedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SanctionedAddresses_AddedByAdminId",
                table: "SanctionedAddresses",
                column: "AddedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_SanctionedAddresses_Address_Active",
                table: "SanctionedAddresses",
                column: "Address",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SanctionedAddresses");
        }
    }
}
