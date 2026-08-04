using StockAggregatorApp.Models;
using StockAggregatorApp.Services;
using Xunit;

namespace StockAggregator.Tests;

/// <summary>
/// The trend classifier is the one piece of the range matrix where a wrong answer
/// still looks plausible on screen, so the boundary cases are pinned here.
/// </summary>
public class TrendSummaryTests
{
    private static IReadOnlyList<decimal?> Closes(params decimal?[] values) => values;

    [Fact]
    public void FewerThanTwoCloses_IsUnknown()
    {
        var summary = RangeMatrixQueryService.Summarise(Closes(100m, null, null));

        Assert.Equal(TrendDirection.Unknown, summary.Direction);
        Assert.Null(summary.NetPct);
        Assert.Null(summary.PositionInBand);
    }

    [Fact]
    public void SteadyClimb_IsUp_WithNetAndPositionAtTop()
    {
        var summary = RangeMatrixQueryService.Summarise(Closes(100m, 102m, 104m, 106m, 110m));

        Assert.Equal(TrendDirection.Up, summary.Direction);
        Assert.Equal(10m, summary.NetPct);
        Assert.Equal(10m, summary.BandPct);
        // Last close is the high, so it sits at the top of the band: momentum, not reversion.
        Assert.Equal(100m, summary.PositionInBand);
    }

    [Fact]
    public void SteadyDecline_IsDown_WithPositionAtBottom()
    {
        var summary = RangeMatrixQueryService.Summarise(Closes(110m, 106m, 103m, 99m));

        Assert.Equal(TrendDirection.Down, summary.Direction);
        Assert.True(summary.NetPct < -2m);
        Assert.Equal(0m, summary.PositionInBand);
    }

    [Fact]
    public void SmallNetMove_IsChanneling_EvenThoughEndpointsDiffer()
    {
        // Drifts a little but never decisively: under the 2% floor.
        var summary = RangeMatrixQueryService.Summarise(Closes(100m, 101m, 99m, 100m, 101m));

        Assert.Equal(TrendDirection.Channeling, summary.Direction);
    }

    [Fact]
    public void OscillationEndingHigh_IsChanneling_NotAnUptrend()
    {
        // Net +4% clears the 2% floor, but the path swung 100 -> 112 -> 96 -> 104:
        // a 16.7% band. The net covers well under half of it, so this is a symbol
        // mean-reverting inside a channel, not a trend.
        var summary = RangeMatrixQueryService.Summarise(Closes(100m, 112m, 96m, 104m));

        Assert.Equal(TrendDirection.Channeling, summary.Direction);
        Assert.Equal(4m, summary.NetPct);
        Assert.True(summary.BandPct > 16m);
    }

    [Fact]
    public void ChannelClosingNearItsLow_ReportsLowPositionInBand()
    {
        // The reversion setup worth surfacing: choppy, and currently pinned to the bottom.
        var summary = RangeMatrixQueryService.Summarise(Closes(100m, 108m, 104m, 96m, 97m));

        Assert.Equal(TrendDirection.Channeling, summary.Direction);
        Assert.True(summary.PositionInBand < 15m);
    }

    [Fact]
    public void GapsAreSkipped_ChangeMeasuredOnPopulatedClosesOnly()
    {
        var withGaps = RangeMatrixQueryService.Summarise(Closes(100m, null, 105m, null, 110m));
        var without = RangeMatrixQueryService.Summarise(Closes(100m, 105m, 110m));

        Assert.Equal(without.NetPct, withGaps.NetPct);
        Assert.Equal(without.Direction, withGaps.Direction);
    }

    [Fact]
    public void FlatLine_IsChanneling_WithoutDividingByZero()
    {
        var summary = RangeMatrixQueryService.Summarise(Closes(50m, 50m, 50m));

        Assert.Equal(TrendDirection.Channeling, summary.Direction);
        Assert.Equal(0m, summary.NetPct);
        Assert.Equal(0m, summary.BandPct);
        Assert.Equal(50m, summary.PositionInBand);
    }
}
