using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxSequenceOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_CreatedAt_Pending",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "OutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAt_Pending",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt", "Sequence" },
                filter: "[Status] IN ('PENDING', 'FAILED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_CreatedAt_Pending",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAt_Pending",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt" },
                filter: "[Status] IN ('PENDING', 'FAILED')");
        }
    }
}
