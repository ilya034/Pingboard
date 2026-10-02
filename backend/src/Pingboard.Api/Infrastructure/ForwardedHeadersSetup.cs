using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Доверенные заголовки прокси (X-Forwarded-For / X-Forwarded-Proto).
///     По умолчанию ВЫКЛЮЧЕНО и включается только явной настройкой: включённые
///     ForwardedHeaders без доверенного прокси — это дыра, а не удобство. Ключ раздела
///     rate limit'а — адрес соединения, поэтому клиент, которому разрешено подставлять
///     X-Forwarded-For, обходит лимит на логин простым перебором заголовка.
///     Включать там, где Api действительно доступен только через свой прокси (compose/prod):
///     ForwardedHeaders__Enabled=true и хотя бы один из списков —
///     ForwardedHeaders__KnownProxies__0=172.18.0.2 либо
///     ForwardedHeaders__KnownNetworks__0=172.16.0.0/12.
///     Если включено без списков, остаётся умолчание фреймворка (доверие только loopback).
/// </summary>
internal static class ForwardedHeadersSetup
{
    private const string SectionName = "ForwardedHeaders";

    /// <summary>
    ///     Регистрирует разбор заголовков прокси и сообщает, нужно ли ставить
    ///     <c>UseForwardedHeaders()</c> в конвейер (он обязан идти до rate limiter'а).
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
