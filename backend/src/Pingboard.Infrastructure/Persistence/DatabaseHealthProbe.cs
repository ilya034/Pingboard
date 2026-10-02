using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;

namespace Pingboard.Infrastructure.Persistence;

/// <summary>
///     Readiness-проба: спрашивает БД через тот же DbContext и ту же строку подключения,
///     что и рабочие запросы. Живёт в Infrastructure, потому что EF Core — деталь этого слоя:
///     Api знает только порт <see cref="IDatabaseHealthProbe" />.
/// </summary>
public sealed class DatabaseHealthProbe(UptimeDbContext db) : IDatabaseHealthProbe
{
    public Task<bool> CanConnectAsync(CancellationToken ct)
    {
        return db.Database.CanConnectAsync(ct);
    }
}
