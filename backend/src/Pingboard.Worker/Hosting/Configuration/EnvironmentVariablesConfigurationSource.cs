using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

public sealed class EnvironmentVariablesConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new EnvironmentVariablesConfigurationProvider();
    }
}
