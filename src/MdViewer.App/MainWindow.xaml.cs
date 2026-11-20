using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MdViewer.App.Editor;
using MdViewer.App.Integration;
using MdViewer.App.Theming;
using MdViewer.App.Updates;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Documents;
using MdViewer.Core.Files;
using MdViewer.Core.Recovery;
using MdViewer.Core.Settings;
using Microsoft.Win32;

namespace MdViewer.App;

/// <summary>
/// Ventana principal. Repartida en varios archivos:
/// MainWindow.xaml.cs (documento y ciclo de vida), MainWindow.Menus.cs (menús y atajos),
/// MainWindow.Commands.cs (acciones), MainWindow.Export.cs (exportar e imprimir) y MainWindow.Images.cs.
/// </summary>
public partial class MainWindow : Window
{
    private const string FileFilter = "Markdown (*.md;*.markdown;*.mdown;*.mkd;*.qmd)|*.md;*.markdown;*.mdown;*.mkd;*.qmd|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

    /// <summary>Solo la primera ventana ofrece recuperar borradores.</summary>
    private static bool _recoveryOffered;

    private readonly string? _initialFile;
    private readonly EditorBridge _editor;
    private readonly UpdateService _updates = new();
    private readonly RecoveryStore _recovery = new(App.Paths.RecoveryFolder);
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _dirtyCheckTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private MarkdownDocument _document = MarkdownDocument.CreateNew();
    private EditorMode _mode = EditorMode.Live;
    private bool _readOnly;
    private bool _focusMode;
    private bool _typewriterMode;
    private bool _editorReady;
    private bool _editorChanged;
    private bool _closingConfirmed;
    private bool _checkingDisk;
    private DocumentStats? _stats;

    // Texto del documento al cargarlo y cómo lo devuelve el editor justo después de cargarlo.
    // El editor puede normalizar el Markdown (espacios, saltos finales…); comparar con su propia
    // versión inicial evita marcar como modificado un documento que el usuario no ha tocado y
    // permite guardar el texto original sin reformatearlo.
    private string _loadedText = string.Empty;
    private string _editorBaseline = string.Empty;

