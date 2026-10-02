using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Persistence.Configurations;

public sealed class MonitorConfiguration : IEntityTypeConfiguration<Monitor>
{
    public void Configure(EntityTypeBuilder<Monitor> builder)
    {
        builder.ToTable(NamingConventions.MonitorsTable);
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.OwnerId).HasColumnName("user_id");
        builder.Property(m => m.Name).HasColumnName("name").IsRequired();
        builder.Property(m => m.Url).HasColumnName("url").IsRequired();
        builder.Property(m => m.IntervalSeconds).HasColumnName("interval_seconds");
        builder.Property(m => m.Enabled).HasColumnName("enabled");
        builder.Property(m => m.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(m => m.LastOk).HasColumnName("last_ok");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        // Вычисляемые свойства в БД не храним.
        builder.Ignore(m => m.NextCheckDueAt);

        builder.HasIndex(m => m.OwnerId).HasDatabaseName("ix_monitors_user");

        // Воркер выбирает пачку «просроченных» мониторов — под этот запрос и индекс.
        builder.HasIndex(m => new { m.Enabled, m.LastCheckedAt }).HasDatabaseName("ix_monitors_due");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
