using Station.Plugins.Abstractions.Manifests;

using TruePath;
using TruePath.SystemIo;

namespace Station.Plugins.Restoration;

internal sealed class PluginSourceSnapshot : IDisposable
{
    private readonly AbsolutePath? temporaryRoot;

    public PluginSourceSnapshot(
        string pluginRoot,
        PluginManifest manifest,
        AbsolutePath? temporaryRoot = null,
        bool isLocalDirectory = false
    )
    {
        PluginRoot = pluginRoot;
        Manifest = manifest;
        this.temporaryRoot = temporaryRoot;
        IsLocalDirectory = isLocalDirectory;
    }

    public string PluginRoot { get; }

    public PluginManifest Manifest { get; }

    public bool IsLocalDirectory { get; }

    public void Dispose()
    {
        if ( temporaryRoot is { } root && root.ExistsDirectory() )
        {
            PluginRestoreInfrastructure.ClearAttributes( root );
            root.DeleteDirectoryRecursively();
        }
    }
}