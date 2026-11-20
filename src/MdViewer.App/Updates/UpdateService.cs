using MdViewer.Core.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace MdViewer.App.Updates;

/// <summary>
/// Actualizaciones desde GitHub Releases mediante Velopack.
/// Solo funciona en una instalación hecha con el instalador (no al ejecutar desde Visual Studio).
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/rboluda/mdViewer";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Versión descargada y lista para aplicar, si la hay.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Busca y descarga en segundo plano. Devuelve true si hay una actualización lista.</summary>
    public async Task<bool> CheckAndDownloadAsync()
    {
        if (!IsInstalled)
        {
            return false;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return false;
            }

            await _manager.DownloadUpdatesAsync(update);
            _pending = update;
            Log.Info($"Actualización descargada: {PendingVersion}");
            return true;
        }
        catch (Exception ex)
        {
            // Sin conexión, GitHub caído, límite de API… nunca debe afectar al uso normal.
            Log.Warn("No se pudo comprobar si hay actualizaciones", ex);
            return false;
        }
    }

    /// <summary>Aplica la actualización descargada y reinicia la aplicación.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
    }

    /// <summary>Aplica la actualización al cerrar la aplicación, sin reiniciarla.</summary>
    public void ApplyOnExit()
    {
        if (_pending is not null)
        {
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
        }
    }
}
