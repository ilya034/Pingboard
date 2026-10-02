using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Persistence.Configurations;

public sealed class CheckResultConfiguration : IEntityTypeConfiguration<CheckResult>
{
    public void Configure(EntityTypeBuilder<CheckResult> builder)
    {
        builder.ToTable(NamingConventions.ChecksTable);
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(c => c.MonitorId).HasColumnName("monitor_id");
        builder.Property(c => c.CheckedAt).HasColumnName("checked_at");
        builder.Property(c => c.Ok).HasColumnName("ok");
        builder.Property(c => c.StatusCode).HasColumnName("status_code");
        builder.Property(c => c.LatencyMs).HasColumnName("latency_ms");
        builder.Property(c => c.Error).HasColumnName("error");

        // Основной запрос — «история монитора за окно, свежие сверху».
        builder.HasIndex(c => new { c.MonitorId, c.CheckedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_checks_monitor_time");

        builder.HasOne<Monitor>()
            .WithMany()
            .HasForeignKey(c => c.MonitorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
