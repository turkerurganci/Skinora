using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T125_DeliveryEvidenceCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryEvidenceCaptures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Evidence = table.Column<int>(type: "int", nullable: false),
                    AutoReleaseGated = table.Column<bool>(type: "bit", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryEvidenceCaptures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryEvidenceCaptures_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-00000000003c"), "Delivery", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Envanter kanıtına dayalı OTOMATİK teslimat onayı açık mı (02 §9.2 launch kapısı). false iken `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` kanıtı kayda geçer ve ekranda görünür ama parayı tek başına serbest bırakmaz — insan incelemesi gerekir. Alıcının kendi 'teslim aldım' onayı bu kapıdan ETKİLENMEZ. İlk N gerçek teslimatın kanıtı (DeliveryEvidenceCaptures) incelendikten sonra true yapılır (DEPLOY_RUNBOOK §H).", true, "delivery.inventory_evidence_auto_release_enabled", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvidenceCaptures_AutoReleaseGated",
                table: "DeliveryEvidenceCaptures",
                column: "AutoReleaseGated");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvidenceCaptures_TransactionId",
                table: "DeliveryEvidenceCaptures",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryEvidenceCaptures");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003c"));
        }
    }
}
