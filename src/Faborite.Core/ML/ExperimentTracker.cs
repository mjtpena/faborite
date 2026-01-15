using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Faborite.Core.ML;

/// <summary>
/// Production-ready model versioning and experiment tracking system.
/// Issue #169 - Model versioning and experiment tracking (MLflow-style)
/// </summary>
public class ExperimentTracker : IDisposable
{
    private readonly ILogger<ExperimentTracker> _logger;
    private readonly string _experimentPath;
    private readonly Dictionary<string, Experiment> _experiments = new();

    public ExperimentTracker(ILogger<ExperimentTracker> logger, string experimentPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _experimentPath = experimentPath ?? throw new ArgumentNullException(nameof(experimentPath));

        Directory.CreateDirectory(_experimentPath);

        _logger.LogInformation("Experiment tracker initialized at {Path}", _experimentPath);
    }

    public async Task<string> CreateExperimentAsync(
        string name,
        Dictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var experimentId = Guid.NewGuid().ToString("N");
            
            var experiment = new Experiment
            {
                Id = experimentId,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                Tags = tags ?? new Dictionary<string, string>()
            };

            _experiments[experimentId] = experiment;

            var experimentDir = Path.Combine(_experimentPath, experimentId);
            Directory.CreateDirectory(experimentDir);

            var metadataPath = Path.Combine(experimentDir, "experiment.json");
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(experiment), cancellationToken);

            _logger.LogInformation("Created experiment {Name} with ID {Id}", name, experimentId);

