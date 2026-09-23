using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using AIFileOrganizer.Desktop.Views;

namespace AIFileOrganizer.Desktop;

public partial class App : Application
{
    internal static (string Folder, string OutputPath)? DemoCapture { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            if (DemoCapture is not null)
                window.Height = 900;
            desktop.MainWindow = window;
            if (DemoCapture is { } capture)
            {
                window.Opened += async (_, _) =>
                {
                    try
                    {
                        string databasePath = Path.Combine(
                            Path.GetTempPath(),
                            "AIFileOrganizer-demo-" + Guid.NewGuid().ToString("N") + ".db");
                        List<MovePlan> plans = await Program.ScanFromDesktopAsync(
                            new[] { capture.Folder },
                            20,
                            databasePath,
                            new Uri("http://localhost:11434"));
                        window.ShowDemoPreview(capture.Folder, plans);
                        await Task.Delay(250);
                        using RenderTargetBitmap bitmap = new(new PixelSize(
                            (int)window.ClientSize.Width,
                            (int)window.ClientSize.Height));
                        bitmap.Render(window);
                        bitmap.Save(capture.OutputPath, PngBitmapEncoderOptions.Default);
                        Console.WriteLine($"Saved demo screenshot: {capture.OutputPath}");
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine(error);
                        Environment.ExitCode = 1;
                    }
                    finally
                    {
                        desktop.Shutdown();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
