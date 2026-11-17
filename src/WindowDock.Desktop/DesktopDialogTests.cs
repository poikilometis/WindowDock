using Avalonia.Controls;
using Avalonia.VisualTree;

namespace WindowDock;

internal static class DesktopDialogTests
{
    internal static async Task RunAsync(MainWindow main)
    {
        // Recycle populated containers as real filtering and editing do. Rendering an empty
        // editor alone misses null template data raised by Avalonia's deferred text events.
        var session = main.Session;
        var candidates = new[] { new WindowCandidate { Title = "Проверка списка", ProcessName = "qa" } };
        var rules = session.Config.Rules;
        var projects = session.Config.Projects;
        var pending = session.Pending.ToArray();
        var profile = ProjectProfile.Create(session.Current.Id);
        profile.Commands.Add(ProjectCommand.Create());
        session.Config.Rules = [WindowRule.Create(session.Current.Id)];
        session.Config.Projects = [profile];
        session.Pending.Add(new SavedPanel { Id = "qa-list-role", WorkspaceId = session.Current.Id, Title = "Ожидающее окно", ProcessName = "qa" });
        try
        {
            var picker = new WindowPicker(candidates, 10);
            var pickerTask = main.ShowEditorAsync(picker);
            await Task.Delay(100);
            var filter = picker.GetVisualDescendants().OfType<TextBox>().First();
            var list = picker.GetVisualDescendants().OfType<ListBox>().First();
            foreach (var query in new[] { "нет совпадений", "Проверка", "", "нет совпадений", "" })
            {
                filter.Text = query; await Task.Delay(60);
                DesktopSelfTest.Check(list.ItemCount == (query == "нет совпадений" ? 0 : 1), "Window-picker filtering failed after recycling populated items.");
            }
            picker.Close(false); await pickerTask;
            foreach (var editor in new EditorWindow[]
            {
                new SearchWindow(session), new RulesWindow(main), new ProjectsWindow(main), new ProjectEditor(main, profile),
                new PanelPicker(session), new PendingGroupsWindow(main), new HookSessionsWindow(main)
            })
            {
                var task = main.ShowEditorAsync(editor); await Task.Delay(80);
                foreach (var items in editor.GetVisualDescendants().OfType<ListBox>().ToArray())
                {
                    var source = items.ItemsSource; var count = items.ItemCount;
                    items.ItemsSource = null; await Task.Delay(40);
                    items.ItemsSource = source; await Task.Delay(40);
                    DesktopSelfTest.Check(items.ItemCount == count, editor.Title + " did not recover its rows after recycling.");
                }
                editor.Close(false); await task;
            }
            await NestedEditorsAsync(main, profile);
            DesktopSelfTest.Pass("Populated window picker filters repeatedly; search, rules, projects, commands, panels, saved roles and hook-session lists recycle safely.");
        }
        finally
        {
            session.Config.Rules = rules; session.Config.Projects = projects;
            session.Pending.Clear(); session.Pending.AddRange(pending);
        }
    }
    private static async Task NestedEditorsAsync(MainWindow main, ProjectProfile profile)
    {
        var projects = new ProjectsWindow(main);
        var modal = main.ShowEditorAsync(projects); await Task.Delay(60);
        var handle = projects.TryGetPlatformHandle()!.Handle;
        var size = projects.ClientSize;
        var edit = new ProjectEditor(main, GroupPersistence.Clone(profile));
        var editTask = main.ShowEditorAsync(edit, projects); await Task.Delay(60);
        DesktopSelfTest.Check(projects.TryGetPlatformHandle()!.Handle == handle && projects.ClientSize == size && !edit.IsVisible, "Nested project creates another native dialog or resizes its host.");
        var input = projects.GetVisualDescendants().OfType<TextBox>().First(); input.Text = "Retained parent input"; input.Focus();
        var command = new CommandEditor(ProjectCommand.Create());
        var commandTask = main.ShowEditorAsync(command, edit); await Task.Delay(60);
        DesktopSelfTest.Check(projects.TryGetPlatformHandle()!.Handle == handle && !command.IsVisible && projects.Title == "Команда проекта", "Nested command is not hosted in the same settings window.");
        DesktopSelfTest.Snapshot(projects, "avalonia-project-command-page.png");
        command.FinishPage(false); DesktopSelfTest.Check(!await commandTask, "Back commits a cancelled command."); await Task.Delay(25);
        DesktopSelfTest.Check(ReferenceEquals(projects.FocusManager?.GetFocusedElement(), input), "Back does not restore the parent control focus.");
        DesktopSelfTest.Check(input.Text == "Retained parent input" && projects.GetVisualDescendants().OfType<TextBox>().Contains(input), "Returning from a command loses the parent form.");
        edit.FinishPage(false); DesktopSelfTest.Check(!await editTask && projects.Title == "Проекты", "Back does not return to the project list.");
        projects.Close(false); await modal;
        var root = new ProjectsWindow(main); var rootTask = main.ShowEditorAsync(root); await Task.Delay(40);
        var nested = new ProjectEditor(main, GroupPersistence.Clone(profile)); var nestedTask = main.ShowEditorAsync(nested, root);
        root.Close(false); DesktopSelfTest.Check(!await nestedTask && !await rootTask, "Closing the settings host leaves a pending nested editor.");
        DesktopSelfTest.Pass("Project and command pages share one HWND and stable bounds; Back preserves parent input, cancels changes and closing the host completes pending pages.");
    }
}
