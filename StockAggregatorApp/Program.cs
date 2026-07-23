using StockAggregatorApp.Auth;
using StockAggregatorApp.Data;
using StockAggregatorApp.Repositories;
using StockAggregatorApp.Services;

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

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
