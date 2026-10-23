using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T172a_PostRestoreTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundRowClosures_Network_Ethereum",
                table: "OutboundRowClosures");

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000053"), "Settlement", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Giden transfer gönderimi DURDURULSUN mu (T172a — DEPLOY_RUNBOOK §N.8). true iken gönderim işi HİÇBİR satırı göndermez (ödeme, iade, sweep, her ağ): satır PENDING kalır, deneme hakkı harcanmaz, FAILED olmaz; onay işi ve izleme sürer. Geri yüklemeden sonra imza servisleri kapatma aracının (AD33–AD37) zincir okuması için açıkken kullanılır. Normalde false.", true, "outbound.dispatch_paused", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundRowClosures_Network_Known",
                table: "OutboundRowClosures",
                sql: "[Network] IN ('TRON', 'ETHEREUM')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboundRowClosures_Network_Known",
                table: "OutboundRowClosures");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000053"));

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboundRowClosures_Network_Ethereum",
                table: "OutboundRowClosures",
                sql: "[Network] = 'ETHEREUM'");
        }
    }
}
