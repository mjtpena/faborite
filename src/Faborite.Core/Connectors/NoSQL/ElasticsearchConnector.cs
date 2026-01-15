using Elasticsearch.Net;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.NoSQL;

/// <summary>
/// Production-ready Elasticsearch/OpenSearch connector for full-text search integration.
/// Issue #140 - Elasticsearch/OpenSearch full-text search integration
/// </summary>
public class ElasticsearchConnector : IDisposable
{
    private readonly ILogger<ElasticsearchConnector> _logger;
    private readonly IElasticLowLevelClient _client;

    public ElasticsearchConnector(
        ILogger<ElasticsearchConnector> logger,
        string[] nodes,
        string? username = null,
        string? password = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var uris = nodes.Select(n => new Uri(n)).ToArray();
        var pool = new StaticConnectionPool(uris);
        
        var settings = new ConnectionConfiguration(pool);

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            settings = settings.BasicAuthentication(username, password);
        }

        _client = new ElasticLowLevelClient(settings);

        _logger.LogInformation("Elasticsearch connector initialized for {Nodes}", string.Join(",", nodes));
    }

    public async Task<ElasticsearchResponse<dynamic>> SearchAsync(
        string index,
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Searching index {Index} with query: {Query}", index, query);

            var response = await _client.SearchAsync<DynamicResponse>(
                index,
                PostData.String(query));

            _logger.LogInformation("Search completed: {Count} hits", 
                response.Success ? "success" : "failed");

            return new ElasticsearchResponse<dynamic>(
                response.Success,
                response.Body,
                response.DebugInformation
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search index {Index}", index);
            throw;
        }
    }

    public async Task<bool> IndexDocumentAsync(
        string index,
        string id,
        object document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Indexing document {Id} into {Index}", id, index);

            var response = await _client.IndexAsync<DynamicResponse>(
                index,
                id,
                PostData.Serializable(document));

            if (!response.Success)
            {
                _logger.LogError("Failed to index document: {Error}", response.DebugInformation);
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {Id}", id);
            throw;
        }
    }

    public async Task<int> BulkIndexAsync(
        string index,
        List<Dictionary<string, object?>> documents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Bulk indexing {Count} documents into {Index}", 
                documents.Count, index);

            var bulkBody = new List<object>();
            
            foreach (var doc in documents)
            {
                bulkBody.Add(new { index = new { _index = index } });
                bulkBody.Add(doc);
            }

            var response = await _client.BulkAsync<DynamicResponse>(
                PostData.MultiJson(bulkBody));

            if (!response.Success)
            {
                _logger.LogError("Bulk index failed: {Error}", response.DebugInformation);
                return 0;
            }

            _logger.LogInformation("Bulk index completed: {Count} documents", documents.Count);
            return documents.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk index documents");
            throw;
        }
    }

    public async Task<bool> DeleteDocumentAsync(
        string index,
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting document {Id} from {Index}", id, index);

            var response = await _client.DeleteAsync<DynamicResponse>(index, id);

            if (!response.Success)
            {
                _logger.LogError("Failed to delete document: {Error}", response.DebugInformation);
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {Id}", id);
            throw;
        }
    }

    public async Task<bool> CreateIndexAsync(
        string index,
        string? mappings = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating index {Index}", index);

            var body = mappings ?? "{}";
            var response = await _client.Indices.CreateAsync<DynamicResponse>(
                index,
                body);

            if (!response.Success)
            {
                _logger.LogError("Failed to create index: {Error}", response.DebugInformation);
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create index {Index}", index);
            throw;
        }
    }

    public async Task<bool> DeleteIndexAsync(
        string index,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting index {Index}", index);

            var response = await _client.Indices.DeleteAsync<DynamicResponse>(index);

            if (!response.Success)
            {
                _logger.LogError("Failed to delete index: {Error}", response.DebugInformation);
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete index {Index}", index);
            throw;
        }
    }

    public async Task<List<string>> ListIndicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing indices");

            var response = await _client.Cat.IndicesAsync<DynamicResponse>();

            if (!response.Success)
            {
                _logger.LogError("Failed to list indices: {Error}", response.DebugInformation);
                return new List<string>();
            }

            // Parse cat API response
            var indices = new List<string>();
            var body = response.Body.ToString();
            if (!string.IsNullOrEmpty(body))
            {
                var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 2)
                    {
                        indices.Add(parts[2]); // Index name is typically 3rd column
                    }
                }
            }

            _logger.LogInformation("Found {Count} indices", indices.Count);
            return indices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list indices");
            throw;
        }
    }

    public async Task<ClusterHealth> GetClusterHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting cluster health");

            var response = await _client.Cluster.HealthAsync<DynamicResponse>();

            if (!response.Success)
            {
                _logger.LogError("Failed to get cluster health: {Error}", response.DebugInformation);
                return new ClusterHealth("unknown", 0, 0, 0);
            }

            var body = response.Body as dynamic;
            var status = body?.status?.ToString() ?? "unknown";
            var nodes = (int)(body?.number_of_nodes ?? 0);
            var dataNodes = (int)(body?.number_of_data_nodes ?? 0);
            var activePrimaryShards = (int)(body?.active_primary_shards ?? 0);

            return new ClusterHealth(status, nodes, dataNodes, activePrimaryShards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cluster health");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("Elasticsearch connector disposed");
    }
}

public record ElasticsearchResponse<T>(
    bool Success,
    T? Body,
    string DebugInfo
);

public record ClusterHealth(
    string Status,
    int NumberOfNodes,
    int NumberOfDataNodes,
    int ActivePrimaryShards
);
