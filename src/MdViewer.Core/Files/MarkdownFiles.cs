using System.Text.RegularExpressions;

namespace MdViewer.Core.Files;

/// <summary>Archivos Markdown de una carpeta: enumeración, búsqueda y apertura rápida.</summary>
public static partial class MarkdownFiles
{
    public static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".qmd", ".txt"];

    /// <summary>Carpetas que nunca interesa recorrer (dependencias, control de versiones, compilados).</summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__",
    };

    public static bool IsMarkdown(string path)
        => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsSkippedFolder(string name) => SkippedFolders.Contains(name) || name.StartsWith('.');

    /// <summary>Archivos Markdown de la carpeta y subcarpetas (con un límite para carpetas enormes).</summary>
    public static IReadOnlyList<string> Enumerate(string folder, int limit = 20000)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0 && result.Count < limit)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    if (IsMarkdown(file))
                    {
                        result.Add(file);
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    if (!IsSkippedFolder(Path.GetFileName(dir)))
                    {
                        pending.Push(dir);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Carpetas sin permiso o desaparecidas: se omiten.
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    /// <summary>
    /// Puntuación de coincidencia aproximada (las letras de la consulta aparecen en orden).
    /// Devuelve null si no coincide. Mayor es mejor: premia el nombre de archivo, inicios de palabra y letras seguidas.
    /// </summary>
    public static int? FuzzyScore(string query, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var q = query.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var path = relativePath.ToUpperInvariant();
        var nameStart = path.LastIndexOfAny(['\\', '/']) + 1;

        int score = 0, qi = 0, lastMatch = -2;
        for (var i = 0; i < path.Length && qi < q.Length; i++)
        {
            if (path[i] != q[qi])
            {
                continue;
            }

            score += 1;
            if (i >= nameStart) score += 3;                                   // en el nombre del archivo
            if (i == lastMatch + 1) score += 4;                               // letras consecutivas
            if (i == 0 || !char.IsLetterOrDigit(path[i - 1])) score += 5;      // inicio de palabra
            lastMatch = i;
            qi++;
        }

        if (qi < q.Length)
        {
            return null;
        }

        return score - (path.Length / 20);
    }

    /// <summary>Resultado de una búsqueda en la carpeta.</summary>
    public sealed record SearchHit(string FilePath, int LineNumber, string LineText, int MatchStart, int MatchLength);

    /// <summary>Busca texto (o una expresión regular) en todos los archivos Markdown de la carpeta.</summary>
    public static IReadOnlyList<SearchHit> Search(string folder, string query, bool caseSensitive, bool regex, bool wholeWord,
        int maxHits = 2000, CancellationToken cancellationToken = default)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(query))
        {
            return hits;
        }

        var pattern = regex ? query : Regex.Escape(query);
        if (wholeWord)
        {
            pattern = $@"\b{pattern}\b";
        }

        var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var matcher = new Regex(pattern, options, TimeSpan.FromSeconds(1));

        foreach (var file in Enumerate(folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] lines;
            try
            {
                lines = Documents.TextFileCodec.Read(file).Text.Split('\n');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var match = matcher.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }

                hits.Add(new SearchHit(file, i + 1, lines[i].Trim(), match.Index - (lines[i].Length - lines[i].TrimStart().Length), match.Length));
                if (hits.Count >= maxHits)
                {
                    return hits;
                }
            }
        }

        return hits;
    }

    /// <summary>Título de un documento: primer título Markdown o, si no hay, la primera línea con texto.</summary>
    public static (string Title, string Excerpt) Summary(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            string? title = null;
            var excerpt = new List<string>();
            for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed == "---")
                {
                    continue;
                }

                if (title is null && HeadingRegex().Match(trimmed) is { Success: true } heading)
                {
                    title = heading.Groups[1].Value;
                    continue;
                }

                if (excerpt.Count < 2 && !trimmed.StartsWith('|') && !trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    excerpt.Add(trimmed.TrimStart('#', '>', '-', '*', ' '));
                }
            }

            return (title ?? Path.GetFileNameWithoutExtension(path), string.Join(" ", excerpt));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (Path.GetFileNameWithoutExtension(path), string.Empty);
        }
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+?)\s*#*$")]
    private static partial Regex HeadingRegex();
}
