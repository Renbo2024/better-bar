using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Services.Search;
using BetterBarApp.Windows;
using Microsoft.Win32;

namespace BetterBarApp.Pages;

/// <summary>
/// In-window editor for a <see cref="StartButtonItem"/>: appearance, this button's own
/// search sources / recency, and its custom folders. Applies live — basics refresh the
/// panels; source toggles rebuild the button's engine; folder edits reload its folders.
/// </summary>
public partial class StartButtonPage : Page
{
    private readonly StartButtonItem?   _item;
    private readonly BarDefinition?     _def;
    private readonly StartSearchService? _search;
    private readonly ObservableCollection<SearchFolderRow> _locations = [];
    private SearchFolderRow? _selected;
    private StartMenuWindow? _previewMenu;
    private bool _loaded;

    public StartButtonPage()
    {
        var ctx = SettingsWindow.TakeContext() as ItemEditContext;
        _item = ctx?.Item as StartButtonItem;
        _def  = ctx?.Definition;
        InitializeComponent();
        PageScrolling.Attach(PageScroll, GetType().Name);

        if (_item != null)
        {
            _search = StartSearch.For(_item);

            DirectoryBox.Text   = _item.SourceDirectory;
            MarginBox.Value = _item.Margin;
            IconSizeBox.Value = _item.IconSize;
            IconMarginBox.Value = _item.IconMargin;
            TextMarginBox.Value = _item.TextMargin;
            TextSizeBox.Value = _item.TextSize;
            MaxTextWidthBox.Value = _item.MaxTextWidth;
            MinMenuHeightBox.Value = _item.MinMenuHeight;
            SearchMaxBox.Value = _item.SearchMaxResults;
            SearchFadeBox.Value = _item.SearchFadeMs;

            SearchQuickLaunchBox.IsChecked = _item.SearchQuickLaunch;
            SearchAppsBox.IsChecked        = _item.SearchApps;
            SearchSettingsBox.IsChecked    = _item.SearchSettings;
            SearchDocumentsBox.IsChecked   = _item.SearchDocuments;

            RecencyQuickLaunchBox.IsChecked = _item.FrecencyQuickLaunch;
            RecencyAppsBox.IsChecked        = _item.FrecencyApps;
            RecencySettingsBox.IsChecked    = _item.FrecencySettings;
            RecencyDocumentsBox.IsChecked   = _item.FrecencyDocuments;

            AliasesBox.Text = string.Join(Environment.NewLine,
                _item.SearchAliases.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                   .Select(kv => $"{kv.Key} = {kv.Value}"));

            foreach (var loc in _item.SearchLocations)
                _locations.Add(new SearchFolderRow
                {
                    Name = loc.Name, Path = loc.Path, Cascade = loc.Cascade,
                    Frecency = loc.Frecency, IncludeRegex = loc.IncludeRegex, ExcludeRegex = loc.ExcludeRegex,
                });
            LocationsList.ItemsSource = _locations;

            _search.SnapshotChanged += OnSearchSnapshot;
            RefreshLocationStatuses();
        }

        _loaded = true;
        Unloaded += OnUnloaded;
    }

    private void RefreshPanels()
    {
        SettingsService.Save();
        if (_def != null) PanelManager.RefreshDefinition(_def);
        // Keep an open preview in sync with config edits (icon size, margins, min height, folder…).
        if (_previewMenu is { IsLoaded: true }) _previewMenu.Refresh();
    }

