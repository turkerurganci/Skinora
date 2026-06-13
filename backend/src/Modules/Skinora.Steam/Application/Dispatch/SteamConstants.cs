namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// CS2 trade-offer constants (06 §3.24 "Sabitler"). MVP is single-app
/// (Counter-Strike 2) so the app/context ids are pinned rather than
/// per-transaction. Used to build the <c>items[]</c> descriptor on every
/// outbound trade-offer dispatch (T106a).
/// </summary>
public static class SteamConstants
{
    /// <summary>Steam app id — 730 = Counter-Strike 2 (08 §7.2).</summary>
    public const int Cs2AppId = 730;

    /// <summary>
    /// Inventory context id for CS2 items — "2" is the standard
    /// backpack/inventory context for app 730. Steam expects this as a string.
    /// </summary>
    public const string Cs2ContextId = "2";
}
