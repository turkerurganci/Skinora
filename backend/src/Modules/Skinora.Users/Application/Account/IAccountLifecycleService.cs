namespace Skinora.Users.Application.Account;

/// <summary>
/// Deactivate + delete flows for <c>/users/me</c> (07 §5.17, 02 §19, 06 §6.2).
/// </summary>
public interface IAccountLifecycleService
{
    Task<AccountDeactivateOutcome> DeactivateAsync(
        Guid userId, CancellationToken cancellationToken);

    Task<AccountDeleteOutcome> DeleteAsync(
        Guid userId, string? confirmation, CancellationToken cancellationToken);
}

public abstract record AccountDeactivateOutcome
{
    public sealed record Success(DateTime DeactivatedAt) : AccountDeactivateOutcome;
    public sealed record UserNotFound : AccountDeactivateOutcome;
    public sealed record HasActiveTransactions : AccountDeactivateOutcome;
}

public abstract record AccountDeleteOutcome
{
    public sealed record Success(DateTime DeletedAt) : AccountDeleteOutcome;
    public sealed record UserNotFound : AccountDeleteOutcome;
    public sealed record HasActiveTransactions : AccountDeleteOutcome;
    public sealed record ConfirmationInvalid : AccountDeleteOutcome;
}
