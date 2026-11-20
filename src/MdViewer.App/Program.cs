using Velopack;

namespace MdViewer.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Debe ser lo primero: Velopack gestiona aquí los eventos de instalación,
        // actualización y desinstalación (y puede terminar el proceso).
        VelopackApp.Build().Run();

        var app = new App(args);
        app.InitializeComponent();
        return app.Run();
    }
}
