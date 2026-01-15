using Prometheus.Client;
using Prometheus.Client.Collectors;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Faborite.Core.Connectors.TimeSeries;

/// <summary>
/// Production-ready Prometheus metrics connector for observability.
/// Issue #155 - Prometheus connector
/// </summary>
public class PrometheusConnector : IAsyncDisposable
{
    private readonly ILogger<PrometheusConnector> _logger;
    private readonly IMetricFactory _metricFactory;
    private readonly Dictionary<string, ICounter> _counters = new();
    private readonly Dictionary<string, IGauge> _gauges = new();
    private readonly Dictionary<string, IHistogram> _histograms = new();
    private readonly Dictionary<string, ISummary> _summaries = new();

    public PrometheusConnector(ILogger<PrometheusConnector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var registry = new CollectorRegistry();
        _metricFactory = new MetricFactory(registry);

        _logger.LogInformation("Prometheus connector initialized");
    }

    public void IncrementCounter(string name, string help, double value = 1.0, params string[] labels)
    {
        try
        {
            if (!_counters.TryGetValue(name, out var counter))
            {
                counter = _metricFactory.CreateCounter(name, help, labels);
                _counters[name] = counter;
            }

            counter.Inc(value);

            _logger.LogDebug("Counter {Name} incremented by {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment counter {Name}", name);
            throw;
        }
    }

    public void SetGauge(string name, string help, double value, params string[] labels)
    {
        try
        {
            if (!_gauges.TryGetValue(name, out var gauge))
            {
                gauge = _metricFactory.CreateGauge(name, help, labels);
                _gauges[name] = gauge;
            }

            gauge.Set(value);

            _logger.LogDebug("Gauge {Name} set to {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set gauge {Name}", name);
            throw;
        }
    }

    public void IncGauge(string name, string help, double value = 1.0, params string[] labels)
    {
        try
        {
            if (!_gauges.TryGetValue(name, out var gauge))
            {
                gauge = _metricFactory.CreateGauge(name, help, labels);
                _gauges[name] = gauge;
            }

            gauge.Inc(value);

            _logger.LogDebug("Gauge {Name} incremented by {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment gauge {Name}", name);
            throw;
        }
    }

    public void DecGauge(string name, string help, double value = 1.0, params string[] labels)
    {
        try
        {
            if (!_gauges.TryGetValue(name, out var gauge))
            {
                gauge = _metricFactory.CreateGauge(name, help, labels);
                _gauges[name] = gauge;
            }

            gauge.Dec(value);

            _logger.LogDebug("Gauge {Name} decremented by {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrement gauge {Name}", name);
            throw;
        }
    }

    public void ObserveHistogram(string name, string help, double value, double[]? buckets = null, params string[] labels)
    {
        try
        {
            if (!_histograms.TryGetValue(name, out var histogram))
            {
                if (buckets != null && buckets.Length > 0)
                {
                    histogram = _metricFactory.CreateHistogram(name, help, labels, buckets);
                }
                else
                {
                    histogram = _metricFactory.CreateHistogram(name, help, labels);
                }

                _histograms[name] = histogram;
            }

            histogram.Observe(value);

            _logger.LogDebug("Histogram {Name} observed value {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to observe histogram {Name}", name);
            throw;
        }
    }

    public void ObserveSummary(string name, string help, double value, params string[] labels)
    {
        try
        {
            if (!_summaries.TryGetValue(name, out var summary))
            {
                summary = _metricFactory.CreateSummary(name, help, labels);
                _summaries[name] = summary;
            }

            summary.Observe(value);

            _logger.LogDebug("Summary {Name} observed value {Value}", name, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to observe summary {Name}", name);
            throw;
        }
    }

    public IDisposable TimeHistogram(string name, string help, double[]? buckets = null, params string[] labels)
    {
        if (!_histograms.TryGetValue(name, out var histogram))
        {
            if (buckets != null && buckets.Length > 0)
            {
                histogram = _metricFactory.CreateHistogram(name, help, labels, buckets);
            }
            else
            {
                histogram = _metricFactory.CreateHistogram(name, help, labels);
            }

            _histograms[name] = histogram;
        }

        return histogram.NewTimer();
    }

    public IDisposable TimeSummary(string name, string help, params string[] labels)
    {
        if (!_summaries.TryGetValue(name, out var summary))
        {
            summary = _metricFactory.CreateSummary(name, help, labels);
            _summaries[name] = summary;
        }

        return summary.NewTimer();
    }

    public async Task<PrometheusMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = new PrometheusMetrics
            {
                Counters = _counters.Count,
                Gauges = _gauges.Count,
                Histograms = _histograms.Count,
                Summaries = _summaries.Count,
                TotalMetrics = _counters.Count + _gauges.Count + _histograms.Count + _summaries.Count
            };

            return await Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            throw;
        }
    }

    public async Task<string> ExportMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sb = new StringBuilder();

            // Export counters
            foreach (var (name, counter) in _counters)
            {
                sb.AppendLine($"# HELP {name} Counter metric");
                sb.AppendLine($"# TYPE {name} counter");
                sb.AppendLine($"{name} {counter}");
            }

            // Export gauges
            foreach (var (name, gauge) in _gauges)
            {
                sb.AppendLine($"# HELP {name} Gauge metric");
                sb.AppendLine($"# TYPE {name} gauge");
                sb.AppendLine($"{name} {gauge}");
            }

            // Export histograms
            foreach (var (name, histogram) in _histograms)
            {
                sb.AppendLine($"# HELP {name} Histogram metric");
                sb.AppendLine($"# TYPE {name} histogram");
                sb.AppendLine($"{name} {histogram}");
            }

            // Export summaries
            foreach (var (name, summary) in _summaries)
            {
                sb.AppendLine($"# HELP {name} Summary metric");
                sb.AppendLine($"# TYPE {name} summary");
                sb.AppendLine($"{name} {summary}");
            }

            _logger.LogDebug("Exported {Total} metrics", _counters.Count + _gauges.Count + _histograms.Count + _summaries.Count);

            return await Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export metrics");
            throw;
        }
    }

    public void Reset()
    {
        _counters.Clear();
        _gauges.Clear();
        _histograms.Clear();
        _summaries.Clear();

        _logger.LogInformation("All metrics reset");
    }

    public async ValueTask DisposeAsync()
    {
        Reset();
        await Task.CompletedTask;
        _logger.LogDebug("Prometheus connector disposed");
    }
}

public class PrometheusMetrics
{
    public int Counters { get; set; }
    public int Gauges { get; set; }
    public int Histograms { get; set; }
    public int Summaries { get; set; }
    public int TotalMetrics { get; set; }
}
