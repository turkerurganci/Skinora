using System.Text;

namespace Skinora.Shared.Discord;

/// <summary>
/// Escapes the seven characters Discord's Markdown renderer treats as
/// formatting (T80 — 08 §6.2). User-generated or item-derived strings —
/// item names, usernames, transaction notes — must pass through this
/// helper before they are embedded in a DM body; otherwise a stray
/// <c>*</c> / <c>_</c> / <c>~</c> turns the message into bold / italic /
/// strikethrough and a stray <c>&gt;</c> at line start opens a block
/// quote.
/// </summary>
/// <remarks>
/// <para>
/// Reserved characters per the Discord Markdown reference:
/// <c>* _ ~ ` &gt; | \</c>. Backslash is included so a pre-escaped
/// fragment cannot smuggle an unbalanced backslash that breaks downstream
/// escaping.
/// </para>
/// <para>
/// Mention spam (<c>@everyone</c>, <c>@here</c>, role pings) is handled
/// separately by the <c>allowed_mentions: { "parse": [] }</c> payload
/// flag on every outbound message (08 §6.2); the escaper only neutralises
/// Markdown formatting.
/// </para>
/// </remarks>
public static class DiscordMarkdownEscaper
{
    private static readonly HashSet<char> Reserved = new()
    {
        '*', '_', '~', '`', '>', '|', '\\',
    };

    /// <summary>
    /// Returns <paramref name="value"/> with every Discord Markdown
    /// reserved character prefixed by a backslash. <c>null</c> returns
    /// an empty string; an already-empty string returns the empty
    /// string.
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
