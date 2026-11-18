using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DrawingRectangle = System.Drawing.Rectangle;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly DispatcherTimer displayChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer placementTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private string? displayTopology;
    private bool adjustingDisplay;

    private void StartDisplayTracking()
    {
        Screens.Changed += OnDisplaysChanged;
        displayChangeTimer.Tick += (_, _) =>
        {
            displayChangeTimer.Stop();
            ApplyDisplayTopology();
            Refresh();
        };
        placementTimer.Tick += (_, _) =>
        {
            placementTimer.Stop();
            SaveDisplayPlacement();
        };
        ApplyDisplayTopology();
    }

    private void StopDisplayTracking()
    {
        Screens.Changed -= OnDisplaysChanged;
        displayChangeTimer.Stop();
        placementTimer.Stop();
        SaveDisplayPlacement();
    }

    private void OnDisplaysChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnDisplaysChanged(sender, args));
            return;
        }
        if (closing) return;
        placementTimer.Stop();
        displayChangeTimer.Stop();
        displayChangeTimer.Start();
    }

    private static string DisplayId(Screen screen) => FormattableString.Invariant(
        $"{screen.DisplayName}|{screen.Bounds.X},{screen.Bounds.Y}:{screen.Bounds.Width}x{screen.Bounds.Height}");

    private static string Topology(IReadOnlyList<Screen> screens) => string.Join(";", screens.Select(screen =>
        FormattableString.Invariant($"{DisplayId(screen)}|{screen.Scaling:F3}|{screen.IsPrimary}")).Order(StringComparer.Ordinal));

    private void ApplyDisplayTopology()
    {
        if (closing || WindowState == WindowState.Minimized) return;
        var displays = Screens.All;
        if (displays.Count == 0)
        {
            if (Session.Config.Maximized) WindowState = WindowState.Maximized;
            return;
        }
        var topology = Topology(displays);
        if (topology == displayTopology) return;
        var saved = Session.Config.DisplayPlacements.LastOrDefault(placement => placement.Topology == topology);
        var primary = displays.FirstOrDefault(screen => screen.IsPrimary) ?? displays[0];
        var current = Screens.ScreenFromWindow(this);
        var target = displays.FirstOrDefault(screen => DisplayId(screen) == saved?.Display) ?? current ?? primary;
        var area = target.WorkingArea;
        var scale = target.Scaling > 0 ? target.Scaling : 1;
        if (saved is null)
        {
            var legacyPosition = displayTopology is null && Session.Config.WindowX != int.MinValue
                ? new PixelPoint(Session.Config.WindowX, Session.Config.WindowY)
                : Position;
            saved = new()
            {
                Left = (legacyPosition.X - area.X) / scale,
                Top = (legacyPosition.Y - area.Y) / scale,
                Width = Session.Config.WindowWidth + FrameInsets().Width,
                Height = Session.Config.WindowHeight + FrameInsets().Height,
                Maximized = displayTopology is null ? Session.Config.Maximized : lastVisibleState == WindowState.Maximized
            };
        }
        displayTopology = topology;
        ApplyPlacement(target, saved);
    }

    private void ApplyPlacement(Screen target, DisplayPlacement saved)
    {
        var area = target.WorkingArea;
        if (area.Width <= 0 || area.Height <= 0) return;
        var scale = target.Scaling > 0 ? target.Scaling : 1;
        var bounds = saved.Restore(new DrawingRectangle(area.X, area.Y, area.Width, area.Height), scale);
        adjustingDisplay = true;
        try
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            var frame = FrameInsets();
            MinWidth = Math.Clamp(area.Width / scale - frame.Width, 1, 640);
            MinHeight = Math.Clamp(area.Height / scale - frame.Height, 1, 360);
            Width = Math.Max(MinWidth, bounds.Width / scale - frame.Width);
            Height = Math.Max(MinHeight, bounds.Height / scale - frame.Height);
            Position = new(bounds.X, bounds.Y);
            if (saved.Maximized) WindowState = WindowState.Maximized;
        }
        finally
        {
            adjustingDisplay = false;
            SchedulePlacementSave();
        }
    }

    private Size FrameInsets() => FrameSize is { } frame
        ? new Size(Math.Max(0, frame.Width - ClientSize.Width), Math.Max(0, frame.Height - ClientSize.Height))
        : default;

    private void SchedulePlacementSave()
    {
        if (displayTopology is null || adjustingDisplay || closing || WindowState == WindowState.Minimized) return;
        placementTimer.Stop();
        placementTimer.Start();
    }

    private void SaveDisplayPlacement()
    {
        if (displayTopology is null || adjustingDisplay || WindowState == WindowState.Minimized) return;
        if (Topology(Screens.All) != displayTopology)
        {
            if (!closing) OnDisplaysChanged(this, EventArgs.Empty);
            return;
        }
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var frame = FrameInsets();
        var existing = Session.Config.DisplayPlacements.LastOrDefault(placement => placement.Topology == displayTopology);
        var maximized = WindowState == WindowState.Maximized;
        var placement = new DisplayPlacement
        {
            Topology = displayTopology,
            Display = DisplayId(screen),
            Left = maximized ? existing?.Left ?? 0 : (Position.X - area.X) / scale,
            Top = maximized ? existing?.Top ?? 0 : (Position.Y - area.Y) / scale,
            Width = maximized ? existing?.Width ?? Session.Config.WindowWidth + frame.Width : Width + frame.Width,
            Height = maximized ? existing?.Height ?? Session.Config.WindowHeight + frame.Height : Height + frame.Height,
            Maximized = maximized
        };
        if (!placement.IsValid) return;
        if (existing is not null) Session.Config.DisplayPlacements.Remove(existing);
        Session.Config.DisplayPlacements.Add(placement);
        if (Session.Config.DisplayPlacements.Count > 12) Session.Config.DisplayPlacements.RemoveAt(0);
        QueueSave();
    }

    private MenuItem DisplayMenu()
    {
        var current = Screens.ScreenFromWindow(this);
        var displays = Screens.All.Select((screen, index) => (Control)Item(
            string.IsNullOrWhiteSpace(screen.DisplayName) ? L10n.Format("Workflow.DisplayNumber", index + 1) : screen.DisplayName,
            () => ApplyPlacement(screen, new()
            {
                Left = 24, Top = 24,
                Width = (WindowState == WindowState.Normal ? Width : Session.Config.WindowWidth) + FrameInsets().Width,
                Height = (WindowState == WindowState.Normal ? Height : Session.Config.WindowHeight) + FrameInsets().Height,
                Maximized = WindowState == WindowState.Maximized
            }), check: screen.Equals(current))).ToArray();
        return Group(L10n.Get("Workflow.MoveToDisplay"), displays);
    }
}
