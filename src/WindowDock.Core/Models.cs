#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WindowDock
{
    [DataContract] public sealed class WorkspaceConfig
    {
        internal const int MaxCapacity = 25;
        internal static readonly int DefaultBackgroundColor = Color.FromArgb(19, 22, 27).ToArgb();
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public int Capacity;
        [DataMember] public int Columns;
        [DataMember] public string BackgroundMode;
        [DataMember] public int BackgroundColor;
        [DataMember] public string BackgroundPath;
        [DataMember] public string Layout, OverflowFor;
        [DataMember(EmitDefaultValue = false)] public string ActivePanelId, PinnedPanelId, StackPanelId;
        [DataMember] public double MasterRatio, PrimaryRatio, DualRatio;
        [DataMember] public List<double> ColumnWeights, RowWeights;
        [DataMember(EmitDefaultValue = false)] public SplitArea CustomLayout;
        [DataMember(EmitDefaultValue = false)] public string PresentationDisplay, VirtualDesktop;
        [DataMember(EmitDefaultValue = false)] public int LayoutSlotCount;
        [DataMember(EmitDefaultValue = false)] public SurfaceStyle TabStyle;
        public static WorkspaceConfig Create(string name)
        {
            return new WorkspaceConfig { Id = Guid.NewGuid().ToString("N"), Name = name, Capacity = 15,
                Columns = 0, BackgroundMode = "solid", BackgroundColor = DefaultBackgroundColor, BackgroundPath = "",
                Layout = "grid", MasterRatio = .60, PrimaryRatio = .58, DualRatio = .5,
                ColumnWeights = new List<double>(), RowWeights = new List<double>() };
        }
    }

    [DataContract] public sealed class AppConfig
    {
        [DataMember] public int Version;
        [DataMember] public bool HoverScroll;
        [DataMember] public string ThemeId;
        [DataMember] public int MinWidth, MinHeight;
        [DataMember] public int WindowX, WindowY, WindowWidth, WindowHeight;
        [DataMember] public bool Maximized, Magnet;
        [DataMember] public string SelectedWorkspace;
        [DataMember] public List<WorkspaceConfig> Workspaces;
        [DataMember] public bool RestoreGroups, AutomaticRules;
        [DataMember] public List<WindowRule> Rules;
        [DataMember] public List<ProjectProfile> Projects;
        [DataMember] public string LastHookProject;
        [DataMember(EmitDefaultValue = false)] public Dictionary<int, string> Shortcuts;
        [DataMember(EmitDefaultValue = false)] public bool AgentNotifications, NotifyOnCompletion;
        [DataMember(EmitDefaultValue = false)] public bool RememberAgentSessions, ReopenTerminal;
        [DataMember(EmitDefaultValue = false)] public SurfaceStyle WindowStyle, HeaderStyle, TabStyle;
        [DataMember(EmitDefaultValue = false)] public string SleepMode;
        [DataMember(EmitDefaultValue = false)] public List<DisplayPlacement> DisplayPlacements;
        public static AppConfig Default()
        {
            WorkspaceConfig w = WorkspaceConfig.Create(L10n.Get("Models.Agents"));
            return new AppConfig { Version = 4, HoverScroll = true, ThemeId = "frost", RestoreGroups = true, AutomaticRules = true,
                Rules = new List<WindowRule>(), Projects = new List<ProjectProfile>(), MinWidth = 440, MinHeight = 280, WindowWidth = 1440,
                WindowHeight = 920, WindowX = Int32.MinValue, WindowY = Int32.MinValue, Workspaces = new List<WorkspaceConfig> { w }, SelectedWorkspace = w.Id };
        }
        public void Validate()
        {
            if (Version < 3) { HoverScroll = true; ThemeId = "frost"; Version = 3; }
            if (Version < 4) { RestoreGroups = true; AutomaticRules = true; Version = 4; }
            if (Rules == null) Rules = new List<WindowRule>();
            if (Projects == null) Projects = new List<ProjectProfile>();
            Shortcuts ??= new Dictionary<int, string>();
            DisplayPlacements = (DisplayPlacements ?? new List<DisplayPlacement>())
                .Where(p => p != null && p.IsValid).DistinctBy(p => p.Topology).TakeLast(12).ToList();
            Rules.RemoveAll(delegate(WindowRule rule) { return rule == null; });
            Projects.RemoveAll(delegate(ProjectProfile project) { return project == null; });
            foreach (WindowRule rule in Rules) rule.Validate();
            foreach (ProjectProfile project in Projects) project.Validate();
            if (ThemeId is not ("graphite" or "light" or "frost" or "glass" or "beams")) ThemeId = "frost";
            if (SleepMode is not ("open" or "agents" or "docked")) SleepMode = null;
            MinWidth = Math.Max(320, Math.Min(1200, MinWidth));
            MinHeight = Math.Max(200, Math.Min(900, MinHeight));
            WindowWidth = Math.Max(640, WindowWidth); WindowHeight = Math.Max(360, WindowHeight);
            if (Workspaces == null || Workspaces.Count == 0) Workspaces = Default().Workspaces;
            Workspaces.RemoveAll(delegate(WorkspaceConfig w) { return w == null; });
            if (Workspaces.Count == 0) Workspaces = Default().Workspaces;
            HashSet<string> ids = new HashSet<string>();
            foreach (WorkspaceConfig w in Workspaces)
            {
                if (String.IsNullOrEmpty(w.Id) || !ids.Add(w.Id)) { w.Id = Guid.NewGuid().ToString("N"); ids.Add(w.Id); }
                if (String.IsNullOrWhiteSpace(w.Name)) w.Name = L10n.Get("Models.Tab");
                w.Name = w.Name.Trim(); if (w.Name.Length > 48) w.Name = w.Name.Substring(0, 48);
                w.Capacity = Math.Clamp(w.Capacity, 1, WorkspaceConfig.MaxCapacity);
                w.LayoutSlotCount = Math.Clamp(w.LayoutSlotCount, 0, WorkspaceConfig.MaxCapacity);
                w.Columns = Math.Max(0, Math.Min(6, w.Columns));
                if (w.CustomLayout?.Validate() == false) w.CustomLayout = null;
                if (w.PresentationDisplay?.Length > 512) w.PresentationDisplay = null;
                if (!Guid.TryParse(w.VirtualDesktop, out var desktop) || desktop == Guid.Empty) w.VirtualDesktop = null;
                if (w.Layout is not ("grid" or "master" or "dual" or "stack" or "adaptive" or "custom")) w.Layout = "grid";
                w.MasterRatio = ConfigValues.Ratio(w.MasterRatio, .60); w.PrimaryRatio = ConfigValues.Ratio(w.PrimaryRatio, .58); w.DualRatio = ConfigValues.Ratio(w.DualRatio, .5);
                w.ColumnWeights = ConfigValues.Weights(w.ColumnWeights); w.RowWeights = ConfigValues.Weights(w.RowWeights);
                if (w.BackgroundMode != "solid" && w.BackgroundMode != "image" && w.BackgroundMode != "transparent") w.BackgroundMode = "solid";
            }
            if (!Workspaces.Exists(delegate(WorkspaceConfig w) { return w.Id == SelectedWorkspace; })) SelectedWorkspace = Workspaces[0].Id;
        }
    }

    internal static class ConfigValues
    {
        public static double Ratio(double value, double fallback) { return Double.IsNaN(value) || Double.IsInfinity(value) || value <= 0 ? fallback : Math.Max(.15, Math.Min(.85, value)); }
        public static List<double> Weights(List<double> weights)
        {
            if (weights == null || weights.Count > 25 || weights.Exists(delegate(double w) { return w <= 0 || Double.IsNaN(w) || Double.IsInfinity(w); })) return new List<double>();
            return weights;
        }
    }
    [DataContract] public sealed class WindowRecord
    {
        internal int? ManagedOpacity;
        [DataMember] public long Handle, Parent, Owner, Style, ExStyle;
        [DataMember] public bool NativeTopLevel;
        [DataMember(EmitDefaultValue = false)] public bool Linked;
        [DataMember(EmitDefaultValue = false)] public string ExecutablePath, ProjectDirectory;
        [DataMember(EmitDefaultValue = false)] public bool? Enabled;
        [DataMember] public byte[] OriginalRegion;
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks;
        [DataMember] public string Title, ProcessName, ClassName, WorkspaceId;
        [DataMember] public int X, Y, Width, Height;
        [DataMember] public int ShowCmd, PlacementFlags, MinX, MinY, MaxX, MaxY, NormalLeft, NormalTop, NormalRight, NormalBottom;
        public IntPtr Hwnd { get { return new IntPtr(Handle); } }
    }
    [DataContract] public sealed class Journal
    {
        [DataMember] public int HostPid;
        [DataMember] public long HostStartTicks;
        [DataMember] public List<WindowRecord> Windows;
    }

    internal static class Store
    {
        private static long successfulWrites;
        internal static long SuccessfulWrites => System.Threading.Interlocked.Read(ref successfulWrites);
        public static string DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        public static string ConfigPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using (FileStream f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return ContractJson<T>.Serializer.ReadObject(f) as T;
        }
        internal static byte[] Serialize<T>(T value)
        {
            using var stream = new MemoryStream(); ContractJson<T>.Serializer.WriteObject(stream, value); return stream.ToArray();
        }
        public static void Write<T>(string path, T value) => WriteBytes(path, Serialize(value));
        internal static void WriteBytes(string path, byte[] bytes, string backup = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { f.Write(bytes); f.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, backup);
                else File.Move(temporary, path);
                System.Threading.Interlocked.Increment(ref successfulWrites);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void Log(string text)
        {
            try { Directory.CreateDirectory(DirectoryPath); File.AppendAllText(Path.Combine(DirectoryPath, "WindowDock.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); } catch { }
        }
    }

    internal static class ContractJson<T>
    {
        [ThreadStatic] private static DataContractJsonSerializer serializer;
        internal static DataContractJsonSerializer Serializer => serializer ??= new DataContractJsonSerializer(typeof(T));
    }

    internal sealed class GridPlan
    {
        public int Columns, Rows;
        public Size Extent;
        public List<Rectangle> Tiles = new List<Rectangle>();
    }
    internal static class LayoutEngine
    {
        public static GridPlan Plan(int count, Size size, int minWidth, int minHeight, int columns, int gap)
        {
            GridPlan p = new GridPlan();
            if (count <= 0) { p.Extent = Size.Empty; return p; }
            int width = Math.Max(1, size.Width), height = Math.Max(1, size.Height);
            int maxCols = Math.Max(1, (width - gap) / (minWidth + gap));
            int desired = (int)Math.Ceiling(Math.Sqrt(count * (double)width / height * minHeight / minWidth));
            p.Columns = Math.Min(count, Math.Max(1, Math.Min(maxCols, columns > 0 ? columns : desired)));
            p.Rows = (count + p.Columns - 1) / p.Columns;
            int w = Math.Max(1, (width - gap * (p.Columns + 1)) / p.Columns);
            int h = Math.Max(minHeight, (height - gap * (p.Rows + 1)) / p.Rows);
            p.Extent = new Size(gap + p.Columns * (w + gap), gap + p.Rows * (h + gap));
            for (int i = 0; i < count; i++) p.Tiles.Add(new Rectangle(gap + (i % p.Columns) * (w + gap), gap + (i / p.Columns) * (h + gap), w, h));
            return p;
        }
    }
}
