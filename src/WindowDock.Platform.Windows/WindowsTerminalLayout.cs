using System.Diagnostics;
using System.Text;

namespace WindowDock;

internal static class WindowsTerminalLayout
{
    private const string Option = "firstWindowPreference";
    private const string Value = "persistedWindowLayout";
    private static string Alias => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "wt.exe");

    internal static IReadOnlyList<string> SettingsFiles()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.Combine(root, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(root, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(root, "Microsoft", "Windows Terminal", "settings.json")
        }.Where(File.Exists).ToArray();
    }

    internal static string UpdateSetting(string settings)
    {
        var root = JsonSyntax.Parse(settings);
        if (root.Kind != "object") throw new FormatException(L10n.Get("Recovery.TerminalSettingsUnavailable"));
        if (root.String(Option) == Value) return settings;
        var property = root.Get(Option);
        if (property is not null) return settings[..property.Start] + "\"" + Value + "\"" + settings[property.End..];
        var newline = settings.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = newline + "    \"" + Option + "\": \"" + Value + "\"" + (root.Properties.Count == 0 ? newline : ",");
        return settings.Insert(root.Start + 1, insertion);
    }

    internal static void Enable(string path)
    {
        if (!SettingsFiles().Contains(path, StringComparer.OrdinalIgnoreCase))
            throw new FileNotFoundException(L10n.Get("Recovery.TerminalSettingsUnavailable"));
        var info = new FileInfo(path);
        if (info.Length > 8 * 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(L10n.Get("Recovery.TerminalSettingsUnavailable"));
        var before = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(before);
        var after = UpdateSetting(text);
        if (text == after) return;
        if (!before.SequenceEqual(File.ReadAllBytes(path)))
            throw new IOException(L10n.Get("Recovery.TerminalSettingsUnavailable"));
        var backup = path + ".windowdock.bak";
        Store.WriteBytes(path, Encoding.UTF8.GetBytes(after), File.Exists(backup) ? null : backup);
    }

    internal static void Reopen()
    {
        if (!File.Exists(Alias)) throw new FileNotFoundException(L10n.Get("Recovery.TerminalSettingsUnavailable"));
        using var process = Process.Start(new ProcessStartInfo(Alias) { UseShellExecute = true });
    }

    internal static ShortcutProcess OpenShortcut(string path)
    {
        if (!RecoveryShortcut.IsShortcutPath(path) || !File.Exists(path))
            throw new FileNotFoundException(L10n.Get("Recovery.ShortcutUnavailable"));
        // Resolve the original shortcut through Windows, including its arguments,
        // working directory and elevation preference.
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return process is null ? default : new(process.Id, WindowMatching.StartTicks(process.Id));
    }
}
