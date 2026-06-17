namespace Skinora.Shared.Enums;

public enum DisputeStatus
{
    OPEN,
    ESCALATED,
    CLOSED,

    // --- Admin resolution outcomes (WP5 / T58 admin dispute resolve) ---
    // Terminal states set only by the admin resolve path (AdminId/AdminNote/
    // ResolvedAt required). CLOSED stays reserved for system auto-resolution
    // (auto-checker / submit-txhash). 03 §6.4 / 02 §10.4.
    RESOLVED_FOR_SELLER,
    RESOLVED_FOR_BUYER
}
