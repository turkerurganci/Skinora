namespace Skinora.Shared.Enums;

/// <summary>
/// Action chosen by the admin when releasing an emergency hold (07 §9.22 AD19c).
/// </summary>
public enum EmergencyHoldReleaseAction
{
    /// <summary>Resume the transaction at <c>PreviousStatusBeforeHold</c>; timeout deadlines extend by the frozen remainder.</summary>
    RESUME,

    /// <summary>Cancel the transaction (forbidden when <c>PreviousStatusBeforeHold = ITEM_DELIVERED</c>).</summary>
    CANCEL,
}
