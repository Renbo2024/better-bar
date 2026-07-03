using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace BetterBarApp.Services;

/// <summary>
/// Owns the app's themes and swaps the active palette dictionary at runtime. Styles reference palette
/// keys via DynamicResource, so replacing the merged theme dictionary (or overwriting one of its
/// entries) re-colours every panel and item live.
///
/// Two kinds of theme: built-in (compiled XAML in Themes/, read-only) and user (JSON files under
/// %APPDATA%\BetterBar\Themes, editable in the theme editor — clone / import / export supported).
/// </summary>
public static class ThemeService
{
    public sealed class ThemeInfo
    {
        public string  Name     { get; set; }
        public Uri?    Source   { get; }     // built-in: compiled dictionary
        public string? FilePath { get; }     // user: backing JSON file
        public bool    BuiltIn  { get; }

        public ThemeInfo(string name, Uri? source, string? filePath, bool builtIn)
        { Name = name; Source = source; FilePath = filePath; BuiltIn = builtIn; }
    }

    private static readonly string ThemesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterBar", "Themes");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static readonly List<ThemeInfo> _themes =
    [
        new("Dark",  new Uri("Themes/Dark.xaml",  UriKind.Relative), null, true),
        new("Light", new Uri("Themes/Light.xaml", UriKind.Relative), null, true),
    ];

    // Migrate the pre-rename saved theme names so existing installs keep their choice.
    private static readonly Dictionary<string, string> _renamed = new()
    {
        ["Windows 11"] = "Dark",
        ["Windows 7"]  = "Light",
    };

    /// <summary>All themes (built-in first, then user), refreshed from disk.</summary>
    public static IReadOnlyList<ThemeInfo> Available => _themes;

    public static ThemeInfo Current { get; private set; } = _themes[0];

    private static ThemeInfo DefaultTheme => _themes[0];   // Dark — the canonical full key set

    // ── Lifecycle ───────────────────────────────────────────────────────────────
    public static void Initialize()
    {
        AppPrefs.Load();
        LoadUserThemes();
        if (_renamed.TryGetValue(AppPrefs.Theme, out var newName)) AppPrefs.Theme = newName;
        var saved = _themes.FirstOrDefault(t => t.Name == AppPrefs.Theme);
        if (saved != null) Apply(saved.Name);
    }

    private static void LoadUserThemes()
    {
        _themes.RemoveAll(t => !t.BuiltIn);
        try
        {
            if (!Directory.Exists(ThemesDir)) return;
            foreach (var path in Directory.EnumerateFiles(ThemesDir, "*.json"))
            {
                var file = LoadFile(path);
                if (file != null) _themes.Add(new ThemeInfo(file.Name, null, path, false));
            }
        }
        catch { }
    }

    public static ThemeInfo? Find(string name) => _themes.FirstOrDefault(t => t.Name == name);

    // ── Apply ─────────────────────────────────────────────────────────────────────
    public static void Apply(string name)
    {
        var info = Find(name);
        if (info == null) return;

        if (info.BuiltIn)
            SwapDictionary(new ResourceDictionary { Source = info.Source });
        else
            SwapDictionary(BuildDictionary(info.Name, ReadValues(info)));

        Current = info;
        AppPrefs.Theme = info.Name;
        AppPrefs.Save();
    }

