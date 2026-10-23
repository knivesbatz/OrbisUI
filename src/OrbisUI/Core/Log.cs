namespace OrbisUI.Core;

public static class Log
{
    static readonly object Gate = new();
    const long MaxBytes = 2 * 1024 * 1024;

    static string FilePath => Path.Combine(AppPaths.Logs, "orbisui.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var f = FilePath;
                var fi = new FileInfo(f);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(f, Path.Combine(AppPaths.Logs, "orbisui.old.log"), true);
                File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the shell */ }
    }
}
