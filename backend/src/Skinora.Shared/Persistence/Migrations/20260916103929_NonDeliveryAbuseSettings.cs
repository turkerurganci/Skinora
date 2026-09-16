using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NonDeliveryAbuseSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000041"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Teslim etmeme yaptırımının yuvarlanan penceresi (gün) — 02 §14.2. Sayılan olaylar: ödeme alındıktan sonra teslimat süresinin dolması, ödeme sonrası satıcı iptali, teslimattan sonra geri alma (DeliveryReversedAt). Admin kararıyla serbest bırakılan ve alıcının Steam hesabı yüzünden dolan süreler sayılmaz. 0 = kural kapalı.", true, "non_delivery_window_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-000000000042"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Pencere içindeki kaçıncı teslim etmeme olayında satıcı hesabına ABNORMAL_BEHAVIOR flag'i (pattern NON_DELIVERY_REPEAT) yazılacağı — 02 §14.2 'eşiği aşan ilk tekrar'. Hesap flag'i yeni işlem açmayı engeller; açık işlemler dondurulmaz (02 §14.0 cascade yalnız yaptırım listesi / hesap ele geçirme içindir). 0 = kural kapalı.", true, "non_delivery_flag_count", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "2" },
                    { new Guid("0aa51010-0000-0000-0000-000000000043"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Pencere içindeki kaçıncı teslim etmeme olayında satıcı hesabının OTOMATİK askıya alınacağı — 02 §14.2 'sonraki tekrar'. Askı süresizdir, admin incelemesiyle kalkar; giriş engellenmez, para hareketi yolları kapanır. Askı yalnız YENİ bir olayla tetiklenir: admin askıyı kaldırdıktan sonra pencere hâlâ eşiğin üstündeyse, başka bir işlemin tamamlanması askıyı geri getirmez. 0 = kural kapalı.", true, "non_delivery_suspend_count", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "3" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000041"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000042"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000043"));
        }
    }
}
