using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace WindowDock;

public partial class App : Application
{
    internal static WorkspaceSession? Session { get; private set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsWindows() && Program.HoverProbeDirectory is { } probeDirectory) { DockTheme.Apply("graphite"); desktop.MainWindow = new HoverProbeWindow(probeDirectory); base.OnFrameworkInitializationCompleted(); return; }
            if (Program.TestSubjectToken is { } token) { DockTheme.Apply("graphite"); desktop.MainWindow = new FixtureWindow(token); base.OnFrameworkInitializationCompleted(); return; }
            AppConfig config;
            try { config = Store.Read<AppConfig>(Store.ConfigPath) ?? AppConfig.Default(); }
            catch (Exception exception)
            {
                Store.Log(exception.ToString());
                if (File.Exists(Store.ConfigPath)) File.Copy(Store.ConfigPath, Store.ConfigPath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), true);
                config = AppConfig.Default();
            }
            if (Program.IsSelfTest || Program.PerformanceReport is not null)
            {
                config = AppConfig.Default(); config.WindowWidth = 1160; config.WindowHeight = 820; config.MinWidth = 320; config.MinHeight = 220;
                config.AutomaticRules = false; config.RestoreGroups = false; config.Workspaces[0].Capacity = 10;
                config.Workspaces.Add(WorkspaceConfig.Create(L10n.Get("Appaxaml.SecondTab")));
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            config.Validate();
            IWindowBackend backend = OperatingSystem.IsWindows() ? new WindowsWindowBackend() : new UnsupportedWindowBackend(L10n.Get("Appaxaml.TheInterfaceIsAvailableDockingOtherApplications"));
            Session = new(config, backend);
            DockTheme.Apply(config.ThemeId);
            desktop.MainWindow = new MainWindow(Session);
        }
        base.OnFrameworkInitializationCompleted();
    }
}