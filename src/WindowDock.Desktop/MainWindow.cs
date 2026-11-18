using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace WindowDock;

internal sealed partial class MainWindow : Window
{
    internal WorkspaceSession Session { get; }
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new(5, 2) };
    private readonly ScrollViewer tabScroll;
    private readonly ScrollViewer viewport;
    private readonly Canvas canvas = new();
    private readonly Grid workspace = new() { RowDefinitions = new("Auto,*") };
    private readonly Border toolbar;
    private readonly TextBlock status = new() { FontSize = 11.5, Margin = new(8, 3), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = false };
    private readonly Grid root = new() { RowDefinitions = new("30,*") };
    private readonly Dictionary<string, PanelCard> cards = [];
    private readonly Dictionary<string, (Button Button, TextBlock Name, TextBlock Count)> tabButtons = [];
    private readonly List<LayoutGrip> grips = [];
    private readonly WallpaperView wallpaper = new();
    private readonly Border empty;
    private readonly Button collect, magnet, add, grid, attention;
    private string? previewThemeId;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AgentSignalServer? signals;
    private bool syncQueued, arranging, closing, nativeReady, layoutFrameQueued;
    private int dialogDepth;
    private string lastWorkspace = "", lastStatus = "", lastTheme = "";
    private string? draggedTab;
    private DateTime statusUntil;
    private Point? tabDragStart;
    private readonly Dictionary<string, double> offsets = [];

    public MainWindow(WorkspaceSession session)
    {
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        Session = session; Title = "WindowDock"; MinWidth = 640; MinHeight = 360;
        Width = session.Config.WindowWidth; Height = session.Config.WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (session.Config.WindowX != int.MinValue) { WindowStartupLocation = WindowStartupLocation.Manual; Position = new(session.Config.WindowX, session.Config.WindowY); }
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://WindowDock/Assets/windowdock.png")));
        Background = Brushes.Transparent;
        wallpaper.Failed += SetStatus;
        tabScroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        tabScroll.PointerWheelChanged += (_, args) => { tabScroll.Offset = new(Math.Clamp(tabScroll.Offset.X - args.Delta.Y * 48, 0, Math.Max(0, tabScroll.Extent.Width - tabScroll.Viewport.Width)), 0); args.Handled = true; };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new(3, 2, 5, 2) };
        collect = DockControls.Button(L10n.Get("MainWindow.CollectWindows"), "collect", () => UserAction(() => CollectWindows(new())), tip: L10n.Get("MainWindow.CollectDesktopWindowsMinimizedWindowsAreSkipped"));
        collect.Classes.Add("secondary");
        collect.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(collect).Properties.IsRightButtonPressed) return;
            ShowCollectMenu();
            args.Handled = true;
        };
        var newTab = DockControls.Button("", "add", () => _ = NewWorkspaceAsync(), tip: L10n.Get("MainWindow.NewTabCtrlN"));
        add = DockControls.Button("", "addwindow", () => _ = AddWindowsAsync(), true, L10n.Get("MainWindow.AddWindowsCtrlAltA"));
        grid = DockControls.Button("", "grid", ReturnToGrid, tip: L10n.Get("MainWindow.GridShowAllWindowsAndResetThe")); grid.Classes.Add("layout");
        magnet = DockControls.Button("", "magnet", () => { Session.Config.Magnet = !Session.Config.Magnet; Session.Save(); Refresh(); }, tip: L10n.Get("MainWindow.CaptureWindowsAsYouDragThem")); magnet.Classes.Add("magnet");
        attention = DockControls.Button("", "bell", () => _ = UserActionAsync(OpenAttentionAsync), tip: L10n.Get("Workflow.AgentActivity"));
        ((StackPanel)attention.Content!).Children.Add(attentionCount);
        var menu = DockControls.Button("", "more", () => ShowAppMenu(), tip: L10n.Get("MainWindow.WindowDockMenu"));
        foreach (var button in new[] { collect, newTab, add, grid, magnet, attention, menu }) actions.Children.Add(button);
        status.MaxWidth = 220; status.VerticalAlignment = VerticalAlignment.Center;
        var commands = new Grid { ColumnDefinitions = new("*,Auto,Auto") }; commands.Children.Add(tabScroll);
        Grid.SetColumn(status, 1); commands.Children.Add(status); Grid.SetColumn(actions, 2); commands.Children.Add(actions);
        toolbar = new Border { Child = commands }; root.Children.Add(toolbar);
        viewport = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRowSpan(wallpaper, 2);
        workspace.Children.Add(wallpaper);
        workspace.Children.Add(CreateStackBar());
        Grid.SetRow(viewport, 1);
        workspace.Children.Add(viewport);
        var instruction = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400 };
        instruction.Children.Add(new TextBlock { Text = L10n.Get("MainWindow.AddWindowsToThisTab"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center });
        var emptyAdd = DockControls.Button(L10n.Get("MainWindow.AddWindows"), "addwindow", () => _ = AddWindowsAsync(), true);
        emptyAdd.Height = 30; emptyAdd.MinWidth = 178; emptyAdd.HorizontalAlignment = HorizontalAlignment.Center; emptyAdd.IsEnabled = session.Backend.Available;
        instruction.Children.Add(emptyAdd);
        instruction.Children.Add(new TextBlock { Text = session.Backend.UnavailableReason ?? L10n.Get("MainWindow.OrEnableTheMagnetAndDragA"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = DockTheme.Muted });
        empty = new Border { Child = instruction, Background = Brushes.Transparent, IsVisible = true, Padding = new(20) };
        Grid.SetRow(empty, 1); workspace.Children.Add(empty); Grid.SetRow(workspace, 1); root.Children.Add(workspace); Content = root;
        viewport.ScrollChanged += (_, _) => QueueSync();
        viewport.PropertyChanged += (_, args) => { if (args.Property == ScrollViewer.ViewportProperty) ArrangePanels(); };
        PositionChanged += (_, _) => { QueueSync(); SchedulePlacementSave(); };
        ScalingChanged += (_, _) => QueueSync();
        SizeChanged += (_, _) => { UpdateCollectCaption(); SchedulePlacementSave(); };
        PropertyChanged += (_, args) =>
        {
            if (args.Property != WindowStateProperty && args.Property != IsVisibleProperty) return;
            if (WindowState != WindowState.Minimized) lastVisibleState = WindowState;
            wallpaper.SetRunning(IsVisible && WindowState != WindowState.Minimized);
            SchedulePlacementSave();
            QueueSync();
        };
        LayoutUpdated += (_, _) => FlushSync();
        Session.StructureChanged += Refresh;
        Session.PanelChanged += RefreshPanel;
        Session.SaveRequested += QueueSave;
        Session.SelectionChanged += RefreshSelection; Session.LayoutInvalidated += InvalidateLayout;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); if (!closing) Session.SaveInBackground(); };
        Session.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(WorkspaceSession.Status)) RefreshStatus(); };
        Session.Backend.Changed += BackendChanged;
        Session.AgentActivityChanged += QueueAttentionUpdate;
        Session.AgentStatusTransition += OnAgentStatusTransition;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) }; timer.Tick += (_, _) => Poll();
        Opened += OnOpened; Closing += OnClosing; Closed += (_, _) => OnClosed();
        KeyDown += OnKeyDown;
        Refresh();
    }
    private void UpdateCollectCaption()
    {
        status.MaxWidth = Bounds.Width < 1000 ? 120 : 220;
        if (collect.Content is StackPanel row && row.Children.OfType<TextBlock>().LastOrDefault() is { } label)
            label.Text = Bounds.Width < 1000 ? L10n.Get("MainWindow.CollectWindows") : L10n.Get("MainWindow.CollectDesktopWindows");
    }
    private void OnOpened(object? sender, EventArgs args)
    {
        try
        {
            if (TryGetPlatformHandle() is { } platform) { Session.Backend.Initialize(platform.Handle); nativeReady = true; }
            Session.Backend.HoverEnabled = Session.Config.HoverScroll;
            if (OperatingSystem.IsWindows() && Session.Backend is WindowsWindowBackend windows)
            {
                windows.SignalHandler = Session.ReceiveAgentSignal;
                UpdateShortcuts();
                UpdateAttentionCount();
            }
            if (Program.CrashTarget == 0) signals = new(async signal =>
            {
                if (closing) return false;
                return await Dispatcher.UIThread.InvokeAsync(() => !closing && Session.ReceiveAgentSignal(signal));
            });
            StartDisplayTracking();
            ApplyAppearance(); UpdateCollectCaption(); Poll(); timer.Start(); QueueSync();
            if (Program.CrashTarget != 0)
            {
                var candidate = Session.Backend.Enumerate().First(c => c.Handle == Program.CrashTarget && c.ProcessId == Program.CrashTargetPid);
                Session.Attach(candidate);
                Dispatcher.UIThread.Post(() => File.WriteAllText(Path.Combine(Program.CrashDirectory!, "ready.txt"), "ready"), DispatcherPriority.Background);
            }
            if (Program.IsSelfTest) _ = DesktopSelfTest.RunAsync(this);
            else if (Program.PerformanceReport is not null && OperatingSystem.IsWindows()) _ = DesktopPerformanceTest.RunAsync(this, Program.PerformanceReport);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message); Store.Log(exception.ToString());
            if (Program.IsSelfTest && sender is MainWindow) DesktopSelfTest.FailStartup(exception);
        }
    }
    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closing) return;
        closing = true; EndPanelDrag(); timer.Stop(); saveTimer.Stop();
        StopDisplayTracking();
        Session.Config.Maximized = lastVisibleState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { Session.Config.WindowWidth = (int)Width; Session.Config.WindowHeight = (int)Height; Session.Config.WindowX = Position.X; Session.Config.WindowY = Position.Y; }
        Session.Dispose();
    }
    private void OnClosed()
    {
        Session.StructureChanged -= Refresh; Session.PanelChanged -= RefreshPanel; Session.SaveRequested -= QueueSave; Session.SelectionChanged -= RefreshSelection; Session.LayoutInvalidated -= InvalidateLayout; Session.Backend.Changed -= BackendChanged; wallpaper.Dispose();
        Session.AgentActivityChanged -= QueueAttentionUpdate;
        Session.AgentStatusTransition -= OnAgentStatusTransition;
        if (signals is not null) _ = signals.DisposeAsync();
    }
    private void QueueSave() { if (closing) return; saveTimer.Stop(); saveTimer.Start(); }
    private void Poll()
    {
        if (closing) return;
        if (dialogDepth > 0) { Session.Backend.Maintain(); return; }
        UserAction(() => { Session.Poll(); RefreshStatus(); Session.Backend.Maintain(); });
        if (status.IsVisible && DateTime.UtcNow > statusUntil) status.IsVisible = false;
    }
    internal void UserAction(Action action)
    {
        try { action(); } catch (Exception exception) { SetStatus(exception.Message); Store.Log(exception.ToString()); }
    }
    internal async Task UserActionAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception exception) { SetStatus(exception.Message); Store.Log(exception.ToString()); }
    }
    private void SetStatus(string message) { Session.Status = message; RefreshStatus(); }
    private void RefreshStatus()
    {
        if (Session.Status == lastStatus) return;
        lastStatus = Session.Status; status.Text = lastStatus; status.Foreground = DockTheme.Muted; status.IsVisible = !string.IsNullOrEmpty(lastStatus);
        ToolTip.SetTip(status, lastStatus); statusUntil = DateTime.UtcNow.AddSeconds(12);
    }
    internal int RefreshCount { get; private set; }
    internal int LayoutCount { get; private set; }
    internal void TestRefresh() => Refresh();
    internal void TestWindowEvent(WindowSystemEvent change) => BackendChanged(change);
    internal DrawingRectangle TestViewportBounds => ScreenBounds(viewport);
    internal double TestToolbarHeight => toolbar.Bounds.Height;
    internal IReadOnlyList<PanelCard> TestCards => cards.Values.ToArray();
    private void Refresh()
    {
        if (closing) return; layoutFrameQueued = false; RefreshCount++;
        if (lastWorkspace != Session.SelectedWorkspace)
        {
            offsets[lastWorkspace] = viewport.Offset.Y; lastWorkspace = Session.SelectedWorkspace;
            viewport.Offset = new(0, offsets.GetValueOrDefault(lastWorkspace));
        }
        RefreshTabs(); QueueAttentionUpdate();
        var current = Session.CurrentPanels.ToList();
        var currentIds = current.Select(panel => panel.Id).ToHashSet(); var allIds = Session.Panels.Select(panel => panel.Id).ToHashSet();
        foreach (var (id, card) in cards.ToArray())
            if (!allIds.Contains(id) || id != card.Panel.Id) { canvas.Children.Remove(card); cards.Remove(id); }
        foreach (var card in cards.Values) { card.IsVisible = currentIds.Contains(card.Panel.Id); card.RefreshTheme(); }
        foreach (var panel in current)
            if (!cards.ContainsKey(panel.Id)) { var card = new PanelCard(panel, this); cards.Add(panel.Id, card); canvas.Children.Add(card); }
        if (cards.Count > 96)
            foreach (var (id, card) in cards.Where(pair => !pair.Value.IsVisible).Take(cards.Count - 96).ToArray())
            { canvas.Children.Remove(card); cards.Remove(id); }
        empty.IsVisible = current.Count == 0; viewport.IsVisible = current.Count > 0;
        add.IsEnabled = Session.Backend.Available && current.Count < Session.Current.Capacity; collect.IsEnabled = Session.Backend.Available;
        magnet.Classes.Set("active", Session.Config.Magnet);
        var gridActive = Session.Current.Layout == "grid" && Session.FocusedPanel is null;
        grid.Classes.Set("active", gridActive);
        if (grid.Content is StackPanel gridRow && gridRow.Children.OfType<DockIcon>().FirstOrDefault() is { } gridGlyph) { gridGlyph.InkResource = gridActive ? "DockAccent" : null; gridGlyph.InvalidateVisual(); }
        grid.IsEnabled = current.Count > 0;
        ApplyAppearance(); ArrangePanels(); RefreshStatus(); QueueSync();
        // The first native frame must not wait behind persistence or a background dispatcher turn.
        if (IsVisible) { root.UpdateLayout(); FlushSync(); }
    }
    private void RefreshSelection()
    {
        foreach (var card in cards.Values) if (card.IsVisible) card.RefreshTheme();
    }
    private void InvalidateLayout(bool final)
    {
        if (closing) return;
        void Apply()
        {
            layoutFrameQueued = false;
            var active = Session.Current.Layout == "grid" && Session.FocusedPanel is null;
            grid.Classes.Set("active", active);
            if (grid.Content is StackPanel row && row.Children.OfType<DockIcon>().FirstOrDefault() is { } icon) { icon.InkResource = active ? "DockAccent" : null; icon.InvalidateVisual(); }
            ArrangePanels(); root.UpdateLayout(); FlushSync();
        }
        if (final) { Apply(); return; }
        if (layoutFrameQueued) return; layoutFrameQueued = true;
        RequestAnimationFrame(_ => { if (layoutFrameQueued && !closing) Apply(); });
    }
    private void RefreshPanel(RuntimePanel panel)
    {
        if (closing) return;
        cards.GetValueOrDefault(panel.Id)?.RefreshTheme();
        if (tabButtons.TryGetValue(panel.WorkspaceId, out var tab))
            tab.Count.Foreground = Session.Panels.Any(p => p.WorkspaceId == panel.WorkspaceId && p.Attention) ? DockTheme.Accent : DockTheme.Muted;
    }
    private void RefreshTabs()
    {
        foreach (var (id, tab) in tabButtons.ToArray())
            if (!Session.Config.Workspaces.Any(w => w.Id == id)) { tabs.Children.Remove(tab.Button); tabButtons.Remove(id); }
        foreach (var config in Session.Config.Workspaces)
        {
            if (!tabButtons.TryGetValue(config.Id, out var controls))
            {
                var name = new TextBlock { MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                var count = new TextBlock { FontSize = 11, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(name); row.Children.Add(count);
                var button = new Button { Content = row, Height = 26, MinHeight = 0 }; button.Classes.Add("dock"); button.Classes.Add("tab");
                button.Click += (_, _) => OpenWorkspace(config);
                button.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, args) =>
                {
                    if (args.GetCurrentPoint(button).Properties.IsRightButtonPressed) { ShowTabMenu(config, button); args.Handled = true; return; }
                    if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                    if (args.ClickCount == 2) { draggedTab = null; tabDragStart = null; args.Pointer.Capture(null); _ = RenameWorkspaceAsync(config); args.Handled = true; return; }
                    draggedTab = config.Id; tabDragStart = args.GetPosition(this);
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
                button.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, args) =>
                {
                    if (draggedTab is not null && tabDragStart is { } start && Math.Abs(args.GetPosition(this).X - start.X) + Math.Abs(args.GetPosition(this).Y - start.Y) > 10)
                    {
                        var source = Session.Config.Workspaces.FirstOrDefault(w => w.Id == draggedTab);
                        var destination = HitTab(args.GetPosition(this));
                        if (source is not null && destination is not null) Session.ReorderWorkspace(source, destination);
                        args.Pointer.Capture(null); args.Handled = true;
                    }
                    draggedTab = null; tabDragStart = null;
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
                tabButtons.Add(config.Id, controls = (button, name, count)); tabs.Children.Add(button);
            }
            controls.Name.Text = config.Name;
            var quantity = Session.Panels.Count(p => p.WorkspaceId == config.Id); var attention = Session.Panels.Any(p => p.WorkspaceId == config.Id && p.Attention);
            controls.Count.Text = quantity > 0 ? quantity.ToString() : ""; controls.Count.Foreground = attention ? DockTheme.Accent : DockTheme.Muted;
            controls.Button.Classes.Set("active", config.Id == Session.SelectedWorkspace);
            ToolTip.SetTip(controls.Button, $"{config.Name} · {quantity} / {config.Capacity}");
        }
        for (var index = 0; index < Session.Config.Workspaces.Count; index++)
        {
            var button = tabButtons[Session.Config.Workspaces[index].Id].Button;
            if (tabs.Children.IndexOf(button) != index) { tabs.Children.Remove(button); tabs.Children.Insert(index, button); }
        }
    }
    private void ApplyAppearance()
    {
        var theme = previewThemeId ?? Session.Config.ThemeId;
        if (lastTheme != theme)
        {
            DockTheme.Apply(theme); lastTheme = theme;
            status.Foreground = DockTheme.Muted;
            foreach (var icon in this.GetVisualDescendants().OfType<DockIcon>()) icon.InvalidateVisual();
            foreach (var card in cards.Values) card.RefreshTheme();
        }
        toolbar.Background = DockTheme.Surface;
        var requested = Session.Current.BackgroundMode == "transparent" ? "transparent" : DockTheme.IsGlass ? "glass" : "solid";
        if (Tag as string != requested)
        {
            Tag = requested;
            TransparencyLevelHint = requested == "glass" ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent] : requested == "transparent" ? [WindowTransparencyLevel.Transparent] : [WindowTransparencyLevel.None];
        }
        TransparencyBackgroundFallback = DockTheme.Ground;
        wallpaper.Configure(Session.Current);
        if (wallpaper.Error is { } error) SetStatus(error);
        if (IsVisible) Session.Backend.SetAppearance(DockTheme.IsDark);
    }
    private void ArrangePanels()
    {
        if (arranging || closing) return; arranging = true; LayoutCount++;
        try
        {
            var panels = Session.CurrentPanels.ToList(); var width = Math.Max(1, (int)viewport.Viewport.Width); var height = Math.Max(1, (int)viewport.Viewport.Height);
            if (width < 10) { width = Math.Max(1, (int)workspace.Bounds.Width - 8); height = Math.Max(1, (int)workspace.Bounds.Height); }
            var plan = FlexibleLayout.Plan(panels.Count, new DrawingSize(width, height), Session.Config.MinWidth, Session.Config.MinHeight, Session.Current, 4);
            var stack = ShowStack(panels, width);
            if (Session.FocusedPanel is { } focused)
            {
                foreach (var panel in panels)
                {
                    var card = cards[panel.Id]; card.IsVisible = panel.Id == focused;
                    if (card.IsVisible) Place(card, new(4, 4, Math.Max(1, width - 8), Math.Max(1, height - 8)));
                }
                canvas.Height = height; foreach (var grip in grips) grip.IsVisible = false;
            }
            else if (stack)
            {
                ArrangeStack(panels, width, height);
                canvas.Height = height;
                foreach (var grip in grips) grip.IsVisible = false;
            }
            else
            {
                for (var index = 0; index < panels.Count; index++) { var card = cards[panels[index].Id]; card.IsVisible = true; Place(card, plan.Tiles[index]); }
                canvas.Height = Math.Max(height, plan.Extent.Height);
                // Retain a captured divider until its gesture finishes.
                if (!grips.Any(g => g.IsDragging))
                {
                    while (grips.Count > plan.Dividers.Count) { canvas.Children.Remove(grips[^1]); grips.RemoveAt(grips.Count - 1); }
                    for (var index = 0; index < plan.Dividers.Count; index++)
                    {
                        var spec = plan.Dividers[index];
                        if (index >= grips.Count) { var grip = new LayoutGrip(spec, Session); grips.Add(grip); canvas.Children.Add(grip); }
                        grips[index].Update(spec); grips[index].IsVisible = true; Place(grips[index], spec.Bounds);
                    }
                }
            }
            canvas.Width = width;
        }
        finally { arranging = false; }
        QueueSync();
    }
    private static void Place(Control control, DrawingRectangle bounds)
    { Canvas.SetLeft(control, bounds.X); Canvas.SetTop(control, bounds.Y); control.Width = bounds.Width; control.Height = bounds.Height; }
    private void QueueSync()
    {
        if (syncQueued || closing) return; syncQueued = true;
        Dispatcher.UIThread.Post(FlushSync, DispatcherPriority.Loaded);
    }
    private void FlushSync()
    {
        if (!syncQueued || closing || arranging) return;
        syncQueued = false; UserAction(Synchronize);
    }
    private static DrawingRectangle ScreenBounds(Control control)
    {
        var start = control.PointToScreen(default); var end = control.PointToScreen(new(control.Bounds.Width, control.Bounds.Height));
        return new(start.X, start.Y, Math.Max(0, end.X - start.X), Math.Max(0, end.Y - start.Y));
    }
    private void Synchronize()
    {
        if (!nativeReady || TryGetPlatformHandle() is not { } platform) return;
        var visible = IsVisible && WindowState != WindowState.Minimized;
        List<WindowPlacement> placements = []; List<NativeHoverArea> areas = [];
        var origin = this.PointToScreen(default); var scale = RenderScaling;
        DrawingRectangle BoundsOnScreen(Control control)
        {
            var start = control.TranslatePoint(default, this) ?? default;
            var end = control.TranslatePoint(new(control.Bounds.Width, control.Bounds.Height), this) ?? start;
            var a = PixelPoint.FromPoint(start, scale); var b = PixelPoint.FromPoint(end, scale);
            return new(origin.X + a.X, origin.Y + a.Y, Math.Max(0, b.X - a.X), Math.Max(0, b.Y - a.Y));
        }
        var viewportBounds = BoundsOnScreen(viewport);
        foreach (var panel in Session.CurrentPanels)
        {
            if (!cards.TryGetValue(panel.Id, out var card) || !card.IsVisible || card.Slot.Bounds.Width <= 0) continue;
            var bounds = BoundsOnScreen(card.Slot); var clip = DrawingRectangle.Intersect(bounds, viewportBounds); if (clip.Width <= 0 || clip.Height <= 0) continue;
            placements.Add(new(panel.Record, bounds, clip, BoundsOnScreen(card), (int)Math.Round(7 * RenderScaling)));
            areas.Add(new(panel.Record.Handle, panel.Record.Handle, clip, true));
        }
        areas.Add(new(platform.Handle, platform.Handle, viewportBounds, false));
        areas.Add(new(platform.Handle, platform.Handle, BoundsOnScreen(tabScroll), false));
        Session.Backend.Synchronize(new(platform.Handle, visible), placements);
        CompletePanelActivation(placements, visible);
        Session.Backend.UpdateHoverTargets(visible ? areas : []);
    }
    internal void ToggleFocus(RuntimePanel panel)
    { Session.FocusedPanel = Session.FocusedPanel == panel.Id ? null : panel.Id; if (Session.FocusedPanel is not null) viewport.Offset = default; }
    private WorkspaceConfig? HitTab(Point point)
    {
        var screen = this.PointToScreen(point);
        return Session.Config.Workspaces.FirstOrDefault(w => tabButtons.TryGetValue(w.Id, out var tab) && ScreenBounds(tab.Button).Contains(screen.X, screen.Y));
    }
    internal void ShowDropTarget(Point? point)
    {
        var target = point is { } p ? HitTab(p)?.Id : null;
        foreach (var (id, tab) in tabButtons) { tab.Button.BorderThickness = id == target ? new(1) : new(0); tab.Button.BorderBrush = id == target ? DockTheme.Accent : Brushes.Transparent; }
    }
    internal void DropPanel(RuntimePanel panel, Point point)
    {
        if (HitTab(point) is { } workspace) { Session.Move(Session.ActionPanels(panel), workspace); return; }
        var screen = this.PointToScreen(point);
        var target = cards.Values.FirstOrDefault(c => c.IsVisible && ScreenBounds(c).Contains(screen.X, screen.Y));
        if (target is not null) Session.ReorderPanel(panel, target.Panel);
    }
    private void BackendChanged(WindowSystemEvent change) => Dispatcher.UIThread.Post(() =>
    {
        if (closing || dialogDepth > 0) return;
        UserAction(() =>
        {
            if (change.Kind == "hotkey") RunHotkey(change.Command);
            else if (change.Kind == "foreground")
            {
                var panel = Session.Panels.FirstOrDefault(p => p.Record.Handle == change.Handle);
                // Native activation events can arrive after a menu/dialog or for a hidden owner.
                // Only an explicit user navigation command is allowed to choose another workspace.
                if (panel is not null && panel.WorkspaceId == Session.SelectedWorkspace &&
                    cards.TryGetValue(panel.Id, out var card) && card.IsVisible)
                {
                    Session.RememberActivation(panel);
                    if (panel.AgentStatus is not ("waiting" or "error")) panel.Attention = false;
                    RefreshPanel(panel);
                }
            }
            else if (change.Kind == "attention")
            {
                var panel = Session.Panels.FirstOrDefault(p => p.Record.Handle == change.Handle);
                if (panel is not null) { panel.Attention = true; RefreshPanel(panel); }
            }
            else if (change.Kind == "agent-tray") _ = UserActionAsync(OpenAttentionAsync);
            else if (change.Kind == "title")
            {
                var panel = Session.Panels.FirstOrDefault(p => p.Record.Handle == change.Handle);
                if (panel is not null && Session.Backend.IsAlive(panel.Record)) panel.Title = Session.Backend.Title(panel.Record);
            }
            else if (change.Kind == "destroy") Poll();
            else if (change.Kind == "move-end" && Session.Config.Magnet && OperatingSystem.IsWindows() && Native.GetCursorPos(out var cursor) && ScreenBounds(workspace).Contains(cursor.X, cursor.Y))
            {
                var candidate = Session.Backend.Enumerate().FirstOrDefault(c => c.Handle == change.Handle);
                if (candidate is not null && !RuleEngine.Evaluate(Session.Config.Rules, candidate).Excluded) Session.Attach(candidate);
            }
        });
    });
    private void RunHotkey(int id)
    {
        if (id is >= 101 and <= 109)
        {
            var index = id - 101;
            if (index < Session.Config.Workspaces.Count) OpenWorkspace(Session.Config.Workspaces[index]);
        }
        else switch (id)
        {
            case 120: if (Session.ActivePanel(Session.Current) is { } focused) { RestoreHost(); ToggleFocus(focused); } break;
            case 121: RestoreHost(); _ = AddWindowsAsync(); break;
            case 122: WindowState = WindowState.Minimized; break;
            case 123: RestoreHost(); break;
            case 124: RestoreHost(); _ = SearchAsync(); break;
            case 125: Session.Undo(); break;
            case 126: ToggleHost(); break;
            case 127: if (Session.PreviousPanel() is { } previous) ActivatePanel(previous); break;
            case 128: NextAgent(); break;
        }
    }
    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (dialogDepth > 0) return;
        if (args.Key == Key.Escape && dragPreview is not null) { EndPanelDrag(); args.Handled = true; return; }
        var control = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (control && args.Key == Key.N) { _ = NewWorkspaceAsync(); args.Handled = true; }
        else if (control && args.Key == Key.K) { _ = SearchAsync(); args.Handled = true; }
        else if (control && args.Key == Key.Z) { Session.Undo(); args.Handled = true; }
        else if (control && args.Key == Key.A) { Session.SelectAll(true); args.Handled = true; }
        else if (args.Key == Key.Escape && Session.FocusedPanel is not null) { Session.FocusedPanel = null; args.Handled = true; }
    }
}
