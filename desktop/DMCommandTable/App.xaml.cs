using Microsoft.UI.Xaml;

namespace DMCommandTable;

public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        UnhandledException += (_, args) => ReportStartupError(args.Exception);
        try { InitializeComponent(); }
        catch (Exception error)
        {
            ReportStartupError(error);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception error)
        {
            ReportStartupError(error);
            throw;
        }
    }

    private static void ReportStartupError(Exception error)
    {
        var directory = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.GetTempPath(), "DM Command Table");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "winui-startup-error.log"), error.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
