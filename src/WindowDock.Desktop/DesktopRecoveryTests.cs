using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WindowDock;

[SupportedOSPlatform("windows")]
internal static class DesktopRecoveryTests
{
    internal static async Task RunAsync(MainWindow main)
    {
        await AgentCardAsync(main);
        await LaunchShortcutAsync(main);
    }

    private static async Task AgentCardAsync(MainWindow main)
    {
        var session = main.Session;
        var panel = session.CurrentPanels.First();
        var key = "popup:" + Guid.NewGuid().ToString("N");
        session.ReceiveAgentSignal(new() { SessionKey = key, Handle = panel.Record.Handle, Status = "waiting", Provider = "codex" });
        var bell = main.GetVisualDescendants().OfType<Button>().Single(button =>
            button.Content is StackPanel row && row.Children.OfType<DockIcon>().Any(icon => icon.Kind == "bell"));
        void ClickBell() => bell.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, bell));
        Popup? FindPopup() => main.GetVisualDescendants().OfType<Popup>().FirstOrDefault(item => item.Child is AgentActivityCard);
        ClickBell();
        await Task.Delay(120);
        var popup = FindPopup();
        DesktopSelfTest.Check(popup is { IsOpen: true, IsUsingOverlayLayer: false, Child: AgentActivityCard } &&
            Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            !desktop.Windows.OfType<EditorWindow>().Any(window => window.IsVisible),
            "The bell did not show a native popup without opening an editor window.");
        var card = (AgentActivityCard)popup!.Child!;
        var list = card.GetVisualDescendants().OfType<ListBox>().Single();
        var activity = list.Items.OfType<AgentActivity>().Single(item => item.Key == key);
        DesktopSelfTest.Check(activity.Panel == panel && Native.IsWindowVisible(panel.Record.Hwnd) && Native.IsWindowEnabled(panel.Record.Hwnd),
            "The popup hid the embedded terminal or missed the agent event.");
        var handle = TopLevel.GetTopLevel(card)!.TryGetPlatformHandle()!.Handle;
        Native.GetWindowRect(handle, out var bounds);
        var anchor = main.PointToScreen(new Point(main.ClientSize.Width, main.TestToolbarHeight));
        DesktopSelfTest.Check(Math.Abs(bounds.Top - anchor.Y - 8 * main.RenderScaling) < 4 &&
            Math.Abs(bounds.Right - anchor.X + 8 * main.RenderScaling) < 4, "Agent activity is not aligned under the right edge of the header.");
        DesktopSelfTest.Snapshot(card, "avalonia-agent-activity.png");
        list.SelectedItem = activity;
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Task.Delay(100);
        DesktopSelfTest.Check(FindPopup() is null && session.ActivePanel(session.Current) == panel,
            "Opening an agent from the card did not dismiss it and focus its panel.");
        ClickBell();
        await Task.Delay(65);
        ClickBell();
        DesktopSelfTest.Check(FindPopup() is null, "Clicking the bell twice left the popup open.");
        session.ReceiveAgentSignal(new() { SessionKey = key, Handle = panel.Record.Handle, Status = "clear" });
        DesktopSelfTest.Pass("The bell shows a compact right-aligned activity popup over native windows and opens the selected panel.");
    }

    private static async Task LaunchShortcutAsync(MainWindow main)
    {
        var session = main.Session;
        var reference = session.Panels.First().Record;
        var tab = session.NewWorkspace("Shortcut recovery fixture");
        var previous = session.SelectedWorkspace;
        var restoreGroups = session.Config.RestoreGroups;
        var token = "shortcut-" + Guid.NewGuid().ToString("N");
        var title = "WindowDock test " + token;
        var path = Path.Combine(Store.DirectoryPath, token + ".lnk");
        Directory.CreateDirectory(Store.DirectoryPath);
        var roles = Enumerable.Range(0, 2).Select(slot => new SavedPanel
        {
            Id = Guid.NewGuid().ToString("N"), Order = slot, WorkspaceId = tab.Id,
            ProcessName = reference.ProcessName, ClassName = reference.ClassName, ExecutablePath = reference.ExecutablePath,
            Alias = "Fixture " + slot, Title = "Previous boot", LastSeenUtc = DateTime.UtcNow.Ticks
        }).ToArray();
        try
        {
            CreateShortcut(path, token);
            session.Pending.AddRange(roles);
            session.Config.RestoreGroups = true;
            session.SetRecoveryShortcut(roles[0], path, title, reopen: true);
            await main.RestoreWorkAsync(startup: true);
            var restored = session.PanelsIn(tab).ToArray();
            DesktopSelfTest.Check(restored.Select(panel => (panel.Id, panel.Title, panel.LayoutSlot)).SequenceEqual(
                roles.Select(role => (role.Id, title, role.Order))) &&
                restored.Select(panel => panel.Record.ProcessId).Distinct().Count() == 2 && session.SelectedWorkspace == previous,
                "Opening the real .lnk file did not produce two independent native windows in their saved slots. " + session.Status);
            DesktopSelfTest.Pass("Windows resolves the original shortcut arguments and relaunches each missing window into its saved workspace.");
        }
        finally
        {
            var processes = session.PanelsIn(tab).Where(panel => panel.Title == title).Select(panel => panel.Record.ProcessId)
                .Concat(session.DesktopWindows().Where(window => window.Title == title).Select(window => window.ProcessId)).Distinct().ToArray();
            session.SetRecoveryShortcut(roles[0], "", null, reopen: false);
            session.CloseWorkspace(tab);
            session.Config.RestoreGroups = restoreGroups;
            foreach (var processId in processes)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (process.MainWindowTitle == title) process.CloseMainWindow();
                }
                catch (ArgumentException) { }
            }
            File.Delete(path);
        }
    }

    private static void CreateShortcut(string path, string token)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic? link = null;
        try
        {
            link = shell.CreateShortcut(path);
            link.TargetPath = RuntimePaths.Executable;
            link.Arguments = "--test-window " + token;
            link.WorkingDirectory = Store.DirectoryPath;
            link.Save();
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject((object)link);
            Marshal.ReleaseComObject((object)shell);
        }
    }
}
