using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.PayoutIssues;

// ---------- POST /transactions/:id/report-payout-issue (07 §7.11) ----------

/// <summary>Request body for <c>POST /transactions/:id/report-payout-issue</c>.</summary>
public sealed record ReportPayoutIssueRequest(string Detail);

/// <summary>
/// Response body for <c>POST /transactions/:id/report-payout-issue</c>
/// (07 §7.11). The <see cref="Status"/> reflects the post-verification state
/// the row landed in during the same request — REPORTED is only observed when
/// the verifier deferred to the retry pipeline (RETRY_SCHEDULED) without a
/// terminal outcome.
/// </summary>
public sealed record ReportPayoutIssueResponse(
    Guid IssueId,
    PayoutIssueStatus Status,
    DateTime CreatedAt,
    string Message);

/// <summary>
/// Outcome of <see cref="IPayoutIssueService.ReportAsync"/>. The controller
/// pattern matches on <see cref="Status"/> to produce 201 / 4xx responses
/// without leaking implementation details.
/// </summary>
public sealed record ReportPayoutIssueOutcome(
    ReportPayoutIssueStatus Status,
    ReportPayoutIssueResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum ReportPayoutIssueStatus
{
    Reported,
    NotFound,
    NotSeller,
    TransactionNotCompleted,
    IssueAlreadyReported,
    ValidationFailed,
}
