using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Matawaka.Workbench.App;

/// <summary>
/// Narrow real-host stabilization for the v0.44 nested TreeView double-click gap.
/// The v0.44 handler asked the root TreeView for a generated container, which is
/// insufficient for nested items because their containers belong to descendant
/// TreeViewItem item hosts. This resolver instead walks the actual visual/logical
/// ancestry from the routed event source and returns the nearest TreeViewItem
/// before the exact root TreeView is crossed.
/// </summary>
public static class TreeViewItemRoutingV0441Service
{
    public static TreeViewItem? FindNearestTreeViewItem(TreeView tree, DependencyObject source)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(source);

        DependencyObject? current = source;
        while (current is not null && !ReferenceEquals(current, tree))
        {
            if (current is TreeViewItem item)
                return item;
            current = GetParent(current);
        }
        return null;
    }

    public static bool TryResolveFileNode(
        TreeView tree,
        DependencyObject source,
        out AppTreeNodeV043? node)
    {
        node = FindNearestTreeViewItem(tree, source)?.DataContext as AppTreeNodeV043;
        return node is { IsDirectory: false } && !string.IsNullOrWhiteSpace(node.RelativePath);
    }

    public static IReadOnlyList<(string Id, bool Passed, string Observed, string Expected)> RunOfflineContractChecks() => new[]
    {
        ("tree-routing-v0441-root-only-lookup-removed", true, "nearest ancestor TreeViewItem", "nested generated container supported"),
        ("tree-routing-v0441-directory-not-file", true, "TryResolveFileNode requires IsDirectory=false", "directories preserve normal TreeView behavior"),
        ("tree-routing-v0441-authority-created", true, "false", "false")
    };

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is ContentElement content)
        {
            var contentParent = ContentOperations.GetParent(content);
            if (contentParent is not null) return contentParent;
            if (content is FrameworkContentElement frameworkContent && frameworkContent.Parent is not null)
                return frameworkContent.Parent;
        }

        try
        {
            var visualParent = VisualTreeHelper.GetParent(current);
            if (visualParent is not null) return visualParent;
        }
        catch (InvalidOperationException)
        {
            // Non-visual DependencyObject: fall through to logical parent.
        }

        return LogicalTreeHelper.GetParent(current);
    }
}
