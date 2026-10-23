using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T169a_DepositRefundDailyLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DepositRefundCountedAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepositRefundLimitHeldAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepositRefundLimitReleasedAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepositRefundLimitReleasedByAdminId",
                table: "BlockchainTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DepositRefundLimitLocks",
                columns: table => new
                {
                    Network = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositRefundLimitLocks", x => x.Network);
                });

            migrationBuilder.InsertData(
                table: "DepositRefundLimitLocks",
                columns: new[] { "Network", "LockedAt" },
                values: new object[,]
                {
                    { "ETHEREUM", null },
                    { "TRON", null }
                });

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000032"),
                column: "Description",
                value: "İade gas fee FALLBACK değeri (USDT). Normalde kesinti sidecar'ın gönderim öncesi tahmininden gelir: T169a'dan beri platformun iade için gönderdiği TRX'in tamamı (hesap açma + %10 paylı yakma takviyesi + takviyenin bandı; 02 §4.7). Tahmin alınamazsa son ölçülen tahminle bu değerin küçüğü kesilir (T160b) ve en düşük iade eşiği bu tutarla hesaplanır. 2,80 = yeni depozitten, USDT tutan alıcıya iadenin ana ağ maliyeti (8,44 TRX × 0,3313; T169a, öneriyle kaydedildi — ⚠ PARA).");

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000051"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "TRON'da depozit adreslerinden yapılan iadelerin herhangi bir 24 saatte gönderebileceği en çok tutar (USDT). Aşacak iade beklemede kalır (FAILED olmaz, deneme hakkı harcanmaz), admin'e alarm gider; pencerede yer açılınca ya da admin AD42 ile serbest bırakınca gider. Sıcak cüzdandan çıkan iadeler sayılmaz. 100 = proje sahibi kararı 2026-10-21 (T169a).", true, "blockchain.tron.deposit_refund_daily_limit_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "100" },
                    { new Guid("0aa51010-0000-0000-0000-000000000052"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum'da depozit adreslerinden (sweep'ten önce) yapılan iadelerin herhangi bir 24 saatte gönderebileceği en çok tutar (USDT). Aşacak iade beklemede kalır (FAILED olmaz, deneme hakkı harcanmaz), admin'e alarm gider; pencerede yer açılınca ya da admin AD42 ile serbest bırakınca gider. Sweep sonrası sıcak cüzdandan çıkan iadeler sayılmaz. 100 = proje sahibi kararı 2026-10-21 (T169a).", true, "blockchain.ethereum.deposit_refund_daily_limit_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "100" }
                });

            // T169a (öneriyle kaydedildi — ⚠ PARA) — the TRON refund fallback
            // follows the new definition of the refund fee (the platform's
            // whole outlay: activation + the 10 % top-up + its Bandwidth —
            // 8.44 TRX × 0.3313 on mainnet for a fresh deposit refunding a
            // USDT holder). Moved ONLY while the row still holds the T169
            // value, so an admin-tuned value survives (T169's rule).
            SetIfStillDefault(migrationBuilder, "blockchain.refund_gas_fee_estimate_usdt", "2.80", "2.60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepositRefundLimitLocks");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000051"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000052"));

            migrationBuilder.DropColumn(
                name: "DepositRefundCountedAt",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "DepositRefundLimitHeldAt",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "DepositRefundLimitReleasedAt",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "DepositRefundLimitReleasedByAdminId",
                table: "BlockchainTransactions");

            SetIfStillDefault(migrationBuilder, "blockchain.refund_gas_fee_estimate_usdt", "2.60", "2.80");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000032"),
                column: "Description",
                value: "İade gas fee FALLBACK değeri (USDT). Normalde kesinti sidecar'ın gönderim öncesi zincir tahmininden gelir (Prova-GasFeeChargedIsFixedGuess, 2026-09-02); tahmin alınamazsa RefundDecisionService bu değeri kullanır ve iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine bu değerle karar verilir.");
        }

        /// <summary>
        /// Move <paramref name="key"/> to <paramref name="newValue"/> only while it
        /// still holds one of <paramref name="oldValues"/> (a configured row) —
        /// the T169_OwnerDecisions rule.
        /// </summary>
        private static void SetIfStillDefault(
            MigrationBuilder migrationBuilder, string key, string newValue, params string[] oldValues)
        {
            var olds = string.Join(", ", oldValues.Select(v => $"N'{v}'"));
            migrationBuilder.Sql(
                $"UPDATE [SystemSettings] SET [Value] = N'{newValue}', [UpdatedAt] = SYSUTCDATETIME() " +
                $"WHERE [Key] = N'{key}' AND [IsConfigured] = 1 AND [Value] IN ({olds});");
        }
    }
}
