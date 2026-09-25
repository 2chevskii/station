using Station.Plugins.Abstractions;
using Station.Plugins.Services;

namespace Station.Plugins.Providers;

internal interface IPluginProvider
{
    string Name { get; }

    Task<PluginSourceSnapshot> GetManifestAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        CancellationToken cancellationToken
    );

    RestoredPluginLocation Restore(
        string configRoot,
        PluginDeclaration declaration,
        PluginSourceSnapshot source,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log
    );
}
