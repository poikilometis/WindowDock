using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;

namespace WindowDock;

internal static class DesktopWorkflowTests
{
    [SupportedOSPlatform("windows")]
    internal static async Task RunAsync(MainWindow main)
    {
        const string terminalSettings = "{\n // retained comment\n \"theme\": \"dark\",\n}";
        var terminalLayout = WindowsTerminalLayout.UpdateSetting(terminalSettings);
        DesktopSelfTest.Check(terminalLayout.Contains("retained comment", StringComparison.Ordinal) &&
            JsonSyntax.Parse(terminalLayout).String("firstWindowPreference") == "persistedWindowLayout" &&
            WindowsTerminalLayout.UpdateSetting(terminalLayout) == terminalLayout,
            "Terminal layout recovery did not preserve unrelated JSONC or emitted a duplicate setting.");
        var session = main.Session;
        var tab = session.Current;
        var otherTab = session.Config.Workspaces.First(workspace => workspace.Id != tab.Id);
        var panels = session.CurrentPanels.Take(3).ToArray();
        var width = main.Width;
        var height = main.Height;
        var state = main.WindowState;
        var oldLayout = tab.Layout;
        try
        {
            tab.Layout = "adaptive";
            main.WindowState = WindowState.Normal;
            main.Width = 680;
            main.Height = 420;
            main.ActivatePanel(panels[1]);
            main.TestRefresh();
            await Task.Delay(150);
            DesktopSelfTest.Check(Native.IsWindowVisible(panels[1].Record.Hwnd) &&
                !Native.IsWindowVisible(panels[0].Record.Hwnd), "The adaptive layout did not keep the focused real window readable.");
            DesktopSelfTest.Snapshot(main, "avalonia-adaptive.png");
            main.ActivatePanel(panels[2]);
            await Task.Delay(120);
            DesktopSelfTest.Check(session.ActivePanel(tab) == panels[2] &&
                Native.IsWindowVisible(panels[2].Record.Hwnd) && !Native.IsWindowVisible(panels[1].Record.Hwnd),
                "Switching the stack targeted a hidden panel.");
            session.SelectedWorkspace = otherTab.Id;
            main.TestWindowEvent(new("hotkey", 0, 101 + session.Config.Workspaces.IndexOf(tab)));
            await Task.Delay(140);
            DesktopSelfTest.Check(session.SelectedWorkspace == tab.Id && session.ActivePanel(tab) == panels[2] &&
                Native.IsWindowVisible(panels[2].Record.Hwnd), "A tab shortcut did not restore the last active stack window.");
            main.TestWindowEvent(new("hotkey", 0, 122));
            await Task.Delay(120);
            DesktopSelfTest.Check(main.WindowState == WindowState.Minimized && !Native.IsWindowVisible(panels[2].Record.Hwnd),
                $"Minimizing WindowDock left the external panel visible (state={main.WindowState}, visible={Native.IsWindowVisible(panels[2].Record.Hwnd)}).");
            main.TestWindowEvent(new("hotkey", 0, 126));
            await Task.Delay(120);
            DesktopSelfTest.Check(main.WindowState == WindowState.Normal && Native.IsWindowVisible(panels[2].Record.Hwnd),
                "Restoring WindowDock failed to reveal the last active window.");
            DesktopSelfTest.Pass("Adaptive stack, activation order, focus memory across tabs and global show/minimize retain the correct native window.");
        }
        finally
        {
            tab.Layout = oldLayout;
            main.WindowState = WindowState.Normal;
            main.Width = width;
            main.Height = height;
            main.WindowState = state;
            main.TestRefresh();
        }
        await PresentationAsync(main, panels[0]);
        await CompatibilityAsync(main);
        await SwapByDropAsync(main);
    }

