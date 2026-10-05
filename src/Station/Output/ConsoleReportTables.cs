using Spectre.Console;

using Station.Core.Execution.Inventory;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station.Output;

internal static class ConsoleReportTables
{
    public static Table BuildTaskTable( IReadOnlyList<EngineTaskRecord> tasks )
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

    public static Table BuildPluginTable( IReadOnlyList<PluginRestoreRecord> plugins )
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

    public static Table BuildInventoryTable(
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

    public static Table BuildRunStats( EngineRunResult result )
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

    private static string GetTaskStatusMarkup( EngineTaskRecord task ) =>
        TaskReportStatus.Get( task.Result ) switch
        {
            "failed" => "[red]failed[/]",
            "changed" => "[yellow]changed[/]",
            "skipped" => "[grey]skipped[/]",
            _ => "[green]ok[/]",
        };

    private static string GetPluginSourceMarkup( PluginRestoreRecord plugin ) =>
        plugin switch
        {
            { IsLocal: true } => "[green]local[/]",
            { CacheHit: true } => "[blue]cache[/]",
            var _ => "[yellow]downloaded[/]",
        };

    private static string EscapeCell( string? value ) => Markup.Escape( value ?? string.Empty );
}
