using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
///     Одна JSON-строка на событие в stdout (фактор XI). Имена полей намеренно совпадают
///     со схемой <c>AddJsonConsole</c> из Api: логи обоих процессов разбирает один пайплайн.
/// </summary>
internal sealed class JsonConsoleLogger(string category, Lock gate) : ILogger
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
            ["Event"] = eventId.Name,
            ["Message"] = formatter(state, exception)
        };

        var scopes = ScopeStack.Current;
        if (scopes.Count > 0) payload["Scopes"] = scopes;

        if (exception is not null) payload["Exception"] = exception.ToString();

        var line = JsonSerializer.Serialize(payload, SerializerOptions);

        lock (gate)
        {
            Console.Out.WriteLine(line);
        }
    }
}
