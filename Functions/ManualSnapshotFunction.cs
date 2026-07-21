using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

public class ManualSnapshotFunction
{
    private readonly ISnapshotRunner _runner;
    private readonly ILogger<ManualSnapshotFunction> _logger;

    public ManualSnapshotFunction(ISnapshotRunner runner, ILogger<ManualSnapshotFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    [Function("ManualSnapshot")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "manual-snapshot")] HttpRequestData req,
        FunctionContext executionContext,
        CancellationToken cancellationToken)
    {
        var (statusCode, body) = await InvokeAsync(cancellationToken);

        var response = req.CreateResponse(statusCode);
        await response.WriteStringAsync(body);
        return response;
    }

    public async Task<(HttpStatusCode StatusCode, string Body)> InvokeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Manual snapshot endpoint invoked.");

        try
        {
            var rowsPersisted = await _runner.RunAsync("manual-http", cancellationToken);
            if (rowsPersisted == 0)
            {
                return (HttpStatusCode.InternalServerError, "No rows were persisted during the snapshot run.");
            }

            return (HttpStatusCode.OK, "Snapshot run completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual snapshot endpoint failed.");
            return (HttpStatusCode.InternalServerError, ex.Message);
        }
    }
}