            return experimentId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create experiment");
            throw;
        }
    }

    public async Task<string> StartRunAsync(
        string experimentId,
        string runName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runId = Guid.NewGuid().ToString("N");

            var run = new ExperimentRun
            {
                RunId = runId,
                ExperimentId = experimentId,
                RunName = runName,
                StartTime = DateTime.UtcNow,
                Status = RunStatus.Running,
                Parameters = parameters ?? new Dictionary<string, object>(),
                Metrics = new Dictionary<string, double>(),
                Tags = new Dictionary<string, string>()
            };

            var runDir = Path.Combine(_experimentPath, experimentId, runId);
            Directory.CreateDirectory(runDir);

            var runPath = Path.Combine(runDir, "run.json");
            await File.WriteAllTextAsync(runPath, JsonSerializer.Serialize(run), cancellationToken);

            _logger.LogInformation("Started run {Name} with ID {Id} in experiment {ExperimentId}",
                runName, runId, experimentId);

            return runId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start run");
            throw;
        }
    }

    public async Task LogMetricAsync(
        string experimentId,
        string runId,
        string metricName,
        double value,
        int? step = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runPath = Path.Combine(_experimentPath, experimentId, runId, "run.json");
            var runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
            var run = JsonSerializer.Deserialize<ExperimentRun>(runJson);

            if (run == null)
                throw new InvalidOperationException($"Run {runId} not found");

            run.Metrics[metricName] = value;

            var metric = new MetricLog
            {
                Name = metricName,
                Value = value,
                Step = step,
                Timestamp = DateTime.UtcNow
            };

            // Append to metrics log
            var metricsLogPath = Path.Combine(_experimentPath, experimentId, runId, "metrics.jsonl");
            await File.AppendAllTextAsync(metricsLogPath, 
                JsonSerializer.Serialize(metric) + Environment.NewLine, cancellationToken);

            // Update run metadata
            await File.WriteAllTextAsync(runPath, JsonSerializer.Serialize(run), cancellationToken);

            _logger.LogDebug("Logged metric {Metric}={Value} for run {RunId}", metricName, value, runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log metric");
            throw;
        }
    }

    public async Task LogParameterAsync(
        string experimentId,
        string runId,
        string paramName,
        object value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runPath = Path.Combine(_experimentPath, experimentId, runId, "run.json");
            var runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
            var run = JsonSerializer.Deserialize<ExperimentRun>(runJson);

            if (run == null)
                throw new InvalidOperationException($"Run {runId} not found");

            run.Parameters[paramName] = value;

            await File.WriteAllTextAsync(runPath, JsonSerializer.Serialize(run), cancellationToken);

            _logger.LogDebug("Logged parameter {Param}={Value} for run {RunId}", paramName, value, runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log parameter");
            throw;
        }
    }

    public async Task LogArtifactAsync(
        string experimentId,
        string runId,
        string artifactName,
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var artifactsDir = Path.Combine(_experimentPath, experimentId, runId, "artifacts");
            Directory.CreateDirectory(artifactsDir);

            var destPath = Path.Combine(artifactsDir, artifactName);
            File.Copy(artifactPath, destPath, overwrite: true);

            _logger.LogInformation("Logged artifact {Artifact} for run {RunId}", artifactName, runId);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log artifact");
            throw;
        }
    }

    public async Task SaveModelAsync(
        string experimentId,
        string runId,
        ITransformer model,
        DataViewSchema schema,
        string modelName = "model.zip",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var modelsDir = Path.Combine(_experimentPath, experimentId, runId, "models");
            Directory.CreateDirectory(modelsDir);

            var modelPath = Path.Combine(modelsDir, modelName);

            var mlContext = new MLContext();
            mlContext.Model.Save(model, schema, modelPath);

            _logger.LogInformation("Saved model {Model} for run {RunId}", modelName, runId);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save model");
            throw;
        }
    }

    public async Task EndRunAsync(
        string experimentId,
        string runId,
        RunStatus status = RunStatus.Completed,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runPath = Path.Combine(_experimentPath, experimentId, runId, "run.json");
            var runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
            var run = JsonSerializer.Deserialize<ExperimentRun>(runJson);

            if (run == null)
                throw new InvalidOperationException($"Run {runId} not found");

            run.EndTime = DateTime.UtcNow;
            run.Status = status;
            run.Duration = (run.EndTime.Value - run.StartTime).TotalSeconds;

            await File.WriteAllTextAsync(runPath, JsonSerializer.Serialize(run), cancellationToken);

            _logger.LogInformation("Ended run {RunId} with status {Status}", runId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end run");
            throw;
        }
    }

    public async Task<List<ExperimentRun>> GetRunsAsync(
        string experimentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var experimentDir = Path.Combine(_experimentPath, experimentId);
            
            if (!Directory.Exists(experimentDir))
                return new List<ExperimentRun>();

            var runs = new List<ExperimentRun>();

            var runDirs = Directory.GetDirectories(experimentDir);

            foreach (var runDir in runDirs)
            {
                var runPath = Path.Combine(runDir, "run.json");
                
                if (!File.Exists(runPath))
                    continue;

                var runJson = await File.ReadAllTextAsync(runPath, cancellationToken);
                var run = JsonSerializer.Deserialize<ExperimentRun>(runJson);

                if (run != null)
                    runs.Add(run);
            }

            _logger.LogInformation("Retrieved {Count} runs for experiment {ExperimentId}",
                runs.Count, experimentId);

            return runs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get runs");
            throw;
        }
    }

    public async Task<ExperimentRun?> GetBestRunAsync(
        string experimentId,
        string metricName,
        bool higherIsBetter = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var runs = await GetRunsAsync(experimentId, cancellationToken);

            var runsWithMetric = runs.Where(r => r.Metrics.ContainsKey(metricName)).ToList();

            if (runsWithMetric.Count == 0)
                return null;

            var bestRun = higherIsBetter
                ? runsWithMetric.OrderByDescending(r => r.Metrics[metricName]).First()
                : runsWithMetric.OrderBy(r => r.Metrics[metricName]).First();

            _logger.LogInformation("Best run for {Metric}: {RunId} with value {Value}",
                metricName, bestRun.RunId, bestRun.Metrics[metricName]);

            return bestRun;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get best run");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Experiment tracker disposed");
    }
}

public class Experiment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class ExperimentRun
{
    public string RunId { get; set; } = "";
    public string ExperimentId { get; set; } = "";
    public string RunName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double Duration { get; set; }
    public RunStatus Status { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public Dictionary<string, double> Metrics { get; set; } = new();
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class MetricLog
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public int? Step { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum RunStatus
{
    Running,
    Completed,
    Failed,
    Killed
}
