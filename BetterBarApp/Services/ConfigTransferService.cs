using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBarApp.Models;

namespace BetterBarApp.Services;

// ── Portable bundle format ──────────────────────────────────────────────────────
// A .bbconfig file is JSON: a versioned envelope holding any subset of the user's themes and
// bar definitions. Bar items keep their polymorphic $type discriminators (same as settings.json),
// and each definition carries its placements so a full backup/restore round-trips.
public sealed class ConfigBundle
{
    // Empty by default so a foreign JSON file (which won't carry this marker) is rejected by Read;
    // Write always stamps it. (Don't default this to the marker, or any {} object looks valid.)
    public string Format  { get; set; } = "";
    public int    Version { get; set; } = 1;
    public List<ThemeEntry>      Themes      { get; set; } = [];
    public List<DefinitionEntry> Definitions { get; set; } = [];
}

public sealed class ThemeEntry
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class DefinitionEntry
{
    public Guid          Id         { get; set; }
    public string        Name       { get; set; } = "";
    public int           HeightPx   { get; set; }
    public List<BarItem> Items      { get; set; } = [];   // polymorphic via BarItem [JsonDerivedType]
    public List<PlacementEntry> Placements { get; set; } = [];
}

public sealed class PlacementEntry
{
    public PanelPosition Position      { get; set; }
    public int           MonitorNumber { get; set; }
    public bool          WasEnabled    { get; set; }
}

/// <summary>
/// Exports a chosen subset of themes + bar definitions to a .bbconfig file, and imports them back
/// with per-object overwrite control (the caller decides which entries to apply).
/// </summary>
public static class ConfigTransferService
{
    public const string Extension = ".bbconfig";
    public const string FileFilter = "BetterBar configuration (*.bbconfig)|*.bbconfig|JSON (*.json)|*.json";
    private const string FormatId = "BetterBar.Config";

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };

    public static ConfigBundle BuildBundle(IEnumerable<string> themeNames, IEnumerable<Guid> definitionIds)
    {
        var bundle = new ConfigBundle();

        foreach (var name in themeNames)
        {
            var info = ThemeService.Find(name);
            if (info == null) continue;
            bundle.Themes.Add(new ThemeEntry { Name = info.Name, Values = ThemeService.RawValues(info) });
        }

        foreach (var id in definitionIds)
        {
            var def = PanelManager.GetDefinition(id);
            if (def == null) continue;
            var entry = new DefinitionEntry { Id = def.Id, Name = def.Name, HeightPx = def.HeightPx, Items = def.Items.ToList() };
            foreach (var p in PanelManager.PanelsFor(def))
                entry.Placements.Add(new PlacementEntry { Position = p.Position, MonitorNumber = p.MonitorNumber, WasEnabled = p.IsEnabled });
            bundle.Definitions.Add(entry);
        }
        return bundle;
    }

    public static void Write(ConfigBundle bundle, string path)
    {
        bundle.Format = FormatId;
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, Opts));
    }

    public static ConfigBundle? Read(string path)
    {
        try { return ReadJson(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>Parses bundle JSON; returns null for malformed JSON or a missing/foreign format marker.</summary>
    public static ConfigBundle? ReadJson(string json)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<ConfigBundle>(json, Opts);
            return bundle?.Format == FormatId ? bundle : null;
        }
        catch { return null; }
    }

    /// <summary>Applies the chosen themes and definitions; conflicting objects are overwritten
    /// (the caller only passes in objects the user consented to via the import checkboxes).</summary>
    public static void Apply(ConfigBundle bundle, ISet<string> themeNames, ISet<Guid> definitionIds)
    {
        foreach (var t in bundle.Themes)
            if (themeNames.Contains(t.Name)) ThemeService.ImportUserTheme(t.Name, t.Values);

        foreach (var d in bundle.Definitions)
            if (definitionIds.Contains(d.Id)) ApplyDefinition(d);

        SettingsService.Save();
    }

    private static void ApplyDefinition(DefinitionEntry e)
    {
        var existing = PanelManager.GetDefinition(e.Id);
        if (existing != null)   // overwrite content; leave its existing placements in place
        {
            existing.Name     = e.Name;
            existing.HeightPx  = e.HeightPx;
            existing.Items.Clear();
            foreach (var item in e.Items) existing.Items.Add(item);
            PanelManager.RefreshDefinition(existing);
        }
        else
        {
            var def = new BarDefinition { Id = e.Id, Name = e.Name, HeightPx = e.HeightPx };
            foreach (var item in e.Items) def.Items.Add(item);
            PanelManager.AddDefinition(def);
            foreach (var p in e.Placements)
            {
                var panel = new PanelConfig { DefinitionId = def.Id, Position = p.Position, MonitorNumber = p.MonitorNumber };
                PanelManager.AddPanel(panel);
                if (p.WasEnabled) PanelManager.EnablePanel(panel);
            }
        }
    }
}
