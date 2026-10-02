using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>Фейк Unit of Work: считает вызовы сохранения, чтобы проверять «одна транзакция на сценарий».</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
