using System.Text.Json;

namespace Faborite.Core.Sync;

public record SyncHistoryEntry(string SyncId, DateTime Timestamp, string[] Tables, long TotalRows, TimeSpan Duration, bool Success, string? Error = null);

public class SyncHistoryTracker
{
    private const string HistoryFile = ".faborite/sync-history.json";
    private readonly List<SyncHistoryEntry> _history = new();

    public SyncHistoryTracker() => LoadHistory();

    public void AddEntry(SyncHistoryEntry entry)
    {
        _history.Add(entry);
        SaveHistory();
    }

    public IReadOnlyList<SyncHistoryEntry> GetHistory(int limit = 50) => _history.OrderByDescending(x => x.Timestamp).Take(limit).ToList();

    private void LoadHistory()
    {
        if (File.Exists(HistoryFile))
        {
            try
            {
                var json = File.ReadAllText(HistoryFile);
                var entries = JsonSerializer.Deserialize<List<SyncHistoryEntry>>(json);
                if (entries != null) _history.AddRange(entries);
            }
            catch { }
        }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFile, json);
        }
        catch { }
    }
}
