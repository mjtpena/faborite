using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Faborite.Core.Connectors.Database;

public record BigQueryConfig(
    string ProjectId,
    string? DatasetId = null,
    string? JsonCredentialsPath = null,
    string? Location = "US");

/// <summary>
/// Production-ready Google BigQuery connector with partition awareness and streaming inserts.
/// Issue #150 - Google BigQuery Connector
/// </summary>
public class BigQueryConnector : IQueryableConnector
{
    private readonly ILogger<BigQueryConnector> _logger;
    private readonly BigQueryConfig _config;
    private readonly BigQueryClient _client;

    public string Name => "BigQuery";
    public string Version => "3.11.0";

    public BigQueryConnector(ILogger<BigQueryConnector> logger, BigQueryConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _client = string.IsNullOrEmpty(config.JsonCredentialsPath)
            ? BigQueryClient.Create(config.ProjectId)
            : BigQueryClient.Create(config.ProjectId, Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(config.JsonCredentialsPath));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing BigQuery connection to project {Project}", _config.ProjectId);

        try
        {
            var query = "SELECT 1 as test";
            var result = await _client.ExecuteQueryAsync(query, parameters: null, cancellationToken: cancellationToken);
            
            _logger.LogInformation("BigQuery connection successful to project {Project}", _config.ProjectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigQuery connection failed");
            return false;
        }
    }

    public Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ConnectorMetadata(
            "BigQuery",
            Version,
            new Dictionary<string, string>
            {
                ["ProjectId"] = _config.ProjectId,
                ["Location"] = _config.Location ?? "US",
                ["SupportsPartitioning"] = "true",
                ["SupportsStreaming"] = "true",
                ["SupportsClustering"] = "true",
                ["SupportsMLModels"] = "true",
                ["SupportsGIS"] = "true",
                ["SupportsTimeTravel"] = "true"
            },
            new List<string> { "Query", "StreamingInsert", "LoadJob", "Extract", "PartitionQuery" }
        ));
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing BigQuery query");
        var startTime = DateTime.UtcNow;

        var result = await _client.ExecuteQueryAsync(query, parameters: null, cancellationToken: cancellationToken);

        var columns = new List<ColumnMetadata>();
        foreach (var field in result.Schema.Fields)
        {
            columns.Add(new ColumnMetadata(
                field.Name,
                field.Type.ToString(),
                field.Mode != "REQUIRED"
            ));
        }

        var rows = new List<Dictionary<string, object?>>();
        
        foreach (var row in result)
        {
            var rowDict = new Dictionary<string, object?>();
            foreach (var field in result.Schema.Fields)
            {
                rowDict[field.Name] = row[field.Name];
            }
            rows.Add(rowDict);
        }

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation("Query returned {RowCount} rows in {Duration}ms",
            rows.Count, duration.TotalMilliseconds);

