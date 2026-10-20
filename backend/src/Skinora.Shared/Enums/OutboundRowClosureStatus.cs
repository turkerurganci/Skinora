namespace Skinora.Shared.Enums;

/// <summary>
/// State of an outbound-row closure proposal (T157 — 06 §3.8b). The money tool
/// is two-step: one admin proposes, a different admin approves.
/// </summary>
public enum OutboundRowClosureStatus
{
    /// <summary>Waiting for a second admin. The row is untouched.</summary>
    PROPOSED,

    /// <summary>A different admin approved; the row carries the chain's hash.</summary>
    APPROVED,

    /// <summary>Rejected or withdrawn; the row is untouched.</summary>
    REJECTED
}
