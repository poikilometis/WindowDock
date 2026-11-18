using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform;
using DrawingSize = System.Drawing.Size;
using DrawingRectangle = System.Drawing.Rectangle;

namespace WindowDock;

internal sealed class WorkspaceWindow : Window
{
    internal WorkspaceConfig Workspace { get; }
    internal string? DesktopBinding { get; set; }
    internal nint Handle => TryGetPlatformHandle()?.Handle ?? 0;
    private readonly MainWindow main;
    private readonly Canvas canvas = new();
    private readonly ScrollViewer viewport;
    private readonly Dictionary<string, PanelCard> cards = [];
    private readonly List<LayoutGrip> dividers = [];
    private readonly Dictionary<SplitArea, Control> emptyAreas = [];
    private readonly WallpaperView wallpaper = new();
    private string? zoom;
    private PixelRect? workingArea;
    private double displayScale;
    private bool arranging;

    internal WorkspaceWindow(MainWindow main, WorkspaceConfig workspace)
    {
        this.main = main;
        Workspace = workspace;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 420;
        MinHeight = 280;
        viewport = new ScrollViewer { Content = canvas };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(6, 2) };
        bar.Children.Add(DockControls.Button(L10n.Get("MainWindow.AddWindows"), "addwindow", () => _ = main.AddWindowsAsync(Workspace, owner: this)));
        bar.Children.Add(DockControls.Button(L10n.Get("Presentation.ReturnTab"), "back", Close));
        var grid = new Grid { RowDefinitions = new("30,*") };
        Grid.SetRow(wallpaper, 1);
        Grid.SetRow(viewport, 1);
        grid.Children.Add(wallpaper);
        grid.Children.Add(viewport);
        grid.Children.Add(bar);
        Content = grid;
        viewport.ScrollChanged += (_, args) =>
        {
            if (args.ViewportDelta != default) Arrange();
            main.QueueWorkspaceSync();
        };
        SizeChanged += (_, _) => Arrange();
        PositionChanged += (_, _) => main.QueueWorkspaceSync();
        ScalingChanged += (_, _) => Arrange();
        PropertyChanged += (_, args) =>
        {
            if (args.Property != WindowStateProperty && args.Property != IsVisibleProperty) return;
            wallpaper.SetRunning(IsVisible && WindowState != WindowState.Minimized);
            main.QueueWorkspaceSync();
        };
        Closed += (_, _) => wallpaper.Dispose();
    }

    internal void MoveTo(Screen display)
    {
        var area = display.WorkingArea;
        var scale = Math.Max(.5, display.Scaling);
        if (workingArea == area && displayScale == scale) return;
        workingArea = area;
        displayScale = scale;
        var wasMaximized = WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        Width = Math.Max(420, Math.Min(1200, area.Width / scale - 38));
        Height = Math.Max(280, Math.Min(840, area.Height / scale - 52));
        Position = new(area.X + 14, area.Y + 14);
        if (wasMaximized) WindowState = WindowState.Maximized;
    }

    internal void Refresh()
    {
        Title = Workspace.Name + " · WindowDock";
        Background = DockTheme.Ground;
        wallpaper.Configure(Workspace);
        var panels = main.Session.Panels.Where(panel => panel.WorkspaceId == Workspace.Id).ToDictionary(panel => panel.Id);
        foreach (var (id, card) in cards.ToArray())
        {
            if (panels.TryGetValue(id, out var panel) && ReferenceEquals(panel, card.Panel)) continue;
            canvas.Children.Remove(card);
            cards.Remove(id);
        }
        foreach (var (id, panel) in panels)
        {
            if (!cards.TryGetValue(id, out var card))
            {
                card = new PanelCard(panel, main, allowDrag: false);
                cards.Add(id, card);
                canvas.Children.Add(card);
            }
            card.RefreshTheme();
        }
        Arrange();
    }

    internal bool Focuses(RuntimePanel panel) => zoom == panel.Id;

    internal void ToggleZoom(RuntimePanel panel)
    {
        zoom = zoom == panel.Id ? null : panel.Id;
        viewport.Offset = default;
        Arrange();
    }

    internal void Reveal(RuntimePanel panel)
    {
        if (zoom != panel.Id) zoom = null;
        Workspace.ActivePanelId = panel.Id;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Arrange();
        Activate();
        if (cards.TryGetValue(panel.Id, out var card)) card.BringIntoView();
    }

    internal void Arrange()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            var config = main.Session.Config;
            var ordered = main.Session.Panels.Where(panel => panel.WorkspaceId == Workspace.Id && cards.ContainsKey(panel.Id)).ToArray();
            var width = Math.Max(1, (int)(viewport.Viewport.Width > 0 ? viewport.Viewport.Width : ClientSize.Width));
            var height = Math.Max(1, (int)(viewport.Viewport.Height > 0 ? viewport.Viewport.Height : ClientSize.Height));
            var size = new DrawingSize(width, height);
            var custom = zoom is null && Workspace.Layout == "custom";
            viewport.HorizontalScrollBarVisibility = custom ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            var visible = new HashSet<PanelCard>();
            void PlacePanel(PanelCard card, DrawingRectangle bounds) { visible.Add(card); Place(card, bounds); }
            if (!custom)
            {
                foreach (var slot in emptyAreas.Values) canvas.Children.Remove(slot);
                emptyAreas.Clear();
            }
            if (zoom is not null && cards.TryGetValue(zoom, out var zoomed))
            {
                PlacePanel(zoomed, new(4, 4, Math.Max(1, width - 8), Math.Max(1, height - 8)));
                SetExtent(size, []);
            }
            else if (custom)
            {
                var plan = CustomLayout.Plan(main.Session.PrepareCustomLayout(Workspace), size, config.MinWidth, config.MinHeight, 4);
                bool HasPanel(SplitArea area) => area.PanelId is { } id && cards.ContainsKey(id);
                foreach (var (area, control) in emptyAreas.ToArray())
                {
                    if (plan.Slots.ContainsKey(area) && !HasPanel(area) && Equals(control.Tag, area.PanelId)) continue;
                    canvas.Children.Remove(control);
                    emptyAreas.Remove(area);
                }
                foreach (var (area, bounds) in plan.Slots)
                {
                    if (HasPanel(area)) PlacePanel(cards[area.PanelId!], bounds);
                    else
                    {
                        if (!emptyAreas.TryGetValue(area, out var slot))
                        {
                            slot = MainWindow.CustomAreaPlaceholder(main.Session, Workspace, area, () => _ = main.AddWindowsAsync(Workspace, area, this));
                            emptyAreas.Add(area, slot);
                            canvas.Children.Add(slot);
                        }
                        Place(slot, bounds);
                    }
                }
                SetExtent(plan.Extent, plan.Dividers);
            }
            else
            {
                var plan = FlexibleLayout.Plan(ordered.Length, size, config.MinWidth, config.MinHeight, Workspace, 4);
                if (ordered.Length > 0 && (Workspace.Layout == "stack" || Workspace.Layout == "adaptive" && plan.Extent.Height > height))
                {
                    var active = main.Session.ActivePanel(Workspace) ?? ordered[0];
                    PlacePanel(cards[active.Id], new(4, 4, Math.Max(1, width - 8), Math.Max(1, height - 8)));
                    SetExtent(size, []);
                }
                else
                {
                    for (var index = 0; index < ordered.Length; index++) PlacePanel(cards[ordered[index].Id], plan.Tiles[index]);
                    SetExtent(new(width, Math.Max(height, plan.Extent.Height)), plan.Dividers);
                }
            }
            foreach (var card in cards.Values)
                if (!visible.Contains(card)) card.IsVisible = false;
        }
        finally { arranging = false; main.QueueWorkspaceSync(); }
    }

    private void SetExtent(DrawingSize size, IReadOnlyList<DividerSpec> specs)
    {
        canvas.Width = size.Width;
        canvas.Height = size.Height;
        MainWindow.ArrangeGrips(canvas, dividers, specs, main.Session, Workspace);
    }

    private static void Place(Control control, DrawingRectangle bounds)
    {
        control.IsVisible = true;
        Canvas.SetLeft(control, bounds.X);
        Canvas.SetTop(control, bounds.Y);
        control.Width = bounds.Width;
        control.Height = bounds.Height;
    }

    internal void AppendPlacements(List<WindowPlacement> windows, List<NativeHoverArea> hover)
    {
        if (!IsVisible || WindowState == WindowState.Minimized || Handle == 0) return;
        var viewportBounds = MainWindow.ScreenBounds(viewport);
        foreach (var card in cards.Values)
        {
            if (!card.IsVisible || card.Panel.Record.Linked) continue;
            var bounds = MainWindow.ScreenBounds(card.Slot);
            var clip = DrawingRectangle.Intersect(bounds, viewportBounds);
            if (clip.IsEmpty) continue;
            var record = card.Panel.Record;
            windows.Add(new(record, bounds, clip, MainWindow.ScreenBounds(card), (int)Math.Round(7 * RenderScaling), Handle));
            hover.Add(new(record.Handle, record.Handle, clip, true, Handle));
        }
        hover.Add(new(Handle, Handle, viewportBounds, false, Handle));
    }
}