        return new QueryResult(rows, columns, rows.Count, duration);
    }

    public async Task<QueryResult> ExecutePartitionQueryAsync(
        string tableName,
        string partitionColumn,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing partition-aware query on {Table} from {Start} to {End}",
            tableName, startDate, endDate);

        var query = $@"
            SELECT *
            FROM `{tableName}`
            WHERE {partitionColumn} BETWEEN @startDate AND @endDate";

        var parameters = new[]
        {
            new BigQueryParameter("startDate", BigQueryDbType.Date, startDate),
            new BigQueryParameter("endDate", BigQueryDbType.Date, endDate)
        };

        var startTime = DateTime.UtcNow;
        var result = await _client.ExecuteQueryAsync(query, parameters, cancellationToken: cancellationToken);

        var columns = new List<ColumnMetadata>();
        foreach (var field in result.Schema.Fields)
        {
            columns.Add(new ColumnMetadata(field.Name, field.Type.ToString(), field.Mode != "REQUIRED"));
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in result)
        {
            var rowDict = new Dictionary<string, object?>();
            foreach (var field in result.Schema.Fields)
            {
                rowDict[field.Name] = row[field.Name];
            }
            rows.Add(rowDict);
        }

        var duration = DateTime.UtcNow - startTime;
        return new QueryResult(rows, columns, rows.Count, duration);
    }

    public async Task<List<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing BigQuery tables in dataset {Dataset}", _config.DatasetId);

        if (string.IsNullOrEmpty(_config.DatasetId))
        {
            _logger.LogWarning("DatasetId not configured, listing all datasets");
            return await ListAllTablesAsync(cancellationToken);
        }

        var tables = new List<TableInfo>();
        var dataset = _client.GetDataset(_config.DatasetId);

        await foreach (var table in dataset.ListTablesAsync())
        {
            var tableRef = table.Reference;
            var tableData = await _client.GetTableAsync(tableRef, cancellationToken: cancellationToken);

            var columns = new List<ColumnMetadata>();
            foreach (var field in tableData.Schema.Fields)
            {
                columns.Add(new ColumnMetadata(
                    field.Name,
                    field.Type.ToString(),
                    field.Mode != "REQUIRED"
                ));
            }

            tables.Add(new TableInfo(
                tableRef.TableId,
                tableRef.DatasetId,
                (long?)tableData.Resource.NumRows ?? 0,
                columns
            ));
        }

        _logger.LogInformation("Found {TableCount} tables", tables.Count);
        return tables;
    }

    public async Task<DataTransferResult> StreamingInsertAsync<T>(
        string tableId,
        IEnumerable<T> rows,
        CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Starting streaming insert to {Table}", tableId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(_config.DatasetId))
                throw new InvalidOperationException("DatasetId must be configured for streaming inserts");

            var tableRef = _client.GetTableReference(_config.DatasetId, tableId);
            
            var rowsList = rows.ToList();
            var bigQueryRows = rowsList.Select(r => new BigQueryInsertRow(System.Text.Json.JsonSerializer.Serialize(r))).ToList();
            var insertOptions = new InsertOptions { AllowUnknownFields = false };
            await _client.InsertRowsAsync(tableRef, bigQueryRows, insertOptions, cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Streaming insert completed: {Rows} rows in {Duration}ms",
                rowsList.Count, duration.TotalMilliseconds);

            return new DataTransferResult(rowsList.Count, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming insert failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> LoadJobAsync(
        string tableId,
        string sourceUri,
        BigQueryFileFormat format = BigQueryFileFormat.Csv,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting load job from {Source} to {Table}", sourceUri, tableId);
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(_config.DatasetId))
                throw new InvalidOperationException("DatasetId must be configured for load jobs");

            var tableRef = _client.GetTableReference(_config.DatasetId, tableId);

            var loadJob = _client.CreateLoadJob(
                sourceUri,
                tableRef,
                null,
                new CreateLoadJobOptions
                {
                    SourceFormat = MapFileFormat(format),
                    WriteDisposition = WriteDisposition.WriteAppend,
                    CreateDisposition = CreateDisposition.CreateIfNeeded
                });

            loadJob = await loadJob.PollUntilCompletedAsync(cancellationToken: cancellationToken);

            if (loadJob.Status.State == "DONE" && loadJob.Status.ErrorResult == null)
            {
                var stats = loadJob.Statistics.Load;
                var duration = DateTime.UtcNow - startTime;

                _logger.LogInformation("Load job completed: {Rows} rows, {Bytes} bytes in {Duration}ms",
                    stats?.OutputRows ?? 0, stats?.OutputBytes ?? 0, duration.TotalMilliseconds);

                return new DataTransferResult(stats?.OutputRows ?? 0, 0, duration, true);
            }
            else
            {
                var error = loadJob.Status.ErrorResult?.Message ?? "Unknown error";
                _logger.LogError("Load job failed: {Error}", error);
                var duration = DateTime.UtcNow - startTime;
                return new DataTransferResult(0, 0, duration, false, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load job failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<string> ExportToGcsAsync(
        string tableId,
        string destinationUri,
        BigQueryFileFormat format = BigQueryFileFormat.Csv,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting {Table} to {Destination}", tableId, destinationUri);

        if (string.IsNullOrEmpty(_config.DatasetId))
            throw new InvalidOperationException("DatasetId must be configured for exports");

        var tableRef = _client.GetTableReference(_config.DatasetId, tableId);

        var extractJob = _client.CreateExtractJob(
            tableRef,
            destinationUri,
            new CreateExtractJobOptions
            {
                DestinationFormat = MapFileFormat(format)
            });

        extractJob = await extractJob.PollUntilCompletedAsync(cancellationToken: cancellationToken);

        if (extractJob.Status.State == "DONE" && extractJob.Status.ErrorResult == null)
        {
            _logger.LogInformation("Export completed successfully");
            return destinationUri;
        }
        else
        {
            var error = extractJob.Status.ErrorResult?.Message ?? "Unknown error";
            throw new InvalidOperationException($"Export failed: {error}");
        }
    }

    private async Task<List<TableInfo>> ListAllTablesAsync(CancellationToken cancellationToken)
    {
        var allTables = new List<TableInfo>();

        await foreach (var dataset in _client.ListDatasetsAsync(_config.ProjectId))
        {
            await foreach (var table in dataset.ListTablesAsync())
            {
                var tableRef = table.Reference;
                var tableData = await _client.GetTableAsync(tableRef, cancellationToken: cancellationToken);

                var columns = new List<ColumnMetadata>();
                foreach (var field in tableData.Schema.Fields)
                {
                    columns.Add(new ColumnMetadata(field.Name, field.Type.ToString(), field.Mode != "REQUIRED"));
                }

                allTables.Add(new TableInfo(tableRef.TableId, tableRef.DatasetId, (long?)tableData.Resource.NumRows ?? 0, columns));
            }
        }

        return allTables;
    }

    private static FileFormat MapFileFormat(BigQueryFileFormat format)
    {
        return format switch
        {
            BigQueryFileFormat.Csv => FileFormat.Csv,
            BigQueryFileFormat.Json => FileFormat.NewlineDelimitedJson,
            BigQueryFileFormat.Avro => FileFormat.Avro,
            BigQueryFileFormat.Parquet => FileFormat.Parquet,
            BigQueryFileFormat.Orc => FileFormat.Orc,
            _ => FileFormat.Csv
        };
    }
}

public enum BigQueryFileFormat
{
    Csv,
    Json,
    Avro,
    Parquet,
    Orc
}
