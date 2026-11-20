using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MdViewer.Core.Diagnostics;

namespace MdViewer.App.Editor;

public enum EditorMode
{
    /// <summary>Edición en vivo: la sintaxis se muestra al situar el cursor.</summary>
    Live,

    /// <summary>Código fuente.</summary>
    Source,

    /// <summary>Código fuente con vista previa al lado (pantalla partida).</summary>
    Split,
}

public sealed record OutlineItem(int Level, string Text);

public sealed record DocumentStats(int Words, int Characters, int CharactersNoSpaces, int Lines, int Paragraphs, int ReadingMinutes);

public sealed record FindResult(int Count, int Current, string? Error);

public sealed record WritingOptions(bool FocusMode, bool TypewriterMode, bool SmartPunctuation, bool AutoPair, bool Spellcheck);

/// <summary>
/// Puente C# ↔ editor web dentro de WebView2.
/// Mantener sincronizado con src/MdViewer.Editor/src/bridge.ts.
/// </summary>
public sealed class EditorBridge
{
    public const string AppHost = "app.mdviewer";
    public const string DocumentHost = "doc.mdviewer";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WebView2 _webView;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _exports = new();
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _mappedDocumentFolder;

    public EditorBridge(WebView2 webView)
    {
        _webView = webView;
    }

    /// <summary>El usuario ha modificado el contenido.</summary>
    public event EventHandler? ContentChanged;

    public event EventHandler<string>? LinkActivated;

    public event EventHandler<DocumentStats>? StatsChanged;

    public event EventHandler<IReadOnlyList<OutlineItem>>? OutlineChanged;

    public event EventHandler<int>? ActiveHeadingChanged;

    /// <summary>Se ha pegado o arrastrado una imagen: nombre y contenido.</summary>
    public event EventHandler<(string Name, byte[] Data)>? ImageReceived;

    public CoreWebView2 Core => _webView.CoreWebView2;

