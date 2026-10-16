using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
/// Логгер с выводом структурированных событий в формате JSON в стандартный поток вывода (stdout).
/// Формат полей согласуется со схемой AddJsonConsole веб-приложения.
/// </summary>
internal sealed class JsonConsoleLogger(string category, Lock gate, TextWriter? writer = null) : ILogger
{
    /// <summary>
    ///     Формат времени — с экранированными ":": в кастомных форматах .NET ":" это
    ///     TimeSeparator текущей культуры, и в части культур время получилось бы "12.30.00".
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH\\:mm\\:ss.fffZ";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return new Scope(ScopeStack.Push(state!));
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Провайдер не решает, что писать: порог задаётся конфигурацией (Logging:LogLevel)
        // и применяется фильтрами LoggerFactory. Хардкод ">= Information" делал бы
        // Logging__LogLevel__Default нерабочим — ни поднять порог, ни опустить.
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var payload = new Dictionary<string, object?>
        {
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString(TimestampFormat),
            ["LogLevel"] = logLevel.ToString(),
            ["Category"] = category,
            ["EventId"] = eventId.Id,
            ["Message"] = formatter(state, exception)
        };

        if (eventId.Name is not null) payload["Event"] = eventId.Name;

        if (state is IEnumerable<KeyValuePair<string, object?>> statePairs)
        {
            var stateDict = new Dictionary<string, object?>();
            foreach (var pair in statePairs)
            {
                stateDict[pair.Key] = pair.Value;
            }

            if (stateDict.Count > 0) payload["State"] = stateDict;
        }

        var scopes = ScopeStack.Current;
        if (scopes.Count > 0)
        {
            var formattedScopes = new List<object>(scopes.Count);
            foreach (var scope in scopes)
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                {
                    var scopeDict = new Dictionary<string, object?>();
                    foreach (var pair in scopePairs)
                    {
                        scopeDict[pair.Key] = pair.Value;
                    }

                    formattedScopes.Add(scopeDict);
                }
                else
                {
                    formattedScopes.Add(scope);
                }
            }

            payload["Scopes"] = formattedScopes;
        }

        if (exception is not null) payload["Exception"] = exception.ToString();

        var line = JsonSerializer.Serialize(payload, SerializerOptions);

        lock (gate)
        {
            (writer ?? Console.Out).WriteLine(line);
        }
    }
}
