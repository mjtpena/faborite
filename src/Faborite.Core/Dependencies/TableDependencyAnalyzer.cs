using Microsoft.Extensions.Logging;

namespace Faborite.Core.Dependencies;

/// <summary>
/// Analyzes and manages table dependencies for proper sync ordering.
/// Issues #27, #28
/// </summary>
public class TableDependencyAnalyzer
{
    private readonly ILogger<TableDependencyAnalyzer> _logger;

    public TableDependencyAnalyzer(ILogger<TableDependencyAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects table dependencies by analyzing foreign key relationships.
    /// </summary>
    public async Task<DependencyGraph> AnalyzeDependenciesAsync(
        List<TableMetadata> tables,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing dependencies for {Count} tables", tables.Count);

        var graph = new DependencyGraph();
        
        foreach (var table in tables)
        {
            graph.AddNode(table.Name);
            
            // Analyze foreign keys to detect dependencies
            foreach (var fk in table.ForeignKeys)
            {
                var referencedTable = fk.ReferencedTable;
                if (tables.Any(t => t.Name == referencedTable))
                {
                    graph.AddEdge(table.Name, referencedTable);
                    _logger.LogDebug("Detected dependency: {Table} depends on {Referenced}", 
                        table.Name, referencedTable);
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Performs topological sort to determine sync order.
    /// </summary>
    public List<string> GetSyncOrder(DependencyGraph graph)
    {
        var sorted = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node))
            {
                TopologicalSort(node, graph, visited, visiting, sorted);
            }
        }

        sorted.Reverse();
        _logger.LogInformation("Sync order determined: {Order}", string.Join(" -> ", sorted));
        return sorted;
    }

    private void TopologicalSort(
        string node,
        DependencyGraph graph,
        HashSet<string> visited,
        HashSet<string> visiting,
        List<string> sorted)
    {
        if (visiting.Contains(node))
        {
            _logger.LogWarning("Circular dependency detected involving {Table}", node);
            return;
        }

        if (visited.Contains(node))
            return;

        visiting.Add(node);

        foreach (var dependency in graph.GetDependencies(node))
        {
            TopologicalSort(dependency, graph, visited, visiting, sorted);
        }

        visiting.Remove(node);
        visited.Add(node);
        sorted.Add(node);
    }

    /// <summary>
    /// Detects circular dependencies in the graph.
    /// </summary>
    public List<List<string>> DetectCircularDependencies(DependencyGraph graph)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>();
        var stack = new Stack<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node))
            {
                DetectCycles(node, graph, visited, stack, cycles);
            }
        }

        if (cycles.Any())
        {
            _logger.LogWarning("Detected {Count} circular dependencies", cycles.Count);
        }

        return cycles;
    }

    private void DetectCycles(
        string node,
        DependencyGraph graph,
        HashSet<string> visited,
        Stack<string> stack,
        List<List<string>> cycles)
    {
        visited.Add(node);
        stack.Push(node);

        foreach (var dependency in graph.GetDependencies(node))
        {
            if (!visited.Contains(dependency))
            {
                DetectCycles(dependency, graph, visited, stack, cycles);
            }
            else if (stack.Contains(dependency))
            {
                // Found a cycle
                var cycle = new List<string>();
                var cycleStart = false;
                foreach (var item in stack.Reverse())
                {
                    if (item == dependency)
                        cycleStart = true;
                    if (cycleStart)
                        cycle.Add(item);
                }
                cycles.Add(cycle);
            }
        }

        stack.Pop();
    }
}

/// <summary>
/// Represents table metadata including foreign key relationships.
/// </summary>
public record TableMetadata(string Name, List<ForeignKeyInfo> ForeignKeys);

public record ForeignKeyInfo(string ColumnName, string ReferencedTable, string ReferencedColumn);

/// <summary>
/// Directed graph representing table dependencies.
/// </summary>
public class DependencyGraph
{
    private readonly Dictionary<string, List<string>> _adjacencyList = new();

    public IEnumerable<string> Nodes => _adjacencyList.Keys;

    public void AddNode(string table)
    {
        if (!_adjacencyList.ContainsKey(table))
        {
            _adjacencyList[table] = new List<string>();
        }
    }

    public void AddEdge(string from, string to)
    {
        AddNode(from);
        AddNode(to);
        _adjacencyList[from].Add(to);
    }

    public List<string> GetDependencies(string table)
    {
        return _adjacencyList.TryGetValue(table, out var deps) ? deps : new List<string>();
    }

    public int GetDependencyCount(string table)
    {
        return _adjacencyList.Values.Count(list => list.Contains(table));
    }
}
