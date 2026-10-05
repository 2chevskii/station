using System.Text.Json;

using Station.Abstractions.Configuration;

namespace Station.Modules.Builtin.Scoop;

internal static class ScoopExportReader
{
    public static bool IsInstalled( string output, string packageName, bool global )
    {
        try
        {
            using var document = JsonDocument.Parse( output );
            var root = document.RootElement;
            if ( root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty( "apps", out var apps )
                || apps.ValueKind != JsonValueKind.Array )
                throw new JsonException( "Expected an object with an 'apps' array." );

            var installed = false;
            foreach ( var app in apps.EnumerateArray() )
            {
                if ( app.ValueKind != JsonValueKind.Object
                    || !app.TryGetProperty( "Name", out var name )
                    || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace( name.GetString() ) )
                    throw new JsonException( "Each app must contain a non-empty 'Name' string." );

                if ( !string.Equals(
                    name.GetString(),
                    packageName,
                    StringComparison.OrdinalIgnoreCase
                ) )
                    continue;

                var info = ReadInfo( app );
                var flags = info.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                );
                if ( flags.Contains( "Install failed", StringComparer.OrdinalIgnoreCase ) )
                    throw new ConfigurationException(
                        $"Scoop reports an incomplete installation of '{packageName}'. Repair it first."
                    );

                var installedGlobally = flags.Contains(
                    "Global install",
                    StringComparer.OrdinalIgnoreCase
                );
                installed |= installedGlobally == global;
            }

            return installed;
        }
        catch ( JsonException exception )
        {
            throw new ConfigurationException(
                $"scoop export returned invalid JSON: {exception.Message}"
            );
        }
    }

    private static string ReadInfo( JsonElement app )
    {
        if ( !app.TryGetProperty( "Info", out var info ) || info.ValueKind == JsonValueKind.Null )
            return string.Empty;

        if ( info.ValueKind != JsonValueKind.String )
            throw new JsonException( "App 'Info' must be a string or null." );

        return info.GetString()!;
    }
}
