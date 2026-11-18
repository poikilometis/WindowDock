using System.Drawing;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void SavedWorkspaceFeatures()
    {
        var backend = new FakeBackend();
        using var session = Session(backend, 6);
        var first = session.Attach(Candidate(4100, "Same terminal"));
        var second = session.Attach(Candidate(4101, "Same terminal"));
        var workspace = session.Current;
        Check(session.SplitPanel(first, true) && workspace.Layout == "custom", "A tile did not split into the custom layout.");
        var empty = workspace.CustomLayout.Leaves().Single(area => area.PanelId is null);
        session.MovePanelToArea(second, empty);
        Check(session.SplitPanel(second, false), "A nested split could not be made.");
        var plan = CustomLayout.Plan(workspace.CustomLayout, new Size(800, 600), 320, 220, 4);
        var rectangles = plan.Slots.Values.ToArray();
        Check(rectangles.Length == 4 && rectangles.All(rectangle => rectangle.Width >= 320 && rectangle.Height >= 220) &&
            rectangles.SelectMany((rectangle, i) => rectangles.Skip(i + 1).Select(other => (rectangle, other)))
                .All(pair => !pair.rectangle.IntersectsWith(pair.other)), "Nested split geometry overlaps or loses minimum pane size.");
        var rootDivider = plan.Dividers[0];
        var expanded = CustomLayout.Plan(workspace.CustomLayout, new Size(1800, 900), 320, 220, 4).Dividers[0];
        FlexibleLayout.Resize(workspace, expanded, 100);
        workspace.PresentationDisplay = "display-two";
        workspace.VirtualDesktop = Guid.NewGuid().ToString("D");
        session.Save();
        var restored = Store.Read<AppConfig>(Store.ConfigPath)!;
        restored.Validate();
        Check(restored.Workspaces[0].CustomLayout.Leaves().Any(area => area.PanelId == second.Id) &&
            restored.Workspaces[0].CustomLayout.Ratio != rootDivider.First / (double)(rootDivider.First + rootDivider.Second) &&
            restored.Workspaces[0].VirtualDesktop == workspace.VirtualDesktop &&
            restored.Workspaces[0].PresentationDisplay == workspace.PresentationDisplay,
            "Workspace splits, ratios or display routing were not kept in the config.");

        var folder = Path.Combine(Store.DirectoryPath, "alpha");
        var otherFolder = Path.Combine(Store.DirectoryPath, "beta");
        var id = Guid.NewGuid().ToString("D");
        var signal = HookPayload.Parse(HookProvider.Find("codex"), "Stop",
            System.Text.Json.JsonSerializer.Serialize(new { session_id = id, cwd = folder, prompt = "PRIVATE_DO_NOT_COPY" }),
            "", [], first.Record.Handle, 12);
        session.ReceiveAgentSignal(signal);
        Check(first.AgentSessions.Count == 0, "Conversation identifiers were retained without the recovery setting.");
        session.RememberAgentSessions(true);
        var saved = first.Capture();
        Check(saved.AgentSessions?.Single().Id == id && !File.ReadAllText(GroupPersistence.PathName).Contains("PRIVATE_DO_NOT_COPY"),
            "Recovery lost the session UUID or retained prompt contents.");
        var command = ProjectCommand.Create();
        command.Executable = "codex.exe";
        command.Arguments = "";
        command.Directory = folder;
        Check(AgentResume.Arguments(command, saved, true) == "resume " + id &&
            AgentResume.Arguments(command, saved, false) == "", "Codex recovery selected the wrong arguments.");
        command.Executable = "sandbox-launcher.exe";
        command.ResumeArguments = "codex resume {sessionId}";
        Check(AgentResume.Arguments(command, saved, true) == "codex resume " + id, "A configured launcher lost its original recovery route.");

        var correct = Candidate(4102, "Same terminal"); correct.ProjectDirectory = folder; correct.ExecutablePath = "B:\\Apps\\terminal.exe";
        var incorrect = Candidate(4103, correct.Title); incorrect.ProjectDirectory = otherFolder; incorrect.ExecutablePath = correct.ExecutablePath;
        var rule = WindowRule.Create(workspace.Id);
        rule.ExecutablePattern = correct.ExecutablePath; rule.DirectoryPattern = folder;
        Check(rule.Matches(correct) && !rule.Matches(incorrect), "Capture rules confuse same-title project windows.");
        Check(WorkspaceSession.MatchesQuery(correct.SearchText, "alpha terminal") && !WorkspaceSession.MatchesQuery(incorrect.SearchText, "alpha terminal"),
            "Project metadata is ignored by multiword search.");
        session.RememberAgentSessions(false);
        Check(first.AgentSessions.Count == 0 && (Store.Read<GroupState>(GroupPersistence.PathName)!.Panels[0].AgentSessions?.Count ?? 0) == 0,
            "Disabling recovery kept saved conversation identifiers.");
        Console.WriteLine("PASS nested splits, display bindings, opt-in agent recovery and project-aware capture persist without transcript data.");
    }
}
