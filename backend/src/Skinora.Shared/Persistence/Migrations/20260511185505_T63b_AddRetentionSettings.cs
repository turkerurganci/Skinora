using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T63b_AddRetentionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000002a"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Processed OutboxMessage retention süresi (gün, 06 §3.18). Status=PROCESSED ve ProcessedAt bu süreden eski kayıtlar OutboxRetentionCleanupJob tarafından toplu hard delete edilir.", true, "retention.outbox_message_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-00000000002b"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "ProcessedEvent retention süresi (gün, 06 §3.19). ProcessedAt bu süreden eski kayıtlar — OutboxMessage temizlenmeden önce — toplu hard delete edilir. FK olmadığı için silme sırası uygulama seviyesinde garanti edilir.", true, "retention.processed_event_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-00000000002c"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "ExternalIdempotencyRecord retention süresi (gün, 06 §3.21). Status=completed ve CompletedAt bu süreden eski kayıtlar toplu hard delete edilir. in_progress ve failed kayıtlar lease/retry akışına bırakılır.", true, "retention.external_idempotency_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-00000000002d"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Bağımsız bildirim (Notification, TransactionId IS NULL) retention süresi (gün, 06 §1, §6.1). CreatedAt bu süreden eski kayıtlar bağlı NotificationDelivery kayıtlarıyla birlikte (önce delivery, sonra notification) toplu hard delete edilir.", true, "retention.orphan_notification_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "365" },
                    { new Guid("0aa51010-0000-0000-0000-00000000002e"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "UserLoginLog retention süresi (gün, 06 §1, §6.1). CreatedAt bu süreden eski kayıtlar toplu hard delete edilir (soft-delete kontrolü dışındadır — retention IsDeleted flag'inden bağımsız çalışır).", true, "retention.user_login_log_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "365" },
                    { new Guid("0aa51010-0000-0000-0000-00000000002f"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Outbox retention job'unun tek SELECT+DELETE iterasyonunda işleyebileceği maksimum kayıt sayısı. Job, eligible kayıt kalmayana kadar batch'leri tekrarlar.", true, "retention.batch_size_outbox", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1000" },
                    { new Guid("0aa51010-0000-0000-0000-000000000030"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Bağımsız bildirim retention job'unun tek iterasyonda işleyebileceği maksimum Notification sayısı. Bağlı NotificationDelivery kayıtları aynı iterasyon içinde silinir.", true, "retention.batch_size_notification", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "500" },
                    { new Guid("0aa51010-0000-0000-0000-000000000031"), "Retention", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "UserLoginLog retention job'unun tek iterasyonda işleyebileceği maksimum kayıt sayısı.", true, "retention.batch_size_user_login_log", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1000" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002a"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002b"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002c"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002d"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002e"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000002f"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000030"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000031"));
        }
    }
}
