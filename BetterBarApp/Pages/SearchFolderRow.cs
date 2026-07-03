using System.ComponentModel;

namespace BetterBarApp.Pages;

/// <summary>
/// Working-copy row for a start button's custom search folder: persisted fields plus a
/// live Status. Edited inline (recency) and via the per-folder dialog. Name/Frecency
/// raise change notifications because they're shown in the list.
/// </summary>
public sealed class SearchFolderRow : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }

    public string Path    { get; set; } = "";
    public bool   Cascade { get; set; } = true;

    private bool _frecency;
    public bool Frecency
    {
        get => _frecency;
        set { _frecency = value; PropertyChanged?.Invoke(this, new(nameof(Frecency))); }
    }

    public string IncludeRegex { get; set; } = "";
    public string ExcludeRegex { get; set; } = "";

    private string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
