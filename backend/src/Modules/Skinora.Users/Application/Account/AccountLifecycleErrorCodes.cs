namespace Skinora.Users.Application.Account;

/// <summary>
/// Error codes for account deactivate/delete endpoints (07 §5.17).
/// </summary>
public static class AccountLifecycleErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string HasActiveTransactions = "HAS_ACTIVE_TRANSACTIONS";
}
