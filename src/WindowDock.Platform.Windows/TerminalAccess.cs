using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WindowDock;

internal static class TerminalAccess
{
    private const int HistoryCharacters = 240000;
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
        $root = [Windows.Automation.AutomationElement]::FromHandle([IntPtr][long]$env:WD_UI_HANDLE)
        if ($root.Current.ProcessId -ne [int]$env:WD_UI_PID) { exit 2 }
        $scope = [Windows.Automation.TreeScope]::Descendants
        $action = $env:WD_UI_ACTION
        if ($action -ne 'history') {
            $condition = [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)
            $tabs = @($root.FindAll($scope, $condition) | Where-Object { $_.Current.Name })
            if ($action -eq 'tabs') {
                [Console]::Write((ConvertTo-Json -InputObject @($tabs | ForEach-Object { $_.Current.Name } | Select-Object -First 128) -Compress))
                exit 0
            }
            $target = @($tabs | Where-Object { $_.Current.Name -ceq $env:WD_UI_TAB })
            if ($target.Count -ne 1) { exit 3 }
            $select = $target[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
            $select.Select()
            if ([int]$env:WD_UI_PANE -eq 0) { exit 0 }
            Start-Sleep -Milliseconds 140
        }
        $property = [Windows.Automation.AutomationElement]::IsTextPatternAvailableProperty
        $condition = [Windows.Automation.PropertyCondition]::new($property, $true)
        $panes = @($root.FindAll($scope, $condition) | Where-Object { $_.Current.ClassName -eq 'TermControl' -and -not $_.Current.IsOffscreen } |
            Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left })
        $index = [int]$env:WD_UI_PANE - 1
        if ($action -eq 'focus') {
            if ($index -lt 0 -or $index -ge $panes.Count) { exit 4 }
            $panes[$index].SetFocus()
            exit 0
        }
        if ($root.GetCurrentPropertyValue($property) -eq $true) { $panes = @($root) }
        $pane = $panes | Where-Object { $_.Current.HasKeyboardFocus } | Select-Object -First 1
        if ($null -eq $pane -and $panes.Count -eq 1) { $pane = $panes[0] }
        if ($null -eq $pane) { exit 4 }
        $pattern = $pane.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern)
        [Console]::Write($pattern.DocumentRange.GetText([int]$env:WD_UI_LIMIT))
        """;

    internal static bool IsTerminal(WindowRecord window) =>
        window.ClassName is Native.ConsoleWindowClass or Native.TerminalWindowClass;

    internal static async Task<string[]> TabsAsync(WindowRecord window)
    {
        var result = await InvokeAsync(window, "tabs", "", 0);
        return JsonSerializer.Deserialize<string[]>(result) ?? [];
    }

    internal static Task<string> FocusAsync(WindowRecord window, TerminalTarget target) =>
        InvokeAsync(window, "focus", target.Tab, target.Pane);

    internal static async Task SaveHistoryAsync(RuntimePanel panel)
    {
        var path = HistoryPath(panel.Id);
        if (path.Length == 0) throw new InvalidOperationException(L10n.Get("Recovery.TerminalUnavailable"));
        var text = panel.Record.ClassName == Native.ConsoleWindowClass
            ? await ReadClassicAsync(panel.Record) : await InvokeAsync(panel.Record, "history", "", 0);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(L10n.Get("Recovery.NoTerminalText"));
        Store.WriteBytes(path, Encoding.UTF8.GetBytes(text));
    }

    internal static string HistoryPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var key)) return "";
        return Path.Combine(Store.DirectoryPath, "terminal-history", key.ToString("N") + ".txt");
    }

    private static Task<string> ReadClassicAsync(WindowRecord window)
    {
        var command = new ProcessStartInfo(RuntimePaths.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "--console-history",
            window.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            window.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            window.ProcessStartTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            command.ArgumentList.Add(argument);
        return ReadWorkerAsync(command, 4500);
    }

    private static async Task<string> InvokeAsync(WindowRecord window, string action, string tab, int pane)
    {
        if (!NativeIdentity.Matches(window)) throw new InvalidOperationException(L10n.Get("Recovery.TerminalUnavailable"));
        var command = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Mta", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
            command.ArgumentList.Add(argument);
        command.Environment["WD_UI_HANDLE"] = window.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Environment["WD_UI_PID"] = window.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Environment["WD_UI_ACTION"] = action;
        command.Environment["WD_UI_TAB"] = tab;
        command.Environment["WD_UI_PANE"] = pane.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Environment["WD_UI_LIMIT"] = HistoryCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await ReadWorkerAsync(command, 4500);
    }

    private static async Task<string> ReadWorkerAsync(ProcessStartInfo command, int milliseconds)
    {
        using var process = Process.Start(command) ?? throw new InvalidOperationException(L10n.Get("Recovery.TerminalUnavailable"));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(milliseconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw new TimeoutException(L10n.Get("Recovery.TerminalUnavailable"));
        }
        await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(L10n.Get("Recovery.TerminalUnavailable"));
        return await output;
    }
}
