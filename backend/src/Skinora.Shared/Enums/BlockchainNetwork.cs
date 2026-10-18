namespace Skinora.Shared.Enums;

/// <summary>
/// The blockchain a payment moves on (06 §2.25, 11 §F8 K1). Stored as its
/// name (<c>'TRON'</c> / <c>'ETHEREUM'</c>) like every other Skinora enum.
/// </summary>
/// <remarks>
/// <see cref="TRON"/> is deliberately the first value: it is the CLR default,
/// so a row created by code that predates the network dimension (or that has
/// not been taught to choose yet) lands on the only network that existed
/// before F8. Do not reorder.
/// </remarks>
public enum BlockchainNetwork
{
    /// <summary>TRON — USDT TRC-20. Every row written before F8 is TRON.</summary>
    TRON,

    /// <summary>Ethereum — USDT ERC-20 (chainId 1; Sepolia 11155111).</summary>
    ETHEREUM,
}
