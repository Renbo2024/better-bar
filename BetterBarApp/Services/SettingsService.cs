using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBarApp.Models;

namespace BetterBarApp.Services;

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterBar");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Deep-clones a bar item by round-tripping it through the same polymorphic JSON used for
    /// persistence — so the concrete type and all its settings are preserved.
    /// </summary>
    public static BarItem CloneItem(BarItem item)
    {
        var json = JsonSerializer.Serialize<BarItem>(item, Opts);   // declared type → writes $type
        return JsonSerializer.Deserialize<BarItem>(json, Opts)!;
    }

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var text = File.ReadAllText(FilePath).TrimStart();
            if (text.StartsWith('['))
            {
                LoadLegacy(text);   // pre-refactor: bare array of panels-with-items
                return;
            }

            var root = JsonSerializer.Deserialize<RootRecord>(text, Opts);
            if (root == null) return;

            foreach (var d in root.Definitions)
                PanelManager.AddDefinition(d.ToDefinition());

            foreach (var p in root.Panels)
            {
                var panel = p.ToPanel();
                PanelManager.AddPanel(panel);
                if (p.WasEnabled) PanelManager.EnablePanel(panel);
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var root = new RootRecord
            {
                Definitions = PanelManager.Definitions.Select(DefinitionRecord.From).ToList(),
                Panels      = PanelManager.Panels.Select(PanelRecord.From).ToList(),
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(root, Opts));
        }
        catch { }
    }

    // ── Migration from the old format ────────────────────────────────────────────
    // Old settings.json was a bare array of panels that each embedded their own
    // Name/HeightPx/Items. Convert each into one definition + one panel placement.
    private static void LoadLegacy(string text)
    {
        var legacy = JsonSerializer.Deserialize<List<LegacyPanelRecord>>(text, Opts);
        if (legacy == null) return;

        foreach (var l in legacy)
        {
            var def = new BarDefinition { Name = l.Name, HeightPx = l.HeightPx };
            foreach (var item in l.Items) def.Items.Add(item);
            PanelManager.AddDefinition(def);

            var panel = new PanelConfig
            {
                DefinitionId  = def.Id,
                Position      = l.Position,
                MonitorNumber = ScreenService.NumberForDevice(l.MonitorDeviceName),
            };
            PanelManager.AddPanel(panel);
            if (l.WasEnabled) PanelManager.EnablePanel(panel);
        }
    }

    // ── Records ──────────────────────────────────────────────────────────────────

    private class RootRecord
    {
        public List<DefinitionRecord> Definitions { get; set; } = [];
        public List<PanelRecord>      Panels      { get; set; } = [];
    }

    private class DefinitionRecord
    {
        public Guid          Id       { get; set; }
        public string        Name     { get; set; } = "";
        public int           HeightPx { get; set; }
        // BarItem serializes with a $type discriminator via [JsonDerivedType].
        public List<BarItem> Items    { get; set; } = [];

        public static DefinitionRecord From(BarDefinition d) => new()
        {
            Id       = d.Id,
            Name     = d.Name,
            HeightPx = d.HeightPx,
            Items    = [.. d.Items],
        };

        public BarDefinition ToDefinition()
        {
            var def = new BarDefinition { Id = Id, Name = Name, HeightPx = HeightPx };
            foreach (var item in Items) def.Items.Add(item);
            return def;
        }
    }

    private class PanelRecord
    {
        public Guid          Id            { get; set; }
        public Guid          DefinitionId  { get; set; }
        public PanelPosition Position      { get; set; }
        public int?          MonitorNumber { get; set; }      // synthetic screen number (0 = primary)
        public string?       MonitorDeviceName { get; set; }  // legacy; migrated to a number on load
        public bool          WasEnabled    { get; set; }

        public static PanelRecord From(PanelConfig p) => new()
        {
            Id            = p.Id,
            DefinitionId  = p.DefinitionId,
            Position      = p.Position,
            MonitorNumber = p.MonitorNumber,
            WasEnabled    = p.IsEnabled,
        };

        public PanelConfig ToPanel() => new()
        {
            Id            = Id,
            DefinitionId  = DefinitionId,
            Position      = Position,
            // New records carry MonitorNumber; older ones only a device name → map it.
            MonitorNumber = MonitorNumber ?? ScreenService.NumberForDevice(MonitorDeviceName),
        };
    }

    private class LegacyPanelRecord
    {
        public string        Name              { get; set; } = "";
        public int           HeightPx          { get; set; }
        public PanelPosition Position          { get; set; }
        public string        MonitorDeviceName { get; set; } = "";
        public bool          WasEnabled        { get; set; }
        public List<BarItem> Items             { get; set; } = [];
    }
}
