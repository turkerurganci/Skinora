using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T123_RenameTimeoutSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000002"),
                columns: new[] { "Description", "Key" },
                values: new object[] { "Satıcı hazırlık onayı penceresi — alıcı kabul ettikten sonra satıcının 'göndermeye hazırım' demesi için tanınan süre (03 §2.3). Dolarsa işlem satıcı kusuruyla iptal olur (02 §3.1).", "seller_confirm_timeout_minutes" });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000006"),
                columns: new[] { "Description", "Key" },
                values: new object[] { "Satıcı teslimat penceresi — ödeme emanete girdikten sonra satıcının item'ı doğrudan alıcıya göndermesi için tanınan süre (02 §2.2 adım 6). Ölçülmemiş bir değerdir; launch'ta muhafazakâr YÜKSEK tutulur (DEPLOY_RUNBOOK §A #6).", "delivery_timeout_minutes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000002"),
                columns: new[] { "Description", "Key" },
                values: new object[] { "Satıcı trade offer timeout süresi", "trade_offer_seller_timeout_minutes" });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000006"),
                columns: new[] { "Description", "Key" },
                values: new object[] { "Alıcı trade offer timeout süresi", "trade_offer_buyer_timeout_minutes" });
        }
    }
}
