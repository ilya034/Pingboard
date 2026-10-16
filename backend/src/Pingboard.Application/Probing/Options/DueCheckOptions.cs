namespace Pingboard.Application.Probing.Options;

/// <summary>Параметры выполнения очереди проверок доступности.</summary>
public sealed class DueCheckOptions
{
    public const string SectionName = "Worker";

    /// <summary>Размер пакета мониторов, запрашиваемых за одну итерацию.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Максимальное количество параллельно выполняемых проверок.</summary>
    public int MaxParallel { get; set; } = 8;
}
