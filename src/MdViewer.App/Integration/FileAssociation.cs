using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using MdViewer.Core.Diagnostics;
using Microsoft.Win32;

namespace MdViewer.App.Integration;

/// <summary>
/// Registra mdViewer como aplicación capaz de abrir archivos Markdown.
///
/// No se apropia de la extensión (no cambia el valor por defecto de ".md"): solo se añade a
/// "Abrir con", a "Cambiar…" en Propiedades y a Configuración → Aplicaciones predeterminadas.
/// Windows reserva al usuario la elección del programa predeterminado.
/// </summary>
public static partial class FileAssociation
{
    public const string ProgId = "mdViewer.Markdown";
    private const string AppKeyName = "mdViewer";
    private const string ExeName = "mdViewer.exe";
    private const string CapabilitiesPath = @"Software\mdViewer\Capabilities";

    public static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".qmd"];

    /// <summary>Registra para el usuario actual o, si se ejecuta como administrador, para todo el equipo.</summary>
    public static void Register(string exePath)
    {
        var hive = IsElevated() ? Registry.LocalMachine : Registry.CurrentUser;
        Register(hive, exePath);
    }

    public static void Register(RegistryKey hive, string exePath)
    {
        var command = $"\"{exePath}\" \"%1\"";
        var icon = $"\"{exePath}\",0";

        // ProgID: cómo se abre y qué icono tiene un documento Markdown asociado a mdViewer.
        using (var progId = hive.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progId.SetValue(null, "Documento Markdown");
            progId.SetValue("FriendlyTypeName", "Documento Markdown");
            using (var defaultIcon = progId.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(null, icon);
            }

            using var open = progId.CreateSubKey(@"shell\open\command");
            open.SetValue(null, command);
        }

        // Cada extensión ofrece nuestro ProgID en "Abrir con" sin cambiar su programa actual.
        foreach (var extension in Extensions)
        {
            using var openWith = hive.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // Entrada de aplicación: nombre visible y tipos admitidos en el diálogo "Abrir con".
        using (var app = hive.CreateSubKey($@"Software\Classes\Applications\{ExeName}"))
        {
            app.SetValue("FriendlyAppName", "mdViewer");
            using (var open = app.CreateSubKey(@"shell\open\command"))
            {
                open.SetValue(null, command);
            }

            using (var defaultIcon = app.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(null, icon);
            }

            using var supported = app.CreateSubKey("SupportedTypes");
            foreach (var extension in Extensions)
            {
                supported.SetValue(extension, string.Empty);
            }
        }

        // Capacidades: hacen que mdViewer aparezca en Configuración → Aplicaciones predeterminadas.
        using (var capabilities = hive.CreateSubKey(CapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", "mdViewer");
            capabilities.SetValue("ApplicationDescription", "Visor y editor de Markdown");
            capabilities.SetValue("ApplicationIcon", icon);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in Extensions)
            {
                associations.SetValue(extension, ProgId);
            }
        }

        using (var registered = hive.CreateSubKey(@"Software\RegisteredApplications"))
        {
            registered.SetValue(AppKeyName, CapabilitiesPath);
        }

        NotifyShell();
    }

    /// <summary>Elimina el registro del usuario actual y, si es posible, del equipo.</summary>
    public static void Unregister()
    {
        Unregister(Registry.CurrentUser);
        if (IsElevated())
        {
            Unregister(Registry.LocalMachine);
        }

        NotifyShell();
    }

    public static void Unregister(RegistryKey hive)
    {
        hive.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        hive.DeleteSubKeyTree($@"Software\Classes\Applications\{ExeName}", throwOnMissingSubKey: false);
        hive.DeleteSubKeyTree(@"Software\mdViewer", throwOnMissingSubKey: false);

        foreach (var extension in Extensions)
        {
            using var openWith = hive.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        using var registered = hive.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        registered?.DeleteValue(AppKeyName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Ruta estable del ejecutable para el registro. En una instalación de Velopack es el lanzador
    /// de la carpeta raíz, que no cambia entre actualizaciones.
    /// </summary>
    public static string StableExePath()
    {
        var launcher = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ExeName));
        return File.Exists(launcher) ? launcher : Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ExeName);
    }

    /// <summary>Registro silencioso para el usuario actual; nunca lanza.</summary>
    public static void TryRegisterForCurrentUser()
    {
        try
        {
            Register(Registry.CurrentUser, StableExePath());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warn("No se pudo registrar mdViewer para archivos Markdown", ex);
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
