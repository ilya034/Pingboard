using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Pingboard.Api.Infrastructure;

/// <summary>
/// Настройка обработки прокси-заголовков (X-Forwarded-For и X-Forwarded-Proto).
/// По умолчанию отключена и требует явного указания доверенных прокси или подсетей для предотвращения спуфинга IP-адресов.
/// </summary>
internal static class ForwardedHeadersSetup
{
    private const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Регистрирует параметры обработки заголовков пересылки и возвращает флаг их включения в конвейер HTTP-запросов.
    /// </summary>
    public static bool AddProxyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        if (!section.GetValue("Enabled", false))
            return false;

        var knownProxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Ровно один хоп — наш прокси: цепочку из нескольких значений не разбираем,
            // иначе клиент сможет «дописать» себе адрес в начало заголовка.
            options.ForwardLimit = 1;

            if (knownProxies.Length == 0 && knownNetworks.Length == 0)
                return;

            // Списки заданы явно — доверяем ровно им, а не умолчаниям фреймворка.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in knownProxies)
                if (IPAddress.TryParse(proxy, out var address))
                    options.KnownProxies.Add(address);

            foreach (var network in knownNetworks)
                // System.Net.IPNetwork — тот тип, который принимает KnownIPNetworks
                // (одноимённый Microsoft.AspNetCore.HttpOverrides.IPNetwork устарел).
                if (System.Net.IPNetwork.TryParse(network, out var parsed))
                    options.KnownIPNetworks.Add(parsed);
        });

        return true;
    }
}
