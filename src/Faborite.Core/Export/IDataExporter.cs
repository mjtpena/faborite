using Faborite.Core.Configuration;

namespace Faborite.Core.Export;

/// <summary>
/// Interface for data export operations.
/// </summary>
public interface IDataExporter : IDisposable
{
    /// <summary>
    /// Export parquet file to the specified format.
    /// </summary>
    string Export(
        string sourceParquetPath,
        string tableName,
        string outputDir,
        FormatConfig config);

    /// <summary>
    /// Export schema to JSON file.
    /// </summary>
    void ExportSchema(string sourceParquetPath, string tableName, string outputDir);
}
