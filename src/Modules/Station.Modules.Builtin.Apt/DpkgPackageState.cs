using Station.Abstractions.Configuration;

namespace Station.Modules.Builtin.Apt;

internal sealed record DpkgPackageState( bool IsPresent, bool IsInstalled, string? Version )
{
    public static DpkgPackageState Missing { get; } = new DpkgPackageState( false, false, null );

    public static DpkgPackageState Parse( string output )
    {
        var fields = output.TrimEnd( '\r', '\n' ).Split( '\t' );
        if ( fields.Length != 2
            || fields.Any( field => field.Contains( '\r' ) || field.Contains( '\n' ) ) )
            throw new ConfigurationException(
                "dpkg-query returned an invalid package status record."
            );

        var version = fields[1];
        return fields[0] switch
        {
            "not-installed" or "config-files" => Missing,
            "installed" when !string.IsNullOrWhiteSpace( version ) =>
                new DpkgPackageState( true, true, version ),
            "half-installed" or "unpacked" or "half-configured"
                or "triggers-awaited" or "triggers-pending" =>
                new DpkgPackageState( true, false, version ),
            _ => throw new ConfigurationException(
                "dpkg-query returned an unknown status or a missing installed version."
            ),
        };
    }
}
