using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MdViewer.Core.Export;
using MdViewer.Core.Settings;
using Microsoft.Win32;

namespace MdViewer.App.Dialogs;

/// <summary>Preferencias (Ctrl+,). Trabaja sobre una copia y solo la aplica al aceptar.</summary>
public sealed class PreferencesWindow : DialogBase
{
    private readonly AppSettings _s;

    // General
    private readonly ComboBox _theme = Combo("Azul (predeterminado)", "Claro", "Oscuro", "Según el sistema");
    private readonly CheckBox _reopen = Check("Reabrir el último documento al iniciar");
    private readonly CheckBox _updates = Check("Buscar actualizaciones automáticamente");
    private readonly CheckBox _welcome = Check("Mostrar la pantalla de bienvenida al iniciar");

    // Guardado
    private readonly CheckBox _autoSave = Check("Guardar automáticamente");
    private readonly TextBox _autoSaveMinutes = new() { Width = 60 };

    // Editor
    private readonly CheckBox _spell = Check("Corrector ortográfico");
    private readonly CheckBox _smart = Check("Puntuación tipográfica (comillas “ ”, raya —, puntos suspensivos …)");
    private readonly CheckBox _pair = Check("Cerrar automáticamente paréntesis, corchetes y llaves");

    // Imágenes
    private readonly ComboBox _imagePolicy = Combo("Dejar la imagen donde está", "Copiar a ./assets", "Copiar a ./<nombre del documento>.assets", "Copiar a una carpeta propia");
    private readonly TextBox _imageFolder = new();
    private readonly CheckBox _relative = Check("Usar rutas relativas cuando sea posible");

    // Exportar
    private readonly TextBox _pandoc = new();
    private readonly ComboBox _paper = Combo("A4", "A5", "A3", "Carta", "Legal");
    private readonly TextBox _margin = new() { Width = 60 };

    public PreferencesWindow(Window owner, AppSettings settings)
        : base(owner, "Preferencias", 620, 560)
    {
        _s = settings;
        ResizeMode = ResizeMode.CanResize;

        _theme.SelectedIndex = (int)settings.Theme;
        _reopen.IsChecked = settings.ReopenLastFile;
        _updates.IsChecked = settings.CheckForUpdates;
        _welcome.IsChecked = !settings.WelcomeShown;
        _autoSave.IsChecked = settings.AutoSave;
        _autoSaveMinutes.Text = Math.Max(1, settings.AutoSaveIntervalSeconds / 60).ToString(CultureInfo.CurrentCulture);
        _spell.IsChecked = settings.SpellCheck;
        _smart.IsChecked = settings.SmartPunctuation;
        _pair.IsChecked = settings.AutoPair;
        _imagePolicy.SelectedIndex = (int)settings.ImageInsertPolicy;
        _imageFolder.Text = settings.ImageCustomFolder ?? string.Empty;
        _relative.IsChecked = settings.PreferRelativeImagePaths;
        _pandoc.Text = settings.PandocPath ?? string.Empty;
        _paper.SelectedIndex = (int)settings.PdfPaperSize;
        _margin.Text = settings.PdfMarginMillimeters.ToString(CultureInfo.CurrentCulture);

        var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
        tabs.Items.Add(Tab("General", Label("Tema"), _theme, Spacer(), _reopen, _updates, _welcome,
            Spacer(), Label("Guardado"), _autoSave, Row(new TextBlock { Text = "Cada", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, _autoSaveMinutes, new TextBlock { Text = "minutos", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }),
            Note("Aunque el guardado automático esté desactivado, mdViewer guarda borradores de recuperación cada 30 segundos.")));
        tabs.Items.Add(Tab("Editor", _spell, _smart, _pair, Spacer(),
            Note("El autocompletado de emojis se activa escribiendo dos puntos, por ejemplo :sonrisa.")));
        var browse = new Button { Content = "Examinar…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Carpeta para las imágenes" };
            if (dialog.ShowDialog(this) == true)
            {
                _imageFolder.Text = dialog.FolderName;
            }
        };
        tabs.Items.Add(Tab("Imágenes", Label("Al insertar una imagen local"), _imagePolicy, Label("Carpeta propia (ruta absoluta o relativa al documento)", 10), RowStretch(_imageFolder, browse), Spacer(), _relative,
            Note("Las imágenes pegadas desde el portapapeles siempre se guardan como archivo; si la opción es «Dejar donde está», se usa ./assets.")));
        var browsePandoc = new Button { Content = "Examinar…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
        browsePandoc.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "pandoc.exe|pandoc.exe", Title = "Ubicación de Pandoc" };
            if (dialog.ShowDialog(this) == true)
            {
                _pandoc.Text = dialog.FileName;
            }
        };
        var found = Pandoc.Locate(settings.PandocPath);
        tabs.Items.Add(Tab("Exportar", Label("PDF: tamaño de papel"), _paper, Label("PDF: márgenes (mm)", 10), _margin, Spacer(),
            Label("Pandoc (para Word, EPUB, LaTeX… e importar)"), RowStretch(_pandoc, browsePandoc),
            Note(found is null ? "Pandoc no está instalado. Descárgalo gratis en pandoc.org." : $"Pandoc encontrado: {found}")));

        var ok = new Button { Content = "Aceptar", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) =>
        {
            Apply();
            DialogResultSafe(true);
            Close();
        };

        var root = new DockPanel();
        var buttons = Buttons(ok, cancel);
        buttons.Margin = new Thickness(12);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
    }

    private void Apply()
    {
        _s.Theme = (MdViewer.Core.Settings.ThemeMode)Math.Max(0, _theme.SelectedIndex);
        _s.ReopenLastFile = _reopen.IsChecked == true;
        _s.CheckForUpdates = _updates.IsChecked == true;
        _s.WelcomeShown = _welcome.IsChecked != true;
        _s.AutoSave = _autoSave.IsChecked == true;
        if (int.TryParse(_autoSaveMinutes.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minutes))
        {
            _s.AutoSaveIntervalSeconds = Math.Clamp(minutes, 1, 120) * 60;
        }

        _s.SpellCheck = _spell.IsChecked == true;
        _s.SmartPunctuation = _smart.IsChecked == true;
        _s.AutoPair = _pair.IsChecked == true;
        _s.ImageInsertPolicy = (ImageInsertPolicy)Math.Max(0, _imagePolicy.SelectedIndex);
        _s.ImageCustomFolder = string.IsNullOrWhiteSpace(_imageFolder.Text) ? null : _imageFolder.Text.Trim();
        _s.PreferRelativeImagePaths = _relative.IsChecked == true;
        _s.PandocPath = string.IsNullOrWhiteSpace(_pandoc.Text) ? null : _pandoc.Text.Trim();
        _s.PdfPaperSize = (PdfPaperSize)Math.Max(0, _paper.SelectedIndex);
        if (double.TryParse(_margin.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var margin))
        {
            _s.PdfMarginMillimeters = Math.Clamp(margin, 0, 60);
        }
    }

    private static ComboBox Combo(params string[] items)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 280 };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }

        return combo;
    }

    private static CheckBox Check(string text) => new() { Content = text, Margin = new Thickness(0, 4, 0, 4) };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };

    private static Border Spacer() => new Border { Height = 10 };

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 2, 0, 2) };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static DockPanel RowStretch(FrameworkElement main, Button side)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(side, Dock.Right);
        panel.Children.Add(side);
        panel.Children.Add(main);
        return panel;
    }

    private static TabItem Tab(string header, params UIElement[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new TabItem { Header = header, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }
}
