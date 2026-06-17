using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP5_AddDisputeResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Cancel",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Disputes_Closed_ResolvedAt",
                table: "Disputes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Cancel",
                table: "Transactions",
                sql: "(Status <> 'CANCELLED_TIMEOUT' AND Status <> 'CANCELLED_SELLER' AND Status <> 'CANCELLED_BUYER' AND Status <> 'CANCELLED_ADMIN' AND Status <> 'REFUNDED') OR (CancelledBy IS NOT NULL AND CancelReason IS NOT NULL AND CancelledAt IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Disputes_Resolved_ResolvedAt",
                table: "Disputes",
                sql: "(Status <> 'CLOSED' AND Status <> 'RESOLVED_FOR_SELLER' AND Status <> 'RESOLVED_FOR_BUYER') OR (ResolvedAt IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Cancel",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Disputes_Resolved_ResolvedAt",
                table: "Disputes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Cancel",
                table: "Transactions",
                sql: "(Status <> 'CANCELLED_TIMEOUT' AND Status <> 'CANCELLED_SELLER' AND Status <> 'CANCELLED_BUYER' AND Status <> 'CANCELLED_ADMIN') OR (CancelledBy IS NOT NULL AND CancelReason IS NOT NULL AND CancelledAt IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Disputes_Closed_ResolvedAt",
                table: "Disputes",
                sql: "(Status <> 'CLOSED') OR (ResolvedAt IS NOT NULL)");
        }
    }
}
