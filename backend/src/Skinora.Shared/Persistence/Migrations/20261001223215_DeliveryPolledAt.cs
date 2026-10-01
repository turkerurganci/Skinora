using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryPolledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryPolledAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryPolledAt",
                table: "Transactions");
        }
    }
}
