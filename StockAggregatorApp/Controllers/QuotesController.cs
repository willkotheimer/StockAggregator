using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using StockAggregatorApp.Models;
using StockAggregatorApp.Services;

namespace StockAggregatorApp.Controllers;

[ApiController]
[Route("api/[controller]")]
[OutputCache(PolicyName = "api")]
public sealed class QuotesController : ControllerBase
{
    private readonly IQuoteQueryService _service;
    private readonly IRangeMatrixQueryService _matrix;

    public QuotesController(IQuoteQueryService service, IRangeMatrixQueryService matrix)
    {
        _service = service;
        _matrix = matrix;
    }

    /// <summary>
    /// The most recent trading days of snapshots as a grid (default 5 = one week).
    /// Read-only; there is deliberately no endpoint to create or modify quotes.
    /// </summary>
    [HttpGet("week")]
    public async Task<ActionResult<WeekQuotesResponse>> GetWeek(
        [FromQuery] int days = 5,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetWeekAsync(days, cancellationToken);
        return Ok(result);
    }

    /// <summary>Trading dates (yyyy-MM-dd) that have captured quotes — for the calendar picker.</summary>
    [HttpGet("available-dates")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetAvailableDates(CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetAvailableDatesAsync(cancellationToken));
    }

    /// <summary>Snapshots + rows for specific days, e.g. ?dates=2026-07-20,2026-07-21.</summary>
    [HttpGet("days")]
    public async Task<ActionResult<WeekQuotesResponse>> GetDays(
        [FromQuery] string dates,
        CancellationToken cancellationToken = default)
    {
        var parsed = (dates ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d.Date : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .Take(31)
            .ToList();

        return Ok(await _service.GetDaysAsync(parsed, cancellationToken));
    }

    /// <summary>
    /// Trading dates present in the backfilled daily history — the selectable set for
    /// range mode. Much wider than <c>available-dates</c>, which only covers days the
    /// intraday snapshot capture actually ran.
    /// </summary>
    [HttpGet("trading-dates")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetTradingDates(CancellationToken cancellationToken = default)
    {
        return Ok(await _matrix.GetTradingDatesAsync(cancellationToken));
    }

    /// <summary>
    /// Close-only symbol x date grid over a range, e.g. ?from=2026-01-02&amp;to=2026-06-30.
    /// Optional dow (0=Sun..6=Sat) keeps just that weekday — dow=5 gives Fridays, and
    /// the per-column change then reads week-over-week.
    /// </summary>
    [HttpGet("matrix")]
    public async Task<ActionResult<RangeMatrixResponse>> GetMatrix(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] int? dow = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseDate(from, out var fromDate) || !TryParseDate(to, out var toDate))
        {
            return BadRequest("from and to must be yyyy-MM-dd dates.");
        }

        if (dow is < 0 or > 6)
        {
            return BadRequest("dow must be 0 (Sunday) through 6 (Saturday).");
        }

        return Ok(await _matrix.GetMatrixAsync(fromDate, toDate, dow, cancellationToken));
    }

    private static bool TryParseDate(string? value, out DateTime date)
    {
        return DateTime.TryParse(
            value ?? string.Empty,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);
    }
}
