using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed partial class MainWindow
{
    internal void ReturnToGrid()
    {
        Session.BeginLayoutChange();
        var workspace = Session.Current;
        workspace.Layout = "grid"; workspace.Columns = 0; workspace.ColumnWeights.Clear(); workspace.RowWeights.Clear();
        Session.CompactLayout(workspace);
        Session.FocusedPanel = null; Session.LayoutChanged();
        SetStatus(L10n.Get("ThemePreview.GridAllWindowsAreShownWithEqual"));
    }
    internal void PreviewTheme(string? id) { previewThemeId = id; Refresh(); }
    internal async Task ChangeAppearanceAsync()
    {
        var dialog = new AppearanceWindow(Session.Config.ThemeId, id => PreviewTheme(id));
        try { if (await ShowEditorAsync(dialog)) { Session.Config.ThemeId = dialog.Result; Session.Save(); } }
        finally { PreviewTheme(null); }
    }
}

internal sealed class AppearanceWindow : EditorWindow
{
    private readonly Dictionary<string, RadioButton> choices = [];
    public string Result { get; private set; }
    internal void SelectTheme(string id) => choices[id].IsChecked = true;
    public AppearanceWindow(string current, Action<string>? preview = null) : base(L10n.Get("ThemePreview.WorkspaceAppearance"), 680, 520, L10n.Get("BackgroundWindow.Save"))
    {
        Result = current;
        Note(L10n.Get("ThemePreview.SelectAThemeToPreviewItImmediately"));
        var grid = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        var themes = new[] { ("graphite", L10n.Get("ThemePreview.Graphite"), L10n.Get("ThemePreview.SolidDarkMaterial")), ("light", L10n.Get("ThemePreview.Light"), L10n.Get("ThemePreview.LightSurfaces")), ("frost", L10n.Get("ThemePreview.FrostedGlass"), L10n.Get("ThemePreview.BlurredTranslucentBackground")), ("glass", L10n.Get("ThemePreview.ClearGlass"), L10n.Get("ThemePreview.LightweightTransparentMaterial")) };
        for (var index = 0; index < themes.Length; index++)
        {
            var (id, name, hint) = themes[index]; var palette = DockTheme.Palette(id);
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(new ThemeScene(id) { Height = 66, Margin = new(-1, 0, -1, 1) });
            content.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse(palette["DockText"])), FontWeight = FontWeight.Medium });
            content.Children.Add(new TextBlock { Text = hint, Foreground = new SolidColorBrush(Color.Parse(palette["DockMuted"])), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            var option = new RadioButton { GroupName = "appearance", IsChecked = current == id, Content = content, Background = new SolidColorBrush(Color.Parse(palette["DockSurface"])), Foreground = new SolidColorBrush(Color.Parse(palette["DockText"])), MinHeight = 140, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            option.Classes.Add("appearance"); Avalonia.Automation.AutomationProperties.SetName(option, name);
            option.IsCheckedChanged += (_, _) => { if (option.IsChecked == true) { Result = id; preview?.Invoke(id); } };
            choices[id] = option; Grid.SetRow(option, index / 2); Grid.SetColumn(option, index % 2); grid.Children.Add(option);
        }
        Body.Children.Add(grid);
        Note(L10n.Get("ThemePreview.ThemesChangeTheShellDockedApplicationsKeep"));
    }
}

internal sealed class ThemeScene(string id) : Control
{
    public override void Render(DrawingContext context)
    {
        var colors = DockTheme.Palette(id);
        IBrush Brush(string key) => new SolidColorBrush(Color.Parse(colors[key]));
        var rect = new Rect(Bounds.Size); if (rect.Width < 10 || rect.Height < 10) return;
        using (context.PushClip(new RoundedRect(rect, 5)))
        {
            context.DrawRectangle(Brush("DockGround"), null, rect);
            if (id is "frost" or "glass")
            {
                context.DrawRectangle(ColorPickerView.Gradient((0, Color.Parse("#779696")), (.55, Color.Parse("#928EA0")), (1, Color.Parse("#AD927C"))), null, rect);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(id == "glass" ? (byte)112 : (byte)186, 24, 25, 29)), null, rect);
            }
            context.DrawRectangle(Brush("DockSurface"), null, new Rect(0, 0, rect.Width, 12));
            context.DrawRectangle(Brush("DockRaised"), null, new Rect(5, 3, 40, 7), 2, 2);
            context.DrawRectangle(Brush("DockAccent"), null, new Rect(rect.Width - 12, 4, 6, 5), 1, 1);
            for (var panel = 0; panel < 2; panel++)
            {
                var x = 5 + panel * (rect.Width / 2); var w = rect.Width / 2 - 9;
                context.DrawRectangle(Brush("DockSurface"), null, new Rect(x, 17, w, rect.Height - 22), 3, 3);
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#15171B")), null, new Rect(x + 2, 25, w - 4, rect.Height - 32), 2, 2);
                for (var line = 0; line < 3; line++) context.DrawRectangle(new SolidColorBrush(Color.Parse(line == 0 ? "#97B5A8" : "#818792")), null, new Rect(x + 7, 30 + line * 8, (w - 18) * (line == 1 ? .65 : .9), 2));
            }
        }
    }
}
