using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T83_AddUserLoginLogVpnSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasVpnSignal",
                table: "UserLoginLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasVpnSignal",
                table: "UserLoginLogs");
        }
    }
}
