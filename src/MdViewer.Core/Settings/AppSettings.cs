namespace MdViewer.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Preferencias del usuario. Todos los valores tienen un valor por defecto seguro.</summary>
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public bool AutoSave { get; set; }

    public int AutoSaveIntervalSeconds { get; set; } = 300;

    public int RecoveryIntervalSeconds { get; set; } = 30;

    public bool CheckForUpdates { get; set; } = true;

    public bool ReopenLastFile { get; set; } = true;

    public List<string> RecentFiles { get; set; } = [];

    public int MaxRecentFiles { get; set; } = 15;

    public WindowPlacement? Window { get; set; }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }
}

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
