using Avalonia;

namespace AIFileOrganizer.Desktop;

internal static class DesktopProgram
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--restore-all", StringComparer.OrdinalIgnoreCase))
        {
            UndoResult result = global::Program.UndoAllOrganizationsFromDesktop();
            Console.WriteLine($"Restored: {result.Restored}");
            Console.WriteLine($"Skipped: {result.Skipped}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
