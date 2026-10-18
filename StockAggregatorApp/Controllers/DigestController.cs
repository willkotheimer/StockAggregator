using Microsoft.AspNetCore.Mvc;
using StockAggregatorApp.Services.Digest;

namespace StockAggregatorApp.Controllers;

/// <summary>
/// The midday digest email. Deliberately NOT output-cached: the Functions app calls
/// it right after saving a fresh snapshot, and a cached body from earlier in the day
/// would send stale prices.
/// </summary>
[ApiController]
public sealed class DigestController : ControllerBase
{
    private readonly IDigestQueryService _digest;

    public DigestController(IDigestQueryService digest)
    {
        _digest = digest;
    }

    /// <summary>
    /// The digest as data: subject, HTML and text bodies, plus the content and signals
    /// they were built from. Defaults to the latest day with snapshots. This is what the
    /// DailyDigest function sends, and it includes the operator's data-check line.
    /// </summary>
    [HttpGet("api/digest")]
    public async Task<ActionResult<DigestResponse>> Get(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _digest.GetDigestAsync(date, includeDataCheck: true, cancellationToken));
    }

    /// <summary>
    /// The email exactly as it would arrive, for checking in a browser. Lives outside
    /// /api so it opens without the API-key header; the data check is left off because
    /// this page is public like the rest of the dashboard.
    /// </summary>
    [HttpGet("digest/preview")]
    public async Task<ContentResult> Preview(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken = default)
    {
        var digest = await _digest.GetDigestAsync(date, includeDataCheck: false, cancellationToken);
        return Content(digest.Html, "text/html; charset=utf-8");
    }
}
