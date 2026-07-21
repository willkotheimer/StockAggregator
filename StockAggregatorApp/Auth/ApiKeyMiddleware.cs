namespace StockAggregatorApp.Auth;

/// <summary>
/// Rejects requests missing the expected API key header — but only when an
/// <c>ApiKey</c> is configured. With no key configured (local dev) it's a no-op,
/// so localhost stays open while published environments set a key. The API is
/// read-only regardless; this is anti-spam, not a security boundary for writes.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly string? _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _expectedKey = config["ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // No key configured → open (local dev).
        if (string.IsNullOrEmpty(_expectedKey))
        {
            await _next(context);
            return;
        }

        // Only guard the API surface; leave health/other paths alone.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        if (!string.Equals(provided, _expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid API key.");
            return;
        }

        await _next(context);
    }
}
