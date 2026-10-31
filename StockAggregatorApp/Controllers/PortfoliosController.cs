using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Services.Portfolios;

namespace StockAggregatorApp.Controllers;

/// <summary>
/// The practice portfolios' leaderboard: each mix's money in, value and holdings, best first,
/// with the rules applied through today's data. Cheap to compute (a month of closes), so not cached.
/// </summary>
[ApiController]
[Route("api/portfolios")]
public sealed class PortfoliosController : ControllerBase
{
    private readonly IPaperPortfolioService _portfolios;

    public PortfoliosController(IPaperPortfolioService portfolios)
    {
        _portfolios = portfolios;
    }

    [HttpGet]
    public async Task<ActionResult<PortfolioLeaderboard>> Get(CancellationToken cancellationToken = default) =>
        Ok(await _portfolios.GetAsync(cancellationToken: cancellationToken));
}
