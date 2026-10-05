using Spectre.Console;

using Station.Core.Execution.Planning;

namespace Station.Output;

internal static class ConsoleExplainTreeBuilder
{
    public static Tree Build( ExplainNode plan )
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
}
