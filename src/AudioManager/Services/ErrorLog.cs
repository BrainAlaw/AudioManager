using System.IO;

namespace AudioManager.Services;

/// <summary>
/// Minimal, never-throwing diagnostic log in the settings folder so background failures leave a trace.
/// </summary>
public static class ErrorLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioManager",
        "error.log");

    public static void Write(string context, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {context}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never become a second failure.
        }
    }
}
