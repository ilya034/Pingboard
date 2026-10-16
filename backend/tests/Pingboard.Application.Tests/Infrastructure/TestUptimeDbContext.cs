using Microsoft.EntityFrameworkCore;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Application.Tests.Infrastructure;

/// <summary>
/// Фабрика тестового экземпляра <see cref="UptimeDbContext"/> для валидации формируемого SQL-кода через ToQueryString без открытия реального соединения.
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
