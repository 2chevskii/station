using Station.Abstractions.Execution;
using Station.Core.Configuration.Loading;
using Station.Core.Execution.Modules;

namespace Station.Core.Execution.Planning;

internal sealed class ExplainTreeBuilder(
    ModuleBinder moduleBinder,
    ITemplateEngine templateEngine,
    TimeProvider timeProvider
)
{
    public ExplainResult Build(
        LoadedConfiguration configuration,
        ModuleCatalog moduleCatalog,
        IReadOnlyList<LoadedTask> tasks
    )
    {
        var diagnostics = new List<string>();
        var hasErrors = false;
        var rootChildren = new List<ExplainNode>
        {
            CreateVariableSection( "variables", configuration.RootVariables ),
        };

        if ( tasks.Count == 0 )
        {
            rootChildren.Add(
                new ExplainNode(
                    "tasks",
                    ExplainNodeKind.Section,
                    [new ExplainNode( "<none>", ExplainNodeKind.Value, [] )]
                )
            );
        }
        else
        {
            var taskNodes = new List<ExplainNode>();
            for ( var index = 0; index < tasks.Count; index++ )
            {
                var task = tasks[index];
                ResolvedModule? resolvedModule;
                try
                {
                    resolvedModule = moduleCatalog.Resolve( task.ModuleName );
                }
                catch ( ConfigurationException exception )
                {
                    hasErrors = true;
                    diagnostics.Add( $"{task.Name}: error: {exception.Message}" );
                    taskNodes.Add(
                        new ExplainNode(
                            $"{task.Name} [{task.ModuleName}]",
                            ExplainNodeKind.Task,
                            [
                                new ExplainNode(
                                    $"error: {exception.Message}",
                                    ExplainNodeKind.Error,
                                    []
                                ),
                            ]
                        )
                    );
                    continue;
                }

                var context = new ModuleContext(
                    ExecutionMode.Validate,
                    task.Name,
                    configuration.ConfigRoot,
                    task.TaskRoot,
                    task.RoleName,
                    task.RoleRoot,
                    task.Variables,
                    ModuleArtifactFactory.Create(
                        configuration.ConfigRoot,
                        task.RoleRoot,
                        task.Variables,
                        templateEngine
                    ),
                    templateEngine,
                    false,
                    timeProvider
                );
                var binding = moduleBinder.Bind(
                    resolvedModule.Schema,
                    context,
                    task.Parameters,
                    true
                );

                foreach ( var diagnostic in binding.Diagnostics )
                    diagnostics.Add( $"{task.Name}: {ModuleDiagnosticFormatter.Format( diagnostic )}" );

                hasErrors |= binding.HasErrors;

                var taskChildren = new List<ExplainNode>
                {
                    new ExplainNode( $"role: {task.RoleName ?? "root"}", ExplainNodeKind.Detail, [] ),
                    CreateParameterSection( binding.Parameters ),
                    CreateVariableSection( "variables", task.Variables ),
                };
                taskNodes.Add(
                    new ExplainNode(
                        $"{task.Name} [{resolvedModule.FullName}]",
                        ExplainNodeKind.Task,
                        taskChildren
                    )
                );
            }

            rootChildren.Add( new ExplainNode( "tasks", ExplainNodeKind.Section, taskNodes ) );
        }

        return new ExplainResult(
            !hasErrors,
            $"Explained {tasks.Count} task(s).",
            diagnostics,
            new ExplainNode( "station", ExplainNodeKind.Root, rootChildren )
        );
    }

    private static ExplainNode CreateParameterSection(
        IReadOnlyList<BoundParameterRecord> parameters
    )
    {
        if ( parameters.Count == 0 )
            return new ExplainNode(
                "parameters",
                ExplainNodeKind.Section,
                [new ExplainNode( "<none>", ExplainNodeKind.Value, [] )]
            );

        var values = parameters.Select( static parameter =>
        {
            var suffix =
                parameter.IsDeferred
                    ? " (deferred)"
                    : parameter.IsDefaultValue
                        ? " (default)"
                        : string.Empty;
            return new ExplainNode(
                $"{parameter.Name} = {parameter.DisplayValue}{suffix}",
                ExplainNodeKind.Value,
                []
            );
        } );

        return new ExplainNode( "parameters", ExplainNodeKind.Section, values.ToArray() );
    }

    private static ExplainNode CreateVariableSection( string title, VariableSet variables )
    {
        var values = variables
            .EnumeratePaths()
            .OrderBy( static item => item.Path, StringComparer.OrdinalIgnoreCase )
            .ToArray();
        if ( values.Length == 0 )
            return new ExplainNode(
                title,
                ExplainNodeKind.Section,
                [new ExplainNode( "<none>", ExplainNodeKind.Value, [] )]
            );

        return new ExplainNode(
            title,
            ExplainNodeKind.Section,
            values
                .Select( static value => new ExplainNode(
                    $"{value.Path} = {ConfigValueDisplay.Format( value.Value )}",
                    ExplainNodeKind.Value,
                    []
                ) )
                .ToArray()
        );
    }
}
