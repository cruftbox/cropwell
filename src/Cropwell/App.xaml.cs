using System.Windows;
using System.Windows.Threading;
using Cropwell.Services;

namespace Cropwell;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Unhandled exception", args.ExceptionObject as Exception);
        DispatcherUnhandledException += HandleDispatcherException;
        base.OnStartup(e);
        var window = new MainWindow();
        if (e.Args.Length > 0) window.InitialPath = e.Args[0];
        MainWindow = window;
        window.Show();
    }

    private static void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unexpected UI error", e.Exception);
        MessageBox.Show("Cropwell encountered an unexpected error. Details were written to the log.", "Cropwell", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
