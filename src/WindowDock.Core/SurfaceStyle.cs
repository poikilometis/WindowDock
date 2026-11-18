using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed record SurfaceStyle
{
    [DataMember(EmitDefaultValue = false)] public string? Fill { get; init; }
    [DataMember(EmitDefaultValue = false)] public uint? StartColor { get; init; }
    [DataMember(EmitDefaultValue = false)] public uint? EndColor { get; init; }
    [DataMember(EmitDefaultValue = false)] public int? Opacity { get; init; }
    [DataMember(EmitDefaultValue = false)] public uint? TitleColor { get; init; }

    internal bool HasFill => Fill is "solid" or "gradient" or "flow";

    internal static SurfaceStyle Resolve(SurfaceStyle? defaults, SurfaceStyle? local)
    {
        defaults ??= new();
        if (local is null) return defaults;
        var fill = local.HasFill || local.Fill == "theme" ? local : defaults;
        return new()
        {
            Fill = fill.Fill, StartColor = fill.StartColor, EndColor = fill.EndColor,
            Opacity = local.Opacity ?? defaults.Opacity, TitleColor = local.TitleColor ?? defaults.TitleColor
        };
    }

    internal int OpacityPercent(int minimum = 0) => Math.Clamp(Opacity ?? 100, minimum, 100);
}
