using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DrawingRectangle = System.Drawing.Rectangle;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private PanelDragPreview? dragPreview;
    private PanelCard? dragSource;
    private readonly Border dropOutline = new()
    {
        BorderThickness = new(1), CornerRadius = new(8), IsVisible = false, IsHitTestVisible = false, ZIndex = 1
    };

    private readonly record struct DropDestination(WorkspaceConfig? Tab = null, PanelCard? Card = null,
        SplitArea? Area = null, int? Slot = null, DrawingRectangle? Outline = null);

    private DropDestination FindDropDestination(RuntimePanel source, Point point)
    {
        if (HitTab(point) is { } tab) return new(Tab: tab);
        if (!viewport.IsVisible || source.WorkspaceId != Session.SelectedWorkspace ||
            this.TranslatePoint(point, viewport) is not { } inside ||
            !new Rect(viewport.Viewport).Contains(inside)) return default;

        var at = this.PointToScreen(point);
        var card = cards.Values.FirstOrDefault(candidate => candidate.Panel != source && candidate.IsVisible &&
            ScreenBounds(candidate).Contains(at.X, at.Y));
        if (card is not null) return new(Card: card);
        foreach (var (area, control) in emptyAreas)
            if (area.PanelId is null && ScreenBounds(control).Contains(at.X, at.Y))
                return new(Area: area, Outline: new((int)Canvas.GetLeft(control), (int)Canvas.GetTop(control),
                    (int)control.Bounds.Width, (int)control.Bounds.Height));

        if (this.TranslatePoint(point, canvas) is not { } position) return default;
        for (var slot = 0; slot < presetSlotBounds.Count; slot++)
        {
            var bounds = presetSlotBounds[slot];
            if (position.X >= bounds.Left && position.X < bounds.Right && position.Y >= bounds.Top && position.Y < bounds.Bottom &&
                !cards.Values.Any(candidate => candidate.IsVisible && candidate.Panel.LayoutSlot == slot))
                return new(Slot: slot, Outline: bounds);
        }
        return default;
    }
    internal bool HasDragPreview => dragPreview is not null;
    internal void UpdatePanelDrag(PanelCard source, Point point)
    {
        if (dragPreview is null)
        {
            dragSource = source; dragPreview = new(source.Panel.Caption, Session.ActionPanels(source.Panel).Count);
            var screen = this.PointToScreen(point); dragPreview.Position = new(screen.X + 16, screen.Y + 18);
            dragPreview.Opened += (_, _) => { if (dragPreview?.TryGetPlatformHandle() is { } handle) Session.Backend.RaiseOverlay(handle.Handle, true); };
            dragPreview.Show(this);
        }
        var at = this.PointToScreen(point); dragPreview.Position = new(at.X + 16, at.Y + 18);
        var target = FindDropDestination(source.Panel, point);
        var tab = target.Tab;
        foreach (var card in cards.Values) card.SetDragState(card == source, card == target.Card);
        dropOutline.IsVisible = target.Outline.HasValue;
        if (target.Outline is { } outline)
        {
            dropOutline.BorderBrush = DockTheme.Accent;
            Place(dropOutline, outline);
        }
        ShowDropTarget(point);
        var incoming = Session.ActionPanels(source.Panel).Count(p => p.WorkspaceId != tab?.Id);
        var available = tab is null ? 0 : tab.Capacity - Session.Panels.Count(p => p.WorkspaceId == tab.Id);
        var valid = tab is null || incoming <= available;
        var hint = tab is not null
            ? valid ? L10n.Format("PanelDragPreview.MoveToTab", tab.Name) : L10n.Get("PanelDragPreview.ThisTabHasNoFreeSlots")
            : L10n.Get(target.Card is not null ? "PanelDragPreview.SwapPanels" :
                target.Outline.HasValue ? "PanelDragPreview.MoveToFreeSlot" : "PanelDragPreview.DropOntoAPanelOrTab");
        dragPreview.SetDestination(hint, valid);
    }
    internal void EndPanelDrag()
    {
        var source = dragSource; dragSource = null;
        var preview = dragPreview; dragPreview = null; source?.CancelDrag(); preview?.Close();
        foreach (var card in cards.Values) card.SetDragState(false, false);
        dropOutline.IsVisible = false;
        ShowDropTarget(null);
    }
}

internal sealed class PanelDragPreview : Window
{
    private readonly TextBlock destination;
    public PanelDragPreview(string caption, int count)
    {
        Title = L10n.Get("PanelDragPreview.MovingPanel"); Width = 300; Height = 88; CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None; ShowInTaskbar = false; ShowActivated = false;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; IsHitTestVisible = false;
        var content = new StackPanel { Spacing = 6 };
        var heading = new Grid { ColumnDefinitions = new("18,*"), ColumnSpacing = 8 };
        heading.Children.Add(new DockIcon { Kind = "addwindow", Width = 16, Height = 16 });
        var title = new TextBlock { Text = count > 1 ? L10n.Format("PanelDragPreview.MovingWindows", count) : caption, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 1); heading.Children.Add(title); content.Children.Add(heading);
        destination = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }; content.Children.Add(destination);
        Content = new Border { Background = DockTheme.Raised, CornerRadius = new(10), Padding = new(14, 12), Margin = new(8), BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 12, Color = Color.FromArgb(65, 0, 0, 0) }), Child = content };
    }
    public void SetDestination(string text, bool valid) { destination.Text = text; destination.Foreground = valid ? DockTheme.Muted : new SolidColorBrush(Color.Parse("#EB8D82")); }
}
