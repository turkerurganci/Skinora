using Skinora.Users.Application.Reputation;

namespace Skinora.Users.Tests.Unit.Reputation;

/// <summary>
/// Verifies the 02 §14.1 / §13 wash-trading rule: within an unordered pair,
/// only the first transaction is counted, and each subsequent one only if it
/// is at least 1 month after the previously counted one in that pair.
/// </summary>
public class WashTradingFilterTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private record Row(Guid Pair1, Guid Pair2, DateTime When, string Tag);

    [Fact]
    public void Single_Row_Is_Counted()
    {
        var rows = new[] { new Row(A, B, T0, "x") };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.Single(result);
        Assert.True(result[0].Counted);
    }

    [Fact]
    public void Same_Pair_Within_Window_Drops_Subsequent_Rows()
    {
        var rows = new[]
        {
            new Row(A, B, T0,                        "first"),
            new Row(A, B, T0.AddDays(10),            "wash-1"),
            new Row(A, B, T0.AddDays(20),            "wash-2"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.True(result[0].Counted);
        Assert.False(result[1].Counted);
        Assert.False(result[2].Counted);
    }

    [Fact]
    public void Same_Pair_Outside_Window_Resumes_Counting()
    {
        var rows = new[]
        {
            new Row(A, B, T0,                        "first"),
            new Row(A, B, T0.AddDays(31),            "second"),
            new Row(A, B, T0.AddDays(62),            "third"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.All(result, v => Assert.True(v.Counted));
    }

    [Fact]
    public void Wash_Window_Restarts_From_Last_Counted_Not_From_First_Counted()
    {
        // Day 0 counted; day 20 dropped (within 30 of day 0); day 40 counted
        // (40 days after day 0). Then day 60 dropped (only 20 days after day 40).
        var rows = new[]
        {
            new Row(A, B, T0,                        "c1"),
            new Row(A, B, T0.AddDays(20),            "drop-near-c1"),
            new Row(A, B, T0.AddDays(40),            "c2"),
            new Row(A, B, T0.AddDays(60),            "drop-near-c2"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.True(result[0].Counted);
        Assert.False(result[1].Counted);
        Assert.True(result[2].Counted);
        Assert.False(result[3].Counted);
    }

    [Fact]
    public void Different_Pairs_Are_Tracked_Independently()
    {
        var rows = new[]
        {
            new Row(A, B, T0,                        "AB"),
            new Row(A, C, T0.AddDays(1),             "AC"),
            new Row(A, B, T0.AddDays(5),             "AB-wash"),
            new Row(A, C, T0.AddDays(40),            "AC-after-window"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.True(result[0].Counted);   // AB
        Assert.True(result[1].Counted);   // AC
        Assert.False(result[2].Counted);  // AB wash
        Assert.True(result[3].Counted);   // AC after window
    }

    [Fact]
    public void Pair_Order_Does_Not_Matter()
    {
        // (A,B) and (B,A) must collapse to the same logical pair.
        var rows = new[]
        {
            new Row(A, B, T0,                        "AB"),
            new Row(B, A, T0.AddDays(10),            "BA-wash"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        Assert.True(result[0].Counted);
        Assert.False(result[1].Counted);
    }

    [Fact]
    public void Out_Of_Order_Input_Is_Sorted_By_Timestamp_Internally()
    {
        // Caller may pass rows in arbitrary order — the filter sorts by timestamp
        // before evaluating windows, then returns results in caller order.
        var rows = new[]
        {
            new Row(A, B, T0.AddDays(20),            "wash"),
            new Row(A, B, T0,                        "first"),
        };

        var result = WashTradingFilter.Apply(rows, r => (r.Pair1, r.Pair2), r => r.When);

        // Index 0 in caller order = day 20 entry, which is the wash one.
        Assert.False(result[0].Counted);
        Assert.True(result[1].Counted);
    }
}
