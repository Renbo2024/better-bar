using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BetterBarApp.Models;
using BetterBarApp.Services;
using BetterBarApp.Windows;

namespace BetterBarApp.Controls;

public partial class LauncherControl : UserControl
{
    private readonly LauncherItem _item;
    private readonly List<string> _orderedPaths = [];
    private readonly Dictionary<string, Button> _buttons = [];

    private string? _dragSource;
    private Point   _dragStart;

    // Layout state shared between Rebuild and the drag handlers.
    private double _cellSize = 1;   // icon + spacing; the highlightable container size
    private int    _numCols  = 1;
    private int    _rows     = 1;

    // Loaded can deliver the wrong height (window still at its default size before
    // RegisterAppBar resizes it). SizeChanged catches the settled height.
    private double _lastBuiltHeight = -1;

    public LauncherControl(LauncherItem item)
    {
        _item = item;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += OnSizeChanged;
        Rebuild();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged) return;                       // we set Width ourselves
        if (Math.Abs(ActualHeight - _lastBuiltHeight) < 1.0) return;
        Rebuild();
    }

    public void Rebuild()
    {
        if (ActualHeight <= 0) return; // not laid out yet; OnSizeChanged fires when ready

        _lastBuiltHeight = ActualHeight;

        _rows           = Math.Max(1, _item.Rows);
        double spacing  = _item.IconSpacing;
        double panelH   = ActualHeight;
        const double minIcon = 8.0;

        // Cell = icon + spacing (the full highlightable container, tiled edge-to-edge).
        // rows cells must fit the available height, so clamp the margin to guarantee
        // a usable icon size no matter what the user configured.
        double minCell  = minIcon + spacing;
        double maxMargin = Math.Max(0, (panelH - _rows * minCell) / 2.0);
        double margin    = Math.Clamp(_item.IconMargin, 0, maxMargin);

        double availH   = panelH - 2 * margin;
        _cellSize       = Math.Floor(availH / _rows);
        double iconSize = Math.Max(minIcon, _cellSize - spacing);

        OuterBorder.Padding = new Thickness(margin);

        // Resolve ordered file list: stored order first, then alphabetical remainder.
        _orderedPaths.Clear();
        if (Directory.Exists(_item.SourceDirectory))
        {
            // Skip companion .bbr files and anything hidden via its sidecar.
            var allFiles  = Directory.GetFiles(_item.SourceDirectory)
                .Where(p => !IconSidecar.IsSidecar(p) && !IconSidecar.IsHidden(p))
                .ToList();
            var inOrder   = _item.IconOrder.Where(allFiles.Contains).ToList();
            var remainder = allFiles.Except(inOrder).OrderBy(Path.GetFileName).ToList();
            _orderedPaths.AddRange(inOrder);
            _orderedPaths.AddRange(remainder);
        }

        int numFiles = _orderedPaths.Count;

        IconPanel.Children.Clear();
        _buttons.Clear();

        if (numFiles == 0)
        {
            Width            = 2 * margin;
            IconPanel.Width  = 0;
            IconPanel.Height = 0;
            BarItemsPanel.InvalidateForChild(this);
            return;
        }

        // Row-major: fill row 0 left→right, then row 1, etc.
        _numCols = (int)Math.Ceiling((double)numFiles / _rows);

        double innerW = _numCols * _cellSize;
        double innerH = _rows    * _cellSize;

        Width            = 2 * margin + innerW;
        IconPanel.Width  = innerW;
        IconPanel.Height = innerH;

        var dpi        = VisualTreeHelper.GetDpi(this);
        int sizePixels = Math.Max(16, (int)Math.Round(iconSize * dpi.DpiScaleX));

        foreach (var path in _orderedPaths)
        {
            var btn = BuildIconButton(path, _cellSize, iconSize);
            _buttons[path] = btn;
            IconPanel.Children.Add(btn);
            LoadIcon(btn, path, sizePixels);
        }

        LayoutButtons();
        // Our Width changed in place; make the owning panel re-discover it (see BarItemsPanel.InvalidateForChild).
        BarItemsPanel.InvalidateForChild(this);
    }

    // Positions every button at its row-major grid slot based on _orderedPaths.
    private void LayoutButtons()
    {
        for (int i = 0; i < _orderedPaths.Count; i++)
        {
            if (!_buttons.TryGetValue(_orderedPaths[i], out var btn)) continue;
            int row = i / _numCols;
            int col = i % _numCols;
            Canvas.SetLeft(btn, col * _cellSize);
            Canvas.SetTop(btn,  row * _cellSize);
        }
    }

    private Button BuildIconButton(string path, double cellSize, double iconSize)
    {
        // Image is the actual icon; it is centered inside the larger cell-sized button
        // so the spacing becomes part of the (highlightable) button, not dead gaps.
        var img = new Image
        {
            Width               = iconSize,
            Height              = iconSize,
            Stretch             = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        var btn = new Button
        {
            // Container is the full cell — the (fading) hover highlight covers icon + spacing.
            Width     = cellSize,
            Height    = cellSize,
            Content   = img,
            Tag       = path,
        };
        if (TryFindResource("BarIconButton") is Style style) btn.Style = style;

        // Optional cursor-trailing hover tip (the file name, without its extension).
        HoverTip.Attach(btn, () => Path.GetFileNameWithoutExtension(path), _item.ShowTooltips, _item.TooltipDelayMs);

        btn.Click += (_, _) => LaunchFile(path);   // reorder vs. launch is decided by the control's mouse handlers

        // Right-click → BetterBar's themed menu (matches the task-button menu).
        // WindowStyle=None panels don't fire ContextMenuService automatically, and
        // the panel's own right-click handler would otherwise swallow the event, so
        // open it manually and mark it handled.
        btn.MouseRightButtonUp += (_, e) =>
        {
            ShowLauncherMenu(path, btn);
            e.Handled = true;
        };

        return btn;
    }

    private static void LaunchFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    // ── Themed right-click menu (same look as the task-button menu): BetterBar items at the top,
    //    then a standard set of file actions, then "Hide". ──
    private void ShowLauncherMenu(string path, Button btn)
    {
        var menu = BarItemMenu.Create(this, btn);

        if (Window.GetWindow(this) is PanelWindow pw)
            menu.Items.Add(BarItemMenu.MakeConfigureItem(this, pw.Definition, _item));
        BarItemMenu.AddBetterBarCommands(this, menu);     // BetterBar's items at the TOP
        menu.Items.Add(BarItemMenu.MakeSeparator(this));

        BarItemMenu.AddCommand(this, menu, "Open",               () => LaunchFile(path));
        BarItemMenu.AddCommand(this, menu, "Open file location", () => ShellFileActions.OpenLocation(path));
        BarItemMenu.AddCommand(this, menu, "Copy",               () => ShellFileActions.CopyToClipboard(path));
        BarItemMenu.AddCommand(this, menu, "Delete",             () => { if (ShellFileActions.Delete(path)) Rebuild(); });
        BarItemMenu.AddCommand(this, menu, "Rename",             () => RenameFile(path));
        BarItemMenu.AddCommand(this, menu, "Properties",         () => ShellFileActions.ShowProperties(path));

        menu.Items.Add(BarItemMenu.MakeSeparator(this));
        BarItemMenu.AddCommand(this, menu, "Hide", () => { IconSidecar.SetHide(path, true); Rebuild(); });

        menu.IsOpen = true;
    }

    // Renames the underlying file (and its companion .bbr), keeping its place in the custom order.
    private void RenameFile(string path)
    {
        var dlg = new StartMenuRenameDialog(Path.GetFileName(path), Window.GetWindow(this));
        if (dlg.ShowDialog() != true) return;

        var newName = dlg.ResultName?.Trim();
        if (string.IsNullOrEmpty(newName)) return;

        var dir     = Path.GetDirectoryName(path)!;
        var newPath = Path.Combine(dir, newName);
        if (string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show("A file with that name already exists.", "Rename",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            File.Move(path, newPath);
            // Keep the icon/hide override with the file.
            var oldSide = IconSidecar.PathFor(path);
            if (File.Exists(oldSide)) File.Move(oldSide, IconSidecar.PathFor(newPath));
            // Preserve the custom order: swap the path in place.
            int idx = _item.IconOrder.IndexOf(path);
            if (idx >= 0) _item.IconOrder[idx] = newPath;
            SettingsService.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Rename failed:\n" + ex.Message, "Rename",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Rebuild();
    }

    // Shell icon APIs use COM internally and must run on an STA thread. Loads are funnelled through
    // the shared IconLoader (one serialized STA worker + retry) rather than a thread per icon, which
    // at startup overwhelmed the shell and left icons blank — a "black spot" on the bar.
    private void LoadIcon(Button btn, string path, int sizePixels)
    {
        IconLoader.Queue(path, sizePixels, Dispatcher, icon =>
        {
            if (btn.Content is Image img) img.Source = icon;
        });
    }

    #region Drag-and-drop reorder (mouse capture)

    // The panel window registers a NATIVE OLE IDropTarget on its HWND (for external file drags onto
    // start buttons), which replaces WPF's managed drop target — so WPF DragOver/Drop no longer fire
    // here. Reorder is therefore driven directly by the mouse (press → drag past threshold → capture →
    // live re-slot → release), independent of WPF/OLE drag-drop.
    private bool _reordering;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStart  = e.GetPosition(this);
        _dragSource = FindButtonPath(e.OriginalSource as DependencyObject);
        _reordering = false;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;

        if (!_reordering)
        {
            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _reordering = true;
            CaptureMouse();   // takes capture from the Button, which cancels its pending Click
        }

        // Live reorder: drop the dragged icon into the slot under the cursor and re-lay out.
        if (_cellSize <= 0) return;
        var pos    = e.GetPosition(IconPanel);
        int col    = Math.Clamp((int)(pos.X / _cellSize), 0, Math.Max(0, _numCols - 1));
        int row    = Math.Clamp((int)(pos.Y / _cellSize), 0, Math.Max(0, _rows - 1));
        int target = Math.Clamp(row * _numCols + col, 0, _orderedPaths.Count - 1);
        int cur    = _orderedPaths.IndexOf(_dragSource);
        if (cur < 0 || cur == target) return;

        _orderedPaths.RemoveAt(cur);
        _orderedPaths.Insert(Math.Clamp(target, 0, _orderedPaths.Count), _dragSource);
        LayoutButtons();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_reordering)
        {
            ReleaseMouseCapture();
            PersistOrder();
            e.Handled = true;   // a completed reorder must not also launch the icon
        }
        _reordering = false;
        _dragSource = null;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    // Walk up from the hit element to the icon Button and return its file path (its Tag), or null.
    private static string? FindButtonPath(DependencyObject? node)
    {
        while (node != null && node is not Button) node = VisualTreeHelper.GetParent(node);
        return (node as Button)?.Tag as string;
    }

    private void PersistOrder()
    {
        _item.IconOrder.Clear();
        foreach (var p in _orderedPaths) _item.IconOrder.Add(p);
        // Persist to disk immediately so the custom order survives a restart.
        SettingsService.Save();
    }

    #endregion
}
