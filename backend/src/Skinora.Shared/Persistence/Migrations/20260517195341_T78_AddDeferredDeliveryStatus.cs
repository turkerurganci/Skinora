using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T78_AddDeferredDeliveryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationDeliveries_Deferred_LastError",
                table: "NotificationDeliveries",
                sql: "(Status <> 'DEFERRED') OR (LastError IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationDeliveries_Deferred_LastError",
                table: "NotificationDeliveries");
        }
    }
}
