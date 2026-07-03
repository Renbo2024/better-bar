using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Models;

public partial class LauncherItem : BarItem
{
    public LauncherItem()
    {
        TypeKey     = ItemTypes.Launcher;
        DisplayName = "Launcher";
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private string _sourceDirectory = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int    _rows            = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int    _iconSpacing     = 4;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private int    _iconMargin      = 4;

    // Hover tooltip (file name) on each icon.
    [ObservableProperty] private bool _showTooltips   = true;
    [ObservableProperty] private int  _tooltipDelayMs = 700;

    // Ordered file paths within SourceDirectory; drag-drop updates this list.
    // Needs a setter: System.Text.Json does NOT populate getter-only collections
    // on deserialize (verified — it serializes them but reloads them empty).
    public ObservableCollection<string> IconOrder { get; set; } = [];

    [JsonIgnore]
    public override string Description
    {
        get
        {
            var src = string.IsNullOrWhiteSpace(SourceDirectory) ? "(no folder)" : SourceDirectory;
            return $"{src}  ·  {Rows} row{(Rows == 1 ? "" : "s")}  ·  {IconSpacing}px spacing  ·  {IconMargin}px margin";
        }
    }
}
