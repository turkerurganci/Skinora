using System.Text;

namespace Skinora.Shared.Telegram;

/// <summary>
/// Escapes the 18 reserved characters required by Telegram's
/// <c>MarkdownV2</c> parse mode (08 §5.2 / Telegram Bot API docs
/// "MarkdownV2 style"). User-generated or item-derived strings — item
/// names, usernames, transaction notes — must pass through this helper
/// before they are embedded in a message; otherwise a stray <c>_</c> /
/// <c>.</c> / <c>!</c> turns the whole <c>sendMessage</c> call into a
/// permanent 400.
/// </summary>
/// <remarks>
/// <para>
/// Reserved characters per the Telegram docs:
/// <c>_ * [ ] ( ) ~ ` &gt; # + - = | { } . !</c>
/// </para>
/// <para>
/// The escaper is intentionally allow-list-free — any of the 18
/// characters is prefixed with <c>\</c> regardless of context. Code
/// spans and pre-formatted blocks have stricter rules; the channel
/// handler doesn't emit either so we don't need the special-cases here.
/// </para>
/// </remarks>
public static class MarkdownV2Escaper
{
    private static readonly HashSet<char> Reserved = new()
    {
        '_', '*', '[', ']', '(', ')', '~', '`', '>',
        '#', '+', '-', '=', '|', '{', '}', '.', '!',
        '\\',
    };

    /// <summary>
    /// Returns <paramref name="value"/> with every MarkdownV2 reserved
    /// character prefixed by a backslash. <c>null</c> returns an empty
    /// string; an already-empty string returns the same instance.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            if (Reserved.Contains(ch))
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
