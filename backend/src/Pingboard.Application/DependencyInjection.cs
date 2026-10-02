using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth;
using Pingboard.Application.Composition;
using Pingboard.Application.Monitors.UseCases;
using Pingboard.Application.Probing;

namespace Pingboard.Application;

/// <summary>
///     Точка входа Application-слоя: Api и Worker не перечисляют сценарии по одному,
///     а вызывают один <c>AddApplication()</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Сценарии — обычные классы, живут в scope запроса/итерации воркера.
        services.AddScoped<CreateMonitor>();
        services.AddScoped<ListMonitors>();
        services.AddScoped<GetMonitor>();
        services.AddScoped<UpdateMonitor>();
        services.AddScoped<DeleteMonitor>();
        services.AddScoped<GetMonitorChecks>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginUser>();

        // Цикл воркера — отдельный сценарий, а не часть Infrastructure.
        services.AddScoped<RunDueChecks>();

        // Время — встроенная абстракция BCL: тесты подсовывают FakeTimeProvider,
        // прод берёт системные часы. Своего IClock не заводим.
        services.TryAddTimeProvider();

        // Точки композиции (Api) регистрируют свою реализацию ICurrentUser ОТДЕЛЬНО и после
        // AddApplication — тогда заглушка ниже не подхватится.
        services.TryAddScoped<ICurrentUser, MissingCurrentUser>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
    }
}
