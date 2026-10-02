namespace Pingboard.Application.Monitors.Options;

/// <summary>Настройки, влияющие на представление данных мониторов.</summary>
public sealed class MonitorsOptions
{
    public const string SectionName = "Monitors";

    /// <summary>Окно расчёта uptime и полосы доступности.</summary>
    public int UptimeWindowHours { get; set; } = 24;

    /// <summary>Сколько сегментов в полосе (по умолчанию час на сегмент).</summary>
    public int UptimeBarSegments { get; set; } = 24;

    /// <summary>Сколько последних проверок читать для дашборда на один монитор.</summary>
    public int DashboardHistoryPerMonitor { get; set; } = 500;
}
