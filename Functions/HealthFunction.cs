using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StockAggregator.Services;

namespace StockAggregator.Functions;

/// <summary>
/// Lightweight health probe. Confirms the app is up and the database is reachable
/// (an Entra-authenticated <c>SELECT 1</c>) without writing any rows — so the
/// post-deploy smoke test can verify a deployment without polluting the data.
/// </summary>
public class HealthFunction
{
    private readonly QuoteRepository _repository;
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(QuoteRepository repository, ILogger<HealthFunction> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.CheckDatabaseAsync(cancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync("Healthy");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed.");

            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Unhealthy: {ex.Message}");
            return response;
        }
    }
}