    public async Task InitializeAsync(string userDataFolder, string editorFolder)
    {
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = IsDebugBuild;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // los atajos los gestiona la ventana
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

    // ───────────── Documento ─────────────

    /// <summary>Carga un documento. <paramref name="documentFolder"/> resuelve imágenes y enlaces relativos.</summary>
    public async Task LoadAsync(string markdown, string? documentFolder, string theme, EditorMode mode,
        WritingOptions writing, bool readOnly, bool toolbar)
    {
        MapDocumentFolder(documentFolder);
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new
        {
            theme,
            mode = ToJs(mode),
            baseUrl = documentFolder is null ? null : $"https://{DocumentHost}/",
            writing,
            @readonly = readOnly,
            toolbar,
        };
        await CallAsync("load", markdown, options);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public Task<string> GetMarkdownAsync() => CallStringAsync("getMarkdown");

    public Task SetValueAsync(string markdown) => CallAsync("setValue", markdown);

    public Task SetThemeAsync(string theme) => CallAsync("setTheme", theme);

    public async Task SetModeAsync(EditorMode mode)
    {
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await CallAsync("setMode", ToJs(mode));
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public Task FocusAsync() => CallAsync("focus");

    // ───────────── Comandos ─────────────

    public Task ExecAsync(string command, string? argument = null)
        => argument is null ? CallAsync("exec", command) : CallAsync("exec", command, argument);

    public Task<string> SelectionMarkdownAsync() => CallStringAsync("selectionMarkdown");

    public Task<string> SelectionTextAsync() => CallStringAsync("selectionText");

    public Task<string> SelectionHtmlAsync() => CallStringAsync("selectionHtml");

    public Task<string> MarkdownToHtmlAsync(string markdown) => CallStringAsync("markdownToHtml", markdown);

    public Task<string> LinkAtCursorAsync() => CallStringAsync("linkAtCursor");

    public Task<string> ImageAtCursorAsync() => CallStringAsync("imageAtCursor");

    public Task<string> CodeAtCursorAsync() => CallStringAsync("codeAtCursor");

    public Task PasteAsync(string text, string? html) => CallAsync("paste", text, html);

    public Task InsertTextAsync(string text) => CallAsync("insertText", text);

    public Task InsertMarkdownAsync(string markdown) => CallAsync("insertMarkdown", markdown);

    public Task ScrollToHeadingAsync(int index) => CallAsync("scrollToHeading", index);

    public Task SetWritingAsync(WritingOptions options) => CallAsync("setWriting", options);

    public Task SetReadonlyAsync(bool value) => CallAsync("setReadonly", value);

    public Task SetToolbarAsync(bool visible) => CallAsync("setToolbar", visible);

    // ───────────── Buscar ─────────────

    public async Task<FindResult> FindAsync(string query, bool caseSensitive, bool regex, bool wholeWord)
        => Parse<FindResult>(await CallRawAsync("find", query, new { caseSensitive, regex, wholeWord }))
           ?? new FindResult(0, 0, null);

    public async Task<FindResult> FindStepAsync(int direction)
        => Parse<FindResult>(await CallRawAsync("findStep", direction)) ?? new FindResult(0, 0, null);

    public Task FindClearAsync() => CallAsync("findClear");

    public Task ReplaceCurrentAsync(string replacement) => CallAsync("replaceCurrent", replacement);

    public async Task<int> ReplaceAllAsync(string replacement)
        => Parse<int>(await CallRawAsync("replaceAll", replacement));

    // ───────────── Exportar ─────────────

    /// <summary>HTML completamente renderizado (fórmulas y diagramas incluidos) del documento actual.</summary>
    public async Task<string> RenderExportAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _exports[id] = completion;
        try
        {
            await CallAsync("renderExport", id);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            _exports.TryRemove(id, out _);
        }
    }

    // ───────────── Vista ─────────────

    public double ZoomFactor
    {
        get => _webView.ZoomFactor;
        set => _webView.ZoomFactor = Math.Clamp(value, 0.5, 3.0);
    }

    public void OpenDevTools()
    {
        Core.Settings.AreDevToolsEnabled = true;
        Core.OpenDevToolsWindow();
    }

    // ───────────── Interno ─────────────

    private static string Script(string function, object?[] arguments)
        => $"window.mdEditor.{function}({string.Join(", ", arguments.Select(a => JsonSerializer.Serialize(a, Json)))})";

    private Task<string> CallRawAsync(string function, params object?[] arguments)
        => _webView.CoreWebView2.ExecuteScriptAsync(Script(function, arguments));

    private Task<string> CallAsync(string function, params object?[] arguments) => CallRawAsync(function, arguments);

    private async Task<string> CallStringAsync(string function, params object?[] arguments)
        => Parse<string>(await CallRawAsync(function, arguments)) ?? string.Empty;

    private static T? Parse<T>(string json)
    {
        try
        {
            return string.IsNullOrEmpty(json) || json == "null" ? default : JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

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
                    if (root.GetProperty("stats").Deserialize<DocumentStats>(Json) is { } stats)
                    {
                        StatsChanged?.Invoke(this, stats);
                    }

                    break;
                case "outline":
                    OutlineChanged?.Invoke(this, root.GetProperty("items").Deserialize<List<OutlineItem>>(Json) ?? []);
                    break;
                case "activeHeading":
                    ActiveHeadingChanged?.Invoke(this, root.GetProperty("index").GetInt32());
                    break;
                case "image":
                    ImageReceived?.Invoke(this, (root.GetProperty("name").GetString() ?? "imagen.png",
                        Convert.FromBase64String(root.GetProperty("data").GetString() ?? string.Empty)));
                    break;
                case "export":
                    if (_exports.TryGetValue(root.GetProperty("id").GetString() ?? string.Empty, out var completion))
                    {
                        completion.TrySetResult(root.GetProperty("html").GetString() ?? string.Empty);
                    }

                    break;
                case "error":
                    Log.Warn($"Error en el editor web: {root.GetProperty("message").GetString()}");
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warn($"Mensaje del editor no válido: {e.WebMessageAsJson[..Math.Min(300, e.WebMessageAsJson.Length)]}", ex);
        }
    }

    private static string ToJs(EditorMode mode) => mode switch
    {
        EditorMode.Source => "sv",
        EditorMode.Split => "split",
        _ => "ir",
    };

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif
}
