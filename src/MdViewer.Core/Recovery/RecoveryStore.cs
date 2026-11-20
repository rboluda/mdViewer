using System.Text;
using System.Text.Json;
using MdViewer.Core.IO;

namespace MdViewer.Core.Recovery;

/// <summary>Borrador de recuperación de un documento con cambios sin guardar.</summary>
public sealed record RecoveryDraft(Guid DocumentId, string? OriginalPath, DateTimeOffset SavedAt, string Text);

/// <summary>
/// Guarda copias de los documentos con cambios para poder recuperarlos tras un cierre inesperado.
/// Cada borrador son dos archivos: {id}.md (texto) y {id}.json (metadatos).
/// </summary>
public sealed class RecoveryStore
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _folder;

    public RecoveryStore(string folder)
    {
        _folder = folder;
    }

    public void Save(Guid documentId, string? originalPath, string text)
    {
        var meta = new DraftMeta(documentId, originalPath, DateTimeOffset.Now);
        AtomicFile.WriteAllBytes(TextPath(documentId), Utf8.GetBytes(text));
        AtomicFile.WriteAllBytes(MetaPath(documentId), JsonSerializer.SerializeToUtf8Bytes(meta));
    }

    /// <summary>Elimina el borrador si existe. No falla si no hay borrador ni carpeta.</summary>
    public void Delete(Guid documentId)
    {
        if (!Directory.Exists(_folder))
        {
            return;
        }

        File.Delete(TextPath(documentId));
        File.Delete(MetaPath(documentId));
    }

    /// <summary>Borradores existentes. Los que estén corruptos se ignoran.</summary>
    public IReadOnlyList<RecoveryDraft> List()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var drafts = new List<RecoveryDraft>();
        foreach (var metaFile in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<DraftMeta>(File.ReadAllBytes(metaFile));
                if (meta is null || !File.Exists(TextPath(meta.DocumentId)))
                {
                    continue;
                }

                var text = File.ReadAllText(TextPath(meta.DocumentId), Utf8);
                drafts.Add(new RecoveryDraft(meta.DocumentId, meta.OriginalPath, meta.SavedAt, text));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Un borrador ilegible no debe impedir recuperar los demás.
            }
        }

        return drafts.OrderByDescending(d => d.SavedAt).ToList();
    }

    private string TextPath(Guid id) => Path.Combine(_folder, $"{id:N}.md");

    private string MetaPath(Guid id) => Path.Combine(_folder, $"{id:N}.json");

    private sealed record DraftMeta(Guid DocumentId, string? OriginalPath, DateTimeOffset SavedAt);
}
