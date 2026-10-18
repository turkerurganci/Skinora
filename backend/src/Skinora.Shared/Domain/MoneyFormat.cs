using System.Globalization;

namespace Skinora.Shared.Domain;

/// <summary>
/// Text form of a USDT amount shown to a user (T144; 07 §2.8, 09 §7.2).
/// </summary>
/// <remarks>
/// <para>
/// The format is <c>0.00####</c> with the invariant culture: at least two
/// decimals, and every non-zero digit up to the storage scale of six is kept.
/// It never rounds. For a whole-cent amount — every amount the buyer is asked
/// to send since T144 — the output is exactly the 2-decimal API form
/// (<c>"10.76"</c>, <c>"102.00"</c>). An amount read from the chain can carry
/// more digits (<c>10.761</c>); it is shown as received rather than rounded
/// into a value the buyer never sent.
/// </para>
/// <para>
/// Use it wherever a user reads an amount they must send or that was sent
/// to them: the payable fields of the transaction detail and the notification
/// texts. A rounded display of a payable amount is exactly the T144 defect —
/// the screen said "10.76" while validation expected 10.761.
/// </para>
/// </remarks>
public static class MoneyFormat
{
    private const string DisplayPattern = "0.00####";

    /// <summary>Formats <paramref name="amount"/> per the rule above.</summary>
    public static string Display(decimal amount) =>
        amount.ToString(DisplayPattern, CultureInfo.InvariantCulture);
}
