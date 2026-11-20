using MdViewer.Core.Documents;
using MdViewer.Core.IO;
using MdViewer.Core.Recovery;
using MdViewer.Core.Settings;

namespace MdViewer.Core.Tests;

public class StorageTests
{
    [Fact]
    public void AtomicFile_replaces_existing_content_and_leaves_no_temp_files()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        File.WriteAllText(path, "viejo");

        AtomicFile.WriteAllBytes(path, "nuevo"u8);

        Assert.Equal("nuevo", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Document_tracks_dirty_state_and_preserves_format_on_save()
    {
        using var temp = new TempFolder();
        var path = temp.File("doc.md");
        await File.WriteAllTextAsync(path, "# Título\nlínea\n", TestContext.Current.CancellationToken);

        var doc = await MarkdownDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.False(doc.IsDirty);

        doc.UpdateText("# Título\r\notra\r\n");
        Assert.True(doc.IsDirty);

        doc.Save();
        Assert.False(doc.IsDirty);
        Assert.Equal("# Título\notra\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.False(doc.HasChangedOnDisk());
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults_and_keep_a_copy()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{ esto no es json");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var temp = new TempFolder();
        var store = new SettingsStore(temp.File("settings.json"));
        var settings = new AppSettings { Theme = ThemeMode.Dark };
        settings.AddRecentFile(@"C:\a.md");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal([@"C:\a.md"], loaded.RecentFiles);
    }

    [Fact]
    public void Recovery_delete_without_folder_does_not_throw()
    {
        using var temp = new TempFolder();
        var store = new RecoveryStore(temp.File("no-existe"));

        store.Delete(Guid.NewGuid());

        Assert.Empty(store.List());
    }

    [Fact]
    public void Recovery_drafts_can_be_saved_listed_and_deleted()
    {
        using var temp = new TempFolder();
        var store = new RecoveryStore(temp.Path);
        var id = Guid.NewGuid();

        store.Save(id, @"C:\doc.md", "borrador ñ");
        File.WriteAllText(temp.File("roto.json"), "no json");

        var draft = Assert.Single(store.List());
        Assert.Equal(id, draft.DocumentId);
        Assert.Equal("borrador ñ", draft.Text);

        store.Delete(id);
        Assert.Empty(store.List());
    }
}
