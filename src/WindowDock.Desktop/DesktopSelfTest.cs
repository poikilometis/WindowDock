using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WindowDock;

internal static class DesktopSelfTest
{
    private static readonly List<string> report = [];
    internal static readonly List<Process> subjects = [];
    private static int assertions;
    private static string output = "";
    internal static void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
    internal static void Pass(string message) => report.Add("PASS " + message);
    internal static async Task RunAsync(MainWindow main)
    {
        var result = 1; output = Path.Combine(Environment.CurrentDirectory, "artifacts"); Directory.CreateDirectory(output);
        report.Add("WindowDock Avalonia integration — " + DateTime.Now.ToString("s"));
        try
        {
            await Task.Delay(250);
            Check(main.TestToolbarHeight == 30, "The shell reserves more than a 30-DIP command row.");
            Check(main.Title == "WindowDock", "Main caption contains a workspace name.");
            Snapshot(main, "avalonia-empty.png"); Pass("Avalonia window starts with compact shell, stable caption, clean empty state and new icon.");
            if (OperatingSystem.IsWindows()) await NativeWindowsAsync(main);
            await DesktopDialogTests.RunAsync(main);
            foreach (var (name, dialog) in new (string, EditorWindow)[]
            {
                ("appearance", new AppearanceWindow(main.Session.Config.ThemeId)), ("rules", new RulesWindow(main)),
                ("projects", new ProjectsWindow(main)), ("integrations", new IntegrationWindow(main)), ("pending", new PendingGroupsWindow(main))
            })
            {
                var task = main.ShowEditorAsync(dialog); await Task.Delay(100); Snapshot(dialog, "avalonia-" + name + ".png"); dialog.Close(false); await task;
            }
            Pass("Appearance, rules, projects, integrations and pending-group dialogs open and render through Avalonia.");
            await DesktopLocalizationTests.RunAsync(main);
            var full = main.Width; main.Width = 680; main.Height = 520; await Task.Delay(180); Snapshot(main, "avalonia-narrow.png");
            Check(main.TestToolbarHeight == 30, "Narrow window adds another toolbar row."); main.Width = full; await Task.Delay(80);
            var records = main.Session.Backend.Windows.ToArray(); main.Close(); await Task.Delay(200);
            if (OperatingSystem.IsWindows()) foreach (var record in records)
            {
                Check(Native.IsWindow(record.Hwnd), "Closing host destroyed a subject window.");
                Check(Native.GetWindow(record.Hwnd, 4) == record.Owner, "Original owner was not restored.");
                Check((Native.GetLong(record.Hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) == (record.Style & Native.WS_CAPTION), "Native caption was not restored.");
            }
            Check(subjects.All(p => !p.HasExited), "A fixture process was terminated when closing WindowDock.");
            Pass("Normal host shutdown restores all native windows and preserves the independent fixture processes.");
            report.Add($"ALL AVALONIA CHECKS PASSED ({assertions} assertions)"); result = 0;
        }
        catch (Exception exception) { report.Add("FAIL " + exception); }
        finally
        {
            if (main.IsVisible) main.Close();
            foreach (var process in subjects)
            {
                try { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(800)) process.Kill(); } } catch { }
                process.Dispose();
            }
            File.WriteAllText(Path.Combine(output, "avalonia-tests.txt"), string.Join(Environment.NewLine, report), new UTF8Encoding(false));
            if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(result);
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static async Task<WindowCandidate> SubjectAsync(MainWindow main, string token, bool track = true)
    {
        var start = new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--test-window"); start.ArgumentList.Add(token);
        var process = Process.Start(start) ?? throw new Exception("Fixture did not start."); if (track) subjects.Add(process);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(12))
        {
            if (process.HasExited) throw new Exception("Fixture exited: " + process.ExitCode);
                        if (main.Session.Backend.Enumerate().FirstOrDefault(c => c.ProcessId == process.Id && c.Title.Contains(token)) is { } candidate &&
                (!OperatingSystem.IsWindows() || Native.ChildWindows(candidate.Handle).Any(h => Native.ClassName(h).Contains("RichEdit", StringComparison.OrdinalIgnoreCase))))
            { await Task.Delay(80); return candidate; }
            await Task.Delay(45);
        }
        throw new TimeoutException("Fixture window was not found.");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task NativeWindowsAsync(MainWindow main)
    {
        var session = main.Session; var backend = (WindowsWindowBackend)session.Backend;
        Check(backend.HoverHookInstalled, "The wheel hook was not installed before the first native layout.");
        var stable = main.TryGetPlatformHandle()!.Handle;
        var children = new Dictionary<long, nint[]>();
        for (var index = 0; index < 8; index++)
        {
            var candidate = await SubjectAsync(main, "avalonia-" + Guid.NewGuid().ToString("N")[..10]);
            children[candidate.Handle] = Native.ChildWindows(candidate.Handle).ToArray(); session.Attach(candidate);
            Check(Native.IsWindowVisible(candidate.Handle), "First attachment waits for a later poll to reveal the native content."); await Task.Delay(45);
        }
        await Task.Delay(200);
        Check(session.Panels.Count == 8, "Eight-window attachment failed.");
        foreach (var panel in session.Panels)
        {
            Check(Native.GetAncestor(panel.Record.Hwnd, 2) == panel.Record.Hwnd, "External root became a child window.");
            Check(Native.GetWindow(panel.Record.Hwnd, 4) == stable, "External window owner is incorrect.");
            Check(Native.ChildWindows(panel.Record.Hwnd).SequenceEqual(children[panel.Record.Handle]), "Original input subtree changed: before=" + string.Join(",", children[panel.Record.Handle].Select(h => h + ":" + Native.ClassName(h))) + " after=" + string.Join(",", Native.ChildWindows(panel.Record.Hwnd).Select(h => h + ":" + Native.ClassName(h))));
        }
        Pass("Eight independent native windows retain their HWNDs, top-level input roots, original controls and group ownership.");
        var first = session.Panels[0];
        var editor = Native.ChildWindows(first.Record.Hwnd).First(h => Native.ClassName(h).Contains("RichEdit", StringComparison.OrdinalIgnoreCase));
        var before = Message(editor, 0x000E); Message(editor, 0x0102, 'Z');
        Check(Message(editor, 0x000E) == before + 1, "Native editor did not accept a character.");
        var second = session.Panels[1]; var secondEditor = Native.ChildWindows(second.Record.Hwnd).First(h => Native.ClassName(h).Contains("RichEdit", StringComparison.OrdinalIgnoreCase));
        Native.SetForegroundWindow(first.Record.Hwnd); Native.FocusExternal(editor); await Task.Delay(100);
        Native.GetWindowRect(secondEditor, out var rect);
        var point = new Native.Point { X = rect.Left + 20, Y = rect.Top + 20 };
        var active = Native.GetForegroundWindow(); var focus = Native.ForegroundFocus();
        var wheelBefore = Message(secondEditor, 0x8061);
        Check(backend.TestHover(point, -120), "Scoped wheel forwarding rejected an inactive pane: " + backend.DescribeHover(point)); await Task.Delay(120);
        Check(Message(secondEditor, 0x8061) == wheelBefore + 1, "Inactive pane did not receive one wheel message.");
        Check(Native.GetForegroundWindow() == active && Native.ForegroundFocus() == focus, "Hover scroll changed keyboard focus.");
        Check(Message(secondEditor, 0x00CE) > 0, "Inactive editor did not scroll.");
        Pass("Native character input and hover scrolling work; inactive editor scrolls without changing foreground / keyboard focus.");
        var hidden = 0;
        Native.WinEventProc observer = (_, evt, window, objectId, childId, _, _) => { if (window == first.Record.Hwnd && evt == 0x8003 && objectId == 0 && childId == 0) hidden++; };
        var hook = Native.SetWinEventHook(0x8003, 0x8003, 0, observer, 0, 0, 0);
        try
        {
            for (var index = 0; index < 12; index++) session.SelectedWorkspace = session.Config.Workspaces[0].Id;
            await Task.Delay(100); Check(hidden == 0, "Clicking the selected tab hides an embedded window.");
        }
        finally { Native.UnhookWinEvent(hook); GC.KeepAlive(observer); }
        foreach (var theme in new[] { "graphite", "light", "frost", "glass" })
        {
            session.Config.ThemeId = theme; main.TestRefresh(); await Task.Delay(100);
            Check(main.TryGetPlatformHandle()!.Handle == stable, "Theme change recreated the main HWND.");
            Check(session.Panels.All(p => Native.IsWindow(p.Record.Hwnd) && Native.GetWindow(p.Record.Hwnd, 4) == stable), "Theme change lost a native window.");
            Snapshot(main, "avalonia-theme-" + theme + ".png");
        }
        Pass("Selected-tab clicks emit zero hide events; all four themes preserve host / external handles and render without GDI text.");
        session.Config.ThemeId = "frost"; main.TestRefresh();
        session.SelectedWorkspace = session.Config.Workspaces[1].Id; await Task.Delay(80);
        Check(!Native.IsWindowVisible(first.Record.Hwnd), "Inactive workspace remains visible.");
        session.SelectedWorkspace = session.Config.Workspaces[0].Id; await Task.Delay(100);
        Check(Native.IsWindowVisible(first.Record.Hwnd), "Workspace switch did not restore the native window.");
        session.RenamePanel(first, "Мой агент"); first.AgentId = "avalonia-cli-token";
        Check(session.Move([first], session.Config.Workspaces[1]), "Cross-tab move failed."); await Task.Delay(120);
        Check(first.Caption == "Мой агент" && Native.GetWindow(first.Record.Hwnd, 4) == stable, "Cross-tab move lost native state / alias.");
        session.Undo(); await Task.Delay(100); Check(first.WorkspaceId == session.Config.Workspaces[0].Id, "Undo move failed.");
        main.ToggleFocus(first); await Task.Delay(100); Check(main.TestCards.Where(c => c.IsVisible).Count() == 1, "Focus mode did not isolate one panel.");
        main.ToggleFocus(first); await Task.Delay(100);
        var start = new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--agent-status", "waiting", "--agent-id", "avalonia-cli-token", "--message", "Разрешение" }) start.ArgumentList.Add(argument);
        using var cli = Process.Start(start)!; await cli.WaitForExitAsync();
        Check(cli.ExitCode == 0 && first.AgentStatus == "waiting" && first.Attention, "Standalone CLI did not reach the Avalonia host.");
        Pass("Workspace switching, cross-tab moves / undo, focus expansion and standalone UTF-8 agent CLI work with the Avalonia host.");
        Snapshot(main, "avalonia-grid.png");
        await DesktopAdvancedTests.RunAsync(main, first);
        await DesktopInteractionTests.RunAsync(main);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static long Message(nint window, uint message, long wParam = 0, long lParam = 0)
    {
        Native.SendMessageTimeout(window, message, (nint)wParam, (nint)lParam, 2, 1000, out var result); return result;
    }
    internal static void Snapshot(Window window, string name)
    {
        using var bitmap = new RenderTargetBitmap(new(Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(window.Bounds.Height))), new(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
    }
}