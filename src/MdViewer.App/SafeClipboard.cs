using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MdViewer.Core.Diagnostics;

namespace MdViewer.App;

/// <summary>
/// Escritura en el portapapeles de Windows sin bloquear la ventana.
/// </summary>
/// <remarks>
/// No se usa <see cref="Clipboard"/> de WPF: deja los datos en «entrega diferida» (otro programa
/// que pega, o el historial del portapapeles de Windows, tiene que pedírselos a esta ventana) y, si
/// el portapapeles está abierto por otro, reintenta durmiendo el hilo de la interfaz. Las dos cosas
/// juntas se bloquean entre sí: la ventana se queda colgada varios segundos y en ese tiempo nadie
/// puede pegar. Aquí los datos se entregan completos al copiarlos (nadie tiene que volver a pedirlos)
/// y, si el portapapeles está ocupado, se espera sin bloquear la interfaz.
/// </remarks>
internal static partial class SafeClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;
    private const int Attempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);

    /// <summary>Copia el texto; devuelve false si el portapapeles sigue ocupado por otro programa.</summary>
    public static Task<bool> SetTextAsync(string text) =>
        SetAsync([(CfUnicodeText, Encoding.Unicode.GetBytes(text + "\0"))]);

    /// <summary>Copia la imagen como mapa de bits (para cualquier programa) y como PNG (conserva la transparencia).</summary>
    public static Task<bool> SetImageAsync(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var png = Encode(new PngBitmapEncoder(), bgra);
        // CF_DIB es un archivo .bmp sin su cabecera de archivo (BITMAPFILEHEADER, 14 bytes).
        var bmp = Encode(new BmpBitmapEncoder(), bgra);
        return SetAsync([(CfDib, bmp[14..]), (RegisterClipboardFormatW("PNG"), png)]);
    }

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource image)
    {
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static async Task<bool> SetAsync(IReadOnlyList<(uint Format, byte[] Data)> items)
    {
        var owner = Application.Current?.MainWindow is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            if (TryWrite(owner, items))
            {
                return true;
            }

            // Otro programa lo tiene abierto un instante (historial del portapapeles, escritorio remoto…).
            await Task.Delay(RetryDelay);
        }

        Log.Warn($"Portapapeles ocupado: no se pudo copiar tras {Attempts} intentos (error {Marshal.GetLastPInvokeError()})");
        return false;
    }

    private static bool TryWrite(IntPtr owner, IReadOnlyList<(uint Format, byte[] Data)> items)
    {
        // Hace falta una ventana propietaria: sin ella Windows rechaza SetClipboardData.
        if (owner == IntPtr.Zero || !OpenClipboard(owner))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            foreach (var (format, data) in items)
            {
                var memory = GlobalAlloc(GmemMoveable, (nuint)data.Length);
                if (memory == IntPtr.Zero)
                {
                    return false;
                }

                var pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }

                Marshal.Copy(data, 0, pointer, data.Length);
                GlobalUnlock(memory);
                // Si se acepta, la memoria pasa a ser del portapapeles; si no, hay que liberarla.
                if (SetClipboardData(format, memory) == IntPtr.Zero)
                {
                    GlobalFree(memory);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
