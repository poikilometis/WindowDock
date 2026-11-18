namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly SurfaceBrushes surfaceBrushes = new();
    private bool keepingAwake;

    internal SurfaceStyle WindowStyle(RuntimePanel panel) => SurfaceStyle.Resolve(Session.Config.WindowStyle, panel.Style);
    internal Avalonia.Media.IBrush SurfaceFill(SurfaceStyle style, Avalonia.Media.IBrush fallback, int opacity = 100) =>
        surfaceBrushes.Get(style, fallback, opacity);

    internal int WindowOpacity(RuntimePanel panel) => WindowStyle(panel).OpacityPercent(15);

    internal void RefreshSurfaceMotion() => surfaceBrushes.SetRunning(
        IsVisible && WindowState != Avalonia.Controls.WindowState.Minimized ||
        workspaceWindows.Values.Any(view => view.IsVisible && view.WindowState != Avalonia.Controls.WindowState.Minimized));

    private async Task EditSurfaceAsync(SurfaceArea area, RuntimePanel? panel = null, WorkspaceConfig? tab = null)
    {
        var config = Session.Config;
        var scoped = panel is not null || tab is not null;
        SurfaceStyle? current = area switch
        {
            SurfaceArea.Window => panel is null ? config.WindowStyle : panel.Style,
            SurfaceArea.Header => config.HeaderStyle,
            _ => tab is null ? config.TabStyle : tab.TabStyle
        };
        var defaults = !scoped ? null : panel is not null ? config.WindowStyle : config.TabStyle;
        var caption = panel?.Caption ?? tab?.Name ?? L10n.Get(area switch
        {
            SurfaceArea.Window => "Surface.AllWindows",
            SurfaceArea.Header => "Surface.Header",
            _ => "Surface.AllTabs"
        });
        var canFade = panel is null || OperatingSystem.IsWindows() && WindowsWindowBackend.SupportsOpacity(panel.Record);
        var editor = new SurfaceStyleWindow(this, caption, area, current, defaults, scoped, canFade);
        if (!await ShowEditorAsync(editor)) return;

        if (panel is not null && Session.Panels.Contains(panel)) panel.Style = editor.Result;
        else if (tab is not null && config.Workspaces.Contains(tab)) tab.TabStyle = editor.Result;
        else if (!scoped)
        {
            switch (area)
            {
                case SurfaceArea.Window: config.WindowStyle = editor.Result; break;
                case SurfaceArea.Header: config.HeaderStyle = editor.Result; break;
                case SurfaceArea.Tab: config.TabStyle = editor.Result; break;
            }
        }
        surfaceBrushes.Reset();
        Refresh();
        Session.Save();
    }

    private Avalonia.Controls.MenuItem SleepMenu() => Group(L10n.Get("Power.PreventSleep"), new (string? Mode, string Key)[]
    {
        (null, "Power.Off"),
        ("open", "Power.WhileOpen"),
        ("agents", "Power.WhileAgentsWork"),
        ("docked", "Power.WhileDocked")
    }.Select(option => Item(L10n.Get(option.Key), () =>
    {
        Session.Config.SleepMode = option.Mode;
        UpdateWakefulness();
        Session.Save();
    }, OperatingSystem.IsWindows(), Session.Config.SleepMode == option.Mode)));

    private void UpdateWakefulness()
    {
        if (!OperatingSystem.IsWindows()) return;
        var requested = !closing && (Session.Config.SleepMode switch
        {
            "open" => true,
            "agents" => Session.HasWorkingHookSession(),
            "docked" => Session.Panels.Count > 0,
            _ => false
        });
        if (requested == keepingAwake) return;
        if (Native.RequestWakefulness(requested)) keepingAwake = requested;
    }
}
