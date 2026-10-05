using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;
using Station.Plugins.Abstractions.Manifests;

using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Providers.Local;

internal static class LocalPluginSourceResolver
{
    public static string GetPluginsDirectory( string configRoot ) =>
    (
        configRoot.AsLocalPath().ResolveToCurrentDirectory() / PluginLayout.PluginsDirectoryName
    ).Value;

    public static string ResolveCandidate( string pluginsRoot, string name )
    {
        var pluginName = name.AsLocalPath();
        if ( pluginName.IsAbsolute || pluginName.FileName != name )
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );

        var pluginDirectory = pluginsRoot.AsAbsolutePath();
        var directoryCandidate = pluginDirectory / name;
        if (
            directoryCandidate.Parent is not { } candidateParent
            || !candidateParent.Equals( pluginDirectory, AbsolutePath.PlatformDefaultComparer )
        )
            throw new ConfigurationException(
                "Local plugin names must stay within the root 'plugins' directory."
            );

        var hasExtension = !string.IsNullOrEmpty( pluginName.GetExtensionWithDot() );
        if ( !hasExtension && directoryCandidate.ExistsDirectory() )
            return directoryCandidate.Value;

        if ( hasExtension )
            return directoryCandidate.ExistsFile() || directoryCandidate.ExistsDirectory()
                ? directoryCandidate.Value
                : string.Empty;

        var archiveCandidate = new[]
            {
                $"{directoryCandidate}.zip".AsAbsolutePath(),
                $"{directoryCandidate}.tar".AsAbsolutePath(),
                $"{directoryCandidate}.tar.gz".AsAbsolutePath(),
                $"{directoryCandidate}.tgz".AsAbsolutePath(),
                $"{directoryCandidate}.7z".AsAbsolutePath(),
            }
            .Where( static path => path.ExistsFile() )
            .Select( static path => path.Value )
            .FirstOrDefault();

        return archiveCandidate ?? string.Empty;
    }
}
