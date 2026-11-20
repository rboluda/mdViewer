using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MdViewer.Core.Files;

namespace MdViewer.App.Dialogs;

/// <summary>Apertura rápida (Ctrl+P): búsqueda aproximada entre los documentos de la carpeta y los recientes.</summary>
public sealed class QuickOpenWindow : DialogBase
{
    private readonly List<(string Path, string Display)> _candidates;
    private readonly TextBox _query = new() { FontSize = 15, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ListBox _results = new() { BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };

    public QuickOpenWindow(Window owner, string? folder, IEnumerable<string> recentFiles)
        : base(owner, "Abrir rápidamente", 640, 460)
    {
        var files = folder is not null ? MarkdownFiles.Enumerate(folder) : [];
        _candidates = recentFiles.Where(File.Exists)
            .Concat(files)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (p, folder is not null && p.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(folder, p) : p))
            .ToList();

        var root = new DockPanel { Margin = new Thickness(14) };
        var hint = new TextBlock { Text = "Escribe parte del nombre del documento · Intro para abrir", Opacity = 0.65, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
        DockPanel.SetDock(_query, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(_query);
        root.Children.Add(hint);
        root.Children.Add(_results);
        Content = root;

        _query.TextChanged += (_, _) => Filter();
        _query.PreviewKeyDown += OnQueryKeyDown;
        _results.MouseDoubleClick += (_, _) => Accept();
        Loaded += (_, _) =>
        {
            Filter();
            _query.Focus();
        };
    }

    public string? SelectedPath { get; private set; }

    private void Filter()
    {
        var query = _query.Text.Trim();
        var matches = _candidates
            .Select(c => (c.Path, c.Display, Score: MarkdownFiles.FuzzyScore(query, c.Display)))
            .Where(c => c.Score is not null)
            .OrderByDescending(c => c.Score)
            .Take(200);

        _results.Items.Clear();
        foreach (var (path, display, _) in matches)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = display, Opacity = 0.6, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = panel, Tag = path };
            System.Windows.Automation.AutomationProperties.SetName(item, display);
            _results.Items.Add(item);
        }

        if (_results.Items.Count > 0)
        {
            _results.SelectedIndex = 0;
        }
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _results.SelectedIndex = Math.Min(_results.Items.Count - 1, _results.SelectedIndex + 1);
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _results.SelectedIndex = Math.Max(0, _results.SelectedIndex - 1);
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
        }
    }

    private void Accept()
    {
        if (_results.SelectedItem is ListBoxItem { Tag: string path })
        {
            SelectedPath = path;
            DialogResultSafe(true);
            Close();
        }
    }
}
