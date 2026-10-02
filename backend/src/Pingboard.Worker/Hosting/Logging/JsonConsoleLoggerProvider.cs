using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
///     Провайдер структурных логов в stdout. Заменяет Microsoft.Extensions.Logging.Console
///     (пакета нет в оффлайн-фиде) и повторяет поведение AddJsonConsole: одна JSON-строка на событие,
///     scope'ы (в Api это correlation id) тоже попадают в запись.
/// </summary>
public sealed class JsonConsoleLoggerProvider : ILoggerProvider
{
    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName)
    {
        return new JsonConsoleLogger(categoryName, _gate);
    }

    public void Dispose()
    {
        // Освобождать нечего: пишем в stdout процесса.
    }
}
