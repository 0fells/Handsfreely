using System.Text.RegularExpressions;
using Handsfreely.App.Localization;

namespace Handsfreely.Core.Tests;

public partial class StringsTests
{
    private static readonly string[] DynamicKeys =
    [
        "hfp.idle", "hfp.connecting", "hfp.connected", "hfp.notfound", "hfp.accessdenied", "hfp.failed",
        "phone.other", "phone.mobile", "phone.home", "phone.work",
        "calllog.in", "calllog.out", "calllog.missed", "calllog.rejected", "calllog.blocked", "calllog.voicemail",
        "perm.name.contacts", "perm.name.calllog", "perm.name.phone",
    ];

    [Fact]
    public void EveryUsedKeyIsTranslated()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Handsfreely.App"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var app = Path.Combine(dir!.FullName, "Handsfreely.App");

        var used = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => KeyRegex().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => !k.EndsWith('.'))
            .Concat(DynamicKeys)
            .ToHashSet();

        Assert.NotEmpty(used);
        var missing = used.Where(k => !Strings.Table.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "missing keys: " + string.Join(", ", missing));
        var empty = Strings.Table.Where(kv => kv.Value.Ru.Length == 0 || kv.Value.En.Length == 0).Select(kv => kv.Key).ToList();
        Assert.True(empty.Count == 0, "untranslated: " + string.Join(", ", empty));
    }

    [GeneratedRegex("""L\.[TF]\(["']([a-z0-9.]+)["']""")]
    private static partial Regex KeyRegex();
}