    public MainWindow(string? initialFile)
    {
        _initialFile = initialFile;
        InitializeComponent();
        RestorePlacement();

        _editor = new EditorBridge(EditorView);
        _editor.ContentChanged += (_, _) => OnEditorChanged();
        _editor.StatsChanged += (_, stats) => OnStatsChanged(stats);
        _editor.LinkActivated += (_, href) => OpenLink(href);
        _editor.OutlineChanged += (_, items) => SidebarPanel.SetOutline(items);
        _editor.ActiveHeadingChanged += (_, index) => SidebarPanel.SetActiveHeading(index);
        _editor.StatusMessage += (_, text) => StatusText.Text = text;
        _editor.CopyRequested += (_, text) =>
        {
            SafeClipboard.SetText(text);
            StatusText.Text = "Código copiado al portapapeles";
        };
        _editor.ImageReceived += async (_, image) => await InsertImageDataAsync(image.Name, image.Data);
        FindBarPanel.Attach(_editor);

        SidebarPanel.FileOpenRequested += async (_, path) => await OpenFromSidebarAsync(path);
        SidebarPanel.OpenFolderRequested += (_, _) => OpenFolderDialog();
        SidebarPanel.HeadingRequested += async (_, index) => await _editor.ScrollToHeadingAsync(index);
        SidebarPanel.TabChanged += (_, tab) => { App.Settings.SidebarTab = tab; App.SaveSettings(); };
        SidebarPanel.FilesViewChanged += (_, view) => { App.Settings.FilesView = view; App.SaveSettings(); };
        SidebarPanel.SearchResultActivated += async (_, r) =>
        {
            await OpenFromSidebarAsync(r.Path);
            await FindBarPanel.SearchAsync(r.Query, r.CaseSensitive, r.Regex, r.WholeWord);
        };

        _recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, App.Settings.RecoveryIntervalSeconds)) };
        _recoveryTimer.Tick += async (_, _) => await SaveRecoveryDraftAsync();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(10, App.Settings.AutoSaveIntervalSeconds)) };
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveAsync();
        _dirtyCheckTimer.Tick += async (_, _) =>
        {
            _dirtyCheckTimer.Stop();
            await SyncFromEditorAsync();
            UpdateTitle();
        };

        Loaded += async (_, _) => await InitializeAsync();
        Activated += async (_, _) => await CheckExternalChangesAsync();
        Drop += OnDrop;

        BuildMenus();
        ApplyViewSettings();
        UpdateTitle();
    }

    private static IEnumerable<MainWindow> AllWindows => Application.Current.Windows.OfType<MainWindow>();

    // ───────────────────────── Arranque ─────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            var editorFolder = Path.Combine(AppContext.BaseDirectory, "editor");
            await _editor.InitializeAsync(App.Paths.WebViewDataFolder, editorFolder);
            _editor.ZoomFactor = App.Settings.Zoom;
            _editorReady = true;
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo inicializar el editor", ex);
            LoadingText.Text = "No se pudo iniciar el editor.\nComprueba que WebView2 Runtime está instalado.";
            return;
        }

        var isFirstWindow = !_recoveryOffered;
        if (!isFirstWindow || !await OfferRecoveryAsync())
        {
            var file = _initialFile;
            if (file is null && isFirstWindow && App.Settings.ReopenLastFile)
            {
                file = App.Settings.RecentFiles.FirstOrDefault(File.Exists);
            }

            if (file is not null && File.Exists(file))
            {
                await OpenFileAsync(file);
            }
            else
            {
                await ShowDocumentAsync(MarkdownDocument.CreateNew());
                if (SidebarPanel.Folder is null && isFirstWindow && App.Settings.RecentFolders.FirstOrDefault(Directory.Exists) is { } folder)
                {
                    SidebarPanel.SetFolder(folder);
                }
            }
        }

        _recoveryTimer.Start();
        if (App.Settings.AutoSave)
        {
            _autoSaveTimer.Start();
        }

        if (isFirstWindow && App.Settings.CheckForUpdates)
        {
            _ = CheckForUpdatesAsync(interactive: false);
        }

        // En una instalación para todo el equipo, el instalador registra mdViewer con su propia cuenta;
        // repetirlo aquí garantiza que cada usuario lo vea en "Abrir con". Es idempotente y no bloquea.
        if (isFirstWindow && _updates.IsInstalled)
        {
            _ = Task.Run(FileAssociation.TryRegisterForCurrentUser);
        }

        if (isFirstWindow && !App.Settings.WelcomeShown && _initialFile is null)
        {
            await ShowWelcomeAsync();
        }
    }

    private async Task<bool> OfferRecoveryAsync()
    {
        _recoveryOffered = true;
        var drafts = _recovery.List();
        if (drafts.Count == 0)
        {
            return false;
        }

        var draft = drafts[0];
        var name = draft.OriginalPath is null ? "un documento sin título" : Path.GetFileName(draft.OriginalPath);
        var answer = MessageBox.Show(this,
            $"mdViewer no se cerró correctamente. Hay cambios sin guardar de {name} ({draft.SavedAt:g}).\n\n¿Quieres recuperarlos?",
            "Recuperar documento", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            foreach (var d in drafts)
            {
                _recovery.Delete(d.DocumentId);
            }

            return false;
        }

        var document = draft.OriginalPath is not null && File.Exists(draft.OriginalPath)
            ? await MarkdownDocument.OpenAsync(draft.OriginalPath)
            : MarkdownDocument.CreateNew();
        document.UpdateText(draft.Text);
        foreach (var d in drafts)
        {
            _recovery.Delete(d.DocumentId);
        }

        await ShowDocumentAsync(document);
        return true;
    }

    // ───────────────────────── Documento ─────────────────────────

    private WritingOptions CurrentWritingOptions()
        => new(_focusMode, _typewriterMode, App.Settings.SmartPunctuation, App.Settings.AutoPair, App.Settings.SpellCheck);

    private async Task ShowDocumentAsync(MarkdownDocument document)
    {
        _dirtyCheckTimer.Stop();
        _document = document;
        _editorChanged = false;
        var folder = document.FilePath is null ? null : Path.GetDirectoryName(document.FilePath);
        await _editor.LoadAsync(document.Text, folder, EditorTheme(), _mode, CurrentWritingOptions(), _readOnly, App.Settings.ToolbarVisible, App.Settings.LineNumbers);
        _loadedText = document.Text;
        _editorBaseline = await _editor.GetMarkdownAsync();

        UpdateTitle();
        StatusText.Text = document.FilePath ?? string.Empty;
        if (folder is not null)
        {
            if (SidebarPanel.Folder is null || !document.FilePath!.StartsWith(SidebarPanel.Folder, StringComparison.OrdinalIgnoreCase))
            {
                SidebarPanel.SetFolder(folder);
            }

            SidebarPanel.Reveal(document.FilePath!);
        }

        // Foco de teclado al control WebView2 (ventana) y después al editor dentro de la página:
        // sin lo primero, lo que se escribe no llega al editor hasta hacer clic en él.
        EditorView.Focus();
        await _editor.FocusAsync();
    }

    private async Task OpenFileAsync(string path)
    {
        try
        {
            var document = await MarkdownDocument.OpenAsync(path);
            await ShowDocumentAsync(document);
            App.Settings.AddRecentFile(document.FilePath!);
            App.SaveSettings();
            RefreshRecentMenu();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"No se pudo abrir {path}", ex);
            MessageBox.Show(this, $"No se pudo abrir el archivo:\n{path}\n\n{ex.Message}", "mdViewer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Abre un archivo en esta ventana, o activa la ventana que ya lo tiene abierto.</summary>
    private async Task OpenOrActivateAsync(string path)
    {
        var existing = AllWindows.FirstOrDefault(w => w != this && string.Equals(w._document.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        if (string.Equals(_document.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (await ConfirmDiscardAsync())
        {
            await OpenFileAsync(path);
        }
    }

    private Task OpenFromSidebarAsync(string path) => OpenOrActivateAsync(path);

    /// <summary>Copia el texto del editor al documento (el editor es la fuente de verdad mientras se edita).</summary>
    /// <remarks>
    /// Se lee siempre del editor: el aviso de cambio llega de forma asíncrona y puede no haber llegado
    /// aún justo después de un comando (p. ej. aplicar negrita y guardar inmediatamente).
    /// </remarks>
    private async Task SyncFromEditorAsync()
    {
        if (_editorReady)
        {
            _editorChanged = false;
            var markdown = await _editor.GetMarkdownAsync();

            // Si el editor vuelve a tener exactamente lo que tenía al cargar (p. ej. tras deshacer),
            // el documento recupera su texto original byte a byte.
            _document.UpdateText(string.Equals(markdown, _editorBaseline, StringComparison.Ordinal) ? _loadedText : markdown);
        }
    }

    /// <summary>Sustituye el contenido del editor (operaciones sobre el texto completo, como reescribir rutas).</summary>
    private async Task ReplaceDocumentTextAsync(string markdown)
    {
        await _editor.SetValueAsync(markdown);
        _document.UpdateText(markdown);
        _editorChanged = false;
        UpdateTitle();
    }

    private async Task<bool> SaveAsync(bool forceDialog = false)
    {
        await SyncFromEditorAsync();

        var path = _document.FilePath;
        if (forceDialog || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = FileFilter,
                DefaultExt = ".md",
                FileName = path is null ? SuggestFileName() : Path.GetFileName(path),
                InitialDirectory = path is null ? SidebarPanel.Folder : Path.GetDirectoryName(path),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            _document.SaveAs(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Error al guardar {path}", ex);
            MessageBox.Show(this, $"No se pudo guardar el archivo:\n{path}\n\n{ex.Message}", "mdViewer",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        // El archivo ya está en disco. Lo que sigue es mantenimiento: si falla, se registra
        // pero nunca se informa como error de guardado.
        _loadedText = _document.Text;
        UpdateTitle();
        StatusText.Text = $"Guardado {DateTime.Now:T}";
        DiscardRecovery();
        App.Settings.AddRecentFile(path);
        App.SaveSettings();
        RefreshRecentMenu();
        SidebarPanel.Reveal(path);
        return true;
    }

    /// <summary>Nombre propuesto para un documento sin guardar: su primer título.</summary>
    private string SuggestFileName()
    {
        var firstHeading = _document.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith('#'))?.TrimStart('#').Trim();
        if (string.IsNullOrWhiteSpace(firstHeading))
        {
            return "Sin título.md";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            firstHeading = firstHeading.Replace(c, ' ');
        }

        return (firstHeading.Length > 60 ? firstHeading[..60] : firstHeading).Trim() + ".md";
    }

    /// <summary>Pregunta si guardar los cambios. Devuelve false si el usuario cancela.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        await SyncFromEditorAsync();
        if (!_document.IsDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(this, $"¿Guardar los cambios de {_document.DisplayName}?", "mdViewer",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer switch
        {
            MessageBoxResult.Yes => await SaveAsync(),
            MessageBoxResult.No => DiscardRecovery(),
            _ => false,
        };
    }

    private bool DiscardRecovery()
    {
        try
        {
            _recovery.Delete(_document.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo borrar el borrador de recuperación", ex);
        }

        return true;
    }

    /// <summary>
    /// El editor avisa de cada edición. Marcamos el título al momento y, tras una pausa breve,
    /// comparamos el texto real: si no ha cambiado (deshacer, teclas sin efecto) se quita la marca.
    /// </summary>
    private void OnEditorChanged()
    {
        if (!_editorChanged)
        {
            _editorChanged = true;
            UpdateTitle();
        }

        _dirtyCheckTimer.Stop();
        _dirtyCheckTimer.Start();
    }

    private async Task SaveRecoveryDraftAsync()
    {
        try
        {
            await SyncFromEditorAsync();
            if (_document.IsDirty)
            {
                _recovery.Save(_document.Id, _document.FilePath, _document.Text);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo guardar el borrador de recuperación", ex);
        }
    }

    private async Task AutoSaveAsync()
    {
        await SyncFromEditorAsync();
        if (_document.IsDirty && _document.FilePath is not null && !_readOnly)
        {
            await SaveAsync();
        }
    }

    private async Task CheckExternalChangesAsync()
    {
        if (_checkingDisk || !_editorReady || !_document.HasChangedOnDisk())
        {
            return;
        }

        _checkingDisk = true;
        try
        {
            await SyncFromEditorAsync();
            var reload = !_document.IsDirty || MessageBox.Show(this,
                $"{_document.DisplayName} se ha modificado fuera de mdViewer.\n\n¿Recargar y perder tus cambios?",
                "Archivo modificado", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

            if (reload)
            {
                await _document.ReloadAsync();
                await ShowDocumentAsync(_document);
                StatusText.Text = "Recargado desde disco";
            }
            else
            {
                // Aceptamos la versión del usuario; no volvemos a preguntar por este cambio externo.
                _document.UpdateText(_document.Text);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo comprobar el archivo en disco", ex);
        }
        finally
        {
            _checkingDisk = false;
        }
    }

    private void OnStatsChanged(DocumentStats stats)
    {
        _stats = stats;
        StatsText.Text = stats.Words == 1 ? "1 palabra" : $"{stats.Words:N0} palabras";
        _wordCountWindow?.Update(stats);
    }

    // ───────────────────────── Enlaces ─────────────────────────

    private void OpenLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
        {
            return;
        }

        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto"
                && !string.Equals(uri.Host, EditorBridge.DocumentHost, StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return;
            }

            var local = ResolveLocalPath(href);
            if (local is null)
            {
                return;
            }

            if (MarkdownFiles.IsMarkdown(local))
            {
                _ = OpenLinkedMarkdownAsync(local);
            }
            else if (File.Exists(local) || Directory.Exists(local))
            {
                // No ejecutamos archivos enlazados: solo se muestran en el Explorador.
                Process.Start("explorer.exe", $"/select,\"{local}\"");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Warn($"No se pudo abrir el enlace {href}", ex);
        }
    }

    private async Task OpenLinkedMarkdownAsync(string path)
    {
        if (File.Exists(path))
        {
            await OpenOrActivateAsync(path);
            return;
        }

        // Enlace a un documento que aún no existe: se ofrece crearlo.
        if (MessageBox.Show(this, $"El documento no existe:\n{path}\n\n¿Quieres crearlo?", "mdViewer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes && await ConfirmDiscardAsync())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"# {Path.GetFileNameWithoutExtension(path)}\n");
            await OpenFileAsync(path);
        }
    }

    private string? ResolveLocalPath(string href)
    {
        var baseFolder = _document.FilePath is null ? null : Path.GetDirectoryName(_document.FilePath);
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Host, EditorBridge.DocumentHost, StringComparison.OrdinalIgnoreCase) && baseFolder is not null)
            {
                return Path.GetFullPath(Path.Combine(baseFolder, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))));
            }

            return uri.IsFile ? uri.LocalPath : null;
        }

        if (baseFolder is null)
        {
            return null;
        }

        var withoutAnchor = href.Split('#')[0];
        return Path.GetFullPath(Path.Combine(baseFolder, Uri.UnescapeDataString(withoutAnchor)));
    }

    // ───────────────────────── Ventana ─────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        // Sin cambios pendientes se cierra directamente, sin pasos asíncronos.
        if (!_closingConfirmed && (!_editorReady || (!_editorChanged && !_document.IsDirty)))
        {
            _closingConfirmed = true;
            PrepareForExit();
        }

        if (_closingConfirmed)
        {
            SavePlacement();
            _wordCountWindow?.Close();
            base.OnClosing(e);
            return;
        }

        // Hay cambios: cancelamos este cierre y preguntamos de forma asíncrona.
        e.Cancel = true;
        _ = ConfirmAndCloseAsync();
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        _closingConfirmed = true;
        PrepareForExit();

        // WPF no permite llamar a Close() mientras se está procesando otro cierre:
        // se pospone hasta que el evento Closing actual haya terminado.
        _ = Dispatcher.BeginInvoke(Close, DispatcherPriority.Background);
    }

    private void PrepareForExit()
    {
        _dirtyCheckTimer.Stop();
        _recoveryTimer.Stop();
        _autoSaveTimer.Stop();
        if (AllWindows.Count() <= 1)
        {
            _updates.ApplyOnExit();
        }
    }

    private void UpdateTitle()
    {
        var dirty = _editorChanged || _document.IsDirty ? "• " : string.Empty;
        var readOnly = _readOnly ? " (solo lectura)" : string.Empty;
        Title = $"{dirty}{_document.DisplayName}{readOnly} — mdViewer";
    }

    private static string EditorTheme() => ThemeManager.EditorTheme(App.Settings.Theme);

    private void RestorePlacement()
    {
        if (App.Settings.Window is not { } p)
        {
            return;
        }

        // Solo restauramos si la ventana queda visible (p. ej. tras desconectar un monitor).
        var visible = p.Left >= SystemParameters.VirtualScreenLeft - 50
                      && p.Top >= SystemParameters.VirtualScreenTop - 50
                      && p.Left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                      && p.Top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!visible)
        {
            return;
        }

        // Las ventanas nuevas se desplazan un poco para no quedar exactamente encima de la anterior.
        var offset = (AllWindows.Count() - 1) * 28;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left + offset;
        Top = p.Top + offset;
        Width = Math.Max(MinWidth, p.Width);
        Height = Math.Max(MinHeight, p.Height);
        if (p.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        App.Settings.Window = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            WindowState == WindowState.Maximized);
        App.SaveSettings();
    }
}
