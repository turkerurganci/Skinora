using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T169_OwnerDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByAdminId",
                table: "BlockchainTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000001d"),
                column: "Description",
                value: "Geo-block — ISO-3166-1 alpha-2 ülke kodları CSV; 'NONE' hiçbir ülke engellenmemiş demektir. Admin tarafından yönetilir. Varsayılan IR,KP,CU,MM = FATF eylem çağrısı listesi (İran, Kuzey Kore, Myanmar) + Küba (proje sahibi kararı 2026-10-21, T169).");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003a"),
                column: "Description",
                value: "Hot wallet TRX bakiye alt eşiği (TRX, gas için). Bu değerin altına düşerse HOT_WALLET_THRESHOLD_BREACHED audit + admin SignalR alert fırlar (T77 — 05 §3.3). 1500 TRX ≈ günde 10 TRON satışında 7 günlük yakma payı (satış başı ~22 TRX; T169, öneriyle kaydedildi — ⚠).");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000044"),
                column: "Description",
                value: "Ethereum iade gas fee FALLBACK değeri (USDT) — runtime tahmin alınamazsa kesilir ve en düşük iade eşiğinin girdisi olur. Varsayılan 0,20: en pahalı iadeyi (88.201 gas) 0,92 gwei'ye kadar karşılar; daha pahalı günde farkı platform öder (T169, öneriyle kaydedildi — ⚠). TRON'un karşılığı blockchain.refund_gas_fee_estimate_usdt.");

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000004f"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "TRON için en düşük satış tutarı (USDT). Fiyatı bunun altındaki ilan TRON'u kabul edemez ve alıcı TRON'da ödeyemez (422 NETWORK_MIN_AMOUNT_NOT_MET); ilan sihirbazı ve kabul formu TRON'u devre dışı gösterir. 50 = proje sahibi kararı 2026-10-21 (T169): 50 $'ın altında TRON ağ ücreti satıcının %9'unu aşar.", true, "blockchain.tron.min_transaction_amount_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "50" },
                    { new Guid("0aa51010-0000-0000-0000-000000000050"), "Settlement", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Satıcı ödemeleri ve alıcı iadeleri OTOMATİK gönderilsin mi (T169 / T175 açılış kapısı). false iken her SELLER_PAYOUT ve *_REFUND satırı admin onayı bekler (AD40/AD41 — cüzdan ekranı, AuditLog OUTBOUND_TRANSFER_APPROVED); sweep (depozit → sıcak cüzdan) beklemez. Test boyunca kapalı (proje sahibi kararı 2026-10-21); ilk en az 5 gerçek teslimat elle incelendikten sonra açılır (11 T175).", true, "outbound.auto_send_enabled", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" }
                });

            // T169 — the owner decisions of 2026-10-21 and the recommendations
            // recorded with them (öneriyle kaydedildi — ⚠). A value is changed
            // ONLY while it still holds the previous default: an admin who
            // already tuned a row keeps their value. On a fresh database the
            // earlier migrations wrote the previous seed, so the result equals
            // the model seed above.
            //
            // Seeded rows (06 §3.17):
            SetIfStillDefault(migrationBuilder, "auth.banned_countries", "IR,KP,CU,MM", "NONE");
            SetIfStillDefault(migrationBuilder, "blockchain.refund_gas_fee_estimate_usdt", "2.60", "2.0");
            SetIfStillDefault(migrationBuilder, "hot_wallet.trx_balance_minimum", "1500", "100");
            SetIfStillDefault(migrationBuilder, "blockchain.payout_gas_fee_estimate_usdt", "2.25", "0.50");
            SetIfStillDefault(migrationBuilder, "blockchain.ethereum.refund_gas_fee_estimate_usdt", "0.20", "0.50");

            // Env-bootstrapped rows (SKINORA_SETTING_*, DEPLOY_RUNBOOK §A): the
            // bootstrap writes them once and never again, so a database already
            // bootstrapped from the previous .env.example values keeps them
            // unless they are moved here. A fresh database still has NULL at this
            // point (the bootstrap runs after the migrations) and is untouched.
            SetIfStillDefault(migrationBuilder, "min_transaction_amount", "10", "1.0", "1", "1.00");
            SetIfStillDefault(migrationBuilder, "max_transaction_amount", "2900", "10000.0", "10000", "10000.00");
            SetIfStillDefault(migrationBuilder, "hot_wallet_limit", "4000", "100000.0", "100000", "100000.00");
            SetIfStillDefault(migrationBuilder, "delivery_timeout_minutes", "720", "60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // T169 — reverse the value moves, again only where the row still
            // holds the T169 value.
            SetIfStillDefault(migrationBuilder, "auth.banned_countries", "NONE", "IR,KP,CU,MM");
            SetIfStillDefault(migrationBuilder, "blockchain.refund_gas_fee_estimate_usdt", "2.0", "2.60");
            SetIfStillDefault(migrationBuilder, "hot_wallet.trx_balance_minimum", "100", "1500");
            SetIfStillDefault(migrationBuilder, "blockchain.payout_gas_fee_estimate_usdt", "0.50", "2.25");
            SetIfStillDefault(migrationBuilder, "blockchain.ethereum.refund_gas_fee_estimate_usdt", "0.50", "0.20");
            SetIfStillDefault(migrationBuilder, "min_transaction_amount", "1.0", "10");
            SetIfStillDefault(migrationBuilder, "max_transaction_amount", "10000.0", "2900");
            SetIfStillDefault(migrationBuilder, "hot_wallet_limit", "100000.0", "4000");
            SetIfStillDefault(migrationBuilder, "delivery_timeout_minutes", "60", "720");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004f"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000050"));

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "ApprovedByAdminId",
                table: "BlockchainTransactions");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000001d"),
                column: "Description",
                value: "Geo-block — ISO-3166-1 alpha-2 ülke kodları CSV (örn: 'IR,KP,CU'); 'NONE' hiçbir ülke engellenmemiş demektir. Admin tarafından yönetilir.");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003a"),
                column: "Description",
                value: "Hot wallet TRX bakiye alt eşiği (TRX, gas için). Bu değerin altına düşerse HOT_WALLET_THRESHOLD_BREACHED audit + admin SignalR alert fırlar (T77 — 05 §3.3). MVP ölçeğinde 100 TRX ≈ 50 TRC-20 transfer gas worst-case headroom.");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000044"),
                column: "Description",
                value: "Ethereum iade gas fee FALLBACK değeri (USDT) — runtime tahmin alınamazsa kesilir ve en düşük iade eşiğinin girdisi olur. Varsayılan 0,50 = T142'nin işlem başı ücret tavanı (08 §3.7.7). TRON'un karşılığı blockchain.refund_gas_fee_estimate_usdt.");

        }

        /// <summary>
        /// Move <paramref name="key"/> to <paramref name="newValue"/> only while it
        /// still holds one of <paramref name="oldValues"/> (a configured row).
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
