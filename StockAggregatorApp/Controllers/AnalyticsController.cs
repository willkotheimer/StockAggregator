using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Models;
using StockAggregatorApp.Services;

namespace StockAggregatorApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsQueryService _service;
    private readonly IReboundQueryService _rebound;
    private readonly IRangeQueryService _ranges;

    public AnalyticsController(IAnalyticsQueryService service, IReboundQueryService rebound, IRangeQueryService ranges)
    {
        _service = service;
        _rebound = rebound;
        _ranges = ranges;
    }

    /// <summary>ETFs rising while most tracked members are flat/negative (defaults to the latest day).</summary>
    [HttpGet("hidden-signal")]
    public async Task<ActionResult<HiddenSignalResponse>> GetHiddenSignal(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetHiddenSignalsAsync(date, cancellationToken));
    }

    /// <summary>Sector ETFs ranked by daily change — the rotation leaderboard.</summary>
    [HttpGet("rotations")]
    public async Task<ActionResult<RotationResponse>> GetRotations(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetRotationsAsync(date, cancellationToken));
    }

    /// <summary>The ETF groups + their member symbols — drives the Rebound tab's pill rows.</summary>
    [HttpGet("etf-groups")]
    public ActionResult<IReadOnlyList<EtfGroupDto>> GetEtfGroups() => Ok(_rebound.GetEtfGroups());

    /// <summary>
    /// Historical rebound base rates for one symbol from the backfilled daily history.
    /// mode=trough (drawdown → recovery) or mode=surge (run-up → pullback);
    /// threshold is the minimum move (%) that opens an episode.
    /// </summary>
    [HttpGet("rebound/{symbol}")]
    public async Task<ActionResult<ReboundResponse>> GetRebound(
        string symbol,
        [FromQuery] string mode = "trough",
        [FromQuery] decimal threshold = 10m,
        CancellationToken cancellationToken = default)
    {
        var reboundMode = string.Equals(mode, "surge", StringComparison.OrdinalIgnoreCase)
            ? ReboundMode.Surge
            : ReboundMode.Trough;

        // Clamp to a sane band so a bad query can't ask for a 0% or absurd threshold.
        threshold = Math.Clamp(threshold, 1m, 90m);

        return Ok(await _rebound.GetReboundAsync(symbol, reboundMode, threshold, cancellationToken));
    }

    /// <summary>
    /// Range / volatility profile for an ETF and its members — typical daily/weekly
    /// range, up-day stats, and typical gain before a pullback (%). The profit-taking scanner.
    /// </summary>
    [HttpGet("ranges/{etf}")]
    public async Task<ActionResult<RangeResponse>> GetRanges(
        string etf,
        [FromQuery] decimal pullback = 5m,
        CancellationToken cancellationToken = default)
    {
        pullback = Math.Clamp(pullback, 1m, 50m);
        var result = await _ranges.GetRangesAsync(etf, pullback, cancellationToken);
        return result is null ? NotFound($"Unknown ETF '{etf}'.") : Ok(result);
    }
}
