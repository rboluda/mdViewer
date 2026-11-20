using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MdViewer.App.Dialogs;

public enum WelcomeAction
{
    None,
    NewDocument,
    OpenFile,
    OpenFolder,
}

/// <summary>Pantalla de bienvenida del primer arranque (también en Ayuda → Bienvenida).</summary>
public sealed class WelcomeWindow : DialogBase
{
    private readonly CheckBox _showAtStartup = new() { Content = "Mostrar esta pantalla al iniciar", VerticalAlignment = VerticalAlignment.Center };

    public WelcomeWindow(Window? owner, bool showAtStartup)
        : base(owner, "Bienvenido a mdViewer", 760, 560)
    {
        Background = Brushes.White;
        _showAtStartup.IsChecked = showAtStartup;
        var accent = (Brush)Application.Current.Resources["AccentBrush"];

        var root = new DockPanel();

        // Cabecera azul con el icono y el título
        var header = new Border { Background = accent, Padding = new Thickness(28, 22, 28, 22) };
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/mdViewer.ico")),
            Width = 56,
            Height = 56,
            Margin = new Thickness(0, 0, 18, 0),
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Bienvenido a mdViewer", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        titles.Children.Add(new TextBlock { Text = "Escribe en Markdown y ve el resultado al instante, en el mismo sitio.", FontSize = 14, Foreground = Brushes.White, Opacity = 0.9, Margin = new Thickness(0, 4, 0, 0) });
        headerPanel.Children.Add(titles);
        header.Child = headerPanel;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Pie con acciones
        var footer = new DockPanel { Margin = new Thickness(28, 12, 28, 18), LastChildFill = false };
        footer.Children.Add(_showAtStartup);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("Nuevo documento", WelcomeAction.NewDocument, primary: true));
        actions.Children.Add(ActionButton("Abrir archivo…", WelcomeAction.OpenFile));
        actions.Children.Add(ActionButton("Abrir carpeta…", WelcomeAction.OpenFolder));
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // Tarjetas
        var cards = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(22, 18, 22, 4) };
        cards.Children.Add(Card("✎", "Edición en vivo", "Los títulos, listas y tablas toman forma mientras escribes. Pulsa Ctrl+/ para ver el código fuente."));
        cards.Children.Add(Card("∑", "Mucho más que texto", "Fórmulas LaTeX, diagramas Mermaid, código resaltado, tareas, notas al pie y avisos."));
        cards.Children.Add(Card("☰", "Índice y archivos", "La barra lateral (Ctrl+Mayús+L) muestra el índice del documento y los archivos de la carpeta."));
        cards.Children.Add(Card("⌕", "Encuentra lo que buscas", "Busca en el documento (Ctrl+F), en toda la carpeta (Ctrl+Mayús+F) o abre un archivo con Ctrl+P."));
        cards.Children.Add(Card("⇪", "Comparte tu trabajo", "Exporta a PDF, HTML o imagen, e imprime. Con Pandoc, también a Word, EPUB y otros formatos."));
        cards.Children.Add(Card("◐", "A tu manera", "Temas azul, claro y oscuro, modo concentración (F8) y máquina de escribir (F9), y preferencias (Ctrl+,)."));
        root.Children.Add(cards);

        Content = root;
    }

    public WelcomeAction Action { get; private set; }

    public bool ShowAtStartup => _showAtStartup.IsChecked == true;

    private Button ActionButton(string text, WelcomeAction action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            IsDefault = primary,
        };
        if (primary)
        {
            button.Background = (Brush)Application.Current.Resources["AccentBrush"];
            button.Foreground = Brushes.White;
        }

        button.Click += (_, _) =>
        {
            Action = action;
            Close();
        };
        return button;
    }

    private static Border Card(string glyph, string title, string text)
    {
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = glyph, FontSize = 26, Foreground = accent, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12.5 });
        return new Border
        {
            Child = panel,
            Margin = new Thickness(6),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF7, 0xFE)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xE4, 0xF7)),
            BorderThickness = new Thickness(1),
        };
    }
}
