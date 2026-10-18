using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <summary>
    /// T148 — Ethereum's per-network payment parameters (05 §3.3a, 06 §3.17,
    /// seed indices 68–75). Data only: eight new <c>SystemSettings</c> rows
    /// under <c>blockchain.ethereum.*</c>, seeded with the T142 measurements.
    /// TRON's rows are untouched — they keep their keys and any admin-tuned
    /// value. No schema change.
    /// </summary>
    public partial class T148_NetworkSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000044"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum iade gas fee FALLBACK değeri (USDT) — runtime tahmin alınamazsa kesilir ve en düşük iade eşiğinin girdisi olur. Varsayılan 0,50 = T142'nin işlem başı ücret tavanı (08 §3.7.7). TRON'un karşılığı blockchain.refund_gas_fee_estimate_usdt.", true, "blockchain.ethereum.refund_gas_fee_estimate_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.50" },
                    { new Guid("0aa51010-0000-0000-0000-000000000045"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum satıcı payout gas fee FALLBACK değeri (USDT) — runtime tahmin alınamazsa kullanılır. Ethereum'da payout ücretini platform tavana kadar üstlenir (08 §3.7.7, T149). TRON'un karşılığı blockchain.payout_gas_fee_estimate_usdt.", true, "blockchain.ethereum.payout_gas_fee_estimate_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.50" },
                    { new Guid("0aa51010-0000-0000-0000-000000000046"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum'da kullanıcıdan kesilebilecek gas fee üst sınırı (USDT). Runtime tahmin bu değeri aşarsa REDDEDİLİR (kırpılmaz) ve fallback kesilir. 10,0 son 30 günün en pahalı bloğunda bile (38,7 gwei) bir iade yakıtını (~7,8 $) geçirir; çöküş günlerinde tavanı aşan tahmin reddedilir (T142 ölçümü, T149 gözden geçirir).", true, "blockchain.ethereum.max_charged_gas_fee_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "10.0" },
                    { new Guid("0aa51010-0000-0000-0000-000000000047"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum'da platformun üstlendiği ağ ücreti tavanı (USD, gönderim başına): sweep işlemi (yakıt + sweep) ve payout için ayrı ayrı; satış başı en çok 1,00 $. Tavanın üstünde sweep/payout bekler, iade beklemez (08 §3.7.7; T142 ölçümü — 30 günün p99'u satış başı 1,73 $, tavanın üstünde geçen süre %3,1).", true, "blockchain.ethereum.fee_cap_usd_per_transfer", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.50" },
                    { new Guid("0aa51010-0000-0000-0000-000000000048"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Ethereum'da ücret tavanı yüzünden KESİNTİSİZ bekleyen sweep ya da payout bu süreyi (dakika) aşarsa admin alarmı. Varsayılan 180 = 3 saat (T142: son 30 günün en uzun beklemesi 2,06 sa, 2025-10-10 çöküşünde 6,45 sa; 08 §3.7.7).", true, "blockchain.ethereum.fee_cap_wait_alarm_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "180" },
                    { new Guid("0aa51010-0000-0000-0000-000000000049"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Ethereum'da son finalized bloğun zaman damgası bu süreden (dakika) eskiyse alarm — yeni ödeme kabul edilemiyordur (08 §3.7.8). Olağan gecikme 12,8–18,8 dk ölçüldü (T142).", true, "blockchain.ethereum.finality_stall_alarm_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-00000000004a"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum sıcak cüzdanının ETH bakiye alt eşiği (ETH). Payout, sweep sonrası iade ve soğuk aktarımın gas'ını sıcak cüzdan kendi ETH'siyle öder; altına düşerse admin alarmı (08 §3.7.5). 0,06 = 7 gün × 20 payout × 0,000211 ETH + bir çöküş günü iadesi (T142).", true, "blockchain.ethereum.hot_wallet_eth_balance_minimum", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.06" },
                    { new Guid("0aa51010-0000-0000-0000-00000000004b"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum yakıt cüzdanının ETH bakiye alt eşiği (ETH). Yakıt cüzdanı depozitlere gas için ETH gönderir; altına düşerse admin alarmı (08 §3.7.5). 0,06 = 7 günlük en yoğun sweep yakıtı (0,030 ETH) + bir çöküş günü iade yakıtı (0,031 ETH) (T142).", true, "blockchain.ethereum.fuel_wallet_eth_balance_minimum", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.06" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000044"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000045"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000046"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000047"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000048"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000049"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004a"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004b"));
        }
    }
}
