namespace WindowDock;

internal sealed partial class WorkspaceSession
{
    internal IEnumerable<RuntimePanel> PanelsIn(WorkspaceConfig workspace) =>
        Panels.Where(panel => panel.WorkspaceId == workspace.Id).OrderBy(panel => panel.LayoutSlot);

    private IEnumerable<int> FreeLayoutSlots(WorkspaceConfig workspace, RuntimePanel? exclude = null)
    {
        var occupied = Panels.Where(panel => panel.WorkspaceId == workspace.Id && panel != exclude)
            .Select(panel => panel.LayoutSlot).ToHashSet();
        return Enumerable.Range(0, WorkspaceConfig.MaxCapacity).Where(slot => !occupied.Contains(slot));
    }

    private int ChooseLayoutSlot(WorkspaceConfig workspace, SavedPanel? saved, RuntimePanel? exclude = null)
    {
        var free = FreeLayoutSlots(workspace, exclude).ToArray();
        if (free.Length == 0) throw new InvalidOperationException(L10n.Get("WorkspaceSession.ThisTabHasNoFreeSlots"));
        if (saved?.WorkspaceId == workspace.Id && free.Contains(saved.Order)) return saved.Order;

        // Preserve saved positions during attachment when other slots are available.
        var reserved = Pending.Where(panel => panel.WorkspaceId == workspace.Id && panel != saved)
            .Select(panel => panel.Order).ToHashSet();
        return free.FirstOrDefault(slot => !reserved.Contains(slot), free[0]);
    }

    private static void AssignLayoutSlot(RuntimePanel panel, WorkspaceConfig workspace, int slot)
    {
        panel.LayoutSlot = slot;
        workspace.LayoutSlotCount = Math.Max(workspace.LayoutSlotCount, slot + 1);
    }

    private void ClearLayoutIfEmpty(string workspaceId)
    {
        if (!Panels.Any(panel => panel.WorkspaceId == workspaceId) && !Pending.Any(panel => panel.WorkspaceId == workspaceId) &&
            Config.Workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is { } tab)
            tab.LayoutSlotCount = 0;
    }

    internal void CompactLayout(WorkspaceConfig workspace)
    {
        var slot = 0;
        foreach (var panel in PanelsIn(workspace).ToArray()) panel.LayoutSlot = slot++;
        workspace.LayoutSlotCount = slot;
    }

    internal bool MovePanelToSlot(RuntimePanel panel, int slot)
    {
        var workspace = Current;
        if (panel.WorkspaceId != workspace.Id || !Panels.Contains(panel) || slot < 0 || slot >= workspace.LayoutSlotCount ||
            !FreeLayoutSlots(workspace).Contains(slot)) return false;
        Checkpoint(L10n.Get("WorkspaceSession.PanelOrder"));
        AssignLayoutSlot(panel, workspace, slot);
        RaiseStructureChanged();
        RequestSave();
        return true;
    }
}
