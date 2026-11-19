using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed class RecoveryShortcut
{
    [DataMember] public string Id = "";
    [DataMember] public string ProcessName = "";
    [DataMember] public string ClassName = "";
    [DataMember] public string? ExecutablePath;
    [DataMember] public string Path = "";
    [DataMember(EmitDefaultValue = false)] public string? LaunchTitle;
    [DataMember(EmitDefaultValue = false)] public bool ReopenOnStartup;

    internal static bool IsShortcutPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 4096 &&
        System.IO.Path.IsPathFullyQualified(path) && System.IO.Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);

    internal bool Valid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(ProcessName) &&
        !string.IsNullOrWhiteSpace(ClassName) && IsShortcutPath(Path);

    internal bool Matches(string process, string windowClass, string? image) =>
        string.Equals(ProcessName, process, StringComparison.OrdinalIgnoreCase) &&
        WindowMatching.SameClass(ClassName, windowClass) &&
        (string.IsNullOrWhiteSpace(ExecutablePath) || string.Equals(ExecutablePath, image, StringComparison.OrdinalIgnoreCase));

    internal bool MatchesOutput(WindowCandidate candidate)
    {
        var sameApplication = Matches(candidate.ProcessName, candidate.ClassName, candidate.ExecutablePath);
        if (string.IsNullOrWhiteSpace(LaunchTitle)) return sameApplication;
        return (sameApplication || candidate.IsTerminal) && string.Equals(LaunchTitle, candidate.Title, StringComparison.Ordinal);
    }
}
