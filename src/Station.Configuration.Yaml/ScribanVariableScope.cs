using System.Collections;

using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

using Station.Abstractions.Configuration;

namespace Station.Configuration.Yaml;

internal sealed class ScribanVariableScope
{
    private readonly VariableSet variables;
    private readonly Func<string, ScribanVariableScope, string> renderTemplate;
    private readonly HashSet<string> activeReferences = new( StringComparer.OrdinalIgnoreCase );
    private readonly HashSet<string> activeTemplates = new( StringComparer.Ordinal );

    public ScribanVariableScope(
        VariableSet variables,
        Func<string, ScribanVariableScope, string> renderTemplate
    )
    {
        this.variables = variables;
        this.renderTemplate = renderTemplate;
        Globals = CreateObject( variables.Root );
    }

    public ScriptObject Globals { get; }

    private object? Resolve( ConfigValue value ) =>
        value switch
        {
            ConfigNull => null,
            ConfigScalar scalar => scalar.Value,
            ConfigObject mapping => CreateObject( mapping ),
            ConfigArray array => array.Select( Resolve ).ToArray(),
            VariableReference reference => ResolveReference( reference.Path ),
            TemplateMarkup markup => ResolveTemplate( markup.TemplateText ),
            var _ => throw new ConfigurationException(
                $"Unsupported configuration value '{value.GetType().Name}'."
            ),
        };

    private ScriptObject CreateObject( ConfigObject mapping ) =>
        new VariableObject( mapping, Resolve );

    private object? ResolveReference( string path )
    {
        if ( !activeReferences.Add( path ) )
            throw new ConfigurationException( $"Variable reference cycle detected at '{path}'." );

        try
        {
            if ( !variables.TryGetRaw( path, out var target ) )
                throw new ConfigurationException( $"Variable '{path}' is not available." );

            return Resolve( target );
        }
        finally
        {
            activeReferences.Remove( path );
        }
    }

    private string ResolveTemplate( string template )
    {
        if ( !activeTemplates.Add( template ) )
            throw new ConfigurationException( "Variable template cycle detected." );

        try
        {
            return renderTemplate( template, this );
        }
        finally
        {
            activeTemplates.Remove( template );
        }
    }

    private sealed class VariableObject : ScriptObject, IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly Func<ConfigValue, object?> resolve;

        public VariableObject( ConfigObject mapping, Func<ConfigValue, object?> resolve )
            : base( mapping.Count, false, StringComparer.OrdinalIgnoreCase )
        {
            this.resolve = resolve;
            foreach ( var (key, value) in mapping )
                SetValue( key, value, false );
        }

        public override bool TryGetValue(
            TemplateContext? context,
            SourceSpan span,
            string member,
            out object? value
        )
        {
            var found = base.TryGetValue( context, span, member, out value );
            if ( found && value is ConfigValue configValue )
                value = resolve( configValue );

            return found;
        }

        public new IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach ( var key in Keys )
                yield return KeyValuePair.Create( key, this[key] );
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString( string? format, IFormatProvider? formatProvider )
        {
            var resolved = new ScriptObject();
            foreach ( var (key, value) in this )
                resolved.SetValue( key, value, false );

            return resolved.ToString( format, formatProvider );
        }
    }
}
