using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

public partial class StartButtonItem : BarItem
{
    public StartButtonItem()
    {
        TypeKey     = ItemTypes.StartButton;
        DisplayName = "Start Button";
    }

    // Stable identity for this start button, used to key its private search index /
    // recency store. Survives edits; a definition shared across monitors reuses it.
    public Guid Id { get; set; } = Guid.NewGuid();

    // Margin around the button on the panel.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private int _margin = 10;

    // Folder shown in the popup's icon list. Empty = no list.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private string _sourceDirectory = string.Empty;

    // Popup row icon dimensions (px).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private int _iconSize = 24;

    // Padding around the icon image inside the popup row (px).
    [ObservableProperty]
    private int _iconMargin = 4;

    // Padding around the text label inside the popup row (px).
    [ObservableProperty]
    private int _textMargin = 4;

    // Popup row text size (px).
    [ObservableProperty]
    private int _textSize = 13;

    // Maximum width of the label before ellipsis (px).
    [ObservableProperty]
    private int _maxTextWidth = 240;

    // Minimum popup height (px) so search results have room even with no icon-list entries.
    [ObservableProperty]
    private int _minMenuHeight = 360;

    // ── Type-to-find search (always replaces the icon list while the user types) ──
    // Each start button has its OWN search definition + engine: which built-in sources
    // it includes, its custom folders, and per-source recency. One button can be a
    // single-folder search, another apps-only — they share nothing.
    [ObservableProperty] private bool _searchQuickLaunch = true;
    [ObservableProperty] private bool _searchApps        = true;
    [ObservableProperty] private bool _searchSettings    = true;
    [ObservableProperty] private bool _searchDocuments   = true;
    [ObservableProperty] private int  _searchMaxResults  = 8;
    // Result fade-in duration in ms; 0 = no transition (instant).
    [ObservableProperty] private int  _searchFadeMs      = 90;

    // Per-source recency (frecency) toggles for the built-in sources. Default off.
    [ObservableProperty] private bool _frecencyQuickLaunch;
    [ObservableProperty] private bool _frecencyApps;
    [ObservableProperty] private bool _frecencySettings;
    [ObservableProperty] private bool _frecencyDocuments;

    // This button's own custom search folders. Needs a setter for JSON round-trip.
    public List<SearchLocation> SearchLocations { get; set; } = new();

    // Per-file display-name overrides set via the "Rename" menu item. Keyed by full path.
    // Needs a setter — System.Text.Json does not populate getter-only collections.
    public Dictionary<string, string> NameOverrides { get; set; } = new();

    // Full paths of files hidden via the "Hide" menu item. Excluded from the icon list.
    public List<string> HiddenFiles { get; set; } = new();

    // Ordered file paths within SourceDirectory; updated by drag-drop reorder.
    // Files in the directory but absent here are appended last (alphabetical).
    public List<string> IconOrder { get; set; } = new();

    // Per-button search aliases: typed query → expansion (e.g. "ps" → "powershell"). When the whole
    // typed query equals an alias, the engine ALSO searches the expansion and ranks those matches above
    // matches of the literal alias text. Keyed by alias; value is the expansion. Setter for JSON round-trip.
    public Dictionary<string, string> SearchAliases { get; set; } = new();

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var src = string.IsNullOrWhiteSpace(SourceDirectory) ? "(no folder)" : SourceDirectory;
            return $"{src}  ·  {Margin}px button  ·  {IconSize}px icons";
        }
    }
}
