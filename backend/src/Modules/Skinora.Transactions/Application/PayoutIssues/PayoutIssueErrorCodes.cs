namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// Error code constants surfaced by the T60 payout-issue pipeline. Mirrors
/// the strings listed under 07 §7.11 "Hatalar".
/// </summary>
public static class PayoutIssueErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string TransactionNotFound = "TRANSACTION_NOT_FOUND";
    public const string NotSeller = "NOT_SELLER";
    public const string TransactionNotCompleted = "TRANSACTION_NOT_COMPLETED";
    public const string IssueAlreadyReported = "ISSUE_ALREADY_REPORTED";
}
