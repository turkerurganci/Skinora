namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// CS2 identifiers used when reading Steam inventories (06 §3.24 "Sabitler").
/// MVP is single-app (Counter-Strike 2), so the app/context ids are pinned
/// rather than resolved per transaction.
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
