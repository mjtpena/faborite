using Microsoft.Extensions.Logging;

namespace Faborite.Api.Webhooks;

/// <summary>
/// Webhook management and delivery system.
/// Issue #57
/// </summary>
public class WebhookManager
{
    private readonly ILogger<WebhookManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, List<WebhookSubscription>> _subscriptions = new();

    public WebhookManager(ILogger<WebhookManager> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public string Subscribe(WebhookSubscription subscription)
    {
        var id = Guid.NewGuid().ToString("N");
        subscription = subscription with { Id = id };

        if (!_subscriptions.ContainsKey(subscription.Event))
        {
            _subscriptions[subscription.Event] = new List<WebhookSubscription>();
        }

        _subscriptions[subscription.Event].Add(subscription);
        _logger.LogInformation("Webhook subscribed: {Id} for event {Event}", id, subscription.Event);

        return id;
    }

    public bool Unsubscribe(string id)
    {
        foreach (var (_, subs) in _subscriptions)
        {
            var removed = subs.RemoveAll(s => s.Id == id);
            if (removed > 0)
            {
                _logger.LogInformation("Webhook unsubscribed: {Id}", id);
                return true;
            }
        }
        return false;
    }

    public async Task TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(eventName, out var subs))
            return;

        _logger.LogInformation("Triggering {Count} webhooks for event {Event}", subs.Count, eventName);

        var tasks = subs.Select(sub => DeliverWebhookAsync(sub, payload, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task DeliverWebhookAsync(WebhookSubscription subscription, object payload, CancellationToken ct)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                @event = subscription.Event,
                timestamp = DateTime.UtcNow,
                data = payload
            });

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            if (!string.IsNullOrEmpty(subscription.Secret))
            {
                var signature = ComputeSignature(json, subscription.Secret);
                content.Headers.Add("X-Webhook-Signature", signature);
            }

            var response = await _httpClient.PostAsync(subscription.Url, content, ct);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Webhook delivered to {Url}", subscription.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook delivery failed to {Url}", subscription.Url);
        }
    }

    private string ComputeSignature(string payload, string secret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}

public record WebhookSubscription(
    string Event,
    string Url,
    string? Secret = null)
{
    public string? Id { get; init; }
}

public static class WebhookEvents
{
    public const string SyncStarted = "sync.started";
    public const string SyncCompleted = "sync.completed";
    public const string SyncFailed = "sync.failed";
    public const string SchemaChanged = "schema.changed";
    public const string QualityIssue = "quality.issue";
}
