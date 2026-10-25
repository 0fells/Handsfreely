using Windows.Storage;

namespace Handsfreely.App.Infrastructure;

public static class AppPaths
{
    public static string DataDir { get; } = Resolve();

    public static string File(string name) => Path.Combine(DataDir, name);

    private static string Resolve()
    {
        string dir;
        try
        {
            dir = ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handsfreely");
        }
        Directory.CreateDirectory(dir);
        return dir;
    }
}
