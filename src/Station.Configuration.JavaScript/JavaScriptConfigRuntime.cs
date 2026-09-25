using Jint;

using Station.Abstractions.Configuration;
using Station.Abstractions.FileSystem;

namespace Station.Configuration.JavaScript;

public sealed class JavaScriptConfigRuntime
{
    private const string DefaultExportName = "default";
    private const string TaskRegistrationFunctionName = "task";
    private const string VariablesModuleSpecifier = "station:vars";
    private const string BuiltInModulesSpecifier = "station:modules/builtin";
    private const long MaximumMemoryBytes = 4_000_000;
    private const int MaximumStatements = 10_000;
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds( 2 );

    public ConfigObject LoadVariables(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var engine = CreateEngine( artifact, context );
        var module = engine.Modules.Import( GetImportSpecifier( artifact ) );
        var exportValue = module.Get( DefaultExportName ).ToObject();
        return ConfigValueFactory.RequireObject(
            ConfigValueFactory.FromClrValue( exportValue ),
            artifact.DisplayName
        );
    }

    public ConfigArray LoadRoleTasks(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var taskRegistrations = new List<object?>();
        var engine = CreateEngine( artifact, context );
        engine.SetValue(
            TaskRegistrationFunctionName,
            new Func<object?, object?, object?, object?>( ( first, second, third ) =>
                {
                    RegisterTask( taskRegistrations, first, second, third, artifact );
                    return null;
                }
            )
        );

        engine.Modules.Import( GetImportSpecifier( artifact ) );
        return new ConfigArray(
            taskRegistrations.Select( static item => ConfigValueFactory.FromClrValue( item ) )
        );
    }

    private static Engine CreateEngine(
        ResolvedConfigArtifact artifact,
        ConfigEvaluationContext context
    )
    {
        var moduleRoot =
            artifact.FullPath.AsAbsolutePath().Parent?.Value
            ?? throw new ConfigurationException(
                $"Could not determine the directory for '{artifact.FullPath}'."
            );
        var engine = new Engine( options =>
            options
                .EnableModules( moduleRoot, false )
                .LimitMemory( MaximumMemoryBytes )
                .MaxStatements( MaximumStatements )
                .TimeoutInterval( EvaluationTimeout )
        );

        engine.Modules.Add(
            VariablesModuleSpecifier,
            builder =>
                builder.AddSource(
                    JavaScriptModuleSource.BuildVarsModuleSource( context.VisibleVariables )
                )
        );
        engine.Modules.Add(
            BuiltInModulesSpecifier,
            builder => builder.AddSource( JavaScriptModuleSource.BuildBuiltinModuleSource() )
        );
        return engine;
    }

    private static string GetImportSpecifier( ResolvedConfigArtifact artifact ) =>
        $"./{artifact.FullPath.AsAbsolutePath().FileName}";

    private static void RegisterTask(
        ICollection<object?> tasks,
        object? first,
        object? second,
        object? third,
        ResolvedConfigArtifact artifact
    )
    {
        string? explicitName = null;
        object? moduleReference;
        object? parametersObject;

        if ( third is null )
        {
            moduleReference = first;
            parametersObject = second;
        }
        else
        {
            explicitName = first?.ToString();
            moduleReference = second;
            parametersObject = third;
        }

        var moduleName = moduleReference?.ToString()?.Trim();
        if ( string.IsNullOrWhiteSpace( moduleName ) )
            throw new ConfigurationException(
                $"JavaScript role file '{artifact.FullPath}' called task(...) with an invalid module reference."
            );

        if ( parametersObject is not IDictionary<string, object?> parameters )
            throw new ConfigurationException(
                $"JavaScript role file '{artifact.FullPath}' called task(...) with a non-object parameters value."
            );

        var task = new Dictionary<string, object?>( StringComparer.OrdinalIgnoreCase );
        if ( !string.IsNullOrWhiteSpace( explicitName ) )
            task["name"] = explicitName;

        task[moduleName] = parameters;
        tasks.Add( task );
    }
}