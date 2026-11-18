namespace WindowDock;

internal sealed partial class WorkspaceSession
{
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
        if (panel.WorkspaceId != SelectedWorkspace || !Panels.Contains(panel)) return;
        if (Current.ActivePanelId == panel.Id && panel.ActivatedUtc != 0 &&
            !Panels.Any(other => other.ActivatedUtc > panel.ActivatedUtc)) return;

        Current.ActivePanelId = panel.Id;
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
        return string.Join(" ", panel.Caption, panel.Title, panel.Record.ProcessName, workspace?.Name, project?.Name, command?.Directory, panel.StatusText);
    }
}
