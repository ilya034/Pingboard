using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Фабрика времени разработки (design-time) для инструментов EF Core CLI (генерация миграций).
/// </summary>
public sealed class UptimeDbContextFactory : IDesignTimeDbContextFactory<UptimeDbContext>
{
    public UptimeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                               ?? "Host=localhost;Database=pingboard;Username=pingboard;Password=pingboard";

        var options = new DbContextOptionsBuilder<UptimeDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(UptimeDbContext).Assembly.FullName))
            .Options;

        return new UptimeDbContext(options);
    }
}