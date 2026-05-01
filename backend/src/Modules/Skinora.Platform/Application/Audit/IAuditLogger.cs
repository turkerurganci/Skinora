namespace Skinora.Platform.Application.Audit;

/// <summary>
/// Central audit log writer (09 §18.6). All <c>AuditLog</c> rows are emitted
/// via this interface — direct <c>DbContext.Set&lt;AuditLog&gt;().Add</c>
/// calls are prohibited (06 §8.6a actor invariant cannot be enforced
/// otherwise).
/// </summary>
/// <remarks>
/// The logger only stages an Added entity on the unit-of-work — the caller
/// owns the surrounding transaction and is responsible for
/// <c>SaveChangesAsync</c>. This keeps audit writes atomic with the business
/// change that produced them; callers must NOT call SaveChanges separately.
/// </remarks>
public interface IAuditLogger
{
    /// <summary>
    /// Stage an audit row on the current <c>AppDbContext</c> change tracker.
    /// Throws <see cref="InvalidActorException"/> when the actor invariant
    /// (06 §8.6a) is violated.
    /// </summary>
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when <see cref="IAuditLogger"/> is called with an actor that
/// violates 06 §8.6a (e.g. <c>ActorType.SYSTEM</c> with a non-system Guid,
/// or <c>USER</c>/<c>ADMIN</c> with <see cref="Guid.Empty"/>).
/// </summary>
public sealed class InvalidActorException : InvalidOperationException
{
    public InvalidActorException(string message) : base(message) { }
}
