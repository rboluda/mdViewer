using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using AppTheme = MdViewer.Core.Settings.ThemeMode;

namespace MdViewer.App.Theming;

/// <summary>
/// Aplica el tema a la ventana (marco, menú, barra de estado, barra de título de Windows)
/// y decide qué tema usa el editor web.
/// </summary>
public static partial class ThemeManager
{
    /// <summary>Azul principal del icono.</summary>
    public static readonly Color BrandBlue = Color.FromRgb(0x00, 0x6E, 0xE8);

    /// <summary>Azul oscuro del icono (bordes y estados pulsados).</summary>
    public static readonly Color BrandBlueDark = Color.FromRgb(0x01, 0x52, 0xB4);

    /// <summary>Nombre del tema para el editor web: "blue", "light" o "dark".</summary>
    public static string EditorTheme(AppTheme theme) => theme switch
    {
        AppTheme.Blue => "blue",
        AppTheme.Dark => "dark",
        AppTheme.System => IsSystemDark() ? "dark" : "light",
        _ => "light",
    };

    public static void Apply(Window window, AppTheme theme)
    {
        var dark = EditorTheme(theme) == "dark";
        Application.Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        var (chromeBack, chromeFore) = theme switch
        {
            AppTheme.Blue => (BrandBlue, Colors.White),
            _ when dark => (Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xF3, 0xF3, 0xF3)),
            _ => (Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0x1B, 0x1B, 0x1B)),
        };

        var resources = Application.Current.Resources;
        resources["ChromeBackgroundBrush"] = Freeze(new SolidColorBrush(chromeBack));
        resources["ChromeForegroundBrush"] = Freeze(new SolidColorBrush(chromeFore));
        resources["DocumentBackgroundBrush"] = Freeze(new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White));

        ApplyTitleBar(window, theme == AppTheme.Blue ? chromeBack : null, theme == AppTheme.Blue ? chromeFore : null);
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static bool IsSystemDark()
        => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int light && light == 0;

    // ── Barra de título nativa (Windows 11). En Windows 10 la llamada no tiene efecto. ──

    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const uint DwmColorDefault = 0xFFFFFFFF;

    private static void ApplyTitleBar(Window window, Color? caption, Color? text)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetColor(hwnd, DwmwaCaptionColor, caption);
        SetColor(hwnd, DwmwaBorderColor, caption);
        SetColor(hwnd, DwmwaTextColor, text);
    }

    private static void SetColor(IntPtr hwnd, int attribute, Color? color)
    {
        // COLORREF = 0x00BBGGRR
        var value = color is { } c ? (uint)(c.R | (c.G << 8) | (c.B << 16)) : DwmColorDefault;
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(uint));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
