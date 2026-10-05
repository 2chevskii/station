using System.Text.RegularExpressions;

using Station.Abstractions.Configuration;

namespace Station.Modules.Builtin.Winget;

internal static class WingetListReader
{
    public static string ReadInstalledVersion( string output, string packageId )
    {
        var lines = output.Split( ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries );
        var inTable = false;
        int? versionColumnWidth = null;
        string? installedVersion = null;

        for ( var lineIndex = 1; lineIndex < lines.Length; lineIndex++ )
        {
            var line = lines[lineIndex].Trim();
            if ( line.Length >= 3 && line.All( character => character == '-' ) )
            {
                var headerColumns = Regex.Matches( lines[lineIndex - 1], @"\S+" );
                if ( headerColumns.Count is < 3 or > 5 )
                    throw new ConfigurationException(
                        "winget list returned an unrecognized table header."
                    );

                // Column boundaries preserve spaces inside versions and source names.
                versionColumnWidth = headerColumns.Count > 3
                    ? headerColumns[3].Index - headerColumns[2].Index
                    : null;
                inTable = true;
                continue;
            }

            if ( !inTable )
                continue;

            var matches = Regex.Matches(
                line,
                @"(?<=\s)" + Regex.Escape( packageId ) + @"(?=\s)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            );
            if ( matches.Count == 0 )
                continue;

            var idMatch = matches[^1];
            if ( installedVersion is not null )
                throw new ConfigurationException(
                    $"winget list returned an incomplete or ambiguous record for '{packageId}'."
                );

            var versionAndColumns = line[(idMatch.Index + idMatch.Length)..].TrimStart();
            if ( versionColumnWidth is { } width )
            {
                if ( versionAndColumns.Length < width
                    || !char.IsWhiteSpace( versionAndColumns[width - 1] ) )
                    throw new ConfigurationException(
                        "winget list returned a misaligned version column."
                    );

                installedVersion = versionAndColumns[..width].TrimEnd();
            }
            else
            {
                installedVersion = versionAndColumns.TrimEnd();
            }
            if ( string.IsNullOrWhiteSpace( installedVersion ) || installedVersion.Contains( '…' ) )
                throw new ConfigurationException(
                    "winget list returned a missing or truncated version."
                );
        }

        return installedVersion ?? throw new ConfigurationException(
            $"winget list did not return a recognizable installed record for '{packageId}'."
        );
    }
}
