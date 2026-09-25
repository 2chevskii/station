using Station.Core.Configuration;
using Station.Core.Configuration.Models;
using Station.Core.Plugins.Services;
using TruePath;
using TruePath.SystemIo;

namespace Station.Core.Plugins.Providers;

internal sealed class LocalPluginProvider : IPluginProvider
{
    public string Name => "local";

    public async Task<RestoredPluginLocation> RestoreAsync(
        string configRoot,
        PluginDeclaration declaration,
        PluginRestoreInfrastructure infrastructure,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var pluginsRoot = infrastructure.GetPluginsDirectory(configRoot);
        var candidate = infrastructure.ResolveLocalPluginCandidate(pluginsRoot, declaration.Name!);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ConfigurationException(
                $"Local plugin '{declaration.Name}' was not found under '{pluginsRoot}'."
            );
        }

        if (new AbsolutePath(candidate).ExistsDirectory())
        {
            log?.Invoke($"Using local plugin directory '{candidate}'.");
            return new RestoredPluginLocation(candidate, CacheHit: true, IsLocal: true);
        }

        return await infrastructure.RestoreToCacheAsync(
            configRoot,
            declaration with
            {
                DisplaySource = candidate,
                Name = new AbsolutePath(candidate).FileName,
            },
            log,
            cancellationToken,
            async tempRoot =>
            {
                var extractRoot = (new AbsolutePath(tempRoot) / "extract").Value;
                new AbsolutePath(extractRoot).CreateDirectory();
                infrastructure.ExtractArchive(candidate, extractRoot);
                await Task.CompletedTask;
                return extractRoot;
            },
            isLocal: true,
            cacheKeyOverride: infrastructure.BuildLocalArchiveCacheKey(candidate)
        );
    }
}
