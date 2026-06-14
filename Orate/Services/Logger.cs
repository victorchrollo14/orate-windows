using System.IO;

namespace Orate.Services;

/// <summary>
/// Minimal append-only file logger at %APPDATA%\Orate\orate.log. Exists because the app runs
/// windowless from the tray, so Debug.WriteLine output is invisible in the field — this gives
/// us something to read when diagnosing a user's machine.
/// </summary>
public static class Logger
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orate");

    private static readonly string FilePath = Path.Combine(Dir, "orate.log");

    private static readonly object Gate = new();

    public static string Path_ => FilePath;

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw into the pipeline.
        }
    }

    public static void Log(string message, Exception ex) => Log($"{message}: {ex}");
}
