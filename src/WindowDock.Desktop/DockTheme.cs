using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace WindowDock;

internal static class DockTheme
{
    public static string Id { get; private set; } = "frost";
    public static bool IsDark => Id != "light";
    public static bool IsGlass => Id is "frost" or "glass";
    public static IBrush Ground => Resource("DockGround");
    public static IBrush Surface => Resource("DockSurface");
    public static IBrush Raised => Resource("DockRaised");
    public static IBrush Text => Resource("DockText");
    public static IBrush Muted => Resource("DockMuted");
    public static IBrush Accent => Resource("DockAccent");
    public static IBrush Border => Resource("DockBorder");
    public static IBrush Material => new SolidColorBrush(IsDark ? Color.FromArgb(Id == "glass" ? (byte)112 : (byte)186, 24, 25, 29) : Color.FromArgb(230, 239, 240, 243));
    public static IBrush Resource(string key) => (IBrush)Application.Current!.Resources[key]!;
    public static void Apply(string id)
    {
        Id = id; var app = Application.Current!; app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var palette = Palette(id);
        foreach (var (key, color) in palette) app.Resources[key] = new SolidColorBrush(Color.Parse(color));
    }
    internal static Dictionary<string, string> Palette(string id) => id switch
    {
        "beams" => new()
        {
            ["DockGround"] = "#090C15", ["DockSurface"] = "#171D2C", ["DockRaised"] = "#233049", ["DockText"] = "#F3F6FF",
            ["DockMuted"] = "#ADBAD2", ["DockAccent"] = "#A0C5FF", ["DockAccentInk"] = "#0D1D36", ["DockBorder"] = "#34425F", ["DockSelection"] = "#2A3E63"
        },
        "light" => new()
        {
            ["DockGround"] = "#EFF0F3", ["DockSurface"] = "#F9F9FB", ["DockRaised"] = "#FFFFFF", ["DockText"] = "#1D1D1F",
            ["DockMuted"] = "#64656D", ["DockAccent"] = "#0064DC", ["DockAccentInk"] = "#FFFFFF", ["DockBorder"] = "#D2D4DA", ["DockSelection"] = "#DCE8FA"
        },
        _ => new()
        {
            ["DockGround"] = "#18191D", ["DockSurface"] = "#222328", ["DockRaised"] = "#2E2F36", ["DockText"] = "#F4F4F7",
            ["DockMuted"] = "#AAABB5", ["DockAccent"] = "#8BBCFF", ["DockAccentInk"] = "#0D223D", ["DockBorder"] = "#40414A", ["DockSelection"] = "#324157"
        }
    };
}
