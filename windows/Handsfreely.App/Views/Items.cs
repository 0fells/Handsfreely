using System.ComponentModel;
using System.Globalization;
using Handsfreely.App.Localization;
using Handsfreely.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Handsfreely.App.Views;

public static class Format
{
    public static DateTime Local(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;

    public static string When(long unixMs)
    {
        var t = Local(unixMs);
        var now = DateTime.Now;
        var culture = L.Language == "ru" ? new CultureInfo("ru-RU") : new CultureInfo("en-US");
        if (t.Date == now.Date) return t.ToString("t", culture);
        if (t.Date == now.Date.AddDays(-1)) return L.T("time.yesterday");
        if ((now.Date - t.Date).TotalDays < 7) return t.ToString("dddd", culture);
        return t.ToString("d", culture);
    }

    public static string Duration(int seconds) =>
        seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letters = parts.Where(p => char.IsLetter(p[0])).Take(2).Select(p => char.ToUpper(p[0]));
        var s = new string(letters.ToArray());
        return s.Length > 0 ? s : "#";
    }

    public static string NumberType(PhoneNumberType type) => L.T($"phone.{type.ToString().ToLowerInvariant()}");
}

public sealed class CallLogItem(CallLogEntry entry, string? contactName)
{
    public CallLogEntry Entry { get; } = entry;
    public string Title { get; } = contactName ?? entry.Name ?? entry.Number;
    public string Subtitle { get; } = (contactName ?? entry.Name) is null ? L.T($"calllog.{entry.Type.ToString().ToLowerInvariant()}") : entry.Number;
    public string When { get; } = Format.When(entry.Date);
    public string Duration { get; } = entry.Duration > 0 ? Format.Duration(entry.Duration) : "";

    public string Glyph { get; } = entry.Type switch
    {
        CallLogType.Out => "",
        CallLogType.Missed or CallLogType.Rejected or CallLogType.Blocked => "",
        _ => "",
    };

    public Brush Brush { get; } = entry.Type is CallLogType.Missed or CallLogType.Rejected
        ? (Brush)Application.Current.Resources["MissedCallBrush"]
        : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
}

public sealed class ContactItem(Contact contact) : INotifyPropertyChanged
{
    private ImageSource? _photo;

    public Contact Contact { get; } = contact;
    public string Name => Contact.Name;
    public string Initials { get; } = Format.Initials(contact.Name);
    public string FirstNumber { get; } = contact.Phones.FirstOrDefault()?.Number ?? "";
    public bool PhotoRequested { get; set; }

    public ImageSource? Photo
    {
        get => _photo;
        set
        {
            _photo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Photo)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NumberItem(PhoneNumber number)
{
    public string Number { get; } = number.Number;
    public string Type { get; } = Format.NumberType(number.Type);
}

public sealed class ThreadItem(SmsThread thread, string title) : INotifyPropertyChanged
{
    private SmsThread _thread = thread;

    public SmsThread Thread
    {
        get => _thread;
        set
        {
            _thread = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    public string Title { get; } = title;
    public string Initials => Format.Initials(Title);
    public string Snippet => Thread.Snippet;
    public string When => Format.When(Thread.Date);
    public Visibility UnreadVisibility => Thread.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Unread => Thread.Unread.ToString(CultureInfo.InvariantCulture);
    public Windows.UI.Text.FontWeight Weight => Thread.Unread > 0 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MessageItem(SmsMessage message)
{
    public SmsMessage Message { get; } = message;
    public string Body => Message.Body;
    public string When { get; } = Format.Local(message.Date).ToString("g", L.Language == "ru" ? new CultureInfo("ru-RU") : new CultureInfo("en-US"));
    public HorizontalAlignment Alignment { get; } = message.Type == SmsType.Out ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Brush Background { get; } = message.Type == SmsType.Out
        ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
        : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

    public Brush Foreground { get; } = message.Type == SmsType.Out
        ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
}

public static class Images
{
    public static async Task<ImageSource> FromBytesAsync(byte[] data)
    {
        var bmp = new BitmapImage();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(data));
        stream.Seek(0);
        await bmp.SetSourceAsync(stream);
        return bmp;
    }
}
