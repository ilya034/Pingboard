using System.Collections;
using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

public sealed class EnvironmentVariablesConfigurationProvider : ConfigurationProvider
{
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key || entry.Value is not string value) continue;

            // Двойное подчёркивание — .NET-нотация вложенных ключей: Worker__MaxParallel.
            data[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        Data = data;
    }
}
