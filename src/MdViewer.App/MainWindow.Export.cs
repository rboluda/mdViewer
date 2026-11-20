using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Documents;
using MdViewer.Core.Export;
using MdViewer.Core.Files;
using MdViewer.Core.Settings;
using AppTheme = MdViewer.Core.Settings.ThemeMode;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MdViewer.App;

/// <summary>Exportar (HTML, PDF, imagen, Pandoc), importar e imprimir.</summary>
public partial class MainWindow
{
    private const string KatexVersion = "0.16.9"; // la que incluye Vditor (dist/js/katex)

    private Func<Task>? _lastExport;

    private static string EditorAssetsFolder => Path.Combine(AppContext.BaseDirectory, "editor", "vditor", "dist");

    private string ExportTitle()
        => _document.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim()
           ?? (_document.FilePath is null ? "Documento" : Path.GetFileNameWithoutExtension(_document.FilePath));

    private string DefaultExportPath(string extension)
        => Path.ChangeExtension(_document.FilePath ?? Path.Combine(SidebarPanel.Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), SuggestFileName()), extension);

    private string? AskExportPath(string title, string filter, string extension)
    {
        var suggested = DefaultExportPath(extension);
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested),
            DefaultExt = extension,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    // ───────────── HTML ─────────────

    /// <summary>
    /// Documento HTML completo. <paramref name="localAssets"/>: fuentes de fórmulas desde la instalación
    /// (para PDF e imagen); si no, desde internet (para que el HTML exportado funcione en cualquier sitio).
    /// </summary>
    private async Task<string> BuildHtmlAsync(bool withStyles, bool localAssets, string? outputFolder)
    {
        await SyncFromEditorAsync();
        var body = await _editor.RenderExportAsync();
        body = FixImagePaths(body, outputFolder);
        var title = WebUtility.HtmlEncode(ExportTitle());

        if (!withStyles)
        {
            return $"<!DOCTYPE html>\n<html lang=\"es\">\n<head>\n<meta charset=\"utf-8\">\n<title>{title}</title>\n</head>\n<body>\n{body}\n</body>\n</html>\n";
        }

        var katexCss = await File.ReadAllTextAsync(Path.Combine(EditorAssetsFolder, "js", "katex", "katex.min.css"));
        var fonts = localAssets
            ? new Uri(Path.Combine(EditorAssetsFolder, "js", "katex", "fonts") + Path.DirectorySeparatorChar).AbsoluteUri
            : $"https://cdn.jsdelivr.net/npm/katex@{KatexVersion}/dist/fonts/";
        katexCss = katexCss.Replace("url(fonts/", $"url({fonts}", StringComparison.Ordinal);
        var contentCss = await File.ReadAllTextAsync(Path.Combine(EditorAssetsFolder, "css", "content-theme", EditorTheme() == "dark" ? "dark.css" : "light.css"));
        var vditorCss = await ReadVditorResetCssAsync();

        return $$"""
            <!DOCTYPE html>
            <html lang="es" data-theme="{{EditorTheme()}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="mdViewer">
            <title>{{title}}</title>
            <style>{{katexCss}}</style>
            <style>{{vditorCss}}</style>
            <style>{{contentCss}}</style>
            <style>{{ExportCss()}}</style>
            </head>
            <body>
            <article class="vditor-reset">
            {{body}}
            </article>
            </body>
            </html>
            """;
    }

    /// <summary>Estilos de contenido de Vditor (.vditor-reset) extraídos de la hoja del editor.</summary>
    private static async Task<string> ReadVditorResetCssAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "editor", "assets");
        var css = Directory.Exists(assets) ? Directory.GetFiles(assets, "*.css").FirstOrDefault() : null;
        if (css is null)
        {
            return string.Empty;
        }

        var text = await File.ReadAllTextAsync(css);
        // Solo las reglas de contenido; el resto (barra de herramientas, paneles del editor) sobra.
        var rules = Regex.Matches(text, @"[^{}]*\.vditor-reset[^{}]*\{[^{}]*\}")
            .Select(m => m.Value.Trim())
            .Where(r => !r.Contains("vditor-ir", StringComparison.Ordinal) && !r.Contains("vditor-sv", StringComparison.Ordinal) && !r.Contains("vditor-wysiwyg", StringComparison.Ordinal));
        return string.Join('\n', rules);
    }

    private static string ExportCss()
    {
        var blue = App.Settings.Theme == AppTheme.Blue;
        var dark = App.Settings.Theme == AppTheme.Dark;
        var accent = blue ? "#006ee8" : dark ? "#4ea1ff" : "#0969da";
        var accentStrong = blue ? "#0152b4" : dark ? "#7bb8ff" : "#24292f";
        var background = dark ? "#1e1e1e" : "#ffffff";
        var foreground = dark ? "#d4d4d4" : blue ? "#1c2733" : "#24292f";
        return $$"""
            html, body { background: {{background}}; color: {{foreground}}; margin: 0; }
            .vditor-reset { max-width: 860px; margin: 0 auto; padding: 40px 32px 60px; font-size: 16px; line-height: 1.7;
              font-family: "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif; color: {{foreground}}; }
            .vditor-reset h1, .vditor-reset h2, .vditor-reset h3, .vditor-reset h4, .vditor-reset h5, .vditor-reset h6 { color: {{accentStrong}}; }
            .vditor-reset a { color: {{accent}}; }
            .vditor-reset blockquote { border-left: 4px solid {{accent}}; padding: .4em 1em; }
            .vditor-reset img { max-width: 100%; }
            .vditor-reset table { border-collapse: collapse; }
            @media print { .vditor-reset { padding: 0; max-width: none; } h1, h2, h3 { break-after: avoid; } pre, table, img, blockquote { break-inside: avoid; } }
            """;
    }

    /// <summary>Las imágenes relativas se convierten en absolutas si el HTML no queda junto al documento.</summary>
    private string FixImagePaths(string html, string? outputFolder)
    {
        var documentFolder = DocumentFolder;
        if (documentFolder is null || string.Equals(outputFolder, documentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        return Regex.Replace(html, @"(<img\b[^>]*?\bsrc="")([^""]+)("")", m =>
        {
            var src = WebUtility.HtmlDecode(m.Groups[2].Value);
            if (MarkdownImages.ResolveLocal(src, documentFolder) is { } local)
            {
                return m.Groups[1].Value + new Uri(local).AbsoluteUri + m.Groups[3].Value;
            }

            return m.Value;
        }, RegexOptions.IgnoreCase);
    }

    private async Task ExportHtmlAsync(bool withStyles)
    {
        var path = AskExportPath(withStyles ? "Exportar a HTML" : "Exportar a HTML sin estilos", "Página web (*.html)|*.html", ".html");
        if (path is null)
        {
            return;
        }

        await ExportHtmlToAsync(path, withStyles);
        _lastExport = () => ExportHtmlToAsync(path, withStyles);
    }

    private async Task ExportHtmlToAsync(string path, bool withStyles)
    {
        StatusText.Text = "Exportando a HTML…";
        var html = await BuildHtmlAsync(withStyles, localAssets: false, Path.GetDirectoryName(path));
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
        Exported(path);
    }

    // ───────────── PDF, imagen e impresión (WebView2 oculto) ─────────────

    /// <summary>
    /// Carga el HTML en un WebView2 que no se ve y ejecuta la acción sobre él.
    /// Con <paramref name="render"/> se aloja en una ventana fuera de la pantalla: la captura de imagen
    /// necesita que la página se dibuje de verdad, y un WebView2 invisible no lo hace.
    /// </summary>
    private async Task WithHiddenWebViewAsync(string html, int width, Func<CoreWebView2, Task> action, bool render = false)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mdviewer-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(temp, html, new UTF8Encoding(false));
        Window? host = null;
        if (render)
        {
            host = new Window
            {
                Left = -32000,
                Top = -32000,
                Width = width,
                Height = 1000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Owner = this,
            };
            host.Show();
        }

        var hwnd = new WindowInteropHelper(host ?? this).Handle;
        var controller = await _editor.Core.Environment.CreateCoreWebView2ControllerAsync(hwnd);
        try
        {
            controller.IsVisible = render;
            controller.Bounds = new Rectangle(0, 0, width, 1000);
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            controller.CoreWebView2.Navigate(new Uri(temp).AbsoluteUri);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException("No se pudo preparar el documento para exportarlo.");
            }

            // Fuentes de las fórmulas e imágenes terminan de cargar tras el evento de navegación.
            await controller.CoreWebView2.ExecuteScriptAsync("document.fonts.ready.then(() => true)");
            await Task.Delay(400);
            await action(controller.CoreWebView2);
        }
        finally
        {
            controller.Close();
            host?.Close();
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private async Task ExportPdfAsync()
    {
        var path = AskExportPath("Exportar a PDF", "Documento PDF (*.pdf)|*.pdf", ".pdf");
        if (path is null)
        {
            return;
        }

        await ExportPdfToAsync(path);
        _lastExport = () => ExportPdfToAsync(path);
    }

    private async Task ExportPdfToAsync(string path)
    {
        StatusText.Text = "Exportando a PDF…";
        var html = await BuildHtmlAsync(withStyles: true, localAssets: true, outputFolder: null);
        await WithHiddenWebViewAsync(html, 900, async web =>
        {
            var settings = _editor.Core.Environment.CreatePrintSettings();
            var (w, h) = App.Settings.PdfPaperSize switch
            {
                PdfPaperSize.A5 => (5.83, 8.27),
                PdfPaperSize.A3 => (11.69, 16.54),
                PdfPaperSize.Letter => (8.5, 11.0),
                PdfPaperSize.Legal => (8.5, 14.0),
                _ => (8.27, 11.69),
            };
            var margin = App.Settings.PdfMarginMillimeters / 25.4;
            settings.PageWidth = w;
            settings.PageHeight = h;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = margin;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            if (!await web.PrintToPdfAsync(path, settings))
            {
                throw new IOException("No se pudo crear el PDF (¿está abierto en otro programa?).");
            }
        });
        Exported(path);
    }

    private async Task ExportImageAsync()
    {
        var path = AskExportPath("Exportar a imagen", "Imagen PNG (*.png)|*.png", ".png");
        if (path is null)
        {
            return;
        }

        await ExportImageToAsync(path);
        _lastExport = () => ExportImageToAsync(path);
    }

    private async Task ExportImageToAsync(string path)
    {
        StatusText.Text = "Exportando a imagen…";
        var html = await BuildHtmlAsync(withStyles: true, localAssets: true, outputFolder: null);
        await WithHiddenWebViewAsync(html, 900, async web =>
        {
            // Captura de la página completa con el protocolo de DevTools.
            var metrics = JsonDocument.Parse(await web.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            var width = Math.Ceiling(metrics.RootElement.GetProperty("cssContentSize").GetProperty("width").GetDouble());
            // Alto del contenido real (no de la ventana), con un margen inferior.
            var bottom = await web.ExecuteScriptAsync("Math.ceil(document.querySelector('article').getBoundingClientRect().bottom)");
            var height = Math.Clamp(double.TryParse(bottom, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 1000, 100, 30000);
            var args = JsonSerializer.Serialize(new
            {
                format = "png",
                captureBeyondViewport = true,
                clip = new { x = 0, y = 0, width, height, scale = 1.5 },
            });
            var result = JsonDocument.Parse(await web.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args).WaitAsync(TimeSpan.FromSeconds(60)));
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(result.RootElement.GetProperty("data").GetString()!));
        }, render: true);
        Exported(path);
    }

    private async Task PrintAsync()
    {
        StatusText.Text = "Preparando la impresión…";
        var html = await BuildHtmlAsync(withStyles: true, localAssets: true, outputFolder: null);
        var temp = Path.Combine(Path.GetTempPath(), $"mdviewer-print-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(temp, html, new UTF8Encoding(false));

        // El diálogo de impresión necesita una ventana visible: se usa una ventana auxiliar con vista previa.
        var window = new Window
        {
            Owner = this,
            Title = $"Imprimir — {_document.DisplayName}",
            Width = 900,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon,
        };
        var view = new Microsoft.Web.WebView2.Wpf.WebView2();
        window.Content = view;
        window.Closed += (_, _) => TryDelete(temp);
        window.Show();
        await view.EnsureCoreWebView2Async(_editor.Core.Environment);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
        view.CoreWebView2.Navigate(new Uri(temp).AbsoluteUri);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(400);
        view.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        StatusText.Text = string.Empty;
    }

    // ───────────── Pandoc ─────────────

    private string? RequirePandoc()
    {
        var pandoc = Pandoc.Locate(App.Settings.PandocPath);
        if (pandoc is null && MessageBox.Show(this,
                "Para este formato hace falta Pandoc, un conversor gratuito que no está instalado.\n\n¿Abrir su página de descarga?",
                "Pandoc no encontrado", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo("https://pandoc.org/installing.html") { UseShellExecute = true });
        }

        return pandoc;
    }

    private async Task ExportWithPandocAsync(Pandoc.Format format)
    {
        if (RequirePandoc() is not { } pandoc)
        {
            return;
        }

        var path = AskExportPath($"Exportar a {format.Name}", format.Filter, format.Extension);
        if (path is null)
        {
            return;
        }

        await ExportWithPandocToAsync(pandoc, format, path);
        _lastExport = () => ExportWithPandocToAsync(pandoc, format, path);
    }

    private async Task ExportWithPandocToAsync(string pandoc, Pandoc.Format format, string path)
    {
        StatusText.Text = $"Exportando a {format.Name} con Pandoc…";
        await SyncFromEditorAsync();
        var args = Pandoc.ExportArguments(format, path, DocumentFolder, ExportTitle());
        await Pandoc.RunAsync(pandoc, args, DocumentFolder, _document.Text);
        Exported(path);
    }

    private async Task ImportAsync()
    {
        if (RequirePandoc() is not { } pandoc)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = "Importar documento", Filter = Pandoc.ImportFilter };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardAsync())
        {
            return;
        }

        StatusText.Text = "Importando con Pandoc…";
        var input = dialog.FileName;
        var mediaFolder = Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_media");
        var markdown = await Pandoc.RunAsync(pandoc, Pandoc.ImportArguments(input, mediaFolder), Path.GetDirectoryName(input));
        await ShowDocumentAsync(MarkdownDocument.CreateUnsaved(markdown));
        StatusText.Text = $"Importado desde {Path.GetFileName(input)}. Guárdalo con Ctrl+S.";
    }

    private async Task ExportAgainAsync()
    {
        if (_lastExport is null)
        {
            StatusText.Text = "Todavía no se ha exportado este documento en esta sesión.";
            return;
        }

        await _lastExport();
    }

    private void Exported(string path)
    {
        Log.Info($"Exportado: {path}");
        StatusText.Text = $"Exportado: {path}";
        if (MessageBox.Show(this, $"Documento exportado:\n{path}\n\n¿Abrirlo ahora?", "Exportar", MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
