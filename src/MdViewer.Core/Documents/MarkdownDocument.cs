namespace MdViewer.Core.Documents;

/// <summary>Documento abierto en el editor. No conoce la interfaz: solo estado y disco.</summary>
public sealed class MarkdownDocument
{
    private string _savedText;

    private MarkdownDocument(string? filePath, string text, TextFormat format)
    {
        FilePath = filePath;
        Text = text;
        _savedText = text;
        Format = format;
        if (filePath is not null)
        {
            LastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
        }
    }

    /// <summary>Identificador estable de la sesión (para borradores de recuperación).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Ruta en disco, o null si el documento aún no se ha guardado nunca.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Texto actual con saltos de línea "\n".</summary>
    public string Text { get; private set; }

    public TextFormat Format { get; private set; }

    public DateTime? LastKnownWriteTimeUtc { get; private set; }

    public bool IsDirty => !string.Equals(Text, _savedText, StringComparison.Ordinal);

    public string DisplayName => FilePath is null ? "Sin título" : Path.GetFileName(FilePath);

    public static MarkdownDocument CreateNew() => new(null, string.Empty, TextFormat.Default);

    public static async Task<MarkdownDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var content = await TextFileCodec.ReadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new MarkdownDocument(fullPath, content.Text, content.Format);
    }

    public void UpdateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = TextFileCodec.NormalizeNewLines(text);
    }

    public void Save() => SaveAs(FilePath ?? throw new InvalidOperationException("El documento no tiene ruta; usa SaveAs."));

    public void SaveAs(string path)
    {
        var fullPath = Path.GetFullPath(path);
        TextFileCodec.Write(fullPath, Text, Format);
        FilePath = fullPath;
        _savedText = Text;
        LastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
    }

    /// <summary>True si el archivo se ha modificado en disco desde la última lectura o escritura propia.</summary>
    public bool HasChangedOnDisk()
        => FilePath is not null
           && File.Exists(FilePath)
           && File.GetLastWriteTimeUtc(FilePath) != LastKnownWriteTimeUtc;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return;
        }

        var content = await TextFileCodec.ReadAsync(FilePath, cancellationToken).ConfigureAwait(false);
        Text = content.Text;
        _savedText = content.Text;
        Format = content.Format;
        LastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(FilePath);
    }
}
