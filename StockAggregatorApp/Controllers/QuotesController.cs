using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Models;
using StockAggregatorApp.Services;

namespace StockAggregatorApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class QuotesController : ControllerBase
{
    private readonly IQuoteQueryService _service;

    public QuotesController(IQuoteQueryService service)
    {
        _service = service;
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
}
