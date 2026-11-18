using Avalonia.Controls;
using Avalonia.Threading;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly HashSet<string> historyCaptures = [];
    private long nextHistoryCapture = Environment.TickCount64 + 180_000;
    private bool savingHistories;

    private async Task RestoreWorkAsync()
    {
        if (closing || !OperatingSystem.IsWindows()) return;
        if (Session.Config.ReopenTerminal &&
            !Session.Panels.Any(panel => panel.Record.ClassName == Native.TerminalWindowClass) &&
            !Session.DesktopWindows().Any(window => window.ClassName == Native.TerminalWindowClass))
        {
            UserAction(WindowsTerminalLayout.Reopen);
            // Give the native layout a chance to restore before resolving project roles.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (!closing && !deadline.IsCancellationRequested &&
                !Session.Panels.Any(panel => panel.Record.ClassName == Native.TerminalWindowClass) &&
                !Session.DesktopWindows(cached: true).Any(window => window.ClassName == Native.TerminalWindowClass))
                await Task.Delay(250);
        }
        if (closing) return;
        foreach (var profile in Session.Config.Projects.Where(project => project.RestoreOnStartup).ToArray())
        {
            var result = Session.OpenProject(profile, startMissing: true, useResumeArguments: true, selectWorkspace: false);
            if (result.FirstOrDefault(role => role.State == ProjectRoleState.Failed) is { } failed)
                SetStatus(L10n.Format("Recovery.ProjectFailed", profile.Name, failed.Error));
        }
    }

    internal async Task EditTerminalTargetAsync(RuntimePanel panel, string key, EditorWindow owner)
    {
        if (!OperatingSystem.IsWindows() || panel.Record.ClassName != Native.TerminalWindowClass) return;
        var tabs = await TerminalAccess.TabsAsync(panel.Record);
        var current = panel.TerminalTargets.FirstOrDefault(target => target.Key == key);
        var dialog = new TerminalTargetWindow(key, tabs, current);
        if (await ShowEditorAsync(dialog, owner) && dialog.Result is { } target)
            Session.SaveTerminalTarget(panel, target);
    }

    private async Task ActivateAgentAsync(RuntimePanel panel, string key)
    {
        ActivatePanel(panel);
        if (!OperatingSystem.IsWindows() || panel.TerminalTargets.FirstOrDefault(target => target.Key == key) is not { } target) return;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (closing || !Session.Panels.Contains(panel) ||
            (workspaceWindows.TryGetValue(panel.WorkspaceId, out var view)
                ? !VirtualDesktops.OnCurrent(view.Handle) : Session.SelectedWorkspace != panel.WorkspaceId)) return;
        await TerminalAccess.FocusAsync(panel.Record, target);
    }

    private async Task SaveTerminalHistoryAsync(RuntimePanel panel)
    {
        if (!OperatingSystem.IsWindows() || !TerminalAccess.IsTerminal(panel.Record)) return;
        var id = panel.Id;
        if (!historyCaptures.Add(id)) return;
        try
        {
            await TerminalAccess.SaveHistoryAsync(panel);
            SetStatus(L10n.Get("Recovery.HistorySaved"));
        }
        finally { historyCaptures.Remove(id); }
    }

    private void SetAutomaticHistory(RuntimePanel panel)
    {
        panel.KeepTerminalHistory = !panel.KeepTerminalHistory;
        Session.Save();
        if (panel.KeepTerminalHistory) _ = UserActionAsync(() => SaveTerminalHistoryAsync(panel));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void PollTerminalHistory()
    {
        if (savingHistories || Environment.TickCount64 < nextHistoryCapture) return;
        nextHistoryCapture = Environment.TickCount64 + 180_000;
        var terminals = Session.Panels.Where(panel => panel.KeepTerminalHistory && TerminalAccess.IsTerminal(panel.Record)).ToArray();
        if (terminals.Length > 0) _ = SaveHistoryBatchAsync(terminals);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task SaveHistoryBatchAsync(IEnumerable<RuntimePanel> terminals)
    {
        savingHistories = true;
        try
        {
            foreach (var panel in terminals)
            {
                if (closing) break;
                var id = panel.Id;
                if (!panel.KeepTerminalHistory || !Session.Panels.Contains(panel) || !historyCaptures.Add(id)) continue;
                try { await TerminalAccess.SaveHistoryAsync(panel); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                { Store.Log("Terminal history could not be updated for a saved panel."); }
                finally { historyCaptures.Remove(id); }
            }
        }
        finally { savingHistories = false; }
    }

    internal async Task ViewTerminalHistoryAsync(string id, string caption, EditorWindow? owner = null)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(TerminalAccess.HistoryPath(id))) return;
        var path = TerminalAccess.HistoryPath(id);
        if (new FileInfo(path).Length > 960000) throw new IOException(L10n.Get("Recovery.NoTerminalText"));
        var text = await File.ReadAllTextAsync(path);
        await ShowEditorAsync(new TerminalHistoryWindow(caption, text), owner);
    }
}
