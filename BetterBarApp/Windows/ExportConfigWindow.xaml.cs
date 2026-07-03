using System.Collections.ObjectModel;
using System.Windows;
using BetterBarApp.Services;

namespace BetterBarApp.Windows;

public partial class ExportConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly TransferNode _themesRoot;
    private readonly TransferNode _barsRoot;

    public ExportConfigWindow()
    {
        InitializeComponent();

        _themesRoot = new TransferNode { Title = "Themes", IsCategory = true };
        foreach (var t in ThemeService.Available)
            _themesRoot.Children.Add(new TransferNode { Title = t.Name + (t.BuiltIn ? "  (built-in)" : ""), Tag = t.Name });

        _barsRoot = new TransferNode { Title = "Bar definitions", IsCategory = true };
        foreach (var d in PanelManager.Definitions)
            _barsRoot.Children.Add(new TransferNode { Title = d.Name, Tag = d.Id });

        Tree.ItemsSource = new ObservableCollection<TransferNode> { _themesRoot, _barsRoot };
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var themeNames = _themesRoot.Children.Where(n => n.IsChecked).Select(n => (string)n.Tag!).ToList();
        var defIds     = _barsRoot.Children.Where(n => n.IsChecked).Select(n => (Guid)n.Tag!).ToList();

        if (themeNames.Count == 0 && defIds.Count == 0)
        {
            MessageBox.Show(this, "Select at least one theme or bar definition to export.", "BetterBar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = ConfigTransferService.FileFilter,
            DefaultExt = ConfigTransferService.Extension,
            FileName = "BetterBar" + ConfigTransferService.Extension,
            Title = "Export configuration",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            ConfigTransferService.Write(ConfigTransferService.BuildBundle(themeNames, defIds), dlg.FileName);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "BetterBar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
