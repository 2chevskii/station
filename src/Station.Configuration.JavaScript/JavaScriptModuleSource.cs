using System.Text;
using System.Text.Json;

using Station.Abstractions.Configuration;

namespace Station.Configuration.JavaScript;

internal static class JavaScriptModuleSource
{
    private static readonly string[] BuiltInModules =
    [
        "copy",
        "template",
        "script",
        "winget",
        "scoop",
        "apt",
    ];

    public static string BuildBuiltinModuleSource()
    {
        var builder = new StringBuilder();
        foreach ( var module in BuiltInModules )
            builder
                .Append( "export const " )
                .Append( module )
                .Append( " = " )
                .Append( JsonSerializer.Serialize( module ) )
                .AppendLine( ";" );

        return builder.ToString();
    }

    public static string BuildVarsModuleSource( VariableSet variables )
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            class StationVariableReference {
              constructor(name) {
                this.$type = 'variable_reference';
                this.name = name;
              }

              toString() {
                return `{{ ${this.name} }}`;
              }

              valueOf() {
                return this.toString();
              }

              [Symbol.toPrimitive]() {
                return this.toString();
              }
            }
            """
        );
        builder.AppendLine( "const __ref = path => new StationVariableReference(path);" );
        builder.AppendLine( "const __default = {};" );

        foreach ( var (key, value) in variables.Root )
        {
            builder
                .Append( "__default[" )
                .Append( JsonSerializer.Serialize( key ) )
                .Append( "] = " )
                .Append( BuildVarsValueExpression( key, value ) )
                .AppendLine( ";" );

            if ( IsValidIdentifier( key ) )
                builder
                    .Append( "export const " )
                    .Append( key )
                    .Append( " = __ref(" )
                    .Append( JsonSerializer.Serialize( key ) )
                    .AppendLine( ");" );
        }

        builder.AppendLine( "export default __default;" );
        return builder.ToString();
    }

    private static string BuildVarsValueExpression( string path, ConfigValue value )
    {
        if ( value is ConfigObject configObject )
        {
            var children = configObject.Select( pair =>
                $"{JsonSerializer.Serialize( pair.Key )}: {BuildVarsValueExpression( $"{path}.{pair.Key}", pair.Value )}"
            );
            return $"{{ {string.Join( ", ", children )} }}";
        }

        return $"__ref({JsonSerializer.Serialize( path )})";
    }

    private static bool IsValidIdentifier( string value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
            return false;

        if ( !(char.IsLetter( value[0] ) || value[0] == '_' || value[0] == '$') )
            return false;

        return value
            .Skip( 1 )
            .All( static character => char.IsLetterOrDigit( character ) || character is '_' or '$' );
    }
}