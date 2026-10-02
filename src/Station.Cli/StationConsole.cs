using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Station.Core.Execution.Inventory;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station.Cli;

public sealed class StationConsole
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
            console.Write( BuildTaskTable( result.Tasks ) );

        if ( result.Tasks.Count > 0 )
        {
            console.WriteLine();
            console.Write( BuildRunStats( result ) );
        }

        WriteDiagnostics( console, result.Diagnostics );
    }

    public void WriteRestoreResult( PluginRestoreResult result )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, "Restore" );

        if ( result.Plugins.Count > 0 )
            console.Write( BuildPluginTable( result.Plugins ) );

        WriteDiagnostics( console, result.Diagnostics );
    }

    public void WriteExplainResult( ExplainResult result )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, "Explain" );
        WriteDiagnostics( console, result.Diagnostics );

        if ( result.Plan is not null )
            console.Write( BuildExplainTree( result.Plan ) );
    }

    public void WriteInventoryResult( InventoryResult result, string kind )
    {
        var console = GetConsole( result.Succeeded );
        WriteSummary( console, result.Succeeded, result.Message, kind );
        if ( result.Succeeded )
            console.Write( BuildInventoryTable( result.Components, kind ) );

        WriteDiagnostics( console, result.Diagnostics );
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

    private static Table BuildTaskTable( IReadOnlyList<EngineTaskRecord> tasks )
    {
        var table = CreateTable( "Tasks" );
        table.AddColumn( "Task" );
        table.AddColumn( "Module" );
        table.AddColumn( "Role" );
        table.AddColumn( "Status" );
        table.AddColumn( "Details" );

        foreach ( var task in tasks )
            table.AddRow(
                EscapeCell( task.TaskName ),
                EscapeCell( task.ModuleName ),
                EscapeCell( string.IsNullOrWhiteSpace( task.RoleName ) ? "root" : task.RoleName ),
                GetTaskStatusMarkup( task ),
                EscapeCell( task.Result.Message ?? string.Empty )
            );

        return table;
    }

    private static Table BuildPluginTable( IReadOnlyList<PluginRestoreRecord> plugins )
    {
        var table = CreateTable( "Plugins" );
        table.AddColumn( "Plugin" );
        table.AddColumn( "Version" );
        table.AddColumn( "Source" );
        table.AddColumn( "Path" );

        foreach ( var plugin in plugins )
            table.AddRow(
                EscapeCell( plugin.PluginName ?? plugin.Requirement ),
                EscapeCell( plugin.Version ?? "unknown" ),
                GetPluginSourceMarkup( plugin ),
                EscapeCell( plugin.PluginRoot )
            );

        return table;
    }

    private static Table BuildInventoryTable(
        IReadOnlyList<AvailableComponent> components,
        string kind
    )
    {
        var table = CreateTable( kind );
        table.AddColumn( "Name" );
        table.AddColumn( "Description" );
        table.AddColumn( "Source" );

        foreach ( var component in components )
            table.AddRow(
                EscapeCell( component.Name ),
                EscapeCell( component.Description ?? "—" ),
                EscapeCell( component.Source )
            );

        return table;
    }

    private static Table BuildRunStats( EngineRunResult result )
    {
        var table = CreateTable( "Summary" );
        table.AddColumn( "Metric" );
        table.AddColumn( "Value" );
        table.AddRow( "Tasks", result.Tasks.Count.ToString() );
        table.AddRow( "Changed", result.Tasks.Count( static task => task.Result.Changed ).ToString() );
        table.AddRow( "Skipped", result.Tasks.Count( static task => task.Result.Skipped ).ToString() );
        table.AddRow( "Failed", result.Tasks.Count( static task => task.Result.Failed ).ToString() );
        return table;
    }

    private static Table CreateTable( string title ) =>
        new Table().Border( TableBorder.Rounded ).BorderColor( Color.Grey ).Title( title ).Expand();

    private static Tree BuildExplainTree( ExplainNode plan )
    {
        var tree = new Tree( new Markup( GetExplainNodeMarkup( plan ) ) )
            .Guide( TreeGuide.Line )
            .Style( new Style( Color.Grey ) );

        foreach ( var child in plan.Children )
            AddExplainNode( tree, child );

        return tree;
    }

    private static void AddExplainNode( IHasTreeNodes parent, ExplainNode node )
    {
        var treeNode = parent.AddNode( new Markup( GetExplainNodeMarkup( node ) ) );
        foreach ( var child in node.Children )
            AddExplainNode( treeNode, child );
    }

    private static string GetExplainNodeMarkup( ExplainNode node ) =>
        node.Kind switch
        {
            ExplainNodeKind.Root => $"[bold green]{Markup.Escape( node.Text )}[/]",
            ExplainNodeKind.Section => $"[bold blue]{Markup.Escape( node.Text )}[/]",
            ExplainNodeKind.Task => $"[bold yellow]{Markup.Escape( node.Text )}[/]",
            ExplainNodeKind.Detail => $"[silver]{Markup.Escape( node.Text )}[/]",
            ExplainNodeKind.Value => Markup.Escape( node.Text ),
            ExplainNodeKind.Error => $"[red]{Markup.Escape( node.Text )}[/]",
            var _ => Markup.Escape( node.Text ),
        };

    private static string GetTaskStatusMarkup( EngineTaskRecord task ) =>
        task.Result.Failed
            ? "[red]failed[/]"
            : task.Result.Changed
                ? "[yellow]changed[/]"
                : task.Result.Skipped
                    ? "[grey]skipped[/]"
                    : "[green]ok[/]";

    private static string GetPluginSourceMarkup( PluginRestoreRecord plugin ) =>
        plugin switch
        {
            { IsLocal: true } => "[green]local[/]",
            { CacheHit: true } => "[blue]cache[/]",
            var _ => "[yellow]downloaded[/]",
        };

    private static string EscapeCell( string? value ) => Markup.Escape( value ?? string.Empty );
}
