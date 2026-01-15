using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.TimeSeries;

/// <summary>
/// Production-ready InfluxDB connector for time series data.
/// Issue #153 - InfluxDB connector
/// </summary>
public class InfluxDbConnector : IAsyncDisposable
{
    private readonly ILogger<InfluxDbConnector> _logger;
    private readonly InfluxDBClient _client;
    private readonly string _org;
    private readonly string _bucket;

    public InfluxDbConnector(
        ILogger<InfluxDbConnector> logger,
        string url,
        string token,
        string org,
        string bucket)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _org = org ?? throw new ArgumentNullException(nameof(org));
        _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));

        _client = new InfluxDBClient(url, token);

        _logger.LogInformation("InfluxDB connector initialized for {Url}/{Org}/{Bucket}",
            url, org, bucket);
    }

    public async Task WritePointAsync(
        string measurement,
        Dictionary<string, object> fields,
        Dictionary<string, string>? tags = null,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var point = PointData.Measurement(measurement);

            if (tags != null)
            {
                foreach (var (key, value) in tags)
                {
                    point = point.Tag(key, value);
                }
            }

            foreach (var (key, value) in fields)
            {
                point = value switch
                {
                    double d => point.Field(key, d),
                    float f => point.Field(key, f),
                    long l => point.Field(key, l),
                    int i => point.Field(key, i),
                    bool b => point.Field(key, b),
                    string s => point.Field(key, s),
                    _ => point.Field(key, value.ToString() ?? "")
                };
            }

            if (timestamp.HasValue)
            {
                point = point.Timestamp(timestamp.Value, WritePrecision.Ns);
            }

            using var writeApi = _client.GetWriteApi();
            writeApi.WritePoint(point, _bucket, _org);

            _logger.LogDebug("Wrote point to measurement {Measurement}", measurement);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write point");
            throw;
        }
    }

    public async Task WriteBatchAsync(
        List<TimeSeriesPoint> points,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Writing batch of {Count} points", points.Count);

            using var writeApi = _client.GetWriteApi();

            foreach (var point in points)
            {
                var dataPoint = PointData.Measurement(point.Measurement);

                if (point.Tags != null)
                {
                    foreach (var (key, value) in point.Tags)
                    {
                        dataPoint = dataPoint.Tag(key, value);
                    }
                }

                foreach (var (key, value) in point.Fields)
                {
                    dataPoint = value switch
                    {
                        double d => dataPoint.Field(key, d),
                        float f => dataPoint.Field(key, f),
                        long l => dataPoint.Field(key, l),
                        int i => dataPoint.Field(key, i),
                        bool b => dataPoint.Field(key, b),
                        string s => dataPoint.Field(key, s),
                        _ => dataPoint.Field(key, value.ToString() ?? "")
                    };
                }

                if (point.Timestamp.HasValue)
                {
                    dataPoint = dataPoint.Timestamp(point.Timestamp.Value, WritePrecision.Ns);
                }

                writeApi.WritePoint(dataPoint, _bucket, _org);
            }

            _logger.LogInformation("Wrote {Count} points to InfluxDB", points.Count);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object>>> QueryAsync(
        string fluxQuery,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Executing Flux query");

            var queryApi = _client.GetQueryApi();
            var tables = await queryApi.QueryAsync(fluxQuery, _org, cancellationToken);

            var results = new List<Dictionary<string, object>>();

            foreach (var table in tables)
            {
                foreach (var record in table.Records)
                {
                    var row = new Dictionary<string, object>();

                    foreach (var (key, value) in record.Values)
                    {
                        if (value != null)
                        {
                            row[key] = value;
                        }
                    }

                    results.Add(row);
                }
            }

            _logger.LogInformation("Query returned {Count} records", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query");
            throw;
        }
    }

    public async Task<List<Dictionary<string, object>>> QueryTimeRangeAsync(
        string measurement,
        DateTime start,
        DateTime stop,
        string? fieldFilter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = $@"
                from(bucket: ""{_bucket}"")
                  |> range(start: {start:yyyy-MM-ddTHH:mm:ssZ}, stop: {stop:yyyy-MM-ddTHH:mm:ssZ})
                  |> filter(fn: (r) => r._measurement == ""{measurement}"")";

            if (!string.IsNullOrEmpty(fieldFilter))
            {
                query += $@"
                  |> filter(fn: (r) => r._field == ""{fieldFilter}"")";
            }

            return await QueryAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query time range");
            throw;
        }
    }

    public async Task<Dictionary<string, double>> GetLastValuesAsync(
        string measurement,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = $@"
                from(bucket: ""{_bucket}"")
                  |> range(start: -1h)
                  |> filter(fn: (r) => r._measurement == ""{measurement}"")
                  |> last()";

            var results = await QueryAsync(query, cancellationToken);

            var lastValues = new Dictionary<string, double>();

            foreach (var record in results)
            {
                if (record.TryGetValue("_field", out var field) &&
                    record.TryGetValue("_value", out var value))
                {
                    if (field is string fieldName && value is double doubleValue)
                    {
                        lastValues[fieldName] = doubleValue;
                    }
                    else if (field is string fieldName2 && value != null)
                    {
                        if (double.TryParse(value.ToString(), out var parsedValue))
                        {
                            lastValues[fieldName2] = parsedValue;
                        }
                    }
                }
            }

            return lastValues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get last values");
            throw;
        }
    }

    public async Task<List<string>> ListMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing measurements");

            var query = $@"
                import ""influxdata/influxdb/schema""
                schema.measurements(bucket: ""{_bucket}"")";

            var results = await QueryAsync(query, cancellationToken);

            var measurements = results
                .Where(r => r.ContainsKey("_value"))
                .Select(r => r["_value"]?.ToString() ?? "")
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .ToList();

            _logger.LogInformation("Found {Count} measurements", measurements.Count);
            return measurements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list measurements");
            throw;
        }
    }

    public async Task DeleteAsync(
        string measurement,
        DateTime start,
        DateTime stop,
        string? predicate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting data from measurement {Measurement}", measurement);

            var deleteApi = _client.GetDeleteApi();

            var predicateStr = string.IsNullOrEmpty(predicate)
                ? $"_measurement=\"{measurement}\""
                : $"_measurement=\"{measurement}\" AND {predicate}";

            await deleteApi.Delete(start, stop, predicateStr, _bucket, _org, cancellationToken);

            _logger.LogInformation("Deleted data from measurement {Measurement}", measurement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete data");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _client.HealthAsync();
            var isHealthy = health.Status == HealthCheck.StatusEnum.Pass;

            _logger.LogInformation("InfluxDB health check: {Status}", health.Status);
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await Task.CompletedTask;
        _logger.LogDebug("InfluxDB connector disposed");
    }
}

public record TimeSeriesPoint(
    string Measurement,
    Dictionary<string, object> Fields,
    Dictionary<string, string>? Tags = null,
    DateTime? Timestamp = null
);
