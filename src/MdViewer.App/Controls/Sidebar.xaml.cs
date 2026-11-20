using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MdViewer.App.Editor;
using MdViewer.Core.Files;
using MdViewer.Core.Settings;

namespace MdViewer.App.Controls;

/// <summary>Barra lateral: archivos de la carpeta (árbol o lista), índice del documento y búsqueda en la carpeta.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Los recursos se liberan en Unloaded, que es el ciclo de vida de un control WPF.")]
public partial class Sidebar : UserControl
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<OutlineItem> _outline = [];
    private int _activeHeading = -1;
    private bool _syncingSelection;
    private CancellationTokenSource? _searchCancellation;

    public Sidebar()
    {
        InitializeComponent();
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshFiles();
        };
        Unloaded += (_, _) =>
        {
            _watcher?.Dispose();
            _searchCancellation?.Dispose();
        };
    }

    public event EventHandler<string>? FileOpenRequested;

    public event EventHandler? OpenFolderRequested;

    public event EventHandler<int>? HeadingRequested;

    public event EventHandler<SidebarTab>? TabChanged;

    public event EventHandler<FilesView>? FilesViewChanged;

    /// <summary>Resultado de búsqueda activado: archivo, consulta y opciones (para buscar dentro al abrirlo).</summary>
    public event EventHandler<(string Path, string Query, bool CaseSensitive, bool Regex, bool WholeWord)>? SearchResultActivated;

    public string? Folder { get; private set; }

    public SidebarTab CurrentTab { get; private set; } = SidebarTab.Outline;

    public FilesView FilesView { get; private set; } = FilesView.Tree;

    public string? CurrentFile { get; set; }

    // ───────────── Pestañas ─────────────

    public void ShowTab(SidebarTab tab)
    {
        CurrentTab = tab;
        (tab switch { SidebarTab.Files => FilesTab, SidebarTab.Search => SearchTab, _ => OutlineTab }).IsChecked = true;
        FilesPanel.Visibility = tab == SidebarTab.Files ? Visibility.Visible : Visibility.Collapsed;
        OutlinePanel.Visibility = tab == SidebarTab.Outline ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = tab == SidebarTab.Search ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        var tab = sender == FilesTab ? SidebarTab.Files : sender == SearchTab ? SidebarTab.Search : SidebarTab.Outline;
        if (tab != CurrentTab || FilesPanel.Visibility == Visibility.Collapsed && OutlinePanel.Visibility == Visibility.Collapsed && SearchPanel.Visibility == Visibility.Collapsed)
        {
            ShowTab(tab);
            TabChanged?.Invoke(this, tab);
        }
    }

    public void FocusSearch(string? query = null)
    {
        ShowTab(SidebarTab.Search);
        if (!string.IsNullOrEmpty(query))
        {
            SearchBox.Text = query;
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ───────────── Archivos ─────────────

    public void SetFilesView(FilesView view)
    {
        FilesView = view;
        FileTree.Visibility = view == FilesView.Tree && Folder is not null ? Visibility.Visible : Visibility.Collapsed;
        ArticleList.Visibility = view == FilesView.List && Folder is not null ? Visibility.Visible : Visibility.Collapsed;
        ViewToggleButton.Content = view == FilesView.Tree ? "☷" : "🗀";
        RefreshFiles();
    }

    private void OnViewToggleClick(object sender, RoutedEventArgs e)
    {
        SetFilesView(FilesView == FilesView.Tree ? FilesView.List : FilesView.Tree);
        FilesViewChanged?.Invoke(this, FilesView);
    }

    public void SetFolder(string? folder)
    {
        if (string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Folder = folder is not null && Directory.Exists(folder) ? folder : null;
        FolderName.Text = Folder is null ? string.Empty : Path.GetFileName(Folder.TrimEnd('\\')) is { Length: > 0 } name ? name : Folder;
        FolderName.ToolTip = Folder;
        NoFolderPanel.Visibility = Folder is null ? Visibility.Visible : Visibility.Collapsed;
        WatchFolder();
        SetFilesView(FilesView);
    }

    private void WatchFolder()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (Folder is null)
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = true, EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, e) =>
            {
                if (Directory.Exists(e.FullPath) || MarkdownFiles.IsMarkdown(e.FullPath))
                {
                    Dispatcher.BeginInvoke(() => { _refreshTimer.Stop(); _refreshTimer.Start(); });
                }
            };
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (s, e) => changed(s, e);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshFiles();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    public void RefreshFiles()
    {
        if (Folder is null)
        {
            FileTree.Items.Clear();
            ArticleList.Items.Clear();
            return;
        }

        if (FilesView == FilesView.Tree)
        {
            FileTree.Items.Clear();
            foreach (var item in CreateTreeItems(Folder))
            {
                FileTree.Items.Add(item);
            }

            if (CurrentFile is not null)
            {
                Reveal(CurrentFile);
            }
        }
        else
        {
            ArticleList.Items.Clear();
            var files = MarkdownFiles.Enumerate(Folder)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime);
            foreach (var file in files)
            {
                ArticleList.Items.Add(CreateArticleItem(file));
            }

            SelectArticle(CurrentFile);
        }
    }

    private IEnumerable<TreeViewItem> CreateTreeItems(string folder)
    {
        string[] dirs, files;
        try
        {
            dirs = Directory.GetDirectories(folder).Where(d => !MarkdownFiles.IsSkippedFolder(Path.GetFileName(d))).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase).ToArray();
            files = Directory.GetFiles(folder).Where(MarkdownFiles.IsMarkdown).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            var item = new TreeViewItem { Header = "📁 " + Path.GetFileName(dir), Tag = dir, ToolTip = dir };
            item.Items.Add(new TreeViewItem { Header = "…" }); // marcador para carga diferida
            item.Expanded += OnFolderExpanded;
            yield return item;
        }

        foreach (var file in files)
        {
            yield return new TreeViewItem { Header = Path.GetFileName(file), Tag = file, ToolTip = file };
        }
    }

    private void OnFolderExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { Tag: string dir } item && item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null })
        {
            item.Items.Clear();
            foreach (var child in CreateTreeItems(dir))
            {
                item.Items.Add(child);
            }
        }

        e.Handled = true;
    }

    private void OnFileTreeActivate(object sender, MouseButtonEventArgs e) => OpenSelectedTreeFile();

    private void OnFileTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedTreeFile();
            e.Handled = true;
        }
    }

    private void OnFileTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Un clic en un archivo lo abre (navegación con teclado: Intro).
        if (!_syncingSelection && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            OpenSelectedTreeFile();
        }
    }

    private void OpenSelectedTreeFile()
    {
        if (FileTree.SelectedItem is TreeViewItem { Tag: string path } && File.Exists(path))
        {
            FileOpenRequested?.Invoke(this, path);
        }
    }

    private static ListBoxItem CreateArticleItem(FileInfo file)
    {
        var (title, excerpt) = MarkdownFiles.Summary(file.FullName);
        var panel = new StackPanel { Margin = new Thickness(3, 3, 3, 4) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = excerpt, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, FontSize = 11 });
        panel.Children.Add(new TextBlock { Text = $"{file.Name} · {file.LastWriteTime:g}", Opacity = 0.55, FontSize = 10, Margin = new Thickness(0, 1, 0, 0) });
        var item = new ListBoxItem { Content = panel, Tag = file.FullName, ToolTip = file.FullName };
        System.Windows.Automation.AutomationProperties.SetName(item, title);
        return item;
    }

    private void OnArticleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSelection && ArticleList.SelectedItem is ListBoxItem { Tag: string path })
        {
            FileOpenRequested?.Invoke(this, path);
        }
    }

    private void OnArticleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ArticleList.SelectedItem is ListBoxItem { Tag: string path })
        {
            FileOpenRequested?.Invoke(this, path);
        }
    }

    private void SelectArticle(string? path)
    {
        _syncingSelection = true;
        ArticleList.SelectedItem = ArticleList.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, path, StringComparison.OrdinalIgnoreCase));
        _syncingSelection = false;
    }

    /// <summary>Muestra y selecciona el archivo en el árbol (abriendo las carpetas necesarias).</summary>
    public void Reveal(string path)
    {
        CurrentFile = path;
        if (Folder is null || !path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (FilesView == FilesView.List)
        {
            SelectArticle(path);
            return;
        }

        _syncingSelection = true;
        try
        {
            ItemsControl parent = FileTree;
            var relative = Path.GetRelativePath(Folder, path).Split(Path.DirectorySeparatorChar);
            var current = Folder;
            foreach (var part in relative)
            {
                current = Path.Combine(current, part);
                var item = parent.Items.OfType<TreeViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, current, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    return;
                }

                if (Directory.Exists(current))
                {
                    item.IsExpanded = true;
                    parent = item;
                }
                else
                {
                    item.IsSelected = true;
                    item.BringIntoView();
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // ───────────── Índice ─────────────

    public void SetOutline(IReadOnlyList<OutlineItem> items)
    {
        _outline = items;
        RenderOutline();
    }

    public void SetActiveHeading(int index)
    {
        _activeHeading = index;
        foreach (ListBoxItem item in OutlineList.Items)
        {
            var active = item.Tag is int i && i == index;
            item.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            if (active)
            {
                // Referencia dinámica: el color sigue al tema si se cambia después.
                item.SetResourceReference(BackgroundProperty, "SidebarHoverBrush");
            }
            else
            {
                item.Background = Brushes.Transparent;
            }
        }
    }

    private void RenderOutline()
    {
        var filter = OutlineFilter.Text.Trim();
        OutlineList.Items.Clear();
        var minLevel = _outline.Count > 0 ? _outline.Min(o => o.Level) : 1;
        for (var i = 0; i < _outline.Count; i++)
        {
            var heading = _outline[i];
            if (filter.Length > 0 && !heading.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            OutlineList.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = heading.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                Tag = i,
                Padding = new Thickness(8 + ((heading.Level - minLevel) * 14), 1, 6, 1),
                FontSize = 12,
                ToolTip = heading.Text,
            });
        }

        NoHeadingsText.Visibility = _outline.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetActiveHeading(_activeHeading);
    }

    private void OnOutlineFilterChanged(object sender, TextChangedEventArgs e) => RenderOutline();

    private void OnOutlineClick(object sender, MouseButtonEventArgs e)
    {
        if (OutlineList.SelectedItem is ListBoxItem { Tag: int index })
        {
            HeadingRequested?.Invoke(this, index);
        }
    }

    private void OnOutlineKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OutlineList.SelectedItem is ListBoxItem { Tag: int index })
        {
            HeadingRequested?.Invoke(this, index);
        }
    }

    // ───────────── Buscar en la carpeta ─────────────

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = RunSearchAsync();
            e.Handled = true;
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => _ = RunSearchAsync();

    private async Task RunSearchAsync()
    {
        SearchResults.Items.Clear();
        var query = SearchBox.Text;
        if (Folder is null)
        {
            SearchSummary.Text = "Abre una carpeta para buscar en sus documentos.";
            return;
        }

        if (string.IsNullOrEmpty(query))
        {
            SearchSummary.Text = string.Empty;
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        bool caseSensitive = SearchCase.IsChecked == true, regex = SearchRegex.IsChecked == true, word = SearchWord.IsChecked == true;
        var folder = Folder;
        SearchSummary.Text = "Buscando…";

        try
        {
            var hits = await Task.Run(() => MarkdownFiles.Search(folder, query, caseSensitive, regex, word, cancellationToken: token), token);
            foreach (var group in hits.GroupBy(h => h.FilePath))
            {
                SearchResults.Items.Add(new ListBoxItem
                {
                    Content = new TextBlock { Text = $"{Path.GetRelativePath(folder, group.Key)}  ({group.Count()})", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    Tag = group.Key,
                    Margin = new Thickness(0, 3, 0, 0),
                });
                foreach (var hit in group.Take(50))
                {
                    var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0), FontSize = 11 };
                    var start = Math.Clamp(hit.MatchStart, 0, hit.LineText.Length);
                    var length = Math.Clamp(hit.MatchLength, 0, hit.LineText.Length - start);
                    text.Inlines.Add(new System.Windows.Documents.Run($"{hit.LineNumber}: ") { Foreground = Brushes.Gray });
                    text.Inlines.Add(hit.LineText[..start]);
                    var match = new System.Windows.Documents.Run(hit.LineText.Substring(start, length)) { FontWeight = FontWeights.Bold };
                    match.SetResourceReference(System.Windows.Documents.TextElement.BackgroundProperty, "SidebarHoverBrush");
                    text.Inlines.Add(match);
                    text.Inlines.Add(hit.LineText[(start + length)..]);
                    var resultItem = new ListBoxItem { Content = text, Tag = group.Key, ToolTip = hit.LineText };
                    System.Windows.Automation.AutomationProperties.SetName(resultItem, $"Línea {hit.LineNumber}: {hit.LineText}");
                    SearchResults.Items.Add(resultItem);
                }
            }

            var files = hits.Select(h => h.FilePath).Distinct().Count();
            SearchSummary.Text = hits.Count == 0 ? "Sin resultados." : $"{hits.Count} coincidencias en {files} documentos.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (ArgumentException ex)
        {
            SearchSummary.Text = $"Expresión no válida: {ex.Message}";
        }
    }

    private void OnSearchResultActivate(object sender, MouseButtonEventArgs e) => ActivateSearchResult();

    private void OnSearchResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSearchResult();
        }
    }

    private void ActivateSearchResult()
    {
        if (SearchResults.SelectedItem is ListBoxItem { Tag: string path })
        {
            SearchResultActivated?.Invoke(this, (path, SearchBox.Text, SearchCase.IsChecked == true, SearchRegex.IsChecked == true, SearchWord.IsChecked == true));
        }
    }
}
