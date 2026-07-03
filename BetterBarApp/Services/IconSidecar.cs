using System.IO;
using System.Text;

namespace BetterBarApp.Services;

/// <summary>
/// Per-file display metadata stored in a companion ".bbr" file that sits next to the
/// target file (full filename + ".bbr" — e.g. "Notepad.lnk" → "Notepad.lnk.bbr",
/// so "App.lnk" and "App.exe" never collide).
/// Because the metadata travels with the file on disk, every icon list built from the
/// same directory shares the same name overrides and hide state.
///
/// Format: simple, human-editable "key=value" lines. Recognised keys:
///   name=Friendly Name      (display-name override; omit/blank to use the filename)
///   hide=true               (exclude the file from icon lists)
/// Blank lines and lines starting with '#' are ignored; unknown keys are preserved.
/// NOTE: ordering is NOT stored here — it stays per-item (IconOrder).
/// </summary>
public sealed class IconSidecar
{
    public string? Name { get; set; }
    public bool    Hide { get; set; }

    // Unknown keys, preserved across rewrites so hand edits aren't lost.
    private readonly List<KeyValuePair<string, string>> _other = [];

    /// <summary>The .bbr path for a given file: full filename + ".bbr" (keeps the
    /// original extension, so files differing only by extension don't collide).</summary>
    public static string PathFor(string filePath) => filePath + ".bbr";

    public static bool IsSidecar(string filePath) =>
        filePath.EndsWith(".bbr", StringComparison.OrdinalIgnoreCase);

    public static IconSidecar Read(string filePath)
    {
        var result  = new IconSidecar();
        var sidecar = PathFor(filePath);
        if (!File.Exists(sidecar)) return result;

        try
        {
            foreach (var raw in File.ReadAllLines(sidecar))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                switch (key.ToLowerInvariant())
                {
                    case "name": result.Name = val.Length == 0 ? null : val; break;
                    case "hide": result.Hide = val.Equals("true", StringComparison.OrdinalIgnoreCase)
                                              || val == "1"
                                              || val.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                    default:     result._other.Add(new(key, val)); break;
                }
            }
        }
        catch { }
        return result;
    }

    public void Save(string filePath)
    {
        var sidecar = PathFor(filePath);
        try
        {
            bool hasName = !string.IsNullOrEmpty(Name);
            // Nothing to record → don't litter the directory with an empty file.
            if (!hasName && !Hide && _other.Count == 0)
            {
                if (File.Exists(sidecar)) File.Delete(sidecar);
                return;
            }

            var sb = new StringBuilder();
            if (hasName) sb.AppendLine($"name={Name}");
            if (Hide)    sb.AppendLine("hide=true");
            foreach (var kv in _other) sb.AppendLine($"{kv.Key}={kv.Value}");
            File.WriteAllText(sidecar, sb.ToString());
        }
        catch { }
    }

    // ── Convenience accessors ────────────────────────────────────────────────────
    public static string? GetName(string filePath) => Read(filePath).Name;
    public static bool    IsHidden(string filePath) => Read(filePath).Hide;

    public static void SetName(string filePath, string? name)
    {
        var s = Read(filePath);
        s.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        s.Save(filePath);
    }

    public static void SetHide(string filePath, bool hide)
    {
        var s = Read(filePath);
        s.Hide = hide;
        s.Save(filePath);
    }
}
