using Skinora.Shared.Enums;

namespace Skinora.Shared.Blockchain;

/// <summary>How a network decides that a transfer can no longer be undone (05 §3.3a, 08 §3.0).</summary>
public enum ConfirmationRule
{
    /// <summary>
    /// A fixed number of blocks on top of the transfer's block — TRON's 20
    /// (19 Super Representatives have signed past it, 05 §3.3).
    /// </summary>
    BlockCount,

    /// <summary>
    /// The block has reached the chain's <c>finalized</c> checkpoint —
    /// Ethereum (11 §F8 K5, 08 §3.7.4). There is no short path: a block count
    /// on top of an unfinalized block does not qualify.
    /// </summary>
    Finalized,
}

/// <summary>
/// The chain facts that differ between the networks the platform pays on
/// (T148 — 05 §3.3a "Ağ başına ayarlar", 06 §1.4 "Onay alanları"). Every
/// TRON-only constant the backend used to apply unconditionally — the
/// 20-block confirmation threshold, SUN as the fee unit, the Energy /
/// Bandwidth receipt fields — is answered here, so code asks the policy of the
/// row's network instead of assuming TRON.
/// </summary>
/// <remarks>
/// <para>
/// These are properties of the chain, not tunables, which is why they are code
/// and not <c>SystemSetting</c> rows: the 20-block rule is also a database
/// CHECK (<c>CK_BlockchainTransactions_Status_Confirmed</c>, T145) and an admin
/// lowering it would only produce rows the database refuses. Values that an
/// operator may tune per network (fee fallbacks, alarm thresholds) live in
/// <c>SystemSetting</c> instead — see <c>BlockchainNetworkSettingKeys</c>.
/// </para>
/// </remarks>
/// <param name="Network">The network this policy describes.</param>
/// <param name="Confirmation">How finality is decided.</param>
/// <param name="RequiredConfirmations">
/// Blocks required under <see cref="ConfirmationRule.BlockCount"/>;
/// <c>null</c> for a <see cref="ConfirmationRule.Finalized"/> network.
/// </param>
/// <param name="NativeAssetSymbol">Gas asset as the balance snapshot reports it (<c>TRX</c> / <c>ETH</c>).</param>
/// <param name="NativeAssetDecimals">Decimals of the gas asset's smallest unit (SUN 6, wei 18).</param>
/// <param name="NativeFeeUnit">Name of that smallest unit (<c>SUN</c> / <c>wei</c>).</param>
/// <param name="ReportsTronResourceUsage">
/// <c>true</c> when a transfer receipt carries TRON's resource accounting —
/// the SUN burned and the Energy consumed / paid by the contract owner
/// (<c>RealizedFeeSun</c>, <c>EnergyUsageTotal</c>, <c>OriginEnergyUsage</c>).
/// Ethereum has no Energy; its realized fee is wei and is recorded in its own
/// field (T154).
/// </param>
/// <param name="ReportsFeeInWei">
/// <c>true</c> when the network's service reports fees in wei as decimal
/// strings (T154, 05 §3.3a): the estimate's <c>feeWei</c> + <c>ethPriceUsdt</c>
/// (the backend re-derives <c>feeUsdt</c> from them exactly) and the receipt's
/// <c>realizedFeeWei</c>, recorded in <c>BlockchainTransaction.RealizedFeeWei</c>.
/// </param>
public sealed record BlockchainNetworkPolicy(
    BlockchainNetwork Network,
    ConfirmationRule Confirmation,
    int? RequiredConfirmations,
    string NativeAssetSymbol,
    int NativeAssetDecimals,
    string NativeFeeUnit,
    bool ReportsTronResourceUsage,
    bool ReportsFeeInWei)
{
    /// <summary>TRON — 20 blocks, TRX in SUN, Energy/Bandwidth receipts (05 §3.3, 08 §3.3).</summary>
    public static readonly BlockchainNetworkPolicy Tron = new(
        Network: BlockchainNetwork.TRON,
        Confirmation: ConfirmationRule.BlockCount,
        RequiredConfirmations: 20,
        NativeAssetSymbol: "TRX",
        NativeAssetDecimals: 6,
        NativeFeeUnit: "SUN",
        ReportsTronResourceUsage: true,
        ReportsFeeInWei: false);

    /// <summary>Ethereum — <c>finalized</c> blocks, ETH in wei, no Energy (08 §3.7).</summary>
    public static readonly BlockchainNetworkPolicy Ethereum = new(
        Network: BlockchainNetwork.ETHEREUM,
        Confirmation: ConfirmationRule.Finalized,
        RequiredConfirmations: null,
        NativeAssetSymbol: "ETH",
        NativeAssetDecimals: 18,
        NativeFeeUnit: "wei",
        ReportsTronResourceUsage: false,
        ReportsFeeInWei: true)
    {
        OutboundSendsAreIdempotent = true,
        ReportsMonitorStartBlock = true,
        RefundsAfterSweepFromHotWallet = true,
        VerifiesUnjournalledTransferStatus = true,
        BalanceSnapshotChunkSize = 20,
    };

    /// <summary>
    /// T158 (h) — the network's service names the block a newly derived
    /// deposit is watched from (<c>watchFromBlock</c>, its last
    /// <c>finalized</c> head — 08 §3.7.4). The backend persists it
    /// (<c>PaymentAddress.MonitorFromBlock</c>) and sends it with every monitor
    /// start, so an outage longer than the service's restart read-back cannot
    /// hide a payment. A derive answer without it is not accepted (the
    /// allocator retries): an Ethereum deposit without a start block would be
    /// back to the bounded read-back. TRON's service reports none: <c>false</c>.
    /// </summary>
    public bool ReportsMonitorStartBlock { get; init; }

    /// <summary>
    /// T158 (f) — a buyer refund queued after the deposit was swept leaves the
    /// network's HOT wallet (08 §3.7.6 "Kaynaklar"): the deposit is empty, the
    /// escrowed money is in the hot wallet. A refund while the sweep is in
    /// flight waits for it to settle. TRON's service only signs deposit-sourced
    /// refunds: <c>false</c> (unchanged behaviour).
    /// </summary>
    public bool RefundsAfterSweepFromHotWallet { get; init; }

    /// <summary>
    /// T158 (g) — every status read carries what the row says the transfer is
    /// (contract, sender, recipient, amount): for a hash the service's journal
    /// does not hold (lost journal) the service checks the receipt's Transfer
    /// log against it and answers <c>UNKNOWN</c> when it does not match —
    /// the row is then never confirmed (fail closed). TRON: <c>false</c>.
    /// </summary>
    public bool VerifiesUnjournalledTransferStatus { get; init; }

    /// <summary>
    /// T158 (i) — most addresses per balance-snapshot request to the network's
    /// service; <c>null</c> = one request. The Ethereum service paces its reads
    /// (200 CU/s, ~0.36 s per address with ETH + USDT + USDC — 08 §3.7.4), so
    /// one request for every deposit outlived the backend's 10-second client
    /// timeout past ~27 addresses; 20 keeps a chunk at ~7 s.
    /// </summary>
    public int? BalanceSnapshotChunkSize { get; init; }

    /// <summary>
    /// T152 — the network's signing service binds a request id
    /// (<c>BlockchainTransaction.Id</c>) to ONE signed transaction and answers
    /// every repeat of that id with the same bytes (Ethereum's transaction
    /// journal, 08 §3.7.6). For such a network a transient broadcast failure
    /// (timeout, lost connection, 5xx) never turns the row <c>FAILED</c>: the
    /// dispatcher keeps retrying with the same id, because the first attempt
    /// may already have been signed and a re-send cannot pay twice — while a
    /// <c>FAILED</c> row invites a manual re-send under a NEW id that could
    /// (proje sahibi kararı 2026-10-18). Only the service's permanent refusals
    /// (400) end a row. TRON's service has no such journal: <c>false</c>.
    /// </summary>
    public bool OutboundSendsAreIdempotent { get; init; }

    /// <summary>The policy of <paramref name="network"/>. Throws for a value no policy exists for.</summary>
    public static BlockchainNetworkPolicy For(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => Tron,
        BlockchainNetwork.ETHEREUM => Ethereum,
        _ => throw new ArgumentOutOfRangeException(
            nameof(network), network, "No chain policy is defined for this network."),
    };

    /// <summary>
    /// Whether a transfer the network's service reports is final. A
    /// <see cref="ConfirmationRule.BlockCount"/> network counts blocks and
    /// ignores <paramref name="finalized"/>; a
    /// <see cref="ConfirmationRule.Finalized"/> network accepts only an explicit
    /// <c>finalized == true</c> — a block count alone, however high, is not the
    /// kabul kuralı (11 §F8 K5).
    /// </summary>
    public bool IsFinal(int? confirmations, bool? finalized) => Confirmation switch
    {
        ConfirmationRule.BlockCount => confirmations is { } count && count >= RequiredConfirmations!.Value,
        ConfirmationRule.Finalized => finalized == true,
        _ => false,
    };

    /// <summary>
    /// The <c>ConfirmationCount</c> to store on a row this network has declared
    /// final. A block-count network never stores less than its threshold — the
    /// TRON <c>CONFIRMED</c> CHECK requires it, and a service that reports a
    /// lower number for a final transfer (or none, e.g. a spam token recorded
    /// without finality) must not produce a row the database refuses. A
    /// finalized network stores what was reported (0 when nothing was).
    /// </summary>
    public int RecordedConfirmationCount(int? reported) => Confirmation switch
    {
        ConfirmationRule.BlockCount => Math.Max(reported ?? 0, RequiredConfirmations!.Value),
        _ => Math.Max(reported ?? 0, 0),
    };
}
