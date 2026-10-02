using System.Globalization;
using Microsoft.Extensions.Configuration;
using Pingboard.Worker.Hosting.Configuration;

namespace Pingboard.Worker.Hosting;

/// <summary>
///     Проба живости воркера (фактор IX): режим «проверить пульс и выйти», который вызывает
///     внешний наблюдатель — <c>docker healthcheck</c> или <c>livenessProbe</c> в k8s.
///
///     Зачем отдельный режим, а не shell-однострочник со <c>date</c> и <c>stat</c>:
///     у воркера нет HTTP-порта, поэтому единственный признак жизни — свежесть файла-пульса,
///     который пишет цикл проверок. Считать её можно снаружи (нужны GNU coreutils и sh
///     в образе) или изнутри — этим и занимается режим. Второй вариант не зависит от состава
///     образа: он работает и в distroless/chiseled, и на Windows-контейнере.
///
///     Коды возврата: 0 — пульс свежий, 1 — пульс старый, файла нет или он нечитаем.
///     Любой нештатный случай — это «нездоров», а не «не знаю»: для проб молчание опаснее
///     ложного перезапуска.
/// </summary>
public static class WorkerSelfCheck
{
    /// <summary>Имя режима в командной строке: <c>dotnet Pingboard.Worker.dll --self-check</c>.</summary>
    public const string FlagName = "--self-check";

    /// <summary>Ключ конфигурации, переопределяющий порог свежести: <c>Worker__HeartbeatMaxAgeSeconds</c>.</summary>
    public const string MaxAgeSecondsKey = "Worker:HeartbeatMaxAgeSeconds";

    /// <summary>
    ///     Порог по умолчанию. Должен совпадать с периодом healthcheck в compose/k8s:
    ///     воркер тикает каждые 5 с (Worker__TickSeconds), 120 с — это 24 пропущенных итерации,
    ///     то есть заведомо «повис», а не «чуть замешкался».
    /// </summary>
    public const int DefaultMaxAgeSeconds = 120;

    /// <summary>
    ///     Читает аргументы и, если запрошен режим проверки, возвращает код выхода.
    ///     <c>null</c> означает «режим не запрошен, запускай обычный цикл».
    ///
    ///     <paramref name="now" /> передаётся явно, чтобы тесты не зависели от системных часов
    ///     (в решении для этого принят <c>TimeProvider</c>, но здесь вызов ровно один, и
    ///     параметр делает функцию чистой). <paramref name="configuration" /> можно передать
    ///     готовую — тогда JSON-файлы и переменные окружения не читаются; так проверяются
    ///     порог и путь из конфигурации.
    /// </summary>
    public static int? Run(string[] args, DateTimeOffset now, IConfiguration? configuration = null)
    {
        var selfCheck = args
            .Select((value, index) => (value, index))
            .FirstOrDefault(x => string.Equals(x.value, FlagName, StringComparison.Ordinal));

        if (selfCheck.value is null) return null;

        // Как и при обычном старте, конфигурация собирается из appsettings.json (если они
        // лежат рядом с бинарником) и переменных окружения — env последнее слово (фактор III).
        // Прочие аргументы командной строки не разбираются: их в воркере нет.
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
    ///     Проверяет свежесть пульса. Возраст считается по отметке ВНУТРИ файла, а не по
    ///     времени изменения файла: так проверка не зависит от точности ФС и работает
    ///     одинаково в контейнере, на смонтированном томе и на Windows.
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
