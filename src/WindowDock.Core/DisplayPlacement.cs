using System.Drawing;
using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed class DisplayPlacement
{
    [DataMember] public string Topology { get; set; } = "";
    [DataMember] public string Display { get; set; } = "";
    [DataMember] public double Left { get; set; }
    [DataMember] public double Top { get; set; }
    [DataMember] public double Width { get; set; }
    [DataMember] public double Height { get; set; }
    [DataMember] public bool Maximized { get; set; }

    internal bool IsValid => !string.IsNullOrEmpty(Topology) && Topology.Length <= 2000 &&
        double.IsFinite(Left) && double.IsFinite(Top) && Width is > 0 and < 20000 && Height is > 0 and < 20000;

    internal Rectangle Restore(Rectangle area, double scale)
    {
        var width = (int)Math.Clamp(Math.Round(Width * scale), 1, area.Width);
        var height = (int)Math.Clamp(Math.Round(Height * scale), 1, area.Height);
        var x = (int)Math.Clamp(Math.Round(Left * scale), 0, area.Width - width);
        var y = (int)Math.Clamp(Math.Round(Top * scale), 0, area.Height - height);
        return new Rectangle(area.X + x, area.Y + y, width, height);
    }
}
