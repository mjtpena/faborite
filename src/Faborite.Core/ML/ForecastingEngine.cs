using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready forecasting engine for time series prediction.
/// Issue #163 - Time series forecasting
/// </summary>
public class ForecastingEngine : IDisposable
{
    private readonly ILogger<ForecastingEngine> _logger;
    private readonly MLContext _mlContext;

    public ForecastingEngine(ILogger<ForecastingEngine> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("Forecasting engine initialized");
    }

    public async Task<ForecastResult> ForecastAsync(
        IDataView trainingData,
        string valueColumn,
        int horizon,
        int windowSize = 10,
        int seriesLength = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training forecast model with horizon {Horizon} and window {Window}",
                horizon, windowSize);

            var forecastingPipeline = _mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "ForecastedValues",
                inputColumnName: valueColumn,
                windowSize: windowSize,
                seriesLength: seriesLength > 0 ? seriesLength : windowSize * 3,
                trainSize: trainingData.GetRowCount() ?? 100,
                horizon: horizon,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: "LowerBound",
                confidenceUpperBoundColumn: "UpperBound");

            var model = forecastingPipeline.Fit(trainingData);

            var forecastEngine = model.CreateTimeSeriesEngine<TimeSeriesData, TimeSeriesForecast>(_mlContext);

            var forecast = forecastEngine.Predict();

            var result = new ForecastResult
            {
                Horizon = horizon,
                ForecastedValues = forecast.ForecastedValues.ToList(),
                LowerBounds = forecast.LowerBound.ToList(),
                UpperBounds = forecast.UpperBound.ToList(),
                Model = model
            };

            _logger.LogInformation("Forecast completed: {Count} values predicted", result.ForecastedValues.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forecast");
            throw;
        }
    }

    public async Task<MultiStepForecastResult> MultiStepForecastAsync(
        List<float> historicalData,
        int horizon,
        int windowSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Multi-step forecasting {Horizon} steps ahead", horizon);

            var data = historicalData.Select(v => new TimeSeriesData { Value = v });
            var dataView = _mlContext.Data.LoadFromEnumerable(data);

            var pipeline = _mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "ForecastedValues",
                inputColumnName: nameof(TimeSeriesData.Value),
                windowSize: windowSize,
                seriesLength: historicalData.Count,
                trainSize: historicalData.Count,
                horizon: horizon,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: "LowerBound",
                confidenceUpperBoundColumn: "UpperBound");

            var model = pipeline.Fit(dataView);
            var forecastEngine = model.CreateTimeSeriesEngine<TimeSeriesData, TimeSeriesForecast>(_mlContext);

            var forecasts = new List<ForecastStep>();

            for (int step = 0; step < horizon; step++)
            {
                var prediction = forecastEngine.Predict();

                forecasts.Add(new ForecastStep
                {
                    Step = step + 1,
                    Value = prediction.ForecastedValues[0],
                    LowerBound = prediction.LowerBound[0],
                    UpperBound = prediction.UpperBound[0]
                });

                // Update forecast engine with predicted value for next step
                forecastEngine.CheckPoint(_mlContext, $"forecast_step_{step}");
            }

            var result = new MultiStepForecastResult
            {
                Horizon = horizon,
                Forecasts = forecasts,
                Model = model
            };

            _logger.LogInformation("Multi-step forecast completed: {Steps} steps", forecasts.Count);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed multi-step forecast");
            throw;
        }
    }

    public async Task<SeasonalForecastResult> SeasonalForecastAsync(
        IDataView trainingData,
        string valueColumn,
        int seasonalPeriod,
        int horizon,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Seasonal forecasting with period {Period}, horizon {Horizon}",
                seasonalPeriod, horizon);

            var pipeline = _mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "ForecastedValues",
                inputColumnName: valueColumn,
                windowSize: seasonalPeriod * 2,
                seriesLength: trainingData.GetRowCount() ?? 100,
                trainSize: trainingData.GetRowCount() ?? 100,
                horizon: horizon,
                confidenceLevel: 0.95f,
                confidenceLowerBoundColumn: "LowerBound",
                confidenceUpperBoundColumn: "UpperBound");

            var model = pipeline.Fit(trainingData);
            var forecastEngine = model.CreateTimeSeriesEngine<TimeSeriesData, TimeSeriesForecast>(_mlContext);

            var forecast = forecastEngine.Predict();

            var result = new SeasonalForecastResult
            {
                SeasonalPeriod = seasonalPeriod,
                Horizon = horizon,
                ForecastedValues = forecast.ForecastedValues.ToList(),
                LowerBounds = forecast.LowerBound.ToList(),
                UpperBounds = forecast.UpperBound.ToList(),
                Model = model
            };

            _logger.LogInformation("Seasonal forecast completed");

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed seasonal forecast");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Forecasting engine disposed");
    }
}

public class TimeSeriesData
{
    public float Value { get; set; }
}

public class TimeSeriesForecast
{
    [VectorType]
    public float[] ForecastedValues { get; set; } = Array.Empty<float>();

    [VectorType]
    public float[] LowerBound { get; set; } = Array.Empty<float>();

    [VectorType]
    public float[] UpperBound { get; set; } = Array.Empty<float>();
}

public class ForecastResult
{
    public int Horizon { get; set; }
    public List<float> ForecastedValues { get; set; } = new();
    public List<float> LowerBounds { get; set; } = new();
    public List<float> UpperBounds { get; set; } = new();
    public ITransformer? Model { get; set; }
}

public class ForecastStep
{
    public int Step { get; set; }
    public float Value { get; set; }
    public float LowerBound { get; set; }
    public float UpperBound { get; set; }
}

public class MultiStepForecastResult
{
    public int Horizon { get; set; }
    public List<ForecastStep> Forecasts { get; set; } = new();
    public ITransformer? Model { get; set; }
}

public class SeasonalForecastResult
{
    public int SeasonalPeriod { get; set; }
    public int Horizon { get; set; }
    public List<float> ForecastedValues { get; set; } = new();
    public List<float> LowerBounds { get; set; } = new();
    public List<float> UpperBounds { get; set; } = new();
    public ITransformer? Model { get; set; }
}
