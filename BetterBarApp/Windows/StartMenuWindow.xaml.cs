using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Services.Search;
using ManagedShell.ShellFolders;
using ManagedShell.ShellFolders.Enums;

namespace BetterBarApp.Windows;

public partial class StartMenuWindow : Window
{
    private readonly StartButtonItem _item;

    /// <summary>
    /// When true, the window does NOT auto-close on Deactivated. Used by the
    /// settings dialog so the menu stays visible while the user tweaks values.
    /// </summary>
    public bool Pinned { get; set; }

    // Suppresses Deactivated→Close while a nested modal (shell menu or rename dialog)
    // is open and our window has temporarily lost focus.
    private bool _suppressClose;

    // ── Dependency properties bound by the item template (live) ──────────────────

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(StartMenuWindow),
            new PropertyMetadata(24.0));
    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(nameof(IconMargin), typeof(Thickness), typeof(StartMenuWindow),
            new PropertyMetadata(new Thickness(4)));
    public static readonly DependencyProperty TextMarginProperty =
        DependencyProperty.Register(nameof(TextMargin), typeof(Thickness), typeof(StartMenuWindow),
            new PropertyMetadata(new Thickness(4)));
    public static readonly DependencyProperty TextSizeProperty =
        DependencyProperty.Register(nameof(TextSize), typeof(double), typeof(StartMenuWindow),
            new PropertyMetadata(13.0));
    public static readonly DependencyProperty MaxTextWidthProperty =
        DependencyProperty.Register(nameof(MaxTextWidth), typeof(double), typeof(StartMenuWindow),
            new PropertyMetadata(240.0));

    public double    IconSize    { get => (double)GetValue(IconSizeProperty);    set => SetValue(IconSizeProperty, value); }
    public Thickness IconMargin  { get => (Thickness)GetValue(IconMarginProperty); set => SetValue(IconMarginProperty, value); }
    public Thickness TextMargin  { get => (Thickness)GetValue(TextMarginProperty); set => SetValue(TextMarginProperty, value); }
    public double    TextSize    { get => (double)GetValue(TextSizeProperty);    set => SetValue(TextSizeProperty, value); }
    public double    MaxTextWidth{ get => (double)GetValue(MaxTextWidthProperty); set => SetValue(MaxTextWidthProperty, value); }

    // This button's private search engine (created/kept warm by the registry).
    private readonly StartSearchService _search;

    public StartMenuWindow(StartButtonItem item, Window owner)
    {
        _item   = item;
        _search = StartSearch.For(item);
        Owner   = owner;
        InitializeComponent();
        // Floor the height here so EVERY path (live menu and the settings preview) keeps room for
        // search results, even with an empty icon list. The live caller may further clamp it to the
        // screen edge via MaxHeight.
        MinHeight = Math.Max(0, _item.MinMenuHeight);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Caps auto-growth at the monitor this window opened on, unless the caller
    /// already set <see cref="Window.MaxHeight"/> (the start button supplies a
    /// direction-aware cap). Done here — before the SizeToContent measure pass —
    /// so the window never momentarily overshoots the screen.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (double.IsPositiveInfinity(MaxHeight))
        {
            var src = System.Windows.PresentationSource.FromVisual(this);
            double dx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(Left * dx), (int)(Top * dy)));
            MaxHeight = screen.WorkingArea.Height / dy;
        }

        // Keep the bottom edge pinned during SizeToContent resizes (see WndProc),
        // so the bar grows upward atomically without bobbing the search box.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }
    private const int  WM_WINDOWPOSCHANGING = 0x0046;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Bottom-anchored (bottom panel): when SizeToContent changes the height, set
        // the top in the SAME operation so the bottom edge stays put — one atomic
        // move+resize, no two-step bob.
        if (msg == WM_WINDOWPOSCHANGING && _useBottomAnchor)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.flags & SWP_NOSIZE) == 0)
            {
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (dpi <= 0) dpi = 1.0;
                wp.y = (int)Math.Round(_bottomAnchor * dpi) - wp.cy;
                wp.flags &= ~SWP_NOMOVE;
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    // When set, the window keeps its BOTTOM edge fixed and grows upward — so the
    // search box (docked at the bottom) doesn't bob as the result count changes.
    private double _bottomAnchor;
    private bool   _useBottomAnchor;

    /// <summary>
    /// For a top-placed panel the menu drops downward, so the search box moves to the
    /// TOP (adjacent to the bar) and results grow down below it — the reverse of the
    /// bottom-panel layout. The window's top stays pinned to the bar (SizeToContent
    /// grows it downward), so the search box doesn't bob.
    /// </summary>
    public void SetSearchAtTop()
    {
        DockPanel.SetDock(SearchBox, Dock.Top);
        SearchBox.Margin = new Thickness(0, 0, 0, 8);
    }

    public void SetBottomAnchor(double bottomLogical)
    {
        _bottomAnchor    = bottomLogical;
        _useBottomAnchor = true;
        // Initial placement; subsequent SizeToContent resizes are pinned atomically
        // by WndProc (WM_WINDOWPOSCHANGING).
        Top = _bottomAnchor - ActualHeight;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Refresh();
        SearchBox.Focus();
        SearchBox.SelectAll();

        // ListBox / Selector class handlers may mark mouse events as Handled before
        // a XAML-attribute handler would see them. AddHandler with handledEventsToo
        // lets us observe the event regardless. Hook DOWN, UP, and the bubble forms
        // so whichever phase fires first, we catch it.
        IconList.AddHandler(PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnAnyRightClick), handledEventsToo: true);
        IconList.AddHandler(PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnAnyRightClick),   handledEventsToo: true);
        IconList.AddHandler(MouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnAnyRightClick),   handledEventsToo: true);
        IconList.AddHandler(MouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnAnyRightClick),   handledEventsToo: true);

        // ── Type-to-find search wiring (always on) ──
        // Front layer = the one currently shown; back layer is rendered then faded in.
        _frontScroller = ResultsScrollerA; _frontList = ResultsListA;
        _backScroller  = ResultsScrollerB; _backList  = ResultsListB;

        _search.EnsureBuilding();
        _search.SnapshotChanged += OnIndexReady;
        _searchDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _searchDebounce.Tick += (_, _) => { _searchDebounce!.Stop(); RunSearch(); };
        SearchBox.TextChanged    += (_, _) => { _searchDebounce!.Stop(); _searchDebounce!.Start(); };
        SearchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        SearchBox.ContextMenu     = BuildSearchBoxMenu();
    }

    // Search box right-click: standard editing commands + a manual index reload (spec §7.5).
    private System.Windows.Controls.ContextMenu BuildSearchBoxMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Cut",    Command = ApplicationCommands.Cut });
        menu.Items.Add(new MenuItem { Header = "Copy",   Command = ApplicationCommands.Copy });
        menu.Items.Add(new MenuItem { Header = "Paste",  Command = ApplicationCommands.Paste });
        menu.Items.Add(new Separator());
        var reload = new MenuItem { Header = "Reload search index" };
        reload.Click += (_, _) => _search.Reload();
        menu.Items.Add(reload);
        return menu;
    }

    private bool _menuShownForCurrentClick;

    private void OnAnyRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_menuShownForCurrentClick) return;

        var node = e.OriginalSource as DependencyObject;
        while (node != null && node is not ListBoxItem)
            node = VisualTreeHelper.GetParent(node);
        if (node is not ListBoxItem lbi || lbi.DataContext is not IconEntry entry)
            return;

        _menuShownForCurrentClick = true;
        e.Handled = true;
        try     { ShowShellMenuFor(entry); }
        finally { _menuShownForCurrentClick = false; }
    }

    /// <summary>
    /// Re-reads the item's settings, updates dependency properties (so the template
    /// re-binds), and rebuilds the icon list. Safe to call while the window is open.
    /// </summary>
    public void Refresh()
    {
        IconSize     = _item.IconSize;
        IconMargin   = new Thickness(_item.IconMargin);
        TextMargin   = new Thickness(_item.TextMargin);
        TextSize     = _item.TextSize;
        MaxTextWidth = _item.MaxTextWidth;
        MinHeight    = Math.Max(0, _item.MinMenuHeight);   // honour the configured floor live
        BuildIconList();
    }

    // ── Icon list ────────────────────────────────────────────────────────────────

    public class IconEntry : INotifyPropertyChanged
    {
        public string Path { get; init; } = string.Empty;

        private BitmapSource? _icon;
        public BitmapSource? Icon
        {
            get => _icon;
            set { _icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); }
        }

        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; PropertyChanged?.Invoke(this, new(nameof(DisplayName))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly List<IconEntry> _entries = [];

    private void BuildIconList()
    {
        _entries.Clear();
        IconList.ItemsSource = null;

        if (string.IsNullOrWhiteSpace(_item.SourceDirectory) ||
            !Directory.Exists(_item.SourceDirectory))
        {
            return;
        }

        MigrateLegacyMetadata();

        // Per-file name/hide lives in companion .bbr files (see IconSidecar). Skip
        // the .bbr files themselves and anything marked hidden.
        var allFiles  = Directory.GetFiles(_item.SourceDirectory)
            .Where(p => !IconSidecar.IsSidecar(p) && !IconSidecar.IsHidden(p))
            .ToList();
        var inOrder   = _item.IconOrder.Where(allFiles.Contains).ToList();
        var remainder = allFiles.Except(inOrder).OrderBy(System.IO.Path.GetFileName).ToList();

        foreach (var path in inOrder.Concat(remainder))
        {
            var entry = new IconEntry
            {
                Path        = path,
                DisplayName = ResolveDisplayName(path),
            };
            _entries.Add(entry);
            LoadIconSta(entry, path);
        }

        IconList.ItemsSource = _entries;
    }

    private void PersistOrder()
    {
        _item.IconOrder = _entries.Select(e => e.Path).ToList();
        SettingsService.Save();
    }

    private static string ResolveDisplayName(string path) =>
        IconSidecar.GetName(path) ?? System.IO.Path.GetFileNameWithoutExtension(path);

    // One-time move of pre-.bbr per-item overrides into companion files, so existing
    // renames/hides aren't lost. Clears the legacy fields and persists once.
    private void MigrateLegacyMetadata()
    {
        if (_item.NameOverrides.Count == 0 && _item.HiddenFiles.Count == 0) return;

        foreach (var (path, name) in _item.NameOverrides)
            if (!string.IsNullOrEmpty(name)) IconSidecar.SetName(path, name);
        foreach (var path in _item.HiddenFiles)
            IconSidecar.SetHide(path, true);

        _item.NameOverrides.Clear();
        _item.HiddenFiles.Clear();
        SettingsService.Save();
    }

    private void LoadIconSta(IconEntry entry, string path)
    {
        var dispatcher = Dispatcher;
        var sizePx     = Math.Max(16, _item.IconSize * 2); // request crisp icons for HiDPI
        var thread     = new Thread(() =>
        {
            BitmapSource? icon = null;
            try { icon = ShellIconService.GetIcon(path, sizePx); } catch { }
            if (icon != null)
                dispatcher.BeginInvoke(() => entry.Icon = icon);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    // ── Window dismissal ─────────────────────────────────────────────────────────

    private bool _closing;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;   // so a deactivation during close doesn't re-enter Close()
        _search.SnapshotChanged -= OnIndexReady;
        _iconCts?.Cancel();
        base.OnClosing(e);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Launching an item (or any close) steals foreground, firing Deactivated
        // mid-close — calling Close() again throws "while a Window is closing".
        if (Pinned || _suppressClose || _closing) return;
        Close();
    }

    // ── Hooks ────────────────────────────────────────────────────────────────────

    private void IconList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }


    // ── Type-to-find search ──────────────────────────────────────────────────────
    private System.Windows.Threading.DispatcherTimer? _searchDebounce;
    private List<SearchRow> _resultRows = [];
    private int _highlightIndex = -1;   // index into _resultRows of the highlighted item
    // Cancels in-flight icon loads when the result set changes (spec §9.4 / §13.4).
    private CancellationTokenSource? _iconCts;
    // Cancels an in-flight path enumeration when the query changes.
    private CancellationTokenSource? _pathCts;
    private bool   _pathMode;          // showing a filesystem listing (vs. the index)
    private string _pathDir = "";      // the folder currently being listed in path mode

    // Double-buffered result layers: _front is what's on screen; a new result set is
    // built into _back (still invisible), then we cross-fade so no empty frame shows.
    private System.Windows.Controls.ScrollViewer _frontScroller = null!, _backScroller = null!;
    private System.Windows.Controls.ItemsControl _frontList = null!, _backList = null!;
    private bool _showingResults;

    // Index finished building (raised off-thread): re-run if we're showing "loading".
    private void OnIndexReady() => Dispatcher.BeginInvoke(() =>
    {
        if (LoadingText.Visibility == Visibility.Visible && SearchBox.Text.Trim().Length > 0)
            RunSearch();
    });

    private void RunSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) { ShowIconList(); return; }

        // Freeze the window size for the whole search session so it does NOT resize
        // (and therefore doesn't repaint/flicker the search box) as results change.
        EnterSearchSizing();

        int max = Math.Max(1, _item.SearchMaxResults);

        // Path mode: if the text reads as a drive (C:\) or UNC (\\srv\) path, list the
        // files/folders there instead of the normal sources.
        if (PathSearch.TryParse(SearchBox.Text, out var dir, out var filter))
        {
            RunPathSearch(dir, filter, max);
            return;
        }
        _pathMode = false;
        _pathCts?.Cancel();   // leaving path mode → abandon any in-flight enumeration

        // The engine only built the sources this button enabled, so every source in
        // its snapshot is in scope — no per-query source filter needed here.
        var sections = _search.Search(SearchBox.Text, Math.Min(5, max), max);

        if (sections == null)   // index not ready yet → cold-start state (spec §10.1)
        {
            IconList.Visibility         = Visibility.Collapsed;
            _frontScroller.Visibility   = Visibility.Collapsed;
            _backScroller.Visibility    = Visibility.Collapsed;
            LoadingText.Visibility      = Visibility.Visible;
            _showingResults             = false;
            return;
        }

        IconList.Visibility    = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Collapsed;
        _showingResults        = true;
        BuildResultRows(sections);
    }

    // Path mode: enumerate the typed folder off-thread (UNC shares can be slow), then
    // show just those entries — no other sources. Cancels if the query changes.
    private void RunPathSearch(string dir, string filter, int max)
    {
        IconList.Visibility    = Visibility.Collapsed;
        LoadingText.Visibility = Visibility.Collapsed;
        _showingResults        = true;
        _pathMode              = true;
        _pathDir               = dir;

        _pathCts?.Cancel();
        _pathCts = new CancellationTokenSource();
        var ct = _pathCts.Token;

        Task.Run(() =>
        {
            var entries = PathSearch.Enumerate(dir, filter, max, ct);
            if (ct.IsCancellationRequested) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (ct.IsCancellationRequested) return;
                // No auto-selection in path mode — the user selects by arrowing.
                BuildResultRows(new[] { new SearchSection("path", dir, entries) }, autoSelect: false);
            });
        }, ct);
    }

    private void ShowIconList()
    {
        _iconCts?.Cancel();
        _pathCts?.Cancel();
        ExitSearchSizing();
        _showingResults = false;
        _pathMode       = false;

        // Reset both layers to a clean, collapsed baseline.
        foreach (var sc in new[] { ResultsScrollerA, ResultsScrollerB })
        {
            sc.BeginAnimation(OpacityProperty, null);
            sc.Opacity          = 1;
            sc.Visibility       = Visibility.Collapsed;
            sc.IsHitTestVisible = true;
        }
        ResultsListA.ItemsSource = null;
        ResultsListB.ItemsSource = null;
        _frontScroller = ResultsScrollerA; _frontList = ResultsListA;
        _backScroller  = ResultsScrollerB; _backList  = ResultsListB;

        LoadingText.Visibility = Visibility.Collapsed;
        IconList.Visibility    = Visibility.Visible;
        _resultRows.Clear();
        _highlightIndex = -1;
    }

    // While searching, the window is frozen at the height it already had (the icon
    // list's natural height) so it neither jumps when search starts nor resizes per
    // keystroke — the search box stays rock-stable and results scroll within it.
    private bool _searchSizing;

    private void EnterSearchSizing()
    {
        if (_searchSizing) return;
        _searchSizing = true;

        double frozen = ActualHeight;   // current height — no jump
        SizeToContent = SizeToContent.Manual;
        if (frozen > 0) Height = frozen;
    }

    private void ExitSearchSizing()
    {
        if (!_searchSizing) return;
        _searchSizing = false;
        SizeToContent = SizeToContent.Height;   // back to growing with the icon list
    }

    private void BuildResultRows(IReadOnlyList<SearchSection> sections, bool autoSelect = true)
    {
        // New result set → abandon the previous set's in-flight icon loads.
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;

        // Build the new rows and render them into the BACK (invisible) layer; the
        // cross-fade then reveals it without the on-screen list ever going empty.
        var rows = new List<SearchRow>();
        foreach (var section in sections)
        {
            rows.Add(new SearchRow { IsHeader = true, Text = section.DisplayName });
            foreach (var entry in section.Items)
            {
                var row = new SearchRow { Entry = entry, Text = entry.DisplayName, Glyph = EntryGlyph.For(entry.Kind) };
                rows.Add(row);
                LoadResultIconSta(row, ct);
            }
        }
        if (rows.Count == 0)
            rows.Add(new SearchRow { IsHeader = true, Text = "No results" });

        _resultRows = rows;

        // Prepare the back layer: VISIBLE (so it actually lays out — a Collapsed element
        // doesn't measure/arrange) but fully transparent and inert. UpdateLayout realizes
        // the item containers now, so the fade reveals a complete list, not an empty box.
        _backScroller.IsHitTestVisible = false;
        _backScroller.BeginAnimation(OpacityProperty, null);
        _backScroller.Opacity    = 0;
        _backScroller.Visibility = Visibility.Visible;
        _backList.ItemsSource    = rows;
        _backScroller.UpdateLayout();
        _backScroller.ScrollToTop();

        _highlightIndex = -1;
        // Auto-select the first result for a typed search (spec §9.3). In path-browsing
        // mode there is NO default selection — the user selects by arrowing.
        if (autoSelect) SetHighlight(NextItemRow(-1, +1));
        CrossFadeToBack();
    }

    // Cross-fade the freshly-rendered (transparent) back layer over the current front
    // layer, then swap roles. With fade = 0 it's an instant swap. Robust against rapid
    // retyping: a layer being faded out is only collapsed once it's *still* the back.
    private void CrossFadeToBack()
    {
        var newFront = _backScroller;   // populated + laid out at opacity 0 by the caller
        var oldFront = _frontScroller;

        int ms = _item.SearchFadeMs;
        if (ms <= 0)
        {
            newFront.BeginAnimation(OpacityProperty, null);
            oldFront.BeginAnimation(OpacityProperty, null);
            newFront.Opacity    = 1;
            oldFront.Opacity    = 0;
            oldFront.Visibility = Visibility.Collapsed;
        }
        else
        {
            var dur     = new Duration(TimeSpan.FromMilliseconds(ms));
            var fadeIn  = new System.Windows.Media.Animation.DoubleAnimation(0, 1, dur);
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(oldFront.Opacity, 0, dur);
            // Collapse the old layer once faded out — unless a newer retype has made it
            // the front again (in which case it must stay visible).
            fadeOut.Completed += (_, _) =>
            {
                if (oldFront == _backScroller) oldFront.Visibility = Visibility.Collapsed;
            };
            newFront.BeginAnimation(OpacityProperty, fadeIn);
            oldFront.BeginAnimation(OpacityProperty, fadeOut);
        }

        newFront.IsHitTestVisible = true;
        oldFront.IsHitTestVisible = false;

        // Swap roles: the layer we revealed is now the front.
        _frontScroller = newFront; _backScroller = oldFront;
        (_frontList, _backList) = (_backList, _frontList);
    }

    // Next/previous row that is an actual item (skips section headers); -1 if none.
    private int NextItemRow(int from, int dir)
    {
        for (int i = from + dir; i >= 0 && i < _resultRows.Count; i += dir)
            if (_resultRows[i].Entry != null) return i;
        return -1;
    }

    private void SetHighlight(int index, bool scrollIntoView = false)
    {
        if (index == _highlightIndex) return;
        if (_highlightIndex >= 0 && _highlightIndex < _resultRows.Count)
            _resultRows[_highlightIndex].IsHighlighted = false;
        _highlightIndex = index;
        if (_highlightIndex >= 0 && _highlightIndex < _resultRows.Count)
            _resultRows[_highlightIndex].IsHighlighted = true;
        if (scrollIntoView) ScrollHighlightIntoView();
    }

    // Keep the keyboard-highlighted row visible: scroll the (visible) results layer so the selected
    // item comes into view when arrowing past the top or bottom edge. The results list uses a non-
    // virtualizing StackPanel, so every item container is realized; BringIntoView on it bubbles a
    // RequestBringIntoView that the wrapping ScrollViewer honors (minimal scroll, both directions).
    private void ScrollHighlightIntoView()
    {
        if (_highlightIndex < 0 || _highlightIndex >= _resultRows.Count) return;
        if (_frontList?.ItemContainerGenerator.ContainerFromIndex(_highlightIndex) is FrameworkElement fe)
            fe.BringIntoView();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool showingResults = _showingResults;
        switch (e.Key)
        {
            case Key.Down when showingResults:
                { int n = NextItemRow(_highlightIndex, +1); if (n >= 0) SetHighlight(n, scrollIntoView: true); e.Handled = true; break; }
            case Key.Up when showingResults:
                { int n = NextItemRow(_highlightIndex, -1); if (n >= 0) SetHighlight(n, scrollIntoView: true); e.Handled = true; break; }
            case Key.Enter:
                if (!showingResults) break;
                var hl = (_highlightIndex >= 0 && _highlightIndex < _resultRows.Count)
                    ? _resultRows[_highlightIndex].Entry : null;
                if (_pathMode)
                {
                    if (hl is { Kind: EntryKind.Folder })
                        DrillIntoFolder(hl);              // 1st Enter on a folder: descend
                    else if (hl != null)
                        LaunchSearchEntry(hl);            // a file: open it
                    else
                        OpenInExplorer(_pathDir);         // nothing selected: open the folder
                    e.Handled = true;
                }
                else if (hl != null)
                {
                    LaunchSearchEntry(hl);
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0) { _searchDebounce?.Stop(); SearchBox.Clear(); ShowIconList(); }
                else if (!Pinned) Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SearchRow { Entry: { } entry }) return;
        if (_pathMode && entry.Kind == EntryKind.Folder) DrillIntoFolder(entry);  // browse into it
        else LaunchSearchEntry(entry);
    }

    private void ResultItem_Hover(object sender, MouseEventArgs e)
    {
        // Mouse takes over the highlight (spec §11.3); guard avoids redundant churn.
        if ((sender as FrameworkElement)?.DataContext is SearchRow { IsHeader: false } row && !row.IsHighlighted)
            SetHighlight(_resultRows.IndexOf(row));
    }

    private void LaunchSearchEntry(SearchEntry entry)
    {
        try { Launcher.Launch(entry); _search.RecordLaunch(entry, SearchBox.Text); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not launch:\n" + ex.Message,
                "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Pinned) Close();
    }

    // Path mode: descend into the highlighted folder by appending it to the search box
    // and re-enumerating in place (no menu close, stays in path mode).
    private void DrillIntoFolder(SearchEntry folder)
    {
        var path = folder.LaunchTarget;
        if (!path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
        SearchBox.Text = path;
        SearchBox.CaretIndex = path.Length;
        _searchDebounce?.Stop();   // re-run immediately rather than after the debounce
        RunSearch();
    }

    // Path mode: open the current folder in Explorer (Enter with no item selected).
    private void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open:\n" + ex.Message,
                "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Pinned) Close();
    }

    // Loads the same icon Explorer would show. LaunchTarget is a file path or a
    // shell parsing name (AppsFolder / Control Panel) — both resolve via the shell
    // image factory. ms-settings: URIs return null and keep the category glyph.
    private void LoadResultIconSta(SearchRow row, CancellationToken ct)
    {
        var entry = row.Entry;
        if (entry == null) return;

        var target     = entry.LaunchTarget;
        var viaPidl    = entry.LaunchVia == LaunchKind.ShellItem;
        var dispatcher = Dispatcher;
        var thread = new Thread(() =>
        {
            if (ct.IsCancellationRequested) return;
            BitmapSource? icon = null;
            try
            {
                icon = viaPidl
                    ? ShellIconService.GetIconFromIdList(Convert.FromBase64String(target), 32)
                    : ShellIconService.GetIcon(target, 32);
            }
            catch { }
            if (icon == null || ct.IsCancellationRequested) return;
            dispatcher.BeginInvoke(() => { if (!ct.IsCancellationRequested) row.Icon = icon; });
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public sealed class SearchRow : INotifyPropertyChanged
    {
        public bool        IsHeader { get; init; }
        public string      Text     { get; init; } = "";
        public string      Glyph    { get; init; } = "";
        public SearchEntry? Entry   { get; init; }

        private bool _hl;
        public bool IsHighlighted { get => _hl; set { _hl = value; Notify(nameof(IsHighlighted)); } }

        private BitmapSource? _icon;
        public BitmapSource? Icon { get => _icon; set { _icon = value; Notify(nameof(Icon)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) => PropertyChanged?.Invoke(this, new(n));
    }

    // ── Shell context menu with BetterBar Rename appended ───────────────────────

    private const string RenameLabel = "Rename";
    private const uint   RenameUid   = 0x9001;
    private const string HideLabel   = "Hide";
    private const uint   HideUid     = 0x9002;

    private void ShowShellMenuFor(IconEntry entry)
    {
        var post = new ShellMenuCommandBuilder();
        post.AddSeparator();
        post.AddCommand(new ShellMenuCommand
        {
            Label = RenameLabel,
            UID   = RenameUid,
            Flags = MFT.BYCOMMAND,
        });
        post.AddCommand(new ShellMenuCommand
        {
            Label = HideLabel,
            UID   = HideUid,
            Flags = MFT.BYCOMMAND,
        });

        // Captured by the callback; if Rename is selected we defer the dialog
        // to AFTER the shell menu's ctor returns, because opening a modal dialog
        // inside the shell menu's own callstack just no-ops silently.
        IconEntry? pendingRename = null;
        IconEntry? pendingHide   = null;

        _suppressClose  = true;
        bool wasTopmost = Topmost;
        Topmost         = false;
        try
        {
            var folder = new ShellFolder(_item.SourceDirectory, IntPtr.Zero, false);
            var file   = new ShellFile(folder, entry.Path);

            _ = new ShellItemContextMenu(
                new ShellItem[] { file },
                folder,
                IntPtr.Zero,
                (command, items, _) =>
                {
                    // Two routes that should ALL go to our custom dialog:
                    //   * our custom "Rename" added via postBuilder — identified by
                    //     the UID converted to decimal string (RetroBar's convention).
                    //   * the shell's native "rename" verb, in case canRename:false
                    //     doesn't fully suppress it. Intercepting it as "handled"
                    //     prevents the OS from renaming the actual file on disk.
                    if (command == RenameUid.ToString() ||
                        string.Equals(command, "rename", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingRename = entry;
                        return true;   // handled — shell will not run its rename verb
                    }
                    if (command == HideUid.ToString())
                    {
                        pendingHide = entry;
                        return true;   // handled
                    }
                    return false;       // any other command: let the shell process
                },
                isInteractive: true,
                canRename:     false,
                preBuilder:    new ShellMenuCommandBuilder(),
                postBuilder:   post);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Shell menu failed:\n" + ex,
                "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Topmost        = wasTopmost;
            _suppressClose = false;
            Activate();
        }

        // Now safe to show our own modal dialog — the shell menu is fully gone.
        if (pendingRename != null)
            ShowRenameDialog(pendingRename);
        else if (pendingHide != null)
            HideEntry(pendingHide);
    }

    private void HideEntry(IconEntry entry)
    {
        IconSidecar.SetHide(entry.Path, true);
        BuildIconList();    // refresh to drop the hidden row
    }

    private void ShowRenameDialog(IconEntry entry)
    {
        _suppressClose = true;
        try
        {
            var dlg = new StartMenuRenameDialog(entry.DisplayName, this);
            if (dlg.ShowDialog() == true && dlg.ResultName is { } newName)
            {
                IconSidecar.SetName(entry.Path, newName);   // blank clears the override
                entry.DisplayName = ResolveDisplayName(entry.Path);
            }
        }
        finally
        {
            _suppressClose = false;
            Activate();
        }
    }

    // ── Drag-and-drop ────────────────────────────────────────────────────────────
    // - Internal drag = a row dragged within the list → reorder (custom format).
    // - External drag = a file dropped from outside (DataFormats.FileDrop) →
    //   create a shortcut for each file in the configured source directory.

    private const string InternalDragFormat = "BetterBar.StartMenu.IconEntry";

    private Point     _dragStart;
    private IconEntry? _dragSource;

    private void IconList_LeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart  = e.GetPosition(IconList);
        _dragSource = FindEntryAt(e.OriginalSource as DependencyObject);
    }

    private void IconList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;
        var pos   = e.GetPosition(IconList);
        var delta = pos - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var src = _dragSource;
        _dragSource = null;
        var data = new DataObject(InternalDragFormat, src.Path);
        DragDrop.DoDragDrop(IconList, data, DragDropEffects.Move);
    }

    // A plain left click (press + release on the same row without crossing the
    // drag threshold) launches the item. _dragSource is non-null only when no
    // drag started — MouseMove clears it the moment a reorder drag begins.
    private void IconList_LeftUp(object sender, MouseButtonEventArgs e)
    {
        var entry = _dragSource;
        _dragSource = null;
        if (entry == null) return;
        if (FindEntryAt(e.OriginalSource as DependencyObject) != entry) return;
        LaunchEntry(entry);
    }

    private void LaunchEntry(IconEntry entry)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName         = entry.Path,
                UseShellExecute  = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(entry.Path) ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not launch:\n" + ex.Message,
                "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Real Start-menu behaviour: dismiss after launching (unless pinned open
        // by the settings preview).
        if (!Pinned) Close();
    }

    private void IconList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat))
        {
            // Live reorder during drag.
            e.Effects = DragDropEffects.Move;
            var draggedPath = e.Data.GetData(InternalDragFormat) as string;
            if (draggedPath == null) { e.Handled = true; return; }
            int targetIdx = IndexAt(e.GetPosition(IconList));
            int currentIdx = _entries.FindIndex(en => en.Path == draggedPath);
            if (targetIdx >= 0 && currentIdx >= 0 && targetIdx != currentIdx)
            {
                var moving = _entries[currentIdx];
                _entries.RemoveAt(currentIdx);
                _entries.Insert(Math.Clamp(targetIdx, 0, _entries.Count), moving);
                // The List<T> doesn't notify; refresh the bound view.
                IconList.Items.Refresh();
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void IconList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat))
        {
            // Reorder already applied live in DragOver — persist final state.
            PersistOrder();
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // External file(s) → create shortcut(s) in the source directory.
            if (string.IsNullOrWhiteSpace(_item.SourceDirectory) ||
                !Directory.Exists(_item.SourceDirectory))
                return;

            var paths    = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var failures = ShortcutService.AddToDirectory(paths, _item.SourceDirectory);
            BuildIconList();    // refresh to show the new shortcuts
            PersistOrder();

            if (failures.Count > 0)
                MessageBox.Show(this,
                    "Could not add:\n" + string.Join("\n", failures),
                    "BetterBar", MessageBoxButton.OK, MessageBoxImage.Warning);

            // If we were pinned open during a hover-launched drag, release it
            // now so the menu can dismiss normally once focus moves away.
            Pinned = false;
        }
        e.Handled = true;
    }

    private IconEntry? FindEntryAt(DependencyObject? src)
    {
        while (src != null && src is not ListBoxItem)
            src = VisualTreeHelper.GetParent(src);
        return src is ListBoxItem lbi ? lbi.DataContext as IconEntry : null;
    }

    // Returns the index in _entries closest to the given Y position in the ListBox.
    private int IndexAt(Point pt)
    {
        var hit = VisualTreeHelper.HitTest(IconList, pt);
        var dep = hit?.VisualHit as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is ListBoxItem lbi && lbi.DataContext is IconEntry e)
            return _entries.IndexOf(e);
        return _entries.Count > 0 ? _entries.Count - 1 : -1;
    }

}
