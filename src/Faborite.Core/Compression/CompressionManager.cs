using Microsoft.Extensions.Logging;

namespace Faborite.Core.Compression;

/// <summary>
/// Manages compression algorithm selection per table.
/// Issue #36
/// </summary>
public class CompressionManager
{
    private readonly ILogger<CompressionManager> _logger;

    public CompressionManager(ILogger<CompressionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Selects optimal compression algorithm based on data characteristics.
    /// </summary>
    public CompressionAlgorithm SelectOptimalAlgorithm(DataCharacteristics characteristics)
    {
        // High entropy data (already compressed, encrypted, random)
        if (characteristics.Entropy > 0.9)
        {
            _logger.LogDebug("High entropy detected, using None compression");
            return CompressionAlgorithm.None;
        }

        // Text-heavy data
        if (characteristics.TextRatio > 0.7)
        {
            _logger.LogDebug("Text-heavy data, using Gzip compression");
            return CompressionAlgorithm.Gzip;
        }

        // Numerical data
        if (characteristics.NumericRatio > 0.7)
        {
            _logger.LogDebug("Numeric data, using Snappy compression");
            return CompressionAlgorithm.Snappy;
        }

        // Mixed data, prioritize speed
        if (characteristics.SizeMB < 100)
        {
            _logger.LogDebug("Small dataset, using LZ4 for speed");
            return CompressionAlgorithm.Lz4;
        }

        // Large datasets, prioritize ratio
        _logger.LogDebug("Large dataset, using Zstd for compression ratio");
        return CompressionAlgorithm.Zstd;
    }

    /// <summary>
    /// Gets compression settings for a specific algorithm.
    /// </summary>
    public CompressionSettings GetSettings(CompressionAlgorithm algorithm, CompressionLevel level)
    {
        return algorithm switch
        {
            CompressionAlgorithm.None => new CompressionSettings(algorithm, 0, 1.0),
            CompressionAlgorithm.Snappy => new CompressionSettings(algorithm, 1, 0.5),
            CompressionAlgorithm.Lz4 => new CompressionSettings(algorithm, GetLz4Level(level), 0.6),
            CompressionAlgorithm.Zstd => new CompressionSettings(algorithm, GetZstdLevel(level), 0.3),
            CompressionAlgorithm.Gzip => new CompressionSettings(algorithm, GetGzipLevel(level), 0.4),
            CompressionAlgorithm.Brotli => new CompressionSettings(algorithm, GetBrotliLevel(level), 0.2),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
    }

    private int GetLz4Level(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.Fast => 3,
        CompressionLevel.Balanced => 5,
        CompressionLevel.Best => 9,
        _ => 5
    };

    private int GetZstdLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.Fast => 3,
        CompressionLevel.Balanced => 10,
        CompressionLevel.Best => 19,
        _ => 10
    };

    private int GetGzipLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.Fast => 3,
        CompressionLevel.Balanced => 6,
        CompressionLevel.Best => 9,
        _ => 6
    };

    private int GetBrotliLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.Fast => 4,
        CompressionLevel.Balanced => 8,
        CompressionLevel.Best => 11,
        _ => 8
    };

    /// <summary>
    /// Estimates compression ratio for given algorithm and data.
    /// </summary>
    public double EstimateCompressionRatio(CompressionAlgorithm algorithm, DataCharacteristics characteristics)
    {
        // These are rough estimates based on typical performance
        var baseRatio = algorithm switch
        {
            CompressionAlgorithm.None => 1.0,
            CompressionAlgorithm.Snappy => 0.5,
            CompressionAlgorithm.Lz4 => 0.45,
            CompressionAlgorithm.Zstd => 0.3,
            CompressionAlgorithm.Gzip => 0.35,
            CompressionAlgorithm.Brotli => 0.25,
            _ => 1.0
        };

        // Adjust based on entropy (higher entropy = less compressible)
        return baseRatio + (characteristics.Entropy * (1.0 - baseRatio));
    }
}

public enum CompressionAlgorithm
{
    None,
    Snappy,
    Lz4,
    Zstd,
    Gzip,
    Brotli
}

public enum CompressionLevel
{
    Fastest,
    Fast,
    Balanced,
    Best
}

public record CompressionSettings(
    CompressionAlgorithm Algorithm,
    int Level,
    double EstimatedSpeedRatio);

public record DataCharacteristics(
    double SizeMB,
    double Entropy,
    double TextRatio,
    double NumericRatio,
    double NullRatio)
{
    /// <summary>
    /// Analyzes data sample to determine characteristics.
    /// </summary>
    public static DataCharacteristics Analyze(byte[] sample)
    {
        var sizeMB = sample.Length / 1024.0 / 1024.0;
        var entropy = CalculateEntropy(sample);
        
        // Simple heuristics (would need more sophisticated analysis in production)
        var textRatio = 0.5; // Placeholder
        var numericRatio = 0.3; // Placeholder
        var nullRatio = 0.05; // Placeholder

        return new DataCharacteristics(sizeMB, entropy, textRatio, numericRatio, nullRatio);
    }

    private static double CalculateEntropy(byte[] data)
    {
        var frequencies = new int[256];
        foreach (var b in data)
        {
            frequencies[b]++;
        }

        var entropy = 0.0;
        var length = data.Length;

        foreach (var freq in frequencies)
        {
            if (freq == 0) continue;
            
            var probability = freq / (double)length;
            entropy -= probability * Math.Log2(probability);
        }

        // Normalize to 0-1 range (max entropy is 8 bits)
        return entropy / 8.0;
    }
}
