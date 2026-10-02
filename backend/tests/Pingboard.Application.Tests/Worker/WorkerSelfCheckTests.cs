using Microsoft.Extensions.Configuration;
using Pingboard.Worker.Hosting;

namespace Pingboard.Application.Tests.Worker;

/// <summary>
///     Проба живости воркера (фактор IX). Это единственное место, где «жив ли воркер»
///     решается кодом, а не наблюдением: у воркера нет HTTP-порта, поэтому признак жизни —
///     свежесть файла-пульса.
///
///     Тесты держат три вещи, которые иначе ломаются молча:
///     * режим не должен включаться сам (обычный запуск воркера не имеет права выйти с 0);
///     * «пульса нет» и «пульс устарел» — это НЕЗДОРОВ, а не «не знаю»: для liveness-пробы
///       молчание опаснее ложного перезапуска;
///     * возраст считается по отметке внутри файла, поэтому содержимое проверяется отдельно
///       от времени изменения файла.
/// </summary>
public sealed class WorkerSelfCheckTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pingboard-selfcheck-" + Guid.NewGuid().ToString("N"));

    public WorkerSelfCheckTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Каталог временный: не убрался — не повод ронять прогон тестов.
        }
    }

    private string WriteHeartbeat(DateTimeOffset stamp)
    {
        var path = Path.Combine(_dir, "heartbeat");
        File.WriteAllText(path, stamp.ToString("O"));
        return path;
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void Без_флага_режим_не_включается()
    {
        Assert.Null(WorkerSelfCheck.Run([], Now));
        Assert.Null(WorkerSelfCheck.Run(["dotnet", "worker/Pingboard.Worker.dll"], Now));
    }

    [Fact]
    public void Свежий_пульс_это_ноль()
    {
        var path = WriteHeartbeat(Now.AddSeconds(-30));

        var exitCode = WorkerSelfCheck.Run([WorkerSelfCheck.FlagName, "--heartbeat", path], Now);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Устаревший_пульс_это_единица()
    {
        // 121 с при пороге 120: ровно за границей, чтобы тест ловил сдвиг сравнения с <= на <.
        var path = WriteHeartbeat(Now.AddSeconds(-(WorkerSelfCheck.DefaultMaxAgeSeconds + 1)));

        var exitCode = WorkerSelfCheck.Run([WorkerSelfCheck.FlagName, "--heartbeat", path], Now);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Пульс_ровно_на_границе_порога_считается_свежим()
    {
        var path = WriteHeartbeat(Now.AddSeconds(-WorkerSelfCheck.DefaultMaxAgeSeconds));

        Assert.Equal(0, WorkerSelfCheck.Run([WorkerSelfCheck.FlagName, "--heartbeat", path], Now));
    }

    [Fact]
    public void Отсутствие_файла_пульса_это_нездоров()
    {
        var path = Path.Combine(_dir, "нет-такого-файла");

        var status = WorkerSelfCheck.Inspect(path, Now, TimeSpan.FromSeconds(120));

        Assert.False(status.Healthy);
        Assert.Contains("ещё не написан", status.Message);
    }

    [Fact]
    public void Пустой_путь_в_конфигурации_это_нездоров_с_объяснением()
    {
        var status = WorkerSelfCheck.Inspect(string.Empty, Now, TimeSpan.FromSeconds(120));

        Assert.False(status.Healthy);
        Assert.Contains("HeartbeatPath", status.Message);
    }

    [Fact]
    public void Битый_файл_пульса_это_нездоров_а_не_исключение()
    {
        var path = Path.Combine(_dir, "heartbeat");
        File.WriteAllText(path, "это не отметка времени");

        var status = WorkerSelfCheck.Inspect(path, Now, TimeSpan.FromSeconds(120));

        Assert.False(status.Healthy);
        Assert.Contains("повреждён", status.Message);
    }

    [Fact]
    public void Возраст_считается_по_содержимому_а_не_по_mtime()
    {
        var path = WriteHeartbeat(Now.AddMinutes(-10));

        // Файл только что записан (mtime = сейчас), но отметка внутри — десять минут назад.
        // Если бы проверка смотрела на mtime, воркер, писавший пульс один раз и повисший,
        // выглядел бы здоровым.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        Assert.False(WorkerSelfCheck.Inspect(path, Now, TimeSpan.FromSeconds(120)).Healthy);
    }

    [Fact]
    public void Порог_берётся_из_конфигурации()
    {
        var path = WriteHeartbeat(Now.AddSeconds(-300));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:HeartbeatPath"] = path,
                ["Worker:HeartbeatMaxAgeSeconds"] = "600",
            })
            .Build();

        var options = WorkerSelfCheck.ResolveOptions(configuration, [WorkerSelfCheck.FlagName]);

        Assert.Equal(path, options.Path);
        Assert.Equal(600, options.MaxAgeSeconds);
        Assert.Equal(0, WorkerSelfCheck.Run([WorkerSelfCheck.FlagName], Now, configuration));
    }

    [Fact]
    public void Кривой_порог_в_конфигурации_откатывается_к_умолчанию()
    {
        foreach (var broken in new[] { "0", "-5", "не число", "" })
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Worker:HeartbeatMaxAgeSeconds"] = broken,
                })
                .Build();

            var options = WorkerSelfCheck.ResolveOptions(configuration, [WorkerSelfCheck.FlagName]);

            Assert.Equal(WorkerSelfCheck.DefaultMaxAgeSeconds, options.MaxAgeSeconds);
        }
    }

    [Fact]
    public void Аргумент_перебивает_конфигурацию()
    {
        var fromConfig = WriteHeartbeat(Now.AddSeconds(-300));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:HeartbeatPath"] = fromConfig,
                ["Worker:HeartbeatMaxAgeSeconds"] = "10",
            })
            .Build();

        // Путь и порог из аргументов: именно так пробу можно настроить в манифесте,
        // не меняя окружение контейнера.
        var options = WorkerSelfCheck.ResolveOptions(
            configuration,
            [WorkerSelfCheck.FlagName, "--heartbeat", "/another/path", "--max-age", "30"]);

        Assert.Equal("/another/path", options.Path);
        Assert.Equal(30, options.MaxAgeSeconds);
    }
}
