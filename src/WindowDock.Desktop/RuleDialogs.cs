using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace WindowDock;

internal sealed class RulesWindow : EditorWindow
{
    public List<WindowRule> Result { get; private set; }
    public bool Automatic { get; private set; }
    public RulesWindow(MainWindow host) : base(L10n.Get("RuleDialogs.CaptureRules"), 760, 530)
    {
        Result = GroupPersistence.Clone(host.Session.Config.Rules);
        var enabled = Check(L10n.Get("RuleDialogs.AutomaticallyCaptureMatchingNewWindows"), host.Session.Config.AutomaticRules); Body.Children.Add(enabled);
        Note(L10n.Get("RuleDialogs.ExclusionsTakePriorityPreviewShowsTheResult"));
        var list = new ListBox { Height = 280, ItemTemplate = Rows.Template<WindowRule>(rule => Rows.Line((rule.Exclude ? L10n.Get("RuleDialogs.Exclude") : "") + rule.Name + (rule.Enabled ? "" : L10n.Get("RuleDialogs.Disabled")), (rule.ProcessPattern.Length == 0 ? L10n.Get("RuleDialogs.AnyApplication") : rule.ProcessPattern) + " · " + rule.TitlePattern)) };
        void Update() { list.ItemsSource = Result.ToArray(); list.SelectedItem = Result.FirstOrDefault(); }
        async Task Edit(bool create)
        {
            var existing = create ? null : list.SelectedItem as WindowRule; if (!create && existing is null) return;
            var working = existing is null ? WindowRule.Create(host.Session.Current.Id) : GroupPersistence.Clone(existing);
            var dialog = new RuleEditor(working, host.Session.Config.Workspaces);
            if (!await host.ShowEditorAsync(dialog, this)) return;
            if (existing is null) Result.Add(working); else Result[Result.IndexOf(existing)] = working; Update(); list.SelectedItem = working;
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(DockControls.Button(L10n.Get("BasicDialogs.Add"), "add", () => _ = host.UserActionAsync(() => Edit(true))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Edit"), "settings", () => _ = host.UserActionAsync(() => Edit(false))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Remove"), "close", () => { if (list.SelectedItem is WindowRule rule) { Result.Remove(rule); Update(); } }));
        actions.Children.Add(DockControls.Button(L10n.Get("RuleDialogs.Check"), "search", () => _ = host.UserActionAsync(async () => { await host.ShowEditorAsync(new RulePreview(Result, host.Session.DesktopWindows(), host.Session.Config.Workspaces), this); })));
        Body.Children.Add(actions); Body.Children.Add(list); Update();
        list.DoubleTapped += (_, _) => _ = host.UserActionAsync(() => Edit(false));
        Commit = () => { foreach (var rule in Result) rule.Validate(); Automatic = enabled.IsChecked == true; };
    }
}

internal sealed class RuleEditor : EditorWindow
{
    public RuleEditor(WindowRule rule, IReadOnlyList<WorkspaceConfig> workspaces) : base(L10n.Get("RuleDialogs.CaptureRule"), 680, 550)
    {
        var name = Input(rule.Name); var process = Input(rule.ProcessPattern); var title = Input(rule.TitlePattern);
        var executable = Input(rule.ExecutablePattern ?? "");
        var directory = Input(rule.DirectoryPattern ?? "");
        var target = Choices(workspaces.Select(w => new Choice<WorkspaceConfig>(w, w.Name)), w => w.Id == rule.WorkspaceId); target.SelectedItem ??= target.Items.Cast<object>().FirstOrDefault();
        var enabled = Check(L10n.Get("RuleDialogs.RuleEnabled"), rule.Enabled); var exclude = Check(L10n.Get("RuleDialogs.ExcludeMatchingWindowsFromCapture"), rule.Exclude);
        var terminal = Check(L10n.Get("RuleDialogs.TerminalsOnly"), rule.TerminalOnly); var overflow = Check(L10n.Get("RuleDialogs.CreateTabsWhenFull"), rule.Overflow);
        Field(L10n.Get("EditorWindow.Name"), name); Field(L10n.Get("RuleDialogs.Application"), process); Field(L10n.Get("RuleDialogs.WindowTitle"), title); Field(L10n.Get("Models.Tab"), target);
        Field(L10n.Get("WindowSearch.Executable"), executable);
        Field(L10n.Get("EditorWindow.ProjectFolder"), directory);
        Body.Children.Add(enabled); Body.Children.Add(exclude); Body.Children.Add(terminal); Body.Children.Add(overflow);
        Note(L10n.Get("RuleDialogs.PatternsMatchesAnyTextMatchesOneCharacter"));
        exclude.IsCheckedChanged += (_, _) => { target.IsEnabled = exclude.IsChecked != true; overflow.IsEnabled = exclude.IsChecked != true; };
        Commit = () =>
        {
            rule.Name = Required(name, L10n.Get("EditorWindow.Name")); rule.ProcessPattern = process.Text ?? ""; rule.TitlePattern = title.Text ?? "";
            rule.ExecutablePattern = executable.Text ?? "";
            rule.DirectoryPattern = directory.Text ?? "";
            rule.WorkspaceId = Value<WorkspaceConfig>(target).Id; rule.Enabled = enabled.IsChecked == true; rule.Exclude = exclude.IsChecked == true;
            rule.TerminalOnly = terminal.IsChecked == true; rule.Overflow = overflow.IsChecked == true; rule.Validate();
        };
    }
}

internal sealed class RulePreview : EditorWindow
{
    public RulePreview(IEnumerable<WindowRule> rules, IReadOnlyList<WindowCandidate> candidates, IReadOnlyList<WorkspaceConfig> workspaces) : base(L10n.Get("RuleDialogs.RulePreview"), 760, 480, L10n.Get("BasicDialogs.Close"))
    {
        Note(L10n.Get("RuleDialogs.WindowsStayWhereTheyAre"));
        var rows = candidates.Select(candidate =>
        {
            var decision = RuleEngine.Evaluate(rules, candidate);
            var result = decision.Excluded ? L10n.Get("RuleDialogs.Excluded") : decision.Rule is null ? L10n.Get("RuleDialogs.NoMatchingRule") : L10n.Format("RuleDialogs.Tab", (workspaces.FirstOrDefault(w => w.Id == decision.Rule.WorkspaceId)?.Name ?? L10n.Get("RuleDialogs.NotFound")));
            return new Choice<WindowCandidate>(candidate, string.Join(" · ",
                new[] { result, decision.Rule?.Name, candidate.Display }.Where(text => !string.IsNullOrWhiteSpace(text))));
        }).ToArray();
        Body.Children.Add(new ListBox { ItemsSource = rows, Height = 300 });
    }
}
