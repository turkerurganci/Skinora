namespace Skinora.Fraud.Application.Flags;

/// <summary>Result of a <c>FraudFlagService.ApproveAsync</c> call.</summary>
public abstract record ApproveFlagOutcome
{
    private ApproveFlagOutcome() { }

    /// <summary>Approval committed (transaction promoted to <c>CREATED</c> when applicable).</summary>
    public sealed record Success(FraudFlagReviewResultDto Result) : ApproveFlagOutcome;

    /// <summary>Flag id not found.</summary>
    public sealed record NotFound : ApproveFlagOutcome;

    /// <summary>Flag has already been reviewed (idempotency / 409).</summary>
    public sealed record AlreadyReviewed : ApproveFlagOutcome;

    /// <summary>Linked transaction is no longer in <c>FLAGGED</c> state.</summary>
    public sealed record TransactionNotFlagged : ApproveFlagOutcome;
}

/// <summary>Result of a <c>FraudFlagService.RejectAsync</c> call.</summary>
public abstract record RejectFlagOutcome
{
    private RejectFlagOutcome() { }

    /// <summary>Rejection committed (transaction transitioned to <c>CANCELLED_ADMIN</c> when applicable).</summary>
    public sealed record Success(FraudFlagReviewResultDto Result) : RejectFlagOutcome;

    /// <summary>Flag id not found.</summary>
    public sealed record NotFound : RejectFlagOutcome;

    /// <summary>Flag has already been reviewed.</summary>
    public sealed record AlreadyReviewed : RejectFlagOutcome;

    /// <summary>Linked transaction is no longer in <c>FLAGGED</c> state.</summary>
    public sealed record TransactionNotFlagged : RejectFlagOutcome;
}
