using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready recommendation engine using matrix factorization.
/// Issue #175 - Predictive data lineage recommendations
/// </summary>
public class RecommendationEngine : IDisposable
{
    private readonly ILogger<RecommendationEngine> _logger;
    private readonly MLContext _mlContext;

    public RecommendationEngine(ILogger<RecommendationEngine> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("Recommendation engine initialized");
    }

    public async Task<RecommendationModel> TrainMatrixFactorizationAsync(
        IDataView trainingData,
        string userIdColumn,
        string itemIdColumn,
        string labelColumn,
        int numberOfIterations = 20,
        int approximationRank = 8,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training matrix factorization model with rank {Rank}, iterations {Iterations}",
                approximationRank, numberOfIterations);

            var options = new MatrixFactorizationTrainer.Options
            {
                MatrixColumnIndexColumnName = userIdColumn,
                MatrixRowIndexColumnName = itemIdColumn,
                LabelColumnName = labelColumn,
                NumberOfIterations = numberOfIterations,
                ApproximationRank = approximationRank,
                LearningRate = 0.001,
                Lambda = 0.01
            };

            var pipeline = _mlContext.Recommendation().Trainers.MatrixFactorization(options);

            var model = pipeline.Fit(trainingData);

            var predictions = model.Transform(trainingData);
            var metrics = _mlContext.Regression.Evaluate(predictions, labelColumnName: labelColumn);

            var result = new RecommendationModel
            {
                Model = model,
                RSquared = metrics.RSquared,
                RootMeanSquaredError = metrics.RootMeanSquaredError,
                MeanAbsoluteError = metrics.MeanAbsoluteError,
                NumberOfIterations = numberOfIterations,
                ApproximationRank = approximationRank
            };

            _logger.LogInformation("Model trained: R² {RSquared:F4}, RMSE {RMSE:F4}",
                result.RSquared, result.RootMeanSquaredError);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train matrix factorization");
            throw;
        }
    }

    public async Task<List<RecommendationScore>> RecommendItemsAsync(
        ITransformer model,
        uint userId,
        List<uint> allItemIds,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Recommending top {N} items for user {UserId}", topN, userId);

            var predictionEngine = _mlContext.Model.CreatePredictionEngine<UserItemPair, RecommendationPrediction>(model);

            var scores = new List<RecommendationScore>();

            foreach (var itemId in allItemIds)
            {
                var prediction = predictionEngine.Predict(new UserItemPair
                {
                    UserId = userId,
                    ItemId = itemId
                });

                scores.Add(new RecommendationScore
                {
                    UserId = userId,
                    ItemId = itemId,
                    Score = prediction.Score
                });
            }

            var topRecommendations = scores
                .OrderByDescending(s => s.Score)
                .Take(topN)
                .ToList();

            _logger.LogInformation("Generated {Count} recommendations for user {UserId}",
                topRecommendations.Count, userId);

            return await Task.FromResult(topRecommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate recommendations");
            throw;
        }
    }

    public async Task<List<SimilarItem>> FindSimilarItemsAsync(
        IDataView data,
        uint targetItemId,
        string itemIdColumn,
        string featuresColumn,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Finding {N} similar items to item {ItemId}", topN, targetItemId);

            // Extract all items
            var items = _mlContext.Data.CreateEnumerable<ItemFeatures>(data, reuseRowObject: false).ToList();

            var targetItem = items.FirstOrDefault(i => i.ItemId == targetItemId);
            if (targetItem == null)
            {
                throw new ArgumentException($"Item {targetItemId} not found");
            }

            // Calculate cosine similarity
            var similarities = new List<SimilarItem>();

            foreach (var item in items)
            {
                if (item.ItemId == targetItemId)
                    continue;

                var similarity = CosineSimilarity(targetItem.Features, item.Features);

                similarities.Add(new SimilarItem
                {
                    ItemId = item.ItemId,
                    Similarity = similarity
                });
            }

            var topSimilar = similarities
                .OrderByDescending(s => s.Similarity)
                .Take(topN)
                .ToList();

            _logger.LogInformation("Found {Count} similar items", topSimilar.Count);

            return await Task.FromResult(topSimilar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar items");
            throw;
        }
    }

    public async Task<List<UserRecommendation>> GenerateBatchRecommendationsAsync(
        ITransformer model,
        List<uint> userIds,
        List<uint> allItemIds,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating batch recommendations for {Users} users",
                userIds.Count);

            var predictionEngine = _mlContext.Model.CreatePredictionEngine<UserItemPair, RecommendationPrediction>(model);

            var userRecommendations = new List<UserRecommendation>();

            foreach (var userId in userIds)
            {
                var scores = new List<RecommendationScore>();

                foreach (var itemId in allItemIds)
                {
                    var prediction = predictionEngine.Predict(new UserItemPair
                    {
                        UserId = userId,
                        ItemId = itemId
                    });

                    scores.Add(new RecommendationScore
                    {
                        UserId = userId,
                        ItemId = itemId,
                        Score = prediction.Score
                    });
                }

                var topRecommendations = scores
                    .OrderByDescending(s => s.Score)
                    .Take(topN)
                    .ToList();

                userRecommendations.Add(new UserRecommendation
                {
                    UserId = userId,
                    Recommendations = topRecommendations
                });
            }

            _logger.LogInformation("Generated batch recommendations for {Count} users",
                userRecommendations.Count);

            return await Task.FromResult(userRecommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate batch recommendations");
            throw;
        }
    }

    private static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            throw new ArgumentException("Vectors must have same length");

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        magnitudeA = Math.Sqrt(magnitudeA);
        magnitudeB = Math.Sqrt(magnitudeB);

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0;

        return dotProduct / (magnitudeA * magnitudeB);
    }

    public void Dispose()
    {
        _logger.LogDebug("Recommendation engine disposed");
    }
}

public class UserItemPair
{
    [LoadColumn(0)]
    public uint UserId { get; set; }

    [LoadColumn(1)]
    public uint ItemId { get; set; }

    [LoadColumn(2)]
    public float Label { get; set; }
}

public class RecommendationPrediction
{
    public float Score { get; set; }
}

public class ItemFeatures
{
    public uint ItemId { get; set; }

    [VectorType]
    public float[] Features { get; set; } = Array.Empty<float>();
}

public class RecommendationModel
{
    public ITransformer? Model { get; set; }
    public double RSquared { get; set; }
    public double RootMeanSquaredError { get; set; }
    public double MeanAbsoluteError { get; set; }
    public int NumberOfIterations { get; set; }
    public int ApproximationRank { get; set; }
}

public class RecommendationScore
{
    public uint UserId { get; set; }
    public uint ItemId { get; set; }
    public float Score { get; set; }
}

public class SimilarItem
{
    public uint ItemId { get; set; }
    public double Similarity { get; set; }
}

public class UserRecommendation
{
    public uint UserId { get; set; }
    public List<RecommendationScore> Recommendations { get; set; } = new();
}
