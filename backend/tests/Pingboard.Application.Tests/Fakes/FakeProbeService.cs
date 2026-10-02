using Pingboard.Application.Abstractions;
using Pingboard.Domain.Common;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>Фейк пробера: возвращает заданный исход и запоминает, какие URL реально проверялись.</summary>
public sealed class FakeProbeService(Func<string, ProbeOutcome>? handler = null) : IProbeService
{
    private readonly List<string> _probedUrls = [];

    public IReadOnlyList<string> ProbedUrls => _probedUrls;

    public Task<ProbeOutcome> CheckAsync(string url, CancellationToken ct)
    {
        lock (_probedUrls)
        {
            _probedUrls.Add(url);
        }

        return Task.FromResult(handler?.Invoke(url) ?? ProbeOutcome.Success(200, 15));
    }

    public static FakeProbeService AlwaysUp()
    {
        return new FakeProbeService();
    }

    public static FakeProbeService AlwaysDown(string error = "Таймаут 5000 мс")
    {
        return new FakeProbeService(_ => ProbeOutcome.Failure(error));
    }
}
