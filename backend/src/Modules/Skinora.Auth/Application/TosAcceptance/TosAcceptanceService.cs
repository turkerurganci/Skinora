using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.TosAcceptance;

public sealed class TosAcceptanceService : ITosAcceptanceService
{
    private const int TosVersionMaxLength = 20;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public TosAcceptanceService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TosAcceptanceResult> AcceptAsync(
        Guid userId, string tosVersion, bool ageOver18, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(tosVersion))
            failures.Add(new ValidationFailure(nameof(tosVersion), "tosVersion is required."));
        else if (tosVersion.Length > TosVersionMaxLength)
            failures.Add(new ValidationFailure(nameof(tosVersion),
                $"tosVersion must not exceed {TosVersionMaxLength} characters."));

        if (!ageOver18)
            failures.Add(new ValidationFailure(nameof(ageOver18),
                "ageOver18 must be true — platform is restricted to users 18 years or older."));

        if (failures.Count > 0)
            throw new ValidationException(failures);

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException($"User {userId} not found.");

        // WP11 — version-aware acceptance (T30 reprompt, 07 §4.4). Re-accepting
        // the SAME version is a true duplicate no-op → 409. A DIFFERENT version
        // is a re-acceptance after a ToS version bump → allowed: the accepted
        // version + timestamp are re-stamped, but the original 18+ attestation
        // (AgeConfirmedAt) is the first-acceptance age gate and is preserved.
        if (user.TosAcceptedAt is not null
            && string.Equals(user.TosAcceptedVersion, tosVersion, StringComparison.Ordinal))
        {
            throw new DomainException("TOS_ALREADY_ACCEPTED",
                "This Terms of Service version has already been accepted.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        user.TosAcceptedVersion = tosVersion;
        user.TosAcceptedAt = now;
        user.AgeConfirmedAt ??= now;

        await _db.SaveChangesAsync(cancellationToken);

        return new TosAcceptanceResult(now);
    }
}
