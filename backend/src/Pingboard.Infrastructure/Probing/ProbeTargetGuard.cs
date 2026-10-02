using System.Net;
using System.Net.Sockets;

namespace Pingboard.Infrastructure.Probing;

/// <summary>
///     Барьер SSRF для пробера. Адрес проверки задаёт пользователь, поэтому «проверить URL» —
///     это ещё и «сделать запрос от имени сервера куда угодно»: во внутреннюю сеть, в Docker-сеть,
///     в метаданные облака (169.254.169.254). Барьер стоит на уровне соединения
///     (<c>SocketsHttpHandler.ConnectCallback</c>): адрес резолвится здесь и здесь же проверяется,
///     поэтому подмена DNS между валидацией и коннектом (DNS rebinding) не работает — соединяемся
///     ровно с проверенным IP.
///     Отключается только явной настройкой <c>Probe__AllowPrivateNetworks=true</c> — для стенда,
///     где проверки внутренних адресов нужны осознанно (README §6.5).
/// </summary>
public static class ProbeTargetGuard
{
    /// <summary>Разделяемая политика: false — проверять можно только публичные адреса.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback(
        bool allowPrivateNetworks)
    {
        return (context, ct) => ConnectAsync(context, allowPrivateNetworks, ct);
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        bool allowPrivateNetworks,
        CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var addresses = await Dns.GetHostAddressesAsync(host, ct);

        if (!allowPrivateNetworks)
        {
            var allowed = addresses.Where(IsPublic).ToArray();

            if (allowed.Length == 0)
                // Сообщение намеренно не различает «порт закрыт» и «адрес запрещён»:
                // текст отказа попадает в историю проверок и владельцу монитора, то есть
                // иначе превратился бы в способ прощупывать внутреннюю сеть по коду ошибки.
                throw new IOException(
                    $"Address {host} is unavailable: checks against internal and service addresses are not allowed " +
                    "(override with Probe__AllowPrivateNetworks=true).");

            addresses = allowed;
        }

        Exception? lastError = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw lastError ?? new IOException($"Failed to connect to {host}:{port}");
    }

    /// <summary>Публичный адрес: не loopback, не приватный диапазон, не link-local и не multicast.</summary>
    private static bool IsPublic(IPAddress address)
    {
        // ::ffff:10.0.0.1 — это IPv4-адрес, и проверять нужно именно его.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] switch
            {
                0 => false, // 0.0.0.0/8 — «эта сеть»
                10 => false, // 10.0.0.0/8
                100 when bytes[1] is >= 64 and <= 127 => false, // 100.64.0.0/10 — CGNAT
                127 => false, // 127.0.0.0/8 — loopback
                169 when bytes[1] == 254 => false, // 169.254.0.0/16 — link-local, в т.ч. метаданные облака
                172 when bytes[1] is >= 16 and <= 31 => false, // 172.16.0.0/12
                192 when bytes[1] == 168 => false, // 192.168.0.0/16
                192 when bytes[1] == 0 && bytes[2] == 0 => false, // 192.0.0.0/24
                198 when bytes[1] is 18 or 19 => false, // 198.18.0.0/15 — benchmark
                >= 224 => false, // multicast и зарезервированное
                _ => true
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6Any)) return false;

            var bytes = address.GetAddressBytes();

            if ((bytes[0] & 0xFE) == 0xFC) return false; // fc00::/7 — unique local
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return false; // fe80::/10 — link-local
            if (bytes[0] == 0xFF) return false; // ff00::/8 — multicast

            // 64:ff9b::/96 (NAT64): за адресом стоит IPv4 — проверяем его, иначе барьер обходится.
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
                return IsPublic(new IPAddress(bytes[12..16]));

            return true;
        }

        return false;
    }
}
