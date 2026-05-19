namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Allowed values for <see cref="SanctionedAddress.Source"/> — enforced by
/// CHECK constraint in <c>SanctionedAddressConfiguration</c>. MVP'de admin
/// yalnız <c>MANUAL</c> set'ler; <c>OFAC</c> / <c>EU</c> / <c>UN</c> auto-sync
/// (post-MVP) için reserved.
/// </summary>
public static class SanctionedAddressSources
{
    public const string Ofac = "OFAC";
    public const string Eu = "EU";
    public const string Un = "UN";
    public const string Manual = "MANUAL";

    public static readonly IReadOnlyList<string> All = new[] { Ofac, Eu, Un, Manual };

    public static bool IsKnown(string? value) =>
        !string.IsNullOrEmpty(value) && All.Contains(value);
}
