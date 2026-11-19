using Avalonia.Controls;

namespace WindowDock;

internal sealed class CaptureOptionsWindow : EditorWindow
{
    internal CaptureOptions Options { get; private set; } = new();

    internal CaptureOptionsWindow(WorkspaceSession session, IReadOnlyList<WindowCandidate> windows)
        : base(L10n.Get("Workflow.CollectOptions"), 650, 570, L10n.Get("MainWindow.CollectWindows"))
    {
        Note(L10n.Format("Workflow.CaptureDestination", session.Current.Name));
        var query = Input("");
        Field(L10n.Get("BasicDialogs.WindowOrApplicationName"), query);
        var terminal = Check(L10n.Get("RuleDialogs.TerminalsOnly"), false);
        var minimized = Check(L10n.Get("Workflow.IncludeMinimized"), false);
        var rules = Check(L10n.Get("Workflow.UseRules"), true);
        Body.Children.Add(terminal);
        Body.Children.Add(minimized);
        Body.Children.Add(rules);
        var list = CaptureReportWindow.MakeList();
        Body.Children.Add(list);
        CaptureOptions Current() => new(terminal.IsChecked == true, minimized.IsChecked == true, rules.IsChecked == true, query.Text ?? "");
        void Refresh() => list.ItemsSource = session.PreviewCapture(windows, Current());
        query.TextChanged += (_, _) => Refresh();
        foreach (var check in new[] { terminal, minimized, rules }) check.IsCheckedChanged += (_, _) => Refresh();
        Commit = () => Options = Current();
        Opened += (_, _) => query.Focus();
        Refresh();
    }
}

internal sealed class CaptureReportWindow : EditorWindow
{
    internal CaptureReportWindow(IReadOnlyList<CaptureResult> results)
        : base(L10n.Get("Workflow.CaptureReport"), 650, 465, L10n.Get("BasicDialogs.Close"))
    {
        var list = MakeList();
        list.ItemsSource = results;
        Body.Children.Add(list);
    }

    internal static ListBox MakeList() => new()
    {
        Height = 230,
        ItemTemplate = Rows.Template<CaptureResult>(item => Rows.Line(item.Window.Title,
            string.Join(" · ", new[] { item.Window.ProcessName, L10n.Get("Workflow.Capture." + item.State), item.Tab, item.Explanation }
                .Where(text => !string.IsNullOrWhiteSpace(text)))))
    };
}

internal sealed class ProjectOpenWindow : EditorWindow
{
    internal bool StartMissing { get; private set; }
    internal bool Resume { get; private set; }
    internal bool RetryUnmatched { get; private set; }

    internal ProjectOpenWindow(ProjectProfile profile, IReadOnlyList<ProjectRole> plan)
        : base(L10n.Format("Workflow.OpenProject", profile.Name), 670, 505, L10n.Get("Workflow.Open"))
    {
        var start = Check(L10n.Get("Workflow.StartMissing"), true);
        var resume = Check(L10n.Get("Workflow.UseResumeArguments"), false);
        var retry = Check(L10n.Get("Workflow.RetryUnmatched"), false);
        resume.IsVisible = profile.Commands.Any(command => !string.IsNullOrWhiteSpace(command.ResumeArguments));
        retry.IsVisible = plan.Any(role => role.CanRetry);
        start.IsCheckedChanged += (_, _) => resume.IsEnabled = retry.IsEnabled = start.IsChecked == true;
        Body.Children.Add(start);
        Body.Children.Add(resume);
        Body.Children.Add(retry);
        Body.Children.Add(RoleList(plan));
        if (plan.Any(role => role.State == ProjectRoleState.Ambiguous)) Note(L10n.Get("Workflow.ResolveProjectRole"));
        Commit = () =>
        {
            StartMissing = start.IsChecked == true;
            Resume = StartMissing && resume.IsChecked == true;
            RetryUnmatched = StartMissing && retry.IsChecked == true;
        };
    }

    internal static ListBox RoleList(IReadOnlyList<ProjectRole> roles) => new()
    {
        Height = 250,
        ItemsSource = roles,
        ItemTemplate = Rows.Template<ProjectRole>(role => Rows.Line(role.Command.Name,
            string.Join(" · ", new[] { L10n.Get("Workflow.Project." + role.State), role.Panel?.Caption ?? role.Window?.Title, role.Error }
                .Where(text => !string.IsNullOrWhiteSpace(text)))))
    };
}

internal sealed class ProjectReportWindow : EditorWindow
{
    internal ProjectReportWindow(IReadOnlyList<ProjectRole> roles)
        : base(L10n.Get("Workflow.ProjectReport"), 650, 450, L10n.Get("BasicDialogs.Close"))
    {
        Body.Children.Add(ProjectOpenWindow.RoleList(roles));
    }
}

internal sealed class ShortcutsWindow : EditorWindow
{
    internal Dictionary<int, string> Result { get; }

    internal ShortcutsWindow(AppConfig config, IReadOnlySet<int> unavailable) : base(L10n.Get("Workflow.Shortcuts"), 675, 550)
    {
        Result = new(config.Shortcuts);
        var list = new ListBox { Height = 225 };
        var input = Input("");
        input.MaxLength = 80;
        string Binding(DockCommand command) => Result.GetValueOrDefault(command.Id) ?? command.Default;
        list.ItemTemplate = Rows.Template<DockCommand>(command => Rows.Line(
            DockShortcuts.Label(command), Binding(command) + (unavailable.Contains(command.Id) ? " · " + L10n.Get("Workflow.ShortcutUnavailable") : "")));
        list.ItemsSource = DockShortcuts.Commands;
        DockCommand? editing = null;
        void SaveEdit()
        {
            if (editing is not null) Result[editing.Id] = input.Text?.Trim() ?? "";
        }
        list.SelectionChanged += (_, _) =>
        {
            SaveEdit();
            editing = list.SelectedItem as DockCommand;
            input.Text = editing is null ? "" : Binding(editing);
        };
        Body.Children.Add(list);
        Field(L10n.Get("Workflow.Shortcut"), input);
        Body.Children.Add(DockControls.Button(L10n.Get("Workflow.ResetShortcut"), "back", () =>
        {
            if (editing is not null) input.Text = editing.Default;
        }));
        Note(L10n.Get("Workflow.ShortcutHelp"));
        list.SelectedIndex = 0;
        Commit = () =>
        {
            SaveEdit();
            var assigned = new HashSet<DockShortcut>();
            foreach (var command in DockShortcuts.Commands)
            {
                var text = Binding(command);
                if (text.Length == 0) continue;
                if (!DockShortcut.TryParse(text, out var commandBinding) || !assigned.Add(commandBinding))
                    throw new InvalidOperationException(L10n.Format("Workflow.InvalidShortcut", DockShortcuts.Label(command)));
            }
        };
    }
}
