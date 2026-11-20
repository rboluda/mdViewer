using System.Text.Json;
using System.Text.Json.Serialization;
using MdViewer.Core.IO;

namespace MdViewer.Core.Settings;

/// <summary>
/// Carga y guarda la configuración en JSON. Si el archivo está corrupto, se aparta
/// como settings.json.corrupt y se arranca con valores por defecto: la app nunca deja de abrir.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(_path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            File.Copy(_path, _path + ".corrupt", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
        => AtomicFile.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(settings, Options));
}
