using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WindowDock;

internal sealed class DockIcon : Control
{
    public string Kind { get; init; } = "add";
    public IBrush? Ink { get; set; }
    public string? InkResource { get; set; }
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["add"] = "M10 4 V16 M4 10 H16",
        ["addwindow"] = "M12 16 H4 A2 2 0 0 1 2 14 V5 A2 2 0 0 1 4 3 H13 A2 2 0 0 1 15 5 V9 M2 7 H15 M15 11 V18 M11.5 14.5 H18.5",
        ["collect"] = "M7 12 H3 V3 H13 V8 M13 8 H17 V16 H7 V12 M3 7 H10 M7 4 L10 7 L7 10",
        ["grid"] = "M3 3 H8 V8 H3 Z M12 3 H17 V8 H12 Z M3 12 H8 V17 H3 Z M12 12 H17 V17 H12 Z",
        ["focus"] = "M3 8 V3 H8 M12 3 H17 V8 M17 12 V17 H12 M8 17 H3 V12",
        ["magnet"] = "M4 3 V11 A6 6 0 0 0 16 11 V3 M4 7 H7 M13 7 H16",
        ["back"] = "M12 4 L6 10 L12 16",
        ["detach"] = "M10 4 H4 V16 H16 V10 M10 2 H18 V10 M18 2 L8 12",
        ["search"] = "M12 12 L18 18 M14 8 A6 6 0 1 1 2 8 A6 6 0 1 1 14 8",
        ["close"] = "M5 5 L15 15 M15 5 L5 15",
        ["check"] = "M4 10 L8 14 L16 6",
        ["bell"] = "M4 14 H16 L14.5 11 V8 A4.5 4.5 0 0 0 5.5 8 V11 Z M8 17 Q10 19 12 17",
        ["settings"] = "M3 5 H17 M3 10 H17 M3 15 H17 M7 3 V7 M13 8 V12 M8 13 V17"
    };
    private static readonly Dictionary<string, Geometry> geometries = [];
    public DockIcon() { Width = Height = 16; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var ink = Ink ?? DockTheme.Resource(InkResource ?? "DockText");
        if (Kind == "more")
        {
            var dotScale = Math.Min(Bounds.Width, Bounds.Height) / 20;
            using (context.PushTransform(Matrix.CreateScale(dotScale, dotScale)))
                for (var x = 4.0; x <= 16; x += 6) context.DrawEllipse(ink, null, new Point(x, 10), 1.45, 1.45);
            return;
        }
        if (!Paths.TryGetValue(Kind, out var path)) return;
        if (!geometries.TryGetValue(Kind, out var geometry)) geometries[Kind] = geometry = StreamGeometry.Parse(path);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 20;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, new Pen(Ink ?? DockTheme.Resource(InkResource ?? "DockText"), 1.15, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}

internal static class DockControls
{
    public static Button Button(string text, string icon, Action? action = null, bool primary = false, string? tip = null)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(icon)) row.Children.Add(new DockIcon { Kind = icon, InkResource = primary ? "DockAccentInk" : null });
        if (!string.IsNullOrEmpty(text)) row.Children.Add(new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var button = new Button { Content = row, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        button.Classes.Add("dock"); if (icon == "more") button.Classes.Add("menu-action"); if (primary) button.Classes.Add("primary");
        if (action is not null) button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip ?? text); Avalonia.Automation.AutomationProperties.SetName(button, tip ?? text);
        return button;
    }
    public static TextBlock Note(string text) { var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 }; note.Classes.Add("note"); return note; }
}
