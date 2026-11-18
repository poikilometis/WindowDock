using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly Dictionary<string, WorkspaceWindow> workspaceWindows = [];
    private Border? presentationNotice;
    private bool reconcilingPresentations;

    private string ScreenKey(Screen screen) => !string.IsNullOrWhiteSpace(screen.DisplayName) &&
        Screens.All.Count(display => display.DisplayName == screen.DisplayName) == 1 ? screen.DisplayName : DisplayId(screen);

    private Screen? ScreenFor(WorkspaceConfig workspace) => !string.IsNullOrWhiteSpace(workspace.PresentationDisplay)
        ? Screens.All.FirstOrDefault(screen => ScreenKey(screen) == workspace.PresentationDisplay)
        : Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();

    internal void QueueWorkspaceSync() => QueueSync();

    private void RefreshWorkspaceWindows()
    {
        if (!nativeReady || closing || reconcilingPresentations || !OperatingSystem.IsWindows() ||
            Session.Backend is not WindowsWindowBackend backend) return;
        reconcilingPresentations = true;
        try
        {
            var targets = Session.Config.Workspaces.Where(tab => !string.IsNullOrEmpty(tab.PresentationDisplay) || !string.IsNullOrEmpty(tab.VirtualDesktop))
                .Select(tab => (Tab: tab, Screen: ScreenFor(tab))).Where(target => target.Screen is not null)
                .ToDictionary(target => target.Tab.Id);
            foreach (var (id, view) in workspaceWindows.ToArray())
                if (!targets.TryGetValue(id, out var target) || !ReferenceEquals(view.Workspace, target.Tab)) view.Close();
            foreach (var (id, target) in targets)
            {
                if (!workspaceWindows.TryGetValue(id, out var view))
                {
                    view = new WorkspaceWindow(this, target.Tab);
                    view.MoveTo(target.Screen!);
                    if (!backend.RegisterWorkspaceHost(view.Handle))
                    {
                        view.Close();
                        SetStatus(L10n.Get("Presentation.OpenFailed"));
                        continue;
                    }
                    workspaceWindows.Add(id, view);
                    view.Closing += (_, args) =>
                    {
                        if (closing || !OperatingSystem.IsWindows()) return;
                        args.Cancel = !backend.ReleaseWorkspaceHost(view.Handle);
                        if (args.Cancel) SetStatus(L10n.Get("Presentation.WaitForWindows"));
                    };
                    view.Closed += (_, _) =>
                    {
                        workspaceWindows.Remove(id);
                        if (closing || reconcilingPresentations) return;
                        target.Tab.PresentationDisplay = null;
                        target.Tab.VirtualDesktop = null;
                        Session.Save();
                        Dispatcher.UIThread.Post(Refresh);
                    };
                    view.LayoutUpdated += (_, _) => QueueSync();
                    view.Show();
                }
                view.MoveTo(target.Screen!);
                if (view.DesktopBinding != target.Tab.VirtualDesktop)
                {
                    view.DesktopBinding = target.Tab.VirtualDesktop;
                    if (Guid.TryParse(view.DesktopBinding, out var desktop) && VirtualDesktops.Desktop(view.Handle) != desktop &&
                        !VirtualDesktops.Move(view.Handle, desktop))
                        SetStatus(L10n.Get("Presentation.DesktopUnavailable"));
                }
                view.Refresh();
            }
        }
        finally { reconcilingPresentations = false; }
    }

    private void ShowPresentationNotice(bool presented)
    {
        if (!presented)
        {
            if (presentationNotice is not null) presentationNotice.IsVisible = false;
            return;
        }
        presentationNotice ??= CreatePresentationNotice();
        presentationNotice.IsVisible = true;
    }

    private Border CreatePresentationNotice()
    {
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8,
            Children =
            {
                DockControls.Note(L10n.Get("Presentation.SeparateWindow")),
                DockControls.Button(L10n.Get("Workflow.Open"), "focus", () => ShowWorkspaceWindow(Session.Current)),
                DockControls.Button(L10n.Get("Presentation.ReturnTab"), "back", () => ReturnWorkspace(Session.Current))
            }
        };
        var notice = new Border { Child = content, Padding = new(16) };
        Grid.SetRowSpan(notice, 2);
        workspace.Children.Add(notice);
        return notice;
    }

    private void ShowWorkspaceWindow(WorkspaceConfig tab)
    {
        if (!OperatingSystem.IsWindows() || !workspaceWindows.TryGetValue(tab.Id, out var view)) return;
        if (!VirtualDesktops.OnCurrent(view.Handle))
        {
            SetStatus(L10n.Get("Presentation.SwitchDesktop"));
            return;
        }
        if (view.WindowState == WindowState.Minimized) view.WindowState = WindowState.Normal;
        view.Activate();
    }

    private void ReturnWorkspace(WorkspaceConfig tab)
    {
        if (workspaceWindows.TryGetValue(tab.Id, out var view))
        {
            view.Close();
            return;
        }
        tab.PresentationDisplay = null;
        tab.VirtualDesktop = null;
        Session.Save();
        Refresh();
    }

    internal WorkspaceWindow? TestWorkspaceWindow(string id) => workspaceWindows.GetValueOrDefault(id);

    internal void PresentWorkspace(WorkspaceConfig tab, Screen display)
    {
        Session.BeginLayoutChange();
        tab.PresentationDisplay = ScreenKey(display);
        Refresh();
        if (Session.SelectedWorkspace == tab.Id && workspaceWindows.ContainsKey(tab.Id) &&
            Session.Config.Workspaces.FirstOrDefault(other => other != tab && !workspaceWindows.ContainsKey(other.Id)) is { } next)
            Session.SelectedWorkspace = next.Id;
        Session.Save();
    }

    internal void BindWorkspaceDesktop(WorkspaceConfig tab)
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle ||
            VirtualDesktops.Desktop(handle.Handle) is not { } desktop)
        {
            SetStatus(L10n.Get("Presentation.DesktopUnavailable"));
            return;
        }
        Session.BeginLayoutChange();
        tab.VirtualDesktop = desktop.ToString("D");
        Refresh();
        Session.Save();
    }

    private MenuItem PresentationMenu(WorkspaceConfig tab)
    {
        List<Control> items = [];
        foreach (var (display, index) in Screens.All.Select((screen, index) => (screen, index)))
        {
            var name = string.IsNullOrWhiteSpace(display.DisplayName) ? L10n.Format("Workflow.DisplayNumber", index + 1) : display.DisplayName;
            items.Add(Item(name, () => PresentWorkspace(tab, display), OperatingSystem.IsWindows(), tab.PresentationDisplay == ScreenKey(display)));
        }
        items.Add(new Separator());
        items.Add(Item(L10n.Get("Presentation.BindDesktop"), () => BindWorkspaceDesktop(tab), OperatingSystem.IsWindows()));
        items.Add(Item(L10n.Get("Presentation.ReturnTab"), () => ReturnWorkspace(tab),
            !string.IsNullOrWhiteSpace(tab.PresentationDisplay) || !string.IsNullOrWhiteSpace(tab.VirtualDesktop)));
        return Group(L10n.Get("Presentation.OpenOnDisplay"), items);
    }
}
