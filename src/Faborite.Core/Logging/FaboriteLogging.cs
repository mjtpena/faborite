using Microsoft.Extensions.Logging;

namespace Faborite.Core;

/// <summary>
/// Static logger factory for Faborite.
/// </summary>
public static class FaboriteLogging
{
    private static ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initialize the logger factory. Call this at application startup.
    /// </summary>
    public static void Initialize(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Create a logger for the specified type.
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        return _loggerFactory?.CreateLogger<T>() ?? new NullLogger<T>();
    }

    /// <summary>
    /// Create a logger with the specified name.
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        return _loggerFactory?.CreateLogger(categoryName) ?? new NullLogger();
    }
}

/// <summary>
/// Null logger implementation that does nothing.
/// </summary>
internal class NullLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

/// <summary>
/// Null logger implementation that does nothing.
/// </summary>
internal class NullLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
