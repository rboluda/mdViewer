using System.Text.RegularExpressions;
using MdViewer.Core.Settings;

namespace MdViewer.Core.Files;

/// <summary>Referencias a imágenes dentro de un documento Markdown y operaciones sobre ellas.</summary>
public static partial class MarkdownImages
{
    public sealed record ImageReference(string Source, int Index, int Length);

    /// <summary>Imágenes Markdown ![alt](src "título") y etiquetas &lt;img src="…"&gt;.</summary>
    public static IReadOnlyList<ImageReference> Find(string markdown)
    {
        var result = new List<ImageReference>();
        foreach (Match m in MarkdownImageRegex().Matches(markdown))
        {
            var group = m.Groups["src"];
            result.Add(new ImageReference(group.Value, group.Index, group.Length));
        }

        foreach (Match m in HtmlImageRegex().Matches(markdown))
        {
            var group = m.Groups["src"];
            result.Add(new ImageReference(group.Value, group.Index, group.Length));
        }

        return result.OrderBy(r => r.Index).ToList();
    }

    public static bool IsRemote(string source)
        => source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ruta local absoluta de una imagen, o null si es remota o no se puede resolver.</summary>
    public static string? ResolveLocal(string source, string? documentFolder)
    {
        if (string.IsNullOrWhiteSpace(source) || IsRemote(source) || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var clean = Uri.UnescapeDataString(source.Split('?', '#')[0]);
        if (clean.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(clean).LocalPath;
        }

        if (Path.IsPathRooted(clean))
        {
            return Path.GetFullPath(clean);
        }

        return documentFolder is null ? null : Path.GetFullPath(Path.Combine(documentFolder, clean));
    }

    /// <summary>Carpeta donde se copian las imágenes según la preferencia, o null si se dejan donde están.</summary>
    public static string? TargetFolder(AppSettings settings, string documentPath)
    {
        var folder = Path.GetDirectoryName(documentPath)!;
        return settings.ImageInsertPolicy switch
        {
            ImageInsertPolicy.CopyToAssets => Path.Combine(folder, "assets"),
            ImageInsertPolicy.CopyToDocumentAssets => Path.Combine(folder, Path.GetFileNameWithoutExtension(documentPath) + ".assets"),
            ImageInsertPolicy.CopyToCustomFolder when !string.IsNullOrWhiteSpace(settings.ImageCustomFolder)
                => Path.IsPathRooted(settings.ImageCustomFolder) ? settings.ImageCustomFolder : Path.Combine(folder, settings.ImageCustomFolder),
            _ => null,
        };
    }

    /// <summary>Ruta libre en la carpeta: añade " (2)", " (3)"… si el nombre ya existe con otro contenido.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        var candidate = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(candidate); i++)
        {
            candidate = Path.Combine(folder, $"{name} ({i}){extension}");
        }

        return candidate;
    }

    /// <summary>Texto que se escribe en el Markdown para una imagen: relativo con "/" o absoluto, y con espacios escapados.</summary>
    public static string ToMarkdownPath(string imagePath, string? documentFolder, bool preferRelative)
    {
        var path = imagePath;
        if (preferRelative && documentFolder is not null)
        {
            var relative = Path.GetRelativePath(documentFolder, imagePath);
            if (!Path.IsPathRooted(relative))
            {
                path = relative;
            }
        }

        path = path.Replace('\\', '/');
        return path.Contains(' ', StringComparison.Ordinal) ? $"<{path}>" : path;
    }

    /// <summary>Sustituye las rutas indicadas en el Markdown (de atrás hacia delante para no desplazar índices).</summary>
    public static string Rewrite(string markdown, IReadOnlyDictionary<ImageReference, string> replacements)
    {
        var builder = new System.Text.StringBuilder(markdown);
        foreach (var (reference, replacement) in replacements.OrderByDescending(r => r.Key.Index))
        {
            builder.Remove(reference.Index, reference.Length).Insert(reference.Index, replacement);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?(?<src>[^)\s>]+(?:\s[^)""'>]+)*)>?(?:\s+[""'][^)]*[""'])?\s*\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageRegex();
}
