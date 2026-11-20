using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Files;
using MdViewer.Core.Settings;
using Microsoft.Win32;

namespace MdViewer.App;

/// <summary>Imágenes: insertar, pegar, copiar, mover y descargar.</summary>
public partial class MainWindow
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".avif"];

    private const string ImageFilter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.avif|Todos los archivos (*.*)|*.*";

    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private string? DocumentFolder => _document.FilePath is null ? null : Path.GetDirectoryName(_document.FilePath);

    /// <summary>Para copiar imágenes junto al documento hace falta que esté guardado.</summary>
    private async Task<bool> EnsureSavedForImagesAsync()
    {
        if (_document.FilePath is not null)
        {
            return true;
        }

        if (MessageBox.Show(this, "Para guardar imágenes junto al documento, primero hay que guardarlo.\n\n¿Guardarlo ahora?", "mdViewer",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
        {
            return false;
        }

        return await SaveAsync();
    }

    private async Task InsertImageFromFileAsync()
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter, Multiselect = true, Title = "Insertar imagen" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            await InsertFileAsync(file);
        }
    }

    /// <summary>Inserta un archivo soltado o pegado: las imágenes como imagen y el resto como enlace.</summary>
    private async Task InsertFileAsync(string path)
    {
        if (!_editorReady || _readOnly || !File.Exists(path))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (!IsImage(path))
        {
            var link = MarkdownImages.ToMarkdownPath(path, DocumentFolder, App.Settings.PreferRelativeImagePaths);
            await _editor.InsertMarkdownAsync($"[{name}]({link})");
            return;
        }

        var finalPath = path;
        if (App.Settings.ImageInsertPolicy != ImageInsertPolicy.KeepInPlace)
        {
            if (!await EnsureSavedForImagesAsync())
            {
                return;
            }

            var target = MarkdownImages.TargetFolder(App.Settings, _document.FilePath!)!;
            if (!Path.GetDirectoryName(path)!.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(target);
                finalPath = MarkdownImages.UniquePath(target, Path.GetFileName(path));
                File.Copy(path, finalPath);
            }
        }

        var src = MarkdownImages.ToMarkdownPath(finalPath, DocumentFolder, App.Settings.PreferRelativeImagePaths);
        await _editor.InsertMarkdownAsync($"![{name}]({src})");
    }

    /// <summary>Imagen pegada o arrastrada desde el editor: se guarda como archivo y se inserta.</summary>
    private async Task InsertImageDataAsync(string name, byte[] data)
    {
        if (!_editorReady || _readOnly || data.Length == 0 || !await EnsureSavedForImagesAsync())
        {
            return;
        }

        var target = MarkdownImages.TargetFolder(App.Settings, _document.FilePath!)
                     ?? Path.Combine(DocumentFolder!, "assets");
        Directory.CreateDirectory(target);
        if (name is "image.png" or "imagen.png" || string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(name)))
        {
            name = $"imagen-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        }

        var path = MarkdownImages.UniquePath(target, name);
        await File.WriteAllBytesAsync(path, data);
        var src = MarkdownImages.ToMarkdownPath(path, DocumentFolder, App.Settings.PreferRelativeImagePaths);
        await _editor.InsertMarkdownAsync($"![{Path.GetFileNameWithoutExtension(path)}]({src})");
        StatusText.Text = $"Imagen guardada en {path}";
    }

    private static void SetImagePolicy(ImageInsertPolicy policy)
    {
        App.Settings.ImageInsertPolicy = policy;
        App.SaveSettings();
    }

    private void ChooseCustomImageFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Carpeta para las imágenes", InitialDirectory = DocumentFolder };
        if (dialog.ShowDialog(this) == true)
        {
            App.Settings.ImageCustomFolder = dialog.FolderName;
            SetImagePolicy(ImageInsertPolicy.CopyToCustomFolder);
        }
    }

    private async Task<string?> ImageAtCursorPathAsync()
    {
        var src = await _editor.ImageAtCursorAsync();
        if (string.IsNullOrEmpty(src))
        {
            StatusText.Text = "Sitúa el cursor sobre una imagen.";
            return null;
        }

        if (src.StartsWith($"https://{Editor.EditorBridge.DocumentHost}/", StringComparison.OrdinalIgnoreCase))
        {
            src = Uri.UnescapeDataString(src[$"https://{Editor.EditorBridge.DocumentHost}/".Length..]);
        }

        return src;
    }

    private async Task CopyImageAsync()
    {
        if (await ImageAtCursorPathAsync() is not { } src)
        {
            return;
        }

        byte[] bytes;
        if (MarkdownImages.IsRemote(src))
        {
            bytes = await Http.GetByteArrayAsync(new Uri(src));
        }
        else if (MarkdownImages.ResolveLocal(src, DocumentFolder) is { } local && File.Exists(local))
        {
            bytes = await File.ReadAllBytesAsync(local);
        }
        else
        {
            StatusText.Text = "No se encuentra el archivo de la imagen.";
            return;
        }

        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        SafeClipboard.SetImage(image);
        StatusText.Text = "Imagen copiada al portapapeles";
    }

    private async Task OpenImageLocationAsync()
    {
        if (await ImageAtCursorPathAsync() is not { } src)
        {
            return;
        }

        if (MarkdownImages.IsRemote(src))
        {
            OpenLink(src);
        }
        else if (MarkdownImages.ResolveLocal(src, DocumentFolder) is { } local && File.Exists(local))
        {
            Process.Start("explorer.exe", $"/select,\"{local}\"");
        }
        else
        {
            StatusText.Text = "No se encuentra el archivo de la imagen.";
        }
    }

    /// <summary>Mueve o copia todas las imágenes locales del documento a otra carpeta y actualiza las rutas.</summary>
    private async Task RelocateAllImagesAsync(bool move)
    {
        if (!await EnsureSavedForImagesAsync())
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = move ? "Mover las imágenes a" : "Copiar las imágenes a", InitialDirectory = DocumentFolder };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await SyncFromEditorAsync();
        var replacements = new Dictionary<MarkdownImages.ImageReference, string>();
        var done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(dialog.FolderName);
        foreach (var image in MarkdownImages.Find(_document.Text))
        {
            if (MarkdownImages.ResolveLocal(image.Source, DocumentFolder) is not { } local || !File.Exists(local))
            {
                continue;
            }

            if (!done.TryGetValue(local, out var destination))
            {
                destination = Path.GetDirectoryName(local)!.Equals(dialog.FolderName, StringComparison.OrdinalIgnoreCase)
                    ? local
                    : MarkdownImages.UniquePath(dialog.FolderName, Path.GetFileName(local));
                if (destination != local)
                {
                    if (move)
                    {
                        File.Move(local, destination);
                    }
                    else
                    {
                        File.Copy(local, destination);
                    }
                }

                done[local] = destination;
            }

            replacements[image] = MarkdownImages.ToMarkdownPath(destination, DocumentFolder, App.Settings.PreferRelativeImagePaths);
        }

        if (replacements.Count == 0)
        {
            StatusText.Text = "El documento no tiene imágenes locales.";
            return;
        }

        await ReplaceDocumentTextAsync(MarkdownImages.Rewrite(_document.Text, replacements));
        StatusText.Text = $"{done.Count} imágenes {(move ? "movidas" : "copiadas")} a {dialog.FolderName}";
    }

    /// <summary>Descarga las imágenes de internet a la carpeta de imágenes del documento.</summary>
    private async Task DownloadRemoteImagesAsync()
    {
        if (!await EnsureSavedForImagesAsync())
        {
            return;
        }

        await SyncFromEditorAsync();
        var target = MarkdownImages.TargetFolder(App.Settings, _document.FilePath!) ?? Path.Combine(DocumentFolder!, "assets");
        var replacements = new Dictionary<MarkdownImages.ImageReference, string>();
        var failed = 0;
        foreach (var image in MarkdownImages.Find(_document.Text).Where(i => MarkdownImages.IsRemote(i.Source)))
        {
            try
            {
                StatusText.Text = $"Descargando {image.Source}…";
                var uri = new Uri(image.Source);
                var bytes = await Http.GetByteArrayAsync(uri);
                Directory.CreateDirectory(target);
                var name = Path.GetFileName(uri.LocalPath);
                var path = MarkdownImages.UniquePath(target, string.IsNullOrWhiteSpace(name) ? "imagen.png" : name);
                await File.WriteAllBytesAsync(path, bytes);
                replacements[image] = MarkdownImages.ToMarkdownPath(path, DocumentFolder, App.Settings.PreferRelativeImagePaths);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or IOException)
            {
                failed++;
                Log.Warn($"No se pudo descargar {image.Source}", ex);
            }
        }

        if (replacements.Count > 0)
        {
            await ReplaceDocumentTextAsync(MarkdownImages.Rewrite(_document.Text, replacements));
        }

        StatusText.Text = replacements.Count == 0 && failed == 0
            ? "El documento no tiene imágenes de internet."
            : $"{replacements.Count} imágenes descargadas" + (failed > 0 ? $", {failed} con error" : string.Empty);
    }

    private async Task ReloadImagesAsync()
    {
        await SyncFromEditorAsync();
        await _editor.Core.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
        await _editor.SetValueAsync(_document.Text);
        StatusText.Text = "Imágenes recargadas";
    }
}
