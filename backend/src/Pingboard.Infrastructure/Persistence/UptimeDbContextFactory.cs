using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Нужен только инструментам EF Core (dotnet ef migrations add ...): они создают DbContext
///     вне DI-контейнера. Строка подключения на этапе design-time не используется —
///     миграции строятся из модели, а не из БД.
///     Админ-процессы (фактор XII): dotnet ef migrations add / bundle — не «фича приложения».
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