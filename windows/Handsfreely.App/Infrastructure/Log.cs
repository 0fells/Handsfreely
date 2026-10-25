namespace Handsfreely.App.Infrastructure;

public static class Log
{
    private static readonly object Lock = new();
    private static readonly string PathName = AppPaths.File("handsfreely.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        lock (Lock)
        {
            try
            {
                var info = new FileInfo(PathName);
                if (info.Exists && info.Length > 2 * 1024 * 1024)
                    System.IO.File.Move(PathName, PathName + ".old", true);
                System.IO.File.AppendAllText(PathName, line);
            }
            catch (IOException)
            {
            }
        }
    }

    public static string FilePath => PathName;
}
