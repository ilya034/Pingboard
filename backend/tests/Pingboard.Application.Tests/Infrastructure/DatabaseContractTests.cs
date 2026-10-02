using Microsoft.EntityFrameworkCore;
using Pingboard.Api.Infrastructure;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Repositories;

namespace Pingboard.Application.Tests.Infrastructure;

/// <summary>
///     Контракты запросов, которые видит только Postgres: планировщик воркера и лимиты дашборда.
///     На этой машине нет ни Docker, ни работающего Postgres, поэтому проверяется сам SQL
///     (<c>ToQueryString</c>): это ловит ровно те ошибки, которые иначе всплыли бы только
///     на живом стенде, — NULLS LAST в ORDER BY, «top N на группу», N+1 в uptime и
///     расхождение имени UNIQUE-индекса с тем, что ждёт маппер ошибок.
/// </summary>
public sealed class DatabaseContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ListDueQuery_PutsNeverCheckedMonitorsFirst()
    {
        using var db = TestUptimeDbContext.Create();

        var sql = MonitorRepository.ListDueQuery(db, Now, 10).ToQueryString();

        // Голый ORDER BY last_checked_at в Postgres ставит NULL (ни разу не проверенные
        // мониторы) В КОНЕЦ — при заполненном батче они не проверялись бы никогда.
        Assert.Contains("ORDER BY COALESCE(m.last_checked_at", sql);
        Assert.Contains("LIMIT", sql);
    }

    [Fact]
    public void HistoryForManyQuery_LimitsRowsPerMonitorInOneScan()
    {
        using var db = TestUptimeDbContext.Create();

        var sql = CheckRepository.HistoryForManyQuery(db, [Guid.NewGuid(), Guid.NewGuid()], Now, Now, 5)
            .ToQueryString();

        // Лимит — на монитор, а не на всю выборку: иначе частый монитор вытесняет редкие.
        Assert.Contains("row_number() OVER (PARTITION BY c.monitor_id ORDER BY c.checked_at DESC)", sql);
        Assert.Contains("ranked.rn <= @limit_per_monitor", sql);

        // И это по-прежнему один запрос, а не N обращений (и не UNION из N подзапросов).
        Assert.Equal(1, Occurrences(sql, "FROM checks"));
        Assert.DoesNotContain("UNION", sql);
    }

    [Fact]
    public void UptimeRatiosQuery_AggregatesAllMonitorsInOneQuery()
    {
        using var db = TestUptimeDbContext.Create();

        var sql = CheckRepository.UptimeRatiosQuery(db, [Guid.NewGuid(), Guid.NewGuid()], Now, Now).ToQueryString();

        // Один GROUP BY на весь дашборд вместо двух CountAsync на каждый монитор (2N+1 → 3).
        Assert.Contains("GROUP BY c.monitor_id", sql);
        Assert.Contains("count(*) FILTER (WHERE c.ok)", sql);
        Assert.Equal(1, Occurrences(sql, "FROM checks"));
    }

    [Fact]
    public void UniqueEmailIndex_HasTheNameErrorMapperExpects()
    {
        using var db = TestUptimeDbContext.Create();

        var index = db.Model.FindEntityType(typeof(User))!.GetIndexes().Single(i => i.IsUnique);

        // Маппер отличает гонку регистраций от прочих нарушений уникальности по имени индекса:
        // если схему переименуют, ошибка перестанет превращаться в 400 по полю email.
        Assert.Equal(ExceptionStatusMapper.UsersEmailIndex, index.GetDatabaseName());
    }

    private static int Occurrences(string text, string value)
    {
        return text.Split(value).Length - 1;
    }
}
