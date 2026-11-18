using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void StablePanelSlots()
    {
        using var session = Session(new FakeBackend(), 6);
        var source = session.Current;
        var incoming = session.Attach(Candidate(5900, "Incoming"));
        var otherIncoming = session.Attach(Candidate(5901, "Second incoming"));
        var target = session.NewWorkspace("Six slots");
        target.Capacity = 6;
        var grid = Enumerable.Range(0, 6).Select(index => session.Attach(Candidate(5910 + index), target)).ToArray();

        session.SwapPanels(grid[4], grid[1]);
        Check(session.PanelsIn(target).SequenceEqual(new[] { grid[0], grid[4], grid[2], grid[3], grid[1], grid[5] }),
            "Moving bottom middle onto top middle shifted another pane instead of swapping the two slots.");
        session.Undo();
        Check(session.PanelsIn(target = session.Config.Workspaces.Single(tab => tab.Id == target.Id)).SequenceEqual(grid),
            "Undo did not restore the original pane slots.");

        source = session.Config.Workspaces.Single(tab => tab.Id == source.Id);
        Check(session.Move([grid[1], grid[4]], source) && target.LayoutSlotCount == 6 &&
            session.PanelsIn(target).Select(panel => panel.LayoutSlot).SequenceEqual(new[] { 0, 2, 3, 5 }),
            "Moving windows out of a tab compacted its remaining positions.");
        Check(session.Move([incoming, otherIncoming], target) && incoming.LayoutSlot == 1 && otherIncoming.LayoutSlot == 4,
            "Multiple incoming windows did not fill the first two exact vacancies.");
        Check(session.Move([grid[5]], source) && target.LayoutSlotCount == 6,
            "Moving out the final panel reduced the allocated grid.");
        session.SelectedWorkspace = target.Id;
        Check(session.MovePanelToSlot(incoming, 5) && incoming.LayoutSlot == 5,
            "The last empty slot disappeared before it could be used.");

        session.Save();
        var saved = Store.Read<GroupState>(GroupPersistence.PathName)!.Panels.Where(panel => panel.WorkspaceId == target.Id)
            .OrderBy(panel => panel.Order).ToArray();
        using var reopened = new WorkspaceSession(Store.Read<AppConfig>(Store.ConfigPath)!, new FakeBackend());
        var restoredTab = reopened.Config.Workspaces.Single(tab => tab.Id == target.Id);
        foreach (var panel in saved.Reverse()) reopened.Attach(Candidate((int)panel.Handle, panel.Title), restoredTab, panel);
        Check(restoredTab.LayoutSlotCount == 6 && reopened.PanelsIn(restoredTab).Select(panel => panel.LayoutSlot)
            .SequenceEqual(new[] { 0, 2, 3, 4, 5 }), "Restart compacted vacancies or assigned saved panels in arrival order.");
        report.Add("PASS windows swap in place, fill first empty positions across tabs, and retain gaps through undo and restart.");
    }
}
