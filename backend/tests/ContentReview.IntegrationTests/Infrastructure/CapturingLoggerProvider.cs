using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ContentReview.IntegrationTests.Infrastructure;

/// <summary>One captured log entry with the scopes that were active when it was written.</summary>
public sealed record CapturedLog(LogLevel Level, string Category, string Message, Exception? Exception, IReadOnlyList<string> Scopes)
{
    public string Everything => $"{Message} {string.Join(' ', Scopes)} {Exception}";
}

/// <summary>Keeps every log entry (and its scopes) in memory so tests can look at what the API logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            owner._scopes.ForEachScope(
                (scope, list) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        list.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                    }
                    else
                    {
                        list.Add(scope?.ToString() ?? string.Empty);
                    }
                },
                scopes);

            owner._entries.Enqueue(new CapturedLog(logLevel, category, formatter(state, exception), exception, scopes));
        }
    }
}
