using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pingboard.Domain.Entities;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Единственный DbContext приложения. Domain про EF не знает — весь маппинг здесь (Fluent API).
/// </summary>
public sealed class UptimeDbContext(DbContextOptions<UptimeDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Monitor> Monitors => Set<Monitor>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ВАЖНО: все DateTimeOffset уходят в Postgres с Offset=0 (timestamptz).
        // Без этого Npgsql падает на записи с локальным offset — классические грабли из §13 PLAN.md.
        var utc = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(),
            value => value.ToUniversalTime());

        var utcNullable = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            value => value.HasValue ? value.Value.ToUniversalTime() : null,
            value => value.HasValue ? value.Value.ToUniversalTime() : null);

        // ApplyConfigurationsFromAssembly сам отбирает типы, реализующие IEntityTypeConfiguration<>.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UptimeDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(utc);
            else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(utcNullable);

        base.OnModelCreating(modelBuilder);
    }
}
