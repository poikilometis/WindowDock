using Avalonia.Controls;
using Avalonia.Threading;

namespace WindowDock;

internal sealed partial class MainWindow
{
    private readonly HashSet<string> historyCaptures = [];
    private readonly NotificationHighlight notificationHighlight = new();
    private long nextHistoryCapture = Environment.TickCount64 + 180_000;
    private bool savingHistories;
    private readonly CancellationTokenSource recoveryLifetime = new();
    private bool restoringWork;

    internal async Task RestoreWorkAsync(bool startup, Action<string>? report = null)
    {
        if (closing || !OperatingSystem.IsWindows() || !Session.Backend.Available) return;
        void Report(string message) { if (report is null) SetStatus(message); else report(message); }
        if (restoringWork)
        {
            Report(L10n.Get("Recovery.RestoringWindows"));
            return;
        }
        restoringWork = true;
        var cancellation = recoveryLifetime.Token;
        var requested = Session.Pending.Select(saved => saved.Id).ToHashSet();
        try
        {
            if (!startup) Report(L10n.Get("Recovery.RestoringWindows"));
            if (!startup || Session.Config.RestoreGroups) Session.Poll(forceRestore: true);
            string? problem = null;
            var restoreGroups = !startup || Session.Config.RestoreGroups;
            var needsDefaultTerminal = Session.Pending.Any(saved => saved.ClassName == Native.TerminalWindowClass &&
                Session.ShortcutFor(saved) is null && !Session.HasProjectCommand(saved));
            if (Session.Config.ReopenTerminal &&
                (restoreGroups && needsDefaultTerminal || startup && Session.Config.RecoveryShortcuts.Count == 0) &&
                !Session.Panels.Any(panel => panel.Record.ClassName == Native.TerminalWindowClass) &&
                !Session.DesktopWindows().Any(window => window.ClassName == Native.TerminalWindowClass))
            {
                try
                {
                    WindowsTerminalLayout.Reopen();
                    var deadline = Environment.TickCount64 + 4_000;
                    while (Environment.TickCount64 < deadline && !Session.DesktopWindows().Any(window => window.ClassName == Native.TerminalWindowClass))
                        await Task.Delay(250, cancellation);
                    if (restoreGroups) Session.Poll(forceRestore: true);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { problem = exception.Message; }
            }

            foreach (var profile in Session.Config.Projects.ToArray())
            {
                if (startup && !profile.RestoreOnStartup) continue;
                var commands = Session.Pending.Where(saved => saved.ProjectId == profile.Id && !string.IsNullOrEmpty(saved.CommandId))
                    .Select(saved => saved.CommandId!).ToHashSet();
                if (!startup && commands.Count == 0) continue;
                var result = Session.OpenProject(profile, startMissing: true, useResumeArguments: true, selectWorkspace: false,
                    commands: startup ? null : commands);
                if (result.FirstOrDefault(role => role.State == ProjectRoleState.Failed || role.Error.Length > 0) is { } failed)
                    problem ??= L10n.Format("Recovery.ProjectFailed", profile.Name, failed.Error);
            }
            if (restoreGroups && Session.Config.RecoveryShortcuts.Count > 0)
            {
                var shortcutProblem = await Session.RecoverShortcutsAsync(startup,
                    path => Task.Run(() => OperatingSystem.IsWindows()
                        ? WindowsTerminalLayout.OpenShortcut(path) : throw new PlatformNotSupportedException(), cancellation), cancellation);
                problem ??= shortcutProblem;
            }
            if (!startup)
                while (Session.IsLaunchingSavedRole(requested))
                {
                    await Task.Delay(250, cancellation);
                    Session.Poll(forceRestore: true);
                }
            var restored = Session.Panels.Count(panel => requested.Contains(panel.Id));
            var remaining = Session.Pending.Count;
            if (!startup || restored > 0 || problem is not null)
            {
                var summary = L10n.Format("Recovery.RestoreSummary", restored, remaining);
                if (problem is not null) summary += " " + problem;
                else if (!startup && remaining > 0) summary += " " + L10n.Get("Recovery.RestorePendingHelp");
                Report(summary);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { restoringWork = false; }
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

    private async Task ActivateAgentAsync(RuntimePanel panel, string key, bool fromNotification = false)
    {
        ActivatePanel(panel);
        if (fromNotification)
        {
            var card = Session.SelectedWorkspace == panel.WorkspaceId ? cards.GetValueOrDefault(panel.Id) : null;
            if (workspaceWindows.TryGetValue(panel.WorkspaceId, out var presented))
                card = OperatingSystem.IsWindows() && VirtualDesktops.OnCurrent(presented.Handle) ? presented.CardFor(panel) : null;
            if (card is { IsEffectivelyVisible: true }) card.ShowNotificationHighlight(notificationHighlight);
            else notificationHighlight.Stop();
        }
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
