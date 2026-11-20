using System.IO;
using System.Windows;
using System.Windows.Threading;
using MdViewer.Core;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Settings;

namespace MdViewer.App;

public partial class App : Application
{
    private readonly string[] _args;

    /// <summary>Requerido por el Main que genera WPF; el punto de entrada real es <see cref="Program"/>.</summary>
    public App()
        : this([])
    {
    }

    public App(string[] args)
    {
        _args = args;
    }

    public static AppPaths Paths => AppPaths.Default;

    public static SettingsStore SettingsStore { get; } = new(AppPaths.Default.SettingsFile);

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Initialize(Paths.LogsFolder);
        Log.Info($"Inicio mdViewer {typeof(App).Assembly.GetName().Version}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Excepción no controlada (dominio)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Excepción no observada en tarea", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        Settings = SettingsStore.Load();

        var fileToOpen = _args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var window = new MainWindow(fileToOpen);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        Log.Info("Salida");
        base.OnExit(e);
    }

    public static void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("No se pudo guardar la configuración", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Excepción no controlada (UI)", e.Exception);
        MessageBox.Show(
            $"Se ha producido un error inesperado:\n\n{e.Exception.Message}\n\nLos detalles están en el registro:\n{Paths.LogsFolder}",
            "mdViewer", MessageBoxButton.OK, MessageBoxImage.Error);

        // Seguimos en marcha: los borradores de recuperación protegen el trabajo del usuario.
        e.Handled = true;
    }
}