    // The active theme dictionary is the one carrying the ThemeName marker.
    private static void SwapDictionary(ResourceDictionary replacement)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Contains("ThemeName"));
        if (existing != null) dicts[dicts.IndexOf(existing)] = replacement;
        else                  dicts.Insert(0, replacement);
    }

    private static ResourceDictionary? ActiveDictionary() =>
        Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Contains("ThemeName"));

    private static ResourceDictionary BuildDictionary(string name, IDictionary<string, object> values)
    {
        var rd = new ResourceDictionary { ["ThemeName"] = name };
        foreach (var k in ThemeSchema.Keys)
        {
            if (!values.TryGetValue(k.Key, out var v)) continue;
            rd[k.Key] = ToResource(k.Kind, v);
        }
        return rd;
    }

    private static object ToResource(ThemeKeyKind kind, object value) => kind switch
    {
        ThemeKeyKind.CornerRadius => new CornerRadius(Convert.ToInt32(value)),
        _ => Freeze(new SolidColorBrush((Color)value)),
    };

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Live-update a single key of the active theme (instant preview while editing).</summary>
    public static void SetLiveValue(string key, ThemeKeyKind kind, object value)
    {
        var active = ActiveDictionary();
        if (active != null) active[key] = ToResource(kind, value);
    }

    // ── Reading values ──────────────────────────────────────────────────────────
    /// <summary>Effective palette values of a theme (Color for colours, int for corner radius),
    /// always complete — missing keys fall back to the default (Dark) theme.</summary>
    public static Dictionary<string, object> ReadValues(ThemeInfo info)
    {
        var result = ReadDictionary(DefaultTheme.Source!);   // complete baseline
        if (info.BuiltIn)
        {
            foreach (var kv in ReadDictionary(info.Source!)) result[kv.Key] = kv.Value;
        }
        else if (info.FilePath != null && LoadFile(info.FilePath) is { } file)
        {
            foreach (var (key, raw) in file.Values)
            {
                var k = ThemeSchema.Find(key);
                if (k == null) continue;
                if (k.Kind == ThemeKeyKind.CornerRadius) { if (int.TryParse(raw, out var i)) result[key] = i; }
                else if (ParseColor(raw) is { } c) result[key] = c;
            }
        }
        return result;
    }

    private static Dictionary<string, object> ReadDictionary(Uri source)
    {
        var rd = new ResourceDictionary { Source = source };
        var d = new Dictionary<string, object>();
        foreach (var k in ThemeSchema.Keys)
        {
            if (!rd.Contains(k.Key)) continue;
            switch (rd[k.Key])
            {
                case SolidColorBrush b when k.Kind == ThemeKeyKind.Color: d[k.Key] = b.Color; break;
                case CornerRadius cr when k.Kind == ThemeKeyKind.CornerRadius: d[k.Key] = (int)cr.TopLeft; break;
            }
        }
        return d;
    }

    // ── User theme CRUD ───────────────────────────────────────────────────────────
    public static ThemeInfo CreateUserTheme(string name, IDictionary<string, object> values)
    {
        Directory.CreateDirectory(ThemesDir);
        var info = new ThemeInfo(UniqueName(name), null, Path.Combine(ThemesDir, Guid.NewGuid() + ".json"), false);
        _themes.Add(info);
        Save(info, values);
        return info;
    }

    public static ThemeInfo CloneTheme(ThemeInfo source, string? newName = null) =>
        CreateUserTheme(newName ?? source.Name + " (Copy)", ReadValues(source));

    public static void Save(ThemeInfo info, IDictionary<string, object> values)
    {
        if (info.FilePath == null) return;
        var file = new ThemeFile
        {
            Name = info.Name,
            Values = values.ToDictionary(
                kv => kv.Key,
                kv => kv.Value is Color c ? HexOf(c) : Convert.ToInt32(kv.Value).ToString()),
        };
        try { File.WriteAllText(info.FilePath, JsonSerializer.Serialize(file, JsonOpts)); } catch { }
    }

    public static void Rename(ThemeInfo info, string newName)
    {
        if (info.BuiltIn) return;
        info.Name = UniqueName(newName, info);
        Save(info, ReadValues(info));
        if (Current == info) { AppPrefs.Theme = info.Name; AppPrefs.Save(); }
    }

    public static void DeleteUserTheme(ThemeInfo info)
    {
        if (info.BuiltIn) return;
        try { if (info.FilePath != null) File.Delete(info.FilePath); } catch { }
        _themes.Remove(info);
        if (Current == info) Apply(DefaultTheme.Name);
    }

    /// <summary>Palette values of a theme as serialisable strings (hex / int) — for config export.</summary>
    public static Dictionary<string, string> RawValues(ThemeInfo info) =>
        ReadValues(info).ToDictionary(
            kv => kv.Key,
            kv => kv.Value is Color c ? HexOf(c) : Convert.ToInt32(kv.Value).ToString());

    public static bool UserThemeExists(string name) => _themes.Any(t => !t.BuiltIn && t.Name == name);

    /// <summary>Creates or (if a user theme with this name exists) overwrites a theme from raw values.</summary>
    public static void ImportUserTheme(string name, Dictionary<string, string> raw)
    {
        var values = ReadValues(DefaultTheme);   // complete baseline, then overlay
        foreach (var (key, rawValue) in raw)
        {
            var k = ThemeSchema.Find(key);
            if (k == null) continue;
            if (k.Kind == ThemeKeyKind.CornerRadius) { if (int.TryParse(rawValue, out var i)) values[key] = i; }
            else if (ParseColor(rawValue) is { } c) values[key] = c;
        }

        var existing = _themes.FirstOrDefault(t => !t.BuiltIn && t.Name == name);
        if (existing != null)
        {
            Save(existing, values);
            if (Current == existing) Apply(existing.Name);   // re-apply if it's the active theme
        }
        else CreateUserTheme(name, values);
    }

    public static ThemeInfo Import(string path)
    {
        var file = LoadFile(path) ?? new ThemeFile { Name = Path.GetFileNameWithoutExtension(path) };
        var values = ReadValues(DefaultTheme);   // baseline, then overlay the imported values
        foreach (var (key, raw) in file.Values)
        {
            var k = ThemeSchema.Find(key);
            if (k == null) continue;
            if (k.Kind == ThemeKeyKind.CornerRadius) { if (int.TryParse(raw, out var i)) values[key] = i; }
            else if (ParseColor(raw) is { } c) values[key] = c;
        }
        return CreateUserTheme(string.IsNullOrWhiteSpace(file.Name) ? "Imported theme" : file.Name, values);
    }

    public static void Export(ThemeInfo info, string path)
    {
        var file = new ThemeFile
        {
            Name = info.Name,
            Values = ReadValues(info).ToDictionary(
                kv => kv.Key,
                kv => kv.Value is Color c ? HexOf(c) : Convert.ToInt32(kv.Value).ToString()),
        };
        try { File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOpts)); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private static string UniqueName(string desired, ThemeInfo? self = null)
    {
        string name = string.IsNullOrWhiteSpace(desired) ? "Custom theme" : desired.Trim();
        if (!_themes.Any(t => t != self && t.Name == name)) return name;
        for (int i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!_themes.Any(t => t != self && t.Name == candidate)) return candidate;
        }
    }

    private static ThemeFile? LoadFile(string path)
    {
        try { return JsonSerializer.Deserialize<ThemeFile>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }
    }

    private static string HexOf(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }

    private sealed class ThemeFile
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = [];
    }
}
