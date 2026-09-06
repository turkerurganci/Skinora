using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeGasFeeEstimateFallbackDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000032"),
                column: "Description",
                value: "İade gas fee FALLBACK değeri (USDT). Normalde kesinti sidecar'ın gönderim öncesi zincir tahmininden gelir (Prova-GasFeeChargedIsFixedGuess, 2026-09-02); tahmin alınamazsa RefundDecisionService bu değeri kullanır ve iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine bu değerle karar verilir.");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003b"),
                column: "Description",
                value: "Satıcı payout gas fee FALLBACK değeri (USDT). Normalde gas-fee koruma split'inin (02 §4.7) girdisi sidecar'ın gönderim öncesi zincir tahmininden gelir (Prova-GasFeeChargedIsFixedGuess, 2026-09-02); tahmin alınamazsa SellerPayoutQueueJob bu değeri kullanır: gasFee komisyon×%10 eşiğini aşarsa aşan kısım satıcının alacağından düşülür (04 §7.3 örneği: 0.50 → satıcıdan 0.30).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000032"),
                column: "Description",
                value: "T72 MVP iade gas fee tahmini (USDT). RefundDecisionService bu değeri kullanarak iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine karar verir. T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir.");

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003b"),
                column: "Description",
                value: "WP1 MVP satıcı payout gas fee tahmini (USDT). SellerPayoutQueueJob bu değeri gas-fee koruma split'inde (02 §4.7) kullanır: gasFee komisyon×%10 eşiğini aşarsa aşan kısım satıcının alacağından düşülür (04 §7.3 örneği: 0.50 → satıcıdan 0.30). T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir.");
        }
    }
}
