using System.Runtime.Versioning;
using Avalonia.Controls;

namespace WindowDock;

internal static class DesktopWorkflowTests
{
    [SupportedOSPlatform("windows")]
    internal static async Task RunAsync(MainWindow main)
    {
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
    }
}
