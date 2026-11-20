using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MdViewer.App.Editor;

namespace MdViewer.App.Controls;

/// <summary>Barra de buscar y reemplazar dentro del documento.</summary>
public partial class FindBar : UserControl
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private EditorBridge? _editor;

    public FindBar()
    {
        InitializeComponent();
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await FindAsync();
        };
        SetReplaceVisible(false);
    }

    public event EventHandler? Closed;

    public void Attach(EditorBridge editor) => _editor = editor;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(bool replace, string? initialText)
    {
        Visibility = Visibility.Visible;
        SetReplaceVisible(replace || ReplaceBox.Visibility == Visibility.Visible && replace);
        if (!string.IsNullOrEmpty(initialText) && !initialText.Contains('\n', StringComparison.Ordinal))
        {
            FindBox.Text = initialText;
        }

        FindBox.Focus();
        FindBox.SelectAll();
        _ = FindAsync();
    }

    /// <summary>Busca un texto concreto (p. ej. desde la búsqueda en la carpeta).</summary>
    public async Task SearchAsync(string query, bool caseSensitive, bool regex, bool wholeWord)
    {
        Visibility = Visibility.Visible;
        FindBox.Text = query;
        CaseToggle.IsChecked = caseSensitive;
        RegexToggle.IsChecked = regex;
        WordToggle.IsChecked = wholeWord;
        _debounce.Stop();
        await FindAsync();
    }

    public async Task CloseAsync()
    {
        Visibility = Visibility.Collapsed;
        if (_editor is not null)
        {
            await _editor.FindClearAsync();
            await _editor.FocusAsync();
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    public async Task StepAsync(int direction)
    {
        if (_editor is null)
        {
            return;
        }

        if (!IsOpen || string.IsNullOrEmpty(FindBox.Text))
        {
            Open(replace: false, initialText: null);
            return;
        }

        Show(await _editor.FindStepAsync(direction));
    }

    private void SetReplaceVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        ReplaceLabel.Visibility = v;
        ReplaceBox.Visibility = v;
        ReplaceButtons.Visibility = v;
        ReplaceToggleButton.Content = visible ? "Ocultar reemplazo" : "Reemplazar…";
    }

    private async Task FindAsync()
    {
        if (_editor is null)
        {
            return;
        }

        Show(await _editor.FindAsync(FindBox.Text, CaseToggle.IsChecked == true, RegexToggle.IsChecked == true, WordToggle.IsChecked == true));
    }

    private void Show(FindResult result)
    {
        CountText.Text = result.Error is not null
            ? "Expresión no válida"
            : string.IsNullOrEmpty(FindBox.Text) ? string.Empty
            : result.Count == 0 ? "Sin resultados" : $"{result.Current} de {result.Count}";
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e) => _ = FindAsync();

    private async void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await StepAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CloseAsync();
        }
    }

    private async void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ReplaceCurrentAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CloseAsync();
        }
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e) => await StepAsync(-1);

    private async void OnNextClick(object sender, RoutedEventArgs e) => await StepAsync(1);

    private void OnReplaceToggleClick(object sender, RoutedEventArgs e) => SetReplaceVisible(ReplaceBox.Visibility != Visibility.Visible);

    private async void OnCloseClick(object sender, RoutedEventArgs e) => await CloseAsync();

    private async void OnReplaceClick(object sender, RoutedEventArgs e) => await ReplaceCurrentAsync();

    private async Task ReplaceCurrentAsync()
    {
        if (_editor is null)
        {
            return;
        }

        await _editor.ReplaceCurrentAsync(ReplaceBox.Text);
        await Task.Delay(120); // el editor vuelve a renderizar tras la edición
        await FindAsync();
    }

    private async void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        var count = await _editor.ReplaceAllAsync(ReplaceBox.Text);
        CountText.Text = count == 0 ? "Sin resultados" : $"{count} reemplazados";
    }
}
