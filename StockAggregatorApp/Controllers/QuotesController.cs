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
}
