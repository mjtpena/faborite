using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.NoSQL;

/// <summary>
/// MongoDB aggregation pipeline extensions.
/// Issue #138 - MongoDB aggregation pipeline support
/// </summary>
public class MongoDbAggregationExtensions
{
    private readonly ILogger<MongoDbAggregationExtensions> _logger;
    private readonly IMongoDatabase _database;

    public MongoDbAggregationExtensions(
        ILogger<MongoDbAggregationExtensions> logger,
        IMongoDatabase database)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<List<BsonDocument>> AggregateAsync(
        string collectionName,
        List<BsonDocument> pipeline,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing aggregation pipeline on {Collection} with {Stages} stages",
                collectionName, pipeline.Count);

            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var result = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync(cancellationToken);

            _logger.LogInformation("Aggregation returned {Count} documents", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute aggregation pipeline");
            throw;
        }
    }

    public async Task<List<BsonDocument>> MatchStageAsync(
        string collectionName,
        BsonDocument matchFilter,
        CancellationToken cancellationToken = default)
    {
        var pipeline = new List<BsonDocument>
        {
            new BsonDocument("$match", matchFilter)
        };

        return await AggregateAsync(collectionName, pipeline, cancellationToken);
    }

    public async Task<List<BsonDocument>> GroupByAsync(
        string collectionName,
        string groupByField,
        Dictionary<string, string> aggregations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Grouping {Collection} by {Field}", collectionName, groupByField);

            var groupStage = new BsonDocument
            {
                { "_id", $"${groupByField}" }
            };

            foreach (var (key, aggFunc) in aggregations)
            {
                groupStage[key] = new BsonDocument($"${aggFunc}", $"${key}");
            }

            var pipeline = new List<BsonDocument>
            {
                new BsonDocument("$group", groupStage)
            };

            return await AggregateAsync(collectionName, pipeline, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute group by");
            throw;
        }
    }

    public async Task<List<BsonDocument>> LookupJoinAsync(
        string collectionName,
        string foreignCollection,
        string localField,
        string foreignField,
        string asField,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Joining {Collection} with {Foreign} on {LocalField}={ForeignField}",
                collectionName, foreignCollection, localField, foreignField);

            var lookupStage = new BsonDocument("$lookup", new BsonDocument
            {
                { "from", foreignCollection },
                { "localField", localField },
                { "foreignField", foreignField },
                { "as", asField }
            });

            var pipeline = new List<BsonDocument> { lookupStage };

            return await AggregateAsync(collectionName, pipeline, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute lookup join");
            throw;
        }
    }

    public async Task<List<BsonDocument>> FacetSearchAsync(
        string collectionName,
        Dictionary<string, List<BsonDocument>> facets,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing faceted search on {Collection}", collectionName);

            var facetDoc = new BsonDocument();
            foreach (var (facetName, facetPipeline) in facets)
            {
                facetDoc[facetName] = new BsonArray(facetPipeline);
            }

            var pipeline = new List<BsonDocument>
            {
                new BsonDocument("$facet", facetDoc)
            };

            return await AggregateAsync(collectionName, pipeline, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute faceted search");
            throw;
        }
    }

    public async Task<List<BsonDocument>> ComplexAggregationAsync(
        string collectionName,
        AggregationPipelineBuilder builder,
        CancellationToken cancellationToken = default)
    {
        return await AggregateAsync(collectionName, builder.Build(), cancellationToken);
    }

    public async Task<BsonDocument> GetAggregationStatsAsync(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pipeline = new List<BsonDocument>
            {
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", BsonNull.Value },
                    { "count", new BsonDocument("$sum", 1) },
                    { "avgDocSize", new BsonDocument("$avg", new BsonDocument("$bsonSize", "$$ROOT")) }
                })
            };

            var result = await AggregateAsync(collectionName, pipeline, cancellationToken);
            return result.FirstOrDefault() ?? new BsonDocument();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get aggregation stats");
            throw;
        }
    }
}

/// <summary>
/// Fluent builder for MongoDB aggregation pipelines.
/// </summary>
public class AggregationPipelineBuilder
{
    private readonly List<BsonDocument> _stages = new();

    public AggregationPipelineBuilder Match(BsonDocument filter)
    {
        _stages.Add(new BsonDocument("$match", filter));
        return this;
    }

    public AggregationPipelineBuilder Group(string idField, Dictionary<string, string> aggregations)
    {
        var groupDoc = new BsonDocument { { "_id", $"${idField}" } };
        foreach (var (key, aggFunc) in aggregations)
        {
            groupDoc[key] = new BsonDocument($"${aggFunc}", $"${key}");
        }
        _stages.Add(new BsonDocument("$group", groupDoc));
        return this;
    }

    public AggregationPipelineBuilder Sort(Dictionary<string, int> sortFields)
    {
        var sortDoc = new BsonDocument();
        foreach (var (field, direction) in sortFields)
        {
            sortDoc[field] = direction;
        }
        _stages.Add(new BsonDocument("$sort", sortDoc));
        return this;
    }

    public AggregationPipelineBuilder Limit(int count)
    {
        _stages.Add(new BsonDocument("$limit", count));
        return this;
    }

    public AggregationPipelineBuilder Skip(int count)
    {
        _stages.Add(new BsonDocument("$skip", count));
        return this;
    }

    public AggregationPipelineBuilder Project(Dictionary<string, object> fields)
    {
        var projectDoc = new BsonDocument();
        foreach (var (field, include) in fields)
        {
            if (include is BsonValue bsonValue)
                projectDoc[field] = bsonValue;
            else if (include is int intValue)
                projectDoc[field] = intValue;
            else if (include is string strValue)
                projectDoc[field] = strValue;
        }
        _stages.Add(new BsonDocument("$project", projectDoc));
        return this;
    }

    public AggregationPipelineBuilder Unwind(string field)
    {
        _stages.Add(new BsonDocument("$unwind", $"${field}"));
        return this;
    }

    public AggregationPipelineBuilder Lookup(string from, string localField, string foreignField, string asField)
    {
        _stages.Add(new BsonDocument("$lookup", new BsonDocument
        {
            { "from", from },
            { "localField", localField },
            { "foreignField", foreignField },
            { "as", asField }
        }));
        return this;
    }

    public AggregationPipelineBuilder AddFields(Dictionary<string, object> fields)
    {
        var addFieldsDoc = new BsonDocument();
        foreach (var (field, value) in fields)
        {
            if (value is BsonValue bsonValue)
                addFieldsDoc[field] = bsonValue;
            else if (value is string strValue)
                addFieldsDoc[field] = strValue;
        }
        _stages.Add(new BsonDocument("$addFields", addFieldsDoc));
        return this;
    }

    public AggregationPipelineBuilder Bucket(string groupByField, int[] boundaries, string outputField = "count")
    {
        _stages.Add(new BsonDocument("$bucket", new BsonDocument
        {
            { "groupBy", $"${groupByField}" },
            { "boundaries", new BsonArray(boundaries) },
            { "output", new BsonDocument { { outputField, new BsonDocument("$sum", 1) } } }
        }));
        return this;
    }

    public List<BsonDocument> Build()
    {
        return _stages;
    }
}
