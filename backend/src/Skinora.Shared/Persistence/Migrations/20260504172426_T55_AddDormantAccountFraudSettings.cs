using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T55_AddDormantAccountFraudSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000023"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Dormant kontrolü için minimum hesap yaşı (gün). Bu yaşın altında hesap 'yeni hesap' sayılır ve T39 yeni hesap limitleri uygulanır; bu eşiğin üzerinde 0 işlemli hesabın yüksek tutarlı denemesi ABNORMAL_BEHAVIOR ile flag'lenir (02 §14.3).", true, "dormant_account_min_age_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000024"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Dormant hesap için tek işlem tutar eşiği (USDT). Hiç işlem yapmamış hesabın bu tutarın üzerinde işlem denemesi otomatik flag tetikler. Admin tarafından risk profiline göre belirlenir.", "dormant_account_value_threshold", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000023"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000024"));
        }
    }
}
