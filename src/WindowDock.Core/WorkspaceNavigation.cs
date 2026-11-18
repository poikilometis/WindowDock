namespace WindowDock;

internal sealed partial class WorkspaceSession
{
    internal IReadOnlyList<WindowCandidate> DesktopWindows(bool cached = false)
    {
        var windows = cached ? Backend.Discover() : Backend.Enumerate();
        if (HookSessions.Count == 0) return windows;
        var signals = HookSessions.Values.Select(entry => entry.Signal).Where(signal => !string.IsNullOrWhiteSpace(signal.Directory)).ToArray();
        var byConsole = signals.Where(signal => signal.ConsoleHandle != 0).ToLookup(signal => signal.ConsoleHandle);
        var reuse = windows.ToLookup(window => (window.ProcessId, window.ProcessStartTicks));
        var byProcess = signals.SelectMany(signal => (signal.ProcessChain ?? []).Where(process => process.StartTicks != 0)
            .Select(process => (Process: (process.Id, process.StartTicks), Signal: signal))).ToLookup(entry => entry.Process, entry => entry.Signal);
        foreach (var window in windows)
        {
            var identity = (window.ProcessId, window.ProcessStartTicks);
            var matches = byConsole[window.Handle.ToInt64()].Concat(reuse[identity].Take(2).Count() == 1 ? byProcess[identity] : []);
            var directories = matches.Select(signal => signal.Directory).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            window.ProjectDirectory = directories.Length == 1 ? directories[0] : null;
        }
        return windows;
    }

    internal static bool MatchesQuery(string text, string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .All(term => text.Contains(term, StringComparison.CurrentCultureIgnoreCase));

    internal RuntimePanel AttachFromSearch(WindowCandidate window, bool link)
    {
        using var update = BeginUpdate();
        var before = Config.Workspaces.Select(workspace => workspace.Id).ToHashSet();
        var target = Destination(Current, true)!;
        try { return Attach(window, target, link: link); }
        catch { RemoveFailedDestination(target, before); throw; }
    }
    internal RuntimePanel? ActivePanel(WorkspaceConfig workspace)
    {
        var panels = Panels.Where(panel => panel.WorkspaceId == workspace.Id);
        return panels.FirstOrDefault(panel => panel.Id == workspace.ActivePanelId)
            ?? panels.OrderByDescending(panel => panel.ActivatedUtc).FirstOrDefault();
    }

    internal RuntimePanel? StackPanel()
    {
        return CurrentPanels.FirstOrDefault(panel => panel.Id == Current.StackPanelId) ?? ActivePanel(Current);
    }

    internal void SelectStackPanel(RuntimePanel panel)
    {
        if (panel.WorkspaceId != SelectedWorkspace || Current.StackPanelId == panel.Id) return;
        Current.StackPanelId = panel.Id;
        RequestSave();
    }

    internal void RememberActivation(RuntimePanel panel)
    {
        if (!Panels.Contains(panel) || Config.Workspaces.FirstOrDefault(tab => tab.Id == panel.WorkspaceId) is not { } workspace) return;
        if (workspace.ActivePanelId == panel.Id && panel.ActivatedUtc != 0 &&
            !Panels.Any(other => other.ActivatedUtc > panel.ActivatedUtc)) return;

        workspace.ActivePanelId = panel.Id;
        panel.ActivatedUtc = DateTime.UtcNow.Ticks;
        RequestSave();
    }

    internal RuntimePanel? PreviousPanel()
    {
        var active = ActivePanel(Current)?.Id;
        return Panels.Where(panel => panel.Id != active && panel.ActivatedUtc > 0).MaxBy(panel => panel.ActivatedUtc);
    }

    internal void TogglePinnedPanel(RuntimePanel panel)
    {
        var workspace = Config.Workspaces.FirstOrDefault(w => w.Id == panel.WorkspaceId);
        if (workspace is null || !Panels.Contains(panel)) return;
        Checkpoint(L10n.Get("Workflow.PinPanel"));
        workspace.PinnedPanelId = workspace.PinnedPanelId == panel.Id ? null : panel.Id;
        LayoutChanged();
    }

    public IReadOnlyList<RuntimePanel> Search(string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var exact = query.Trim();
        return Panels
            .Where(panel =>
            {
                if (terms.Length == 0) return true;
                var label = SearchText(panel);
                return terms.All(term => label.Contains(term, StringComparison.CurrentCultureIgnoreCase));
            })
            .OrderByDescending(panel => exact.Length > 0 && panel.Caption.Equals(exact, StringComparison.CurrentCultureIgnoreCase))
            .ThenByDescending(panel => panel.ActivatedUtc)
            .ToArray();
    }

    private string SearchText(RuntimePanel panel)
    {
        var workspace = Config.Workspaces.FirstOrDefault(workspace => workspace.Id == panel.WorkspaceId);
        var project = Config.Projects.FirstOrDefault(project => project.Id == panel.ProjectId);
        var command = project?.Commands.FirstOrDefault(command => command.Id == panel.CommandId);
        return string.Join(" ", panel.Caption, panel.Title, panel.Record.ProcessName, panel.Record.ExecutablePath,
            panel.Record.ProjectDirectory, workspace?.Name, project?.Name, command?.Directory, panel.StatusText,
            string.Join(" ", panel.AgentSessions.Select(item => item.Directory)));
    }
}
