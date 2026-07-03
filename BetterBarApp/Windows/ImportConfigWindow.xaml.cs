using System.Collections.ObjectModel;
using System.Windows;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

public partial class ImportConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ConfigBundle _bundle;
    private readonly TransferNode _themesRoot;
    private readonly TransferNode _barsRoot;

    public ImportConfigWindow(ConfigBundle bundle)
    {
        _bundle = bundle;
        InitializeComponent();

        var roots = new ObservableCollection<TransferNode>();

        if (bundle.Themes.Count > 0)
        {
            _themesRoot = new TransferNode { Title = "Themes", IsCategory = true, IsChecked = true };
            foreach (var t in bundle.Themes)
            {
                bool conflict = ThemeService.UserThemeExists(t.Name);
                _themesRoot.Children.Add(new TransferNode
                {
                    Title = t.Name, Tag = t.Name,
                    Conflict = conflict,
                    Status = conflict ? "Overwrites existing" : "New",
                    IsChecked = !conflict,   // safe default: import new, leave overwrites off
                });
            }
            roots.Add(_themesRoot);
        }
        else _themesRoot = new TransferNode();

        if (bundle.Definitions.Count > 0)
        {
            _barsRoot = new TransferNode { Title = "Bar definitions", IsCategory = true, IsChecked = true };
            foreach (var d in bundle.Definitions)
            {
                bool conflict = PanelManager.GetDefinition(d.Id) != null;
                _barsRoot.Children.Add(new TransferNode
                {
                    Title = d.Name, Tag = d.Id,
                    Conflict = conflict,
                    Status = conflict ? "Overwrites existing" : "New",
                    IsChecked = !conflict,
                });
            }
            roots.Add(_barsRoot);
        }
        else _barsRoot = new TransferNode();

        Tree.ItemsSource = roots;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var themeNames = _themesRoot.Children.Where(n => n.IsChecked).Select(n => (string)n.Tag!).ToHashSet();
        var defIds     = _barsRoot.Children.Where(n => n.IsChecked).Select(n => (Guid)n.Tag!).ToHashSet();

        if (themeNames.Count == 0 && defIds.Count == 0) { DialogResult = false; Close(); return; }

        try
        {
            ConfigTransferService.Apply(_bundle, themeNames, defIds);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, "BetterBar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
