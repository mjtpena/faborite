using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Net;
using CosmosDatabase = Microsoft.Azure.Cosmos.Database;

namespace Faborite.Core.Connectors.NoSQL;

public record CosmosDbConfig(
    string AccountEndpoint,
    string AccountKey,
    string DatabaseId,
    string? PreferredRegion = null,
    int MaxRetryAttempts = 3,
    int MaxRetryWaitTimeSeconds = 30);

/// <summary>
/// Production-ready Azure Cosmos DB connector with multi-model support.
/// Supports SQL API, partition key optimization, bulk operations, and change feed.
/// Issue #157
/// </summary>
public class CosmosDbConnector : IDataConnector, IDisposable
{
    private readonly CosmosDbConfig _config;
    private readonly ILogger<CosmosDbConnector> _logger;
    private readonly CosmosClient _client;
    private CosmosDatabase? _database;

    public string Name => "Azure Cosmos DB";
    public string Version => "1.0.0";

    public CosmosDbConnector(CosmosDbConfig config, ILogger<CosmosDbConnector> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = new CosmosClientOptions
        {
            ApplicationName = "Faborite",
            MaxRetryAttemptsOnRateLimitedRequests = config.MaxRetryAttempts,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(config.MaxRetryWaitTimeSeconds),
            AllowBulkExecution = true
        };

        if (!string.IsNullOrEmpty(config.PreferredRegion))
        {
            options.ApplicationRegion = config.PreferredRegion;
        }

        _client = new CosmosClient(config.AccountEndpoint, config.AccountKey, options);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing Cosmos DB connection to {Endpoint}/{Database}",
            _config.AccountEndpoint, _config.DatabaseId);

        try
        {
            _database = await _client.CreateDatabaseIfNotExistsAsync(_config.DatabaseId, cancellationToken: cancellationToken);
            
            var properties = await _database.ReadAsync(cancellationToken: cancellationToken);
            
            _logger.LogInformation("Cosmos DB connection successful: Database={Database}, ETag={ETag}",
                properties.Resource.Id, properties.ETag);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cosmos DB connection failed");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);

        var throughput = await _database!.ReadThroughputAsync(cancellationToken);
        var containers = await ListContainersAsync(cancellationToken);

        return new ConnectorMetadata(
            "NoSQL",
            Version,
            new Dictionary<string, string>
            {
                ["DatabaseId"] = _config.DatabaseId,
                ["Endpoint"] = _config.AccountEndpoint,
                ["ConsistencyLevel"] = "Session",
                ["ThroughputMode"] = throughput != null ? "Provisioned" : "Serverless",
                ["ProvisionedRU"] = throughput?.ToString() ?? "Serverless",
                ["ContainerCount"] = containers.Count.ToString(),
                ["SupportsMultiRegion"] = "true",
                ["SupportsSQLAPI"] = "true",
                ["SupportsChangeFeed"] = "true",
                ["SupportsBulkOperations"] = "true"
            },
            new List<string> { "Query", "BulkInsert", "BulkUpdate", "BulkDelete", "ChangeFeed", "PointRead" }
        );
    }