    [SupportedOSPlatform("windows")]
    private static async Task SwapByDropAsync(MainWindow main)
    {
        var session = main.Session;
        var original = session.SelectedWorkspace;
        var panels = session.CurrentPanels.Take(6).ToArray();
        DesktopSelfTest.Check(panels.Length == 6, "A six-window grid is required for the swap regression.");
        var tab = session.NewWorkspace("Grid drop fixture");
        tab.Columns = 3;
        var moved = false;
        var swapped = false;
        var width = main.Width;
        var height = main.Height;
        try
        {
            moved = session.Move(panels, tab);
            DesktopSelfTest.Check(moved, "Could not put the six fixture windows in a dedicated grid.");
            main.Width = 1160;
            main.Height = 820;
            await Task.Delay(180);
            var before = panels.Select(Bounds).ToArray();
            DesktopSelfTest.Check(before[1].X == before[4].X && before[1].Y < before[4].Y,
                "The middle panels are not in the same column.");
            var destination = main.TestCards.Single(card => card.Panel == panels[1]);
            var point = destination.TranslatePoint(new Avalonia.Point(destination.Bounds.Width / 2, 11), main)!.Value;
            main.DropPanel(panels[4], point);
            swapped = panels[4].LayoutSlot == 1 && panels[1].LayoutSlot == 4;
            await Task.Delay(160);
            DesktopSelfTest.Check(swapped && Bounds(panels[4]) == before[1] && Bounds(panels[1]) == before[4],
                "Dropping the lower middle window did not exchange the two native bounds.");
            foreach (var index in new[] { 0, 2, 3, 5 })
                DesktopSelfTest.Check(Bounds(panels[index]) == before[index], "Dropping one window moved an unrelated pane.");
            DesktopSelfTest.Pass("Dropping the bottom middle panel on top middle swaps only those two real windows.");
        }
        finally
        {
            if (swapped) session.Undo();
            if (moved) session.Undo();
            session.CloseWorkspace(session.Config.Workspaces.Single(workspace => workspace.Id == tab.Id));
            session.SelectedWorkspace = original;
            main.Width = width;
            main.Height = height;
            main.TestRefresh();
        }
        static System.Drawing.Rectangle Bounds(RuntimePanel panel)
        {
            Native.GetWindowRect(panel.Record.Hwnd, out var bounds);
            return bounds.Rectangle;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task CompatibilityAsync(MainWindow main)
    {
        var candidate = await DesktopSelfTest.SubjectAsync(main, "compatibility-" + Guid.NewGuid().ToString("N")[..8]);
        var original = Native.GetLong(candidate.Handle, Native.GWL_STYLE);
        var owner = Native.GetWindow(candidate.Handle, 4);
        var panel = main.Session.Attach(candidate);
        try
        {
            main.Session.KeepOriginalWindow(panel);
            await Task.Delay(90);
            DesktopSelfTest.Check(panel.Record.Linked && panel.Capture().Linked &&
                Native.GetLong(candidate.Handle, Native.GWL_STYLE) == original && Native.GetWindow(candidate.Handle, 4) == owner,
                "Linked mode changed the application's original native frame or owner.");
            DesktopSelfTest.Check(main.TestCards.First(card => card.Panel == panel).Slot.Child is Button &&
                Native.IsWindowVisible(candidate.Handle), "Linked mode did not offer the original live window.");
            DesktopSelfTest.Pass("A live embedded panel can become a link with its original frame, window owner and process restored.");
        }
        finally { main.Session.ReleasePanels([panel]); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task PresentationAsync(MainWindow main, RuntimePanel panel)
    {
        var session = main.Session;
        var primary = session.Current;
        var testTab = session.NewWorkspace("Display fixture");
        var display = main.Screens.ScreenFromWindow(main) ?? main.Screens.All[0];
        var host = main.TryGetPlatformHandle()!.Handle;
        try
        {
            session.Move([panel], testTab);
            main.PresentWorkspace(testTab, display);
            await Task.Delay(180);
            var view = main.TestWorkspaceWindow(testTab.Id);
            DesktopSelfTest.Check(view is { IsVisible: true } && Native.GetWindow(panel.Record.Hwnd, 4) == view.Handle &&
                Native.IsWindowVisible(panel.Record.Hwnd), "A separate workspace did not receive its interactive native pane.");
            DesktopSelfTest.Check(session.SelectedWorkspace == primary.Id && session.CurrentPanels.Any(other => Native.IsWindowVisible(other.Record.Hwnd)),
                "Opening another workspace replaces the main monitor's workspace.");
            if (VirtualDesktops.Desktop(host) is { } desktop)
            {
                main.BindWorkspaceDesktop(testTab);
                DesktopSelfTest.Check(testTab.VirtualDesktop == desktop.ToString("D") && VirtualDesktops.Desktop(view!.Handle) == desktop,
                    "Workspace binding did not use the current Windows virtual desktop.");
            }
            main.WindowState = WindowState.Minimized;
            await Task.Delay(100);
            DesktopSelfTest.Check(Native.IsWindowVisible(panel.Record.Hwnd), "Minimizing the primary window hides a separate workspace.");
            main.WindowState = WindowState.Normal;
            view!.Close();
            await Task.Delay(120);
            DesktopSelfTest.Check(Native.IsWindow(panel.Record.Hwnd) && Native.GetWindow(panel.Record.Hwnd, 4) == host &&
                main.TestWorkspaceWindow(testTab.Id) is null && testTab.PresentationDisplay is null,
                "Returning the tab did not preserve the original application and its owner.");
            DesktopSelfTest.Pass("A second workspace owns its real native panes independently, binds to the current virtual desktop and returns its panes intact.");
        }
        finally
        {
            main.WindowState = WindowState.Normal;
            main.TestWorkspaceWindow(testTab.Id)?.Close();
            session.Move([panel], primary);
            session.CloseWorkspace(testTab);
            session.SelectedWorkspace = primary.Id;
            main.TestRefresh();
        }
    }
}
