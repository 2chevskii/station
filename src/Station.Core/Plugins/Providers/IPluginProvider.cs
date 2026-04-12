using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Infrastructure.Processes;
using Station.Core.Plugins.Services;

namespace Station.Core.Plugins.Providers;

internal interface IPluginProvider
{
    string Name { get; }

    Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log,
        CancellationToken cancellationToken);
}
