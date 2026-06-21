namespace Skinora.Shared.Notifications;

/// <summary>
/// Composes a <c>{boldOpen}{title}{boldClose}\n\n{body}</c> notification message
/// whose length never exceeds a channel's hard limit (Discord 2000 / Telegram
/// 4096 — 08 §6.2). The title/body are escaped for the channel's Markdown
/// dialect, and when the composed message would overflow, the RAW text is
/// truncated (with an ellipsis) BEFORE escaping.
/// </summary>
/// <remarks>
/// Truncating the raw text and re-escaping is what makes this safe: truncating an
/// already-escaped string could split a <c>\X</c> escape pair, leaving a dangling
/// trailing backslash — itself a reserved char that triggers a Telegram 400
/// "can't parse entities" and (since the channel handlers map that to a permanent
/// failure) auto-disables the user's notification preference. The bold markers are
/// always appended last, so they cannot be truncated away either.
/// </remarks>
public static class BoldHeaderMessageComposer
{
    private const string Ellipsis = "…"; // U+2026 — not reserved in either Markdown dialect

    public static string Compose(
        string title,
        string body,
        int maxLength,
        Func<string?, string> escape,
        string boldOpen,
        string boldClose)
    {
        var escapedTitle = escape(title);
        var escapedBody = escape(body);
        var overhead = boldOpen.Length + boldClose.Length + 2; // the "\n\n" separator

        if (overhead + escapedTitle.Length + escapedBody.Length <= maxLength)
        {
            return $"{boldOpen}{escapedTitle}{boldClose}\n\n{escapedBody}";
        }

        var budget = maxLength - overhead; // shared by the escaped title + body
        string finalTitle;
        string finalBody;
        if (escapedTitle.Length >= budget)
        {
            // The title alone overflows — truncate it and drop the body.
            finalTitle = FitEscaped(title, budget, escape);
            finalBody = string.Empty;
        }
        else
        {
            finalTitle = escapedTitle;
            finalBody = FitEscaped(body, budget - escapedTitle.Length, escape);
        }

        return $"{boldOpen}{finalTitle}{boldClose}\n\n{finalBody}";
    }

    /// <summary>
    /// Escapes the longest prefix of <paramref name="raw"/> whose escaped form
    /// (plus an ellipsis) fits in <paramref name="escapedBudget"/>. Because the
    /// raw text is truncated and re-escaped as a whole, the result can never end in
    /// a half-written escape sequence.
    /// </summary>
    private static string FitEscaped(string raw, int escapedBudget, Func<string?, string> escape)
    {
        var escaped = escape(raw);
        if (escaped.Length <= escapedBudget)
        {
            return escaped;
        }

        var escapedEllipsis = escape(Ellipsis);
        var target = escapedBudget - escapedEllipsis.Length;
        if (target <= 0)
        {
            return string.Empty;
        }

        // Escaping never shrinks a string, so the raw prefix that fits is at most
        // `target` chars — start there and shrink until the escaped form fits.
        var length = Math.Min(raw.Length, target);
        while (length > 0 && escape(raw[..length]).Length > target)
        {
            length--;
        }

        return escape(raw[..length]) + escapedEllipsis;
    }
}
