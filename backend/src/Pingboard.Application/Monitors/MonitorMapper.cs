using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Monitors;

/// <summary>
///     Собирает DTO-представление монитора. Здесь же живёт арифметика полосы доступности,
///     чтобы её можно было покрыть юнит-тестами без БД.
/// </summary>
public static class MonitorMapper
{
    public static MonitorDto ToDto(Monitor monitor, MonitorStatusSummary? summary = null)
    {
        var state = summary ?? new MonitorStatusSummary(null, null, []);

        return new MonitorDto(
            monitor.Id,
            monitor.Name,
            monitor.Url,
            monitor.IntervalSeconds,
            monitor.Enabled,
            monitor.LastOk,
            monitor.LastCheckedAt,
            monitor.CreatedAt,
            monitor.UpdatedAt,
            state.UptimeRatio,
            state.LastLatencyMs,
            state.UptimeBar);
    }

    public static MonitorCheckDto ToDto(CheckResult check)
    {
        return new MonitorCheckDto(check.Id, check.CheckedAt, check.Ok, check.StatusCode, check.LatencyMs, check.Error);
    }

    /// <summary>
    ///     Статус монитора сразу после создания/обновления: проверок за окно ещё нет, но полоса
    ///     должна быть той же длины, что и на дашборде, — иначе клиент рисует другую разметку
    ///     (пустой массив против массива из «unknown»-сегментов).
    /// </summary>
    public static MonitorStatusSummary EmptySummary(DateTimeOffset from, DateTimeOffset to, int segments)
    {
        return new MonitorStatusSummary(null, null, BuildUptimeBar(Array.Empty<CheckResult>(), from, to, segments));
    }

    /// <summary>
    ///     Раскладывает проверки по сегментам окна (по умолчанию 24 часа на 24 сегмента).
    ///     Сегмент без проверок остаётся unknown — это видно на дашборде серым.
    ///     Полоса из этого метода точна ровно настолько, насколько полна переданная история:
    ///     если она ограничена лимитом, дальние сегменты окна окажутся unknown (см.
    ///     <see cref="BuildUptimeBar(IEnumerable{CheckBucket}, DateTimeOffset, DateTimeOffset, int)" />).
    /// </summary>
    public static IReadOnlyList<UptimeBucketDto> BuildUptimeBar(
        IEnumerable<CheckResult> checks,
        DateTimeOffset from,
        DateTimeOffset to,
        int segments)
    {
        if (segments <= 0) return [];

        var total = to - from;
        if (total <= TimeSpan.Zero) return [];

        // Окно почти никогда не делится на число сегментов нацело: остаток тиков
        // (total.Ticks % segments) отдаём последнему сегменту, иначе проверки из этого
        // «хвоста» выпадали бы из полосы совсем.
        var bucketSizeTicks = total.Ticks / segments;
        if (bucketSizeTicks <= 0) return [];

        var counters = new (int Total, int Failed)[segments];

        foreach (var check in checks)
        {
            var offset = check.CheckedAt - from;
            if (offset < TimeSpan.Zero || offset >= total) continue;

            var index = (int)Math.Min(segments - 1, offset.Ticks / bucketSizeTicks);
            if (index < 0 || index >= segments) continue;

            counters[index].Total++;
            if (!check.Ok) counters[index].Failed++;
        }

        return ToBar(counters, from, to, segments);
    }

    /// <summary>
    ///     Та же полоса, но из готового агрегата по сегментам (один GROUP BY в БД). Основной путь
    ///     дашборда: он не зависит от глубины подтянутой истории, поэтому полоса покрывает окно
    ///     целиком и согласована с uptime за то же окно.
    /// </summary>
    public static IReadOnlyList<UptimeBucketDto> BuildUptimeBar(
        IEnumerable<CheckBucket> buckets,
        DateTimeOffset from,
        DateTimeOffset to,
        int segments)
    {
        if (segments <= 0) return [];

        var total = to - from;
        if (total <= TimeSpan.Zero || total.Ticks / segments <= 0) return [];

        var counters = new (int Total, int Failed)[segments];

        foreach (var bucket in buckets)
        {
            // Номер сегмента пришёл из БД: он может оказаться за границей (последний тик окна,
            // остаток от деления окна на сегменты). Клампим — ровно как это делает разбор истории.
            var index = Math.Clamp(bucket.Index, 0, segments - 1);
            counters[index].Total += bucket.Total;
            counters[index].Failed += bucket.Failed;
        }

        return ToBar(counters, from, to, segments);
    }

    /// <summary>
    ///     Границы сегментов — единственное место, где они вычисляются: и полоса из истории,
    ///     и полоса из агрегата обязаны иметь одинаковую разметку по времени.
    /// </summary>
    private static IReadOnlyList<UptimeBucketDto> ToBar(
        (int Total, int Failed)[] counters,
        DateTimeOffset from,
        DateTimeOffset to,
        int segments)
    {
        var bucketSize = TimeSpan.FromTicks((to - from).Ticks / segments);
        var bar = new List<UptimeBucketDto>(segments);

        for (var i = 0; i < segments; i++)
        {
            var bucketFrom = from + bucketSize * i;
            // Последний сегмент закрывает окно ровно до to, вместе с остатком тиков.
            var bucketTo = i == segments - 1 ? to : bucketFrom + bucketSize;
            bar.Add(new UptimeBucketDto(bucketFrom, bucketTo, counters[i].Total, counters[i].Failed));
        }

        return bar;
    }
}
