using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Station.Output;
using Station.Core.Execution.Inventory;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station;

public sealed class StationConsole : ICommandOutput
{
    private readonly IAnsiConsole standardConsole;
    private readonly IAnsiConsole errorConsole;

    public StationConsole(
        [FromKeyedServices( "stdout" )] IAnsiConsole standardConsole,
        [FromKeyedServices( "stderr" )] IAnsiConsole errorConsole
    )
    {
        this.standardConsole = standardConsole;
        this.errorConsole = errorConsole;
    }

    public void WriteLogMessage( string message )
    {
        standardConsole.MarkupLine( $"[grey][[station]][/] {Markup.Escape( message )}" );
    }

    public void WriteResult( EngineRunResult result )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, result.Mode.ToString() );

        if ( result.Tasks.Count > 0 )
            console.Write( ConsoleReportTables.BuildTaskTable( result.Tasks ) );

        if ( result.Tasks.Count > 0 )
        {
            console.WriteLine();
            console.Write( ConsoleReportTables.BuildRunStats( result ) );
        }

        WriteDiagnostics( console, result.Diagnostics );
    }

    public void WriteRestoreResult( PluginRestoreResult result )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, "Restore" );

        if ( result.Plugins.Count > 0 )
            console.Write( ConsoleReportTables.BuildPluginTable( result.Plugins ) );

        WriteDiagnostics( console, result.Diagnostics );
    }

    public void WriteExplainResult( ExplainResult result )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, "Explain" );
        WriteDiagnostics( console, result.Diagnostics );

        if ( result.Plan is not null )
            console.Write( ConsoleExplainTreeBuilder.Build( result.Plan ) );
    }

    public void WriteInventoryResult( InventoryResult result, string kind )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, kind );
        if ( result.Succeeded )
            console.Write( ConsoleReportTables.BuildInventoryTable( result.Components, kind ) );

        WriteDiagnostics( console, result.Diagnostics );
    }

    public void WriteError(
        string command,
        string message,
        IReadOnlyList<string> diagnostics,
        int exitCode = 1
    )
    {
        WriteSummary( errorConsole, false, message, command );
        WriteDiagnostics( errorConsole, diagnostics );
    }

    private IAnsiConsole GetConsole( bool succeeded ) => succeeded ? standardConsole : errorConsole;

    private static void WriteSummary(
        IAnsiConsole console,
        bool succeeded,
        string message,
        string operation
    )
    {
        var status = succeeded ? "[green]Success[/]" : "[red]Failed[/]";
        var body = $"{status}\n{Markup.Escape( message )}";
        console.Write(
            new Panel( new Markup( body ) )
                .Header( Markup.Escape( operation ) )
                .Border( BoxBorder.Rounded )
                .BorderStyle( new Style( succeeded ? Color.Green : Color.Red ) )
                .Expand()
        );
    }

    private static void WriteDiagnostics( IAnsiConsole console, IReadOnlyList<string> diagnostics )
    {
        if ( diagnostics.Count == 0 )
            return;

        console.WriteLine();
        console.Write( new Rule( "[yellow]Diagnostics[/]" ).RuleStyle( "yellow" ) );
        foreach ( var diagnostic in diagnostics )
            console.MarkupLine( $"[yellow]-[/] {Markup.Escape( diagnostic )}" );
    }
}
