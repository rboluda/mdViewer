using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MdViewer.App;

/// <summary>
/// Portapapeles con reintentos: Windows devuelve CLIPBRD_E_CANT_OPEN si otro programa lo tiene
/// abierto en ese instante (gestores de portapapeles, escritorio remoto…).
/// </summary>
internal static class SafeClipboard
{
    public static void SetText(string text) => Retry(() => Clipboard.SetDataObject(text, copy: true));

    public static void SetImage(BitmapSource image) => Retry(() => Clipboard.SetImage(image));

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
            catch (ExternalException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
        }
    }
}