    // ── Basics + search max/fade (no engine impact) ──
    private void Basics_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.SourceDirectory = DirectoryBox.Text.Trim();
        _item.Margin = MarginBox.Value;
        _item.IconSize = IconSizeBox.Value;
        _item.IconMargin = IconMarginBox.Value;
        _item.TextMargin = TextMarginBox.Value;
        _item.TextSize = TextSizeBox.Value;
        _item.MaxTextWidth = MaxTextWidthBox.Value;
        _item.MinMenuHeight = MinMenuHeightBox.Value;
        _item.SearchMaxResults = SearchMaxBox.Value;
        _item.SearchFadeMs = SearchFadeBox.Value;
        RefreshPanels();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select source folder" };
        if (dlg.ShowDialog() == true) { DirectoryBox.Text = dlg.FolderName; Basics_Changed(sender, e); }
    }

    // ── Which built-in sources are searched → rebuild the engine ──
    private void Sources_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.SearchQuickLaunch = SearchQuickLaunchBox.IsChecked == true;
        _item.SearchApps        = SearchAppsBox.IsChecked == true;
        _item.SearchSettings    = SearchSettingsBox.IsChecked == true;
        _item.SearchDocuments   = SearchDocumentsBox.IsChecked == true;
        SettingsService.Save();
        _search?.Reload();
    }

    // ── Per-source recency (live predicate, no reload) ──
    private void Recency_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        _item.FrecencyQuickLaunch = RecencyQuickLaunchBox.IsChecked == true;
        _item.FrecencyApps        = RecencyAppsBox.IsChecked == true;
        _item.FrecencySettings    = RecencySettingsBox.IsChecked == true;
        _item.FrecencyDocuments   = RecencyDocumentsBox.IsChecked == true;
        SettingsService.Save();
    }

    // ── Search aliases ── (parsed from "alias = expansion" lines; applied live, no engine reload)
    private void Aliases_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || _item == null) return;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in AliasesBox.Text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var alias     = line[..eq].Trim();
            var expansion = line[(eq + 1)..].Trim();
            if (alias.Length == 0 || expansion.Length == 0) continue;
            dict[alias] = expansion;   // duplicate alias → last line wins
        }
        _item.SearchAliases = dict;
        SettingsService.Save();   // engine reads SearchAliases live on the next keystroke
    }

    // ── Custom folders ──
    private void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Add a folder to search" };
        if (dlg.ShowDialog() != true) return;
        var path = PathUtil.ToUnc(dlg.FolderName);
        if (_locations.Any(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        _locations.Add(new SearchFolderRow
        {
            Path = path,
            Name = Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
            Cascade = true,
        });
        PersistFolders(reload: true);
        RefreshLocationStatuses();
    }

    private void RemoveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _locations.Remove(_selected);
        _selected = null;
        UpdateFolderButtons();
        PersistFolders(reload: true);
    }

    private void EditLocation_Click(object sender, RoutedEventArgs e) => Configure(_selected);

    private void Location_Click(object sender, MouseButtonEventArgs e)
    {
        _selected = (sender as FrameworkElement)?.DataContext as SearchFolderRow;
        UpdateFolderButtons();
    }

    private void Location_DoubleClick(object sender, MouseButtonEventArgs e) =>
        Configure((sender as FrameworkElement)?.DataContext as SearchFolderRow);

    private void Configure(SearchFolderRow? row)
    {
        if (row == null) return;
        var dlg = new FolderConfigWindow(row, Window.GetWindow(this)!);
        if (dlg.ShowDialog() == true) { PersistFolders(reload: true); RefreshLocationStatuses(); }
    }

    // Inline recency toggle: persist so the live predicate sees it, but no re-crawl.
    private void FolderRecency_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        PersistFolders(reload: false);
    }

    private void UpdateFolderButtons()
    {
        RemoveLocationBtn.IsEnabled = _selected != null;
        EditLocationBtn.IsEnabled   = _selected != null;
    }

    // Rebuild the item's folder list from the working copy; optionally re-crawl them.
    private void PersistFolders(bool reload)
    {
        if (_item == null) return;
        _item.SearchLocations = _locations
            .Select(l => new SearchLocation
            {
                Name = l.Name, Path = l.Path, Cascade = l.Cascade,
                Frecency = l.Frecency, IncludeRegex = l.IncludeRegex, ExcludeRegex = l.ExcludeRegex,
            })
            .ToList();
        SettingsService.Save();
        if (reload) _search?.ReloadLocations();
    }

    private void OnSearchSnapshot() => Dispatcher.BeginInvoke(RefreshLocationStatuses);

    private void RefreshLocationStatuses()
    {
        if (_search == null) return;
        foreach (var row in _locations)
        {
            var count = _search.CountFor(StartSearchService.LocationSourceId(row.Path));
            row.Status = count.HasValue
                ? (count.Value > 0 ? $"{count.Value} files" : "0 files (check access)")
                : (Directory.Exists(PathUtil.ToUnc(row.Path)) ? "Indexing…" : "Not accessible");
        }
    }

    // ── Preview (toggles the preview menu on/off) ──
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null) return;

        // Already open → toggle it closed.
        if (_previewMenu is { IsLoaded: true })
        {
            _previewMenu.Pinned = false;
            _previewMenu.Close();
            _previewMenu = null;
            return;
        }

        var owner = Window.GetWindow(this)!;
        _previewMenu = new StartMenuWindow(_item, owner) { Pinned = true };
        _previewMenu.Closed += (_, _) => _previewMenu = null;   // user closed it some other way
        _previewMenu.Left = Math.Max(0, owner.Left - _previewMenu.Width - 12);
        _previewMenu.Top  = owner.Top;
        _previewMenu.Show();
        owner.Activate();
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        SettingsWindow.Navigate(typeof(BarEditorPage), _def);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_search != null) _search.SnapshotChanged -= OnSearchSnapshot;
        if (_previewMenu is { IsLoaded: true }) { _previewMenu.Pinned = false; _previewMenu.Close(); }
        _previewMenu = null;
    }
}
