using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T68_AddProcessedNonces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedNonces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nonce = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedNonces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedNonces_ExpiresAt",
                table: "ProcessedNonces",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "UX_ProcessedNonces_Source_Nonce",
                table: "ProcessedNonces",
                columns: new[] { "Source", "Nonce" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedNonces");
        }
    }
}
