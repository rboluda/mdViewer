using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MdViewer.App.Dialogs;
using MdViewer.App.Editor;
using MdViewer.App.Theming;
using MdViewer.Core.Diagnostics;
using MdViewer.Core.Documents;
using MdViewer.Core.Files;
using MdViewer.Core.Settings;
using AppTheme = MdViewer.Core.Settings.ThemeMode;
using Microsoft.Win32;

namespace MdViewer.App;

/// <summary>Acciones de los menús Archivo, Editar, Párrafo, Ver, Temas y Ayuda.</summary>
public partial class MainWindow
{
    private WordCountWindow? _wordCountWindow;
    private bool _fullScreen;
    private WindowState _stateBeforeFullScreen;
    private WindowStyle _styleBeforeFullScreen;

    // ───────────────────────── Archivo ─────────────────────────

    private async Task NewDocumentAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            await ShowDocumentAsync(MarkdownDocument.CreateNew());
        }
    }

    private static void NewWindow() => new MainWindow(null).Show();

    private async Task OpenFileDialogAsync()
    {
        var dialog = new OpenFileDialog { Filter = FileFilter, InitialDirectory = SidebarPanel.Folder };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenOrActivateAsync(dialog.FileName);
        }
    }

    private void OpenFolderDialog()
    {
        var dialog = new OpenFolderDialog { Title = "Abrir carpeta", InitialDirectory = SidebarPanel.Folder };
        if (dialog.ShowDialog(this) == true)
        {
            OpenFolder(dialog.FolderName);
        }
    }

    private void OpenFolder(string folder)
    {
        SidebarPanel.SetFolder(folder);
        App.Settings.AddRecentFolder(folder);
        App.SaveSettings();
        RefreshRecentMenu();
        ShowFiles(SidebarPanel.FilesView);
    }

    private async Task QuickOpenAsync()
    {
        var dialog = new QuickOpenWindow(this, SidebarPanel.Folder, App.Settings.RecentFiles);
        if (dialog.ShowDialog() == true && dialog.SelectedPath is { } path)
        {
            await OpenOrActivateAsync(path);
        }
    }

    private void RefreshRecentMenu()
    {
        foreach (var window in AllWindows.Append(this).Distinct())
        {
            window.FillRecentMenu();
        }
    }

    private void FillRecentMenu()
    {
        if (_recentMenu is null)
        {
            return;
        }

        _recentMenu.Items.Clear();
        foreach (var path in App.Settings.RecentFiles.Take(15))
        {
            var item = new MenuItem { Header = path.Replace("_", "__", StringComparison.Ordinal) };
            var target = path;
            item.Click += Run(() => OpenOrActivateAsync(target)).ToRouted();
            _recentMenu.Items.Add(item);
        }

        if (App.Settings.RecentFolders.Count > 0)
        {
            _recentMenu.Items.Add(new Separator());
            foreach (var folder in App.Settings.RecentFolders.Take(8))
            {
                var item = new MenuItem { Header = "📁 " + folder.Replace("_", "__", StringComparison.Ordinal) };
                var target = folder;
                item.Click += (_, _) => OpenFolder(target);
                _recentMenu.Items.Add(item);
            }
        }

        if (_recentMenu.Items.Count > 0)
        {
            _recentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "_Borrar la lista" };
            clear.Click += (_, _) =>
            {
                App.Settings.RecentFiles.Clear();
                App.Settings.RecentFolders.Clear();
                App.SaveSettings();
                RefreshRecentMenu();
            };
            _recentMenu.Items.Add(clear);
        }

        _recentMenu.IsEnabled = _recentMenu.Items.Count > 0;
    }

    private async Task ReopenWithEncodingAsync(int codePage, bool bom)
    {
        if (_document.FilePath is null)
        {
            StatusText.Text = "El documento todavía no está guardado.";
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var encoding = codePage switch
        {
            65001 => new UTF8Encoding(bom),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: bom),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: bom),
            _ => Encoding.GetEncoding(codePage),
        };
        await _document.ReloadWithEncodingAsync(encoding);
        await ShowDocumentAsync(_document);
        StatusText.Text = $"Reabierto como {PropertiesWindow.EncodingName(_document.Format)}";
    }

    private async Task ReloadFromDiskAsync()
    {
        if (_document.FilePath is null)
        {
            return;
        }

        await SyncFromEditorAsync();
        if (_document.IsDirty && MessageBox.Show(this, "Se perderán los cambios sin guardar. ¿Recargar desde el disco?", "mdViewer",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await _document.ReloadAsync();
        await ShowDocumentAsync(_document);
        StatusText.Text = "Recargado desde disco";
    }

    private async Task MoveToAsync()
    {
        await SyncFromEditorAsync();
        var dialog = new SaveFileDialog
        {
            Title = "Mover a",
            Filter = FileFilter,
            FileName = _document.FilePath is null ? SuggestFileName() : Path.GetFileName(_document.FilePath),
            InitialDirectory = _document.FilePath is null ? SidebarPanel.Folder : Path.GetDirectoryName(_document.FilePath),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var oldPath = _document.MoveTo(dialog.FileName);
        if (oldPath is not null)
        {
            App.Settings.RecentFiles.RemoveAll(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
        }

        App.Settings.AddRecentFile(dialog.FileName);
        App.SaveSettings();
        RefreshRecentMenu();
        _loadedText = _document.Text;
        UpdateTitle();
        StatusText.Text = dialog.FileName;
        SidebarPanel.RefreshFiles();
        SidebarPanel.Reveal(dialog.FileName);
    }

    private async Task SaveAllAsync()
    {
        foreach (var window in AllWindows.ToList())
        {
            await window.SyncFromEditorAsync();
            if (window._document.IsDirty)
            {
                window.Activate();
                await window.SaveAsync();
            }
        }
    }

    private async Task ShowPropertiesAsync()
    {
        await SyncFromEditorAsync();
        new PropertiesWindow(this, _document, _stats).ShowDialog();
    }

    private void OpenFileLocation()
    {
        if (_document.FilePath is { } path && File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else
        {
            StatusText.Text = "El documento todavía no está guardado.";
        }
    }

    private void RevealInSidebar()
    {
        if (_document.FilePath is not { } path)
        {
            return;
        }

        ShowFiles(FilesView.Tree);
        SidebarPanel.Reveal(path);
    }

    private async Task DeleteFileAsync()
    {
        if (_document.FilePath is not { } path || !File.Exists(path))
        {
            StatusText.Text = "El documento todavía no está guardado.";
            return;
        }

        if (MessageBox.Show(this, $"¿Enviar «{Path.GetFileName(path)}» a la papelera de reciclaje?", "Eliminar",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        App.Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        App.SaveSettings();
        RefreshRecentMenu();
        DiscardRecovery();
        await ShowDocumentAsync(MarkdownDocument.CreateNew());
        SidebarPanel.RefreshFiles();
        StatusText.Text = "Documento enviado a la papelera";
    }

    private async Task ShowPreferencesAsync()
    {
        var previousTheme = App.Settings.Theme;
        if (new PreferencesWindow(this, App.Settings).ShowDialog() != true)
        {
            return;
        }

        App.SaveSettings();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, App.Settings.AutoSaveIntervalSeconds));
        foreach (var window in AllWindows)
        {
            if (App.Settings.AutoSave)
            {
                window._autoSaveTimer.Start();
            }
            else
            {
                window._autoSaveTimer.Stop();
            }

            if (window._editorReady)
            {
                await window._editor.SetWritingAsync(window.CurrentWritingOptions());
            }
        }

        if (previousTheme != App.Settings.Theme)
        {
            await SetThemeAsync(App.Settings.Theme);
        }
    }

    // ───────────────────────── Editar ─────────────────────────

    private async Task PasteFromClipboardAsync()
    {
        if (!_editorReady || _readOnly)
        {
            return;
        }

        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            await InsertImageDataAsync($"imagen-{DateTime.Now:yyyyMMdd-HHmmss}.png", stream.ToArray());
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            foreach (var file in Clipboard.GetFileDropList().Cast<string>())
            {
                await InsertFileAsync(file);
            }

            return;
        }

        var html = Clipboard.ContainsText(TextDataFormat.Html) ? ExtractHtmlFragment(Clipboard.GetText(TextDataFormat.Html)) : null;
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        await _editor.PasteAsync(text, html);
    }

    /// <summary>El formato HTML del portapapeles de Windows lleva una cabecera; nos quedamos con el fragmento.</summary>
    private static string ExtractHtmlFragment(string clipboardHtml)
    {
        const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
        var s = clipboardHtml.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        var e = clipboardHtml.IndexOf(end, StringComparison.OrdinalIgnoreCase);
        return s >= 0 && e > s ? clipboardHtml[(s + start.Length)..e] : clipboardHtml;
    }

    private async Task PastePlainTextAsync()
    {
        if (_editorReady && !_readOnly && Clipboard.ContainsText())
        {
            await _editor.InsertTextAsync(Clipboard.GetText());
        }
    }

    private async Task CopyAsMarkdownAsync() => SetClipboardText(await _editor.SelectionMarkdownAsync());

    private async Task CopyAsPlainTextAsync() => SetClipboardText(await _editor.SelectionTextAsync());

    private async Task CopyAsHtmlAsync() => SetClipboardText(await _editor.SelectionHtmlAsync());

    private void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "No hay nada seleccionado.";
            return;
        }

        SafeClipboard.SetText(text);
        StatusText.Text = "Copiado al portapapeles";
    }

    private static async Task ToggleWritingSettingAsync(Action<AppSettings> change)
    {
        change(App.Settings);
        App.SaveSettings();
        foreach (var window in AllWindows.Where(w => w._editorReady))
        {
            await window._editor.SetWritingAsync(window.CurrentWritingOptions());
        }
    }

    private void SetLineEnding(LineEnding lineEnding)
    {
        _document.ChangeFormat(_document.Format with { LineEnding = lineEnding });
        UpdateTitle();
        StatusText.Text = lineEnding == LineEnding.Lf ? "Se guardará con saltos de línea LF" : "Se guardará con saltos de línea CRLF";
    }

    private async Task TrimTrailingWhitespaceAsync()
    {
        await SyncFromEditorAsync();
        var lines = _document.Text.Split('\n');
        // Dos espacios al final son un salto de línea en Markdown: se conservan.
        var trimmed = string.Join('\n', lines.Select(l => l.EndsWith("  ", StringComparison.Ordinal) && l.Trim().Length > 0 ? l.TrimEnd() + "  " : l.TrimEnd()));
        await ReplaceDocumentTextAsync(trimmed);
    }

    private async Task TabsToSpacesAsync()
    {
        await SyncFromEditorAsync();
        await ReplaceDocumentTextAsync(_document.Text.Replace("\t", "    ", StringComparison.Ordinal));
    }

    private async Task OpenFindAsync(bool replace)
    {
        var selection = _editorReady ? await _editor.SelectionTextAsync() : null;
        FindBarPanel.Open(replace, selection);
    }

    private async Task SearchInFolderAsync()
    {
        var selection = _editorReady ? await _editor.SelectionTextAsync() : null;
        if (!App.Settings.SidebarVisible)
        {
            ToggleSidebar();
        }

        SidebarPanel.FocusSearch(selection?.Contains('\n', StringComparison.Ordinal) == true ? null : selection);
    }

    /// <summary>Abre el panel de emojis de Windows (Win + .) con el cursor en el editor.</summary>
    private async Task ShowEmojiPanelAsync()
    {
        EditorView.Focus();
        await _editor.FocusAsync();
        await Task.Delay(80);
        const byte VkLWin = 0x5B, VkOemPeriod = 0xBE;
        const uint KeyUp = 0x0002;
        keybd_event(VkLWin, 0, 0, IntPtr.Zero);
        keybd_event(VkOemPeriod, 0, 0, IntPtr.Zero);
        keybd_event(VkOemPeriod, 0, KeyUp, IntPtr.Zero);
        keybd_event(VkLWin, 0, KeyUp, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);

    // ───────────────────────── Párrafo y formato ─────────────────────────

    private async Task InsertTableAsync()
    {
        var dialog = new TableSizeDialog(this);
        if (dialog.ShowDialog() == true)
        {
            await _editor.ExecAsync("table", $"{dialog.Rows}x{dialog.Columns}");
        }
    }

    private async Task CopyCodeBlockAsync()
    {
        var code = await _editor.CodeAtCursorAsync();
        if (string.IsNullOrEmpty(code))
        {
            StatusText.Text = "Sitúa el cursor dentro de un bloque de código.";
            return;
        }

        SafeClipboard.SetText(code);
        StatusText.Text = "Código copiado";
    }

    private async Task OpenLinkAtCursorAsync()
    {
        var href = await _editor.LinkAtCursorAsync();
        if (string.IsNullOrEmpty(href))
        {
            StatusText.Text = "Sitúa el cursor sobre un enlace.";
            return;
        }

        OpenLink(href);
    }

    private async Task CopyLinkAddressAsync()
    {
        var href = await _editor.LinkAtCursorAsync();
        if (string.IsNullOrEmpty(href))
        {
            StatusText.Text = "Sitúa el cursor sobre un enlace.";
            return;
        }

        SafeClipboard.SetText(href);
        StatusText.Text = "Dirección copiada";
    }

    // ───────────────────────── Ver ─────────────────────────

    private void ApplyViewSettings()
    {
        SidebarPanel.SetFilesView(App.Settings.FilesView);
        SidebarPanel.ShowTab(App.Settings.SidebarTab);
        SetSidebarVisible(App.Settings.SidebarVisible);
        StatusBarPanel.Visibility = App.Settings.StatusBarVisible ? Visibility.Visible : Visibility.Collapsed;
        Topmost = App.Settings.AlwaysOnTop;
        ThemeManager.Apply(this, App.Settings.Theme);
    }

    private void SetSidebarVisible(bool visible)
    {
        SidebarColumn.Width = visible ? new GridLength(Math.Clamp(App.Settings.SidebarWidth, 160, 600)) : new GridLength(0);
        SplitterColumn.Width = visible ? new GridLength(4) : new GridLength(0);
        SidebarPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = SidebarPanel.Visibility;
    }

    private void ToggleSidebar()
    {
        App.Settings.SidebarVisible = !App.Settings.SidebarVisible;
        App.SaveSettings();
        SetSidebarVisible(App.Settings.SidebarVisible);
    }

    private void ShowSidebarTab(SidebarTab tab)
    {
        if (!App.Settings.SidebarVisible)
        {
            ToggleSidebar();
        }

        SidebarPanel.ShowTab(tab);
        App.Settings.SidebarTab = tab;
        App.SaveSettings();
    }

    private void ShowFiles(FilesView view)
    {
        ShowSidebarTab(SidebarTab.Files);
        if (SidebarPanel.FilesView != view)
        {
            SidebarPanel.SetFilesView(view);
            App.Settings.FilesView = view;
            App.SaveSettings();
        }
    }

    private void OnSidebarToggleClick(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void OnSidebarResized(object sender, DragCompletedEventArgs e)
    {
        App.Settings.SidebarWidth = SidebarColumn.ActualWidth;
        App.SaveSettings();
    }

    private void OnModeToggleClick(object sender, RoutedEventArgs e)
        => Run(() => SetModeAsync(_mode == EditorMode.Live ? EditorMode.Source : EditorMode.Live))();

    private async Task SetModeAsync(EditorMode mode)
    {
        if (!_editorReady || mode == _mode)
        {
            return;
        }

        await SyncFromEditorAsync();
        _mode = mode;
        await _editor.SetModeAsync(mode);
        ModeToggleButton.Content = mode == EditorMode.Live ? "</>" : "✎";
        ModeToggleButton.ToolTip = mode == EditorMode.Live ? "Modo código fuente (Ctrl+ç)" : "Volver a la edición en vivo (Ctrl+ç)";
        StatusText.Text = mode switch
        {
            EditorMode.Source => "Modo código fuente",
            EditorMode.Split => "Pantalla partida: código fuente a la izquierda, resultado a la derecha",
            _ => _document.FilePath ?? string.Empty,
        };
    }

    private async Task ToggleReadOnlyAsync()
    {
        _readOnly = !_readOnly;
        if (_editorReady)
        {
            await _editor.SetReadonlyAsync(_readOnly);
        }

        ReadonlyText.Visibility = _readOnly ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitle();
    }

    private async Task ToggleFocusModeAsync()
    {
        _focusMode = !_focusMode;
        await _editor.SetWritingAsync(CurrentWritingOptions());
        StatusText.Text = _focusMode ? "Modo concentración activado (F8 para salir)" : "Modo concentración desactivado";
    }

    private async Task ToggleTypewriterModeAsync()
    {
        _typewriterMode = !_typewriterMode;
        await _editor.SetWritingAsync(CurrentWritingOptions());
        StatusText.Text = _typewriterMode ? "Modo máquina de escribir activado (F9 para salir)" : "Modo máquina de escribir desactivado";
    }

    private void ToggleStatusBar()
    {
        App.Settings.StatusBarVisible = !App.Settings.StatusBarVisible;
        App.SaveSettings();
        StatusBarPanel.Visibility = App.Settings.StatusBarVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ToggleToolbarAsync()
    {
        App.Settings.ToolbarVisible = !App.Settings.ToolbarVisible;
        App.SaveSettings();
        await _editor.SetToolbarAsync(App.Settings.ToolbarVisible);
    }

    private void ToggleWordCountWindow()
    {
        if (_wordCountWindow is { IsVisible: true })
        {
            _wordCountWindow.Close();
            return;
        }

        _wordCountWindow = new WordCountWindow(this);
        _wordCountWindow.Closed += (_, _) => _wordCountWindow = null;
        _wordCountWindow.Update(_stats);
        _wordCountWindow.Show();
    }

    private void OnStatsClick(object sender, RoutedEventArgs e) => ToggleWordCountWindow();

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _stateBeforeFullScreen = WindowState;
            _styleBeforeFullScreen = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal; // necesario para que WPF cubra la barra de tareas
            WindowState = WindowState.Maximized;
            MainMenu.Visibility = Visibility.Collapsed;
            _fullScreen = true;
            StatusText.Text = "Pantalla completa (F11 para salir)";
        }
        else
        {
            WindowStyle = _styleBeforeFullScreen;
            WindowState = _stateBeforeFullScreen;
            MainMenu.Visibility = Visibility.Visible;
            _fullScreen = false;
        }
    }

    private void ToggleAlwaysOnTop()
    {
        Topmost = !Topmost;
        App.Settings.AlwaysOnTop = Topmost;
        App.SaveSettings();
    }

    /// <summary>
    /// Acercar, alejar o tamaño real. En pantalla partida actúa sobre la mitad activa (código o
    /// resultado) de forma independiente; en los demás modos, sobre toda la página.
    /// </summary>
    private async Task ZoomAsync(string action, string? pane = null)
    {
        if (!_editorReady)
        {
            return;
        }

        if (_mode == EditorMode.Split)
        {
            var result = await _editor.ZoomPaneAsync(action, pane);
            if (!string.IsNullOrEmpty(result))
            {
                StatusText.Text = result + " · Ctrl+rueda sobre cada mitad para ajustarla por separado";
                return;
            }
        }

        if (pane is not null)
        {
            StatusText.Text = "El tamaño del código y del resultado por separado solo se aplica en la pantalla partida.";
            return;
        }

        SetZoom(action switch
        {
            "in" => _editor.ZoomFactor + 0.1,
            "out" => _editor.ZoomFactor - 0.1,
            _ => 1.0,
        });
    }

    private void SetZoom(double zoom)
    {
        if (!_editorReady)
        {
            return;
        }

        _editor.ZoomFactor = Math.Round(zoom, 2);
        App.Settings.Zoom = _editor.ZoomFactor;
        App.SaveSettings();
        StatusText.Text = $"Zoom {_editor.ZoomFactor:P0}";
    }

    private void SwitchWindow()
    {
        var windows = AllWindows.ToList();
        if (windows.Count < 2)
        {
            StatusText.Text = "Solo hay una ventana abierta (Ctrl+Mayús+N abre otra).";
            return;
        }

        var next = windows[(windows.IndexOf(this) + 1) % windows.Count];
        if (next.WindowState == WindowState.Minimized)
        {
            next.WindowState = WindowState.Normal;
        }

        next.Activate();
    }

    // ───────────────────────── Temas ─────────────────────────

    private static async Task SetThemeAsync(AppTheme theme)
    {
        App.Settings.Theme = theme;
        App.SaveSettings();
        foreach (var window in AllWindows)
        {
            ThemeManager.Apply(window, theme);
            if (window._editorReady)
            {
                await window._editor.SetThemeAsync(EditorTheme());
            }
        }
    }

    // ───────────────────────── Ayuda ─────────────────────────

    private async Task ShowWelcomeAsync()
    {
        var welcome = new WelcomeWindow(this, showAtStartup: !App.Settings.WelcomeShown || !App.Settings.WelcomeShown);
        welcome.ShowDialog();
        App.Settings.WelcomeShown = !welcome.ShowAtStartup;
        App.SaveSettings();
        switch (welcome.Action)
        {
            case WelcomeAction.NewDocument:
                await NewDocumentAsync();
                break;
            case WelcomeAction.OpenFile:
                await OpenFileDialogAsync();
                break;
            case WelcomeAction.OpenFolder:
                OpenFolderDialog();
                break;
        }
    }

    private async Task OpenMarkdownGuideAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            await ShowDocumentAsync(MarkdownDocument.CreateUnsaved(HelpContent.MarkdownGuide));
        }
    }

    private async Task OpenShortcutsAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var builder = new StringBuilder("# Atajos de teclado\n\n");
        foreach (var top in MainMenu.Items.OfType<MenuItem>())
        {
            var rows = new List<string>();
            CollectShortcuts(top, string.Empty, rows);
            if (rows.Count == 0)
            {
                continue;
            }

            builder.Append("## ").Append(MenuText(top)).Append("\n\n| Acción | Atajo |\n| --- | --- |\n");
            foreach (var row in rows)
            {
                builder.Append(row).Append('\n');
            }

            builder.Append('\n');
        }

        await ShowDocumentAsync(MarkdownDocument.CreateUnsaved(builder.ToString()));
    }

    private static void CollectShortcuts(MenuItem menu, string prefix, List<string> rows)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            var text = prefix + MenuText(item);
            if (!string.IsNullOrEmpty(item.InputGestureText))
            {
                rows.Add($"| {text} | `{item.InputGestureText}` |");
            }

            CollectShortcuts(item, text + " › ", rows);
        }
    }

    private static string MenuText(MenuItem item) => (item.Header switch
    {
        AccessText a => a.Text,
        string s => s,
        _ => item.Header?.ToString() ?? string.Empty,
    }).Replace("_", string.Empty, StringComparison.Ordinal).Replace("…", string.Empty, StringComparison.Ordinal);

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
            MessageBox.Show(this, "Tienes la última versión.", "Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnApplyUpdate(object sender, RoutedEventArgs e) => Run(ApplyUpdateAsync)();

    private async Task ApplyUpdateAsync()
    {
        foreach (var window in AllWindows.ToList())
        {
            if (!await window.ConfirmDiscardAsync())
            {
                return;
            }

            window._closingConfirmed = true;
        }

        _updates.ApplyAndRestart();
    }

    private static void OpenLogsFolder()
    {
        Directory.CreateDirectory(App.Paths.LogsFolder);
        Process.Start(new ProcessStartInfo(App.Paths.LogsFolder) { UseShellExecute = true });
    }

    private void ShowAbout()
    {
        var version = _updates.CurrentVersion ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        MessageBox.Show(this,
            $"mdViewer {version}\n\nEditor Markdown libre y de código abierto (MIT).\n{Updates.UpdateService.RepositoryUrl}\n\n" +
            "Usa Vditor (MIT), KaTeX (MIT), Mermaid (MIT), highlight.js (BSD) y Velopack (MIT).",
            "Acerca de mdViewer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ───────────────────────── Arrastrar y soltar ─────────────────────────

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        try
        {
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    OpenFolder(file);
                }
                else if (MarkdownFiles.IsMarkdown(file))
                {
                    await OpenOrActivateAsync(file);
                    break;
                }
                else
                {
                    await InsertFileAsync(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo procesar el archivo soltado", ex);
        }
    }
}

internal static class ActionExtensions
{
    public static RoutedEventHandler ToRouted(this Action action) => (_, _) => action();
}
