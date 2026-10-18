using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T158_PaymentAddressMonitorFromBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MonitorFromBlock",
                table: "PaymentAddresses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAddresses_MonitorFromBlock",
                table: "PaymentAddresses",
                sql: "[MonitorFromBlock] IS NULL OR [MonitorFromBlock] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAddresses_MonitorFromBlock",
                table: "PaymentAddresses");

            migrationBuilder.DropColumn(
                name: "MonitorFromBlock",
                table: "PaymentAddresses");
        }
    }
}
