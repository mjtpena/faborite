namespace Faborite.Api.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private static readonly Dictionary<string, (DateTime resetTime, int count)> _requestCounts = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public RateLimitingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var enabled = _configuration.GetValue<bool>("RateLimiting:Enabled");
        if (!enabled)
        {
            await _next(context);
            return;
        }

        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var perMinute = _configuration.GetValue<int>("RateLimiting:PerMinute", 60);

        await _lock.WaitAsync();
        try
        {
            if (!_requestCounts.TryGetValue(clientId, out var entry) || entry.resetTime < DateTime.UtcNow)
            {
                entry = (DateTime.UtcNow.AddMinutes(1), 0);
            }

            entry.count++;
            _requestCounts[clientId] = entry;

            if (entry.count > perMinute)
            {
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = "60";
                await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                return;
            }

            context.Response.Headers["X-RateLimit-Limit"] = perMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = (perMinute - entry.count).ToString();
        }
        finally
        {
            _lock.Release();
        }

        await _next(context);
    }
}
