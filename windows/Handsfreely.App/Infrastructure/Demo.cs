namespace Handsfreely.App.Infrastructure;

public static class Demo
{
    static Demo()
    {
        var args = Environment.GetCommandLineArgs();
        var start = Array.IndexOf(args, "--demo");
        if (start < 0) return;
        Enabled = true;
        foreach (var a in args.Skip(start + 1))
        {
            if (a is "ru" or "en") Language = a;
            else if (a == "dark") Dark = true;
            else if (Enum.TryParse<AppPage>(a, true, out var page)) Page = page;
        }
    }

    public static bool Enabled { get; }
    public static string Language { get; } = "en";
    public static bool Dark { get; }
    public static AppPage Page { get; } = AppPage.Calls;
}
