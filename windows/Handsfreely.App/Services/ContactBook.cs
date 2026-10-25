using Handsfreely.Core.Protocol;

namespace Handsfreely.App.Services;

public sealed class ContactBook
{
    private Dictionary<string, Contact> _byNumber = [];

    public IReadOnlyList<Contact> Contacts { get; private set; } = [];

    public event Action? Changed;

    public void Set(IReadOnlyList<Contact> contacts)
    {
        Contacts = [.. contacts.OrderByDescending(c => c.Starred).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
        var map = new Dictionary<string, Contact>();
        foreach (var c in contacts)
            foreach (var p in c.Phones)
                map.TryAdd(Key(p.Number), c);
        _byNumber = map;
        Changed?.Invoke();
    }

    public void Clear() => Set([]);

    public Contact? Find(string? number) =>
        number is null ? null : _byNumber.GetValueOrDefault(Key(number));

    public string NameOr(string? number, string? fallback = null) =>
        Find(number)?.Name ?? fallback ?? number ?? "";

    public static string Key(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}
