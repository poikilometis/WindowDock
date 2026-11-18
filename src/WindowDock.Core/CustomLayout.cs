using System.Drawing;
using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed class SplitArea
{
    [DataMember(EmitDefaultValue = false)] public string? PanelId;
    [DataMember] public bool SideBySide;
    [DataMember] public double Ratio = .5;
    [DataMember(EmitDefaultValue = false)] public SplitArea? First, Second;

    internal bool IsLeaf => First is null;

    internal IEnumerable<SplitArea> Leaves()
    {
        if (IsLeaf) { yield return this; yield break; }
        foreach (var leaf in First!.Leaves()) yield return leaf;
        foreach (var leaf in Second!.Leaves()) yield return leaf;
    }

    internal void Split(bool sideBySide)
    {
        First = new() { PanelId = PanelId };
        Second = new();
        PanelId = null;
        SideBySide = sideBySide;
        Ratio = .5;
    }

    internal SplitArea RemoveEmpty(SplitArea target)
    {
        if (IsLeaf) return this;
        if (First == target) return Second!;
        if (Second == target) return First!;
        First = First!.RemoveEmpty(target);
        Second = Second!.RemoveEmpty(target);
        return this;
    }

    internal static SplitArea Create(IReadOnlyList<string> panels, bool sideBySide = true)
    {
        if (panels.Count <= 1) return new() { PanelId = panels.FirstOrDefault() };
        var splitAt = (panels.Count + 1) / 2;
        return new()
        {
            SideBySide = sideBySide,
            First = Create(panels.Take(splitAt).ToArray(), !sideBySide),
            Second = Create(panels.Skip(splitAt).ToArray(), !sideBySide)
        };
    }

    internal bool Validate()
    {
        var visited = new HashSet<SplitArea>();
        var panels = new HashSet<string>();
        return ValidateNode(this);

        bool ValidateNode(SplitArea node)
        {
            if (!visited.Add(node) || visited.Count > 2 * CustomLayout.MaxAreas - 1) return false;
            if (node.First is null || node.Second is null)
                return node.First is null && node.Second is null &&
                    (node.PanelId is null || node.PanelId.Length is > 0 and <= 128 && panels.Add(node.PanelId));
            node.PanelId = null;
            if (!double.IsFinite(node.Ratio) || node.Ratio is <= 0 or >= 1) node.Ratio = .5;
            return ValidateNode(node.First) && ValidateNode(node.Second);
        }
    }
}

internal sealed record SplitPlan(Size Extent, Dictionary<SplitArea, Rectangle> Slots, List<DividerSpec> Dividers);

internal static class CustomLayout
{
    // Leave room for newly captured panels even when part of the saved layout is empty.
    internal const int MaxAreas = 64;

    internal static SplitPlan Plan(SplitArea root, Size viewport, int minWidth, int minHeight, int gap)
    {
        var minimums = new Dictionary<SplitArea, Size>();
        var minimum = Measure(root);
        var extent = new Size(Math.Max(viewport.Width, minimum.Width + 2 * gap),
            Math.Max(viewport.Height, minimum.Height + 2 * gap));
        var plan = new SplitPlan(extent, [], []);
        Arrange(root, new(gap, gap, extent.Width - 2 * gap, extent.Height - 2 * gap));
        return plan;

        Size Measure(SplitArea area)
        {
            if (area.IsLeaf) return minimums[area] = new(minWidth, minHeight);
            var first = Measure(area.First!);
            var second = Measure(area.Second!);
            return minimums[area] = area.SideBySide
                ? new(first.Width + second.Width + gap, Math.Max(first.Height, second.Height))
                : new(Math.Max(first.Width, second.Width), first.Height + second.Height + gap);
        }

        void Arrange(SplitArea area, Rectangle bounds)
        {
            if (area.IsLeaf) { plan.Slots.Add(area, bounds); return; }
            var firstArea = area.First!;
            var secondArea = area.Second!;
            var horizontal = area.SideBySide;
            var available = (horizontal ? bounds.Width : bounds.Height) - gap;
            var firstMin = horizontal ? minimums[firstArea].Width : minimums[firstArea].Height;
            var secondMin = horizontal ? minimums[secondArea].Width : minimums[secondArea].Height;
            var firstSize = Math.Clamp((int)Math.Round(available * area.Ratio), firstMin, available - secondMin);
            var secondSize = available - firstSize;
            var divider = horizontal
                ? new Rectangle(bounds.X + firstSize, bounds.Y, gap, bounds.Height)
                : new Rectangle(bounds.X, bounds.Y + firstSize, bounds.Width, gap);
            plan.Dividers.Add(new()
            {
                Split = area, Bounds = divider, Vertical = horizontal, First = firstSize, Second = secondSize,
                Minimum = firstMin, SecondMinimum = secondMin
            });
            Arrange(firstArea, horizontal ? new(bounds.X, bounds.Y, firstSize, bounds.Height) : new(bounds.X, bounds.Y, bounds.Width, firstSize));
            Arrange(secondArea, horizontal ? new(divider.Right, bounds.Y, secondSize, bounds.Height) : new(bounds.X, divider.Bottom, bounds.Width, secondSize));
        }
    }
}

