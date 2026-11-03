using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Scheduler.IntegrationTests.Repositories;

/// <summary>
/// Keeps every log entry in memory so tests can check what would reach production logs.
/// It also keeps the values of the active log scopes (for example the correlation id).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    // One provider instance can be shared by several test hosts (WithWebHostBuilder); each host gives its own
    // scope provider, so keep them all. Only the host that runs the current request has scopes in this async flow.
    private readonly ConcurrentQueue<IExternalScopeProvider> _scopeProviders = new();
    private readonly LoggerExternalScopeProvider _ownScopes = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProviders.Enqueue(scopeProvider);

    private IEnumerable<IExternalScopeProvider> ScopeProviders => _scopeProviders.IsEmpty ? new[] { _ownScopes } : _scopeProviders;

    public void Dispose()
    {
    }

    public sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message)
    {
        /// <summary>Key/value pairs of every scope that was active, outermost first.</summary>
        public IReadOnlyList<KeyValuePair<string, object?>> ScopeValues { get; init; } = [];

        /// <summary>The message, the exception text and every scope value, for "never logs X" checks.</summary>
        public string AllText { get; init; } = string.Empty;

        public object? Scope(string key) => ScopeValues.LastOrDefault(pair => pair.Key == key).Value;
    }

    private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._ownScopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = new List<KeyValuePair<string, object?>>();
            var text = new System.Text.StringBuilder();
            foreach (var scopes in owner.ScopeProviders)
            {
                scopes.ForEachScope(
                    (scope, _) =>
                    {
                        text.Append(scope).Append(' ');
                        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                        {
                            foreach (var pair in pairs)
                            {
                                values.Add(pair);
                                text.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
                            }
                        }
                    },
                    (object?)null);
            }

            var message = formatter(state, exception);
            text.Append(message).Append(' ').Append(exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> stateValues)
            {
                foreach (var pair in stateValues)
                {
                    text.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                }
            }

            owner._entries.Enqueue(new LogEntry(category, logLevel, eventId, message) { ScopeValues = values, AllText = text.ToString() });
        }
    }
}
