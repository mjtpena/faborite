using Microsoft.Extensions.Logging;

namespace Faborite.Core.Streaming;

/// <summary>
/// Streaming data transfer to reduce memory footprint.
/// Issue #35
/// </summary>
public class StreamingDataTransfer
{
    private readonly ILogger<StreamingDataTransfer> _logger;
    private readonly int _batchSize;

    public StreamingDataTransfer(ILogger<StreamingDataTransfer> logger, int batchSize = 10000)
    {
        _logger = logger;
        _batchSize = batchSize;
    }

    /// <summary>
    /// Transfers data in batches using streaming to minimize memory usage.
    /// </summary>
    public async Task<TransferResult> TransferAsync(
        Stream source,
        Stream destination,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var totalBytes = 0L;
        var batchCount = 0;

        try
        {
            var buffer = new byte[_batchSize];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                
                totalBytes += bytesRead;
                batchCount++;

                progress?.Report(new TransferProgress(
                    BytesTransferred: totalBytes,
                    BatchesProcessed: batchCount,
                    ElapsedTime: DateTime.UtcNow - startTime
                ));

                _logger.LogTrace("Transferred batch {Batch}: {Bytes} bytes", batchCount, bytesRead);
            }

            await destination.FlushAsync(cancellationToken);
            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Completed streaming transfer: {Bytes} bytes in {Batches} batches ({Duration}ms)",
                totalBytes, batchCount, duration.TotalMilliseconds);

            return new TransferResult(
                Success: true,
                BytesTransferred: totalBytes,
                BatchCount: batchCount,
                Duration: duration,
                ThroughputMBps: totalBytes / duration.TotalSeconds / 1024 / 1024
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming transfer failed after {Bytes} bytes", totalBytes);
            throw;
        }
    }

    /// <summary>
    /// Transfers data with compression enabled.
    /// </summary>
    public async Task<TransferResult> TransferCompressedAsync(
        Stream source,
        Stream destination,
        CompressionType compression,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Stream compressedStream = compression switch
        {
            CompressionType.Gzip => new System.IO.Compression.GZipStream(destination, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
            CompressionType.Brotli => new System.IO.Compression.BrotliStream(destination, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
            CompressionType.Deflate => new System.IO.Compression.DeflateStream(destination, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
            _ => destination
        };

        try
        {
            return await TransferAsync(source, compressedStream, progress, cancellationToken);
        }
        finally
        {
            if (compressedStream != destination)
            {
                await compressedStream.DisposeAsync();
            }
        }
    }
}

public enum CompressionType
{
    None,
    Gzip,
    Brotli,
    Deflate
}

public record TransferProgress(
    long BytesTransferred,
    int BatchesProcessed,
    TimeSpan ElapsedTime);

public record TransferResult(
    bool Success,
    long BytesTransferred,
    int BatchCount,
    TimeSpan Duration,
    double ThroughputMBps);
