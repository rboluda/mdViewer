using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MdViewer.App.Editor;
using MdViewer.App.Theming;
using MdViewer.App.Updates;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Documents;
using MdViewer.Core.Recovery;
using MdViewer.Core.Settings;
using Microsoft.Win32;
using AppTheme = MdViewer.Core.Settings.ThemeMode;

namespace MdViewer.App;

public partial class MainWindow : Window
{
    private const string FileFilter = "Markdown (*.md;*.markdown;*.mdown;*.qmd)|*.md;*.markdown;*.mdown;*.qmd|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

    private readonly string? _initialFile;
    private readonly EditorBridge _editor;
    private readonly UpdateService _updates = new();
    private readonly RecoveryStore _recovery = new(App.Paths.RecoveryFolder);
    private readonly DispatcherTimer _recoveryTimer;
    private readonly DispatcherTimer _autoSaveTimer;

    private MarkdownDocument _document = MarkdownDocument.CreateNew();
    private EditorMode _mode = EditorMode.Live;
    private bool _editorReady;
    private bool _editorChanged;

    // Texto del documento al cargarlo y cómo lo devuelve el editor justo después de cargarlo.
    // El editor puede normalizar el Markdown (espacios, saltos finales…); comparar con su propia
    // versión inicial evita marcar como modificado un documento que el usuario no ha tocado y
    // permite guardar el texto original sin reformatearlo.
    private string _loadedText = string.Empty;
    private string _editorBaseline = string.Empty;
    private readonly DispatcherTimer _dirtyCheckTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _closingConfirmed;
    private bool _checkingDisk;

    public MainWindow(string? initialFile)
    {
        _initialFile = initialFile;
        InitializeComponent();
        RestorePlacement();

        _editor = new EditorBridge(EditorView);
        _editor.ContentChanged += (_, _) => OnEditorChanged();
        _editor.StatsChanged += (_, chars) => StatsText.Text = $"{chars:N0} caracteres";
        _editor.LinkActivated += (_, href) => OpenLink(href);

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
        PreviewKeyDown += OnPreviewKeyDown;
        Drop += OnDrop;

        UpdateThemeMenu();
        RefreshRecentMenu();
        UpdateTitle();
    }

