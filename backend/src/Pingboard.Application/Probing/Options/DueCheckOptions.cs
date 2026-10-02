namespace Pingboard.Application.Probing.Options;

/// <summary>Настройки цикла проверок. Биндятся из секции Worker (env Worker__*).</summary>
public sealed class DueCheckOptions
{
    public const string SectionName = "Worker";

    /// <summary>Сколько мониторов берём в одну итерацию.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Сколько проверок выполняем параллельно (фактор VIII: concurrency).</summary>
    public int MaxParallel { get; set; } = 8;
}
