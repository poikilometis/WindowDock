using System.Drawing;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void Workflows()
    {
        StablePanelSlots();
        SavedWorkspaceFeatures();
        var backend = new FakeBackend();
        using var session = Session(backend, 2);
        var original = session.Current;
        var work = session.NewWorkspace("Project Beta");
        work.Capacity = 2;
        session.SelectedWorkspace = work.Id;
        var hidden = Candidate(3301, "Beta review");
        hidden.IsMinimized = true;
        var excluded = Candidate(3302, "Beta private");
        session.Config.Rules.Add(new WindowRule { Id = "exclude", Name = "Private", Enabled = true, Exclude = true, TitlePattern = "private" });
        var windows = new[] { Candidate(3300, "Beta build"), hidden, excluded, Candidate(3303, "Beta tests"), Candidate(3304, "Alpha project") };
        var options = new CaptureOptions(IncludeMinimized: true, UseRules: false, Query: "Beta");
        var preview = session.PreviewCapture(windows, options);
        Check(preview.Count(result => result.State == CaptureState.Ready) == 3 &&
              preview.Single(result => result.Window == excluded).State == CaptureState.Excluded &&
              preview.Count(result => result.Tab == work.Name) == 2 &&
              session.Panels.Count == 0 && session.Config.Workspaces.Count == 2, "Capture preview moves windows or ignores filter, exclusion or target.");
        Check(session.Collect(windows, options) == 3 && session.CurrentPanels.Count() == 2 &&
              session.Panels.All(panel => panel.WorkspaceId != original.Id), "Filtered capture fills an unrelated workspace or loses a minimized window.");
        var overflow = session.Config.Workspaces.Single(workspace => workspace.OverflowFor == work.Id);
        Check(session.LastCapture.Single(result => result.Window.Handle == 3303).Tab == overflow.Name, "Capture report disagrees with overflow handling.");

        var roleWindow = Candidate(3305, "Beta API");
        var otherSimilar = Candidate(3306, "Beta API auxiliary");
        backend.Candidates.AddRange([roleWindow, otherSimilar]);
        var project = ProjectProfile.Create(original.Id);
        project.Name = "Beta";
        var command = ProjectCommand.Create();
        command.Executable = "fixture-must-not-launch";
        command.ReuseProcess = "terminal.exe";
        command.ReuseTitle = "Beta API";
        project.Commands.Add(command);
        session.Config.Projects.Add(project);
        Check(session.OpenProject(project, true, false).Single().State == ProjectRoleState.Ambiguous,
            "Ambiguous candidates started a duplicate command.");
        command.ReuseTitle = "Beta AP?";
        var attached = session.OpenProject(project, true, false).Single();
        var again = session.OpenProject(project, true, true).Single();
        Check(attached.State == ProjectRoleState.Open && attached.Panel is not null &&
              ReferenceEquals(attached.Panel, again.Panel) && attached.Panel.Record.Handle == roleWindow.Handle &&
              session.Panels.Count == 4, "Opening a project a second time creates or relaunches its existing window.");
        var panel = attached.Panel!;
        var legacy = session.CurrentPanels.FirstOrDefault(item => item != panel) ?? session.CurrentPanels.First();
        var originalProjectId = legacy.ProjectId;
        var originalCommandId = legacy.CommandId;
        legacy.ProjectId = legacy.CommandId = "";
        legacy.AgentId = "old-launch-token";
        legacy.Alias = command.Name;
        command.ReuseProcess = command.ReuseTitle = "";
        Check(session.OpenProject(project, true, false).Single().State == ProjectRoleState.Ambiguous,
            "Migrated project roles with a live legacy launch token were started again.");
        legacy.ProjectId = originalProjectId;
        legacy.CommandId = originalCommandId;
        session.RememberActivation(panel);
        session.SelectedWorkspace = work.Id;
        var review = session.CurrentPanels.Single(item => item.Record.Handle == hidden.Handle);
        session.RememberActivation(review);
        Check(session.PreviousPanel() == panel && session.ActivePanel(original) == panel &&
              session.Search("Beta review").First() == review, "Project activation or recent keyboard navigation lost its destination.");

        var statusTransitions = 0;
        session.AgentStatusTransition += item => { if (item.NeedsAttention) statusTransitions++; };
        AgentSignal Signal(string key, string status, long time) => new()
        {
            Handle = panel.Record.Handle, SessionKey = key, Status = status, EventUtcTicks = time
        };
        session.ReceiveAgentSignal(Signal("claude:a", "waiting", 12));
        session.ReceiveAgentSignal(Signal("claude:a", "waiting", 13));
        session.ReceiveAgentSignal(Signal("codex:b", "done", 16));
        session.ReceiveAgentSignal(Signal("claude:a", "done", 11));
        Check(statusTransitions == 1 && session.AgentActivities().Single(item => item.NeedsAttention).Panel == panel,
            "Duplicate or out-of-order status events lost an unresolved prompt or generated repeated alerts.");
        session.ReceiveAgentSignal(Signal("claude:a", "working", 14));
        Check(session.AgentActivities().All(item => !item.NeedsAttention), "A new state for the same session did not clear its pending prompt.");
        var screen = new Rectangle(-1920, 0, 1920, 1040);
        var bounds = new DisplayPlacement { Left = -140, Top = 2000, Width = 900, Height = 720 }.Restore(screen, 1.25);
        Check(screen.Contains(bounds) && bounds.Width == 1125 && bounds.Height == 900 && bounds.Left == screen.Left,
            "Display restoration ignores DPI, negative coordinates or visible work area.");
        Check(DockShortcut.TryParse("Ctrl+Alt+Space", out var shortcut) &&
              DockShortcut.TryParse("alt+ctrl+space", out var reordered) && shortcut == reordered &&
              !DockShortcut.TryParse("Ctrl+Ctrl+A", out _) && !DockShortcut.TryParse("Space", out _),
            "Shortcut validation accepts conflicts or unmodified typing keys.");

        using var restoration = Session(new FakeBackend());
        var saved = Enumerable.Range(0, 3).Select(index => new SavedPanel
        {
            Id = "role" + index, WorkspaceId = restoration.Current.Id, Order = index,
            ProcessName = "terminal", ClassName = "fixture", LastSeenUtc = DateTime.UtcNow.Ticks
        }).ToArray();
        restoration.Pending.AddRange(saved);
        for (var index = 2; index >= 0; index--) restoration.Attach(Candidate(3410 + index), saved: saved[index]);
        Check(restoration.CurrentPanels.Select(item => item.Id).SequenceEqual(saved.Select(item => item.Id)),
            "Windows starting in reverse order lost their saved layout positions.");
        report.Add("PASS filtered capture planning and exclusions, launch reuse and ambiguity, recent navigation, independent attention, monitor geometry and global shortcut validation.");
    }
}
