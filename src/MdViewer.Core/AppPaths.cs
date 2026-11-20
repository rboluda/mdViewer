namespace MdViewer.Core;

/// <summary>Rutas de datos de la aplicación en el perfil del usuario.</summary>
public sealed class AppPaths
{
    public AppPaths(string roamingRoot, string localRoot)
    {
        RoamingRoot = roamingRoot;
        LocalRoot = localRoot;
    }

    /// <summary>%APPDATA%\mdViewer: configuración que viaja con el perfil.</summary>
    public string RoamingRoot { get; }

    /// <summary>%LOCALAPPDATA%\mdViewer: logs, recuperación, caché.</summary>
    public string LocalRoot { get; }

    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");
    public string ThemesFolder => Path.Combine(RoamingRoot, "themes");
    public string LogsFolder => Path.Combine(LocalRoot, "logs");
    public string RecoveryFolder => Path.Combine(LocalRoot, "recovery");
    public string WebViewDataFolder => Path.Combine(LocalRoot, "webview");

    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdViewer"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mdViewer"));
}
