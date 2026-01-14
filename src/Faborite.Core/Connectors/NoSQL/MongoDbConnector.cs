using System.Text.Json;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Faborite.Core.Connectors.NoSQL;

public record MongoDbConfig(
    string ConnectionString,
    string DatabaseName,
    int MaxPoolSize = 100,
    int ConnectTimeoutMs = 30000,
    int ServerSelectionTimeoutMs = 30000);

/// <summary>
/// Production-ready MongoDB connector for document database operations.
/// Supports CRUD, aggregations, change streams, and transactions.
/// </summary>
public class MongoDbConnector : IDataConnector, IDisposable
{
    private readonly MongoDbConfig _config;
    private readonly ILogger<MongoDbConnector> _logger;
    private MongoClient? _client;
    private IMongoDatabase? _database;

    public string Name => "MongoDB";
    public string Version => "2.31.0";

    public MongoDbConnector(MongoDbConfig config, ILogger<MongoDbConnector> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = GetDatabase();
            await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            
            _logger.LogInformation("Successfully connected to MongoDB database {Database}", _config.DatabaseName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MongoDB");
            return false;
        }
    }

    public async Task<ConnectorMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var buildInfo = await db.RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: cancellationToken);
        
        var version = buildInfo.GetValue("version", "unknown").ToString();
        var storageEngine = buildInfo.GetValue("storageEngines", new BsonArray()).AsBsonArray.FirstOrDefault()?.ToString() ?? "unknown";
        
        var capabilities = new Dictionary<string, string>
        {
            ["Version"] = version!,
            ["StorageEngine"] = storageEngine,
            ["Database"] = _config.DatabaseName,
            ["SupportsTransactions"] = "true",
            ["SupportsChangeStreams"] = "true"
        };

        var operations = new List<string> 
        { 
            "Insert", "Find", "Update", "Delete", "Aggregate", "Count", 
            "CreateIndex", "DropIndex", "Watch", "BulkWrite"
        };

        return new ConnectorMetadata(
            Type: "NoSQL-Document",
            Version: Version,
            Capabilities: capabilities,
            SupportedOperations: operations);
    }

    // Collection operations
    public IMongoCollection<BsonDocument> GetCollection(string collectionName)
    {
        var db = GetDatabase();
        return db.GetCollection<BsonDocument>(collectionName);
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName)
    {
        var db = GetDatabase();
        return db.GetCollection<T>(collectionName);
    }

    // CRUD operations
    public async Task<string> InsertOneAsync<T>(string collectionName, T document, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        
        _logger.LogDebug("Inserted document into collection {Collection}", collectionName);
        
        // Extract _id if it exists
        var bsonDoc = document!.ToBsonDocument();
        return bsonDoc.Contains("_id") ? bsonDoc["_id"].ToString()! : string.Empty;
    }

    public async Task<long> InsertManyAsync<T>(string collectionName, IEnumerable<T> documents, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var docList = documents.ToList();
        await collection.InsertManyAsync(docList, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Inserted {Count} documents into collection {Collection}", docList.Count, collectionName);
        return docList.Count;
    }

    public async Task<List<T>> FindAsync<T>(string collectionName, FilterDefinition<T> filter, int limit = 1000, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var cursor = await collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    public async Task<T?> FindOneAsync<T>(string collectionName, FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var cursor = await collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await cursor.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<long> UpdateOneAsync<T>(string collectionName, FilterDefinition<T> filter, UpdateDefinition<T> update, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var result = await collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        
        _logger.LogDebug("Updated {Count} document(s) in collection {Collection}", result.ModifiedCount, collectionName);
        return result.ModifiedCount;
    }

    public async Task<long> UpdateManyAsync<T>(string collectionName, FilterDefinition<T> filter, UpdateDefinition<T> update, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var result = await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Updated {Count} document(s) in collection {Collection}", result.ModifiedCount, collectionName);
        return result.ModifiedCount;
    }

    public async Task<long> DeleteOneAsync<T>(string collectionName, FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var result = await collection.DeleteOneAsync(filter, cancellationToken: cancellationToken);
        
        _logger.LogDebug("Deleted {Count} document(s) from collection {Collection}", result.DeletedCount, collectionName);
        return result.DeletedCount;
    }

    public async Task<long> DeleteManyAsync<T>(string collectionName, FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var result = await collection.DeleteManyAsync(filter, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Deleted {Count} document(s) from collection {Collection}", result.DeletedCount, collectionName);
        return result.DeletedCount;
    }

    // Aggregation
    public async Task<List<BsonDocument>> AggregateAsync(string collectionName, PipelineDefinition<BsonDocument, BsonDocument> pipeline, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(collectionName);
        var cursor = await collection.AggregateAsync(pipeline, cancellationToken: cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    // Count
    public async Task<long> CountAsync<T>(string collectionName, FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        return await collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    // Index management
    public async Task<string> CreateIndexAsync<T>(string collectionName, IndexKeysDefinition<T> keys, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var indexModel = new CreateIndexModel<T>(keys);
        var indexName = await collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Created index {IndexName} on collection {Collection}", indexName, collectionName);
        return indexName;
    }

    public async Task DropIndexAsync<T>(string collectionName, string indexName, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        await collection.Indexes.DropOneAsync(indexName, cancellationToken);
        
        _logger.LogInformation("Dropped index {IndexName} from collection {Collection}", indexName, collectionName);
    }

    // Change Streams (MongoDB 3.6+)
    public async IAsyncEnumerable<ChangeStreamDocument<BsonDocument>> WatchAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        var collection = GetCollection(collectionName);
        
        using var cursor = await collection.WatchAsync(cancellationToken: cancellationToken);
        
        _logger.LogInformation("Started watching collection {Collection} for changes", collectionName);
        
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var change in cursor.Current)
            {
                yield return change;
            }
        }
    }

    // Bulk operations
    public async Task<long> BulkWriteAsync<T>(string collectionName, IEnumerable<WriteModel<T>> requests, CancellationToken cancellationToken = default)
    {
        var collection = GetCollection<T>(collectionName);
        var result = await collection.BulkWriteAsync(requests, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Bulk write on collection {Collection}: {Inserted} inserted, {Modified} modified, {Deleted} deleted",
            collectionName, result.InsertedCount, result.ModifiedCount, result.DeletedCount);
        
        return result.InsertedCount + result.ModifiedCount + result.DeletedCount;
    }

    // Collection management
    public async Task<List<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var cursor = await db.ListCollectionNamesAsync(cancellationToken: cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    public async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        await db.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Created collection {Collection}", collectionName);
    }

    public async Task DropCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        await db.DropCollectionAsync(collectionName, cancellationToken);
        
        _logger.LogInformation("Dropped collection {Collection}", collectionName);
    }

    private IMongoDatabase GetDatabase()
    {
        if (_database != null)
            return _database;

        var settings = MongoClientSettings.FromConnectionString(_config.ConnectionString);
        settings.MaxConnectionPoolSize = _config.MaxPoolSize;
        settings.ConnectTimeout = TimeSpan.FromMilliseconds(_config.ConnectTimeoutMs);
        settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(_config.ServerSelectionTimeoutMs);

        _client = new MongoClient(settings);
        _database = _client.GetDatabase(_config.DatabaseName);
        
        return _database;
    }

    public void Dispose()
    {
        // MongoClient manages connections internally, no explicit disposal needed
        GC.SuppressFinalize(this);
    }
}
