using System.IO;
using System.Windows;
using System.Windows.Threading;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Documents;

namespace MdViewer.App;

/// <summary>
/// Cambios hechos por otros programas en el archivo abierto: se vigila el archivo y, si lo
/// modifican o lo borran, se pregunta qué hacer (cargar la versión del disco o quedarse con la
/// del editor). Si la ventana no está activa se espera a que el usuario vuelva a ella.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "La vigilancia se detiene al cerrar la ventana (PrepareForExit), que es su ciclo de vida.")]
public partial class MainWindow
{
    private FileSystemWatcher? _fileWatcher;
    private string? _watchedPath;
    private DispatcherTimer? _diskDebounce;
    private bool _diskCheckAgain;

    /// <summary>Vigila el archivo del documento actual (no hace nada si ya se vigila esa ruta).</summary>
    private void WatchDocumentFile()
    {
        var path = _document.FilePath;
        if (string.Equals(path, _watchedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopWatchingFile();
        _watchedPath = path;
        var folder = path is null ? null : Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            // Se vigila la carpeta filtrando por nombre: así también se detecta el archivo cuando otro
            // programa lo guarda escribiendo uno temporal y renombrándolo, o lo borra y lo vuelve a crear.
            _fileWatcher = new FileSystemWatcher(folder, Path.GetFileName(path)!)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(ScheduleDiskCheck);
            _fileWatcher.Changed += changed;
            _fileWatcher.Created += changed;
            _fileWatcher.Deleted += changed;
            _fileWatcher.Renamed += (_, _) => Dispatcher.BeginInvoke(ScheduleDiskCheck);
            _fileWatcher.Error += (_, e) => Log.Warn("Vigilancia del archivo interrumpida", e.GetException());
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Unidades que no admiten notificaciones: queda la comprobación al activar la ventana.
            Log.Warn($"No se puede vigilar {path}", ex);
            _fileWatcher?.Dispose();
            _fileWatcher = null;
        }
    }

    private void StopWatchingFile()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        _watchedPath = null;
        _diskDebounce?.Stop();
    }

    /// <summary>
    /// Los programas suelen escribir un archivo en varios pasos (varias notificaciones seguidas):
    /// se espera a que dejen de llegar antes de comprobarlo.
    /// </summary>
    private void ScheduleDiskCheck()
    {
        if (_diskDebounce is null)
        {
            _diskDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _diskDebounce.Tick += async (_, _) =>
            {
                _diskDebounce.Stop();
                await CheckExternalChangesAsync();
            };
        }

        _diskDebounce.Stop();
        _diskDebounce.Start();
    }

    private async Task CheckExternalChangesAsync()
    {
        if (!_editorReady || _closingConfirmed)
        {
            return;
        }

        if (_checkingDisk)
        {
            // Ya hay una pregunta en pantalla: al terminar se vuelve a mirar por si ha cambiado otra vez.
            _diskCheckAgain = true;
            return;
        }

        _checkingDisk = true;
        try
        {
            do
            {
                _diskCheckAgain = false;
                var change = await _document.CheckDiskAsync();
                if (change == DiskChange.None)
                {
                    continue;
                }

                if (!IsActive)
                {
                    // No se interrumpe al usuario mientras trabaja en otro programa: se pregunta al volver.
                    StatusText.Text = change == DiskChange.Deleted
                        ? $"{_document.DisplayName} se ha eliminado fuera de mdViewer"
                        : $"{_document.DisplayName} se ha modificado fuera de mdViewer";
                    return;
                }

                if (change == DiskChange.Modified)
                {
                    await AskReloadAsync();
                }
                else
                {
                    await AskKeepDeletedAsync();
                }
            }
            while (_diskCheckAgain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // El otro programa aún lo tiene bloqueado: se reintenta en un momento.
            Log.Warn("No se pudo comprobar el archivo en disco", ex);
            ScheduleDiskCheck();
        }
        finally
        {
            _checkingDisk = false;
        }
    }

    private async Task AskReloadAsync()
    {
        await SyncFromEditorAsync();
        var name = _document.DisplayName;
        var message = _document.IsDirty
            ? $"{_document.FilePath}\n\nOtro programa ha modificado este archivo y en mdViewer tienes cambios sin guardar.\n\n" +
              "¿Quieres cargar las modificaciones? Se perderán tus cambios.\n\n" +
              "Si respondes No, se mantiene tu versión y al guardar sobrescribirás la del disco."
            : $"{_document.FilePath}\n\nOtro programa ha modificado este archivo.\n\n¿Quieres cargar las modificaciones?";
        var answer = MessageBox.Show(this, message, "Archivo modificado", MessageBoxButton.YesNo,
            _document.IsDirty ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            var before = _document.Text;
            await _document.ReloadAsync();
            // Antes de recargar el editor: él sustituye este aviso por cuántas líneas han cambiado.
            StatusText.Text = $"{name} recargado con las modificaciones del disco";
            await ReloadEditorKeepingPositionAsync(before);
            DiscardRecovery();
        }
        else
        {
            _document.KeepEditorVersion();
            UpdateTitle();
            StatusText.Text = "Se mantiene tu versión; al guardar sobrescribirás la del disco";
        }
    }

    private async Task AskKeepDeletedAsync()
    {
        var answer = MessageBox.Show(this,
            $"{_document.FilePath}\n\nEste archivo ya no existe: otro programa lo ha eliminado, movido o renombrado.\n\n" +
            "¿Quieres mantenerlo abierto en el editor? Al guardar se volverá a crear.",
            "Archivo eliminado", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _document.KeepEditorVersion();
            UpdateTitle();
            StatusText.Text = "El archivo no existe en disco; guárdalo para volver a crearlo";
        }
        else
        {
            DiscardRecovery();
            await ShowDocumentAsync(MarkdownDocument.CreateNew());
        }
    }

    /// <summary>
    /// Pone en el editor el texto recargado sin volver al principio del documento y resalta unos
    /// segundos lo que ha cambiado respecto a <paramref name="previousText"/>.
    /// </summary>
    private async Task ReloadEditorKeepingPositionAsync(string previousText)
    {
        _dirtyCheckTimer.Stop();
        await _editor.ReloadValueAsync(_document.Text, previousText);
        _loadedText = _document.Text;
        _editorBaseline = await _editor.GetMarkdownAsync();
        _editorChanged = false;
        UpdateTitle();
    }
}
