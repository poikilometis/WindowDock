using System.Drawing;
namespace WindowDock;
internal static partial class Native
{
    public static bool MatchesRecord(WindowRecord record) => NativeIdentity.Matches(record);
    public static IReadOnlyList<Rectangle> WorkAreas() => WindowsScreens.WorkAreas();
    public static Rectangle PrimaryWorkArea() => WorkAreas()[0];
    internal static bool ClientAnimationsEnabled()
    {
        const uint getClientAreaAnimation = 0x1042;
        return !SystemParametersInfo(getClientAreaAnimation, 0, out var enabled, 0) || enabled != 0;
    }
}
