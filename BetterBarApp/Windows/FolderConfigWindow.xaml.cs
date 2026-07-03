using System.Text.RegularExpressions;
using System.Windows;
using BetterBarApp.Pages;

namespace BetterBarApp.Windows;

/// <summary>
/// Per-folder config for a custom search location: display name, subfolder cascade,
/// recency toggle, and the two regex filters ("Only include" / "Always exclude").
/// Edits the working-copy <see cref="SearchFolderRow"/> in place; the start button
/// page persists and reloads the affected source.
/// </summary>
public partial class FolderConfigWindow : Window
{
    private readonly SearchFolderRow _row;

    public FolderConfigWindow(SearchFolderRow row, Window owner)
    {
        _row  = row;
        Owner = owner;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PathText.Text       = _row.Path;
            NameBox.Text        = _row.Name;
            CascadeBox.IsChecked = _row.Cascade;
            FrecencyBox.IsChecked = _row.Frecency;
            IncludeBox.Text     = _row.IncludeRegex;
            ExcludeBox.Text     = _row.ExcludeRegex;
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateRegex(IncludeBox.Text, "Only include") ||
            !ValidateRegex(ExcludeBox.Text, "Always exclude"))
            return;

        _row.Name         = NameBox.Text.Trim();
        _row.Cascade      = CascadeBox.IsChecked == true;
        _row.Frecency     = FrecencyBox.IsChecked == true;
        _row.IncludeRegex = IncludeBox.Text.Trim();
        _row.ExcludeRegex = ExcludeBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // Reject an invalid pattern up front rather than silently treating it as "no filter".
    private bool ValidateRegex(string pattern, string label)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { _ = new Regex(pattern); return true; }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, $"\"{label}\" is not a valid regular expression:\n\n{ex.Message}",
                "Invalid regex", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
