using System.Globalization;
using System.Text;

namespace MdViewer.Core.Diagnostics;

/// <summary>
/// Registro mínimo a archivo (un archivo por día, se conservan 14).
/// Nunca lanza excepciones: un fallo de log no debe tumbar la aplicación.
/// </summary>
public static class Log
{
    private const int DaysToKeep = 14;
    private static readonly Lock Sync = new();
    private static string? _folder;

    public static void Initialize(string folder)
    {
        _folder = folder;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in new DirectoryInfo(folder).GetFiles("mdviewer-*.log")
                         .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-DaysToKeep)))
            {
                old.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (_folder is null)
        {
            return;
        }

        var now = DateTime.Now;
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();

        try
        {
            var file = Path.Combine(_folder, $"mdviewer-{now:yyyyMMdd}.log");
            lock (Sync)
            {
                File.AppendAllText(file, line.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
