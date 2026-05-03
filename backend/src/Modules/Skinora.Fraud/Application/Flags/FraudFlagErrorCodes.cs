namespace Skinora.Fraud.Application.Flags;

/// <summary>
/// Stable error codes returned by <see cref="IFraudFlagService"/> and
/// <see cref="IFraudFlagAdminQueryService"/>. The HTTP layer (T54
/// AdminFlagsController) maps each code to a status (404 / 409 / 422 / 400).
/// </summary>
public static class FraudFlagErrorCodes
{
    /// <summary>Flag id was not found (07 §9.4–§9.5: 404 FLAG_NOT_FOUND).</summary>
    public const string FlagNotFound = "FLAG_NOT_FOUND";

    /// <summary>Flag has already been reviewed (07 §9.4: 409 ALREADY_REVIEWED).</summary>
    public const string AlreadyReviewed = "ALREADY_REVIEWED";

    /// <summary>Linked transaction is no longer in <c>FLAGGED</c> state.</summary>
    public const string TransactionNotFlagged = "TRANSACTION_NOT_FLAGGED";

    /// <summary>Linked transaction was not found (data integrity drift).</summary>
    public const string TransactionNotFound = "TRANSACTION_NOT_FOUND";

    /// <summary>Linked user was not found (data integrity drift).</summary>
    public const string UserNotFound = "USER_NOT_FOUND";

    /// <summary>Generic validation error (e.g. note too long).</summary>
    public const string ValidationError = "VALIDATION_ERROR";
}
