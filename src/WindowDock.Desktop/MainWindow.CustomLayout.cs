using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DrawingSize = System.Drawing.Size;

namespace WindowDock;

internal sealed partial class MainWindow
{
    internal static void ArrangeGrips(Canvas canvas, List<LayoutGrip> grips, IReadOnlyList<DividerSpec> dividers,
        WorkspaceSession session, WorkspaceConfig workspace)
    {
        if (grips.Any(grip => grip.IsDragging)) return;
        while (grips.Count > dividers.Count)
        {
            canvas.Children.Remove(grips[^1]);
            grips.RemoveAt(grips.Count - 1);
        }
        for (var index = 0; index < dividers.Count; index++)
        {
            var spec = dividers[index];
            if (index >= grips.Count)
            {
                var grip = new LayoutGrip(spec, session, workspace);
                grips.Add(grip);
                canvas.Children.Add(grip);
            }
            grips[index].Update(spec, workspace);
            grips[index].IsVisible = true;
            Place(grips[index], spec.Bounds);
        }
    }

    private void ClearEmptyAreas()
    {
        foreach (var control in emptyAreas.Values) canvas.Children.Remove(control);
        emptyAreas.Clear();
    }

    private void ArrangeCustomPanels(WorkspaceConfig workspace, DrawingSize viewportSize)
    {
        var plan = CustomLayout.Plan(Session.PrepareCustomLayout(workspace), viewportSize,
            Session.Config.MinWidth, Session.Config.MinHeight, 4);
        bool HasPanel(SplitArea area) => area.PanelId is { } id && cards.TryGetValue(id, out var card) && card.Panel.WorkspaceId == workspace.Id;
        foreach (var (area, control) in emptyAreas.ToArray())
        {
            if (plan.Slots.ContainsKey(area) && !HasPanel(area) && Equals(control.Tag, area.PanelId)) continue;
            canvas.Children.Remove(control);
            emptyAreas.Remove(area);
        }
        foreach (var (area, bounds) in plan.Slots)
        {
            if (HasPanel(area))
            {
                var card = cards[area.PanelId!];
                card.IsVisible = true;
                Place(card, bounds);
                continue;
            }
            if (!emptyAreas.TryGetValue(area, out var slot))
            {
                emptyAreas.Add(area, slot = CustomAreaPlaceholder(Session, workspace, area, () => _ = AddWindowsAsync(workspace, area)));
                canvas.Children.Add(slot);
            }
            Place(slot, bounds);
        }
        canvas.Width = plan.Extent.Width;
        canvas.Height = plan.Extent.Height;
        ArrangeGrips(canvas, grips, plan.Dividers, Session, workspace);
    }

    internal static Control CustomAreaPlaceholder(WorkspaceSession session, WorkspaceConfig workspace, SplitArea area, Action add)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (area.PanelId is null)
        {
            row.Children.Add(DockControls.Button(L10n.Get("MainWindow.AddWindows"), "addwindow", add));
            if (workspace.CustomLayout is { IsLeaf: false })
                row.Children.Add(DockControls.Button("", "close", () => session.RemoveEmptyArea(workspace, area), tip: L10n.Get("CustomLayout.RemoveArea")));
        }
        else row.Children.Add(DockControls.Note(L10n.Get("CustomLayout.SavedWindow")));
        var border = new Border { Child = row, Padding = new(8), Tag = area.PanelId };
        border.Classes.Add("tile");
        return border;
    }
}
