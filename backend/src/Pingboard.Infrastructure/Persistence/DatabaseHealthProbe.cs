using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Readiness-проба: спрашивает БД через тот же DbContext и ту же строку подключения,
///     что и рабочие запросы.
/// </summary>
public sealed class DatabaseHealthProbe(UptimeDbContext db) : IDatabaseHealthProbe
{
    public Task<bool> CanConnectAsync(CancellationToken ct)
    {
        return db.Database.CanConnectAsync(ct);
    }
}
