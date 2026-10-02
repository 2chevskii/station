using Microsoft.Extensions.DependencyInjection;

namespace Station.Core.Execution.Modules;

internal sealed class ModuleActivator( IServiceProvider services )
{
    public ConfigModule Create( Type moduleType, ModuleContext context )
    {
        try
        {
            return (ConfigModule)ActivatorUtilities.CreateInstance( services, moduleType, context );
        }
        catch ( InvalidOperationException exception )
        {
            throw new ConfigurationException(
                $"Unable to create module '{moduleType.FullName}': {exception.Message}"
            );
        }
    }
}
