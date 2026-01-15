using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready AutoML engine for automated model selection and training.
/// Issue #161 - AutoML for automated model selection
/// </summary>
public class AutoMLEngine : IDisposable
{
    private readonly ILogger<AutoMLEngine> _logger;
    private readonly MLContext _mlContext;

    public AutoMLEngine(ILogger<AutoMLEngine> logger, int? seed = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mlContext = seed.HasValue ? new MLContext(seed.Value) : new MLContext();

        _logger.LogInformation("AutoML engine initialized");
    }

    public async Task<AutoMLResult> AutoTrainClassificationAsync(
        IDataView trainingData,
        string labelColumn,
        uint maxExperimentTimeInSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting AutoML classification training for {Seconds}s", maxExperimentTimeInSeconds);

            var experiment = _mlContext.Auto()
                .CreateBinaryClassificationExperiment(maxExperimentTimeInSeconds);

            var result = experiment.Execute(trainingData, labelColumn);

            var metrics = result.BestRun.ValidationMetrics;

            var autoResult = new AutoMLResult
            {
                BestTrainer = result.BestRun.TrainerName,
                Accuracy = metrics.Accuracy,
                AucRoc = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                PositivePrecision = metrics.PositivePrecision,
                PositiveRecall = metrics.PositiveRecall,
                TrainingTime = result.BestRun.RuntimeInSeconds,
                Model = result.BestRun.Model
            };

            _logger.LogInformation("Best model: {Trainer} with accuracy {Accuracy:P2}", 
                autoResult.BestTrainer, autoResult.Accuracy);

            return await Task.FromResult(autoResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train classification model");
            throw;
        }
    }

    public async Task<AutoMLRegressionResult> AutoTrainRegressionAsync(
        IDataView trainingData,
        string labelColumn,
        uint maxExperimentTimeInSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting AutoML regression training for {Seconds}s", maxExperimentTimeInSeconds);

            var experiment = _mlContext.Auto()
                .CreateRegressionExperiment(maxExperimentTimeInSeconds);

            var result = experiment.Execute(trainingData, labelColumn);

            var metrics = result.BestRun.ValidationMetrics;

            var autoResult = new AutoMLRegressionResult
            {
                BestTrainer = result.BestRun.TrainerName,
                RSquared = metrics.RSquared,
                MeanAbsoluteError = metrics.MeanAbsoluteError,
                MeanSquaredError = metrics.MeanSquaredError,
                RootMeanSquaredError = metrics.RootMeanSquaredError,
                TrainingTime = result.BestRun.RuntimeInSeconds,
                Model = result.BestRun.Model
            };

            _logger.LogInformation("Best model: {Trainer} with R² {RSquared:F4}", 
                autoResult.BestTrainer, autoResult.RSquared);

            return await Task.FromResult(autoResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train regression model");
            throw;
        }
    }

    public async Task<AutoMLMulticlassResult> AutoTrainMulticlassAsync(
        IDataView trainingData,
        string labelColumn,
        uint maxExperimentTimeInSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting AutoML multiclass training for {Seconds}s", maxExperimentTimeInSeconds);

            var experiment = _mlContext.Auto()
                .CreateMulticlassClassificationExperiment(maxExperimentTimeInSeconds);

            var result = experiment.Execute(trainingData, labelColumn);

            var metrics = result.BestRun.ValidationMetrics;

            var autoResult = new AutoMLMulticlassResult
            {
                BestTrainer = result.BestRun.TrainerName,
                MicroAccuracy = metrics.MicroAccuracy,
                MacroAccuracy = metrics.MacroAccuracy,
                LogLoss = metrics.LogLoss,
                LogLossReduction = metrics.LogLossReduction,
                TrainingTime = result.BestRun.RuntimeInSeconds,
                Model = result.BestRun.Model
            };

            _logger.LogInformation("Best model: {Trainer} with micro accuracy {Accuracy:P2}", 
                autoResult.BestTrainer, autoResult.MicroAccuracy);

            return await Task.FromResult(autoResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to train multiclass model");
            throw;
        }
    }

    public void SaveModel(ITransformer model, DataViewSchema schema, string path)
    {
        try
        {
            _logger.LogInformation("Saving model to {Path}", path);

            _mlContext.Model.Save(model, schema, path);

            _logger.LogInformation("Model saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save model");
            throw;
        }
    }

    public ITransformer LoadModel(string path, out DataViewSchema schema)
    {
        try
        {
            _logger.LogInformation("Loading model from {Path}", path);

            var model = _mlContext.Model.Load(path, out schema);

            _logger.LogInformation("Model loaded successfully");

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("AutoML engine disposed");
    }
}

public class AutoMLResult
{
    public string BestTrainer { get; set; } = "";
    public double Accuracy { get; set; }
    public double AucRoc { get; set; }
    public double F1Score { get; set; }
    public double PositivePrecision { get; set; }
    public double PositiveRecall { get; set; }
    public double TrainingTime { get; set; }
    public ITransformer? Model { get; set; }
}

public class AutoMLRegressionResult
{
    public string BestTrainer { get; set; } = "";
    public double RSquared { get; set; }
    public double MeanAbsoluteError { get; set; }
    public double MeanSquaredError { get; set; }
    public double RootMeanSquaredError { get; set; }
    public double TrainingTime { get; set; }
    public ITransformer? Model { get; set; }
}

public class AutoMLMulticlassResult
{
    public string BestTrainer { get; set; } = "";
    public double MicroAccuracy { get; set; }
    public double MacroAccuracy { get; set; }
    public double LogLoss { get; set; }
    public double LogLossReduction { get; set; }
    public double TrainingTime { get; set; }
    public ITransformer? Model { get; set; }
}
