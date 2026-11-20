using System.Text;
using MdViewer.Core.IO;

namespace MdViewer.Core.Documents;

/// <summary>Contenido de un archivo leído: texto con saltos normalizados a "\n" y su formato original.</summary>
public sealed record TextFileContent(string Text, TextFormat Format);

/// <summary>
/// Lee y escribe archivos de texto preservando su formato original.
/// Internamente el texto siempre usa "\n"; al guardar se restauran los saltos originales.
/// </summary>
public static class TextFileCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Codificación de reserva para archivos que no son UTF-8 válido (ANSI de Windows en español).</summary>
    public static Encoding LegacyFallback => Encoding.GetEncoding(1252);

    public static TextFileContent Read(string path) => Decode(File.ReadAllBytes(path));

    public static async Task<TextFileContent> ReadAsync(string path, CancellationToken cancellationToken = default)
        => Decode(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));

    public static void Write(string path, string text, TextFormat format)
        => AtomicFile.WriteAllBytes(path, Encode(text, format));

    public static TextFileContent Decode(ReadOnlySpan<byte> bytes)
    {
        Encoding encoding;
        bool hasBom;
        string text;

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            encoding = new UTF8Encoding(false);
            hasBom = true;
            text = encoding.GetString(bytes[3..]);
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
            hasBom = true;
            text = encoding.GetString(bytes[2..]);
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            hasBom = true;
            text = encoding.GetString(bytes[2..]);
        }
        else
        {
            hasBom = false;
            try
            {
                text = StrictUtf8.GetString(bytes);
                encoding = new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                encoding = LegacyFallback;
                text = encoding.GetString(bytes);
            }
        }

        var lineEnding = DetectLineEnding(text);
        return new TextFileContent(NormalizeNewLines(text), new TextFormat(encoding, hasBom, lineEnding));
    }

    public static byte[] Encode(string text, TextFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(format);

        var normalized = NormalizeNewLines(text);
        var output = format.LineEnding == LineEnding.Lf ? normalized : normalized.Replace("\n", format.NewLine, StringComparison.Ordinal);

        var preamble = format.HasBom ? BomFor(format.Encoding) : [];
        var body = format.Encoding.GetBytes(output);
        if (preamble.Length == 0)
        {
            return body;
        }

        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    public static string NormalizeNewLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>Salto de línea predominante; CRLF si el archivo no tiene ninguno (convención de Windows).</summary>
    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
        {
            return LineEnding.CrLf;
        }

        if (lf > crlf && lf >= cr)
        {
            return LineEnding.Lf;
        }

        return cr > crlf ? LineEnding.Cr : LineEnding.CrLf;
    }

    private static byte[] BomFor(Encoding encoding) => encoding switch
    {
        UTF8Encoding => [0xEF, 0xBB, 0xBF],
        UnicodeEncoding u when u.CodePage == 1201 => [0xFE, 0xFF],
        UnicodeEncoding => [0xFF, 0xFE],
        _ => [],
    };
}
