using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.Transactions.Tests.Unit.PaymentAddresses;

/// <summary>
/// Unit coverage for <see cref="StablecoinContractOptions"/> — the network-aware
/// replacement for reading <see cref="KnownStablecoinContracts"/> directly
/// (08 §3.3).
/// </summary>
/// <remarks>
/// The assertions that matter here are about the failure this class exists to
/// prevent, not about the happy path: an unconfigured deployment must still
/// resolve to mainnet, and a configured one must stop returning mainnet.
/// <para>
/// Previously <see cref="KnownStablecoinContracts.Usdt"/> was armed on EVERY
/// network. On a testnet, <c>classifyToken</c> then compared a real deposit
/// against a contract that could never match — result: <c>wrong_token</c>,
/// followed by an automatic refund of a correct payment.
/// </para>
/// </remarks>
public class StablecoinContractOptionsTests
{
    private const string NileUsdt = "TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf";

    [Fact]
    public void Unconfigured_Falls_Back_To_Mainnet()
    {
        var sut = new StablecoinContractOptions();

        Assert.Equal(KnownStablecoinContracts.Usdt, sut.ResolveContractAddress(StablecoinType.USDT));
        Assert.Equal(KnownStablecoinContracts.Usdc, sut.ResolveContractAddress(StablecoinType.USDC));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Value_Falls_Back_To_Mainnet(string configured)
    {
        // docker-compose passes ${TRON_USDT_CONTRACT:-}, so an operator who has
        // not filled the variable in reaches this path rather than an empty
        // expectedContract that would match nothing at all.
        var sut = new StablecoinContractOptions { Usdt = configured };

        Assert.Equal(KnownStablecoinContracts.Usdt, sut.ResolveContractAddress(StablecoinType.USDT));
    }

    [Fact]
    public void Configured_Address_Replaces_The_Mainnet_Constant()
    {
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Equal(NileUsdt, sut.ResolveContractAddress(StablecoinType.USDT));
        Assert.NotEqual(KnownStablecoinContracts.Usdt, sut.ResolveContractAddress(StablecoinType.USDT));
    }

    [Fact]
    public void Configuring_One_Token_Leaves_The_Other_On_Mainnet()
    {
        // TRON_USDC_CONTRACT is deliberately blank on Nile — the allowlist
        // carries USDT only until a testnet USDC address is resolved.
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Equal(KnownStablecoinContracts.Usdc, sut.ResolveContractAddress(StablecoinType.USDC));
    }

    [Fact]
    public void ResolveByContract_Recognises_The_Configured_Network()
    {
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Equal(StablecoinType.USDT, sut.ResolveByContract(NileUsdt));
    }

    [Fact]
    public void ResolveByContract_Rejects_The_Mainnet_Address_On_A_Configured_Network()
    {
        // The reverse of the arming bug: once the deployment declares its
        // network, a deposit in the mainnet contract is a token this
        // deployment does not accept, not a USDT payment.
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Null(sut.ResolveByContract(KnownStablecoinContracts.Usdt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TSomeUnknownSpamTokenContractAddress00")]
    public void ResolveByContract_Returns_Null_For_Unknown_Input(string? contractAddress)
    {
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Null(sut.ResolveByContract(contractAddress));
    }

    [Fact]
    public void ResolveByContract_Is_Case_Sensitive()
    {
        // Tron addresses are case-sensitive base58 (T70 derivation); folding
        // case would accept a near-miss address as a valid deposit.
        var sut = new StablecoinContractOptions { Usdt = NileUsdt };

        Assert.Null(sut.ResolveByContract(NileUsdt.ToUpperInvariant()));
    }

    [Fact]
    public void ResolveContractAddress_Rejects_Undefined_Enum_Value()
    {
        var sut = new StablecoinContractOptions();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sut.ResolveContractAddress((StablecoinType)999));
    }
}
