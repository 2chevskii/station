using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Station.Core.Execution.Inventory;
using Station.Core.Execution.Planning;
using Station.Core.Execution.Running;
using Station.Core.Plugins.Restoration;

namespace Station.Cli.Output;

public sealed class JsonCommandOutput(
    [FromKeyedServices( "stdout" )] TextWriter standardOutput,
    [FromKeyedServices( "stderr" )] TextWriter standardError
) : ICommandOutput
{
    public void WriteLogMessage( string message ) =>
        standardError.WriteLine( $"[station] {message}" );

    public void WriteResult( EngineRunResult result )
    {
        WriteReport(
            result.Mode.ToString().ToLowerInvariant(),
            result.Succeeded,
            result.Succeeded ? 0 : 1,
            result.Message,
            result.Diagnostics,
            writer => WriteRunData( writer, result )
        );
    }

    public void WriteRestoreResult( PluginRestoreResult result )
    {
        WriteReport(
            "restore",
            result.Succeeded,
            result.Succeeded ? 0 : 1,
            result.Message,
            result.Diagnostics,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartArray( "plugins" );
                foreach ( var plugin in result.Plugins )
                {
                    writer.WriteStartObject();
                    writer.WriteString( "requirement", plugin.Requirement );
                    writer.WriteString( "pluginName", plugin.PluginName );
                    writer.WriteString( "version", plugin.Version );
                    writer.WriteString( "pluginRoot", plugin.PluginRoot );
                    writer.WriteBoolean( "cacheHit", plugin.CacheHit );
                    writer.WriteBoolean( "isLocal", plugin.IsLocal );
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        );
    }

    public void WriteExplainResult( ExplainResult result )
    {
        WriteReport(
            "explain",
            result.Succeeded,
            result.Succeeded ? 0 : 1,
            result.Message,
            result.Diagnostics,
            writer =>
            {
                writer.WriteStartObject();
                writer.WritePropertyName( "plan" );
                WritePlan( writer, result.Plan );
                writer.WriteEndObject();
            }
        );
    }

    public void WriteInventoryResult( InventoryResult result, string kind )
    {
        WriteReport(
            $"list {kind}",
            result.Succeeded,
            result.Succeeded ? 0 : 1,
            result.Message,
            result.Diagnostics,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartArray( "components" );
                foreach ( var component in result.Components )
                {
                    writer.WriteStartObject();
                    writer.WriteString( "name", component.Name );
                    writer.WriteString( "description", component.Description );
                    writer.WriteString( "source", component.Source );
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        );
    }

    public void WriteError(
        string command,
        string message,
        IReadOnlyList<string> diagnostics,
        int exitCode = 1
    ) => WriteReport( command, false, exitCode, message, diagnostics, null );

    private void WriteReport(
        string command,
        bool succeeded,
        int exitCode,
        string message,
        IReadOnlyList<string> diagnostics,
        Action<Utf8JsonWriter>? writeData
    )
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter( buffer, new JsonWriterOptions { Indented = true } );
        writer.WriteStartObject();
        writer.WriteNumber( "schemaVersion", 1 );
        writer.WriteString( "command", command );
        writer.WriteBoolean( "succeeded", succeeded );
        writer.WriteNumber( "exitCode", exitCode );
        writer.WriteString( "message", message );
        writer.WriteStartArray( "diagnostics" );
        foreach ( var diagnostic in diagnostics )
            writer.WriteStringValue( diagnostic );

        writer.WriteEndArray();
        writer.WritePropertyName( "data" );
        if ( writeData is null )
            writer.WriteNullValue();
        else
            writeData( writer );

        writer.WriteEndObject();
        writer.Flush();
        standardOutput.WriteLine( Encoding.UTF8.GetString( buffer.ToArray() ) );
    }

    private static void WriteRunData( Utf8JsonWriter writer, EngineRunResult result )
    {
        writer.WriteStartObject();
        writer.WriteStartObject( "summary" );
        writer.WriteNumber( "taskCount", result.Tasks.Count );
        writer.WriteNumber( "changedCount", result.ChangedCount );
        writer.WriteNumber( "failedCount", result.Tasks.Count( task => task.Result.Failed ) );
        writer.WriteNumber( "skippedCount", result.Tasks.Count( task => task.Result.Skipped ) );
        writer.WriteEndObject();
        writer.WriteStartArray( "tasks" );
        foreach ( var task in result.Tasks )
        {
            writer.WriteStartObject();
            writer.WriteString( "taskName", task.TaskName );
            writer.WriteString( "moduleName", task.ModuleName );
            writer.WriteString( "roleName", task.RoleName );
            writer.WriteString( "status", TaskReportStatus.Get( task.Result ) );
            writer.WriteBoolean( "changed", task.Result.Changed );
            writer.WriteBoolean( "failed", task.Result.Failed );
            writer.WriteBoolean( "skipped", task.Result.Skipped );
            writer.WriteString( "message", task.Result.Message );
            writer.WritePropertyName( "outputs" );
            if ( task.Result.Outputs is { } outputs )
                outputs.WriteTo( writer );
            else
                writer.WriteNullValue();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePlan( Utf8JsonWriter writer, ExplainNode? node )
    {
        if ( node is null )
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString( "text", node.Text );
        writer.WriteString( "kind", node.Kind.ToString().ToLowerInvariant() );
        writer.WriteStartArray( "children" );
        foreach ( var child in node.Children )
            WritePlan( writer, child );

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
