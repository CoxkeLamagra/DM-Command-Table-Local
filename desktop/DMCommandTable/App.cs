using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DMCommandTable;

public sealed class App : Application
{
    private MainWindow? window;

    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(startup =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    public App() => UnhandledException += (_, args) => ReportStartupError(args.Exception);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
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
