using System.ComponentModel;

namespace WindowDock;

internal readonly record struct ShortcutProcess(int Id, long StartTicks);

internal sealed partial class WorkspaceSession
{
    private ShortcutAttempt? shortcutAttempt;
    private bool recoveringShortcuts;

    internal RecoveryShortcut? ShortcutFor(SavedPanel saved) =>
        Config.RecoveryShortcuts.FirstOrDefault(shortcut => shortcut.Id == saved.RecoveryShortcutId) ??
        Config.RecoveryShortcuts.FirstOrDefault(shortcut => shortcut.Matches(saved.ProcessName, saved.ClassName, saved.ExecutablePath));

    internal void SetRecoveryShortcut(SavedPanel saved, string path, string? title, bool reopen)
    {
        var existing = ShortcutFor(saved);
        if (path.Length == 0)
        {
            if (existing is not null) Config.RecoveryShortcuts.Remove(existing);
        }
        else
        {
            if (!RecoveryShortcut.IsShortcutPath(path) || !File.Exists(path))
                throw new FileNotFoundException(L10n.Get("Recovery.ShortcutUnavailable"));
            var shortcut = existing ?? new RecoveryShortcut
            {
                Id = Guid.NewGuid().ToString("N"), ProcessName = saved.ProcessName,
                ClassName = saved.ClassName, ExecutablePath = saved.ExecutablePath
            };
            shortcut.Path = path;
            shortcut.LaunchTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            shortcut.ReopenOnStartup = reopen;
            if (existing is null) Config.RecoveryShortcuts.Add(shortcut);
        }
        Save();
    }

    internal bool HasProjectCommand(SavedPanel saved) => Config.Projects.Any(project => project.Id == saved.ProjectId &&
        project.Commands.Any(command => command.Id == saved.CommandId && !string.IsNullOrWhiteSpace(command.Executable)));

    private WorkspaceConfig? SavedDestination(SavedPanel saved) =>
        Config.Workspaces.FirstOrDefault(workspace => workspace.Id == saved.WorkspaceId &&
            Panels.Count(panel => panel.WorkspaceId == workspace.Id) < workspace.Capacity);

    internal void AttachSavedWindow(SavedPanel saved, WindowCandidate candidate)
    {
        if (!Pending.Contains(saved)) return;
        var workspace = SavedDestination(saved) ?? throw new InvalidOperationException(L10n.Get("WorkspaceSession.TheSavedPanelSTabIsFull"));
        Attach(candidate, workspace, saved);
    }

    internal async Task<string?> RecoverShortcutsAsync(bool startup, Func<string, Task<ShortcutProcess>> start,
        CancellationToken cancellation, TimeSpan? windowTimeout = null)
    {
        if (recoveringShortcuts || !Backend.Available) return null;
        recoveringShortcuts = true;
        string? problem = null;
        HashSet<string> stalled = [];
        try
        {
            Poll(forceRestore: true);
            var roles = Config.Workspaces.SelectMany(workspace => Pending
                .Where(saved => saved.WorkspaceId == workspace.Id).OrderBy(saved => saved.Order)).ToArray();
            foreach (var saved in roles)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Pending.Contains(saved) || SavedDestination(saved) is null || HasProjectCommand(saved) ||
                    ShortcutFor(saved) is not { } shortcut || (startup && !shortcut.ReopenOnStartup) || stalled.Contains(shortcut.Id)) continue;

                var desktop = DesktopWindows().Where(candidate => !Panels.Any(panel => panel.Record.Handle == candidate.Handle)).ToArray();
                if (desktop.Any(shortcut.MatchesOutput))
                {
                    // An unmatched live window could already be the requested role.
                    problem ??= L10n.Get("Recovery.AmbiguousShortcutWindow");
                    stalled.Add(shortcut.Id);
                    continue;
                }

                var attempt = new ShortcutAttempt(saved, shortcut, desktop.Select(WindowMatching.Identity).ToHashSet());
                shortcutAttempt = attempt;
                try
                {
                    attempt.Process = await start(shortcut.Path);
                    cancellation.ThrowIfCancellationRequested();
                    attempt.Started = true;
                    var deadline = Environment.TickCount64 + (long)(windowTimeout ?? TimeSpan.FromSeconds(20)).TotalMilliseconds;
                    do
                    {
                        Poll(forceRestore: true);
                        if (!Pending.Contains(saved) || SavedDestination(saved) is null) break;
                        await Task.Delay(200, cancellation);
                    } while (Environment.TickCount64 < deadline);
                    if (Pending.Contains(saved))
                    {
                        problem ??= attempt.Error ?? L10n.Format("Recovery.ShortcutDidNotOpen", Path.GetFileName(shortcut.Path));
                        stalled.Add(shortcut.Id);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                {
                    problem ??= L10n.Format("Recovery.ShortcutFailed", Path.GetFileName(shortcut.Path), exception.Message);
                    stalled.Add(shortcut.Id);
                }
                finally { shortcutAttempt = null; }
            }
        }
        finally { recoveringShortcuts = false; }
        return problem;
    }

