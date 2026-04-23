namespace Skinora.Users.Application.Settings;

/// <summary>
/// Updates the user's <c>PreferredLanguage</c> field (07 §5.10). Valid codes
/// are the four MVP languages: <c>en</c>, <c>zh</c>, <c>es</c>, <c>tr</c>.
/// </summary>
public interface ILanguageService
{
    Task<LanguageUpdateResult> UpdateAsync(
        Guid userId, string? language, CancellationToken cancellationToken);
}

public enum LanguageUpdateStatus
{
    Success,
    UserNotFound,
    InvalidLanguage,
}

public sealed record LanguageUpdateResult(LanguageUpdateStatus Status, string? Language)
{
    public static LanguageUpdateResult Success(string language)
        => new(LanguageUpdateStatus.Success, language);

    public static LanguageUpdateResult Failure(LanguageUpdateStatus status)
        => new(status, null);
}
