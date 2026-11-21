using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinPulse.Core;
using WinPulse.ViewModels;

namespace WinPulse;

public partial class App : Application
{
    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();
        Log.Install();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled AppDomain exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.Run(e.Args);
            Shutdown(Environment.ExitCode);
            return;
        }

        if (e.Args.Any(a =>
                a.Equals("--check-updates", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--update", StringComparison.OrdinalIgnoreCase)))
        {
            var exitCode = 1;
            try
            {
                AppHost.Initialize();
                exitCode = UpdateCli.Run(e.Args);
            }
            catch (Exception ex)
            {
                Log.Error("Update CLI failed", ex);
                Console.WriteLine("FAILED: " + ex.Message);
            }
            finally
            {
                AppHost.Shutdown();
            }

            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
            return;
        }

        _instanceMutex = new Mutex(true, @"Global\WinPulse.SingleInstance", out var isNewInstance);
        if (!isNewInstance)
        {
            WindowActivator.ActivateExisting();
            Shutdown(0);
            return;
        }

        try
        {
            AppHost.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show(
                "WinPulse could not start.\n\n" + ex.Message,
                "WinPulse", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        if (e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            window.WindowState = WindowState.Minimized;
        }

        viewModel.OnStarted();

        var viewArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--view=", StringComparison.OrdinalIgnoreCase));
        if (viewArgument is not null)
        {
            viewModel.NavigateTo(NormalizeViewName(viewArgument["--view=".Length..]));
        }
    }

    private static string NormalizeViewName(string value) => value.Trim().ToLowerInvariant() switch
    {
        "files" or "file" or "activity" or "file activity" => "File activity",
        "processes" or "process" or "tasks" => "Processes",
        "apps" or "applications" or "programs" => "Apps",
        "cleanup" or "clean" => "Cleanup",
        "settings" or "preferences" => "Settings",
        _ => "Dashboard",
    };

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            AppHost.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Shutdown error", ex);
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "Something went wrong, but WinPulse is still running.\n\n" + e.Exception.Message +
            "\n\nDetails were written to:\n" + AppPaths.LogFile,
            "WinPulse", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