    private bool RestoreShortcutWindow(IReadOnlyList<WindowCandidate> candidates)
    {
        if (shortcutAttempt is not { Started: true } attempt || !Pending.Contains(attempt.Saved) ||
            SavedDestination(attempt.Saved) is not { } workspace) return false;
        var outputs = candidates.Where(candidate => attempt.Reserves(candidate) &&
            !Panels.Any(panel => panel.Record.Handle == candidate.Handle)).ToArray();
        if (outputs.Length > 1 && attempt.Process.StartTicks != 0)
        {
            var process = attempt.Process;
            var descendants = outputs.Where(window => (window.ProcessId == process.Id && window.ProcessStartTicks == process.StartTicks) ||
                Backend.ProcessChain(window.ProcessId).Any(parent => parent.Id == process.Id && parent.StartTicks == process.StartTicks)).ToArray();
            if (descendants.Length > 0) outputs = descendants;
        }
        if (outputs.Length != 1)
        {
            if (outputs.Length > 1) attempt.Error = L10n.Get("Recovery.AmbiguousShortcutWindow");
            return false;
        }
        try
        {
            var panel = Attach(outputs[0], workspace, attempt.Saved, checkpoint: false);
            panel.RecoveryShortcutId = attempt.Shortcut.Id;
            shortcutAttempt = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            attempt.Error = exception.Message;
            return false;
        }
    }

    private bool RestoreRunningWindows(IReadOnlyList<WindowCandidate> candidates)
    {
        var roles = Pending.Where(saved => shortcutAttempt?.Saved != saved &&
            !launches.Any(launch => launch.SavedId == saved.Id) && SavedDestination(saved) is not null).OrderBy(saved => saved.Order).ToArray();
        var matches = WindowMatching.RestoreMatches(roles, candidates.Where(candidate =>
            !Panels.Any(panel => panel.Record.Handle == candidate.Handle)).ToArray());
        var changed = false;
        foreach (var (saved, candidate) in matches)
        {
            if (SavedDestination(saved) is not { } workspace) continue;
            try { Attach(candidate, workspace, saved, checkpoint: false); changed = true; }
            catch (Exception exception) { Store.Log(exception.Message); }
        }
        return changed;
    }

    private sealed class ShortcutAttempt(SavedPanel saved, RecoveryShortcut shortcut, HashSet<string> previous)
    {
        internal SavedPanel Saved { get; } = saved;
        internal RecoveryShortcut Shortcut { get; } = shortcut;
        internal bool Started { get; set; }
        internal ShortcutProcess Process { get; set; }
        internal string? Error { get; set; }
        internal bool Reserves(WindowCandidate candidate) => Shortcut.MatchesOutput(candidate) && !previous.Contains(WindowMatching.Identity(candidate));
    }
}
