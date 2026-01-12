using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Faborite.Api.Versioning;

/// <summary>
/// API versioning support with multiple strategies.
/// Issue #62
/// </summary>
public class ApiVersionManager
{
    private readonly ILogger<ApiVersionManager> _logger;
    private readonly Dictionary<string, ApiVersion> _versions = new();

    public ApiVersionManager(ILogger<ApiVersionManager> logger)
    {
        _logger = logger;
        InitializeVersions();
    }

    private void InitializeVersions()
    {
        _versions["1.0"] = new ApiVersion("1.0", new DateTime(2024, 1, 1), false);
        _versions["1.1"] = new ApiVersion("1.1", new DateTime(2025, 1, 1), false);
        _versions["2.0"] = new ApiVersion("2.0", new DateTime(2026, 1, 1), false);
    }

    public ApiVersion? ResolveVersion(HttpRequest request, VersionStrategy strategy)
    {
        var version = strategy switch
        {
            VersionStrategy.Header => request.Headers["X-API-Version"].ToString(),
            VersionStrategy.QueryString => request.Query["api-version"].ToString(),
            VersionStrategy.MediaType => ExtractFromMediaType(request.Headers["Accept"].ToString()),
            VersionStrategy.UrlPath => ExtractFromPath(request.Path),
            _ => "2.0" // default to latest
        };

        return _versions.GetValueOrDefault(version ?? "2.0");
    }

    private string ExtractFromMediaType(string acceptHeader)
    {
        // Extract version from Accept: application/vnd.faborite.v2+json
        if (acceptHeader.Contains("v1")) return "1.0";
        if (acceptHeader.Contains("v2")) return "2.0";
        return "2.0";
    }

    private string ExtractFromPath(PathString path)
    {
        // Extract from /api/v2/sync
        if (path.Value?.Contains("/v1/") == true) return "1.0";
        if (path.Value?.Contains("/v2/") == true) return "2.0";
        return "2.0";
    }

    public bool IsVersionDeprecated(string version)
    {
        return _versions.TryGetValue(version, out var v) && v.IsDeprecated;
    }
}

public enum VersionStrategy
{
    Header,
    QueryString,
    MediaType,
    UrlPath
}

public record ApiVersion(string Version, DateTime ReleaseDate, bool IsDeprecated)
{
    public DateTime? DeprecationDate { get; init; }
    public DateTime? SunsetDate { get; init; }
}

/// <summary>
/// Advanced rate limiting with per-user quotas.
/// Issue #63
/// </summary>
public class AdvancedRateLimiter
{
    private readonly ILogger<AdvancedRateLimiter> _logger;
    private readonly Dictionary<string, RateLimitBucket> _buckets = new();

    public AdvancedRateLimiter(ILogger<AdvancedRateLimiter> logger)
    {
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckLimitAsync(string userId, RateLimitTier tier)
    {
        var key = $"{userId}:{tier}";
        
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new RateLimitBucket(GetLimitForTier(tier));
            _buckets[key] = bucket;
        }

        var allowed = bucket.TryConsume();
        
        if (!allowed)
        {
            _logger.LogWarning("Rate limit exceeded for user {User} on tier {Tier}", userId, tier);
        }

        return new RateLimitResult(
            Allowed: allowed,
            Limit: bucket.Limit,
            Remaining: bucket.Remaining,
            ResetAt: bucket.ResetAt
        );
    }

    private int GetLimitForTier(RateLimitTier tier)
    {
        return tier switch
        {
            RateLimitTier.Free => 100,
            RateLimitTier.Pro => 1000,
            RateLimitTier.Enterprise => 10000,
            _ => 100
        };
    }
}

public enum RateLimitTier { Free, Pro, Enterprise }

public class RateLimitBucket
{
    public int Limit { get; }
    public int Remaining { get; private set; }
    public DateTime ResetAt { get; private set; }

    public RateLimitBucket(int limit)
    {
        Limit = limit;
        Remaining = limit;
        ResetAt = DateTime.UtcNow.AddHours(1);
    }

    public bool TryConsume()
    {
        if (DateTime.UtcNow >= ResetAt)
        {
            Remaining = Limit;
            ResetAt = DateTime.UtcNow.AddHours(1);
        }

        if (Remaining > 0)
        {
            Remaining--;
            return true;
        }

        return false;
    }
}

public record RateLimitResult(bool Allowed, int Limit, int Remaining, DateTime ResetAt);