    // ───────────────────────── Arranque ─────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            var editorFolder = Path.Combine(AppContext.BaseDirectory, "editor");
            await _editor.InitializeAsync(App.Paths.WebViewDataFolder, editorFolder);
            _editorReady = true;
            LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo inicializar el editor", ex);
            LoadingText.Text = "No se pudo iniciar el editor.\nComprueba que WebView2 Runtime está instalado.";
            return;
        }

        if (!await OfferRecoveryAsync())
        {
            var file = _initialFile;
            if (file is null && App.Settings.ReopenLastFile)
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
            }
        }

        _recoveryTimer.Start();
        if (App.Settings.AutoSave)
        {
            _autoSaveTimer.Start();
        }

        if (App.Settings.CheckForUpdates)
        {
            _ = CheckForUpdatesAsync(interactive: false);
        }
    }

    private async Task<bool> OfferRecoveryAsync()
    {
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

    private async Task ShowDocumentAsync(MarkdownDocument document)
    {
        _dirtyCheckTimer.Stop();
        _document = document;
        _editorChanged = false;
        var folder = document.FilePath is null ? null : Path.GetDirectoryName(document.FilePath);
        await _editor.LoadAsync(document.Text, folder, EditorTheme(), _mode);
        _loadedText = document.Text;
        _editorBaseline = await _editor.GetMarkdownAsync();

        UpdateTitle();
        StatusText.Text = document.FilePath ?? string.Empty;

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

    /// <summary>Copia el texto del editor al documento (el editor es la fuente de verdad mientras se edita).</summary>
    private async Task SyncFromEditorAsync()
    {
        if (_editorReady && _editorChanged)
        {
            _editorChanged = false;
            var markdown = await _editor.GetMarkdownAsync();

            // Si el editor vuelve a tener exactamente lo que tenía al cargar (p. ej. tras deshacer),
            // el documento recupera su texto original byte a byte.
            _document.UpdateText(string.Equals(markdown, _editorBaseline, StringComparison.Ordinal) ? _loadedText : markdown);
        }
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
                FileName = path is null ? "Sin título.md" : Path.GetFileName(path),
                InitialDirectory = path is null ? null : Path.GetDirectoryName(path),
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
        UpdateTitle();
        StatusText.Text = $"Guardado {DateTime.Now:T}";
        try
        {
            _recovery.Delete(_document.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo borrar el borrador de recuperación", ex);
        }

        App.Settings.AddRecentFile(path);
        App.SaveSettings();
        RefreshRecentMenu();
        return true;
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
        if (_document.IsDirty && _document.FilePath is not null)
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

    // ───────────────────────── Comandos ─────────────────────────

    private async void OnNew(object sender, ExecutedRoutedEventArgs e)
    {
        if (await ConfirmDiscardAsync())
        {
            await ShowDocumentAsync(MarkdownDocument.CreateNew());
        }
    }

    private async void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = FileFilter };
        if (dialog.ShowDialog(this) == true && await ConfirmDiscardAsync())
        {
            await OpenFileAsync(dialog.FileName);
        }
    }

    private async void OnSave(object sender, ExecutedRoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAs(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(forceDialog: true);

    private void OnExit(object sender, ExecutedRoutedEventArgs e) => Close();

    private async void OnToggleSourceMode(object sender, RoutedEventArgs e) => await ToggleSourceModeAsync();

    private async Task ToggleSourceModeAsync()
    {
        if (!_editorReady)
        {
            return;
        }

        _mode = _mode == EditorMode.Live ? EditorMode.Source : EditorMode.Live;
        SourceModeMenu.IsChecked = _mode == EditorMode.Source;
        await _editor.SetModeAsync(_mode);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+/ (teclado US) o Ctrl+7 / Ctrl+÷ del teclado numérico (teclado español)
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.OemQuestion or Key.Divide or Key.D7)
        {
            e.Handled = true;
            await ToggleSourceModeAsync();
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && await ConfirmDiscardAsync())
        {
            await OpenFileAsync(files[0]);
        }
    }

    private async void OnThemeSelected(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            App.Settings.Theme = theme;
            App.SaveSettings();
            UpdateThemeMenu();
            if (_editorReady)
            {
                await _editor.SetThemeAsync(EditorTheme());
            }
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(interactive: true);

    private void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        _ = ApplyUpdateAsync();
    }

    private async Task ApplyUpdateAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            _closingConfirmed = true;
            _updates.ApplyAndRestart();
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Paths.LogsFolder);
        Process.Start(new ProcessStartInfo(App.Paths.LogsFolder) { UseShellExecute = true });
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var version = _updates.CurrentVersion ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        MessageBox.Show(this,
            $"mdViewer {version}\n\nEditor Markdown libre y de código abierto (MIT).\n{UpdateService.RepositoryUrl}",
            "Acerca de mdViewer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (!_updates.IsInstalled)
        {
            if (interactive)
            {
                MessageBox.Show(this, "Las actualizaciones automáticas solo funcionan en la versión instalada.",
                    "Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        if (interactive)
        {
            StatusText.Text = "Buscando actualizaciones…";
        }

        var ready = await _updates.CheckAndDownloadAsync();
        if (ready)
        {
            UpdateButton.Content = $"Reiniciar para actualizar a {_updates.PendingVersion}";
            UpdateButton.Visibility = Visibility.Visible;
        }
        else if (interactive)
        {
            StatusText.Text = string.Empty;
            MessageBox.Show(this, "Tienes la última versión.", "Actualizaciones", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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

            if (IsMarkdownFile(local))
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
        if (File.Exists(path) && await ConfirmDiscardAsync())
        {
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

    private static bool IsMarkdownFile(string path)
        => Path.GetExtension(path).ToUpperInvariant() is ".MD" or ".MARKDOWN" or ".MDOWN" or ".QMD";

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
        _updates.ApplyOnExit();
    }

    private void UpdateTitle()
    {
        var dirty = _editorChanged || _document.IsDirty ? "• " : string.Empty;
        Title = $"{dirty}{_document.DisplayName} — mdViewer";
    }

    private void RefreshRecentMenu()
    {
        RecentMenu.Items.Clear();
        foreach (var path in App.Settings.RecentFiles)
        {
            var item = new MenuItem { Header = path.Replace("_", "__", StringComparison.Ordinal), Tag = path };
            item.Click += async (_, _) =>
            {
                if (await ConfirmDiscardAsync())
                {
                    await OpenFileAsync(path);
                }
            };
            RecentMenu.Items.Add(item);
        }

        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private void UpdateThemeMenu()
    {
        ThemeBlueMenu.IsChecked = App.Settings.Theme == AppTheme.Blue;
        ThemeLightMenu.IsChecked = App.Settings.Theme == AppTheme.Light;
        ThemeDarkMenu.IsChecked = App.Settings.Theme == AppTheme.Dark;
        ThemeSystemMenu.IsChecked = App.Settings.Theme == AppTheme.System;
        ThemeManager.Apply(this, App.Settings.Theme);
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

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
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
    }
}
