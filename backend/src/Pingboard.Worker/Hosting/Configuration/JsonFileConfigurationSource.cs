using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

/// <summary>
///     Мини-провайдер конфигурации из JSON-файла.
///     ПОЧЕМУ СВОЙ: пакет Microsoft.Extensions.Configuration.Json отсутствует в оффлайн-фиде
///     (см. Directory.Packages.props). Провайдер читает файл один раз на старте и раскладывает
///     вложенные объекты в ключи вида <c>Worker:TickSeconds</c> — ровно то, что ожидает
///     биндинг Options.
///     Основной источник конфигурации в проде — переменные окружения (фактор III);
///     appsettings — удобство локального запуска. Когда появится nuget.org, этот файл
///     заменяется на штатный <c>AddJsonFile</c>.
/// </summary>
public sealed class JsonFileConfigurationSource(string path) : IConfigurationSource
{
    public string Path { get; } = path;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new JsonFileConfigurationProvider(Path);
    }
}
