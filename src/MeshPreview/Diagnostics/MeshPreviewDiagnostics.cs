namespace MHUpkManager.MeshPreview;

internal static class MeshPreviewDiagnostics
{
    private static readonly object Sync = new();

    public static void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            lock (Sync)
            {
                Directory.CreateDirectory(GetLogDirectory());
                File.AppendAllText(GetLogPath(), line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    public static void LogException(string context, Exception exception)
    {
        Log($"{context}{Environment.NewLine}{exception}");
    }

    private static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "MHUpkManager_RuntimeLogs");
    }

    private static string GetLogPath()
    {
        return Path.Combine(GetLogDirectory(), $"meshpreview-{DateTime.Now:yyyyMMdd}.log");
    }
}
