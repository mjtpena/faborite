using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Faborite.Core.Json;

/// <summary>
/// JSON processing and transformation capabilities.
/// Issue #55
/// </summary>
public class JsonProcessor
{
    private readonly ILogger<JsonProcessor> _logger;

    public JsonProcessor(ILogger<JsonProcessor> logger)
    {
        _logger = logger;
    }

    public JsonElement ExtractValue(string json, string jsonPath)
    {
        _logger.LogDebug("Extracting JSON path: {Path}", jsonPath);
        
        var doc = JsonDocument.Parse(json);
        return NavigateJsonPath(doc.RootElement, jsonPath);
    }

    public string TransformJson(string json, List<JsonTransformation> transformations)
    {
        var doc = JsonDocument.Parse(json);
        var mutableJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();

        foreach (var transform in transformations)
        {
            ApplyTransformation(mutableJson, transform);
        }

        return JsonSerializer.Serialize(mutableJson, new JsonSerializerOptions { WriteIndented = true });
    }

    public List<Dictionary<string, object>> FlattenJson(string json)
    {
        _logger.LogInformation("Flattening JSON structure");
        
        var doc = JsonDocument.Parse(json);
        var flattened = new List<Dictionary<string, object>>();
        
        FlattenElement(doc.RootElement, "", flattened);
        
        return flattened;
    }

    private void FlattenElement(JsonElement element, string prefix, List<Dictionary<string, object>> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var newPrefix = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenElement(property.Value, newPrefix, result);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenElement(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;

            default:
                var dict = new Dictionary<string, object> { [prefix] = GetValue(element) };
                result.Add(dict);
                break;
        }
    }

    private JsonElement NavigateJsonPath(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var property))
            {
                current = property;
            }
            else
            {
                throw new ArgumentException($"Path not found: {path}");
            }
        }

        return current;
    }

    private void ApplyTransformation(Dictionary<string, object> json, JsonTransformation transform)
    {
        switch (transform.Operation)
        {
            case JsonOperation.Add:
                json[transform.Path] = transform.Value ?? "";
                break;
            case JsonOperation.Remove:
                json.Remove(transform.Path);
                break;
            case JsonOperation.Rename:
                if (json.TryGetValue(transform.Path, out var value))
                {
                    json.Remove(transform.Path);
                    json[transform.NewPath ?? transform.Path] = value;
                }
                break;
            case JsonOperation.Replace:
                json[transform.Path] = transform.Value ?? "";
                break;
        }
    }

    private object GetValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }
}

public enum JsonOperation { Add, Remove, Rename, Replace }

public record JsonTransformation(
    JsonOperation Operation,
    string Path,
    object? Value = null,
    string? NewPath = null);
