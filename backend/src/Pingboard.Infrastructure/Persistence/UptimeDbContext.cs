using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pingboard.Domain.Entities;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Единственный DbContext приложения.
/// </summary>
public sealed class UptimeDbContext(DbContextOptions<UptimeDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Monitor> Monitors => Set<Monitor>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var utc = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(),
            value => value.ToUniversalTime());

        var utcNullable = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            value => value.HasValue ? value.Value.ToUniversalTime() : null,
            value => value.HasValue ? value.Value.ToUniversalTime() : null);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UptimeDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entityType.GetProperties())
            if (property.ClrType == typeof(DateTimeOffset))
                property.SetValueConverter(utc);
            else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(utcNullable);

        base.OnModelCreating(modelBuilder);
    }
}
