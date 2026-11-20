using System.IO;
using Microsoft.Win32;

namespace MdViewer.App.Integration;

/// <summary>
/// Mantiene al día la versión que muestra Windows en "Aplicaciones instaladas".
/// Velopack la actualiza en las instalaciones por usuario, pero no en la entrada que crea el
/// instalador .msi (Archivos de programa), que seguiría mostrando la versión original.
/// </summary>
public static class UninstallEntry
{
    public const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Pone <paramref name="version"/> en las entradas de mdViewer cuya ubicación de instalación es
    /// <paramref name="installRoot"/>. Devuelve cuántas se actualizaron. Las entradas ocultas
    /// (componentes internos del .msi) no se tocan.
    /// </summary>
    public static int UpdateVersion(RegistryKey hive, string installRoot, string version, string uninstallPath = UninstallPath)
    {
        using var uninstall = hive.OpenSubKey(uninstallPath, writable: true);
        if (uninstall is null)
        {
            return 0;
        }

        var root = Normalize(installRoot);
        var updated = 0;
        foreach (var name in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(name, writable: true);
            if (entry is null
                || !string.Equals(entry.GetValue("DisplayName") as string, "mdViewer", StringComparison.OrdinalIgnoreCase)
                || entry.GetValue("SystemComponent") is int hidden && hidden == 1
                || entry.GetValue("InstallLocation") is not string location
                || !string.Equals(Normalize(location), root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(entry.GetValue("DisplayVersion") as string, version, StringComparison.Ordinal))
            {
                entry.SetValue("DisplayVersion", version);
                updated++;
            }
        }

        return updated;
    }

    /// <summary>Actualiza las entradas del equipo (si hay permisos) y del usuario. Nunca lanza.</summary>
    public static void TryUpdate(string version)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                UpdateVersion(hive, root, version);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Sin permisos de administrador no se puede escribir en el equipo: no es un error.
            }
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
}
