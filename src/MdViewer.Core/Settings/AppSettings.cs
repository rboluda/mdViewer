namespace MdViewer.Core.Settings;

public enum ThemeMode
{
    /// <summary>Tema de la marca: azul y blanco (predeterminado).</summary>
    Blue,
    Light,
    Dark,
    System,
}

public enum SidebarTab
{
    Outline,
    Files,
    Search,
}

public enum FilesView
{
    Tree,
    List,
}

/// <summary>Qué hacer con una imagen local al insertarla en un documento.</summary>
public enum ImageInsertPolicy
{
    /// <summary>Enlazar la imagen donde está.</summary>
    KeepInPlace,

    /// <summary>Copiarla a ./assets junto al documento.</summary>
    CopyToAssets,

    /// <summary>Copiarla a ./&lt;nombre del documento&gt;.assets.</summary>
    CopyToDocumentAssets,

    /// <summary>Copiarla a una carpeta elegida por el usuario.</summary>
    CopyToCustomFolder,
}

public enum PdfPaperSize
{
    A4,
    A5,
    A3,
    Letter,
    Legal,
}

/// <summary>Preferencias del usuario. Todos los valores tienen un valor por defecto seguro.</summary>
public sealed class AppSettings
{
    // ── General ──
    public ThemeMode Theme { get; set; } = ThemeMode.Blue;

    public bool ReopenLastFile { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    public bool WelcomeShown { get; set; }

    // ── Guardado ──
    public bool AutoSave { get; set; }

    public int AutoSaveIntervalSeconds { get; set; } = 300;

    public int RecoveryIntervalSeconds { get; set; } = 30;

    // ── Editor ──
    public bool SpellCheck { get; set; }

    public bool SmartPunctuation { get; set; }

    public bool AutoPair { get; set; } = true;

    public double Zoom { get; set; } = 1.0;

    // ── Ventana ──
    public bool SidebarVisible { get; set; } = true;

    public double SidebarWidth { get; set; } = 220;

    public SidebarTab SidebarTab { get; set; } = SidebarTab.Outline;

    public FilesView FilesView { get; set; } = FilesView.Tree;

    public bool StatusBarVisible { get; set; } = true;

    public bool ToolbarVisible { get; set; }

    public bool AlwaysOnTop { get; set; }

    public WindowPlacement? Window { get; set; }

    // ── Imágenes ──
    public ImageInsertPolicy ImageInsertPolicy { get; set; } = ImageInsertPolicy.CopyToAssets;

    public string? ImageCustomFolder { get; set; }

    public bool PreferRelativeImagePaths { get; set; } = true;

    // ── Exportar ──
    public string? PandocPath { get; set; }

    public PdfPaperSize PdfPaperSize { get; set; } = PdfPaperSize.A4;

    public double PdfMarginMillimeters { get; set; } = 20;

    // ── Recientes ──
    public List<string> RecentFiles { get; set; } = [];

    public List<string> RecentFolders { get; set; } = [];

    public int MaxRecentFiles { get; set; } = 15;

    public void AddRecentFile(string path) => AddRecent(RecentFiles, path);

    public void AddRecentFolder(string path) => AddRecent(RecentFolders, path);

    private void AddRecent(List<string> list, string path)
    {
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxRecentFiles)
        {
            list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        }
    }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
