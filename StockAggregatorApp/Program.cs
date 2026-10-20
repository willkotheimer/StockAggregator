using StockAggregatorApp;
using StockAggregatorApp.Auth;
using StockAggregatorApp.Data;
using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;
using StockAggregatorApp.Services.Digest;
using StockAggregatorApp.Services.Projection;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "ui";

builder.Services.AddControllers();

// Bind the ordered ETF groupings from the "EtfGroups" config array.
builder.Services.Configure<EtfGroupOptions>(options =>
    options.Groups = builder.Configuration.GetSection(EtfGroupOptions.SectionName).Get<List<EtfGroup>>() ?? new());

builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddScoped<IQuoteReadRepository, QuoteReadRepository>();
builder.Services.AddScoped<IQuoteQueryService, QuoteQueryService>();
builder.Services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
builder.Services.AddScoped<IAnalyticsQueryService, AnalyticsQueryService>();
builder.Services.AddScoped<IDailyOhlcReadRepository, DailyOhlcReadRepository>();
builder.Services.AddScoped<IReboundQueryService, ReboundQueryService>();
builder.Services.AddScoped<IRangeQueryService, RangeQueryService>();
builder.Services.AddScoped<ICorrelationQueryService, CorrelationQueryService>();
builder.Services.AddScoped<ICrawlerQueryService, CrawlerQueryService>();
builder.Services.AddScoped<IPriceHistoryQueryService, PriceHistoryQueryService>();
builder.Services.AddScoped<IRangeMatrixQueryService, RangeMatrixQueryService>();
builder.Services.AddScoped<IDigestReadRepository, DigestReadRepository>();
builder.Services.AddScoped<IDigestQueryService, DigestQueryService>();
builder.Services.AddScoped<IProjectionReadRepository, ProjectionReadRepository>();

// The projection test replays five years of history (~seconds of CPU), so its
// scorecard is computed once per new trading day and shared.
builder.Services.AddSingleton<ProjectionScorecardCache>();
builder.Services.AddHostedService<ProjectionScorecardRefresher>();

// The crawler screener scans every symbol's full history (~150s), so it's memoized
// per window and refreshed nightly instead of computed on each request.
builder.Services.AddSingleton<CrawlerCache>();
builder.Services.AddHostedService<CrawlerCacheRefresher>();

// In-memory output cache for the read API. The data changes only a few times a day
// (snapshots + the nightly rollup), so caching responses keeps repeat/bot traffic off
// the serverless SQL database — letting it auto-pause — without any external cache.
// Route params (e.g. rebound/{symbol}) are already part of the path key; this varies
// by every query key the API reads so per-window/per-day/etc. results stay distinct.
// Keep this list in sync when adding a query parameter to a cached GET.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("api", policy => policy
        .Expire(TimeSpan.FromMinutes(10))
        .SetVaryByQuery(CachedQueryKeys.All));
});

// Allow the React dev/published origin(s) from config.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .WithMethods("GET")));

var app = builder.Build();

// Serve the built React app (published into wwwroot) and fall back to index.html
// for client-side routes. The UI calls the API same-origin, so no CORS in prod.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(CorsPolicy);
app.UseMiddleware<ApiKeyMiddleware>();
// After the API-key gate so only authorized requests are served from / stored in the cache.
app.UseOutputCache();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
