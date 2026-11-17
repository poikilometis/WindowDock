using Avalonia.Controls;
using Avalonia.Layout;

namespace WindowDock;
internal sealed class FixtureWindow : Window
{
    private NativeProbeEditor? native;
    public FixtureWindow(string token)
    {
        Title = "WindowDock test " + token; Width = 700; Height = 460;
        var header = new TextBlock { Text = "Проверочное окно · " + token, Margin = new(12), Height = 26 };
        var editor = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = string.Join(Environment.NewLine, Enumerable.Range(0, 100).Select(i => $"Строка {i}: ввод, прокрутка и сохранение независимого процесса.")) };
        var layout = new DockPanel(); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header); layout.Children.Add(editor); Content = layout;
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle) native = new(handle.Handle, (int)(ClientSize.Width * RenderScaling), (int)(ClientSize.Height * RenderScaling));
        };
        SizeChanged += (_, _) => { if (OperatingSystem.IsWindows()) native?.Resize((int)(ClientSize.Width * RenderScaling), (int)(ClientSize.Height * RenderScaling)); };
        Closed += (_, _) => { if (OperatingSystem.IsWindows()) native?.Dispose(); };
    }
}