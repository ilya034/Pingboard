using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

/// <summary>
/// Источник конфигурации для чтения параметров из JSON-файлов.
/// Преобразует иерархическую структуру JSON в плоский набор ключей (например, <c>Worker:TickSeconds</c>) для связывания с Options.
/// </summary>
public sealed class JsonFileConfigurationSource(string path) : IConfigurationSource
{
    public string Path { get; } = path;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new JsonFileConfigurationProvider(Path);
    }
}
