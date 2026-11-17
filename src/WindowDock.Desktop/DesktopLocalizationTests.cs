using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WindowDock;

internal static class DesktopLocalizationTests
{
    internal static async Task RunAsync(MainWindow main)
    {
        var language = L10n.Language;
        var selected = main.Session.SelectedWorkspace;
        try
        {
            foreach (var locale in L10n.SupportedLanguages)
            {
                L10n.Initialize(locale);
                foreach (var dialog in new EditorWindow[] {
                    new AppearanceWindow(main.Session.Config.ThemeId), new BackgroundWindow(main.Session.Current),
                    new ProjectsWindow(main), new ProjectEditor(main, ProjectProfile.Create(main.Session.Current.Id)), new CommandEditor(ProjectCommand.Create()),
                    new RulesWindow(main), new RuleEditor(WindowRule.Create(main.Session.Current.Id), main.Session.Config.Workspaces),
                    new IntegrationWindow(main), new SettingsWindow(main.Session.Config, main.Session.Current), new HelpWindow(), new WindowPicker([], 10) })
                {
                    var task = main.ShowEditorAsync(dialog);
                    await Task.Delay(45);
                    if (dialog is AppearanceWindow && locale is "en" or "de" or "th" or "ja" or "zh-Hant")
                        DesktopSelfTest.Snapshot(dialog, "locale-" + locale + "-appearance.png");
                    dialog.Width = dialog.MinWidth;
                    await Task.Delay(35);
                    foreach (var text in dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.TextTrimming == TextTrimming.None))
                    {
                        DesktopSelfTest.Check(text.TextLayout.Width <= text.Bounds.Width + 1.5,
                            $"Clipped {locale} label in {dialog.GetType().Name}: {text.Text} ({text.TextLayout.Width:F1} > {text.Bounds.Width:F1})");
                    }
                    dialog.Close(false); await task;
                    DesktopSelfTest.Check(main.Session.SelectedWorkspace == selected, "A localized dialog changed the selected tab.");
                }
            }
            DesktopSelfTest.Pass("121 localized editors render at their minimum widths, preserving the selected tab; English, German, Thai, Japanese and traditional Chinese screenshots captured.");
        }
        finally { L10n.Initialize(language); }
    }
}