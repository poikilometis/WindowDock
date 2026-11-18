using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class PanelCard : Border
{
    public RuntimePanel Panel { get; }
    public Border Slot { get; } = new() { Background = Brushes.Transparent };
    private readonly Grid layout = new() { RowDefinitions = new("22,*") };
    private readonly Border header;
    private readonly TextBlock status;
    private readonly TextBlock title;
    private uint? titleInk;
    private readonly MainWindow window;
    private Point? dragStart;
    private IPointer? dragPointer;
    private bool dragging, dropTarget;
    public PanelCard(RuntimePanel panel, MainWindow window, bool allowDrag = true)
    {
        Panel = panel; this.window = window; DataContext = panel; Classes.Add("tile"); ClipToBounds = true;
        var row = new Grid { ColumnDefinitions = new("*,Auto,22,22,22") };
        title = new TextBlock { FontSize = 11.5, Margin = new(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(RuntimePanel.Caption)));
        status = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 4, 0) };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(RuntimePanel.StatusText)));
        Grid.SetColumn(status, 1); row.Children.Add(title); row.Children.Add(status);
        var zoom = DockControls.Button("", "focus", () => window.ToggleFocus(panel), tip: L10n.Get("PanelCard.ZoomWindowReturnToGrid"));
        var more = DockControls.Button("", "more", () => window.ShowPanelMenu(this), tip: L10n.Get("PanelCard.WindowActions"));
        var detach = DockControls.Button("", "detach", () => window.Session.ReleasePanels([panel]), tip: L10n.Get("PanelCard.ReturnWindowToDesktop"));
        var actions = new[] { zoom, more, detach };
        for (var index = 0; index < actions.Length; index++)
        { actions[index].Width = 22; actions[index].Height = 20; actions[index].MinWidth = 0; actions[index].Padding = new(2); Grid.SetColumn(actions[index], index + 2); row.Children.Add(actions[index]); }
        header = new Border { Child = row, Height = 22, Cursor = new(StandardCursorType.SizeAll) };
        layout.Children.Add(header); Grid.SetRow(Slot, 1); layout.Children.Add(Slot); Child = layout; Padding = new(1);
        if (allowDrag)
        {
            header.PointerPressed += BeginDrag;
            header.PointerMoved += ContinueDrag;
            header.PointerReleased += EndDrag;
            header.PointerCaptureLost += (_, _) => { dragStart = null; window.EndPanelDrag(); };
        }
        else header.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(header).Properties.IsRightButtonPressed) window.ShowPanelMenu(this);
        };
        RefreshTheme();
    }
    internal void ShowNotificationHighlight(NotificationHighlight highlight)
    {
        if (highlight.Parent != layout)
        {
            if (highlight.Parent is Panel previous) previous.Children.Remove(highlight);
            Grid.SetRowSpan(highlight, 2);
            layout.Children.Add(highlight);
        }
        highlight.Play();
    }

    public void RefreshTheme()
    {
        var style = window.WindowStyle(Panel);
        var surface = window.SurfaceFill(style, DockTheme.Surface);
        var translucent = style.OpacityPercent(15) < 100 && OperatingSystem.IsWindows() && WindowsWindowBackend.SupportsOpacity(Panel.Record);
        Classes.Set("selected", Panel.Selected);
        header.Background = dragging || dropTarget ? DockTheme.Resource("DockSelection") : surface;
        Background = translucent ? Brushes.Transparent : surface;
        status.Foreground = DockTheme.Accent;
        if (titleInk != style.TitleColor)
        {
            titleInk = style.TitleColor;
            if (titleInk is { } ink) title.Foreground = new SolidColorBrush(Color.FromUInt32(ink));
            else title.ClearValue(TextBlock.ForegroundProperty);
        }
        BorderBrush = dragging || dropTarget || Panel.Selected || Panel.Attention ? DockTheme.Accent : DockTheme.Border;
        Slot.Background = translucent ? Brushes.Transparent : DockTheme.Ground;
        if (Panel.Record.Linked && Slot.Child is null)
        {
            var open = DockControls.Button(L10n.Get("Compatibility.OpenLinked"), "detach", () => window.ActivatePanel(Panel));
            open.HorizontalAlignment = HorizontalAlignment.Center;
            open.VerticalAlignment = VerticalAlignment.Center;
            Slot.Child = open;
        }
    }
    internal void CancelDrag() { dragStart = null; var pointer = dragPointer; dragPointer = null; if (pointer?.Captured == header) pointer.Capture(null); }
    internal void SetDragState(bool source, bool target) { dragging = source; dropTarget = target; RefreshTheme(); }
    private void BeginDrag(object? sender, PointerPressedEventArgs args)
    {
        if (args.Handled) return;
        if (args.GetCurrentPoint(this).Properties.IsRightButtonPressed) { window.ShowPanelMenu(this); args.Handled = true; return; }
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (args.ClickCount == 2) { window.ToggleFocus(Panel); args.Handled = true; return; }
        window.Session.Select(Panel, args.KeyModifiers.HasFlag(KeyModifiers.Control), args.KeyModifiers.HasFlag(KeyModifiers.Shift));
        dragStart = args.GetPosition(window); dragPointer = args.Pointer; args.Pointer.Capture(header); args.Handled = true;
    }
    private void ContinueDrag(object? sender, PointerEventArgs args)
    {
        if (dragStart is not { } start) return;
        var point = args.GetPosition(window);
        if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) >= 6) window.UpdatePanelDrag(this, point);
    }
    private void EndDrag(object? sender, PointerReleasedEventArgs args)
    {
        if (dragStart is not { } start) return;
        dragStart = null; args.Pointer.Capture(null); window.EndPanelDrag();
        var end = args.GetPosition(window);
        if (Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) > 10) window.DropPanel(Panel, end);
        args.Handled = true;
    }
}

internal sealed class LayoutGrip : Border
{
    private DividerSpec spec;
    private readonly WorkspaceSession session;
    private WorkspaceConfig workspace;
    private Point? start;
    public bool IsDragging => start is not null;
    public void Update(DividerSpec next, WorkspaceConfig target)
    {
        if (IsDragging) return;
        spec = next; workspace = target; Cursor = new(spec.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
    }
    public LayoutGrip(DividerSpec spec, WorkspaceSession session, WorkspaceConfig workspace)
    {
        this.spec = spec; this.session = session; this.workspace = workspace; Background = Brushes.Transparent;
        Cursor = new(spec.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        Avalonia.Automation.AutomationProperties.SetName(this, L10n.Get("PanelCard.ResizePanels"));
        PointerEntered += (_, _) => Background = DockTheme.Accent;
        PointerExited += (_, _) => { if (start is null) Background = Brushes.Transparent; };
        PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            start = this.PointToScreen(args.GetPosition(this)).ToPoint(1); session.BeginLayoutChange(); args.Pointer.Capture(this); args.Handled = true;
        };
        PointerMoved += (_, args) =>
        {
            if (start is not { } origin) return;
            var position = this.PointToScreen(args.GetPosition(this)).ToPoint(1);
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var delta = spec.Vertical ? position.X - origin.X : position.Y - origin.Y;
            FlexibleLayout.Resize(workspace, spec, (int)Math.Round(delta / scale)); session.LayoutChanged(false); args.Handled = true;
        };
        PointerReleased += (_, args) => { if (start is null) return; start = null; args.Pointer.Capture(null); session.LayoutChanged(); args.Handled = true; };
    }
}
