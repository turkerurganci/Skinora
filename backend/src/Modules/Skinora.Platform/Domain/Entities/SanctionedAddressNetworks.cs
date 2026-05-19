namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Allowed values for <see cref="SanctionedAddress.Network"/> — enforced by
/// CHECK constraint in <c>SanctionedAddressConfiguration</c>. MVP yalnız
/// <c>TRC-20</c>; diğer ağlar T-future.
/// </summary>
public static class SanctionedAddressNetworks
{
    public const string Trc20 = "TRC-20";

    public static readonly IReadOnlyList<string> All = new[] { Trc20 };

    public static bool IsKnown(string? value) =>
        !string.IsNullOrEmpty(value) && All.Contains(value);
}
