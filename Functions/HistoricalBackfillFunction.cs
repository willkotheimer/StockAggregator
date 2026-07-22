using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// On-demand historical backfill. Pulls ~2 years of daily OHLC per symbol from
/// Yahoo into dbo.DailyOhlc. Throttled + resumable, so it's safe to call
/// repeatedly: each call processes the next batch of not-yet-done symbols and
/// reports how many remain. Trigger it yourself (POST); there's no timer.
///
///   POST /api/backfill                     -> all outstanding symbols, range=2y
///   POST /api/backfill?range=2y&batchSize=25   -> 25 symbols, then call again
///   POST /api/backfill?force=true          -> reprocess every symbol
/// </summary>
public class HistoricalBackfillFunction
{
    private readonly HistoricalBackfillService _backfill;
    private readonly ILogger<HistoricalBackfillFunction> _logger;

    public HistoricalBackfillFunction(HistoricalBackfillService backfill, ILogger<HistoricalBackfillFunction> logger)
    {
        _backfill = backfill;
        _logger = logger;
    }

    [Function("HistoricalBackfill_Run")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "backfill")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(req.Url.Query);

        var range = query.TryGetValue("range", out var r) && !string.IsNullOrWhiteSpace(r) ? r : "2y";
        var batchSize = query.TryGetValue("batchSize", out var bs) && int.TryParse(bs, out var b) && b > 0
            ? b
            : int.MaxValue;
        var force = query.TryGetValue("force", out var fs) && bool.TryParse(fs, out var f) && f;

        var result = await _backfill.RunAsync(range, batchSize, force, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
        }), cancellationToken);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        return response;
    }

    /// <summary>Minimal query-string parser (avoids a System.Web dependency).</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
            else
            {
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return result;
    }
}
