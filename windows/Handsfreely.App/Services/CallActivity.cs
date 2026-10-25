namespace Handsfreely.App.Services;

public static class CallActivity
{
    private static volatile bool _active;

    public static bool Active => _active;

    public static void Set(bool active) => _active = active;
}
