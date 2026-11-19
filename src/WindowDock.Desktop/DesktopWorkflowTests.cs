using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

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
        await AgentNotificationsAsync(main);
    }

    [SupportedOSPlatform("windows")]
    private static async Task AgentNotificationsAsync(MainWindow main)
    {
        var session = main.Session;
        var backend = (WindowsWindowBackend)session.Backend;
        var originalTab = session.Current;
        var otherTab = session.Config.Workspaces.First(workspace => workspace.Id != originalTab.Id);
        var panels = session.CurrentPanels.Take(2).ToArray();
        var first = panels[0];
        var second = panels[1];
        var state = main.WindowState;
        var (enabled, completion) = (session.Config.AgentNotifications, session.Config.NotifyOnCompletion);
        var icons = new Dictionary<uint, uint>();
        var shown = new List<uint>();
        var moved = false;
        main.SetAgentNotifications(false, false);
        backend.TestTrayOperation = (uint operation, ref WindowsAttentionTray.NotificationData data) =>
        {
            if (operation == 0) icons.Add(data.Id, data.State); // NIM_ADD
            if (operation == 1 && data.Info.Length > 0) shown.Add(data.Id); // NIM_MODIFY with a balloon
            if (operation == 2) icons.Remove(data.Id); // NIM_DELETE
            return true;
        };
        var host = main.TryGetPlatformHandle()!.Handle;
        void Click(uint id) => Native.SendMessageTimeout(host, WindowsAttentionTray.CallbackMessage, 0,
            (nint)((id << 16) | WindowsAttentionTray.BalloonUserClick), 2, 1000, out _);
        try
        {
            main.SetAgentNotifications(true, true);
            const string firstKey = "codex:notification-qa";
            const string secondKey = "claude:notification-qa";
            DesktopSelfTest.Check(session.ReceiveAgentSignal(new() { Handle = first.Record.Handle, SessionKey = firstKey, Status = "waiting" }) &&
                session.ReceiveAgentSignal(new() { Handle = second.Record.Handle, SessionKey = secondKey, Status = "done" }),
                "Agent notifications could not be bound to the test windows.");
            DesktopSelfTest.Check(shown.Count == 2 && shown[0] != shown[1] && shown.All(id => icons[id] == 1),
                "Each agent notification needs a distinct, hidden Shell icon for its callback.");

            // An older notification stays tied to its source after a later notification and a status update.
            session.ReceiveAgentSignal(new() { SessionKey = firstKey, Status = "working" });
            moved = session.Move([second], otherTab);
            DesktopSelfTest.Check(moved, "Could not move the notification target to another tab.");
            session.SelectedWorkspace = otherTab.Id;
            main.WindowState = WindowState.Minimized;
            await Task.Delay(80);
            Click(shown[0]);
            await Task.Delay(150);
            DesktopSelfTest.Check(main.WindowState != WindowState.Minimized && session.SelectedWorkspace == originalTab.Id &&
                session.ActivePanel(originalTab) == first && Native.IsWindowVisible(first.Record.Hwnd),
                "The first notification did not restore the workspace and reveal its own window.");
            var firstCard = main.TestCards.Single(card => card.Panel.Id == first.Id);
            var highlight = firstCard.GetVisualDescendants().OfType<NotificationHighlight>().SingleOrDefault();
            DesktopSelfTest.Check(highlight is { IsEffectivelyVisible: true, IsHitTestVisible: false },
                "The notification did not add a non-interactive highlight to its card.");
            DesktopSelfTest.Snapshot(main, "avalonia-notification-highlight.png");

            var editor = new NameEditor("Notification click while editing", "Fixture");
            var dialog = main.ShowEditorAsync(editor);
            try
            {
                await Task.Delay(80);
                Click(shown[1]);
                await Task.Delay(60);
                DesktopSelfTest.Check(session.SelectedWorkspace == originalTab.Id && editor.IsVisible,
                    "A notification changed the tab before the current editor closed.");
            }
            finally { editor.Close(false); await dialog; }
            await Task.Delay(150);
            DesktopSelfTest.Check(session.SelectedWorkspace == otherTab.Id && session.ActivePanel(otherTab) == second &&
                Native.IsWindowVisible(second.Record.Hwnd), "The completion notification did not reveal its window after moving it to another tab.");
            var secondCard = main.TestCards.Single(card => card.Panel.Id == second.Id);
            DesktopSelfTest.Check(highlight is { IsEffectivelyVisible: true } &&
                secondCard.GetVisualDescendants().Contains(highlight) && !firstCard.GetVisualDescendants().Contains(highlight),
                "A new notification did not move the highlight to its own card.");
            DesktopSelfTest.Check(Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                !desktop.Windows.OfType<EditorWindow>().Any(window => window.IsVisible),
                "Clicking a notification opened the generic agent activity dialog.");
            DesktopSelfTest.Check(shown.All(id => !icons.ContainsKey(id)), "Clicked notification icons were left registered.");
            await Task.Delay(4100);
            DesktopSelfTest.Check(highlight is { IsVisible: false }, "The notification highlight stays visible after four seconds.");
            DesktopSelfTest.Pass("Notification clicks reveal their native windows and transfer a four-second highlight to the target card.");
        }
        finally
        {
            main.SetAgentNotifications(false, false);
            backend.TestTrayOperation = null;
            main.SetAgentNotifications(enabled, completion);
            if (moved) session.Undo();
            session.SelectedWorkspace = originalTab.Id;
            main.WindowState = state;
            main.TestRefresh();
        }
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
