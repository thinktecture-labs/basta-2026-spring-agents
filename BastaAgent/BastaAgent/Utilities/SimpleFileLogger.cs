using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Utilities;

public class SimpleFileLoggerProvider(string filePath) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopeProvider;
    private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(
            categoryName,
            name => new SimpleFileLogger(filePath, name, () => _scopeProvider)
        );
    }

    public void Dispose() { }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }
}

internal class SimpleFileLogger(
    string path,
    string category,
    Func<IExternalScopeProvider?> getScopeProvider
) : ILogger
{
    private static readonly object _lock = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        var scopeProvider = getScopeProvider();
        return scopeProvider?.Push(state) ?? NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (!IsEnabled(logLevel))
            return;
        var message = formatter(state, exception);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

        var lines = new List<string>
        {
            $"{timestamp} [{logLevel}] {category} ({eventId.Id}) {message}",
        };
        if (exception is not null)
        {
            lines.Add(exception.ToString());
        }

        // Include scope if present
        var scopeProvider = getScopeProvider();
        if (scopeProvider is not null)
        {
            scopeProvider.ForEachScope(
                (scope, builder) =>
                {
                    lines.Add($"=> Scope: {scope}");
                },
                state
            );
        }

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.AppendAllLines(path, lines);
        }
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        public void Dispose() { }
    }
}
