using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBarApp.Controls;
using BetterBarApp.Models;
using BetterBarApp.Windows;

namespace BetterBarApp.Services;

/// <summary>
/// Owns the two distinct concepts: <see cref="Definitions"/> (reusable bar layouts —
/// height + items) and <see cref="Panels"/> (placements of a definition on a monitor /
/// edge). One definition can drive many panels; editing a definition refreshes every
/// live panel that uses it.
/// </summary>
public static class PanelManager
{
    public static ObservableCollection<BarDefinition> Definitions { get; } = [];
    public static ObservableCollection<PanelConfig>    Panels      { get; } = [];

    private static readonly Dictionary<Guid, PanelWindow> _activeWindows = [];

    private static readonly JsonSerializerOptions CloneOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // ── Definitions ──────────────────────────────────────────────────────────────

    public static BarDefinition NewDefinition()
    {
        var def = new BarDefinition { Name = $"Bar {Definitions.Count + 1}" };
        Definitions.Add(def);
        return def;
    }

    public static void AddDefinition(BarDefinition def) => Definitions.Add(def);

    public static BarDefinition? GetDefinition(Guid id) =>
        Definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>Panels (placements) that render the given definition.</summary>
    public static IEnumerable<PanelConfig> PanelsFor(BarDefinition def) =>
        Panels.Where(p => p.DefinitionId == def.Id);

    /// <summary>Copies a definition (new Id, deep-copied items); does not copy its panels.</summary>
    public static BarDefinition CloneDefinition(BarDefinition source)
    {
        var clone = new BarDefinition
        {
            Name     = source.Name + " (Copy)",
            HeightPx = source.HeightPx,
        };

        // Deep-copy items via a JSON round-trip — BarItem's [JsonDerivedType]
        // discriminators make each concrete subtype survive the trip.
        var json  = JsonSerializer.Serialize(source.Items.ToList(), CloneOpts);
        var items = JsonSerializer.Deserialize<List<BarItem>>(json, CloneOpts) ?? [];
        foreach (var item in items) clone.Items.Add(item);

        Definitions.Add(clone);
        return clone;
    }

    /// <summary>Removes a definition and all of its panels (closing any live ones).</summary>
    public static void RemoveDefinition(BarDefinition def)
    {
        foreach (var panel in PanelsFor(def).ToList())
            RemovePanel(panel);
        Definitions.Remove(def);
    }

    /// <summary>Re-applies a (possibly edited) definition to every live panel using it.</summary>
    public static void RefreshDefinition(BarDefinition def)
    {
        foreach (var panel in PanelsFor(def))
            if (_activeWindows.TryGetValue(panel.Id, out var window))
                window.Refresh();
    }

    // ── Panels (placements) ──────────────────────────────────────────────────────

    public static void AddPanel(PanelConfig panel) => Panels.Add(panel);

    public static void EnablePanel(PanelConfig panel)
    {
        if (_activeWindows.ContainsKey(panel.Id)) return;
        var def = GetDefinition(panel.DefinitionId);
        if (def == null) return;   // orphaned placement — nothing to render

        panel.IsEnabled = true;
        // Bind to a synthetic screen number; if that screen isn't present (e.g. an
        // RDP session with only the primary), the panel stays enabled but simply
        // doesn't show. Primary (0) always resolves.
        if (ScreenService.ForNumber(panel.MonitorNumber) == null) return;

        var window = new PanelWindow(panel, def);
        _activeWindows[panel.Id] = window;
        window.Show();
    }

    public static void DisablePanel(PanelConfig panel)
    {
        panel.IsEnabled = false;
        if (_activeWindows.TryGetValue(panel.Id, out var window))
        {
            // AppBarWindow cancels Close() unless AllowClose is set.
            window.AllowClose = true;
            window.Close();
            _activeWindows.Remove(panel.Id);
        }
    }

    /// <summary>Applies a placement change (monitor/edge) to a live panel.</summary>
    public static void RefreshPanel(PanelConfig panel)
    {
        if (_activeWindows.TryGetValue(panel.Id, out var window))
            window.Refresh();
        else if (panel.IsEnabled)
            EnablePanel(panel);
    }

    public static void RemovePanel(PanelConfig panel)
    {
        DisablePanel(panel);
        Panels.Remove(panel);
    }

    /// <summary>
    /// The Start Button that the Windows key should open: the leftmost one on the highest-priority
    /// live panel. Panels are ranked by monitor — primary (number 0) first, then ascending monitor
    /// number — and within a panel the leftmost Start Button wins.
    /// </summary>
    public static StartButtonControl? FindPrimaryStartButton()
    {
        var ranked = _activeWindows.Values
            .Select(w => new { Window = w, w.Panel })
            .OrderBy(x => x.Panel.MonitorNumber)
            .ThenBy(x => x.Panel.Position);

        foreach (var x in ranked)
            if (x.Window.FindStartButton() is { } sb) return sb;
        return null;
    }

    // ── Late-appearing monitors (RDP / hot-plug): a cheap, CREATE-ONLY poll ──────
    private static System.Windows.Threading.DispatcherTimer? _screenPollTimer;

    /// <summary>
    /// Periodically bring up bars for enabled panels whose monitor has appeared since launch (common
    /// over RDP, where displays connect after the app starts). This is deliberately CREATE-ONLY: it
    /// never closes a live bar, so it cannot disturb an existing AppBar reservation. Repositioning /
    /// removal of live bars on display changes is left to ManagedShell (ProcessScreenChanges), the same
    /// engine RetroBar relies on.
    /// </summary>
    public static void StartScreenPolling()
    {
        _screenPollTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _screenPollTimer.Tick += (_, _) => CreateMissingBars();
        _screenPollTimer.Start();
    }

    private static void CreateMissingBars()
    {
        // Fast path: if every enabled panel already has a live bar, do nothing at all — no monitor
        // re-enumeration, no renumbering. (Enumerating monitors is cheap, but skipping it keeps the
        // steady state completely idle.)
        bool anyMissing = Panels.Any(p => p.IsEnabled && !_activeWindows.ContainsKey(p.Id));
        if (!anyMissing) return;

        ScreenService.Detect();
        foreach (var panel in Panels.Where(p => p.IsEnabled && !_activeWindows.ContainsKey(p.Id)).ToList())
            if (ScreenService.ForNumber(panel.MonitorNumber) != null)
                EnablePanel(panel);   // its monitor is present now → bring the bar up (won't double-create)
    }

    /// <summary>Prepares all live bars to unregister cleanly before app shutdown.</summary>
    public static void ShutdownAll()
    {
        ShellService.AppBarManager.SignalGracefulShutdown();
        foreach (var window in _activeWindows.Values)
            window.AllowClose = true;
    }
}
