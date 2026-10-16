using System.Globalization;
using Microsoft.Extensions.Configuration;
using Pingboard.Worker.Hosting.Configuration;

namespace Pingboard.Worker.Hosting;

/// <summary>
/// Режим проверки работоспособности воркера (healthcheck) для внешних систем мониторинга (Docker healthcheck, Kubernetes probes).
/// Проверяет наличие и актуальность файла пульса (heartbeat), обновляемого фоновым циклом выполнения проверок.
/// Возвращает 0, если пульс актуален, или 1 при его отсутствии, повреждении или устаревании.
/// </summary>
public static class WorkerSelfCheck
{
    /// <summary>Имя флага командной строки для запуска в режиме проверки: <c>--self-check</c>.</summary>
    public const string FlagName = "--self-check";

    /// <summary>Ключ конфигурации, переопределяющий допустимый возраст пульса: <c>Worker:HeartbeatMaxAgeSeconds</c>.</summary>
    public const string MaxAgeSecondsKey = "Worker:HeartbeatMaxAgeSeconds";

    /// <summary>
    /// Допустимый возраст пульса по умолчанию (120 секунд).
    /// </summary>
    public const int DefaultMaxAgeSeconds = 120;

    /// <summary>
    /// Выполняет проверку аргументов и запуск процедуры самопроверки, если передан соответствующий флаг.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <param name="now">Текущее время.</param>
    /// <param name="configuration">Экземпляр конфигурации (опционально, для тестирования).</param>
    /// <returns>Код завершения процесса (0 или 1), либо null, если запуск запрошен в стандартном режиме воркера.</returns>
    public static int? Run(string[] args, DateTimeOffset now, IConfiguration? configuration = null)
    {
        var selfCheck = args
            .Select((value, index) => (value, index))
            .FirstOrDefault(x => string.Equals(x.value, FlagName, StringComparison.Ordinal));

        if (selfCheck.value is null) return null;

        // Конфигурация собирается из appsettings.json и переменных окружения.
        configuration ??= BuildConfiguration();

        var options = ResolveOptions(configuration, args);
        var status = Inspect(options.Path, now, TimeSpan.FromSeconds(options.MaxAgeSeconds));

        // Сообщение печатается всегда: в `docker inspect` и в логах пробы нужен текст причины,
        // а не только код возврата.
        Console.WriteLine($"[self-check] {status.Message}");

        return status.Healthy ? 0 : 1;
    }

    /// <summary>Конфигурация режима самопроверки: те же файлы и env, что у обычного старта воркера.</summary>
    private static IConfiguration BuildConfiguration()
    {
        var basePath = AppContext.BaseDirectory;

        return new ConfigurationBuilder()
            .AddJsonFileIfExists(Path.Combine(basePath, "appsettings.json"))
            .AddJsonFileIfExists(Path.Combine(basePath, $"appsettings.{EnvironmentName()}.json"))
            .AddProcessEnvironmentVariables()
            .Build();
    }

    /// <summary>Настройки режима: путь пульса и порог свежести.</summary>
    public readonly record struct SelfCheckOptions(string Path, int MaxAgeSeconds);

    /// <summary>Результат проверки: годен/не годен и текст причины для лога.</summary>
    public readonly record struct SelfCheckStatus(bool Healthy, string Message);

    /// <summary>
    ///     Разбирает настройки: значение из конфигурации, переопределённое аргументом
    ///     <c>--heartbeat &lt;путь&gt;</c> (путь удобно задать в пробе или healthcheck,
    ///     не меняя окружение контейнера). Аргумент <c>--max-age &lt;секунды&gt;</c> нужен
    ///     для ручной проверки и тестов; в манифестах используется порог по умолчанию.
    /// </summary>
    public static SelfCheckOptions ResolveOptions(IConfiguration configuration, string[] args)
    {
        var path = ArgumentValue(args, "--heartbeat")
                   ?? configuration["Worker:HeartbeatPath"]
                   ?? string.Empty;

        var rawMaxAge = ArgumentValue(args, "--max-age")
                        ?? configuration[MaxAgeSecondsKey];

        var maxAge = int.TryParse(rawMaxAge, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DefaultMaxAgeSeconds;

        // Порог меньше или равный нулю означал бы «нездоров всегда» — это ошибка конфигурации,
        // и лучше вернуться к значению по умолчанию, чем уронить стенд.
        if (maxAge <= 0) maxAge = DefaultMaxAgeSeconds;

        return new SelfCheckOptions(path, maxAge);
    }

    /// <summary>
    /// Проверяет актуальность пульса по метке времени внутри файла.
    /// Не зависит от точности времени изменения файла в файловой системе.
    /// </summary>
    public static SelfCheckStatus Inspect(string path, DateTimeOffset now, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new SelfCheckStatus(false, "Worker__HeartbeatPath пуст: пульс не пишется, проверить живость нечем.");

        if (!File.Exists(path))
            return new SelfCheckStatus(false, $"Пульса нет: {path} ещё не написан (или воркер не стартовал).");

        string raw;
        try
        {
            raw = File.ReadAllText(path).Trim();
        }
        catch (Exception ex)
        {
            return new SelfCheckStatus(false, $"Пульс не читается ({path}): {ex.GetType().Name}.");
        }

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var stamp))
            return new SelfCheckStatus(false, $"Пульс повреждён: в {path} не отметка времени, а {raw.Length} символов.");

        var age = now - stamp;
        var ageText = age.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture);

        return age <= maxAge
            ? new SelfCheckStatus(true, $"Пульс свежий: возраст {ageText} с (порог {maxAge.TotalSeconds:F0} с).")
            : new SelfCheckStatus(false, $"Пульс устарел: возраст {ageText} с (порог {maxAge.TotalSeconds:F0} с).");
    }

    /// <summary>Значение аргумента вида <c>--имя значение</c>; null, если аргумента нет.</summary>
    private static string? ArgumentValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];

        return null;
    }

    /// <summary>Имя окружения — то же правило, что у хоста: DOTNET_ENVIRONMENT, затем ASPNETCORE_ENVIRONMENT.</summary>
    private static string EnvironmentName() =>
        Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? "Production";
}
