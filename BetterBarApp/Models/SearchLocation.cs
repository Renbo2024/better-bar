namespace BetterBarApp.Models;

/// <summary>
/// A user-configured folder included in a start button's search (spec §4.4).
/// Owned by a <see cref="StartButtonItem"/> — each start button has its own list,
/// so two buttons can search entirely different folders.
/// </summary>
public sealed class SearchLocation
{
    public string Name    { get; set; } = "";
    public string Path    { get; set; } = "";
    public bool   Cascade { get; set; } = true;

    /// <summary>Whether recency (frecency) ranking is collected/applied for this folder. Default off.</summary>
    public bool   Frecency { get; set; }

    /// <summary>If set, only files whose name matches this regex are indexed.</summary>
    public string IncludeRegex { get; set; } = "";

    /// <summary>If set, files whose name matches this regex are excluded.</summary>
    public string ExcludeRegex { get; set; } = "";
}
