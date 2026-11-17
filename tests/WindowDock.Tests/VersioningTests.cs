using System.Diagnostics;
using System.Text.Json;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void Versioning()
    {
        var directory = FindRoot();
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(directory, "Directory.Build.props"));
        var release = props.Descendants("Version").Single().Value;
        Check(RuntimePaths.ProductVersion == release, "Compiled product version differs from the authoritative release version.");
        Check(typeof(RuntimePaths).Assembly.GetName().Version!.ToString(3) == release.Split('-')[0], "Assembly identity differs from the public version.");
        foreach (var lockPath in Directory.EnumerateFiles(Path.Combine(directory, "src"), "packages.lock.json", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(directory, "tests"), "packages.lock.json", SearchOption.AllDirectories)))
        {
            using var lockFile = JsonDocument.Parse(File.ReadAllText(lockPath));
            foreach (var framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            foreach (var package in framework.Value.EnumerateObject())
            {
                if (package.Value.GetProperty("type").GetString() != "Project" || !package.Value.TryGetProperty("dependencies", out var dependencies)) continue;
                foreach (var dependency in dependencies.EnumerateObject().Where(d => d.Name.StartsWith("WindowDock.", StringComparison.Ordinal)))
                    Check(dependency.Value.GetString() == "[" + release + ", )", "Stale internal project version in lock file: " + lockPath);
            }
        }
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, HookCommands.ManifestName)));
        Check(protocol.RootElement.GetProperty("product").GetString() == "WindowDock" && protocol.RootElement.GetProperty("hookProtocol").GetInt32() == HookCommands.ProtocolVersion, "Hook protocol manifest is missing or inconsistent.");
        foreach (var provider in HookProvider.All)
        {
            var script = HookCommands.Script(provider, provider.Events[0], RuntimePaths.Executable);
            Check(script.Contains(HookCommands.ManifestName) && !script.Contains("FileMajorPart") && !script.Contains("GetVersionInfo"), "New callback compatibility still depends on the product/file version.");
            Check(HookCommands.IsOwned(HookCommands.Create(provider, provider.Events[0], RuntimePaths.Executable), provider.Id), "Protocol-based callbacks lost managed-hook identity.");
        }
        if (OperatingSystem.IsWindows())
        {
            var file = FileVersionInfo.GetVersionInfo(RuntimePaths.Executable);
            Check(file.ProductVersion!.Split('+')[0] == release, "Windows product metadata differs from the public version.");
            Check(file.FileMajorPart > 2 || file.FileMajorPart == 2 && file.FileMinorPart >= 1, "Existing version-gated callbacks would silently stop sending status.");
        }
        report.Add("PASS public/assembly/product versions, separate legacy-compatible file counter, and release-independent hook protocol.");
    }
}