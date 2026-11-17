namespace WindowDock;

internal static class RuntimePaths
{
    public static string ProductVersion => typeof(RuntimePaths).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    public static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException(L10n.Get("RuntimePaths.CouldNotDetermineTheApplicationPath"));
    public static string[] ProcessArguments(params string[] arguments) => arguments;
}