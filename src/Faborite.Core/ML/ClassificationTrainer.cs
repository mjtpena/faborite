using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready classification trainer supporting multiple algorithms.
/// Issue #164 - Classification model training (XGBoost, Random Forest)
/// </summary>
public class ClassificationTrainer : IDisposable
{
    private readonly ILogger<ClassificationTrainer> _logger;
    private readonly MLContext _mlContext;

    public ClassificationTrainer(ILogger<ClassificationTrainer> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("Classification trainer initialized");
    }

    public async Task<ClassificationResult> TrainLightGbmAsync(
        IDataView trainingData,
        IDataView? validationData = null,
        string labelColumn = "Label",
        string featuresColumn = "Features",
        int numberOfLeaves = 20,
        int numberOfIterations = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training LightGBM classifier with {Leaves} leaves, {Iterations} iterations",
                numberOfLeaves, numberOfIterations);

            var options = new LightGbmBinaryTrainer.Options
            {
                LabelColumnName = labelColumn,
                FeatureColumnName = featuresColumn,
                NumberOfLeaves = numberOfLeaves,
                NumberOfIterations = numberOfIterations,
                MinimumExampleCountPerLeaf = 10,
                LearningRate = 0.1
            };

            var trainer = _mlContext.BinaryClassification.Trainers.LightGbm(options);
            var model = trainer.Fit(trainingData);

            var testData = validationData ?? trainingData;
            var predictions = model.Transform(testData);
            var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: labelColumn);

            var result = new ClassificationResult
            {
                Algorithm = "LightGBM",
                Accuracy = metrics.Accuracy,
                AucRoc = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                Precision = metrics.PositivePrecision,
                Recall = metrics.PositiveRecall,
                Model = model
            };

            _logger.LogInformation("LightGBM trained: Accuracy {Accuracy:P2}, AUC {Auc:F4}",
                result.Accuracy, result.AucRoc);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train LightGBM");
            throw;
        }
    }

    public async Task<ClassificationResult> TrainFastTreeAsync(
        IDataView trainingData,
        IDataView? validationData = null,
        string labelColumn = "Label",
        string featuresColumn = "Features",
        int numberOfTrees = 100,
        int numberOfLeaves = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training FastTree classifier with {Trees} trees",
                numberOfTrees);

            var options = new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = labelColumn,
                FeatureColumnName = featuresColumn,
                NumberOfTrees = numberOfTrees,
                NumberOfLeaves = numberOfLeaves,
                MinimumExampleCountPerLeaf = 10,
                LearningRate = 0.2
            };

            var trainer = _mlContext.BinaryClassification.Trainers.FastTree(options);
            var model = trainer.Fit(trainingData);

            var testData = validationData ?? trainingData;
            var predictions = model.Transform(testData);
            var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: labelColumn);

            var result = new ClassificationResult
            {
                Algorithm = "FastTree",
                Accuracy = metrics.Accuracy,
                AucRoc = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                Precision = metrics.PositivePrecision,
                Recall = metrics.PositiveRecall,
                Model = model
            };

            _logger.LogInformation("FastTree trained: Accuracy {Accuracy:P2}, AUC {Auc:F4}",
                result.Accuracy, result.AucRoc);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train FastTree");
            throw;
        }
    }

    public async Task<ClassificationResult> TrainSdcaAsync(
        IDataView trainingData,
        IDataView? validationData = null,
        string labelColumn = "Label",
        string featuresColumn = "Features",
        int maximumNumberOfIterations = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training SDCA (logistic regression) classifier");

            var options = new SdcaLogisticRegressionBinaryTrainer.Options
            {
                LabelColumnName = labelColumn,
                FeatureColumnName = featuresColumn,
                MaximumNumberOfIterations = maximumNumberOfIterations,
                L2Regularization = 0.01f
            };

            var trainer = _mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(options);
            var model = trainer.Fit(trainingData);

            var testData = validationData ?? trainingData;
            var predictions = model.Transform(testData);
            var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: labelColumn);

            var result = new ClassificationResult
            {
                Algorithm = "SDCA",
                Accuracy = metrics.Accuracy,
                AucRoc = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                Precision = metrics.PositivePrecision,
                Recall = metrics.PositiveRecall,
                Model = model
            };

            _logger.LogInformation("SDCA trained: Accuracy {Accuracy:P2}, AUC {Auc:F4}",
                result.Accuracy, result.AucRoc);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train SDCA");
            throw;
        }
    }

    public async Task<MulticlassResult> TrainMulticlassAsync(
        IDataView trainingData,
        IDataView? validationData = null,
        string labelColumn = "Label",
        string featuresColumn = "Features",
        MulticlassAlgorithm algorithm = MulticlassAlgorithm.LightGbm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Training multiclass classifier with {Algorithm}", algorithm);

            IEstimator<ITransformer> trainer = algorithm switch
            {
                MulticlassAlgorithm.LightGbm => _mlContext.MulticlassClassification.Trainers.LightGbm(
                    labelColumnName: labelColumn,
                    featureColumnName: featuresColumn),
                MulticlassAlgorithm.SdcaMaximumEntropy => _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: labelColumn,
                    featureColumnName: featuresColumn),
                MulticlassAlgorithm.LbfgsMaximumEntropy => _mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy(
                    labelColumnName: labelColumn,
                    featureColumnName: featuresColumn),
                _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
            };

            var model = trainer.Fit(trainingData);

            var testData = validationData ?? trainingData;
            var predictions = model.Transform(testData);
            var metrics = _mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: labelColumn);

            var result = new MulticlassResult
            {
                Algorithm = algorithm.ToString(),
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                LogLossReduction = metrics.LogLossReduction,
                Model = model
            };

            _logger.LogInformation("Multiclass trained: Micro {Micro:P2}, Macro {Macro:P2}",
                result.MicroAccuracy, result.MacroAccuracy);

            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train multiclass classifier");
            throw;
        }
    }

    public async Task<List<ClassificationResult>> CompareAlgorithmsAsync(
        IDataView trainingData,
        IDataView validationData,
        string labelColumn = "Label",
        string featuresColumn = "Features",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Comparing classification algorithms");

            var results = new List<ClassificationResult>();

            // LightGBM
            var lightGbm = await TrainLightGbmAsync(trainingData, validationData, labelColumn, featuresColumn, cancellationToken: cancellationToken);
            results.Add(lightGbm);

            // FastTree
            var fastTree = await TrainFastTreeAsync(trainingData, validationData, labelColumn, featuresColumn, cancellationToken: cancellationToken);
            results.Add(fastTree);

            // SDCA
            var sdca = await TrainSdcaAsync(trainingData, validationData, labelColumn, featuresColumn, cancellationToken: cancellationToken);
            results.Add(sdca);

            var best = results.OrderByDescending(r => r.Accuracy).First();

            _logger.LogInformation("Best algorithm: {Algorithm} with accuracy {Accuracy:P2}",
                best.Algorithm, best.Accuracy);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare algorithms");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Classification trainer disposed");
    }
}

public enum MulticlassAlgorithm
{
    LightGbm,
    SdcaMaximumEntropy,
    LbfgsMaximumEntropy
}

public class ClassificationResult
{
    public string Algorithm { get; set; } = "";
    public double Accuracy { get; set; }
    public double AucRoc { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public ITransformer? Model { get; set; }
}

public class MulticlassResult
{
    public string Algorithm { get; set; } = "";
    public double MicroAccuracy { get; set; }
    public double MacroAccuracy { get; set; }
    public double LogLoss { get; set; }
    public double LogLossReduction { get; set; }
    public ITransformer? Model { get; set; }
}
