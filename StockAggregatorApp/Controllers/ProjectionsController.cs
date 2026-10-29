using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Services.Projection;

namespace StockAggregatorApp.Controllers;

/// <summary>
/// The projection test's scorecard: how often each pattern, alone or paired, placed the
/// price 20 trading days ahead inside the window, against the no-pattern baseline.
/// Not output-cached; <see cref="ProjectionScorecardCache"/> already keeps one result per trading day.
/// </summary>
[ApiController]
[Route("api/projections")]
public sealed class ProjectionsController : ControllerBase
{
    private readonly ProjectionScorecardCache _scorecard;

    public ProjectionsController(ProjectionScorecardCache scorecard)
    {
        _scorecard = scorecard;
    }

    [HttpGet]
    public async Task<ActionResult<ProjectionScorecard>> Get(CancellationToken cancellationToken = default) =>
        Ok(await _scorecard.GetAsync(cancellationToken));

    /// <summary>Today's score and status for every variant of every thesis, flat. The Functions app stores it daily.</summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<IReadOnlyList<ThesisLedgerEntry>>> GetLedger(CancellationToken cancellationToken = default) =>
        Ok(ThesisLedger.Entries(await _scorecard.GetAsync(cancellationToken)));
}
