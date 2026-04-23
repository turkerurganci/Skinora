using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class LanguageService : ILanguageService
{
    // 07 §5.10 — MVP languages only. The enum-like check keeps the column
    // content stable for i18n resource keys without introducing a DB enum.
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { "en", "zh", "es", "tr" };

    private readonly AppDbContext _db;

    public LanguageService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<LanguageUpdateResult> UpdateAsync(
        Guid userId, string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(language) || !Allowed.Contains(language))
            return LanguageUpdateResult.Failure(LanguageUpdateStatus.InvalidLanguage);

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return LanguageUpdateResult.Failure(LanguageUpdateStatus.UserNotFound);

        var normalized = language.ToLowerInvariant();
        user.PreferredLanguage = normalized;
        await _db.SaveChangesAsync(cancellationToken);
        return LanguageUpdateResult.Success(normalized);
    }
}
