using System.Drawing;
namespace WindowDock;
internal static partial class Native
{
    public static bool MatchesRecord(WindowRecord record) => NativeIdentity.Matches(record);
    public static IReadOnlyList<Rectangle> WorkAreas() => WindowsScreens.WorkAreas();
    public static Rectangle PrimaryWorkArea() => WorkAreas()[0];
}