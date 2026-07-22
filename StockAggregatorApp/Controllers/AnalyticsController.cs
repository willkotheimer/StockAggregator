using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Models;
using StockAggregatorApp.Services;

namespace StockAggregatorApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsQueryService _service;

    public AnalyticsController(IAnalyticsQueryService service)
    {
        _service = service;
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
}
