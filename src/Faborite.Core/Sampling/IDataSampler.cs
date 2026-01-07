using Faborite.Core.Configuration;

namespace Faborite.Core.Sampling;

/// <summary>
/// Interface for data sampling operations.
/// </summary>
public interface IDataSampler : IDisposable
{
    /// <summary>
    /// Sample data from parquet files.
    /// </summary>
    Task<SampleResult> SampleFromParquetAsync(
        string parquetPath,
        string tableName,
        string outputPath,
        SampleConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sample data from a Delta table URI.
    /// </summary>
    Task<SampleResult> SampleFromDeltaAsync(
        string deltaUri,
        string tableName,
        string outputPath,
        SampleConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sample from local parquet files (already downloaded).
    /// </summary>
    SampleResult SampleFromLocalParquet(
        string localParquetPath,
        string tableName,
        string outputPath,
        SampleConfig config);
}
