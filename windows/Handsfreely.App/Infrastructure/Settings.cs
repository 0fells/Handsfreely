using System.Text.Json;

namespace Handsfreely.App.Infrastructure;

public sealed class Settings
{
    public string? Language { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool SmsToasts { get; set; } = true;
    public string? ActivePhoneId { get; set; }

    private static readonly string PathName = AppPaths.File("settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Current { get; } = Load();

    private static Settings Load()
    {
        if (Demo.Enabled) return new Settings { Language = Demo.Language, CloseToTray = false, SmsToasts = false };
        try
        {
            if (File.Exists(PathName))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Error("settings load failed", e);
        }
        return new Settings();
    }

    public void Save()
    {
        if (Demo.Enabled) return;
        try
        {
            File.WriteAllText(PathName, JsonSerializer.Serialize(this, Options));
        }
        catch (IOException e)
        {
            Log.Error("settings save failed", e);
        }
    }
}
