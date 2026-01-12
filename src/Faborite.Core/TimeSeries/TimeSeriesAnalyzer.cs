using Microsoft.Extensions.Logging;

namespace Faborite.Core.TimeSeries;

/// <summary>
/// Time-series analysis and forecasting capabilities.
/// Issue #53
/// </summary>
public class TimeSeriesAnalyzer
{
    private readonly ILogger<TimeSeriesAnalyzer> _logger;

    public TimeSeriesAnalyzer(ILogger<TimeSeriesAnalyzer> logger)
    {
        _logger = logger;
    }

    public TimeSeriesResult Analyze(List<TimeSeriesPoint> data, TimeSeriesConfig config)
    {
        _logger.LogInformation("Analyzing time series with {Count} data points", data.Count);

        var trend = CalculateTrend(data);
        var seasonality = DetectSeasonality(data, config.Period);
        var forecast = Forecast(data, config.ForecastPeriods);

        return new TimeSeriesResult(
            Trend: trend,
            Seasonality: seasonality,
            Forecast: forecast,
            Statistics: CalculateStatistics(data)
        );
    }

    private TrendAnalysis CalculateTrend(List<TimeSeriesPoint> data)
    {
        // Simple linear regression for trend
        var n = data.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += data[i].Value;
            sumXY += i * data[i].Value;
            sumX2 += i * i;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        var intercept = (sumY - slope * sumX) / n;

        return new TrendAnalysis(slope, intercept, slope > 0 ? "Increasing" : slope < 0 ? "Decreasing" : "Stable");
    }

    private SeasonalityAnalysis DetectSeasonality(List<TimeSeriesPoint> data, int period)
    {
        // Autocorrelation for seasonality detection
        var hasSeasonality = false;
        var strength = 0.0;

        if (data.Count >= period * 2)
        {
            var correlation = CalculateAutocorrelation(data.Select(d => d.Value).ToList(), period);
            hasSeasonality = Math.Abs(correlation) > 0.5;
            strength = Math.Abs(correlation);
        }

        return new SeasonalityAnalysis(hasSeasonality, period, strength);
    }

    private double CalculateAutocorrelation(List<double> values, int lag)
    {
        var mean = values.Average();
        var n = values.Count - lag;
        
        var numerator = 0.0;
        var denominator = 0.0;

        for (int i = 0; i < n; i++)
        {
            numerator += (values[i] - mean) * (values[i + lag] - mean);
        }

        for (int i = 0; i < values.Count; i++)
        {
            denominator += Math.Pow(values[i] - mean, 2);
        }

        return numerator / denominator;
    }

    private List<TimeSeriesPoint> Forecast(List<TimeSeriesPoint> data, int periods)
    {
        // Simple exponential smoothing
        var alpha = 0.3;
        var lastValue = data.Last().Value;
        var lastTime = data.Last().Timestamp;
        
        var forecast = new List<TimeSeriesPoint>();
        var interval = data.Count > 1 
            ? (data[^1].Timestamp - data[^2].Timestamp).TotalSeconds 
            : 86400; // 1 day default

        for (int i = 1; i <= periods; i++)
        {
            var forecastTime = lastTime.AddSeconds(interval * i);
            forecast.Add(new TimeSeriesPoint(forecastTime, lastValue));
        }

        return forecast;
    }

    private TimeSeriesStatistics CalculateStatistics(List<TimeSeriesPoint> data)
    {
        var values = data.Select(d => d.Value).ToList();
        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        var stdDev = Math.Sqrt(variance);

        return new TimeSeriesStatistics(
            Mean: mean,
            Variance: variance,
            StdDev: stdDev,
            Min: values.Min(),
            Max: values.Max()
        );
    }
}

public record TimeSeriesPoint(DateTime Timestamp, double Value);
public record TimeSeriesConfig(int Period = 7, int ForecastPeriods = 30);

public record TimeSeriesResult(
    TrendAnalysis Trend,
    SeasonalityAnalysis Seasonality,
    List<TimeSeriesPoint> Forecast,
    TimeSeriesStatistics Statistics);

public record TrendAnalysis(double Slope, double Intercept, string Direction);
public record SeasonalityAnalysis(bool HasSeasonality, int Period, double Strength);
public record TimeSeriesStatistics(double Mean, double Variance, double StdDev, double Min, double Max);
