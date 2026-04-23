namespace Skinora.Users.Application.Profiles;

/// <summary>
/// Renders a user's account age relative to now as a short Turkish string —
/// "3 gün", "6 ay", "2 yıl" — matching 07 §5.1 sample. The API emits Turkish
/// verbatim; localisation is a T97 (i18n) concern.
/// </summary>
public static class AccountAgeFormatter
{
    public static string Format(DateTime createdAt, DateTime now)
    {
        var span = now - createdAt;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        var days = (int)Math.Floor(span.TotalDays);
        if (days < 30) return $"{days} gün";

        var months = days / 30;
        if (months < 12) return $"{months} ay";

        var years = months / 12;
        return $"{years} yıl";
    }
}
