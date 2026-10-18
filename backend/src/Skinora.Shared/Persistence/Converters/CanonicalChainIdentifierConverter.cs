using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Skinora.Shared.Blockchain;

namespace Skinora.Shared.Persistence.Converters;

/// <summary>
/// Stores an on-chain identifier (address, contract, transaction hash) in its
/// single canonical form (06 §1.4): EVM <c>0x</c>-hex lowercased, TRON values
/// byte-for-byte unchanged. See <see cref="BlockchainAddress"/>.
/// </summary>
/// <remarks>
/// <para>
/// Applying it at the column — rather than trusting every writer — is what makes
/// "one stored form" a property of the schema instead of a convention: a new
/// code path cannot persist a mixed-case EVM address by forgetting to
/// normalise it. EF also converts a parameter compared against a converted
/// column, so <c>Where(x =&gt; x.Address == input)</c> sends the canonical
/// spelling of <c>input</c> regardless of the database collation.
/// </para>
/// <para>
/// The read side canonicalises as well so a row written before this converter
/// existed still materialises in canonical form. No such EVM row exists (F8
/// adds the first network that has one), and TRON values are an identity on
/// both sides.
/// </para>
/// </remarks>
public sealed class CanonicalChainIdentifierConverter : ValueConverter<string, string>
{
    public static readonly CanonicalChainIdentifierConverter Instance = new();

    public CanonicalChainIdentifierConverter()
        : base(
            v => BlockchainAddress.Canonicalize(v),
            v => BlockchainAddress.Canonicalize(v))
    {
    }
}
