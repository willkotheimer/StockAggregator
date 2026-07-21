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

// Allow the React dev/published origin(s) from config.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .WithMethods("GET")));

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/", () => Results.Ok("StockAggregator API. See /api/quotes/week."));
app.MapControllers();

app.Run();
