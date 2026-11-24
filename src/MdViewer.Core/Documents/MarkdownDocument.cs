namespace MdViewer.Core.Documents;

/// <summary>Documento abierto en el editor. No conoce la interfaz: solo estado y disco.</summary>
public sealed class MarkdownDocument
{
    private string _savedText;

    /// <summary>Cambios que no son de texto (formato, archivo borrado) que también requieren guardar.</summary>
    private bool _pendingNonTextChange;

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

    public bool IsDirty => _pendingNonTextChange || !string.Equals(Text, _savedText, StringComparison.Ordinal);

    public string DisplayName => FilePath is null ? "Sin título" : Path.GetFileName(FilePath);

    public static MarkdownDocument CreateNew() => new(null, string.Empty, TextFormat.Default);

    /// <summary>Documento sin guardar con un contenido inicial (p. ej. importado con Pandoc).</summary>
    public static MarkdownDocument CreateUnsaved(string text)
    {
        var document = new MarkdownDocument(null, string.Empty, TextFormat.Default);
        document.UpdateText(text);
        return document;
    }

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
        MarkClean();
        LastKnownWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
    }

    /// <summary>True si el archivo se ha modificado en disco desde la última lectura o escritura propia.</summary>
    public bool HasChangedOnDisk()
        => FilePath is not null
           && File.Exists(FilePath)
           && File.GetLastWriteTimeUtc(FilePath) != LastKnownWriteTimeUtc;

    /// <summary>
    /// Comprueba si otro programa ha cambiado o borrado el archivo. Si solo ha cambiado la fecha
    /// (mismo contenido, p. ej. al "tocarlo" o guardarlo sin cambios) lo da por visto y no avisa.
    /// Cada cambio se informa una sola vez: tras responder, <see cref="KeepEditorVersion"/>
    /// o <see cref="ReloadAsync"/> lo dan por resuelto.
    /// </summary>
    public async Task<DiskChange> CheckDiskAsync(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return DiskChange.None;
        }

        if (!File.Exists(FilePath))
        {
            return LastKnownWriteTimeUtc is null ? DiskChange.None : DiskChange.Deleted;
        }

        var writeTime = File.GetLastWriteTimeUtc(FilePath);
        if (writeTime == LastKnownWriteTimeUtc)
        {
            return DiskChange.None;
        }

        var content = await TextFileCodec.ReadAsync(FilePath, cancellationToken).ConfigureAwait(false);
        if (string.Equals(content.Text, _savedText, StringComparison.Ordinal) && content.Format == Format)
        {
            LastKnownWriteTimeUtc = writeTime;
            return DiskChange.None;
        }

        return DiskChange.Modified;
    }

    /// <summary>
    /// El usuario decide quedarse con lo que hay en el editor aunque el archivo haya cambiado o
    /// desaparecido: no se vuelve a avisar de ese cambio y el documento queda pendiente de guardar
    /// (al guardar se sobrescribe o se vuelve a crear el archivo).
    /// </summary>
    public void KeepEditorVersion()
    {
        LastKnownWriteTimeUtc = FilePath is not null && File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : null;
        _pendingNonTextChange = true;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return;
        }

        var content = await TextFileCodec.ReadAsync(FilePath, cancellationToken).ConfigureAwait(false);
        ApplyLoaded(content);
    }

    /// <summary>Vuelve a leer el archivo con la codificación indicada (descarta los cambios no guardados).</summary>
    public async Task ReloadWithEncodingAsync(System.Text.Encoding encoding, CancellationToken cancellationToken = default)
    {
        if (FilePath is null)
        {
            return;
        }

        var bytes = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
        ApplyLoaded(TextFileCodec.DecodeWith(bytes, encoding));
    }

    /// <summary>Cambia el formato de guardado (codificación, BOM o saltos de línea). Se aplica al guardar.</summary>
    public void ChangeFormat(TextFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format != Format)
        {
            Format = format;
            _pendingNonTextChange = true;
        }
    }

    /// <summary>Mueve el archivo a otra ruta guardando el contenido actual. Devuelve la ruta anterior.</summary>
    public string? MoveTo(string newPath)
    {
        var oldPath = FilePath;
        var fullPath = Path.GetFullPath(newPath);
        SaveAs(fullPath);
        if (oldPath is not null && !string.Equals(oldPath, fullPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        return oldPath;
    }

    /// <summary>El documento deja de estar asociado a un archivo (p. ej. tras borrarlo): queda sin guardar.</summary>
    public void Detach()
    {
        FilePath = null;
        LastKnownWriteTimeUtc = null;
        _pendingNonTextChange = Text.Length > 0;
    }

    private void ApplyLoaded(TextFileContent content)
    {
        Text = content.Text;
        Format = content.Format;
        MarkClean();
        LastKnownWriteTimeUtc = FilePath is null ? null : File.GetLastWriteTimeUtc(FilePath);
    }

    private void MarkClean()
    {
        _savedText = Text;
        _pendingNonTextChange = false;
    }
}
