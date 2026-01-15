using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready anomaly detection engine using ML.NET.
/// Issue #162 - Anomaly detection for data quality
/// </summary>
public class AnomalyDetectionEngine : IDisposable
{
    private readonly ILogger<AnomalyDetectionEngine> _logger;
    private readonly MLContext _mlContext;

    public AnomalyDetectionEngine(ILogger<AnomalyDetectionEngine> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("Anomaly detection engine initialized");
    }

    public async Task<AnomalyDetectionResult> DetectSpikesAsync(
        IDataView data,
        string valueColumn,
        int confidence = 95,
        int pValueHistoryLength = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Detecting spikes in {Column} with {Confidence}% confidence", 
                valueColumn, confidence);

            var pipeline = _mlContext.Transforms.DetectIidSpike(
                outputColumnName: "Prediction",
                inputColumnName: valueColumn,
                confidence: confidence,
                pvalueHistoryLength: pValueHistoryLength);

            var model = pipeline.fit(data);
            var transformedData = model.Transform(data);

            var predictions = _mlContext.Data.CreateEnumerable<SpikePrediction>(
                transformedData, reuseRowObject: false).ToList();

            var anomalies = predictions
                .Select((p, i) => new AnomalyPoint
                {
                    Index = i,
                    Value = p.Value,
                    IsAnomaly = p.Prediction[0] == 1,
                    Score = p.Prediction[1],
                    PValue = p.Prediction[2]
                })
                .Where(a => a.IsAnomaly)
                .ToList();

            var result = new AnomalyDetectionResult
            {
                TotalPoints = predictions.Count,
                AnomaliesDetected = anomalies.Count,
                AnomalyRate = (double)anomalies.Count / predictions.Count,
                Anomalies = anomalies,
                Model = model
            };

            _logger.LogInformation("Detected {Count} spikes out of {Total} points ({Rate:P2})",
                result.AnomaliesDetected, result.TotalPoints, result.AnomalyRate);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect spikes");
            throw;
        }
    }

    public async Task<AnomalyDetectionResult> DetectChangePointsAsync(
        IDataView data,
        string valueColumn,
        int confidence = 95,
        int changeHistoryLength = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Detecting change points in {Column} with {Confidence}% confidence",
                valueColumn, confidence);

            var pipeline = _mlContext.Transforms.DetectIidChangePoint(
                outputColumnName: "Prediction",
                inputColumnName: valueColumn,
                confidence: confidence,
                changeHistoryLength: changeHistoryLength);

            var model = pipeline.Fit(data);
            var transformedData = model.Transform(data);

            var predictions = _mlContext.Data.CreateEnumerable<ChangePointPrediction>(
                transformedData, reuseRowObject: false).ToList();

            var anomalies = predictions
                .Select((p, i) => new AnomalyPoint
                {
                    Index = i,
                    Value = p.Value,
                    IsAnomaly = p.Prediction[0] == 1,
                    Score = p.Prediction[1],
                    PValue = p.Prediction[2],
                    Martingale = p.Prediction[3]
                })
                .Where(a => a.IsAnomaly)
                .ToList();

            var result = new AnomalyDetectionResult
            {
                TotalPoints = predictions.Count,
                AnomaliesDetected = anomalies.Count,
                AnomalyRate = (double)anomalies.Count / predictions.Count,
                Anomalies = anomalies,
                Model = model
            };

            _logger.LogInformation("Detected {Count} change points out of {Total} points ({Rate:P2})",
                result.AnomaliesDetected, result.TotalPoints, result.AnomalyRate);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect change points");
            throw;
        }
    }

    public async Task<AnomalyDetectionResult> DetectSeasonalAnomaliesAsync(
        IDataView data,
        string valueColumn,
        int seasonalWindowSize = 24,
        int confidence = 95,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Detecting seasonal anomalies in {Column} with window size {Window}",
                valueColumn, seasonalWindowSize);

            var pipeline = _mlContext.Transforms.DetectSeasonalityBySsa(
                outputColumnName: "Prediction",
                inputColumnName: valueColumn,
                confidence: confidence,
                windowSize: seasonalWindowSize,
                seasonalWindowSize: seasonalWindowSize);

            var model = pipeline.Fit(data);
            var transformedData = model.Transform(data);

            var predictions = _mlContext.Data.CreateEnumerable<SeasonalPrediction>(
                transformedData, reuseRowObject: false).ToList();

            var anomalies = predictions
                .Select((p, i) => new AnomalyPoint
                {
                    Index = i,
                    Value = p.Value,
                    IsAnomaly = p.Prediction[0] == 1,
                    Score = p.Prediction[1],
                    PValue = p.Prediction[2]
                })
                .Where(a => a.IsAnomaly)
                .ToList();

            var result = new AnomalyDetectionResult
            {
                TotalPoints = predictions.Count,
                AnomaliesDetected = anomalies.Count,
                AnomalyRate = (double)anomalies.Count / predictions.Count,
                Anomalies = anomalies,
                Model = model
            };

            _logger.LogInformation("Detected {Count} seasonal anomalies out of {Total} points ({Rate:P2})",
                result.AnomaliesDetected, result.TotalPoints, result.AnomalyRate);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect seasonal anomalies");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Anomaly detection engine disposed");
    }
}

public class SpikePrediction
{
    [LoadColumn(0)]
    public float Value { get; set; }

    [VectorType(3)]
    public double[] Prediction { get; set; } = Array.Empty<double>();
}

public class ChangePointPrediction
{
    [LoadColumn(0)]
    public float Value { get; set; }

    [VectorType(4)]
    public double[] Prediction { get; set; } = Array.Empty<double>();
}

public class SeasonalPrediction
{
    [LoadColumn(0)]
    public float Value { get; set; }

    [VectorType(3)]
    public double[] Prediction { get; set; } = Array.Empty<double>();
}

public class AnomalyPoint
{
    public int Index { get; set; }
    public float Value { get; set; }
    public bool IsAnomaly { get; set; }
    public double Score { get; set; }
    public double PValue { get; set; }
    public double? Martingale { get; set; }
}

public class AnomalyDetectionResult
{
    public int TotalPoints { get; set; }
    public int AnomaliesDetected { get; set; }
    public double AnomalyRate { get; set; }
    public List<AnomalyPoint> Anomalies { get; set; } = new();
    public ITransformer? Model { get; set; }
}
