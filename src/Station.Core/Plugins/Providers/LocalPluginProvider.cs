using Station.Core.Configuration;
using Station.Core.Plugins.Services;

namespace Station.Core.Plugins.Providers;

internal sealed class LocalPluginProvider : IPluginProvider
{
    public string Name => "local";

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var pluginsRoot = infrastructure.GetPluginsDirectory(configRoot);
        var candidate = infrastructure.ResolveLocalPluginCandidate(pluginsRoot, declaration.Name!);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ConfigurationException(
                $"Local plugin '{declaration.Name}' was not found under '{pluginsRoot}'."
            );
        }

        if (Directory.Exists(candidate))
        {
            log?.Invoke($"Using local plugin directory '{candidate}'.");
            return new RestoredPluginLocation(candidate, CacheHit: true, IsLocal: true);
        }

        return await infrastructure.RestoreToCacheAsync(
            configRoot,
            declaration with
            {
                DisplaySource = candidate,
                Name = Path.GetFileName(candidate),
            },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var extractRoot = Path.Combine(tempRoot, "extract");
                Directory.CreateDirectory(extractRoot);
                infrastructure.ExtractArchive(candidate, extractRoot);
                await Task.CompletedTask;
                return extractRoot;
            },
            isLocal: true,
            cacheKeyOverride: infrastructure.BuildLocalArchiveCacheKey(candidate)
        );
    }
}
