using Microsoft.Extensions.DependencyInjection;

namespace Station.IntegrationTests.Infrastructure;

internal static class IntegrationServices
{
    public static ServiceProvider Create( Action<IServiceCollection>? configure = null )
    {
        var services = new ServiceCollection().AddStation();
        configure?.Invoke( services );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }
}
