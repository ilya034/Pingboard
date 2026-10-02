using Microsoft.EntityFrameworkCore;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Application.Tests.Infrastructure;

/// <summary>
///     DbContext для проверки <b>текста</b> SQL, который строят репозитории.
///     Соединение не открывается: ToQueryString работает офлайн, строка подключения нужна
///     только для того, чтобы провайдер Npgsql собрал команду. Живой Postgres на машине нет,
///     поэтому это единственная проверка планировщика и «top N на монитор» — см. §9 README.
/// </summary>
public static class TestUptimeDbContext
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=pingboard-tests;Username=pingboard;Password=pingboard";

    public static UptimeDbContext Create()
    {
        var options = new DbContextOptionsBuilder<UptimeDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new UptimeDbContext(options);
    }
}
