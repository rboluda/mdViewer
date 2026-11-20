using System.Text;
using MdViewer.Core.Documents;
using MdViewer.Core.Files;
using MdViewer.Core.Settings;

namespace MdViewer.Core.Tests;

public class FilesTests
{
    [Fact]
    public void Enumerate_finds_markdown_and_skips_dependency_folders()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.File("docs"));
        Directory.CreateDirectory(temp.File("node_modules"));
        File.WriteAllText(temp.File("a.md"), "# A");
        File.WriteAllText(temp.File(Path.Combine("docs", "b.markdown")), "# B");
        File.WriteAllText(temp.File(Path.Combine("node_modules", "c.md")), "# C");
        File.WriteAllText(temp.File("imagen.png"), "x");

        var files = MarkdownFiles.Enumerate(temp.Path).Select(Path.GetFileName).ToList();

        Assert.Equal(["a.md", "b.markdown"], files);
    }

    [Theory]
    [InlineData("fnav", @"notas\Fiesta Navidad.md", true)]
    [InlineData("xyz", @"notas\Fiesta Navidad.md", false)]
    [InlineData("", @"cualquiera.md", true)]
    public void FuzzyScore_matches_letters_in_order(string query, string path, bool matches)
        => Assert.Equal(matches, MarkdownFiles.FuzzyScore(query, path) is not null);

    [Fact]
    public void FuzzyScore_prefers_file_name_matches()
    {
        var inName = MarkdownFiles.FuzzyScore("plan", @"docs\plan.md")!.Value;
        var inFolder = MarkdownFiles.FuzzyScore("plan", @"plan\docs\otro.md")!.Value;

        Assert.True(inName > inFolder);
    }

    [Fact]
    public void Search_finds_lines_with_options()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("a.md"), "# Título\nLa Paella del día\nsin nada\n");
        File.WriteAllText(temp.File("b.md"), "paellas y más\n");

        var any = MarkdownFiles.Search(temp.Path, "paella", caseSensitive: false, regex: false, wholeWord: false, cancellationToken: TestContext.Current.CancellationToken);
        var exact = MarkdownFiles.Search(temp.Path, "Paella", caseSensitive: true, regex: false, wholeWord: true, cancellationToken: TestContext.Current.CancellationToken);
        var regex = MarkdownFiles.Search(temp.Path, @"pae\w+s", caseSensitive: false, regex: true, wholeWord: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, any.Count);
        var hit = Assert.Single(exact);
        Assert.Equal(2, hit.LineNumber);
        Assert.Equal("Paella", hit.LineText.Substring(hit.MatchStart, hit.MatchLength));
        Assert.Single(regex);
    }

    [Fact]
    public void Summary_uses_first_heading_and_text()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("a.md"), "---\ntitle: x\n---\n# Fiesta de Navidad\n\nOrganización del evento.\n");

        var (title, excerpt) = MarkdownFiles.Summary(temp.File("a.md"));

        Assert.Equal("Fiesta de Navidad", title);
        Assert.StartsWith("title: x", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_are_found_resolved_and_rewritten()
    {
        const string md = "Texto ![logo](img/logo.png \"Logo\") y <img src=\"https://x.com/a.jpg\"> y ![b](<mis fotos/b.png>)";

        var images = MarkdownImages.Find(md);

        Assert.Equal(["img/logo.png", "https://x.com/a.jpg", "mis fotos/b.png"], images.Select(i => i.Source));
        Assert.True(MarkdownImages.IsRemote(images[1].Source));
        Assert.Equal(Path.GetFullPath(@"C:\doc\img\logo.png"), MarkdownImages.ResolveLocal(images[0].Source, @"C:\doc"));

        var rewritten = MarkdownImages.Rewrite(md, new Dictionary<MarkdownImages.ImageReference, string> { [images[0]] = "assets/logo.png" });
        Assert.Contains("![logo](assets/logo.png \"Logo\")", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_paths_are_relative_with_forward_slashes()
    {
        Assert.Equal("assets/a.png", MarkdownImages.ToMarkdownPath(@"C:\doc\assets\a.png", @"C:\doc", preferRelative: true));
        Assert.Equal("<assets/mi foto.png>", MarkdownImages.ToMarkdownPath(@"C:\doc\assets\mi foto.png", @"C:\doc", preferRelative: true));
        Assert.Equal("C:/otra/a.png", MarkdownImages.ToMarkdownPath(@"C:\otra\a.png", @"C:\doc", preferRelative: false));
    }

    [Fact]
    public void Image_target_folder_follows_policy_and_unique_path_avoids_overwrite()
    {
        using var temp = new TempFolder();
        var doc = temp.File("notas.md");
        var settings = new AppSettings { ImageInsertPolicy = ImageInsertPolicy.CopyToDocumentAssets };

        Assert.Equal(temp.File("notas.assets"), MarkdownImages.TargetFolder(settings, doc));
        settings.ImageInsertPolicy = ImageInsertPolicy.KeepInPlace;
        Assert.Null(MarkdownImages.TargetFolder(settings, doc));

        File.WriteAllText(temp.File("a.png"), "x");
        Assert.Equal(temp.File("a (2).png"), MarkdownImages.UniquePath(temp.Path, "a.png"));
    }

    [Fact]
    public async Task Document_can_be_reopened_with_another_encoding_and_change_line_endings()
    {
        using var temp = new TempFolder();
        var path = temp.File("ansi.md");
        _ = TextFileCodec.LegacyFallback;
        await File.WriteAllBytesAsync(path, Encoding.GetEncoding(1252).GetBytes("canción\r\n"), TestContext.Current.CancellationToken);

        var doc = await MarkdownDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        await doc.ReloadWithEncodingAsync(Encoding.GetEncoding(1252), TestContext.Current.CancellationToken);
        Assert.Equal("canción\n", doc.Text);

        doc.ChangeFormat(doc.Format with { LineEnding = LineEnding.Lf });
        Assert.True(doc.IsDirty);
        doc.Save();
        Assert.False(doc.IsDirty);
        Assert.Equal("canción\n", Encoding.GetEncoding(1252).GetString(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Document_move_and_detach()
    {
        using var temp = new TempFolder();
        var path = temp.File("a.md");
        File.WriteAllText(path, "hola");
        var doc = await MarkdownDocument.OpenAsync(path, TestContext.Current.CancellationToken);

        doc.MoveTo(temp.File("b.md"));
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(temp.File("b.md")));

        doc.Detach();
        Assert.Null(doc.FilePath);
        Assert.True(doc.IsDirty);
    }
}
