using Handsfreely.App.Infrastructure;
using Handsfreely.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace Handsfreely.App.Views;

public sealed partial class SettingsPage : Page
{
    private const string StartupTaskId = "HandsfreelyStartup";
    private static readonly string?[] Languages = [null, "ru", "en"];

    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        LanguageBox.Items.Add(L.T("settings.language.system"));
        LanguageBox.Items.Add("Русский");
        LanguageBox.Items.Add("English");
        LanguageBox.SelectedIndex = Array.IndexOf(Languages, Settings.Current.Language);
        TrayToggle.IsOn = Settings.Current.CloseToTray;
        SmsToggle.IsOn = Settings.Current.SmsToasts;
        VersionText.Text = L.F("settings.version", typeof(App).Assembly.GetName().Version?.ToString(3));
        Loaded += async (_, _) =>
        {
            await LoadStartupAsync();
            _loading = false;
        };
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0) return;
        var language = Languages[LanguageBox.SelectedIndex];
        if (language == Settings.Current.Language) return;
        L.SetLanguage(language);
    }

    private void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.CloseToTray = TrayToggle.IsOn;
        Settings.Current.Save();
    }

    private void OnSmsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.SmsToasts = SmsToggle.IsOn;
        Settings.Current.Save();
    }

    private async Task LoadStartupAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            StartupToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            ShowStartupHint(task.State);
        }
        catch (Exception e)
        {
            Log.Error("startup task unavailable", e);
            StartupToggle.IsEnabled = false;
        }
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (StartupToggle.IsOn) await task.RequestEnableAsync();
            else task.Disable();
            _loading = true;
            StartupToggle.IsOn = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            _loading = false;
            ShowStartupHint(task.State);
        }
        catch (Exception ex)
        {
            Log.Error("startup toggle failed", ex);
        }
    }

    private void ShowStartupHint(StartupTaskState state) =>
        StartupHint.Text = state == StartupTaskState.DisabledByUser ? L.T("settings.startup.blocked") : "";

    private void OnOpenLogClick(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{Log.FilePath}\"") { UseShellExecute = true });
}
