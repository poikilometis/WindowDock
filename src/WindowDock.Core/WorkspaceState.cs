#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WindowDock
{
    [DataContract] public sealed class SavedPanel
    {
        [DataMember] public string Id, WorkspaceId, Alias, Title, ProcessName, ClassName, AgentId;
        [DataMember] public long Handle, ProcessStartTicks, LastSeenUtc;
        [DataMember] public int ProcessId, Order;
        [DataMember] public List<string> HookSessions;
        [DataMember(EmitDefaultValue = false)] public string ProjectId, CommandId;
        [DataMember(EmitDefaultValue = false)] public string RecoveryShortcutId;
        [DataMember(EmitDefaultValue = false)] public long ActivatedUtc;
        [DataMember(EmitDefaultValue = false)] public bool Linked;
        [DataMember(EmitDefaultValue = false)] public string ExecutablePath, ProjectDirectory;
        [DataMember(EmitDefaultValue = false)] public List<SavedAgentSession> AgentSessions;
        [DataMember(EmitDefaultValue = false)] public List<TerminalTarget> TerminalTargets;
        [DataMember(EmitDefaultValue = false)] public bool KeepTerminalHistory;
        [DataMember(EmitDefaultValue = false)] public SurfaceStyle Style;
    }
    [DataContract] public sealed class GroupState
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<SavedPanel> Panels = new List<SavedPanel>();
        [DataMember] public List<string> Suppressed = new List<string>();
    }
    internal static class GroupPersistence
    {
        public static string PathName { get { return Path.Combine(Store.DirectoryPath, "groups.json"); } }
        public static GroupState Load(out string warning)
        {
            warning = null;
            GroupState state = null;
            try { state = Store.Read<GroupState>(PathName); }
            catch (Exception ex)
            {
                Store.Log("Groups: " + ex.Message);
                warning = L10n.Get("WorkspaceState.TheGroupsFileIsDamagedABackup");
                try { File.Copy(PathName, PathName + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), true); } catch { }
                try { state = Store.Read<GroupState>(PathName + ".bak"); } catch { }
            }
            if (state == null) state = new GroupState();
            if (state.Panels == null) state.Panels = new List<SavedPanel>();
            if (state.Suppressed == null) state.Suppressed = new List<string>();
            HashSet<string> ids = new HashSet<string>();
            long cutoff = DateTime.UtcNow.AddDays(-30).Ticks;
            state.Panels.RemoveAll(delegate(SavedPanel p) {
                return p == null || String.IsNullOrEmpty(p.Id) || !ids.Add(p.Id) ||
                    String.IsNullOrEmpty(p.WorkspaceId) || String.IsNullOrEmpty(p.ProcessName) || p.LastSeenUtc < cutoff;
            });
            if (state.Suppressed.Count > 1000) state.Suppressed = state.Suppressed.Skip(state.Suppressed.Count - 1000).ToList();
            return state;
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(long Length,DateTime Stamp)> validated = new();
        public static void Save(GroupState state) => SaveBytes(PathName, Store.Serialize(state));
        internal static void SaveBytes(string path, byte[] bytes)
        {
            bool valid = false;
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                if (validated.TryGetValue(path, out var previous) && previous.Length == file.Length && previous.Stamp == file.LastWriteTimeUtc) valid = true;
                else try { valid = Store.Read<GroupState>(path) != null; } catch { }
            }
            // File.Replace atomically keeps the previous valid revision as the backup.
            Store.WriteBytes(path, bytes, valid ? path + ".bak" : null);
            var written = new FileInfo(path); validated[path] = (written.Length, written.LastWriteTimeUtc);
        }
        public static T Clone<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer = ContractJson<T>.Serializer;
                serializer.WriteObject(stream, value); stream.Position = 0; return (T)serializer.ReadObject(stream);
            }
        }
    }
    internal static class WindowMatching
    {
        public static long StartTicks(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return p.StartTime.ToUniversalTime().Ticks; }
            catch { return 0; }
        }
        public static string Identity(WindowCandidate window)
        {
            return window.ProcessId + ":" + (window.ProcessStartTicks!=0?window.ProcessStartTicks:StartTicks(window.ProcessId)) + ":" + window.Handle.ToInt64();
        }
        public static string Identity(WindowRecord window)
        {
            return window.ProcessId + ":" + window.ProcessStartTicks + ":" + window.Handle;
        }
        public static int Score(SavedPanel saved, WindowCandidate candidate)
        {
            if (!String.Equals(saved.ProcessName, candidate.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                !SameClass(saved.ClassName, candidate.ClassName)) return 0;
            bool sameProcess = saved.ProcessId == candidate.ProcessId && saved.ProcessStartTicks != 0 &&
                saved.ProcessStartTicks == (candidate.ProcessStartTicks!=0?candidate.ProcessStartTicks:StartTicks(candidate.ProcessId));
            if (sameProcess && saved.Handle == candidate.Handle.ToInt64()) return 1000;
            if (!string.IsNullOrWhiteSpace(saved.ExecutablePath) && !string.IsNullOrWhiteSpace(candidate.ExecutablePath) &&
                !string.Equals(saved.ExecutablePath, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return 0;
            bool title = String.Equals(saved.Title, candidate.Title, StringComparison.Ordinal);
            if (sameProcess && title) return 900;
            if (!string.IsNullOrWhiteSpace(saved.ProjectDirectory) && !string.IsNullOrWhiteSpace(candidate.ProjectDirectory))
                return AgentResume.SameDirectory(saved.ProjectDirectory, candidate.ProjectDirectory) ? title ? 800 : 700 : 0;
            return title && !String.IsNullOrWhiteSpace(saved.Title) ? 500 : 0;
        }

        internal static bool SameClass(string left, string right) => string.Equals(left, right, StringComparison.Ordinal) ||
            (AvaloniaClass(left) && AvaloniaClass(right));

        // Avalonia registers a new window-class GUID in each process.
        private static bool AvaloniaClass(string name) => name is not null &&
            name.StartsWith("Avalonia-", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(9), "D", out _);
        internal static IEnumerable<(SavedPanel Saved, WindowCandidate Window)> RestoreMatches(
            IReadOnlyList<SavedPanel> roles, IReadOnlyList<WindowCandidate> windows)
        {
            // A window must be the unique best candidate for its role, and vice versa.
            // This keeps identical terminal titles from taking the first saved slot by accident.
            var bestByRole = new (WindowCandidate Window, int Score, bool Tied)[roles.Count];
            var bestByWindow = new Dictionary<nint, (int Role, int Score, bool Tied)>();
            for (var index = 0; index < roles.Count; index++)
            {
                foreach (var window in windows)
                {
                    var score = Score(roles[index], window);
                    if (score == 0) continue;
                    var role = bestByRole[index];
                    if (score > role.Score) bestByRole[index] = (window, score, false);
                    else if (score == role.Score) bestByRole[index].Tied = true;
                    var best = bestByWindow.GetValueOrDefault(window.Handle);
                    if (score > best.Score) bestByWindow[window.Handle] = (index, score, false);
                    else if (score == best.Score) bestByWindow[window.Handle] = (best.Role, best.Score, true);
                }
            }
            for (var index = 0; index < roles.Count; index++)
            {
                var best = bestByRole[index];
                if (best.Window is null || best.Tied) continue;
                var reverse = bestByWindow[best.Window.Handle];
                if (reverse.Role == index && !reverse.Tied) yield return (roles[index], best.Window);
            }
        }
    }
}
