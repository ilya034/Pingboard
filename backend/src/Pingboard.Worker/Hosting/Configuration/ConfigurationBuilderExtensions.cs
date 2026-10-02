using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

public static class ConfigurationBuilderExtensions
{
    /// <summary>Добавляет JSON-файл, если он существует (обязательность задаёт вызывающий код).</summary>
    public static IConfigurationBuilder AddJsonFileIfExists(this IConfigurationBuilder builder, string path)
    {
        return File.Exists(path) ? builder.Add(new JsonFileConfigurationSource(path)) : builder;
    }

    /// <summary>
    ///     Переменные окружения. Пакет Configuration.EnvironmentVariables недоступен в оффлайн-фиде,
    ///     а это ключевой источник: <c>Worker__MaxParallel=8</c> → ключ <c>Worker:MaxParallel</c>.
    /// </summary>
    public static IConfigurationBuilder AddProcessEnvironmentVariables(this IConfigurationBuilder builder)
    {
        return builder.Add(new EnvironmentVariablesConfigurationSource());
    }
}
