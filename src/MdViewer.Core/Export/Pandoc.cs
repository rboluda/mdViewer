using System.Diagnostics;

namespace MdViewer.Core.Export;

/// <summary>Integración opcional con Pandoc para exportar e importar otros formatos.</summary>
public static class Pandoc
{
    public sealed record Format(string Name, string PandocFormat, string Extension, string Filter);

    public static readonly Format[] ExportFormats =
    [
        new("Word (.docx)", "docx", ".docx", "Documento de Word|*.docx"),
        new("OpenOffice (.odt)", "odt", ".odt", "Documento ODT|*.odt"),
        new("RTF", "rtf", ".rtf", "Texto enriquecido|*.rtf"),
        new("EPUB", "epub3", ".epub", "Libro electrónico EPUB|*.epub"),
        new("LaTeX", "latex", ".tex", "Documento LaTeX|*.tex"),
        new("MediaWiki", "mediawiki", ".wiki", "MediaWiki|*.wiki;*.txt"),
        new("reStructuredText", "rst", ".rst", "reStructuredText|*.rst"),
        new("Textile", "textile", ".textile", "Textile|*.textile"),
        new("OPML", "opml", ".opml", "OPML|*.opml"),
    ];

    public const string ImportFilter = "Documentos compatibles|*.docx;*.odt;*.rtf;*.epub;*.html;*.htm;*.tex;*.rst;*.textile;*.opml;*.wiki|Todos los archivos (*.*)|*.*";

    /// <summary>Ruta de pandoc.exe: la configurada, la del PATH o las ubicaciones de instalación habituales.</summary>
    public static string? Locate(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim('"'), "pandoc.exe"));
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pandoc", "pandoc.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pandoc", "pandoc.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Ejecuta Pandoc y devuelve la salida estándar. Lanza InvalidOperationException si falla.</summary>
    public static async Task<string> RunAsync(string pandocPath, IEnumerable<string> arguments, string? workingDirectory,
        string? standardInput = null, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(pandocPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar Pandoc.");
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Pandoc terminó con error {process.ExitCode}:\n{await error.ConfigureAwait(false)}");
        }

        return await output.ConfigureAwait(false);
    }

    /// <summary>Argumentos para exportar Markdown (por la entrada estándar) a un archivo.</summary>
    public static IReadOnlyList<string> ExportArguments(Format format, string outputPath, string? resourceFolder, string? title)
    {
        var args = new List<string> { "--from", "gfm+tex_math_dollars+footnotes+yaml_metadata_block", "--to", format.PandocFormat, "--output", outputPath, "--standalone" };
        if (resourceFolder is not null)
        {
            args.Add("--resource-path");
            args.Add(resourceFolder);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            args.Add("--metadata");
            args.Add($"title={title}");
        }

        return args;
    }

    /// <summary>Argumentos para importar un documento a Markdown (salida estándar), extrayendo sus imágenes.</summary>
    public static IReadOnlyList<string> ImportArguments(string inputPath, string mediaFolder)
        => ["--to", "gfm", "--wrap", "none", "--extract-media", mediaFolder, inputPath];
}
