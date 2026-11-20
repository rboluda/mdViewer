using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MdViewer.App.Editor;
using MdViewer.Core.Documents;

namespace MdViewer.App.Dialogs;

/// <summary>Tamaño de la tabla a insertar (filas × columnas).</summary>
public sealed class TableSizeDialog : DialogBase
{
    private readonly TextBox _rows = new() { Text = "3", Width = 60 };
    private readonly TextBox _columns = new() { Text = "3", Width = 60 };

    public TableSizeDialog(Window owner)
        : base(owner, "Insertar tabla", 320, 210)
    {
        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 3; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddRow(grid, 0, "Filas (con la cabecera):", _rows);
        AddRow(grid, 1, "Columnas:", _columns);
        var ok = new Button { Content = "Insertar", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        ok.Click += (_, _) =>
        {
            DialogResultSafe(true);
            Close();
        };
        var buttons = Buttons(ok, cancel);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) =>
        {
            _rows.Focus();
            _rows.SelectAll();
        };
    }

    public int Rows => Math.Clamp(int.TryParse(_rows.Text, out var r) ? r : 3, 2, 100);

    public int Columns => Math.Clamp(int.TryParse(_columns.Text, out var c) ? c : 3, 1, 30);

    private static void AddRow(Grid grid, int row, string label, TextBox box)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
        Grid.SetRow(text, row);
        box.Margin = new Thickness(0, 4, 0, 4);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(text);
        grid.Children.Add(box);
    }
}

/// <summary>Propiedades del documento: ruta, tamaño, fechas, codificación y recuento.</summary>
public sealed class PropertiesWindow : DialogBase
{
    public PropertiesWindow(Window owner, MarkdownDocument document, DocumentStats? stats)
        : base(owner, "Propiedades", 520, 420)
    {
        var rows = new List<(string, string)>
        {
            ("Nombre", document.DisplayName),
            ("Ubicación", document.FilePath is null ? "(sin guardar)" : Path.GetDirectoryName(document.FilePath) ?? string.Empty),
        };

        if (document.FilePath is not null && File.Exists(document.FilePath))
        {
            var info = new FileInfo(document.FilePath);
            rows.Add(("Tamaño", FormatSize(info.Length)));
            rows.Add(("Creado", info.CreationTime.ToString("F", CultureInfo.CurrentCulture)));
            rows.Add(("Modificado", info.LastWriteTime.ToString("F", CultureInfo.CurrentCulture)));
        }

        rows.Add(("Codificación", EncodingName(document.Format)));
        rows.Add(("Saltos de línea", document.Format.LineEnding switch { LineEnding.Lf => "LF (Unix)", LineEnding.Cr => "CR (Mac antiguo)", _ => "CRLF (Windows)" }));
        if (stats is not null)
        {
            rows.Add(("Palabras", stats.Words.ToString("N0", CultureInfo.CurrentCulture)));
            rows.Add(("Caracteres", stats.Characters.ToString("N0", CultureInfo.CurrentCulture)));
            rows.Add(("Líneas", stats.Lines.ToString("N0", CultureInfo.CurrentCulture)));
        }

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Item1, Opacity = 0.7, Margin = new Thickness(0, 3, 8, 3) };
            var value = new TextBox { Text = rows[i].Item2, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        var close = new Button { Content = "Cerrar", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var buttons = Buttons(close);
        buttons.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(buttons, rows.Count);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
    }

    public static string EncodingName(TextFormat format)
    {
        var name = format.Encoding.CodePage switch
        {
            65001 => "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            1252 => "Windows-1252 (ANSI)",
            28591 => "ISO-8859-1",
            _ => format.Encoding.EncodingName,
        };
        return format.HasBom ? $"{name} con BOM" : name;
    }

    private static string FormatSize(long bytes)
        => bytes < 1024 ? $"{bytes} bytes" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:N1} KB" : $"{bytes / 1024.0 / 1024.0:N1} MB";
}

/// <summary>Ventana de recuento de palabras (se actualiza mientras se escribe).</summary>
public sealed class WordCountWindow : DialogBase
{
    private readonly TextBlock _text = new() { FontSize = 14, LineHeight = 24 };

    public WordCountWindow(Window owner)
        : base(owner, "Recuento de palabras", 300, 260)
    {
        ShowActivated = false;
        Topmost = false;
        Content = new Border { Padding = new Thickness(20), Child = _text };
    }

    public void Update(DocumentStats? stats)
    {
        if (stats is null)
        {
            _text.Text = "Sin datos todavía.";
            return;
        }

        var c = CultureInfo.CurrentCulture;
        _text.Text =
            $"Palabras:\t\t{stats.Words.ToString("N0", c)}\n" +
            $"Caracteres:\t\t{stats.Characters.ToString("N0", c)}\n" +
            $"Sin espacios:\t{stats.CharactersNoSpaces.ToString("N0", c)}\n" +
            $"Párrafos:\t\t{stats.Paragraphs.ToString("N0", c)}\n" +
            $"Líneas:\t\t{stats.Lines.ToString("N0", c)}\n" +
            $"Lectura:\t\t{(stats.ReadingMinutes <= 1 ? "≈ 1 minuto" : $"≈ {stats.ReadingMinutes} minutos")}";
    }
}
