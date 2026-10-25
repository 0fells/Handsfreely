using Handsfreely.App.Localization;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Handsfreely.App.Services;

public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private readonly Action _show;
    private readonly Action _exit;

    public TrayService(Action show, Action exit)
    {
        _show = show;
        _exit = exit;
    }

    public void Create()
    {
        _icon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Handsfreely.ico")),
            LeftClickCommand = new RelayCommand(_show),
            NoLeftClickDelay = true,
        };
        BuildMenu();
        L.LanguageChanged += BuildMenu;
        _icon.ForceCreate(false);
    }

    public void SetTooltip(string text)
    {
        if (_icon is not null) _icon.ToolTipText = text;
    }

    private void BuildMenu()
    {
        if (_icon is null) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = L.T("tray.open"), Command = new RelayCommand(_show) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = L.T("tray.exit"), Command = new RelayCommand(_exit) });
        _icon.ContextFlyout = menu;
        _icon.ToolTipText = "Handsfreely";
    }

    public void Dispose() => _icon?.Dispose();
}
