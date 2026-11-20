using MdViewer.App.Integration;
using MdViewer.Core;
using MdViewer.Core.Diagnostics;
using Velopack;

namespace MdViewer.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Debe ser lo primero: Velopack gestiona aquí los eventos de instalación,
        // actualización y desinstalación (y puede terminar el proceso).
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => RunHook("instalación", () => FileAssociation.Register(FileAssociation.StableExePath())))
            .OnAfterUpdateFastCallback(_ => RunHook("actualización", () => FileAssociation.Register(FileAssociation.StableExePath())))
            .OnBeforeUninstallFastCallback(_ => RunHook("desinstalación", FileAssociation.Unregister))
            .Run();

        // Uso manual o desde scripts: mdViewer.exe --register-file-types | --unregister-file-types
        if (args.Contains("--register-file-types", StringComparer.OrdinalIgnoreCase))
        {
            return RunHook("registro manual", () => FileAssociation.Register(FileAssociation.StableExePath()));
        }

        if (args.Contains("--unregister-file-types", StringComparer.OrdinalIgnoreCase))
        {
            return RunHook("baja manual", FileAssociation.Unregister);
        }

        var app = new App(args);
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunHook(string name, Action action)
    {
        Log.Initialize(AppPaths.Default.LogsFolder);
        try
        {
            action();
            Log.Info($"Asociación de archivos Markdown ({name}): correcta");
            return 0;
        }
        catch (Exception ex)
        {
            // Un fallo aquí nunca debe impedir instalar, actualizar o desinstalar.
            Log.Error($"Asociación de archivos Markdown ({name}): error", ex);
            return 1;
        }
    }
}
