using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MdViewer.Core.Diagnostics;

namespace MdViewer.App.Editor;

public enum EditorMode
{
    /// <summary>Renderizado instantáneo: la sintaxis se muestra al situar el cursor.</summary>
    Live,

    /// <summary>Código fuente con vista previa.</summary>
    Source,
}

/// <summary>
/// Puente C# ↔ editor web dentro de WebView2.
/// Mantener sincronizado con src/MdViewer.Editor/src/bridge.ts.
/// </summary>
public sealed class EditorBridge
{
    public const string AppHost = "app.mdviewer";
    public const string DocumentHost = "doc.mdviewer";

    private readonly WebView2 _webView;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _mappedDocumentFolder;

    public EditorBridge(WebView2 webView)
    {
        _webView = webView;
    }

    /// <summary>El usuario ha modificado el contenido.</summary>
    public event EventHandler? ContentChanged;

    public event EventHandler<string>? LinkActivated;

    public event EventHandler<int>? StatsChanged;

    public async Task InitializeAsync(string userDataFolder, string editorFolder)
    {
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = IsDebugBuild;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+S, Ctrl+O… los gestiona la ventana
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.SetVirtualHostNameToFolderMapping(AppHost, editorFolder, CoreWebView2HostResourceAccessKind.Deny);

        core.NavigationStarting += (_, e) =>
        {
            // El editor nunca navega fuera de su página: los enlaces se abren desde C#.
            if (!e.Uri.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                LinkActivated?.Invoke(this, e.Uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            LinkActivated?.Invoke(this, e.Uri);
        };
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += (_, e) => Log.Error($"Proceso de WebView2 caído: {e.ProcessFailedKind} ({e.Reason})");

        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core.Navigate($"https://{AppHost}/index.html");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>Carga un documento. <paramref name="documentFolder"/> resuelve imágenes y enlaces relativos.</summary>
    public async Task LoadAsync(string markdown, string? documentFolder, bool dark, EditorMode mode)
    {
        MapDocumentFolder(documentFolder);
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new { theme = dark ? "dark" : "light", mode = ToJs(mode), baseUrl = documentFolder is null ? null : $"https://{DocumentHost}/" };
        await ExecuteAsync($"window.mdEditor.load({JsonSerializer.Serialize(markdown)}, {JsonSerializer.Serialize(options)})");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task<string> GetMarkdownAsync()
    {
        var json = await ExecuteAsync("window.mdEditor.getMarkdown()");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    public Task SetThemeAsync(bool dark) => ExecuteAsync($"window.mdEditor.setTheme('{(dark ? "dark" : "light")}')");

    public async Task SetModeAsync(EditorMode mode)
    {
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await ExecuteAsync($"window.mdEditor.setMode('{ToJs(mode)}')");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public Task FocusAsync() => ExecuteAsync("window.mdEditor.focus()");

    private Task<string> ExecuteAsync(string script) => _webView.CoreWebView2.ExecuteScriptAsync(script);

    private void MapDocumentFolder(string? folder)
    {
        if (string.Equals(folder, _mappedDocumentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var core = _webView.CoreWebView2;
        if (_mappedDocumentFolder is not null)
        {
            core.ClearVirtualHostNameToFolderMapping(DocumentHost);
        }

        if (folder is not null)
        {
            core.SetVirtualHostNameToFolderMapping(DocumentHost, folder, CoreWebView2HostResourceAccessKind.Allow);
        }

        _mappedDocumentFolder = folder;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith($"https://{AppHost}/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready.TrySetResult();
                    break;
                case "changed":
                    ContentChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case "openLink":
                    LinkActivated?.Invoke(this, root.GetProperty("href").GetString() ?? string.Empty);
                    break;
                case "stats":
                    StatsChanged?.Invoke(this, root.GetProperty("characters").GetInt32());
                    break;
                case "error":
                    Log.Warn($"Error en el editor web: {root.GetProperty("message").GetString()}");
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Warn($"Mensaje del editor no válido: {e.WebMessageAsJson}", ex);
        }
    }

    private static string ToJs(EditorMode mode) => mode == EditorMode.Source ? "sv" : "ir";

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif
}
