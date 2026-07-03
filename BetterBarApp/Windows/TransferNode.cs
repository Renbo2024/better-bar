using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBarApp.Windows;

/// <summary>A checkable node in the export/import object tree (a category or a leaf object).</summary>
public partial class TransferNode : ObservableObject
{
    public string  Title      { get; init; } = "";
    public bool    IsCategory { get; init; }
    public string? Status     { get; init; }   // import only: "New" / "Overwrites existing"
    public bool    Conflict   { get; init; }   // import only: would overwrite an existing object
    public object? Tag        { get; init; }   // leaf payload: Guid (definition) or string (theme name)

    public ObservableCollection<TransferNode> Children { get; } = [];

    [ObservableProperty] private bool _isChecked;

    // A category toggles all of its children.
    partial void OnIsCheckedChanged(bool value)
    {
        if (!IsCategory) return;
        foreach (var c in Children) c.IsChecked = value;
    }
}
