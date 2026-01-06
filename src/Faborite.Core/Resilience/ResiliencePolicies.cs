using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Faborite.Core.Resilience;

/// <summary>
/// Provides resilience policies for network operations.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Default retry policy for transient failures.
    /// </summary>
    public static AsyncRetryPolicy GetDefaultRetryPolicy(ILogger? logger = null)
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .Or<Azure.RequestFailedException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + // Exponential backoff
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)), // Jitter
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    logger?.LogWarning(
                        exception,
                        "Retry {RetryCount} after {Delay}ms due to: {Message}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        exception.Message);
                });
    }

    /// <summary>
    /// Retry policy with custom settings.
    /// </summary>
    public static AsyncRetryPolicy GetRetryPolicy(
        int retryCount = 3,
        int baseDelaySeconds = 2,
        ILogger? logger = null)
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .Or<Azure.RequestFailedException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(baseDelaySeconds, retryAttempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
                onRetry: (exception, timespan, retryAttempt, context) =>
                {
                    logger?.LogWarning(
                        exception,
                        "Retry {RetryCount}/{MaxRetries} after {Delay}ms due to: {Message}",
                        retryAttempt,
                        retryCount,
                        timespan.TotalMilliseconds,
                        exception.Message);
                });
    }

    /// <summary>
    /// Generic retry policy for any type.
    /// </summary>
    public static AsyncRetryPolicy<T> GetRetryPolicy<T>(
        int retryCount = 3,
        int baseDelaySeconds = 2,
        ILogger? logger = null)
    {
        return Policy<T>
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .Or<Azure.RequestFailedException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: retryCount,
                sleepDurationProvider: retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(baseDelaySeconds, retryAttempt)) +
                    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    logger?.LogWarning(
                        outcome.Exception,
                        "Retry {RetryCount}/{MaxRetries} after {Delay}ms due to: {Message}",
                        retryAttempt,
                        retryCount,
                        timespan.TotalMilliseconds,
                        outcome.Exception?.Message ?? "Unknown error");
                });
    }

    private static bool IsTransientError(Azure.RequestFailedException ex)
    {
        // HTTP status codes that indicate transient failures
        return ex.Status switch
        {
            408 => true, // Request Timeout
            429 => true, // Too Many Requests
            500 => true, // Internal Server Error
            502 => true, // Bad Gateway
            503 => true, // Service Unavailable
            504 => true, // Gateway Timeout
            _ => false
        };
    }
}
