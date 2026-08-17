using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T129_SettlementCheckColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementCheckedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementEscalatedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000003d"), "Settlement", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Mutabakat süresi (gün) — teslimat doğrulandıktan sonra satıcı ödemesinin bekletileceği süre (02 §4.5.1). `PayoutEligibleAt = ItemDeliveredAt + bu değer` olarak ITEM_DELIVERED girişinde hesaplanır; süre dolmadan ne satıcı payout'u ne de depozit sweep'i kuyruğa girer. Steam'in 7 günlük trade geri alma penceresini kapsamalıdır — 7'nin altına ayarlanamaz (02 §16.2).", true, "payout_settlement_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "8" },
                    { new Guid("0aa51010-0000-0000-0000-00000000003e"), "Settlement", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Mutabakat sonu kontrolü envanter okunamadığı için sonuca varamadığında, kaç saat sonra admin'e eskale edileceği (03 §2.4 adım 2 üçüncü dal). Eşiğe kadar kontrol her turda tekrarlanır; eşik aşılınca admin bildirimi gider ve işlem insan incelemesine düşer. Ödeme her iki durumda da parkta kalır — eşik yalnızca 'ne zaman insana sorulur' sorusunu yanıtlar, ödemeyi serbest bırakmaz.", true, "settlement.unreadable_escalation_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "48" },
                    { new Guid("0aa51010-0000-0000-0000-00000000003f"), "Settlement", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Geri alma tespitinde OTOMATİK iade açık mı (T129 launch kapısı — T125'in `delivery.inventory_evidence_auto_release_enabled` kapısının ikizi). false iken geri alma imzası kayda geçer ve admin'e eskale edilir ama parayı kendiliğinden hareket ettirmez; admin `admin_resolve_refund` ile karar verir. true iken imza doğrudan `delivery_reversed` tetikler → REFUNDED + alıcıya iade + satıcıya DELIVERY_REVERSED fraud flag. Gerçek bir geri alma ölçülene kadar (T122 runbook §7 ölçemedi) kapalı kalır.", true, "settlement.reversal_auto_refund_enabled", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003d"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003e"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003f"));

            migrationBuilder.DropColumn(
                name: "SettlementCheckedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SettlementEscalatedAt",
                table: "Transactions");
        }
    }
}
