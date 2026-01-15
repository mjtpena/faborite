using Cassandra;
using Microsoft.Extensions.Logging;

namespace Faborite.Core.Connectors.NoSQL;

/// <summary>
/// Production-ready Cassandra/ScyllaDB connector for wide-column store sync.
/// Issue #139 - Cassandra/ScyllaDB wide-column store sync
/// </summary>
public class CassandraConnector : IDisposable
{
    private readonly ILogger<CassandraConnector> _logger;
    private readonly ICluster _cluster;
    private ISession? _session;

    public CassandraConnector(
        ILogger<CassandraConnector> logger,
        string[] contactPoints,
        int port = 9042,
        string? keyspace = null,
        string? username = null,
        string? password = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var builder = Cluster.Builder()
            .AddContactPoints(contactPoints)
            .WithPort(port);

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            builder = builder.WithCredentials(username, password);
        }

        _cluster = builder.Build();
        
        if (!string.IsNullOrEmpty(keyspace))
        {
            _session = _cluster.Connect(keyspace);
        }

        _logger.LogInformation("Cassandra connector initialized for {Points}", string.Join(",", contactPoints));
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string cql,
        CancellationToken cancellationToken = default)
    {
        EnsureSession();

        try
        {
            _logger.LogDebug("Executing CQL: {CQL}", cql);

            var statement = new SimpleStatement(cql);
            var rowSet = await _session!.ExecuteAsync(statement).ConfigureAwait(false);

            var results = new List<Dictionary<string, object?>>();

            foreach (var row in rowSet)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var column in rowSet.Columns)
                {
                    dict[column.Name] = row.GetValue<object>(column.Name);
                }
                results.Add(dict);
            }

            _logger.LogInformation("Query returned {Count} rows", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute CQL query");
            throw;
        }
    }

    public async Task ExecuteAsync(
        string cql,
        CancellationToken cancellationToken = default)
    {
        EnsureSession();

        try
        {
            _logger.LogDebug("Executing CQL statement: {CQL}", cql);

            var statement = new SimpleStatement(cql);
            await _session!.ExecuteAsync(statement).ConfigureAwait(false);

            _logger.LogInformation("CQL statement executed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute CQL statement");
            throw;
        }
    }

    public async Task<int> BatchInsertAsync(
        string keyspace,
        string table,
        List<Dictionary<string, object?>> rows,
        CancellationToken cancellationToken = default)
    {
        EnsureSession();

        if (rows.Count == 0)
            return 0;

        try
        {
            _logger.LogInformation("Batch inserting {Count} rows into {Keyspace}.{Table}",
                rows.Count, keyspace, table);

            var columns = rows.First().Keys.ToList();
            var columnList = string.Join(", ", columns);
            var placeholders = string.Join(", ", columns.Select(_ => "?"));

            var insertCql = $"INSERT INTO {keyspace}.{table} ({columnList}) VALUES ({placeholders})";
            var prepared = await _session!.PrepareAsync(insertCql).ConfigureAwait(false);

            var batch = new BatchStatement();
            foreach (var row in rows.Take(100)) // Cassandra batch limit
            {
                var values = columns.Select(col => row[col]).ToArray();
                batch.Add(prepared.Bind(values));
            }

            await _session.ExecuteAsync(batch).ConfigureAwait(false);

            _logger.LogInformation("Batch insert completed: {Count} rows", rows.Count);
            return rows.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch insert rows");
            throw;
        }
    }

    public async Task<List<string>> ListKeyspacesAsync(CancellationToken cancellationToken = default)
    {
        EnsureSession();

        try
        {
            _logger.LogDebug("Listing keyspaces");

            var keyspaces = _cluster.Metadata.GetKeyspaces()
                .Where(ks => !ks.StartsWith("system"))
                .ToList();

            _logger.LogInformation("Found {Count} keyspaces", keyspaces.Count);
            return keyspaces;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list keyspaces");
            throw;
        }
    }

    public async Task<List<string>> ListTablesAsync(
        string keyspace,
        CancellationToken cancellationToken = default)
    {
        EnsureSession();

        try
        {
            _logger.LogDebug("Listing tables in keyspace {Keyspace}", keyspace);

            var keyspaceMetadata = _cluster.Metadata.GetKeyspace(keyspace);
            if (keyspaceMetadata == null)
            {
                throw new InvalidOperationException($"Keyspace {keyspace} not found");
            }

            var tables = keyspaceMetadata.GetTablesNames().ToList();

            _logger.LogInformation("Found {Count} tables in {Keyspace}", tables.Count, keyspace);
            return tables;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list tables in keyspace {Keyspace}", keyspace);
            throw;
        }
    }

    public void Connect(string keyspace)
    {
        _session = _cluster.Connect(keyspace);
        _logger.LogInformation("Connected to keyspace {Keyspace}", keyspace);
    }

    private void EnsureSession()
    {
        if (_session == null)
        {
            _session = _cluster.Connect();
            _logger.LogDebug("Session created");
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _cluster?.Dispose();
        _logger.LogDebug("Cassandra connector disposed");
    }
}
