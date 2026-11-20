using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MdViewer.App.Editor;
using MdViewer.Core.Export;
using MdViewer.Core.Settings;
using AppTheme = MdViewer.Core.Settings.ThemeMode;

namespace MdViewer.App;

/// <summary>Menús y atajos de teclado. Cada atajo se define una sola vez: rotula el menú y registra la tecla.</summary>
public partial class MainWindow
{
    private readonly List<(MenuItem Item, Func<bool> IsChecked)> _checkables = [];
    private MenuItem _recentMenu = null!;

    private void BuildMenus()
    {
        MainMenu.Items.Clear();
        _checkables.Clear();
        BuildFileMenu();
        BuildEditMenu();
        BuildParagraphMenu();
        BuildFormatMenu();
        BuildViewMenu();
        BuildThemesMenu();
        BuildHelpMenu();
        RefreshRecentMenu();
    }

    // ───────────── Archivo ─────────────

    private void BuildFileMenu()
    {
        var m = TopMenu("_Archivo");
        Item(m, "_Nuevo", Run(NewDocumentAsync), "Ctrl+N", K(Key.N, ModifierKeys.Control));
        Item(m, "Nueva _ventana", NewWindow, "Ctrl+Mayús+N", K(Key.N, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(m);
        Item(m, "_Abrir…", Run(OpenFileDialogAsync), "Ctrl+O", K(Key.O, ModifierKeys.Control));
        Item(m, "Abrir _carpeta…", OpenFolderDialog);
        Sep(m);
        Item(m, "Abrir _rápidamente…", Run(QuickOpenAsync), "Ctrl+P", K(Key.P, ModifierKeys.Control));
        _recentMenu = Sub(m, "Abrir r_eciente");
        var encodings = Sub(m, "Reabrir con _codificación");
        foreach (var (name, codePage, bom) in ReopenEncodings)
        {
            Item(encodings, name, Run(() => ReopenWithEncodingAsync(codePage, bom)));
        }

        Item(m, "Recargar desde el _disco", Run(ReloadFromDiskAsync));
        Sep(m);
        Item(m, "_Guardar", Run(() => SaveAsync()), "Ctrl+S", K(Key.S, ModifierKeys.Control));
        Item(m, "Guardar c_omo…", Run(() => SaveAsync(forceDialog: true)), "Ctrl+Mayús+S", K(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "_Mover a…", Run(MoveToAsync));
        Item(m, "Guardar _todo", Run(SaveAllAsync));
        Sep(m);
        Item(m, "_Propiedades…", Run(ShowPropertiesAsync));
        Item(m, "Abrir _ubicación del archivo", OpenFileLocation);
        Item(m, "Mostrar en la _barra lateral", RevealInSidebar);
        Item(m, "_Eliminar…", Run(DeleteFileAsync));
        Sep(m);
        Item(m, "_Importar…", Run(ImportAsync));
        var export = Sub(m, "E_xportar");
        Item(export, "_PDF…", Run(ExportPdfAsync));
        Item(export, "_HTML…", Run(() => ExportHtmlAsync(withStyles: true)));
        Item(export, "HTML _sin estilos…", Run(() => ExportHtmlAsync(withStyles: false)));
        Item(export, "_Imagen (PNG)…", Run(ExportImageAsync));
        Sep(export);
        foreach (var format in Pandoc.ExportFormats)
        {
            Item(export, format.Name + "…", Run(() => ExportWithPandocAsync(format)));
        }

        Sep(export);
        Item(export, "Exportar de nuevo con la misma configuración", Run(ExportAgainAsync));
        Item(m, "Imp_rimir…", Run(PrintAsync), "Alt+Mayús+P", K(Key.P, ModifierKeys.Alt | ModifierKeys.Shift));
        Sep(m);
        Item(m, "Pre_ferencias…", Run(ShowPreferencesAsync), "Ctrl+,", K(Key.OemComma, ModifierKeys.Control));
        Sep(m);
        Item(m, "Ce_rrar", Close, "Ctrl+W", K(Key.W, ModifierKeys.Control));
    }

    private static readonly (string Name, int CodePage, bool Bom)[] ReopenEncodings =
    [
        ("UTF-8", 65001, false),
        ("UTF-8 con BOM", 65001, true),
        ("UTF-16 LE", 1200, true),
        ("UTF-16 BE", 1201, true),
        ("Windows-1252 (Europa occidental)", 1252, false),
        ("ISO-8859-1 (Latín 1)", 28591, false),
        ("ISO-8859-15 (Latín 9)", 28605, false),
        ("DOS 850 (Europa occidental)", 850, false),
    ];

    // ───────────── Editar ─────────────

    private void BuildEditMenu()
    {
        var m = TopMenu("_Editar");
        // Deshacer, cortar, copiar, pegar y seleccionar todo los gestiona el propio editor con su atajo
        // nativo: en el menú solo se muestran (no se registran como atajo de la ventana).
        Item(m, "_Deshacer", Exec("undo"), "Ctrl+Z");
        Item(m, "_Rehacer", Exec("redo"), "Ctrl+Y");
        Sep(m);
        Item(m, "Cor_tar", Exec("cut"), "Ctrl+X");
        Item(m, "_Copiar", Exec("copy"), "Ctrl+C");
        Item(m, "Copiar _imagen", Run(CopyImageAsync));
        Item(m, "_Pegar", Run(PasteFromClipboardAsync), "Ctrl+V");
        Sep(m);
        Item(m, "Copiar como texto sin _formato", Run(CopyAsPlainTextAsync));
        Item(m, "Copiar como _Markdown", Run(CopyAsMarkdownAsync), "Ctrl+Mayús+C", K(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "Copiar como código _HTML", Run(CopyAsHtmlAsync));
        Item(m, "Pegar como te_xto sin formato", Run(PastePlainTextAsync), "Ctrl+Mayús+V", K(Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(m);
        var selection = Sub(m, "_Selección");
        Item(selection, "Seleccionar _todo", Exec("select-all"), "Ctrl+A");
        Item(selection, "Seleccionar _línea", Exec("select-line"), "Ctrl+L", K(Key.L, ModifierKeys.Control));
        Item(selection, "Seleccionar _palabra", Exec("select-word"), "Ctrl+D", K(Key.D, ModifierKeys.Control));
        Item(selection, "Seleccionar _bloque o celda", Exec("select-block"), "Ctrl+E", K(Key.E, ModifierKeys.Control));
        Item(m, "Mover _arriba", Exec("move-up"), "Alt+↑", K(Key.Up, ModifierKeys.Alt));
        Item(m, "Mover a_bajo", Exec("move-down"), "Alt+↓", K(Key.Down, ModifierKeys.Alt));
        Sep(m);
        Item(m, "_Eliminar", Exec("delete"), "Supr");
        var delete = Sub(m, "Eliminar _rango");
        Item(delete, "_Palabra", Exec("delete-word"), "Ctrl+Mayús+D", K(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        Item(delete, "_Línea", Exec("delete-line"));
        Item(delete, "_Bloque", Exec("delete-block"));
        Sep(m);
        var math = Sub(m, "_Fórmulas");
        Item(math, "_Refrescar fórmulas", Exec("refresh-math"));
        Check(m, "Puntuación _tipográfica", () => App.Settings.SmartPunctuation, Run(() => ToggleWritingSettingAsync(s => s.SmartPunctuation = !s.SmartPunctuation)));
        Check(m, "Cerrar _paréntesis automáticamente", () => App.Settings.AutoPair, Run(() => ToggleWritingSettingAsync(s => s.AutoPair = !s.AutoPair)));
        var endings = Sub(m, "Saltos de _línea");
        Check(endings, "CRLF (Windows)", () => _document.Format.LineEnding == Core.Documents.LineEnding.CrLf, () => SetLineEnding(Core.Documents.LineEnding.CrLf));
        Check(endings, "LF (Unix, macOS)", () => _document.Format.LineEnding == Core.Documents.LineEnding.Lf, () => SetLineEnding(Core.Documents.LineEnding.Lf));
        var whitespace = Sub(m, "Espacios en _blanco");
        Item(whitespace, "Quitar espacios al final de las líneas", Run(TrimTrailingWhitespaceAsync));
        Item(whitespace, "Convertir tabulaciones en espacios", Run(TabsToSpacesAsync));
        Check(m, "Corrector _ortográfico", () => App.Settings.SpellCheck, Run(() => ToggleWritingSettingAsync(s => s.SpellCheck = !s.SpellCheck)));
        Sep(m);
        var find = Sub(m, "_Buscar y reemplazar");
        Item(find, "_Buscar…", Run(() => OpenFindAsync(replace: false)), "Ctrl+F", K(Key.F, ModifierKeys.Control));
        Item(find, "Buscar _siguiente", Run(() => FindBarPanel.StepAsync(1)), "F3", K(Key.F3, ModifierKeys.None));
        Item(find, "Buscar _anterior", Run(() => FindBarPanel.StepAsync(-1)), "Mayús+F3", K(Key.F3, ModifierKeys.Shift));
        Item(find, "_Reemplazar…", Run(() => OpenFindAsync(replace: true)), "Ctrl+H", K(Key.H, ModifierKeys.Control));
        Sep(find);
        Item(find, "Buscar en la _carpeta…", Run(SearchInFolderAsync), "Ctrl+Mayús+F", K(Key.F, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(m);
        Item(m, "E_mojis y símbolos", Run(ShowEmojiPanelAsync), "Win+.");
    }

    // ───────────── Párrafo ─────────────

    private void BuildParagraphMenu()
    {
        var m = TopMenu("_Párrafo");
        for (var level = 1; level <= 6; level++)
        {
            Item(m, $"Título _{level}", Exec($"heading-{level}"), $"Ctrl+{level}", K(Key.D0 + level, ModifierKeys.Control));
        }

        Item(m, "_Párrafo", Exec("paragraph"), "Ctrl+0", K(Key.D0, ModifierKeys.Control));
        Sep(m);
        Item(m, "_Subir nivel de título", Exec("heading-up"), "Ctrl++", K(Key.OemPlus, ModifierKeys.Control));
        Item(m, "_Bajar nivel de título", Exec("heading-down"), "Ctrl+-", K(Key.OemMinus, ModifierKeys.Control));
        Sep(m);
        var table = Sub(m, "_Tabla");
        Item(table, "_Insertar tabla…", Run(InsertTableAsync), "Ctrl+T", K(Key.T, ModifierKeys.Control));
        Sep(table);
        Item(table, "Añadir fila _encima", Exec("table-row-above"));
        Item(table, "Añadir fila _debajo", Exec("table-row-below"), "Ctrl+Intro");
        Item(table, "Añadir columna a la i_zquierda", Exec("table-col-left"));
        Item(table, "Añadir columna a la de_recha", Exec("table-col-right"));
        Sep(table);
        Item(table, "Eliminar _fila", Exec("table-row-delete"));
        Item(table, "Eliminar _columna", Exec("table-col-delete"));
        Item(table, "Eliminar _tabla", Exec("table-delete"));
        Sep(table);
        Item(table, "Alinear a la izquierda", Exec("table-align-left"));
        Item(table, "Centrar", Exec("table-align-center"));
        Item(table, "Alinear a la derecha", Exec("table-align-right"));
        Item(m, "Bloque de _fórmula", Exec("math-block"), "Ctrl+Mayús+M", K(Key.M, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "Bloque de _código", Exec("code-block"), "Ctrl+Mayús+K", K(Key.K, ModifierKeys.Control | ModifierKeys.Shift));
        var code = Sub(m, "_Herramientas de código");
        Item(code, "_Copiar el código del bloque", Run(CopyCodeBlockAsync));
        var alert = Sub(m, "_Aviso");
        Item(alert, "_Nota", Exec("alert", "NOTE"));
        Item(alert, "_Consejo", Exec("alert", "TIP"));
        Item(alert, "_Importante", Exec("alert", "IMPORTANT"));
        Item(alert, "_Advertencia", Exec("alert", "WARNING"));
        Item(alert, "_Precaución", Exec("alert", "CAUTION"));
        Sep(m);
        Item(m, "C_ita", Exec("quote"), "Ctrl+Mayús+Q", K(Key.Q, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(m);
        Item(m, "Lista _numerada", Exec("ordered-list"), "Ctrl+Mayús+'", K(Key.Oem4, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "Lista con _viñetas", Exec("list"), "Ctrl+Mayús+¡", K(Key.Oem6, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "Lista de _tareas", Exec("check"), "Ctrl+Mayús+X", K(Key.X, ModifierKeys.Control | ModifierKeys.Shift));
        var task = Sub(m, "_Estado de la tarea");
        Item(task, "Marcar como _hecha", Exec("task-done"));
        Item(task, "Marcar como _pendiente", Exec("task-todo"));
        Item(task, "_Alternar", Exec("task-toggle"));
        var level2 = Sub(m, "Nivel de _lista");
        Item(level2, "_Aumentar sangría", Exec("indent"), "Tab");
        Item(level2, "_Reducir sangría", Exec("outdent"), "Mayús+Tab");
        Sep(m);
        Item(m, "Insertar párrafo _antes", Exec("insert-before"));
        Item(m, "Insertar párrafo _después", Exec("insert-after"));
        Sep(m);
        Item(m, "Enlace por _referencia", Exec("link-reference"));
        Item(m, "Nota al _pie", Exec("footnote"));
        Sep(m);
        Item(m, "_Línea horizontal", Exec("hr"));
        Item(m, "Í_ndice de contenidos", Exec("toc"));
        Item(m, "_Metadatos YAML", Exec("front-matter"));
    }

    // ───────────── Formato ─────────────

    private void BuildFormatMenu()
    {
        var m = TopMenu("F_ormato");
        Item(m, "_Negrita", Exec("bold"), "Ctrl+B", K(Key.B, ModifierKeys.Control));
        Item(m, "_Cursiva", Exec("italic"), "Ctrl+I", K(Key.I, ModifierKeys.Control));
        Item(m, "_Subrayado", Exec("underline"), "Ctrl+U", K(Key.U, ModifierKeys.Control));
        Item(m, "Có_digo", Exec("inline-code"), "Ctrl+Mayús+ñ", K(Key.Oem3, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(m);
        Item(m, "_Tachado", Exec("strike"), "Alt+Mayús+5", K(Key.D5, ModifierKeys.Alt | ModifierKeys.Shift));
        Item(m, "Co_mentario", Exec("comment"));
        Sep(m);
        Item(m, "_Enlace", Exec("link"), "Ctrl+K", K(Key.K, ModifierKeys.Control));
        var link = Sub(m, "_Acciones del enlace");
        Item(link, "_Abrir enlace", Run(OpenLinkAtCursorAsync));
        Item(link, "_Copiar dirección", Run(CopyLinkAddressAsync));
        Item(link, "_Quitar enlace (dejar el texto)", Exec("link-remove"));
        var image = Sub(m, "_Imagen");
        Item(image, "_Insertar imagen…", Run(InsertImageFromFileAsync), "Ctrl+Mayús+I", K(Key.I, ModifierKeys.Control | ModifierKeys.Shift));
        Sep(image);
        var policy = Sub(image, "Al insertar una imagen _local");
        Check(policy, "_Dejarla donde está", () => App.Settings.ImageInsertPolicy == ImageInsertPolicy.KeepInPlace, () => SetImagePolicy(ImageInsertPolicy.KeepInPlace));
        Check(policy, "Copiarla a ./_assets", () => App.Settings.ImageInsertPolicy == ImageInsertPolicy.CopyToAssets, () => SetImagePolicy(ImageInsertPolicy.CopyToAssets));
        Check(policy, "Copiarla a ./<_documento>.assets", () => App.Settings.ImageInsertPolicy == ImageInsertPolicy.CopyToDocumentAssets, () => SetImagePolicy(ImageInsertPolicy.CopyToDocumentAssets));
        Check(policy, "Copiarla a una _carpeta propia…", () => App.Settings.ImageInsertPolicy == ImageInsertPolicy.CopyToCustomFolder, ChooseCustomImageFolder);
        Check(image, "Usar rutas _relativas", () => App.Settings.PreferRelativeImagePaths, () => { App.Settings.PreferRelativeImagePaths = !App.Settings.PreferRelativeImagePaths; App.SaveSettings(); });
        Sep(image);
        Item(image, "_Copiar imagen", Run(CopyImageAsync));
        Item(image, "Abrir _ubicación de la imagen", Run(OpenImageLocationAsync));
        Sep(image);
        Item(image, "_Mover todas las imágenes a…", Run(() => RelocateAllImagesAsync(move: true)));
        Item(image, "Copiar _todas las imágenes a…", Run(() => RelocateAllImagesAsync(move: false)));
        Item(image, "_Descargar las imágenes de internet", Run(DownloadRemoteImagesAsync));
        Sep(image);
        Item(image, "_Recargar imágenes", Run(ReloadImagesAsync));
        Sep(m);
        Item(m, "_Borrar formato", Exec("clear-format"), "Ctrl+º", K(Key.Oem5, ModifierKeys.Control));
    }

    // ───────────── Ver ─────────────

    private void BuildViewMenu()
    {
        var m = TopMenu("_Ver");
        Check(m, "_Barra lateral", () => App.Settings.SidebarVisible, ToggleSidebar, "Ctrl+Mayús+L", K(Key.L, ModifierKeys.Control | ModifierKeys.Shift));
        Check(m, "Í_ndice", () => App.Settings.SidebarVisible && SidebarPanel.CurrentTab == SidebarTab.Outline, () => ShowSidebarTab(SidebarTab.Outline), "Ctrl+Mayús+1", K(Key.D1, ModifierKeys.Control | ModifierKeys.Shift));
        Check(m, "_Documentos", () => App.Settings.SidebarVisible && SidebarPanel.CurrentTab == SidebarTab.Files && SidebarPanel.FilesView == FilesView.List, () => ShowFiles(FilesView.List), "Ctrl+Mayús+2", K(Key.D2, ModifierKeys.Control | ModifierKeys.Shift));
        Check(m, "Árbol de _archivos", () => App.Settings.SidebarVisible && SidebarPanel.CurrentTab == SidebarTab.Files && SidebarPanel.FilesView == FilesView.Tree, () => ShowFiles(FilesView.Tree), "Ctrl+Mayús+3", K(Key.D3, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "Buscar en la _carpeta", Run(SearchInFolderAsync), "Ctrl+Mayús+F");
        Sep(m);
        Check(m, "Modo _código fuente", () => _mode == EditorMode.Source, Run(() => SetModeAsync(_mode == EditorMode.Source ? EditorMode.Live : EditorMode.Source)), "Ctrl+ç", K(Key.OemQuestion, ModifierKeys.Control));
        RegisterKey(K(Key.D7, ModifierKeys.Control | ModifierKeys.Shift), Run(() => SetModeAsync(_mode == EditorMode.Source ? EditorMode.Live : EditorMode.Source)));
        RegisterKey(K(Key.Divide, ModifierKeys.Control), Run(() => SetModeAsync(_mode == EditorMode.Source ? EditorMode.Live : EditorMode.Source)));
        Check(m, "_Pantalla partida (código y resultado)", () => _mode == EditorMode.Split, Run(() => SetModeAsync(_mode == EditorMode.Split ? EditorMode.Live : EditorMode.Split)));
        Check(m, "Modo _solo lectura", () => _readOnly, Run(ToggleReadOnlyAsync));
        Check(m, "_Números de línea en el código", () => App.Settings.LineNumbers, Run(ToggleLineNumbersAsync));
        Sep(m);
        Check(m, "Modo _concentración", () => _focusMode, Run(ToggleFocusModeAsync), "F8", K(Key.F8, ModifierKeys.None));
        Check(m, "Modo _máquina de escribir", () => _typewriterMode, Run(ToggleTypewriterModeAsync), "F9", K(Key.F9, ModifierKeys.None));
        Sep(m);
        Check(m, "Barra de _estado", () => App.Settings.StatusBarVisible, ToggleStatusBar);
        Check(m, "Barra de _herramientas", () => App.Settings.ToolbarVisible, Run(ToggleToolbarAsync));
        Item(m, "_Recuento de palabras", ToggleWordCountWindow);
        Sep(m);
        Check(m, "_Pantalla completa", () => _fullScreen, ToggleFullScreen, "F11", K(Key.F11, ModifierKeys.None));
        Check(m, "Siempre _encima", () => Topmost, ToggleAlwaysOnTop);
        Sep(m);
        Item(m, "_Tamaño real", Run(() => ZoomAsync("reset")), "Ctrl+Mayús+9", K(Key.D9, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "_Acercar", Run(() => ZoomAsync("in")), "Ctrl+Mayús++", K(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
        Item(m, "A_lejar", Run(() => ZoomAsync("out")), "Ctrl+Mayús+-", K(Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
        var sourceSize = Sub(m, "Tamaño del _código (pantalla partida)");
        Item(sourceSize, "_Acercar", Run(() => ZoomAsync("in", "source")));
        Item(sourceSize, "A_lejar", Run(() => ZoomAsync("out", "source")));
        Item(sourceSize, "_Tamaño real", Run(() => ZoomAsync("reset", "source")));
        var previewSize = Sub(m, "Tamaño del _resultado (pantalla partida)");
        Item(previewSize, "_Acercar", Run(() => ZoomAsync("in", "preview")));
        Item(previewSize, "A_lejar", Run(() => ZoomAsync("out", "preview")));
        Item(previewSize, "_Tamaño real", Run(() => ZoomAsync("reset", "preview")));
        Sep(m);
        Item(m, "Cambiar entre _ventanas abiertas", SwitchWindow, "Ctrl+Tab", K(Key.Tab, ModifierKeys.Control));
        Sep(m);
        Item(m, "Herramientas de _desarrollo", () => _editor.OpenDevTools(), "Mayús+F12", K(Key.F12, ModifierKeys.Shift));
    }

    // ───────────── Temas ─────────────

    private void BuildThemesMenu()
    {
        var m = TopMenu("_Temas");
        Check(m, "_Azul (predeterminado)", () => App.Settings.Theme == AppTheme.Blue, Run(() => SetThemeAsync(AppTheme.Blue)));
        Check(m, "_Claro", () => App.Settings.Theme == AppTheme.Light, Run(() => SetThemeAsync(AppTheme.Light)));
        Check(m, "_Oscuro", () => App.Settings.Theme == AppTheme.Dark, Run(() => SetThemeAsync(AppTheme.Dark)));
        Check(m, "Según el _sistema", () => App.Settings.Theme == AppTheme.System, Run(() => SetThemeAsync(AppTheme.System)));
    }

    // ───────────── Ayuda ─────────────

    private void BuildHelpMenu()
    {
        var m = TopMenu("Ay_uda");
        Item(m, "_Bienvenida", Run(ShowWelcomeAsync));
        Item(m, "_Guía de Markdown", Run(OpenMarkdownGuideAsync));
        Item(m, "_Atajos de teclado", Run(OpenShortcutsAsync));
        Sep(m);
        Item(m, "Buscar _actualizaciones", Run(() => CheckForUpdatesAsync(interactive: true)));
        Item(m, "Abrir carpeta de _registros", OpenLogsFolder);
        Item(m, "Página del _proyecto", () => OpenLink(Updates.UpdateService.RepositoryUrl));
        Sep(m);
        Item(m, "Acerca _de mdViewer", ShowAbout);
    }

    // ───────────── Construcción ─────────────

    private MenuItem TopMenu(string header)
    {
        var item = new MenuItem
        {
            Header = new AccessText { Text = header },
            Padding = new Thickness(7, 2, 7, 2),
            Foreground = SystemColors.MenuTextBrush,
        };
                ((AccessText)item.Header).SetResourceReference(TextBlock.ForegroundProperty, "MenuBarForegroundBrush");
        AutomationProperties.SetName(item, header.Replace("_", string.Empty, StringComparison.Ordinal));
        item.SubmenuOpened += (_, _) => RefreshChecks();
        MainMenu.Items.Add(item);
        return item;
    }

    private static MenuItem Sub(MenuItem parent, string header)
    {
        var item = new MenuItem { Header = header };
        parent.Items.Add(item);
        return item;
    }

    private static void Sep(MenuItem parent) => parent.Items.Add(new Separator());

    private MenuItem Item(MenuItem parent, string header, Action action, string? gestureText = null, KeyGesture? key = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gestureText ?? string.Empty };
        item.Click += (_, _) => action();
        parent.Items.Add(item);
        if (key is not null)
        {
            RegisterKey(key, action);
        }

        return item;
    }

    private void Check(MenuItem parent, string header, Func<bool> isChecked, Action action, string? gestureText = null, KeyGesture? key = null)
    {
        var item = Item(parent, header, action, gestureText, key);
        item.IsCheckable = false; // el estado lo refrescamos nosotros al abrir el menú
        _checkables.Add((item, isChecked));
    }

    private void RefreshChecks()
    {
        foreach (var (item, isChecked) in _checkables)
        {
            item.IsChecked = isChecked();
        }
    }

    private void RegisterKey(KeyGesture gesture, Action action)
        => InputBindings.Add(new KeyBinding(new DelegateCommand(action), gesture));

    private static KeyGesture K(Key key, ModifierKeys modifiers) => new(key, modifiers);

    /// <summary>Comando del editor (se ejecuta dentro de la página).</summary>
    private Action Exec(string command, string? argument = null) => Run(async () =>
    {
        if (_editorReady)
        {
            if (_readOnly && command is not ("copy" or "select-all" or "select-line" or "select-word" or "select-block"))
            {
                StatusText.Text = "El documento está en modo solo lectura.";
                return;
            }

            await _editor.ExecAsync(command, argument);
        }
    });

    /// <summary>Envuelve una acción asíncrona: los errores se registran y se muestran sin cerrar la aplicación.</summary>
    private Action Run(Func<Task> action) => async () =>
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Error al ejecutar un comando de menú", ex);
            MessageBox.Show(this, $"No se pudo completar la acción:\n\n{ex.Message}", "mdViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    };

    private sealed class DelegateCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