internal sealed partial class WorkspaceSession
{
    internal SplitArea PrepareCustomLayout(WorkspaceConfig workspace)
    {
        var live = PanelsIn(workspace).Select(panel => panel.Id).ToArray();
        var known = live.Concat(Pending.Where(panel => panel.WorkspaceId == workspace.Id).Select(panel => panel.Id)).ToHashSet();
        var root = workspace.CustomLayout ??= SplitArea.Create(live);
        var leaves = root.Leaves().ToList();
        foreach (var leaf in leaves)
            if (leaf.PanelId is { } id && !known.Contains(id)) leaf.PanelId = null;
        var assigned = leaves.Select(leaf => leaf.PanelId).OfType<string>().ToHashSet();
        foreach (var id in live.Where(id => !assigned.Contains(id)))
        {
            var empty = leaves.FirstOrDefault(leaf => leaf.PanelId is null);
            if (empty is null)
            {
                // This can happen when many saved windows are awaiting a match.
                if (leaves.Count >= CustomLayout.MaxAreas)
                {
                    // Reuse an unavailable window's slot without resetting the user's splits.
                    empty = leaves.First(leaf => !live.Contains(leaf.PanelId));
                    empty.PanelId = id;
                    continue;
                }
                empty = leaves[^1];
                var sideBySide = leaves.Count % 2 == 1;
                empty.Split(sideBySide);
                leaves.RemoveAt(leaves.Count - 1);
                leaves.Add(empty.First!);
                leaves.Add(empty.Second!);
                empty = empty.Second!;
            }
            empty.PanelId = id;
        }
        return root;
    }

    internal bool SplitPanel(RuntimePanel panel, bool sideBySide)
    {
        if (!Panels.Contains(panel) || Config.Workspaces.FirstOrDefault(tab => tab.Id == panel.WorkspaceId) is not { } workspace) return false;
        var root = PrepareCustomLayout(workspace);
        if (root.Leaves().Count() >= CustomLayout.MaxAreas) return false;
        var target = root.Leaves().FirstOrDefault(leaf => leaf.PanelId == panel.Id);
        if (target is null) return false;
        BeginLayoutChange();
        workspace.Layout = "custom";
        target.Split(sideBySide);
        FocusedPanel = null;
        LayoutChanged();
        return true;
    }

    internal void RemoveEmptyArea(WorkspaceConfig workspace, SplitArea area)
    {
        if (workspace.CustomLayout is not { IsLeaf: false } root || area.PanelId is not null || !root.Leaves().Contains(area)) return;
        BeginLayoutChange();
        workspace.CustomLayout = root.RemoveEmpty(area);
        LayoutChanged();
    }

    internal void MovePanelToArea(RuntimePanel panel, SplitArea target)
    {
        if (panel.WorkspaceId != SelectedWorkspace || target.PanelId is not null || Current.CustomLayout is not { } root) return;
        var areas = root.Leaves().ToArray();
        var source = areas.FirstOrDefault(area => area.PanelId == panel.Id);
        if (source is null || !areas.Contains(target)) return;
        BeginLayoutChange();
        (target.PanelId, source.PanelId) = (source.PanelId, null);
        LayoutChanged();
    }
}