    public async Task<List<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);

        _logger.LogInformation("Listing Cosmos DB containers");

        var containers = new List<ContainerInfo>();
        using var iterator = _database!.GetContainerQueryIterator<ContainerProperties>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            
            foreach (var container in response)
            {
                var containerClient = _database.GetContainer(container.Id);
                var itemCount = await EstimateItemCountAsync(containerClient, cancellationToken);
                
                containers.Add(new ContainerInfo(
                    container.Id,
                    container.PartitionKeyPath,
                    itemCount,
                    container.DefaultTimeToLive
                ));
            }
        }

        _logger.LogInformation("Found {ContainerCount} containers", containers.Count);
        return containers;
    }

    public async Task<QueryResult> ExecuteQueryAsync(
        string containerName,
        string query,
        object? partitionKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing Cosmos DB query on {Container}", containerName);
        var startTime = DateTime.UtcNow;

        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        var queryDefinition = new QueryDefinition(query);
        var queryRequestOptions = new QueryRequestOptions();

        if (partitionKey != null)
        {
            queryRequestOptions.PartitionKey = new PartitionKey(partitionKey.ToString());
        }

        var rows = new List<Dictionary<string, object?>>();
        double totalRU = 0;

        using var iterator = container.GetItemQueryIterator<Dictionary<string, object?>>(
            queryDefinition,
            requestOptions: queryRequestOptions);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            totalRU += response.RequestCharge;
            rows.AddRange(response);
        }

        var duration = DateTime.UtcNow - startTime;
        
        _logger.LogInformation("Query returned {RowCount} items in {Duration}ms, RU consumed: {RU}",
            rows.Count, duration.TotalMilliseconds, totalRU);

        var columns = rows.Count > 0
            ? rows[0].Keys.Select(k => new ColumnMetadata(k, "object", true)).ToList()
            : new List<ColumnMetadata>();

        return new QueryResult(rows, columns, rows.Count, duration);
    }

    public async Task<T?> PointReadAsync<T>(
        string containerName,
        string id,
        object partitionKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        try
        {
            var response = await container.ReadItemAsync<T>(
                id,
                new PartitionKey(partitionKey.ToString()),
                cancellationToken: cancellationToken);

            _logger.LogDebug("Point read successful: {Id}, RU: {RU}", id, response.RequestCharge);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Item not found: {Id}", id);
            return default;
        }
    }

    public async Task<DataTransferResult> BulkUpsertAsync<T>(
        string containerName,
        IEnumerable<T> items,
        Func<T, string> idSelector,
        Func<T, object> partitionKeySelector,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bulk upsert to {Container}", containerName);
        var startTime = DateTime.UtcNow;

        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        var tasks = items.Select(item =>
            container.UpsertItemAsync(
                item,
                new PartitionKey(partitionKeySelector(item).ToString()),
                cancellationToken: cancellationToken
            )
        );

        try
        {
            var responses = await Task.WhenAll(tasks);
            var totalRU = responses.Sum(r => r.RequestCharge);
            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Bulk upsert completed: {Count} items, {RU} RU, {Duration}ms",
                responses.Length, totalRU, duration.TotalMilliseconds);

            return new DataTransferResult(responses.Length, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk upsert failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async Task<DataTransferResult> BulkDeleteAsync(
        string containerName,
        IEnumerable<(string Id, object PartitionKey)> itemsToDelete,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bulk delete from {Container}", containerName);
        var startTime = DateTime.UtcNow;

        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        var tasks = itemsToDelete.Select(item =>
            container.DeleteItemAsync<object>(
                item.Id,
                new PartitionKey(item.PartitionKey.ToString()),
                cancellationToken: cancellationToken
            )
        );

        try
        {
            var responses = await Task.WhenAll(tasks);
            var totalRU = responses.Sum(r => r.RequestCharge);
            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Bulk delete completed: {Count} items, {RU} RU, {Duration}ms",
                responses.Length, totalRU, duration.TotalMilliseconds);

            return new DataTransferResult(responses.Length, 0, duration, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk delete failed");
            var duration = DateTime.UtcNow - startTime;
            return new DataTransferResult(0, 0, duration, false, ex.Message);
        }
    }

    public async IAsyncEnumerable<T> WatchChangeFeedAsync<T>(
        string containerName,
        string? continuationToken = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        _logger.LogInformation("Starting change feed processor for {Container}", containerName);

        var iterator = container.GetChangeFeedIterator<T>(
            ChangeFeedStartFrom.Beginning(),
            ChangeFeedMode.Incremental);

        while (iterator.HasMoreResults && !cancellationToken.IsCancellationRequested)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);

            _logger.LogDebug("Change feed batch: {Count} items, RU: {RU}",
                response.Count, response.RequestCharge);

            foreach (var item in response)
            {
                yield return item;
            }

            if (response.Count == 0)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public async Task<string> CreateContainerAsync(
        string containerName,
        string partitionKeyPath,
        int? throughput = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating container {Container} with partition key {PartitionKey}",
            containerName, partitionKeyPath);

        await EnsureDatabaseAsync(cancellationToken);

        var containerProperties = new ContainerProperties(containerName, partitionKeyPath);
        
        var response = throughput.HasValue
            ? await _database!.CreateContainerIfNotExistsAsync(containerProperties, throughput.Value, cancellationToken: cancellationToken)
            : await _database!.CreateContainerIfNotExistsAsync(containerProperties, cancellationToken: cancellationToken);

        _logger.LogInformation("Container {Container} created/exists", containerName);
        return response.Container.Id;
    }

    public async Task<ThroughputResponse?> ScaleThroughputAsync(
        string containerName,
        int targetRU,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scaling {Container} to {RU} RU/s", containerName, targetRU);

        await EnsureDatabaseAsync(cancellationToken);
        var container = _database!.GetContainer(containerName);

        try
        {
            var response = await container.ReplaceThroughputAsync(targetRU, cancellationToken: cancellationToken);
            
            _logger.LogInformation("Throughput scaled successfully to {RU} RU/s", response.Resource.Throughput);
            return response;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("Container is in serverless mode, cannot scale throughput");
            return null;
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_database == null)
        {
            _database = await _client.CreateDatabaseIfNotExistsAsync(_config.DatabaseId, cancellationToken: cancellationToken);
        }
    }

    private async Task<long> EstimateItemCountAsync(Container container, CancellationToken cancellationToken)
    {
        try
        {
            var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c");
            using var iterator = container.GetItemQueryIterator<long>(query);
            
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                return response.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to estimate item count for {Container}", container.Id);
        }

        return 0;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

public record ContainerInfo(
    string Name,
    string PartitionKeyPath,
    long EstimatedItemCount,
    int? DefaultTimeToLive
);
