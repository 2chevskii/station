using Spectre.Console.Cli;

namespace Station.Commands;

internal static class StationCommands
{
    public static void Configure( IConfigurator configuration )
    {
        configuration.SetApplicationName( "station" );
        configuration.SetApplicationVersion( typeof( StationCommands ).Assembly
            .GetName().Version!.ToString() );
        configuration.UseStrictParsing();
        configuration.AddCommand<RestoreCommand>( "restore" )
            .WithDescription( "Resolve and cache required plugins." );
        configuration.AddCommand<ExplainCommand>( "explain" )
            .WithDescription( "Show the static task plan, bound parameters, and visible variables." );
        configuration.AddBranch<StationSettings>( "list", list =>
        {
            list.SetDescription( "Show available roles and modules." );
            list.AddCommand<ListRolesCommand>( "roles" )
                .WithDescription( "Show available roles, descriptions, and sources." );
            list.AddCommand<ListModulesCommand>( "modules" )
                .WithDescription( "Show available modules, descriptions, and sources." );
        } );
        configuration.AddCommand<ValidateCommand>( "validate" )
            .WithDescription( "Validate config and plugins." );
        configuration.AddCommand<CheckCommand>( "check" )
            .WithDescription( "Dry-run the selected configuration." );
        configuration.AddCommand<ApplyCommand>( "apply" )
            .WithDescription( "Apply the selected configuration." );
    }
}
