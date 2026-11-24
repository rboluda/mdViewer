using MdViewer.Core.Documents;

namespace MdViewer.Core.Tests;

public class DiskChangeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void TouchLater(string path, string? text = null)
    {
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Unchanged_file_reports_nothing()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Hola\n", Ct);
        var doc = await MarkdownDocument.OpenAsync(path, Ct);

        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
    }

    [Fact]
    public async Task External_edit_is_reported_until_reloaded()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Hola\n", Ct);
        var doc = await MarkdownDocument.OpenAsync(path, Ct);

        TouchLater(path, "# Hola\notro programa\n");

        Assert.Equal(DiskChange.Modified, await doc.CheckDiskAsync(Ct));
        Assert.Equal(DiskChange.Modified, await doc.CheckDiskAsync(Ct));

        await doc.ReloadAsync(Ct);
        Assert.Equal("# Hola\notro programa\n", doc.Text);
        Assert.False(doc.IsDirty);
        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
    }

    [Fact]
    public async Task Same_content_with_new_date_is_not_a_change()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Hola\n", Ct);
        var doc = await MarkdownDocument.OpenAsync(path, Ct);

        TouchLater(path);

        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
        Assert.False(doc.HasChangedOnDisk());
    }

    [Fact]
    public async Task Keeping_editor_version_stops_asking_and_leaves_document_to_save()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Hola\n", Ct);
        var doc = await MarkdownDocument.OpenAsync(path, Ct);

        TouchLater(path, "# Cambiado fuera\n");
        Assert.Equal(DiskChange.Modified, await doc.CheckDiskAsync(Ct));

        doc.KeepEditorVersion();

        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
        Assert.True(doc.IsDirty);
        doc.Save();
        Assert.Equal("# Hola\n", await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task Deleted_file_is_reported_once_kept_and_recreated_on_save()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Hola\n", Ct);
        var doc = await MarkdownDocument.OpenAsync(path, Ct);

        File.Delete(path);
        Assert.Equal(DiskChange.Deleted, await doc.CheckDiskAsync(Ct));

        doc.KeepEditorVersion();
        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
        Assert.True(doc.IsDirty);

        doc.Save();
        Assert.True(File.Exists(path));
        Assert.Equal(DiskChange.None, await doc.CheckDiskAsync(Ct));
    }
}
