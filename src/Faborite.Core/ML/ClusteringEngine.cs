using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready clustering engine for unsupervised learning.
/// Issue #167 - Clustering algorithms
/// </summary>
public class ClusteringEngine : IDisposable
{
    private readonly ILogger<ClusteringEngine> _logger;
    private readonly MLContext _mlContext;

    public ClusteringEngine(ILogger<ClusteringEngine> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("Clustering engine initialized");
    }

    public async Task<ClusteringResult> KMeansClusterAsync(
        IDataView data,
        string featuresColumn,
        int numberOfClusters = 3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training K-means with {Clusters} clusters", numberOfClusters);

            var pipeline = _mlContext.Clustering.Trainers.KMeans(
                featureColumnName: featuresColumn,
                numberOfClusters: numberOfClusters);

            var model = pipeline.Fit(data);
            var predictions = model.Transform(data);

            var metrics = _mlContext.Clustering.Evaluate(
                predictions,
                scoreColumnName: "Score",
                featureColumnName: featuresColumn);

            var clusterPredictions = _mlContext.Data.CreateEnumerable<ClusterPrediction>(
                predictions, reuseRowObject: false).ToList();

            var clusterSizes = clusterPredictions
                .GroupBy(p => p.PredictedLabel)
                .ToDictionary(g => g.Key, g => g.Count());

            var result = new ClusteringResult
            {
                NumberOfClusters = numberOfClusters,
                AverageDistance = metrics.AverageDistance,
                DaviesBouldinIndex = metrics.DaviesBouldinIndex,
                ClusterSizes = clusterSizes,
                Predictions = clusterPredictions,
                Model = model
            };

            _logger.LogInformation("K-means clustering completed: Avg distance {Distance:F4}, DBI {DBI:F4}",
                result.AverageDistance, result.DaviesBouldinIndex);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed K-means clustering");
            throw;
        }
    }

    public async Task<int> FindOptimalClustersAsync(
        IDataView data,
        string featuresColumn,
        int minClusters = 2,
        int maxClusters = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Finding optimal clusters between {Min} and {Max}",
                minClusters, maxClusters);

            var results = new Dictionary<int, double>();

            for (int k = minClusters; k <= maxClusters; k++)
            {
                var pipeline = _mlContext.Clustering.Trainers.KMeans(
                    featureColumnName: featuresColumn,
                    numberOfClusters: k);

                var model = pipeline.Fit(data);
                var predictions = model.Transform(data);

                var metrics = _mlContext.Clustering.Evaluate(
                    predictions,
                    scoreColumnName: "Score",
                    featureColumnName: featuresColumn);

                results[k] = metrics.AverageDistance;

                _logger.LogDebug("K={K}: Avg distance {Distance:F4}", k, metrics.AverageDistance);
            }

            // Find elbow point using simple derivative
            var elbow = minClusters;
            var maxDecrease = 0.0;

            for (int k = minClusters + 1; k < maxClusters; k++)
            {
                var decrease = results[k - 1] - results[k];
                if (decrease > maxDecrease)
                {
                    maxDecrease = decrease;
                    elbow = k;
                }
            }

            _logger.LogInformation("Optimal number of clusters: {Elbow}", elbow);

            return await Task.FromResult(elbow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find optimal clusters");
            throw;
        }
    }

    public async Task<List<ClusterProfile>> ProfileClustersAsync(
        IDataView data,
        ClusteringResult clusteringResult,
        string[] featureNames,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Profiling {Count} clusters", clusteringResult.NumberOfClusters);

            var profiles = new List<ClusterProfile>();

            var dataEnumerable = _mlContext.Data.CreateEnumerable<DataPoint>(data, reuseRowObject: false).ToList();

            for (uint clusterId = 0; clusterId < clusteringResult.NumberOfClusters; clusterId++)
            {
                var clusterPoints = clusteringResult.Predictions
                    .Where(p => p.PredictedLabel == clusterId)
                    .Select((p, i) => dataEnumerable.ElementAtOrDefault(i))
                    .Where(d => d != null)
                    .ToList();

                if (clusterPoints.Count == 0)
                    continue;

                var featureAverages = new Dictionary<string, float>();

                for (int i = 0; i < featureNames.Length && i < clusterPoints[0]!.Features.Length; i++)
                {
                    var avg = clusterPoints.Average(p => p!.Features[i]);
                    featureAverages[featureNames[i]] = avg;
                }

                profiles.Add(new ClusterProfile
                {
                    ClusterId = clusterId,
                    Size = clusterPoints.Count,
                    FeatureAverages = featureAverages
                });
            }

            _logger.LogInformation("Profiled {Count} clusters", profiles.Count);

            return await Task.FromResult(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to profile clusters");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Clustering engine disposed");
    }
}

public class ClusterPrediction
{
    [ColumnName("PredictedLabel")]
    public uint PredictedLabel { get; set; }

    [ColumnName("Score")]
    public float[] Distances { get; set; } = Array.Empty<float>();
}

public class DataPoint
{
    [VectorType]
    public float[] Features { get; set; } = Array.Empty<float>();
}

public class ClusteringResult
{
    public int NumberOfClusters { get; set; }
    public double AverageDistance { get; set; }
    public double DaviesBouldinIndex { get; set; }
    public Dictionary<uint, int> ClusterSizes { get; set; } = new();
    public List<ClusterPrediction> Predictions { get; set; } = new();
    public ITransformer? Model { get; set; }
}

public class ClusterProfile
{
    public uint ClusterId { get; set; }
    public int Size { get; set; }
    public Dictionary<string, float> FeatureAverages { get; set; } = new();
}
