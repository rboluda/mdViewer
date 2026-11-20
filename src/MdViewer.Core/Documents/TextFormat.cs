using System.Text;

namespace MdViewer.Core.Documents;

public enum LineEnding
{
    CrLf,
    Lf,
    Cr,
}

/// <summary>
/// Formato en disco de un archivo de texto. Se guarda al abrir para escribirlo
/// exactamente igual (codificación, BOM y saltos de línea) al guardar.
/// </summary>
public sealed record TextFormat(Encoding Encoding, bool HasBom, LineEnding LineEnding)
{
    public static TextFormat Default { get; } = new(new UTF8Encoding(false), false, LineEnding.CrLf);

    public string NewLine => LineEnding switch
    {
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => "\r\n",
    };
}
