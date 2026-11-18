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
                !String.Equals(saved.ClassName, candidate.ClassName, StringComparison.Ordinal)) return 0;
            bool sameProcess = saved.ProcessId == candidate.ProcessId && saved.ProcessStartTicks != 0 &&
                saved.ProcessStartTicks == (candidate.ProcessStartTicks!=0?candidate.ProcessStartTicks:StartTicks(candidate.ProcessId));
            if (sameProcess && saved.Handle == candidate.Handle.ToInt64()) return 1000;
            if (!string.IsNullOrWhiteSpace(saved.ExecutablePath) && !string.IsNullOrWhiteSpace(candidate.ExecutablePath) &&
                !string.Equals(saved.ExecutablePath, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return 0;
            bool title = String.Equals(saved.Title, candidate.Title, StringComparison.Ordinal);
            if (sameProcess && title) return 900;
            if (!string.IsNullOrWhiteSpace(saved.ProjectDirectory))
                return AgentResume.SameDirectory(saved.ProjectDirectory, candidate.ProjectDirectory) ? title ? 800 : 700 : 0;
            return title && !String.IsNullOrWhiteSpace(saved.Title) ? 500 : 0;
        }
        public static WindowCandidate UniqueMatch(SavedPanel saved, IEnumerable<WindowCandidate> candidates)
        {
            WindowCandidate best = null; int score = 0; bool ambiguous = false;
            foreach (WindowCandidate candidate in candidates)
            {
                int next = Score(saved, candidate);
                if (next > score) { best = candidate; score = next; ambiguous = false; }
                else if (next > 0 && next == score) ambiguous = true;
            }
            return ambiguous ? null : best;
        }
    }
}
