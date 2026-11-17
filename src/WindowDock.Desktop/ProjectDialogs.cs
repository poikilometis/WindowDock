using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace WindowDock;

internal sealed class ProjectsWindow : EditorWindow
{
    public List<ProjectProfile> Result { get; }
    public ProjectsWindow(MainWindow host) : base(L10n.Get("MainWindowCommands.Projects"), 760, 680)
    {
        Result = GroupPersistence.Clone(host.Session.Config.Projects);
        Note(L10n.Get("ProjectDialogs.SavedCommandsRunOnlyWhenYouChoose"));
        var list = new ListBox { Height = 270, ItemTemplate = Rows.Template<ProjectProfile>(profile => Rows.Line(profile.Name, L10n.Format("ProjectDialogs.Commands", profile.Commands.Count) + host.Session.Config.Workspaces.FirstOrDefault(w => w.Id == profile.WorkspaceId)?.Name)) };
        void Update() { list.ItemsSource = Result.ToArray(); list.SelectedItem = Result.FirstOrDefault(); }
        async Task Edit(bool create)
        {
            var existing = create ? null : list.SelectedItem as ProjectProfile; if (!create && existing is null) return;
            var working = existing is null ? ProjectProfile.Create(host.Session.Current.Id) : GroupPersistence.Clone(existing);
            var editor = new ProjectEditor(host, working);
            if (!await host.ShowEditorAsync(editor, this)) return;
            if (existing is null) Result.Add(working); else Result[Result.IndexOf(existing)] = working; Update(); list.SelectedItem = working;
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(DockControls.Button(L10n.Get("BasicDialogs.Add"), "add", () => _ = host.UserActionAsync(() => Edit(true))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Edit"), "settings", () => _ = host.UserActionAsync(() => Edit(false))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Remove"), "close", () => { if (list.SelectedItem is ProjectProfile profile) { Result.Remove(profile); Update(); } }));
        Body.Children.Add(actions); Body.Children.Add(list); Update(); list.DoubleTapped += (_, _) => _ = host.UserActionAsync(() => Edit(false));
        Commit = () => { foreach (var profile in Result) profile.Validate(); };
    }
}

internal sealed class ProjectEditor : EditorWindow
{
    public ProjectEditor(MainWindow host, ProjectProfile profile) : base(L10n.Get("ProjectDialogs.EditProject"), 760, 580)
    {
        var name = Input(profile.Name);
        var workspace = Choices(host.Session.Config.Workspaces.Select(w => new Choice<WorkspaceConfig>(w, w.Name)), w => w.Id == profile.WorkspaceId); workspace.SelectedItem ??= workspace.Items.Cast<object>().FirstOrDefault();
        Field(L10n.Get("EditorWindow.Name"), name); Field(L10n.Get("ProjectDialogs.InitialTab"), workspace);
        var list = new ListBox { Height = 245, ItemTemplate = Rows.Template<ProjectCommand>(command => Rows.Line(command.Name, command.Executable + " " + command.Arguments)) };
        void Update() { list.ItemsSource = profile.Commands.ToArray(); list.SelectedItem = profile.Commands.FirstOrDefault(); }
        async Task Edit(bool create)
        {
            var existing = create ? null : list.SelectedItem as ProjectCommand; if (!create && existing is null) return;
            if (create && profile.Commands.Count >= 64) throw new InvalidOperationException(L10n.Get("ProjectDialogs.AProjectCanContainUpToCommands"));
            var working = existing is null ? ProjectCommand.Create() : GroupPersistence.Clone(existing);
            if (!await host.ShowEditorAsync(new CommandEditor(working), this)) return;
            if (existing is null) profile.Commands.Add(working); else profile.Commands[profile.Commands.IndexOf(existing)] = working; Update(); list.SelectedItem = working;
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.AddCommand"), "add", () => _ = host.UserActionAsync(() => Edit(true))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Edit"), "settings", () => _ = host.UserActionAsync(() => Edit(false))));
        actions.Children.Add(DockControls.Button(L10n.Get("ProjectDialogs.Remove"), "close", () => { if (list.SelectedItem is ProjectCommand command) { profile.Commands.Remove(command); Update(); } }));
        Body.Children.Add(actions); Body.Children.Add(list); Update();
        Commit = () => { profile.Name = Required(name, L10n.Get("EditorWindow.Name")); profile.WorkspaceId = Value<WorkspaceConfig>(workspace).Id; profile.Validate(); };
    }
}

internal sealed class CommandEditor : EditorWindow
{
    public CommandEditor(ProjectCommand command) : base(L10n.Get("ProjectDialogs.ProjectCommand"), 760, 680)
    {
        var name = Input(command.Name); var executable = Input(command.Executable); var arguments = Input(command.Arguments); var directory = Input(command.Directory); var title = Input(command.TitlePattern);
        var environment = Input(string.Join(Environment.NewLine, command.Environment.Select(e => e.Key + "=" + e.Value)), true);
        var integrations = new List<Choice<string>> { new("", L10n.Get("ProjectDialogs.DoNotConfigure")), new("auto", L10n.Get("ProjectDialogs.DetectAgentAutomatically")) };
        integrations.AddRange(HookProvider.All.Select(p => new Choice<string>(p.Id, p.Name)));
        var integration = Choices(integrations, id => id == command.IntegrationId); integration.SelectedItem ??= integrations[0];
        Field(L10n.Get("WorkspaceSession.PanelName"), name); Field(L10n.Get("ProjectDialogs.Program"), executable); Field(L10n.Get("ProjectDialogs.Arguments"), arguments); Field(L10n.Get("ProjectDialogs.WorkingFolder"), FolderInput(directory));
        Field(L10n.Get("ProjectDialogs.TitlePattern"), title); Field(L10n.Get("ProjectDialogs.AutomaticHookSetup"), integration); Field(L10n.Get("ProjectDialogs.Environment"), environment);
        Note(L10n.Get("ProjectDialogs.OneKEYValueVariablePerLineAgentId"));
        Note(L10n.Get("ProjectDialogs.HooksAreConfiguredBeforeLaunchExistingHooks"));
        Commit = () =>
        {
            command.Name = Required(name, L10n.Get("WorkspaceSession.PanelName")); command.Executable = Required(executable, L10n.Get("ProjectDialogs.Program")); command.Arguments = arguments.Text ?? "";
            command.Directory = directory.Text?.Trim() ?? ""; command.TitlePattern = title.Text ?? ""; command.IntegrationId = Value<string>(integration);
            List<EnvironmentEntry> parsed = []; HashSet<string> keys = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var line in (environment.Text ?? "").Split('\n').Select(s => s.TrimEnd('\r')).Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                var split = line.IndexOf('='); if (split < 1) throw new InvalidOperationException(L10n.Get("ProjectDialogs.VariablesMustUseTheKEYValueFormat"));
                var key = line[..split].Trim(); if (key.Length == 0 || !keys.Add(key) || key.Contains('\0') || line.Contains('\0')) throw new InvalidOperationException(L10n.Format("ProjectDialogs.InvalidOrDuplicateVariableName", key));
                parsed.Add(new() { Key = key, Value = line[(split + 1)..] });
            }
            command.Environment = parsed; command.Validate();
        };
    }
}