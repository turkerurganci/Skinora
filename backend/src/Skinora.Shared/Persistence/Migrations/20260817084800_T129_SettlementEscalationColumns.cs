using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T129_SettlementEscalationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SettlementClearedByAdminId",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementEscalationReason",
                table: "Transactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003f"),
                column: "Description",
                value: "Geri alma tespitinde OTOMATİK iade açık mı (T129 launch kapısı, T125 kapısının ikizi). false iken imza kayda geçer ve admin'e eskale edilir, para hareket etmez; kararı admin verir — satıcı lehine AD32 clear-settlement, alıcı lehine dispute üzerinden AD29. İki kol AYNI sonucu üretmez: DeliveryReversedAt'i yalnız otomatik dal yazar, itibar paydası ve fraud flag yalnız orada oluşur. true iken imza delivery_reversed tetikler. Gerçek geri alma ölçülene kadar (T122 §7) kapalı kalır.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SettlementClearedByAdminId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SettlementEscalationReason",
                table: "Transactions");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003f"),
                column: "Description",
                value: "Geri alma tespitinde OTOMATİK iade açık mı (T129 launch kapısı — T125'in `delivery.inventory_evidence_auto_release_enabled` kapısının ikizi). false iken geri alma imzası kayda geçer ve admin'e eskale edilir ama parayı kendiliğinden hareket ettirmez; admin `admin_resolve_refund` ile karar verir. true iken imza doğrudan `delivery_reversed` tetikler → REFUNDED + alıcıya iade + satıcıya DELIVERY_REVERSED fraud flag. Gerçek bir geri alma ölçülene kadar (T122 runbook §7 ölçemedi) kapalı kalır.");
        }
    }
}
